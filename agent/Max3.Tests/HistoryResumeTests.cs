using Max3.Agent;

internal static class HistoryResumeTests {
    internal static void Run(Action<bool,string> check){
        TaskEvent Event(long sequence,string id)=>new(sequence,new AiStatus("working","confirmed","codex",sequence,id,"metadata"));
        var retained=new[]{Event(1,"desktop:a"),Event(2,"desktop:b"),Event(3,"desktop:a")};
        check(CodexEvents.CursorFor(retained,"desktop:a")==1,"history resume maps a repeated event id to its earliest retained sequence");
        check(CodexEvents.CursorFor(retained,"desktop:b")==2,"history resume maps a retained event id to its sequence");
        check(CodexEvents.CursorFor(retained,"desktop:pruned")==0,"unknown or pruned history id restarts from zero");
        check(CodexEvents.CursorFor(retained,null)==0 && CodexEvents.CursorFor(Array.Empty<TaskEvent>(),"desktop:a")==0,"missing or empty retained history restarts from zero");
        check(BluetoothTransport.ValidHistoryCursor(""),"empty history cursor is valid");
        check(BluetoothTransport.ValidHistoryCursor("desktop:abc-DEF_123"),"history cursor accepts bounded ASCII letters digits colon hyphen underscore");
        check(BluetoothTransport.ValidHistoryCursor(new string('a',128)),"history cursor accepts exactly 128 characters");
        check(!BluetoothTransport.ValidHistoryCursor(new string('a',129)) && !BluetoothTransport.ValidHistoryCursor("desktop:a/b") && !BluetoothTransport.ValidHistoryCursor("任务"),"history cursor rejects oversize, path and non-ASCII values");
        check(BluetoothTransport.HistoryContextSuffix(false,"desktop:a")=="","unselected history resume leaves the legacy handshake context unchanged");
        check(BluetoothTransport.HistoryContextSuffix(true,"desktop:a")=="|history-id-v1|desktop:a","selected history resume binds the exact cursor in the authenticated context");
    }
}
