namespace FanWatch.Core;

/// <summary>Turns raw samples into table rows: one row per Claude session (with its descendants), name groups for everything else.</summary>
public static class Grouper
{
    private const string ClaudeName = "claude";

    public static IReadOnlyList<Row> Build(IReadOnlyList<ProcSample> samples, CpuHistory history, ClaudeSessions claude,
        DateTime nowUtc, int cores)
    {
        var children = samples.Where(s => s.ParentPid != s.Pid).ToLookup(s => s.ParentPid);
        var claimed = new HashSet<ProcKey>();
        var rows = new List<Row>();

        foreach (var c in samples.Where(IsClaude))
        {
            var members = new List<ProcSample> { c };
            CollectDescendants(c, children, members);
            foreach (var m in members) claimed.Add(m.Key);

            var project = claude.ProjectFor(c.Pid, c.StartUtc);
            var label = project != null ? $"{ClaudeName} ({project})" : ClaudeName;
            var kids = members.Select(m => Single(m, m.Name, history, nowUtc, cores)).ToList();
            rows.Add(Combine(label, RowKind.ClaudeSession, c, members, kids, history, nowUtc, cores));
        }

        foreach (var g in samples.Where(s => !claimed.Contains(s.Key)).GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            var members = g.ToList();
            if (members.Count == 1)
            {
                rows.Add(Single(members[0], members[0].Name, history, nowUtc, cores));
                continue;
            }
            var kids = members.Select(m => Single(m, m.Name, history, nowUtc, cores)).ToList();
            rows.Add(Combine($"{members[0].Name} ({members.Count})", RowKind.Group, members[0], members, kids, history, nowUtc, cores));
        }

        return rows;
    }

    private static bool IsClaude(ProcSample s) => s.Name.Equals(ClaudeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Walks the parent-PID tree. A child only counts if it started after its parent (guards against PID reuse); nested claude sessions stay separate.</summary>
    private static void CollectDescendants(ProcSample parent, ILookup<int, ProcSample> children, List<ProcSample> into)
    {
        foreach (var child in children[parent.Pid])
        {
            if (child.StartUtc < parent.StartUtc || IsClaude(child) || into.Contains(child)) continue;
            into.Add(child);
            CollectDescendants(child, children, into);
        }
    }

    private static Row Single(ProcSample s, string label, CpuHistory history, DateTime nowUtc, int cores) => new()
    {
        Label = label,
        Kind = RowKind.Single,
        Root = s,
        Members = [s],
        CpuNow = history.Now(s.Key),
        Avg60 = history.Avg60(s.Key),
        AvgSpan = history.Span(s.Key),
        SinceStart = CpuMath.SinceStartPercent(s.CpuTime, nowUtc - s.StartUtc, cores),
        CpuTotal = s.CpuTime,
        Memory = s.PrivateMemory,
        Protected = !Protection.IsEndable(s),
        Hint = Protection.HintFor(s.Name),
    };

    private static Row Combine(string label, RowKind kind, ProcSample root, List<ProcSample> members, List<Row> kids,
        CpuHistory history, DateTime nowUtc, int cores) => new()
    {
        Label = label,
        Kind = kind,
        Root = root,
        Members = members,
        Children = kids,
        CpuNow = SumNullable(kids.Select(k => k.CpuNow)),
        Avg60 = SumNullable(kids.Select(k => k.Avg60)),
        SinceStart = kids.Sum(k => k.SinceStart),
        CpuTotal = TimeSpan.FromTicks(members.Sum(m => m.CpuTime.Ticks)),
        Memory = members.Sum(m => m.PrivateMemory),
        // A session is endable unless claude.exe itself is protected; protected helpers inside it are skipped on End.
        Protected = kind == RowKind.ClaudeSession ? !Protection.IsEndable(root) : kids.Any(k => k.Protected),
        Hint = Protection.HintFor(root.Name),
    };

    private static double? SumNullable(IEnumerable<double?> values)
    {
        double sum = 0;
        bool any = false;
        foreach (var v in values)
        {
            if (v is null) continue;
            any = true;
            sum += v.Value;
        }
        return any ? sum : null;
    }
}
