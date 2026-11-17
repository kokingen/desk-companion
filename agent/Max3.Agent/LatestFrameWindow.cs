using System.Diagnostics;

namespace Max3.Agent;

// Credits bound bytes already admitted to RFCOMM. There is no queue of snapshots:
// when a credit becomes free the caller reads Collector.Latest again.
internal sealed class LatestFrameWindow(int capacity=8) {
    private readonly object gate=new();
    private readonly SortedDictionary<long,long> pending=[];
    private long highestSent,highestAck;
    private int peak;
    public int Count {get{lock(gate)return pending.Count;}}
    public int Peak {get{lock(gate)return peak;}}
    public bool CanSend {get{lock(gate){CheckTimeout();return pending.Count<capacity;}}}
    public void Sent(long sequence) {lock(gate){
        CheckTimeout();
        if(sequence<=highestSent || pending.Count>=capacity)throw new InvalidDataException("Invalid send window admission");
        pending.Add(sequence,Stopwatch.GetTimestamp());highestSent=sequence;peak=Math.Max(peak,pending.Count);
    }}
    public double? Acknowledge(long sequence) {lock(gate){
        if(sequence<=highestAck)return null; // a delayed cumulative acknowledgement adds no credit
        if(sequence>highestSent || !pending.TryGetValue(sequence,out var sent))throw new InvalidDataException("Unsent Bluetooth acknowledgement");
        var elapsed=Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
        foreach(var item in pending.Keys.TakeWhile(s=>s<=sequence).ToArray())pending.Remove(item);
        highestAck=sequence;return elapsed;
    }}
    private void CheckTimeout(){if(pending.Count>0 && Stopwatch.GetElapsedTime(pending.First().Value).TotalMilliseconds>1500)throw new IOException("Bluetooth acknowledgement window expired");}
}
