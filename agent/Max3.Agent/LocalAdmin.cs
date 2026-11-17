using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using PhoneBox.Agent;

namespace Max3.Agent;

/// <summary>Loopback is a network boundary, not a Windows-user authentication boundary.</summary>
internal static class LocalAdmin {
    internal const string TokenFileName="admin-token.txt";
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static string LoadOrCreate(string directory) {
        var file=new FileInfo(Path.Combine(directory,TokenFileName));
        var acl=new FileSecurity();acl.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{WindowsIdentity.GetCurrent().User!,new SecurityIdentifier("S-1-5-18")})
            acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,AccessControlType.Allow));
        var token="";
        if(file.Exists){FileSystemAclExtensions.SetAccessControl(file,acl);token=File.ReadAllText(file.FullName).Trim();}
        if(token.Length!=64 || !token.All(Uri.IsHexDigit)){
            // A damaged admin credential can safely rotate without changing phone pairing or the pinned certificate.
            token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var temporary=new FileInfo(file.FullName+"."+Guid.NewGuid().ToString("N")+".tmp");
            try {
                using(var stream=FileSystemAclExtensions.Create(temporary,FileMode.CreateNew,FileSystemRights.Write,FileShare.None,4096,FileOptions.None,acl)){
                    stream.Write(System.Text.Encoding.UTF8.GetBytes(token));stream.Flush(flushToDisk:true);
                }
                File.Move(temporary.FullName,file.FullName,overwrite:true);
            }finally{temporary.Delete();}
        }
        return token;
    }
    public static bool Allows(HttpContext context,int port,string token) {
        var request=context.Request;
        var origin=request.Headers.Origin.ToString();
        if(context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)
            || context.Connection.LocalPort!=port || request.Host.Value!=$"127.0.0.1:{port}"
            || origin.Length!=0 && origin!=$"http://127.0.0.1:{port}")return false;
        // This page is static and contains no credentials or machine data. APIs always require the header.
        if(HttpMethods.IsGet(request.Method) && request.Path=="/")return true;
        return Lan.Matches(request.Headers.Authorization,token);
    }
}
