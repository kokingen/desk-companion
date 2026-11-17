using Max3.Agent;

internal static class DesktopStateTests {
    // Pure metadata fixtures. Does not open Codex databases or start an Agent process.
    internal static void Run(Action<bool,string> check){
        const long start=1_700_000_000_000;
        DesktopTaskMetadata Task(string status,long at,params DesktopItemMetadata[] items)=>new("private-thread-id","private-turn-id",status,at,status=="inProgress"?0:at,items);
        AiStatus Evaluate(DesktopTaskMetadata task,long now)=>CodexDesktopState.Evaluate([task],now,start);
        var inherited=Evaluate(Task("inProgress",start-1000),start+500);
        check(inherited.State=="unknown" && inherited.Source=="unavailable","unfinished task inherited at startup cannot confirm current work");
        var live=Task("inProgress",start+100);
        check(Evaluate(live,start+500).State=="working" && Evaluate(live,start+500).Source=="confirmed","new lifecycle activity after monitor startup confirms work");
        check(Evaluate(live,start+61_000).State=="unknown","repeated reads cannot refresh an unchanged in-progress event");
        var completed=Task("completed",start+200);
        var chosen=CodexDesktopState.Evaluate([Task("inProgress",start-1000),completed],start+500,start);
        check(chosen.State=="finished","old unfinished turn cannot override a newer completed turn");
        var freshEarlier=Task("inProgress",start+100);
        check(CodexDesktopState.Evaluate([freshEarlier,completed],start+500,start).State=="working","fresh active turn outranks a newer completed turn");
        check(Evaluate(completed,start+20_000).State=="idle","completed task becomes idle after completion indication");
        var item=new DesktopItemMetadata("private-item-id","reasoning","inProgress",start+300);
        var thinking=Evaluate(Task("inProgress",start-10_000,item),start+500);
        check(thinking.State=="thinking","recent explicit item status supports the detailed reasoning phase");
        var doneItem=item with {Status="completed"};
        check(Evaluate(Task("inProgress",start+100,doneItem),start+500).State=="working","completed reasoning item does not invent the current phase");
        check(!thinking.EventId!.Contains("private-",StringComparison.Ordinal) && thinking.EventId.Length==32,"entire task identity is hashed without original item IDs");
        var another=Evaluate(Task("inProgress",start-10_000,item with {Id="another-item-id"}),start+500);
        check(thinking.EventId!=another.EventId,"different item identities remain distinguishable after hashing");
        var updatedOldTask=Task("inProgress",start-10_000,item);
        check(CodexDesktopState.Evaluate([completed,updatedOldTask],start+500,start).State=="thinking","actual new item activity can make a previously started turn current");
        check(Evaluate(Task("inProgress",start+5000),start+500).State=="unknown","future activity timestamps cannot confirm work");
    }
}
