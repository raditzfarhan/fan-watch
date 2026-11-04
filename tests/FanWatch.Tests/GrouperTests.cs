using static FanWatch.Tests.Fakes;

namespace FanWatch.Tests;

public sealed class GrouperTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fanwatch-grp-" + Guid.NewGuid().ToString("N"));
    public GrouperTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private IReadOnlyList<Row> Build(params ProcSample[] s) =>
        Grouper.Build(s, new CpuHistory(Cores), new ClaudeSessions(_dir), Now, Cores);

    [Fact]
    public void GroupsByName()
    {
        var rows = Build(P(1, "brave", memMb: 100), P(2, "brave", memMb: 200), P(3, "Brave", memMb: 300));
        var g = Assert.Single(rows);
        Assert.Equal(RowKind.Group, g.Kind);
        Assert.Equal("brave (3)", g.Label);
        Assert.Equal(600L * 1024 * 1024, g.Memory);
        Assert.Equal(3, g.Children.Count);
    }

    [Fact]
    public void SingleNotGrouped()
    {
        var r = Assert.Single(Build(P(1, "explorer")));
        Assert.Equal(RowKind.Single, r.Kind);
        Assert.Equal("explorer", r.Label);
        Assert.Empty(r.Children);
    }

    [Fact]
    public void SumsSinceStartAcrossMembers()
    {
        // Each member: 1 core for its whole life on 20 cores = 5%.
        var g = Assert.Single(Build(P(1, "brave", ageMin: 60, cpuMin: 60), P(2, "brave", ageMin: 30, cpuMin: 30)));
        Assert.Equal(10.0, g.SinceStart, 3);
        Assert.Equal(TimeSpan.FromMinutes(90), g.CpuTotal);
    }

    [Fact]
    public void ClaudeAbsorbsDescendants()
    {
        var rows = Build(
            P(10, "claude", ageMin: 60),
            P(11, "node", parent: 10, ageMin: 50),
            P(12, "node", parent: 11, ageMin: 40),
            P(13, "node", parent: 99, ageMin: 30));
        var claude = rows.Single(r => r.Kind == RowKind.ClaudeSession);
        Assert.Equal(3, claude.Members.Count);
        Assert.Equal(3, claude.Children.Count);
        var node = rows.Single(r => r.Label.StartsWith("node"));
        Assert.Single(node.Members);
        Assert.Equal(13, node.Members[0].Pid);
    }

    [Fact]
    public void ChildStartedBeforeParentIsNotAttributed()
    {
        // PID 10 was reused by a newer claude; node 11's real parent died long ago.
        var rows = Build(P(10, "claude", ageMin: 5), P(11, "node", parent: 10, ageMin: 60));
        var claude = rows.Single(r => r.Kind == RowKind.ClaudeSession);
        Assert.Single(claude.Members);
        Assert.Contains(rows, r => r.Label == "node");
    }

    [Fact]
    public void TwoClaudesAreSeparateRows()
    {
        var rows = Build(P(10, "claude"), P(20, "claude"), P(21, "claude", parent: 20, ageMin: 30));
        Assert.Equal(3, rows.Count(r => r.Kind == RowKind.ClaudeSession));
    }

    [Fact]
    public void ClaudeLabelUsesProject()
    {
        var p = P(10, "claude");
        File.WriteAllText(Path.Combine(_dir, "10.json"),
            $$"""{"cwd":"C:\\laragon\\www\\my-office","procStart":"{{p.StartUtc.ToFileTimeUtc()}}"}""");
        Assert.Equal("claude (my-office)", Assert.Single(Build(p)).Label);
    }

    [Fact]
    public void ProtectedGroupFlagged()
    {
        var g = Assert.Single(Build(P(1, "svchost"), P(2, "svchost")));
        Assert.True(g.Protected);
        Assert.Equal("Windows service (often Windows Update)", g.Hint);
    }

    [Fact]
    public void ClaudeWithProtectedChildIsStillEndable()
    {
        // e.g. FanWatch itself (or conhost-like protected helpers) launched from inside a Claude session.
        var rows = Build(P(10, "claude"), P(11, "svchost", parent: 10, ageMin: 30));
        Assert.False(rows.Single(r => r.Kind == RowKind.ClaudeSession).Protected);
    }

    [Fact]
    public void GroupKeyStableWhenCountChanges()
    {
        var a = Assert.Single(Build(P(1, "brave"), P(2, "brave")));
        var b = Assert.Single(Build(P(1, "brave"), P(2, "brave"), P(3, "Brave")));
        Assert.Equal(a.Key, b.Key);
    }

    [Fact]
    public void TwoClaudesInSameFolderHaveDifferentKeys()
    {
        var rows = Build(P(10, "claude"), P(20, "claude"));
        Assert.NotEqual(rows[0].Key, rows[1].Key);
    }

    [Fact]
    public void SystemSessionProcessIsProtected()
    {
        // Session 0 = services / SYSTEM: a normal user can never end these.
        var r = Assert.Single(Build(P(1, "SearchIndexer") with { SessionId = 0 }));
        Assert.True(r.Protected);
    }

    [Fact]
    public void UserSessionProcessIsNotProtected() =>
        Assert.False(Assert.Single(Build(P(1, "notepad") with { SessionId = 1 })).Protected);

    [Fact]
    public void CpuNowNullWhenNoHistory()
    {
        var g = Assert.Single(Build(P(1, "brave"), P(2, "brave")));
        Assert.Null(g.CpuNow);
        Assert.Null(g.Avg60);
    }
}
