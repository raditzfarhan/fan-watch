namespace FanWatch.Core;

/// <summary>Per-process CPU samples over the last 60 s, for "CPU now" and "CPU avg 60 s".</summary>
public sealed class CpuHistory(int cores)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
    private readonly Dictionary<ProcKey, Queue<(DateTime T, TimeSpan Cpu)>> _samples = new();
    private readonly Dictionary<ProcKey, (DateTime T, TimeSpan Cpu)> _previous = new();

    public void Update(IReadOnlyList<ProcSample> samples, DateTime nowUtc)
    {
        var seen = new HashSet<ProcKey>(samples.Count);
        foreach (var s in samples)
        {
            var key = s.Key;
            if (!seen.Add(key)) continue;
            if (!_samples.TryGetValue(key, out var q))
                _samples[key] = q = new Queue<(DateTime, TimeSpan)>();
            else
                _previous[key] = q.Last();

            q.Enqueue((nowUtc, s.CpuTime));
            // Keep only samples strictly newer than 60 s before this one.
            while (q.Count > 1 && q.Peek().T <= nowUtc - Window) q.Dequeue();
        }

        foreach (var gone in _samples.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _samples.Remove(gone);
            _previous.Remove(gone);
        }
    }

    /// <summary>Forget all samples (e.g. after the PC wakes from sleep, when intervals are meaningless).</summary>
    public void Clear()
    {
        _samples.Clear();
        _previous.Clear();
    }

    public double? Now(ProcKey k)
    {
        if (!_samples.TryGetValue(k, out var q) || q.Count < 2 || !_previous.TryGetValue(k, out var prev)) return null;
        var last = q.Last();
        return CpuMath.IntervalPercent(last.Cpu - prev.Cpu, last.T - prev.T, cores);
    }

    /// <summary>How much time <see cref="Avg60"/> actually covers (up to 60 s); null with fewer than 2 samples.</summary>
    public TimeSpan? Span(ProcKey k)
    {
        if (!_samples.TryGetValue(k, out var q) || q.Count < 2) return null;
        return q.Last().T - q.Peek().T;
    }

    public double? Avg60(ProcKey k)
    {
        if (!_samples.TryGetValue(k, out var q) || q.Count < 2) return null;
        var first = q.Peek();
        var last = q.Last();
        return CpuMath.IntervalPercent(last.Cpu - first.Cpu, last.T - first.T, cores);
    }
}
