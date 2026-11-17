using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhoneBox.Agent;

namespace Max3.Agent;

public sealed record AiStatus(string State, string Source, string Tool, long? EventAtUnixMs, string? EventId, string Reason);
public sealed record TaskEvent(long Sequence, AiStatus Status);

/// <summary>Reuses PhoneBox's incremental local Codex reader. No prompts, paths or tool arguments are transmitted.</summary>
public sealed class CodexEvents : BackgroundService
{
    private readonly SessionMonitor reader;
    private readonly ILogger<CodexEvents> log;
    private readonly string journal;
    private readonly JsonSerializerOptions json=new(JsonSerializerDefaults.Web);
    private volatile TaskEvent[] events=[];
    private string? lastId;
    private long sequence;
    private long observedAt;
    public CodexEvents(ILogger<SessionMonitor> readerLog, ILogger<CodexEvents> log,IConfiguration config) {
        reader=new(readerLog);this.log=log;
        var dir=Path.GetFullPath(config["data-dir"] ?? Path.Combine(AppContext.BaseDirectory,"data"));
        journal=Path.Combine(dir,"task-events.jsonl");
        if(File.Exists(journal)) {
            try {
                events=File.ReadLines(journal).TakeLast(2000).Select(line=>JsonSerializer.Deserialize<TaskEvent>(line,json))
                    .OfType<TaskEvent>().Select(e=>e with {Status=PseudonymizeLegacy(e.Status)}).ToArray();
                sequence=events.LastOrDefault()?.Sequence ?? 0;
                lastId=events.LastOrDefault()?.Status.EventId;
            }catch(Exception e)when(e is IOException or JsonException){log.LogWarning("Task journal could not be read: {Type}",e.GetType().Name);}
        }
    }
    public TaskEvent[] EventsAfter(long cursor) => events.Where(e=>e.Sequence>cursor).Take(32).ToArray();
    public long CursorFor(string? eventId)=>CursorFor(events,eventId);
    // Sequences can restart when the journal is reset, so the phone stores the event identity instead.
    // First retained occurrence is deliberate: with an unexpectedly repeated identity it can replay
    // known later events, but it can never skip distinct events between the two occurrences.
    internal static long CursorFor(IReadOnlyList<TaskEvent> retained,string? eventId){
        if(string.IsNullOrEmpty(eventId))return 0;
        foreach(var item in retained)if(string.Equals(item.Status.EventId,eventId,StringComparison.Ordinal))return item.Sequence;
        return 0;
    }
    internal static AiStatus PseudonymizeLegacy(AiStatus status) {
        if(status.EventId is not { } id || !id.StartsWith("desktop:",StringComparison.Ordinal) || id.Length==32)return status;
        var digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..24];
        return status with {EventId="desktop:"+digest};
    }
    private void Remember(AiStatus status) {
        if(status.EventId is null || status.EventId==lastId)return;
        lastId=status.EventId;
        var item=new TaskEvent(++sequence,status);
        var cutoff=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-30L*86400000;
        events=events.Append(item).Where(e=>e.Status.EventAtUnixMs>=cutoff).TakeLast(2000).ToArray();
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
            if(sequence%100==0 || new FileInfo(journal).Exists && new FileInfo(journal).Length>2_000_000) {
                File.WriteAllLines(journal+".tmp",events.Select(e=>JsonSerializer.Serialize(e,json)));
                File.Move(journal+".tmp",journal,true);
            }else File.AppendAllText(journal,JsonSerializer.Serialize(item,json)+"\n");
        }catch(IOException e){log.LogWarning("Task journal write failed: {Type}",e.GetType().Name);}
    }
    private volatile AiStatus latest = new("unknown", "unavailable", "codex", null, null, "No readable task event");
    public AiStatus Snapshot() {
        var s = latest;
        if(s.EventId?.StartsWith("desktop:",StringComparison.Ordinal)==true){
            if(Environment.TickCount64-Interlocked.Read(ref observedAt)>10000)return s with {State="unknown",Source="unavailable",Reason="Desktop task metadata is no longer being observed"};
            if(s.Source=="unavailable")return s;
            if(s.State is "working" or "thinking" or "coding" or "searching" && s.EventAtUnixMs is { } activity && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-activity>CodexDesktopState.ActivityLifetimeMs)
                return s with {State="unknown",Source="unavailable",Reason="No new task activity within 60 seconds; execution cannot be confirmed"};
            return s;
        }
        if (s.EventAtUnixMs is { } t && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t > 60_000)
            return s with { State = "unknown", Reason = "Task event older than 60 seconds" };
        return s;
    }
    protected override async Task ExecuteAsync(CancellationToken ct) {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        using var desktop=new CodexDesktopState();
        try {
            do {
                try {
                    var observed=desktop.Read();
                    if(observed is not null){latest=observed;Interlocked.Exchange(ref observedAt,Environment.TickCount64);Remember(latest);continue;}
                    var sessions = reader.ReadCodex().ToArray();
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var active = sessions.Where(s => s.Status == "busy" && now - s.Since < 60_000)
                        .OrderByDescending(s => s.Since).FirstOrDefault();
                    var s = active ?? sessions.OrderByDescending(s => s.Since).FirstOrDefault();
                    if (s is null) continue;
                    var state = s.Outcome switch {
                        "completed" => now - s.Since < 10_000 ? "finished" : "idle",
                        "aborted" => "failed",
                        _ when s.Status == "busy" => ToolState(s.LastTool),
                        _ => "unknown"
                    };
                    var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s.Id)))[..12];
                    latest = new AiStatus(state, "log_inferred", "codex", s.Since,
                        $"{identity}:{s.Since}:{state}", "Inferred from Codex task lifecycle and last tool; no official live hook");
                    Remember(latest);
                } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
                    log.LogDebug("Codex log unavailable: {Type}", e.GetType().Name);
                    latest = new("unknown", "unavailable", "codex", null, null, "Local event log inaccessible");
                }
            } while (await timer.WaitForNextTickAsync(ct));
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    internal static string ToolState(string? tool) => tool switch {
        "apply_patch" => "coding",
        "web.run" or "web" or "search_query" => "searching",
        _ => "working"
    };
}
