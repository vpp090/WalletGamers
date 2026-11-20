using Game.Api.Domain;

namespace System.Tests;

public sealed class ScriptedRandomSource : IRandomSource
{
    private readonly Queue<double> _queue = new();

    public int Remaining => _queue.Count;

    public void Enqueue(params double[] values)
    {
        foreach (var v in values) _queue.Enqueue(v);
    }

    public void Clear() => _queue.Clear();

    public double NextDouble() =>
        _queue.Count > 0
            ? _queue.Dequeue()
            : throw new InvalidOperationException("ScriptedRandomSource ran out of values (unexpected extra draw).");
}
