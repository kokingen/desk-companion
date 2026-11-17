namespace Max3.Agent;

/// <summary>Native connect cancellation, connection cancellation and scope cleanup share one owner.</summary>
internal sealed class DisposeOnce<T>(T value,Action<Exception>? failure=null) : IDisposable where T:IDisposable {
    private int disposed;
    public T Value=>value;
    public void Dispose(){
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        // Cancellation callbacks can execute on a timer thread: no cleanup failure may escape it.
        try{value.Dispose();}catch(Exception e){
            // Diagnostics must not turn a suppressed cleanup failure into a timer-thread crash.
            try{failure?.Invoke(e);}catch(Exception){}
        }
    }
}
