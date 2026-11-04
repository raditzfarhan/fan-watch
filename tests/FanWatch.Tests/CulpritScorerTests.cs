using static FanWatch.Tests.Fakes;

namespace FanWatch.Tests;

public sealed class CulpritScorerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fanwatch-cs-" + Guid.NewGuid().ToString("N"));
    public CulpritScorerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    /// <summary>Builds rows with optional 60 s history: avgByPid gives each process's Avg60 in % of machine.</summary>
    private IReadOnlyList<Row> Rows(ProcSample[] s, Dictionary<int, double>? avgByPid = null)
    {
        var h = new CpuHistory(Cores);
        if (avgByPid != null)
        {
            h.Update(s, Now.AddSeconds(-60 + 0.001));
            var later = s.Select(p => p with
            {
                CpuTime = p.CpuTime + TimeSpan.FromSeconds((avgByPid.GetValueOrDefault(p.Pid) / 100) * Cores * 60),
            }).ToArray();
            h.Update(later, Now);
            s = later;
        }
        return Grouper.Build(s, h, new ClaudeSessions(_dir), Now, Cores);
    }

    private static Row Find(IReadOnlyList<Row> rows, int pid) =>
        rows.SelectMany(r => r.Kind == RowKind.Group ? r.Children : [r]).Single(r => r.Members.Any(m => m.Pid == pid));

    [Fact]
    public void PicksLongRunnerOnFirstRefresh()
    {
        var rows = Rows([P(10, "claude", ageMin: 11 * 60, cpuMin: 485), P(20, "mc-sustainability", ageMin: 600, cpuMin: 20)]);
        var c = CulpritScorer.Apply(rows, overallCpu: 14, Now);
        Assert.NotNull(c);
        Assert.Equal(10, c!.Root.Pid);
        Assert.Equal(CulpritMark.Primary, c.Mark);
    }

    [Fact]
    public void GroupSizeDoesNotWin()
    {
        var svchosts = Enumerable.Range(100, 95).Select(pid => P(pid, "svchost", ageMin: 600, cpuMin: 600 * 20 * 0.001)).ToList();
        var rows = Rows([.. svchosts, P(10, "claude", ageMin: 600, cpuMin: 600)]);
        Assert.Equal(10, CulpritScorer.Apply(rows, 20, Now)!.Root.Pid);
    }

    [Fact]
    public void TwoSecondSpikeLoses()
    {
        var rows = Rows([P(10, "claude", ageMin: 600, cpuMin: 600), P(30, "spike", ageMin: 2.0 / 60, cpuMin: 40.0 / 60)]);
        Assert.Equal(10, CulpritScorer.Apply(rows, 20, Now)!.Root.Pid);
    }

    [Fact]
    public void RecentHeavyProcessWins()
    {
        var rows = Rows([P(10, "claude", ageMin: 600, cpuMin: 600), P(30, "build", ageMin: 1, cpuMin: 0)],
            new() { [30] = 60 });
        Assert.Equal(30, CulpritScorer.Apply(rows, 70, Now)!.Root.Pid);
    }

    /// <summary>Two refreshes `spanSec` apart; process 30 burns `pct`% of the machine in between.</summary>
    private IReadOnlyList<Row> RowsWithSpan(ProcSample[] s, int pid, double pct, double spanSec)
    {
        var h = new CpuHistory(Cores);
        h.Update(s, Now.AddSeconds(-spanSec));
        var later = s.Select(p => p.Pid != pid ? p : p with { CpuTime = p.CpuTime + TimeSpan.FromSeconds(pct / 100 * Cores * spanSec) }).ToArray();
        h.Update(later, Now);
        return Grouper.Build(later, h, new ClaudeSessions(_dir), Now, Cores);
    }

    [Fact]
    public void SpikeInFirstTwoSecondsAfterOpeningLoses()
    {
        // FanWatch just opened: only 2 s of history. A long-running process bursts to 8 cores (40%) in that window.
        var rows = RowsWithSpan([P(10, "claude", ageMin: 11 * 60, cpuMin: 11 * 60 * 20 * 0.052), P(30, "brave", ageMin: 300, cpuMin: 1)],
            pid: 30, pct: 40, spanSec: 2);
        Assert.Equal(10, CulpritScorer.Apply(rows, 50, Now)!.Root.Pid);
    }

    [Fact]
    public void SustainedBurstWinsOnceWindowFills()
    {
        var rows = RowsWithSpan([P(10, "claude", ageMin: 11 * 60, cpuMin: 11 * 60 * 20 * 0.052), P(30, "brave", ageMin: 300, cpuMin: 1)],
            pid: 30, pct: 40, spanSec: 30);
        Assert.Equal(30, CulpritScorer.Apply(rows, 50, Now)!.Root.Pid);
    }

    [Fact]
    public void ChildSpikeDoesNotInflateSession()
    {
        // Session itself is quiet; a 2-second-old child at 100% must not make the session beat a steady 5% process.
        var rows = Rows([
            P(10, "claude", ageMin: 600, cpuMin: 0),
            P(11, "node", parent: 10, ageMin: 2.0 / 60, cpuMin: 40.0 / 60),
            P(20, "steady", ageMin: 600, cpuMin: 600)]);
        Assert.Equal(20, CulpritScorer.Apply(rows, 20, Now)!.Root.Pid);
    }

    [Fact]
    public void QuietMarksNothing()
    {
        var rows = Rows([P(10, "claude", ageMin: 600, cpuMin: 120), P(20, "brave", ageMin: 600, cpuMin: 60)]);
        Assert.Null(CulpritScorer.Apply(rows, 8, Now));
        Assert.All(rows, r => Assert.Equal(CulpritMark.None, r.Mark));
    }

    [Fact]
    public void BusyMachineStillPicksSomething()
    {
        var rows = Rows([P(10, "a", ageMin: 600, cpuMin: 120)]);
        Assert.NotNull(CulpritScorer.Apply(rows, 40, Now));
    }

    [Fact]
    public void ProtectedPrimaryGetsSecondary()
    {
        var rows = Rows([P(10, "MsMpEng", ageMin: 600, cpuMin: 600 * 20 * 0.2), P(20, "brave", ageMin: 600, cpuMin: 600 * 20 * 0.06)]);
        var c = CulpritScorer.Apply(rows, 40, Now);
        Assert.Equal(10, c!.Root.Pid);
        Assert.Equal(CulpritMark.Secondary, Find(rows, 20).Mark);
    }

    [Fact]
    public void WinnerInsideGroupMarksGroup()
    {
        var rows = Rows([
            P(1, "brave", ageMin: 600, cpuMin: 1),
            P(2, "brave", ageMin: 600, cpuMin: 600 * 20 * 0.1),
            P(3, "explorer", ageMin: 600, cpuMin: 5)]);
        var c = CulpritScorer.Apply(rows, 20, Now);
        Assert.Equal(2, c!.Root.Pid);
        Assert.Equal(CulpritMark.Primary, rows.Single(r => r.Kind == RowKind.Group).Mark);
    }
}
