using System.Net;
using PhoneBox.Agent;

namespace Max3.Agent;

/// <summary>Reloads only explicit RFC1918 listeners when DHCP becomes ready or changes after resume.</summary>
public sealed class LanEndpoints(int adminPort,int tlsPort,string? fixedHost) : BackgroundService {
    private readonly EndpointConfiguration provider=new();
    private string[] hosts=[];
    public string[] Hosts=>Volatile.Read(ref hosts);
    public IConfiguration Configuration {get;private set;}=null!;
    public static bool IsPrivate(string host) {
        if(!IPAddress.TryParse(host,out var ip))return false;
        var b=ip.GetAddressBytes();
        return b.Length==4 && (b[0]==10 || b[0]==172 && b[1] is >=16 and <=31 || b[0]==192 && b[1]==168);
    }
    public void Initialize(){
        var current=Discover();
        if(fixedHost is not null && (!IsPrivate(fixedHost) || !current.Contains(fixedHost)))throw new ArgumentException("--bind must be an existing private LAN IPv4 address");
        Configuration=new ConfigurationBuilder().Add(provider).Build();
        Update(fixedHost is null?current:[fixedHost]);
    }
    private static string[] Discover()=>Lan.Addresses().Where(IsPrivate).Distinct().Order().ToArray();
    internal void Update(string[] current){
        if(current.Any(h=>!IsPrivate(h)))throw new ArgumentException("Only explicit private LAN addresses may be bound");
        var values=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase) {
            ["Endpoints:Admin:Url"]=$"http://127.0.0.1:{adminPort}",
            ["Endpoints:LocalTls:Url"]=$"https://127.0.0.1:{tlsPort}"
        };
        foreach(var address in current)values[$"Endpoints:Lan_{address.Replace('.','_')}:Url"]=$"https://{address}:{tlsPort}";
        Volatile.Write(ref hosts,current);
        provider.Replace(values);
    }
    protected override async Task ExecuteAsync(CancellationToken ct){
        if(fixedHost is not null)return;
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        try{while(await timer.WaitForNextTickAsync(ct)){
            try{var current=Discover();if(!current.SequenceEqual(Hosts))Update(current);}
            catch(System.Net.NetworkInformation.NetworkInformationException){ /* transient adapter enumeration; retry */ }
        }}catch(OperationCanceledException)when(ct.IsCancellationRequested){}
    }
    private sealed class EndpointConfiguration : ConfigurationProvider,IConfigurationSource {
        public IConfigurationProvider Build(IConfigurationBuilder builder)=>this;
        public void Replace(IDictionary<string,string?> values){Data=values;OnReload();}
    }
}
