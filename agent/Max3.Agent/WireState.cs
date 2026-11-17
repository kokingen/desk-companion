namespace Max3.Agent;
public sealed record WireState(int Schema,string AgentSessionId,long ServerSentAtUnixMs,long UptimeMs,Snapshot? Metrics,AiStatus Ai,TaskEvent[]? EventHistory) {
    private static readonly string Session=Guid.NewGuid().ToString("N");
    public static WireState Create(Snapshot? sample,CodexEvents ai,TaskEvent[]? history=null)=>
        new(1,Session,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Environment.TickCount64,sample,ai.Snapshot(),history);
}
