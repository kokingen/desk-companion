sealed class ProbeCleanup(Action action) : IDisposable {
    public void Dispose(){action();throw new NullReferenceException("Simulated non-idempotent library cleanup");}
}
