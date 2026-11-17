using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Max3.Agent;
using PhoneBox.Agent;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows 11 required");
if(IntPtr.Size!=8)throw new PlatformNotSupportedException("Windows x64 required");
// Explicit, read-only Bluetooth probe: no discovery/pairing or device changes during normal monitoring.
if(args.Contains("--bluetooth-probe")) {
    using var probe=new InTheHand.Net.Sockets.BluetoothClient();
    var radio=InTheHand.Net.Bluetooth.BluetoothRadio.Default;
    var phones=probe.PairedDevices.ToArray();
    var observations=new List<object>();
    foreach(var phone in phones){
        phone.Refresh();
        try{var services=(await Task.Run(()=>phone.GetRfcommServicesAsync(false)).WaitAsync(TimeSpan.FromSeconds(10))).ToArray();observations.Add(new {phone.DeviceName,Address=phone.DeviceAddress.ToString(),phone.Authenticated,phone.Connected,CompanionService=services.Contains(BluetoothTransport.ServiceId),Services=services});}
        catch(Exception e){observations.Add(new {phone.DeviceName,phone.Authenticated,phone.Connected,Error=e.GetType().Name+": "+e.Message});}
    }
    Console.WriteLine(JsonSerializer.Serialize(new {Radio=radio.Name,Mode=radio.Mode.ToString(),Phones=observations},new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return;
}
string? Option(string name) => args.SkipWhile(x => x != name).Skip(1).FirstOrDefault();
int Number(string name,int fallback,int min,int max) {
    var raw=Option(name);if(raw is null)return fallback;
    if(!int.TryParse(raw,out var n) || n<min || n>max)throw new ArgumentException($"{name} must be an integer in [{min}, {max}]");
    return n;
}
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.AddSingleton<Collector>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Collector>());
builder.Services.AddSingleton<CodexEvents>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CodexEvents>());
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode=429;
    o.AddPolicy("pair",ctx=>RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _=>new FixedWindowRateLimiterOptions {PermitLimit=5,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
});

// Capture mode does not listen on any network port or create pairing keys.
if (Option("--capture") is { } secondsText) {
    var seconds = Number("--capture",60,1,86400);
    using var host = builder.Build();
    var collector = host.Services.GetRequiredService<Collector>();
    await collector.StartAsync(CancellationToken.None);
    var path = Path.GetFullPath(Option("--out") ?? "samples.jsonl");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var file = new StreamWriter(path);
    using var process = Process.GetCurrentProcess();
    var ready=Stopwatch.StartNew();
    while(collector.Latest is null && ready.Elapsed.TotalSeconds<5)await Task.Delay(25);
    if(collector.Latest is null)throw new InvalidOperationException("No native snapshot within five seconds; capture failed");
    var warmup=Number("--warmup",0,0,60);
    if(warmup>0)await Task.Delay(TimeSpan.FromSeconds(warmup));
    var cpuStart = process.TotalProcessorTime.TotalMilliseconds;
    var wall = Stopwatch.StartNew();
    long seq = -1;long recorded=0;
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
    while (wall.Elapsed.TotalSeconds < seconds && await timer.WaitForNextTickAsync()) {
        var s = collector.Latest;
        if (s is null || s.Sequence == seq) continue;
        seq = s.Sequence;
        recorded++;
        await file.WriteLineAsync(JsonSerializer.Serialize(s, json));
    }
    await collector.StopAsync(CancellationToken.None);
    process.Refresh();
    if(recorded==0)throw new InvalidOperationException("Capture recorded zero snapshots");
    var summary = new { durationMs = wall.Elapsed.TotalMilliseconds, warmupSeconds=warmup, recordedSnapshots=recorded, logicalProcessors = Environment.ProcessorCount,
        agentCpuPercent = (process.TotalProcessorTime.TotalMilliseconds - cpuStart) / wall.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
        workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64, output = path };
    Console.WriteLine(JsonSerializer.Serialize(summary, json));
    return;
}

