using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Max3.Agent;

internal static class LocalAdminTests {
    public static async Task Run(Action<bool,string> check) {
        if(OperatingSystem.IsWindows()){
            var directory=Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),"DeskCompanionFixture-"+Guid.NewGuid().ToString("N")));
            var file=Path.Combine(directory.FullName,LocalAdmin.TokenFileName);
            try {
                var first=LocalAdmin.LoadOrCreate(directory.FullName);
                check(first.Length==64&&LocalAdmin.LoadOrCreate(directory.FullName)==first,"local administration credential persists without rotation on a healthy restart");
                File.WriteAllText(file,"");var recovered=LocalAdmin.LoadOrCreate(directory.FullName);
                check(recovered.Length==64&&recovered!=first&&LocalAdmin.LoadOrCreate(directory.FullName)==recovered,"truncated management credential recovers without changing phone credentials");
                var acl=System.IO.FileSystemAclExtensions.GetAccessControl(new FileInfo(file));
                var owner=System.Security.Principal.WindowsIdentity.GetCurrent().User!;
                check(acl.AreAccessRulesProtected&&acl.GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier)).OfType<System.Security.AccessControl.FileSystemAccessRule>().All(r=>r.IdentityReference==owner||r.IdentityReference.Value=="S-1-5-18"),"management credential file is restricted to current user and SYSTEM");
            }finally{File.Delete(file);directory.Delete();}
        }
        // Isolated synthetic credentials; never reads the running agent's data directory.
        var admin=new string('A',64);var phone=new string('B',64);
        var port=0;var starts=0;
        var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app=builder.Build();
        app.Use(async(c,next)=>{if(!LocalAdmin.Allows(c,port,admin)){c.Response.StatusCode=403;return;}await next();});
        app.MapGet("/",()=>"Static administration page; use the local open script");
        app.MapPost("/api/pair/start",()=>{starts++;return "synthetic pair code";});
        app.MapGet("/api/state",()=>"synthetic state");
        await app.StartAsync();port=new Uri(app.Urls.Single()).Port;
        using var client=new HttpClient(){BaseAddress=new Uri($"http://127.0.0.1:{port}")};
        async Task<HttpStatusCode> Call(string path,string? credential=null,string? origin=null,string? host=null) {
            using var request=new HttpRequestMessage(path=="/"||path.StartsWith("/api/state")?HttpMethod.Get:HttpMethod.Post,path);
            if(credential is not null)request.Headers.Authorization=new("Bearer",credential);
            if(origin is not null)request.Headers.Add("Origin",origin);
            if(host is not null)request.Headers.Host=host;
            using var response=await client.SendAsync(request);return response.StatusCode;
        }
        check(await Call("/")==HttpStatusCode.OK,"anonymous loopback can load only the static administration page");
        check(await Call("/api/pair/start")==HttpStatusCode.Forbidden && starts==0,"another local process cannot issue a pair code without admin authentication");
        check(await Call("/api/state")==HttpStatusCode.Forbidden,"loopback metrics require local administration credential");
        check(await Call("/api/pair/start",phone)==HttpStatusCode.Forbidden,"phone credential is not local administration authentication");
        check(await Call("/api/pair/start?admin="+admin)==HttpStatusCode.Forbidden,"administration token in a query is never accepted");
        check(await Call("/api/pair/start",admin)==HttpStatusCode.OK && starts==1,"correct admin header permits pair-code creation");
        check(await Call("/api/state",admin)==HttpStatusCode.OK,"correct admin header permits local diagnostics");
        check(await Call("/api/pair/start",admin,"https://untrusted.example")==HttpStatusCode.Forbidden && starts==1,"foreign browser origin remains rejected with a valid admin credential");
        check(await Call("/api/pair/start",admin,host:"localhost:"+port)==HttpStatusCode.Forbidden && starts==1,"unexpected Host remains rejected with a valid admin credential");
        await app.StopAsync();
    }
}
