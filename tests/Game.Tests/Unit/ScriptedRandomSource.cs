using Game.Api.Domain;

namespace Game.Tests.Unit;

public sealed class ScriptedRandomSource(params double[] values) : IRandomSource
{
    private readonly Queue<double> _queue = new(values);

    public int Remaining => _queue.Count;

    public double NextDouble() =>
        _queue.Count > 0
            ? _queue.Dequeue()
            : throw new InvalidOperationException("ScriptedRandomSource ran out of values (unexpected extra draw).");
}
