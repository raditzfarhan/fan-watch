namespace FanWatch.Core;

public enum RowKind { Single, Group, ClaudeSession, Server, Info }

public enum CulpritMark { None, Primary, Secondary }

/// <summary>One line in the table: a single process, a name group, or a Claude session with its child processes.</summary>
public sealed class Row
{
    public required string Label { get; init; }
    /// <summary>Stable identity across refreshes (labels change when a group's count changes).</summary>
    public string Key => Kind switch
    {
        RowKind.Group => "g:" + Root.Name.ToLowerInvariant(),
        RowKind.Info => $"i:{Root.Pid}:{Ordinal}",
        _ => $"p:{Root.Pid}:{Root.StartUtc.Ticks}",
    };
    /// <summary>How much time Avg60 covers so far (null until two refreshes).</summary>
    public TimeSpan? AvgSpan { get; init; }
    /// <summary>Position among sibling info rows (tab titles can repeat, so they need their own identity).</summary>
    public int Ordinal { get; init; }
    public required RowKind Kind { get; init; }
    /// <summary>The process whose age drives scoring: the process itself, or the claude.exe of a session. First member for groups.</summary>
    public required ProcSample Root { get; init; }
    public required IReadOnlyList<ProcSample> Members { get; init; }
    /// <summary>For <see cref="RowKind.Server"/>: the process actually listening on the port.</summary>
    public ProcSample? Listener { get; init; }
    public IReadOnlyList<Row> Children { get; init; } = [];
    public double? CpuNow { get; init; }
    public double? Avg60 { get; init; }
    public double SinceStart { get; init; }
    public TimeSpan CpuTotal { get; init; }
    public long Memory { get; init; }
    public bool Protected { get; init; }
    public string? Hint { get; init; }

    // Set by CulpritScorer.
    public double Score { get; set; }
    public CulpritMark Mark { get; set; }
}
