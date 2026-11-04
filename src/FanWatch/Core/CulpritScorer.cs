namespace FanWatch.Core;

/// <summary>
/// Picks the process most likely making the fan loud.
/// Units scored: each Claude session (its process tree) and each individual process elsewhere, never a whole name group,
/// so many small processes can't outvote one busy one.
/// Per process: score = max(SinceStart × min(1, age / 10 min), Avg60 × min(1, window covered / 30 s)).
/// A session's score is the sum of its members'.
/// </summary>
public static class CulpritScorer
{
    public const double QuietOverall = 15, QuietProcess = 3;
    private static readonly TimeSpan FullWeightAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FullAvgSpan = TimeSpan.FromSeconds(30);

    public static Row? Apply(IReadOnlyList<Row> rows, double overallCpu, DateTime nowUtc)
    {
        var units = new List<(Row Unit, Row? Group)>();
        foreach (var r in rows)
        {
            r.Mark = CulpritMark.None;
            foreach (var c in r.Children) { c.Mark = CulpritMark.None; c.Score = ProcessScore(c, nowUtc); }

            switch (r.Kind)
            {
                case RowKind.ClaudeSession:
                    r.Score = r.Children.Sum(c => c.Score);
                    units.Add((r, null));
                    break;
                case RowKind.Group:
                    r.Score = r.Children.Count == 0 ? 0 : r.Children.Max(c => c.Score);
                    units.AddRange(r.Children.Select(c => (c, (Row?)r)));
                    break;
                default:
                    r.Score = ProcessScore(r, nowUtc);
                    units.Add((r, null));
                    break;
            }
        }

        if (units.Count == 0) return null;
        var ranked = units.OrderByDescending(u => u.Unit.Score).ToList();
        var top = ranked[0];
        if (overallCpu < QuietOverall && top.Unit.Score < QuietProcess) return null;

        Mark(top, CulpritMark.Primary);
        if (top.Unit.Protected)
        {
            var alt = ranked.Skip(1).FirstOrDefault(u => !u.Unit.Protected && u.Unit.Score >= QuietProcess);
            if (alt.Unit != null) Mark(alt, CulpritMark.Secondary);
        }
        return top.Unit;
    }

    private static void Mark((Row Unit, Row? Group) u, CulpritMark mark)
    {
        u.Unit.Mark = mark;
        if (u.Group != null && u.Group.Mark == CulpritMark.None) u.Group.Mark = mark;
    }

    private static double ProcessScore(Row r, DateTime nowUtc)
    {
        var age = nowUtc - r.Root.StartUtc;
        double weight = Math.Clamp(age / FullWeightAge, 0, 1);
        // A 60 s average built from only a few seconds is a spike, not sustained load: weight it by how much window it covers.
        double avgWeight = r.AvgSpan is TimeSpan span ? Math.Clamp(span / FullAvgSpan, 0, 1) : 0;
        return Math.Max(r.SinceStart * weight, (r.Avg60 ?? 0) * avgWeight);
    }
}
