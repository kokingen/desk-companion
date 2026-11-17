using Max3.Agent;
internal static class LatestWindowTests {
    internal static void Run(Action<bool,string> check){
        var window=new LatestFrameWindow(8);
        for(long sequence=1;sequence<=8;sequence++)window.Sent(sequence);
        check(!window.CanSend && window.Count==8,"pipelined flow admits at most eight real frames");
        check(window.Acknowledge(3)>=0 && window.Count==5,"cumulative ACK releases all covered credits");
        window.Sent(100);
        check(window.Count==6,"released credit accepts newest sequence without queuing superseded samples");
        check(window.Acknowledge(3) is null && window.Count==6,"duplicate ACK cannot create extra credits");
        var future=false;try{window.Acknowledge(101);}catch(InvalidDataException){future=true;}
        check(future,"ACK beyond highest transmitted snapshot is rejected");
        var unsent=false;try{window.Acknowledge(99);}catch(InvalidDataException){unsent=true;}
        check(unsent,"cumulative ACK must name an actually transmitted snapshot");
        window.Acknowledge(100);
        check(window.CanSend && window.Count==0 && window.Peak==8,"latest cumulative ACK drains bounded in-flight ledger");
    }
}
