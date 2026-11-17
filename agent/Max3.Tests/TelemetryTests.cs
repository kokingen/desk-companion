using System.Text.Json;
using Max3.Agent;

internal static class TelemetryTests {
    internal static void Run(Action<bool,string> check){
        var s=new Snapshot(10,10000,12345,new(32,9900,250,3,true),new(67,5583457484,8333518632,9800,500,2,true),
            [new("nic-id","adapter","description","wifi",true,true,500,600,100,200,90,190,100,9999,10,true,null)],new(100,200,true,9900,250,3));
        var w=new WireState(1,"session",10020,777,s,new("working","confirmed","codex",9990,"task-id","metadata"),null);
        var codec=new BluetoothTelemetry();
        JsonElement Encode(WireState state)=>JsonSerializer.SerializeToElement(codec.Encode(state),new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("data");
        var a=Encode(w);
        check(a.GetArrayLength()==15 && a[7][0][0].GetString()=="nic-id","first telemetry frame includes complete interface identity");
        check(a[8][0][8].GetInt64()==-1 && a[8][0][1].GetUInt64()==500,"telemetry preserves native NIC time offset and exact byte counters");
        check(a[10].GetProperty("usedBytes").GetUInt64()==5583457484 && a[10].GetProperty("percent").GetDouble()==67,"RAM bytes and percent remain one native sample");
        var b=Encode(w with {Metrics=s with {Sequence=11,SampledAtUnixMs=10100}});
        check(b[7].ValueKind==JsonValueKind.Null && b[9].ValueKind==JsonValueKind.Null && b[10].ValueKind==JsonValueKind.Null && b[13].ValueKind==JsonValueKind.Null,"unchanged metadata and slow samples consume no repeated payload");
        var c=Encode(w with {Metrics=s with {Memory=s.Memory with {Sequence=3,SampledAtUnixMs=10300}}});
        check(c[10].GetProperty("sampledAtUnixMs").GetInt64()==10300,"a new RAM sample carries its real timestamp");
        var reset=JsonSerializer.SerializeToElement(new BluetoothTelemetry().Encode(w),new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("data");
        check(reset[7].ValueKind==JsonValueKind.Array && reset[9].ValueKind==JsonValueKind.Object,"reconnection resets delta cache and sends a complete baseline");
    }
}
