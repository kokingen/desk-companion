using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Max3.Agent;

public sealed record CpuSample(double? Percent, long SampledAtUnixMs, double IntervalMs, long Sequence, bool Valid);
public sealed record MemorySample(double? Percent, ulong UsedBytes, ulong TotalBytes, long SampledAtUnixMs, double IntervalMs, long Sequence, bool Valid);
public sealed record NetworkSample(string Id, string Name, string Description, string Kind, bool Connected, bool Physical,
    ulong RxBytes, ulong TxBytes, double? RawDownBps, double? RawUpBps, double? SmoothDownBps, double? SmoothUpBps,
    double IntervalMs, long SampledAtUnixMs, long Sequence, bool Valid, string? Reason);
public sealed record DiskSample(double? ReadBps, double? WriteBps, bool Valid, long SampledAtUnixMs, double IntervalMs, long Sequence);
public sealed record Snapshot(long Sequence, long SampledAtUnixMs, double MonotonicMs, CpuSample Cpu, MemorySample Memory,
    IReadOnlyList<NetworkSample> Interfaces, DiskSample Disk, WifiSample? WifiSignal = null);
public sealed record WifiSignal(string Id, int? RssiDbm, uint NativeError);
public sealed record WifiSample(long Sequence, long SampledAtUnixMs, double IntervalMs, IReadOnlyList<WifiSignal> Interfaces);

