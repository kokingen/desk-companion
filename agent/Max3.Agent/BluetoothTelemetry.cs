using System.Text.Json;

namespace Max3.Agent;

// Negotiated v2 only. Positional telemetry avoids sending NIC names/GUIDs and
// unchanged CPU/RAM/disk/WLAN/task records ten times a second. RFCOMM is an ordered,
// authenticated stream: a new connection always starts with all records present.
// Every packet still contains a new native network sample, never a repeated tick.
internal sealed class BluetoothTelemetry {
    private string? metadata,ai;
    private long cpu=-1,memory=-1,disk=-1,wifi=-1;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    // Wire rates have 0.01 byte/s resolution, intervals 0.01 ms. Exact native byte
    // counters, sample timestamps/sequences and RAM bytes remain lossless.
    private static double? Rate(double? value)=>value is { } n?Math.Round(n,2):null;
    public object Encode(WireState state) {
        var s=state.Metrics??throw new InvalidDataException("Missing telemetry sample");
        var descriptors=s.Interfaces.Select(n=>new object?[]{n.Id,n.Name,n.Description,n.Kind,n.Physical}).ToArray();
        var identity=JsonSerializer.Serialize(descriptors,Json);
        var status=JsonSerializer.Serialize(state.Ai,Json);
        var result=new {type="telemetry",data=new object?[]{
            state.Schema,state.AgentSessionId,state.ServerSentAtUnixMs,state.UptimeMs,
            s.Sequence,s.SampledAtUnixMs,s.MonotonicMs,
            identity!=metadata?descriptors:null,
            s.Interfaces.Select(n=>new object?[]{n.Connected,n.RxBytes,n.TxBytes,Rate(n.RawDownBps),Rate(n.RawUpBps),Rate(n.SmoothDownBps),Rate(n.SmoothUpBps),Math.Round(n.IntervalMs,2),n.SampledAtUnixMs-s.SampledAtUnixMs,n.Valid,n.Reason}).ToArray(),
            s.Cpu.Sequence!=cpu?s.Cpu:null,s.Memory.Sequence!=memory?s.Memory:null,
            s.Disk.Sequence!=disk?s.Disk:null,s.WifiSignal?.Sequence!=wifi?s.WifiSignal:null,
            status!=ai?state.Ai:null,state.EventHistory
        }};
        metadata=identity;ai=status;cpu=s.Cpu.Sequence;memory=s.Memory.Sequence;disk=s.Disk.Sequence;wifi=s.WifiSignal?.Sequence??-1;
        return result;
    }
}
