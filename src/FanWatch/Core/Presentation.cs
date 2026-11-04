namespace FanWatch.Core;

/// <summary>The words FanWatch shows for a row: subtitles, the "why your fan is loud" sentence, button labels.</summary>
public static class Presentation
{
    /// <summary>"36 s", "25 m", "11 h", "2 d 2 h".</summary>
    public static string Age(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h"
        : t.TotalHours >= 1 ? $"{(int)t.TotalHours} h"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} m"
        : $"{Math.Max(0, (int)Math.Round(t.TotalSeconds))} s";

    private static string Count(int n) => n == 1 ? "1 process" : $"{n} processes";

    /// <summary>The second, smaller line under a row's name.</summary>
    public static string Subtitle(Row r, DateTime nowUtc) => r.Kind switch
    {
        RowKind.ClaudeSession => $"{Count(r.Members.Count)} · started {Age(nowUtc - r.Root.StartUtc)} ago",
        RowKind.Group => r.Hint != null ? $"{Count(r.Members.Count)} · {r.Hint}" : Count(r.Members.Count),
        _ => r.Hint ?? $"started {Age(nowUtc - r.Root.StartUtc)} ago · PID {r.Root.Pid}",
    };

    /// <summary>
    /// Why this row is the culprit, mirroring <see cref="CulpritScorer"/>: either it has been busy since it started
    /// (a long runner) or it has been busy over the last minute.
    /// </summary>
    private static bool IsLongRunner(Row r, DateTime nowUtc)
    {
        double longRun = r.SinceStart * Math.Min(1, (nowUtc - r.Root.StartUtc).TotalMinutes / 10);
        double recent = (r.Avg60 ?? 0) * Math.Min(1, (r.AvgSpan?.TotalSeconds ?? 0) / 30);
        return longRun >= recent;
    }

    /// <summary>The number that made this row the culprit, and what it measures.</summary>
    public static (double Percent, string Caption) VerdictFigure(Row r, DateTime nowUtc) => IsLongRunner(r, nowUtc)
        ? (r.SinceStart, "of your whole CPU since it started")
        : (r.Avg60 ?? 0, "of your whole CPU over the last minute");

    public static string VerdictDetail(Row r, int cores, DateTime nowUtc)
    {
        var age = nowUtc - r.Root.StartUtc;
        if (IsLongRunner(r, nowUtc)) return $"Busy since it started {Age(age)} ago, using {Usage(r.SinceStart, cores)}";
        var span = r.AvgSpan ?? TimeSpan.Zero;
        string window = span.TotalSeconds >= 50 ? "minute" : $"{span.TotalSeconds:0} s";
        return $"Busy for the last {window}, using {Usage(r.Avg60 ?? 0, cores)}";
    }

    /// <summary>A share of the whole machine in words: "about one full core", "about 3 cores", "1% of your CPU".</summary>
    public static string Usage(double percentOfMachine, int cores)
    {
        double c = percentOfMachine * cores / 100;
        if (c >= 1.5) return $"about {Math.Round(c):0} cores";
        if (c >= 0.8) return "about one full core";
        if (c >= 0.65) return "most of a core";
        if (c >= 0.35) return "about half a core";
        return $"{percentOfMachine:0.#}% of your CPU";
    }

    public static string EndButton(Row r) => r.Kind switch
    {
        RowKind.ClaudeSession => "End session",
        RowKind.Server => "Stop",
        RowKind.Group when Chromium.Browsers.Contains(r.Root.Name) => $"Close {Chromium.DisplayName(r.Root.Name)}",
        RowKind.Group => $"End all {r.Members.Count}",
        _ => $"End {r.Label}",
    };

    /// <summary>Search box filter: the row's name, process name or hint, or any of its children's.</summary>
    public static bool Matches(Row r, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return true;
        bool Has(string? s) => s != null && s.Contains(query, StringComparison.OrdinalIgnoreCase);
        return Has(r.Label) || Has(r.Root.Name) || Has(r.Hint) || r.Children.Any(c => Matches(c, query));
    }
}

/// <summary>The last few "CPU now" values per row, for sparklines (30 values at a 2 s refresh = one minute).</summary>
public sealed class CpuTrend(int capacity = 30)
{
    private readonly Dictionary<string, Queue<double>> _values = new();

    public void Push(string key, double value)
    {
        if (!_values.TryGetValue(key, out var q)) _values[key] = q = new Queue<double>(capacity);
        q.Enqueue(value);
        while (q.Count > capacity) q.Dequeue();
    }

    public IReadOnlyList<double> Of(string key) => _values.TryGetValue(key, out var q) ? q.ToArray() : [];

    /// <summary>Forget rows that no longer exist.</summary>
    public void Retain(IEnumerable<string> keys)
    {
        var keep = keys.ToHashSet();
        foreach (var k in _values.Keys.Where(k => !keep.Contains(k)).ToList()) _values.Remove(k);
    }

    public void Clear() => _values.Clear();
}
