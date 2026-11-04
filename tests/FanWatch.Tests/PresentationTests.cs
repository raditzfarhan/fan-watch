using static FanWatch.Tests.Fakes;

namespace FanWatch.Tests;

public class PresentationTests
{
    private static Row Single(ProcSample s, string? hint = null, double? avg = null, double since = 0, TimeSpan? span = null) => new()
    {
        Label = s.Name, Kind = RowKind.Single, Root = s, Members = [s], Hint = hint, Avg60 = avg, SinceStart = since, AvgSpan = span,
    };

    [Theory]
    [InlineData(0.6, "36 s")]
    [InlineData(25, "25 m")]
    [InlineData(11 * 60 + 5, "11 h")]
    [InlineData(50 * 60, "2 d 2 h")]
    public void AgeIsShortAndRounded(double minutes, string expected) =>
        Assert.Equal(expected, Presentation.Age(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void ClaudeSessionSubtitleCountsProcessesAndAge()
    {
        var c = P(10, "claude", ageMin: 11 * 60);
        var row = new Row { Label = "claude (my-office)", Kind = RowKind.ClaudeSession, Root = c, Members = [c, P(11, "node", 10), P(12, "bash", 10)] };
        Assert.Equal("3 processes · started 11 h ago", Presentation.Subtitle(row, Now));
    }

    [Fact]
    public void GroupSubtitleAddsHint()
    {
        var a = P(1, "MsMpEng");
        var row = new Row { Label = "MsMpEng (2)", Kind = RowKind.Group, Root = a, Members = [a, P(2, "MsMpEng")], Hint = "Defender is scanning" };
        Assert.Equal("2 processes · Defender is scanning", Presentation.Subtitle(row, Now));
    }

    [Fact]
    public void SingleSubtitlePrefersHint() =>
        Assert.Equal("Defender is scanning", Presentation.Subtitle(Single(P(5, "MsMpEng"), hint: "Defender is scanning"), Now));

    [Fact]
    public void SingleSubtitleShowsAgeAndPid() =>
        Assert.Equal("started 25 m ago · PID 5", Presentation.Subtitle(Single(P(5, "explorer", ageMin: 25)), Now));

    [Fact]
    public void VerdictForLongRunnerSaysHowLong()
    {
        // 5% of a 20-core machine = one core, for 11 h.
        var row = Single(P(10, "claude", ageMin: 11 * 60), avg: 4.8, since: 5, span: TimeSpan.FromSeconds(60));
        Assert.Equal("Busy since it started 11 h ago, using about one full core", Presentation.VerdictDetail(row, Cores, Now));
    }

    [Fact]
    public void VerdictForRecentSpikeSaysLastMinute()
    {
        var row = Single(P(10, "node", ageMin: 2 * 60), avg: 20, since: 0.5, span: TimeSpan.FromSeconds(60));
        Assert.Equal("Busy for the last minute, using about 4 cores", Presentation.VerdictDetail(row, Cores, Now));
    }

    [Fact]
    public void VerdictFigureIsWhatMadeItTheCulprit()
    {
        var longRunner = Single(P(10, "claude", ageMin: 11 * 60), avg: 0.6, since: 5, span: TimeSpan.FromSeconds(60));
        Assert.Equal((5.0, "of your whole CPU since it started"), Presentation.VerdictFigure(longRunner, Now));
        var spike = Single(P(11, "node", ageMin: 120), avg: 20, since: 0.5, span: TimeSpan.FromSeconds(60));
        Assert.Equal((20.0, "of your whole CPU over the last minute"), Presentation.VerdictFigure(spike, Now));
    }

    [Theory]
    [InlineData(1.0, "1% of your CPU")]
    [InlineData(2.0, "about half a core")]
    [InlineData(3.5, "most of a core")]
    [InlineData(5.5, "about one full core")]
    [InlineData(12.4, "about 2 cores")]
    public void UsageInCores(double pct, string expected) => Assert.Equal(expected, Presentation.Usage(pct, Cores));

    [Fact]
    public void EndButtonTextPerKind()
    {
        var c = P(10, "claude");
        Assert.Equal("End session", Presentation.EndButton(new Row { Label = "claude (x)", Kind = RowKind.ClaudeSession, Root = c, Members = [c] }));
        var b = P(20, "brave");
        Assert.Equal("Close Brave", Presentation.EndButton(new Row { Label = "brave (3)", Kind = RowKind.Group, Root = b, Members = [b, b, b] }));
        var n = P(30, "node");
        Assert.Equal("End all 2", Presentation.EndButton(new Row { Label = "node (2)", Kind = RowKind.Group, Root = n, Members = [n, P(31, "node")] }));
        Assert.Equal("End explorer", Presentation.EndButton(Single(P(40, "explorer"))));
    }

    [Fact]
    public void SearchMatchesLabelHintAndChildren()
    {
        var b = P(1, "brave");
        var child = Single(P(2, "brave"), hint: "tab: YouTube");
        var group = new Row { Label = "brave (2)", Kind = RowKind.Group, Root = b, Members = [b], Children = [child] };
        Assert.True(Presentation.Matches(group, ""));
        Assert.True(Presentation.Matches(group, "BRA"));
        Assert.True(Presentation.Matches(group, "youtube"));
        Assert.False(Presentation.Matches(group, "node"));
    }

    [Fact]
    public void TrendKeepsLastValuesPerKey()
    {
        var t = new CpuTrend(capacity: 3);
        foreach (var v in new[] { 1.0, 2, 3, 4 }) t.Push("a", v);
        t.Push("b", 9);
        Assert.Equal([2.0, 3, 4], t.Of("a"));
        Assert.Empty(t.Of("missing"));
        t.Retain(["b"]);
        Assert.Empty(t.Of("a"));
        Assert.Equal([9.0], t.Of("b"));
    }
}
