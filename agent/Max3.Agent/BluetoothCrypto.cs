using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Max3.Agent;

internal static class BluetoothCrypto {
    public static byte[] Mac(byte[] key,string text)=>HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(text));
    public static string Proof(byte[] key,string role,string context)=>Convert.ToBase64String(Mac(key,role+"|"+context));
    public static async Task WritePlain(Stream stream,object value,CancellationToken ct){
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value);if(bytes.Length>4096)throw new InvalidDataException();
        var packet=new byte[4+bytes.Length];BinaryPrimitives.WriteInt32BigEndian(packet,bytes.Length);bytes.CopyTo(packet,4);await stream.WriteAsync(packet,ct);
    }
    public static async Task<JsonDocument> ReadPlain(Stream stream,CancellationToken ct){
        var h=new byte[4];await stream.ReadExactlyAsync(h,ct);var n=BinaryPrimitives.ReadInt32BigEndian(h);
        if(n is <2 or >4096)throw new InvalidDataException("Invalid Bluetooth handshake size");
        var bytes=new byte[n];await stream.ReadExactlyAsync(bytes,ct);return JsonDocument.Parse(bytes);
    }
}

/// <summary>Mutually authenticated RFCOMM framing: separate directional keys, strict counters, AES-256-GCM and bounded GZip.</summary>
internal sealed class BluetoothSecureChannel(Stream stream,byte[] outgoing,byte[] incoming) : IDisposable {
    private readonly SemaphoreSlim sendLock=new(1,1);
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    private volatile bool faulted;
    private ulong sent,received;
    public async Task<int> Write(object value,CancellationToken ct){
        if(faulted)throw new IOException("Bluetooth channel faulted");
        var raw=JsonSerializer.SerializeToUtf8Bytes(value,JsonOptions);
        if(raw.Length>262144)throw new InvalidDataException("Bluetooth payload too large");
        using var compressed=new MemoryStream();using(var gzip=new GZipStream(compressed,CompressionLevel.Fastest,true))gzip.Write(raw);
        var plain=compressed.ToArray();
        await sendLock.WaitAsync(ct);
        try {
            if(faulted)throw new IOException("Bluetooth channel faulted");
            var counter=++sent;var nonce=new byte[12];BinaryPrimitives.WriteUInt32BigEndian(nonce,0x4D415833);BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4),counter);
            var packet=new byte[4+8+plain.Length+16];BinaryPrimitives.WriteInt32BigEndian(packet,packet.Length-4);BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(4),counter);
            using(var aes=new AesGcm(outgoing,16))aes.Encrypt(nonce,plain,packet.AsSpan(12,plain.Length),packet.AsSpan(12+plain.Length,16),packet.AsSpan(4,8));
            // A single RFCOMM write avoids separate short header/data packets and their latency.
            await stream.WriteAsync(packet,ct);await stream.FlushAsync(ct);
            return packet.Length;
        }catch{faulted=true;throw;}finally{sendLock.Release();}
    }
    public async Task<JsonDocument> Read(CancellationToken ct){
        var h=new byte[4];await stream.ReadExactlyAsync(h,ct);var n=BinaryPrimitives.ReadInt32BigEndian(h);
        if(n is <24 or >262144)throw new InvalidDataException("Invalid Bluetooth frame size");
        var packet=new byte[n];await stream.ReadExactlyAsync(packet,ct);var counter=BinaryPrimitives.ReadUInt64BigEndian(packet);
        if(counter!=received+1)throw new CryptographicException("Bluetooth replay/sequence mismatch");
        var nonce=new byte[12];BinaryPrimitives.WriteUInt32BigEndian(nonce,0x4D415833);BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4),counter);
        var plain=new byte[n-24];using(var aes=new AesGcm(incoming,16))aes.Decrypt(nonce,packet.AsSpan(8,n-24),packet.AsSpan(n-16),plain,packet.AsSpan(0,8));
        received=counter;
        using var zipped=new GZipStream(new MemoryStream(plain),CompressionMode.Decompress);using var raw=new MemoryStream();
        var buffer=new byte[4096];int read;while((read=await zipped.ReadAsync(buffer,ct))>0){if(raw.Length+read>262144)throw new InvalidDataException("Inflated Bluetooth frame too large");raw.Write(buffer,0,read);}
        return JsonDocument.Parse(raw.ToArray());
    }
    public void Dispose(){CryptographicOperations.ZeroMemory(outgoing);CryptographicOperations.ZeroMemory(incoming);sendLock.Dispose();}
}