public sealed class Collector(ILogger<Collector> log) : BackgroundService
{
    private volatile Snapshot? latest;
    private volatile CpuSample cpu = new(null, 0, 0, 0, false);
    private volatile MemorySample memory = new(null, 0, 0, 0, 0, 0, false);
    private volatile DiskSample disk = new(null, null, false, 0, 0, 0);
    private volatile WifiSample wifi = new(0,0,0,[]);
    public Snapshot? Latest => latest;
    internal static double Mono() => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
    private static long Utc() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    protected override async Task ExecuteAsync(CancellationToken ct) {
        if (IntPtr.Size != 8 || Marshal.SizeOf<IfRow>() != 1352) throw new PlatformNotSupportedException("Windows x64 MIB_IF_ROW2 ABI required");
        try { await Task.WhenAll(CpuLoop(ct), MemoryLoop(ct), DiskLoop(ct), WifiLoop(ct), NetworkLoop(ct)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private async Task CpuLoop(CancellationToken ct) {
        var ok0 = GetSystemTimes(out var idle0, out var kernel0, out var user0);
        var t0 = Mono();long seq=0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while(await timer.WaitForNextTickAsync(ct)) {
            var ok = GetSystemTimes(out var idle, out var kernel, out var user);
            var now=Mono();var elapsed=now-t0;
            if(elapsed<200)continue; // a delayed PeriodicTimer can coalesce a pending tick; do not shorten the sample window
            var valid=ok && ok0 && elapsed>0 && elapsed<=2000 && kernel>=kernel0 && user>=user0 && idle>=idle0;
            var total=valid?(double)(kernel-kernel0)+(user-user0):0;
            var inactive=valid?(double)(idle-idle0):0;
            valid=valid && total>0 && inactive<=total;
            cpu=new(valid?Math.Clamp(100*(total-inactive)/total,0,100):null,Utc(),elapsed,++seq,valid);
            (idle0,kernel0,user0,t0,ok0)=(idle,kernel,user,now,ok);
        }
    }
    private async Task MemoryLoop(CancellationToken ct) {
        long seq=0;var before=Mono();
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while(await timer.WaitForNextTickAsync(ct)) {
            var m=new MemoryStatus { Length=(uint)Marshal.SizeOf<MemoryStatus>() };
            var ok=GlobalMemoryStatusEx(ref m) && m.TotalPhysical>0 && m.AvailablePhysical<=m.TotalPhysical;
            var now=Mono();var used=ok?m.TotalPhysical-m.AvailablePhysical:0;
            memory=new(ok?100d*used/m.TotalPhysical:null,used,m.TotalPhysical,Utc(),now-before,++seq,ok);before=now;
        }
    }
    private async Task DiskLoop(CancellationToken ct) {
        using var counters=new DiskCounters();long seq=0;var before=Mono();
        if(!counters.Available) log.LogWarning("Physical disk PDH counters unavailable; disk values remain unknown");
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while(await timer.WaitForNextTickAsync(ct)) {
            var now=Mono();var dt=now-before;
            var (read,write)=counters.Sample();
            var valid=dt>0 && dt<=2000 && read is not null && write is not null;
            disk=new(valid?read:null,valid?write:null,valid,Utc(),dt,++seq);before=now;
        }
    }
    private sealed record Adapter(ulong Luid,string Id,string Name,string Description,uint Type,bool Physical,string Kind);
    private async Task NetworkLoop(CancellationToken ct) {
        var adapters=Array.Empty<Adapter>();var rates=new Dictionary<ulong,NetworkRate>();
        var discoverAt=0d;var lastRead=0d;long seq=0;
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while(await timer.WaitForNextTickAsync(ct)) {
            var now=Mono();
            if(now-lastRead<80)continue;
            if(now>=discoverAt) {
                try {
                    adapters=Discover();
                    var current=adapters.Select(x=>x.Luid).ToHashSet();
                    foreach(var id in rates.Keys.Where(id=>!current.Contains(id)).ToArray())rates.Remove(id);
                } catch(Exception e) { log.LogWarning("Network discovery failed: {Type}",e.GetType().Name); }
                discoverAt=now+5000;
            }
            var samples=new List<NetworkSample>(adapters.Length);++seq;
            foreach(var a in adapters) {
                var row=new IfRow{Luid=a.Luid};var error=GetIfEntry2(ref row);var sampled=Mono();
                var connected=error==0 && row.OperStatus==1 && (row.Flags&16)==0;
                var physical=error==0 ? (row.Flags&1)!=0 : a.Physical;
                var kind=error==0 && (row.Type!=a.Type || physical!=a.Physical)?Kind(row.Type,physical,a.Description+" "+a.Name):a.Kind;
                if(!rates.TryGetValue(a.Luid,out var rate))rates[a.Luid]=rate=new NetworkRate();
                var result=rate.Sample(row.InOctets,row.OutOctets,sampled,connected);
                samples.Add(new(a.Id,a.Name,a.Description,kind,connected,physical,row.InOctets,row.OutOctets,
                    result.Down,result.Up,result.SmoothDown,result.SmoothUp,result.IntervalMs,Utc(),seq,result.Valid,
                    error!=0?$"native_error_{error}":result.Reason));
                if(error!=0)discoverAt=Math.Min(discoverAt,now+1000);
            }
            latest=new(seq,Utc(),Mono(),cpu,memory,samples.ToArray(),disk,wifi);
            // Gate coalesced ticks from the start of this real sample. Discovery or
            // native reads taking >20 ms must not suppress the next 100 ms tick.
            lastRead=now;
        }
    }
    internal static string Kind(uint type,bool hardware,string description) {
        if(hardware && type==71)return "wifi";
        if(hardware && type==6)return "ethernet";
        if(type==131 || description.Contains("Tailscale",StringComparison.OrdinalIgnoreCase)
            || description.Contains("TUN",StringComparison.OrdinalIgnoreCase) || description.Contains("VPN",StringComparison.OrdinalIgnoreCase)
            || description.Contains("WireGuard",StringComparison.OrdinalIgnoreCase))return "vpn";
        return !hardware && type!=24?"virtual":"other";
    }
    private static Adapter[] Discover() {
        var error=GetIfTable2Ex(2,out var table); // enumerate metadata without reading statistics for every filter layer
        if(error!=0)throw new InvalidOperationException($"GetIfTable2: {error}");
        try {
            var n=Marshal.ReadInt32(table);if(n<0 || n>4096)throw new InvalidDataException("Invalid native adapter count");
            var list=new List<Adapter>(n);
            for(int i=0;i<n;i++) {
                // Windows SDK netioapi.h: table rows start at 8 (8-byte NET_LUID alignment); each x64 row is 1352.
                var p=IntPtr.Add(table,8+i*1352);var r=Marshal.PtrToStructure<IfRow>(p);
                if(r.Type==24 || (r.Flags&2)!=0)continue; // filter layers duplicate the underlying interface
                var alias=Marshal.PtrToStringUni(IntPtr.Add(p,28),257)!.TrimEnd('\0');
                var desc=Marshal.PtrToStringUni(IntPtr.Add(p,542),257)!.TrimEnd('\0');
                var physical=(r.Flags&1)!=0;
                list.Add(new(r.Luid,r.Guid.ToString("D"),alias,desc,r.Type,physical,Kind(r.Type,physical,desc+" "+alias)));
            }
            return list.ToArray();
        } finally { FreeMibTable(table); }
    }
    private async Task WifiLoop(CancellationToken ct) {
        var error=WlanOpenHandle(2,IntPtr.Zero,out _,out var handle);
        long seq=0;var before=Mono();
        using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(1000));
        try {
            while(await timer.WaitForNextTickAsync(ct)) {
                var values=new List<WifiSignal>();
                if(error==0 && WlanEnumInterfaces(handle,IntPtr.Zero,out var list)==0) {
                    try {
                        var count=Marshal.ReadInt32(list);
                        if(count is >=0 and <=64)for(int i=0;i<count;i++) {
                            var id=Marshal.PtrToStructure<Guid>(IntPtr.Add(list,8+i*532));
                            // wlan_intf_opcode_rssi is a signed LONG in dBm; no SSID/BSSID is retained.
                            var result=WlanQueryInterface(handle,ref id,0x10000102,IntPtr.Zero,out var size,out var data,out _);
                            try { values.Add(new(id.ToString("D"),result==0 && size>=4?Marshal.ReadInt32(data):null,result)); }
                            finally {if(data!=IntPtr.Zero)WlanFreeMemory(data);}
                        }
                    } finally {WlanFreeMemory(list);}
                }
                var now=Mono();wifi=new(++seq,Utc(),now-before,values.ToArray());before=now;
            }
        } finally {if(handle!=IntPtr.Zero)WlanCloseHandle(handle,IntPtr.Zero);}
    }
    // ABI from Windows SDK MIB_IF_ROW2. Only numeric fields needed in the 100ms hot path; no strings allocated by marshalling.
    [StructLayout(LayoutKind.Explicit,Size=1352)]
    internal struct IfRow {
        [FieldOffset(0)]public ulong Luid;
        [FieldOffset(8)]public uint Index;
        [FieldOffset(12)]public Guid Guid;
        [FieldOffset(1128)]public uint Type;
        [FieldOffset(1152)]public byte Flags;
        [FieldOffset(1156)]public uint OperStatus;
        [FieldOffset(1208)]public ulong InOctets;
        [FieldOffset(1280)]public ulong OutOctets;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus {
        public uint Length,Load;
        public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GlobalMemoryStatusEx(ref MemoryStatus m);
    [DllImport("iphlpapi.dll")]private static extern uint GetIfEntry2(ref IfRow row);
    [DllImport("iphlpapi.dll")]private static extern uint GetIfTable2(out IntPtr table);
    [DllImport("iphlpapi.dll")]private static extern uint GetIfTable2Ex(uint level,out IntPtr table);
    [DllImport("iphlpapi.dll")]private static extern void FreeMibTable(IntPtr table);
    [DllImport("wlanapi.dll")]private static extern uint WlanOpenHandle(uint version,IntPtr reserved,out uint negotiated,out IntPtr handle);
    [DllImport("wlanapi.dll")]private static extern uint WlanEnumInterfaces(IntPtr handle,IntPtr reserved,out IntPtr list);
    [DllImport("wlanapi.dll")]private static extern uint WlanQueryInterface(IntPtr handle,ref Guid id,uint opcode,IntPtr reserved,out uint size,out IntPtr data,out uint valueType);
    [DllImport("wlanapi.dll")]private static extern void WlanFreeMemory(IntPtr memory);
    [DllImport("wlanapi.dll")]private static extern uint WlanCloseHandle(IntPtr handle,IntPtr reserved);
}

internal sealed record RateResult(double? Down,double? Up,double? SmoothDown,double? SmoothUp,double IntervalMs,bool Valid,string? Reason);
internal sealed class NetworkRate {
    private ulong rx,tx;private double at,smoothDown,smoothUp;private bool baseline,smoothing;
    public RateResult Sample(ulong received,ulong sent,double now,bool connected) {
        var dt=now-at;
        string? reason=!connected?"disconnected":!baseline?"baseline":dt<=0?"invalid_time":dt>2000?"sample_gap":received<rx || sent<tx?"counter_reset":null;
        var valid=reason is null;
        double? down=valid?(received-rx)*1000d/dt:null;double? up=valid?(sent-tx)*1000d/dt:null;
        if(valid){var a=1-Math.Exp(-dt/400);smoothDown=smoothing?smoothDown+a*(down!.Value-smoothDown):down!.Value;smoothUp=smoothing?smoothUp+a*(up!.Value-smoothUp):up!.Value;smoothing=true;}
        else smoothing=false;
        (rx,tx,at,baseline)=(received,sent,now,connected);
        return new(down,up,valid?smoothDown:null,valid?smoothUp:null,dt,valid,reason);
    }
}

internal sealed class DiskCounters : IDisposable {
    private IntPtr query,read,write;
    public bool Available {get;}
    public DiskCounters() {
        Available=PdhOpenQueryW(null,IntPtr.Zero,out query)==0
            && PdhAddEnglishCounterW(query,@"\PhysicalDisk(_Total)\Disk Read Bytes/sec",IntPtr.Zero,out read)==0
            && PdhAddEnglishCounterW(query,@"\PhysicalDisk(_Total)\Disk Write Bytes/sec",IntPtr.Zero,out write)==0;
        if(Available)PdhCollectQueryData(query);
    }
    public (double?,double?) Sample() {
        if(!Available || PdhCollectQueryData(query)!=0)return(null,null);
        double? Get(IntPtr c) => PdhGetFormattedCounterValue(c,0x200,out _,out var v)==0 && v.Status<=1 && double.IsFinite(v.Value) && v.Value>=0?v.Value:null;
        return(Get(read),Get(write));
    }
    public void Dispose(){if(query!=IntPtr.Zero){PdhCloseQuery(query);query=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Explicit,Size=16)]private struct PdhValue{[FieldOffset(0)]public uint Status;[FieldOffset(8)]public double Value;}
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]private static extern uint PdhOpenQueryW(string? source,IntPtr data,out IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]private static extern uint PdhAddEnglishCounterW(IntPtr query,string path,IntPtr data,out IntPtr counter);
    [DllImport("pdh.dll")]private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]private static extern uint PdhGetFormattedCounterValue(IntPtr counter,uint format,out uint type,out PdhValue value);
    [DllImport("pdh.dll")]private static extern uint PdhCloseQuery(IntPtr query);
}
