using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Diagnostics;
using InTheHand.Net;
using InTheHand.Net.Sockets;

namespace Max3.Agent;

public sealed record BluetoothPeer(string Name,string Address);
public sealed record BluetoothStatus(bool Connected,string? Device,string Message,IReadOnlyList<BluetoothPeer> Paired,long SentFrames=0,long LastPhoneMessageAtUnixMs=0,long AcknowledgedSequence=0,double? AckRoundTripMs=null,bool FlowControl=false,string FlowMode="none",int InFlight=0,int PeakInFlight=0,long SentBytes=0,long TotalSentFrames=0,long TotalSentBytes=0,long Connections=0);

public sealed class BluetoothTransport(Collector collector,CodexEvents ai,ILogger<BluetoothTransport> log,string dir,string token,string fingerprint,LanEndpoints endpoints,int tlsPort) : BackgroundService {
    public static readonly Guid ServiceId=new("96fe3f30-25c4-4c20-b788-20dff0197b31");
    internal const string HistoryFeature="event-id-v1";
    internal const string HistoryContext="history-id-v1";
    internal static bool ValidHistoryCursor(string? value)=>value is not null && value.Length<=128 && value.All(HistoryCursorChar);
    private static bool HistoryCursorChar(char c)=>c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or ':' or '-' or '_';
    internal static string HistoryContextSuffix(bool selected,string? cursor)=>selected?"|"+HistoryContext+"|"+(cursor??""):"";
    private volatile BluetoothStatus status=new(false,null,"Select a paired Android device in local administration",[]);
    private readonly string configFile=Path.Combine(dir,"bluetooth-phone.txt");
    private volatile string? chosen;
    private volatile CancellationTokenSource? active;
    private long sentFrames,lastMessageAt,lastAck,sendTicks,ackRoundTripUs,sentBytes,totalSentFrames,totalSentBytes,connections;
    private volatile bool flowControl;
    private volatile LatestFrameWindow? sendWindow;
    public BluetoothStatus Status()=>status with {SentFrames=Interlocked.Read(ref sentFrames),LastPhoneMessageAtUnixMs=Interlocked.Read(ref lastMessageAt),AcknowledgedSequence=Interlocked.Read(ref lastAck),AckRoundTripMs=flowControl?Interlocked.Read(ref ackRoundTripUs)/1000d:null,FlowControl=flowControl,FlowMode=sendWindow is not null?"latest-window-v2":flowControl?"stop-wait-v1":"legacy",InFlight=sendWindow?.Count??0,PeakInFlight=sendWindow?.Peak??0,SentBytes=Interlocked.Read(ref sentBytes),TotalSentFrames=Interlocked.Read(ref totalSentFrames),TotalSentBytes=Interlocked.Read(ref totalSentBytes),Connections=Interlocked.Read(ref connections)};
    public bool Select(string address){if(!status.Paired.Any(p=>p.Address==address))return false;try{File.WriteAllText(configFile,address);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){log.LogWarning("Cannot save Bluetooth selection: {Type}",e.GetType().Name);return false;}chosen=address;try{active?.Cancel();}catch(ObjectDisposedException){}return true;}
    protected override async Task ExecuteAsync(CancellationToken ct){
        try{if(File.Exists(configFile))chosen=File.ReadAllText(configFile).Trim();}catch(Exception e)when(e is IOException or UnauthorizedAccessException){log.LogWarning("Cannot read Bluetooth selection: {Type}",e.GetType().Name);}
        while(!ct.IsCancellationRequested){
            var phase="adapter";
            try {
                using var owned=new DisposeOnce<BluetoothClient>(new BluetoothClient(),e=>log.LogDebug("Bluetooth cleanup: {Type}",e.GetType().Name));
                var client=owned.Value;
                var paired=client.PairedDevices.Select(p=>new BluetoothPeer(p.DeviceName,p.DeviceAddress.ToString())).ToArray();
                var peer=paired.FirstOrDefault(p=>p.Address==chosen) ?? (paired.Length==1?paired[0]:null);
                if(peer is null){status=new(false,null,"Pair an Android device, then select it in local administration",paired);await Task.Delay(5000,ct);continue;}
                status=new(false,peer.Name,"Connecting Bluetooth",paired);
                phase="security";client.Authenticate=true;client.Encrypt=true;
                using var connect=CancellationTokenSource.CreateLinkedTokenSource(ct);connect.CancelAfter(5000);
                using var abort=connect.Token.Register(owned.Dispose);
                phase="connect";
                // AF_BTH does not support the TCP ConnectEx path used by Socket.BeginConnect on this runtime.
                var pending=Task.Run(()=>client.Connect(BluetoothAddress.Parse(peer.Address),ServiceId));
                // Dispose closes the native socket on timeout; also observe a delayed native completion.
                _=pending.ContinueWith(t=>{_=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
                try{await pending.WaitAsync(connect.Token);}catch(OperationCanceledException){owned.Dispose();try{await pending.WaitAsync(ct);}catch(Exception){}throw;}
                connect.CancelAfter(Timeout.Infinite);
                using var stream=client.GetStream();
                using var connection=CancellationTokenSource.CreateLinkedTokenSource(ct);
                active=connection;
                using var stop=connection.Token.Register(owned.Dispose);
                using var handshake=CancellationTokenSource.CreateLinkedTokenSource(connection.Token);handshake.CancelAfter(4000);
                phase="handshake";var root=SHA256.HashData(Encoding.UTF8.GetBytes(token));var cn=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                await BluetoothCrypto.WritePlain(stream,new {v=1,pc=fingerprint,cn,features="ack-route-v1",flow="latest-window-v2",maxInFlight=3,historyResume=HistoryFeature},handshake.Token);
                using var challenge=await BluetoothCrypto.ReadPlain(stream,handshake.Token);
                var sn=challenge.RootElement.GetProperty("sn").GetString()!;
                if(Convert.FromBase64String(sn).Length!=32)throw new CryptographicException("Invalid Bluetooth nonce");
                flowControl=challenge.RootElement.TryGetProperty("features",out var features) && features.GetString()=="ack-route-v1";
                var pipelined=flowControl && challenge.RootElement.TryGetProperty("flow",out var mode) && mode.GetString()=="latest-window-v2"
                    && challenge.RootElement.TryGetProperty("maxInFlight",out var limit) && limit.GetInt32()==3;
                var resumeHistory=false;var historyAfterId="";
                if(challenge.RootElement.TryGetProperty("historyResume",out var resume) && resume.ValueKind==JsonValueKind.String && resume.GetString()==HistoryFeature){
                    if(!challenge.RootElement.TryGetProperty("historyAfterId",out var afterId) || afterId.ValueKind!=JsonValueKind.String || !ValidHistoryCursor(afterId.GetString()))
                        throw new CryptographicException("Invalid Bluetooth history cursor");
                    resumeHistory=true;historyAfterId=afterId.GetString()!;
                }
                var context="v1|"+fingerprint+"|"+cn+"|"+sn+(flowControl?"|ack-route-v1":"")+(pipelined?"|latest-window-v2|3":"")+HistoryContextSuffix(resumeHistory,historyAfterId);
                var expected=BluetoothCrypto.Mac(root,"phone|"+context);
                if(!CryptographicOperations.FixedTimeEquals(expected,Convert.FromBase64String(challenge.RootElement.GetProperty("proof").GetString()!)))throw new CryptographicException("Bluetooth phone identity rejected");
                await BluetoothCrypto.WritePlain(stream,new {proof=BluetoothCrypto.Proof(root,"pc",context)},handshake.Token);
                using var secure=new BluetoothSecureChannel(stream,BluetoothCrypto.Mac(root,"pc-to-phone|"+context),BluetoothCrypto.Mac(root,"phone-to-pc|"+context));
                CryptographicOperations.ZeroMemory(root);
                status=new(true,peer.Name,"Bluetooth authenticated; Wi-Fi standby",paired);
                Interlocked.Exchange(ref sentFrames,0);Interlocked.Exchange(ref sentBytes,0);Interlocked.Exchange(ref lastMessageAt,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());Interlocked.Exchange(ref lastAck,0);
                phase="stream";
                Interlocked.Increment(ref connections);log.LogInformation("Authenticated Bluetooth connection established");
                // Bound the application queue to one state. On acknowledgement, collect the newest snapshot;
                // superseded samples remain in the collector, never queue seconds of obsolete UI data.
                var acknowledgements=Channel.CreateBounded<long>(1);
                var window=pipelined?new LatestFrameWindow(3):null;sendWindow=window;
                var historyCursor=resumeHistory?ai.CursorFor(historyAfterId):0;
                connection.CancelAfter(flowControl?15000:75000);
                var reading=ReadMessages(secure,acknowledgements.Writer,window,connection);var writing=WriteStates(secure,acknowledgements.Reader,window,historyCursor,connection.Token);
                await Task.WhenAny(reading,writing);connection.Cancel();owned.Dispose();
                try{await Task.WhenAll(reading,writing);}catch(Exception e)when(e is IOException or OperationCanceledException or ObjectDisposedException){ }
            }catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception e){var error=e is SocketException socket?$"{socket.SocketErrorCode} ({socket.NativeErrorCode})":e.GetType().Name;status=status with {Connected=false,Message=$"Bluetooth {phase}: {error}"};log.LogDebug("Bluetooth: {Type}",error);}
            finally{active=null;sendWindow=null;status=status with {Connected=false};}
            if(!ct.IsCancellationRequested)try{await Task.Delay(3000,ct);}catch(OperationCanceledException){break;}
        }
    }
    private async Task WriteStates(BluetoothSecureChannel channel,ChannelReader<long> acknowledgements,LatestFrameWindow? window,long cursor,CancellationToken ct){
        long sequence=-1;string? lastRoute=null;var telemetry=new BluetoothTelemetry();using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(25));
        while(await timer.WaitForNextTickAsync(ct)){
            if(window is not null && !window.CanSend)continue;
            var sample=collector.Latest;if(sample is null || sample.Sequence==sequence)continue;
            var history=ai.EventsAfter(cursor);if(window is not null)history=history.Take(1).ToArray();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(2000);
            var hosts=endpoints.Hosts;var route=string.Join(',',hosts);
            if(flowControl && lastRoute!=route){await channel.Write(new {type="route",hosts,port=tlsPort},timeout.Token);lastRoute=route;}
            Interlocked.Exchange(ref sendTicks,Stopwatch.GetTimestamp());
            window?.Sent(sample.Sequence);
            var state=WireState.Create(sample,ai,history.Length>0?history:null);var bytes=await channel.Write(window is not null?telemetry.Encode(state):new {type="state",data=state},timeout.Token);
            Interlocked.Add(ref sentBytes,bytes);Interlocked.Add(ref totalSentBytes,bytes);
            Interlocked.Increment(ref sentFrames);Interlocked.Increment(ref totalSentFrames);
            if(flowControl && window is null && await acknowledgements.ReadAsync(timeout.Token)!=sample.Sequence)throw new InvalidDataException("Bluetooth acknowledgement mismatch");
            sequence=sample.Sequence;if(history.Length>0)cursor=history[^1].Sequence;
        }
    }
    private async Task ReadMessages(BluetoothSecureChannel channel,ChannelWriter<long> acknowledgements,LatestFrameWindow? window,CancellationTokenSource connection){
        var ct=connection.Token;
        while(!ct.IsCancellationRequested){using var message=await channel.Read(ct);var root=message.RootElement;
            Interlocked.Exchange(ref lastMessageAt,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            connection.CancelAfter(flowControl?15000:75000);
            if(root.GetProperty("type").GetString()=="ack"){
                var sequence=root.GetProperty("sequence").GetInt64();
                if(!flowControl)throw new InvalidDataException("Unexpected Bluetooth acknowledgement");
                double? rtt;
                if(window is not null)rtt=window.Acknowledge(sequence);
                else {if(!acknowledgements.TryWrite(sequence))throw new InvalidDataException("Unexpected Bluetooth acknowledgement");rtt=Stopwatch.GetElapsedTime(Interlocked.Read(ref sendTicks)).TotalMilliseconds;}
                if(rtt is { } observed){Interlocked.Exchange(ref lastAck,sequence);Interlocked.Exchange(ref ackRoundTripUs,(long)(observed*1000));}
                continue;
            }
            if(root.GetProperty("type").GetString()!="ping")throw new InvalidDataException("Unsupported Bluetooth message");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(2000);
            await channel.Write(new {type="time",id=root.GetProperty("id").GetInt64(),unixMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},timeout.Token);
        }
    }
}
