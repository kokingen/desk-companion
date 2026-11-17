using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Max3.Agent;

/// <summary>Reads metadata only from the desktop's paginated history. Never selects message text, commands or arguments.</summary>
internal sealed class CodexDesktopState : IDisposable
{
    private IntPtr db;
    private readonly long monitoringStartedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    internal const long ActivityLifetimeMs=60_000;
    private readonly string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex","thread_history_1.sqlite");
    public AiStatus? Read() {
        if(!File.Exists(file))return null;
        if(db==IntPtr.Zero) {
            // SQLITE_OPEN_READONLY | SQLITE_OPEN_FULLMUTEX. Never migrate or alter the Codex database.
            var rc=sqlite3_open_v2(file,out db,1|0x10000,IntPtr.Zero);
            if(rc!=0){Dispose();return null;}
            sqlite3_busy_timeout(db,40);
        }
        var turns=new List<string?[]>();
        var seen=new HashSet<(string?,string?)>();
        void AddTurn(string?[] row){if(seen.Add((row[0],row[1])))turns.Add(row);}
        foreach(var row in Query("SELECT thread_id,turn_id,status,started_at,completed_at FROM thread_turns ORDER BY COALESCE(completed_at,started_at) DESC LIMIT 8"))AddTurn(row);
        // Also include up to 32 in-progress turns so recent completions cannot push active work out of the candidate set.
        foreach(var row in Query("SELECT thread_id,turn_id,status,started_at,completed_at FROM thread_turns WHERE status='inProgress' ORDER BY started_at DESC LIMIT 32"))AddTurn(row);
        if(turns.Count==0)return null;
        var candidates=new List<DesktopTaskMetadata>();
        foreach(var turn in turns){
            var items=Query("SELECT item_id,item_type,created_at_ms,started_at_ms,completed_at_ms,json_extract(item_json,'$.status'),json_extract(item_json,'$.phase') FROM thread_items WHERE thread_id=? AND turn_id=? ORDER BY rollout_ordinal DESC LIMIT 16",turn[0],turn[1]);
            candidates.Add(new(turn[0],turn[1],turn[2],Seconds(turn[3]),Seconds(turn[4]),items.Select(i=>new DesktopItemMetadata(i[0],i[1],i[5],Math.Max(Number(i[4]),Math.Max(Number(i[3]),Number(i[2]))))).ToArray()));
        }
        return Evaluate(candidates,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),monitoringStartedAt);
    }
    private static long Number(string? value)=>long.TryParse(value,CultureInfo.InvariantCulture,out var n) && n>0?n:0;
    private static long Seconds(string? value){var n=Number(value);return n<=long.MaxValue/1000?n*1000:0;}
    internal static AiStatus Evaluate(IReadOnlyList<DesktopTaskMetadata> turns,long now,long monitoringStartedAt){
        // Prefer a still-valid in-progress turn with fresh activity so newer completions cannot hide live work.
        var fresh=turns.Where(t=>t.Status=="inProgress" && t.ActivityAtUnixMs>monitoringStartedAt && t.ActivityAtUnixMs<=now && now-t.ActivityAtUnixMs<=ActivityLifetimeMs)
            .OrderByDescending(t=>t.ActivityAtUnixMs).FirstOrDefault();
        // Otherwise use the most recent actual lifecycle/item timestamp, not an arbitrary unfinished turn.
        var turn=fresh ?? turns.OrderByDescending(t=>t.ActivityAtUnixMs).First();
        var at=turn.ActivityAtUnixMs;
        var last=turn.Items.OrderByDescending(i=>i.ActivityAtUnixMs).FirstOrDefault();
        var state="unknown";var source="confirmed";var reason="Codex desktop turn metadata";
        if(at<=0 || at>now){source="unavailable";reason="Task metadata has no usable activity timestamp";}
        else if(turn.Status=="completed") {state=now-at<10000?"finished":"idle";}
        else if(turn.Status is "failed" or "interrupted") {state=now-at<10000?"failed":"idle";}
        else if(turn.Status=="inProgress") {
            if(at<=monitoringStartedAt){source="unavailable";reason="Unfinished task predates this monitor; no new task event observed";}
            else if(now-at>ActivityLifetimeMs){source="unavailable";reason="No new task activity within 60 seconds; execution cannot be confirmed";}
            else {
                var active=turn.Items.Where(i=>i.Status=="inProgress" && i.ActivityAtUnixMs>monitoringStartedAt && i.ActivityAtUnixMs<=now && now-i.ActivityAtUnixMs<=ActivityLifetimeMs)
                    .OrderByDescending(i=>i.ActivityAtUnixMs).FirstOrDefault();
                state=active?.Type switch {"reasoning"=>"thinking","fileChange"=>"coding","webSearch"=>"searching",_=>"working"};
                if(active is null)reason="Recent in-progress turn activity observed; detailed phase unavailable";
            }
        }
        else {source="unavailable";reason="No recognised desktop task lifecycle";}
        // The complete identity is pseudonymised; item IDs must not escape alongside hashed turn IDs.
        var identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]{turn.ThreadId,turn.TurnId,last?.Id,turn.Status,state,source}))))[..24];
        return new(state,source,"codex",at,$"desktop:{identity}",reason);
    }
    private List<string?[]> Query(string sql,params string?[] args) {
        var result=new List<string?[]>();
        if(sqlite3_prepare_v2(db,sql,-1,out var statement,IntPtr.Zero)!=0)return result;
        try {
            for(int i=0;i<args.Length;i++)sqlite3_bind_text(statement,i+1,args[i] ?? "",-1,new IntPtr(-1));
            while(sqlite3_step(statement)==100) {
                var row=new string?[sqlite3_column_count(statement)];
                for(int i=0;i<row.Length;i++) {
                    var size=sqlite3_column_bytes(statement,i);
                    if(size>512)throw new InvalidDataException("Unexpected desktop metadata length");
                    var value=sqlite3_column_text(statement,i);row[i]=value==IntPtr.Zero?null:Marshal.PtrToStringUTF8(value,size);
                }
                result.Add(row);
            }
            return result;
        }finally{sqlite3_finalize(statement);}
    }
    public void Dispose(){if(db!=IntPtr.Zero){sqlite3_close(db);db=IntPtr.Zero;}}
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string file,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int bytes,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_bind_text(IntPtr statement,int index,[MarshalAs(UnmanagedType.LPUTF8Str)]string value,int bytes,IntPtr destructor);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_column_count(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_column_bytes(IntPtr statement,int index);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern IntPtr sqlite3_column_text(IntPtr statement,int index);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_busy_timeout(IntPtr db,int ms);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]private static extern int sqlite3_close(IntPtr db);
}

internal sealed record DesktopItemMetadata(string? Id,string? Type,string? Status,long ActivityAtUnixMs);
internal sealed record DesktopTaskMetadata(string? ThreadId,string? TurnId,string? Status,long StartedAtUnixMs,long CompletedAtUnixMs,IReadOnlyList<DesktopItemMetadata> Items){
    public long ActivityAtUnixMs=>Math.Max(Math.Max(StartedAtUnixMs,CompletedAtUnixMs),Items.Count>0?Items.Max(i=>i.ActivityAtUnixMs):0);
}