var dataDir = Path.GetFullPath(Option("--data-dir") ?? Path.Combine(AppContext.BaseDirectory, "data"));
Directory.CreateDirectory(dataDir);
// Restrict generated credentials to the current Windows user and SYSTEM. No system policy changes.
var acl = new System.Security.AccessControl.DirectorySecurity();
acl.SetAccessRuleProtection(true, false);
foreach (var sid in new[] { System.Security.Principal.WindowsIdentity.GetCurrent().User!, new System.Security.Principal.SecurityIdentifier("S-1-5-18") })
    acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(sid, System.Security.AccessControl.FileSystemRights.FullControl,
        System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
        System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
System.IO.FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(dataDir), acl);
foreach(var name in new[]{"phone-token.txt","lan-cert.pfx","task-events.jsonl",LocalAdmin.TokenFileName}) {
    var file=new FileInfo(Path.Combine(dataDir,name));
    if(!file.Exists)continue;
    var fileAcl=new System.Security.AccessControl.FileSecurity();fileAcl.SetAccessRuleProtection(true,false);
    foreach(var sid in new[]{System.Security.Principal.WindowsIdentity.GetCurrent().User!,new System.Security.Principal.SecurityIdentifier("S-1-5-18")})
        fileAcl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(sid,System.Security.AccessControl.FileSystemRights.FullControl,System.Security.AccessControl.AccessControlType.Allow));
    System.IO.FileSystemAclExtensions.SetAccessControl(file,fileAcl);
}
using var instanceLock = new FileStream(Path.Combine(dataDir,"agent.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
var adminToken=LocalAdmin.LoadOrCreate(dataDir);
var tokenFile = Path.Combine(dataDir, "phone-token.txt");
var token = File.Exists(tokenFile) ? File.ReadAllText(tokenFile).Trim() : Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
if (token.Length != 64) throw new InvalidDataException("Invalid stored token; restore backup or explicitly reset pairing");
File.WriteAllText(tokenFile, token);
var certFile=Path.Combine(dataDir,"lan-cert.pfx");
if(File.Exists(certFile)){using var validateStored=System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(certFile,null);}
using var cert = Lan.Certificate(certFile);
var fingerprint = Lan.Fingerprint(cert);
var codes = new PairingCodes();
var adminPort = Number("--port",18765,1,65535);
var tlsPort = Number("--tls-port",18766,1,65535);
if(adminPort==tlsPort)throw new ArgumentException("--port and --tls-port must differ");
var durationSeconds=Number("--duration",0,0,86400);
var endpoints=new LanEndpoints(adminPort,tlsPort,Option("--bind"));endpoints.Initialize();
builder.Services.AddSingleton(endpoints);
builder.Services.AddHostedService(sp=>sp.GetRequiredService<LanEndpoints>());
builder.Services.AddSingleton(sp=>new BluetoothTransport(sp.GetRequiredService<Collector>(),sp.GetRequiredService<CodexEvents>(),sp.GetRequiredService<ILogger<BluetoothTransport>>(),dataDir,token,fingerprint,endpoints,tlsPort));
builder.Services.AddHostedService(sp=>sp.GetRequiredService<BluetoothTransport>());
builder.WebHost.ConfigureKestrel(k => {
    k.Limits.MaxRequestBodySize = 1024;
    k.Limits.MaxConcurrentConnections = 16;
    k.ConfigureHttpsDefaults(o=>o.ServerCertificate=cert);
    k.Configure(endpoints.Configuration,reloadOnChange:true);
});
var app = builder.Build();
app.Use(async (ctx, next) => {
    var remote = ctx.Connection.RemoteIpAddress;
    var local = remote is not null && IPAddress.IsLoopback(remote);
    var path = ctx.Request.Path.Value;
    var origin = ctx.Request.Headers.Origin.ToString();
    if (ctx.Connection.LocalPort == adminPort) {
        if (!LocalAdmin.Allows(ctx,adminPort,adminToken)) { ctx.Response.StatusCode = 403; return; }
    } else {
        if (origin.Length != 0 || remote is null || !local && !LanEndpoints.IsPrivate(remote.ToString())) { ctx.Response.StatusCode = 403; return; }
        var claim = ctx.Request.Method == "POST" && path == "/api/pair/claim";
        var read = ctx.Request.Method == "GET" && path is "/api/state" or "/api/stream" or "/api/time";
        if (!claim && (!read || !Lan.Matches(ctx.Request.Headers.Authorization, token))) { ctx.Response.StatusCode = 403; return; }
    }
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseRateLimiter();
bool Admin(HttpContext c) => c.Connection.LocalPort == adminPort;
object PairReply() => new { pc = Environment.MachineName, fingerprint, port = tlsPort, hosts=endpoints.Hosts, token };
app.MapGet("/", (HttpContext c) => {
    if (!Admin(c)) return Results.StatusCode(403);
    using var s = typeof(Collector).Assembly.GetManifestResourceStream("Max3.Agent.pairing.html")!;
    using var r = new StreamReader(s);
    return Results.Content(r.ReadToEnd(), "text/html");
});
app.MapPost("/api/pair/start", (HttpContext c) => {
    if (!Admin(c)) return Results.StatusCode(403);
    var hosts=endpoints.Hosts;
    if (hosts.Length==0) return Results.Json(new {error="No private LAN IPv4 address. Connect Windows to Wi-Fi/Ethernet; the agent detects addresses automatically."},statusCode:409);
    var p = codes.Start(Environment.TickCount64);
    // Pasted/scanned in the application's own pairing UI; nothing is sent to an external QR service.
    var link = PairingCodes.Link(Environment.MachineName, fingerprint, tlsPort, hosts, p.Code)
        .Replace("https://ersinkoc.github.io/phonebox/pair/#", "deskcompanion://pair?");
    return Results.Json(new { link, check = PairingCodes.Check(fingerprint), svg = PairingCodes.Svg(link), expiresInSeconds = 300, hosts });
});
app.MapPost("/api/pair/claim", async (HttpContext c) => {
    var code = await PairingCodes.ReadClaimCodeAsync(c.Request);
    return codes.Claim(code, Environment.TickCount64, invalidateOnFailures:false) ? Results.Json(PairReply()) : Results.StatusCode(403);
}).RequireRateLimiting("pair");
app.MapGet("/api/time", () => new { unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), monotonicMs = Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency });
app.MapGet("/api/bluetooth/status",(HttpContext c,BluetoothTransport bt)=>Admin(c)?Results.Json(bt.Status()):Results.StatusCode(403));
app.MapPost("/api/bluetooth/select",async(HttpContext c,BluetoothTransport bt)=>{
    if(!Admin(c))return Results.StatusCode(403);
    using var body=await JsonDocument.ParseAsync(c.Request.Body);
    return bt.Select(body.RootElement.GetProperty("address").GetString() ?? "")?Results.Ok():Results.BadRequest();
});
object State(Snapshot? sample, CodexEvents ai, TaskEvent[]? history = null) => WireState.Create(sample,ai,history);
app.MapGet("/api/state", (Collector c, CodexEvents ai) => Results.Json(State(c.Latest, ai)));
app.MapGet("/api/stream", async (HttpContext ctx, Collector c, CodexEvents ai, CancellationToken ct) => {
    ctx.Response.ContentType = "text/event-stream";
    ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
    long sequence = -1;
    long eventCursor = 0;
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(25));
    try {
        while (await timer.WaitForNextTickAsync(ct)) {
            var sample = c.Latest;
            if (sample is null || sample.Sequence == sequence) continue;
            sequence = sample.Sequence;
            // Bound slow clients: no queue of historical frames can build up behind a disconnected phone.
            using var write = CancellationTokenSource.CreateLinkedTokenSource(ct);
            write.CancelAfter(2000);
            var history = ai.EventsAfter(eventCursor);
            await ctx.Response.WriteAsync("event: state\ndata: " + JsonSerializer.Serialize(State(sample, ai, history.Length>0?history:null), json) + "\n\n", write.Token);
            await ctx.Response.Body.FlushAsync(write.Token);
            if(history.Length>0)eventCursor=history[^1].Sequence;
        }
    } catch (OperationCanceledException) { } catch (IOException) { }
});
if (durationSeconds>0) app.Lifetime.ApplicationStarted.Register(() =>
    _ = Task.Delay(TimeSpan.FromSeconds(durationSeconds)).ContinueWith(_ => app.Lifetime.StopApplication()));
Console.WriteLine($"Desk Companion: http://127.0.0.1:{adminPort}/ ; private TLS {string.Join(",", endpoints.Hosts)}:{tlsPort}. No firewall changes.");
await app.RunAsync();
