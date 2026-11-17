using Max3.Agent;
using PhoneBox.Agent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
var count=0;
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);count++;}
var r=new NetworkRate();
Check(!r.Sample(1000,1000,100,true).Valid,"first sample is baseline");
var s=r.Sample(3000,2000,300,true);
Check(s.Valid && s.Down==10000 && s.Up==5000,"actual 200ms delta, bytes/s");
Check(!r.Sample(20,30,400,true).Valid,"counter reset suppressed");
Check(r.Sample(120,230,500,true).Down==1000,"baseline renewed after reset");
Check(!r.Sample(10000,10000,600,false).Valid,"disconnected values invalid");
Check(!r.Sample(20000,20000,700,true).Valid,"reconnect begins baseline");
Check(r.Sample(20100,20200,800,true).Down==1000,"reconnect cannot spike");
Check(!r.Sample(999999,999999,8000,true).Valid,"sleep gap invalidates rate");
Check(r.Sample(1000099,1000199,8100,true).Down==1000,"resume rebases counters");
Check(!r.Sample(1000199,1000299,8100,true).Valid,"zero elapsed time invalid");
var max=new NetworkRate();max.Sample(ulong.MaxValue-1000,ulong.MaxValue-1000,0,true);
Check(max.Sample(ulong.MaxValue-500,ulong.MaxValue-600,100,true).Down==5000,"large 64-bit counters without precision cancellation");
Check(Collector.Kind(71,true,"Wireless")=="wifi","hardware wifi classified");
Check(Collector.Kind(6,true,"Ethernet")=="ethernet","hardware ethernet classified");
Check(Collector.Kind(6,false,"Tailscale")=="vpn","tunnel excluded from physical totals");
Check(Collector.Kind(6,false,"Hyper-V")=="virtual","virtual ethernet excluded");
var codes=new PairingCodes();var p=codes.Start(1000);
Check(!codes.Claim("invalid",1001),"wrong code rejected");
Check(codes.Claim(p.Code,1002),"correct single-use code accepted");
Check(!codes.Claim(p.Code,1003),"replayed code rejected");
p=codes.Start(2000);Check(!codes.Claim(p.Code,2000+PairingCodes.LifetimeMs+1),"expired code rejected");
p=codes.Start(3000);for(int i=0;i<PairingCodes.MaxFailures;i++)codes.Claim("bad",3001);
Check(!codes.Claim(p.Code,3002),"failed attempt budget invalidates code");
Check(CodexEvents.ToolState("apply_patch")=="coding","coding needs actual tool name");
Check(CodexEvents.ToolState(null)=="working","no fabricated thinking state");
DesktopStateTests.Run(Check);
LatestWindowTests.Run(Check);
TelemetryTests.Run(Check);
HistoryResumeTests.Run(Check);
var legacyId=CodexEvents.PseudonymizeLegacy(new("working","confirmed","codex",1,"desktop:old-hash:private-item-id:working","metadata"));
Check(legacyId.EventId!.Length==32&&!legacyId.EventId.Contains("private-item-id"),"old task journal replay cannot expose original desktop item identifiers");
await LocalAdminTests.Run(Check);
var cleanupCalls=0;var cleanupFailures=0;
using(var owned=new DisposeOnce<ProbeCleanup>(new ProbeCleanup(()=>Interlocked.Increment(ref cleanupCalls)),_=>Interlocked.Increment(ref cleanupFailures))){
    using var cancellation=new CancellationTokenSource();using var a=cancellation.Token.Register(owned.Dispose);using var b=cancellation.Token.Register(owned.Dispose);
    Parallel.Invoke(()=>cancellation.Cancel(),()=>Parallel.For(0,1000,_=>owned.Dispose()));
}
Check(cleanupCalls==1,"concurrent cancellation callbacks and scope cleanup dispose native client once");
Check(cleanupFailures==1,"native cleanup exception is observed without escaping cancellation timer");
var diagnosticCalls=0;
using(var owned=new DisposeOnce<ProbeCleanup>(new ProbeCleanup(()=>{}),_=>{diagnosticCalls++;throw new InvalidOperationException("Diagnostic provider failure");})){
    using var cancellation=new CancellationTokenSource();using var callback=cancellation.Token.Register(owned.Dispose);
    cancellation.Cancel();owned.Dispose();
}
Check(diagnosticCalls==1,"diagnostic callback failure cannot escape native cleanup cancellation");
var txKey=System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);var rxKey=System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
byte[] packet;
using(var wire=new MemoryStream()){
    using var sender=new BluetoothSecureChannel(wire,txKey.ToArray(),rxKey.ToArray());
    await sender.Write(new {type="ping",id=17},CancellationToken.None);packet=wire.ToArray();
}
using(var wire=new MemoryStream(packet)){
    using var receiver=new BluetoothSecureChannel(wire,rxKey.ToArray(),txKey.ToArray());
    using var message=await receiver.Read(CancellationToken.None);
    Check(message.RootElement.GetProperty("id").GetInt32()==17,"Bluetooth directional AES-GCM/GZip frame round trip");
}
using(var wire=new MemoryStream(packet.Concat(packet).ToArray())){
    using var receiver=new BluetoothSecureChannel(wire,rxKey.ToArray(),txKey.ToArray());
    using var first=await receiver.Read(CancellationToken.None);var rejected=false;
    try{using var replay=await receiver.Read(CancellationToken.None);}catch(System.Security.Cryptography.CryptographicException){rejected=true;}
    Check(rejected,"Bluetooth repeated counter rejected");
}
var corrupted=packet.ToArray();corrupted[^1]^=1;
using(var wire=new MemoryStream(corrupted)){
    using var receiver=new BluetoothSecureChannel(wire,rxKey.ToArray(),txKey.ToArray());var rejected=false;
    try{using var altered=await receiver.Read(CancellationToken.None);}catch(System.Security.Cryptography.CryptographicException){rejected=true;}
    Check(rejected,"Bluetooth modified ciphertext rejected");
}
using(var wire=new MemoryStream(packet)){
    using var receiver=new BluetoothSecureChannel(wire,txKey.ToArray(),rxKey.ToArray());var rejected=false;
    try{using var wrongDirection=await receiver.Read(CancellationToken.None);}catch(System.Security.Cryptography.CryptographicException){rejected=true;}
    Check(rejected,"Bluetooth opposite direction key cannot authenticate");
}
using(var wire=new MemoryStream(new byte[]{0,4,0,1})){
    using var receiver=new BluetoothSecureChannel(wire,rxKey.ToArray(),txKey.ToArray());var rejected=false;
    try{using var large=await receiver.Read(CancellationToken.None);}catch(InvalidDataException){rejected=true;}
    Check(rejected,"Bluetooth oversized frame rejected before allocation");
}
Check(LanEndpoints.IsPrivate("172.20.10.2") && !LanEndpoints.IsPrivate("198.18.0.1") && !LanEndpoints.IsPrivate("0.0.0.0") && !LanEndpoints.IsPrivate("8.8.8.8"),"listeners accept RFC1918 only, excluding wildcard/public/VPN benchmark ranges");
// Exercise Kestrel's real reload path without toggling Windows adapters or changing their addresses.
var address=Lan.Addresses().FirstOrDefault(LanEndpoints.IsPrivate);
if(address is not null){
    static int FreePort(){var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();var p=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return p;}
    var admin=FreePort();var tls=FreePort();while(tls==admin)tls=FreePort();
    var endpoints=new LanEndpoints(admin,tls,null);endpoints.Initialize();endpoints.Update([]);
    using var key=System.Security.Cryptography.ECDsa.Create();
    var request=new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Max3 local test",key,System.Security.Cryptography.HashAlgorithmName.SHA256);
    using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddMinutes(10));
    var testBuilder=WebApplication.CreateBuilder();testBuilder.Logging.ClearProviders();
    testBuilder.WebHost.ConfigureKestrel(k=>{k.ConfigureHttpsDefaults(o=>o.ServerCertificate=cert);k.Configure(endpoints.Configuration,true);});
    await using var testHost=testBuilder.Build();testHost.MapGet("/",()=>"ready");await testHost.StartAsync();
    async Task<bool> Connected(){using var socket=new System.Net.Sockets.TcpClient();try{await socket.ConnectAsync(address,tls).WaitAsync(TimeSpan.FromMilliseconds(500));return true;}catch(System.Net.Sockets.SocketException){return false;}catch(TimeoutException){return false;}}
    async Task<bool> WaitFor(bool expected){var watch=System.Diagnostics.Stopwatch.StartNew();do{if(await Connected()==expected)return true;await Task.Delay(100);}while(watch.Elapsed.TotalSeconds<8);return false;}
    Check(!await Connected(),"no LAN listener before private network becomes available");
    endpoints.Update([address]);Check(await WaitFor(true),"Kestrel reload binds a newly available private address");
    endpoints.Update([]);Check(await WaitFor(false),"Kestrel reload closes a removed private address");
    using var http=new HttpClient();Check(await http.GetStringAsync($"http://127.0.0.1:{admin}/")=="ready","loopback administration survives LAN endpoint reload");
    await testHost.StopAsync();
}else Console.WriteLine("SKIP: dynamic listener test requires an existing RFC1918 interface");
Console.WriteLine($"{count} checks passed");
if(args.Length==2 && args[0]=="--network-data") {
    var dir=Path.GetFullPath(args[1]);
    using var cert=System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(dir,"lan-cert.pfx"),null);
    var pin=Lan.Fingerprint(cert);
    using var handler=new HttpClientHandler { ServerCertificateCustomValidationCallback=(_,c,_,_)=>c is not null && Lan.Fingerprint(c)==pin && c.NotAfter.ToUniversalTime()>DateTime.UtcNow };
    using var client=new HttpClient(handler){BaseAddress=new Uri("https://127.0.0.1:18766"),Timeout=TimeSpan.FromSeconds(3)};
    Check((int)(await client.GetAsync("/api/state")).StatusCode==403,"TLS pinned / unauthenticated metrics rejected");
    client.DefaultRequestHeaders.Authorization=new("Bearer","wrong");
    Check((int)(await client.GetAsync("/api/state")).StatusCode==403,"wrong phone key rejected");
    client.DefaultRequestHeaders.Authorization=new("Bearer",File.ReadAllText(Path.Combine(dir,"phone-token.txt")).Trim());
    Check((int)(await client.GetAsync("/api/state")).StatusCode==200,"pinned TLS plus correct key accepted");
    Check((int)(await client.GetAsync("/api/run/example")).StatusCode==403,"remote commands absent from companion LAN surface");
    Check((int)(await client.GetAsync("/")).StatusCode==403,"settings and QR UI remain loopback-only");
    client.DefaultRequestHeaders.Add("Origin","https://untrusted.example");
    Check((int)(await client.GetAsync("/api/state")).StatusCode==403,"browser origin rejected even with phone key");
    using var wrongHandler=new HttpClientHandler { ServerCertificateCustomValidationCallback=(_,c,_,_)=>c is not null && Lan.Fingerprint(c)==new string('0',64) };
    using var wrongClient=new HttpClient(wrongHandler){Timeout=TimeSpan.FromSeconds(3)};
    var rejected=false;try{await wrongClient.GetAsync("https://127.0.0.1:18766/api/state");}catch(HttpRequestException){rejected=true;}
    Check(rejected,"wrong TLS fingerprint fails handshake");
    Console.WriteLine($"{count} checks passed including live TLS/auth checks");
}
