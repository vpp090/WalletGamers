namespace System.Tests;

// Wall clock that can be advanced by tests. Timers and timestamps stay real, because Polly's resilience
// pipeline resolves TimeProvider from DI and a frozen fake clock would never fire its retry delays.
public sealed class OffsetTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);

    public override DateTimeOffset GetUtcNow() =>
        base.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
}
