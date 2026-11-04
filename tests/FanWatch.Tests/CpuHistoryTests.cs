using static FanWatch.Tests.Fakes;

namespace FanWatch.Tests;

public class CpuHistoryTests
{
    private static ProcSample WithCpu(ProcSample p, TimeSpan cpu) => p with { CpuTime = cpu };

    [Fact]
    public void NullOnFirstUpdate()
    {
        var h = new CpuHistory(Cores);
        var p = P(1, "a");
        h.Update([p], Now);
        Assert.Null(h.Now(p.Key));
        Assert.Null(h.Avg60(p.Key));
    }

    [Fact]
    public void NowFromLastInterval()
    {
        var h = new CpuHistory(Cores);
        var p = P(1, "a");
        h.Update([WithCpu(p, TimeSpan.FromSeconds(10))], Now);
        h.Update([WithCpu(p, TimeSpan.FromSeconds(14))], Now.AddSeconds(2));
        Assert.Equal(10.0, h.Now(p.Key)!.Value, 3);
    }

    [Fact]
    public void Avg60UsesOnly60Seconds()
    {
        var h = new CpuHistory(Cores);
        var p = P(1, "a");
        var cpu = TimeSpan.Zero;
        for (int i = 0; i < 40; i++)
        {
            if (i > 0 && i <= 10) cpu += TimeSpan.FromSeconds(2);
            h.Update([WithCpu(p, cpu)], Now.AddSeconds(2 * i));
        }
        Assert.Equal(0, h.Avg60(p.Key)!.Value, 3);
    }

    [Fact]
    public void Avg60AveragesAvailableWindow()
    {
        var h = new CpuHistory(Cores);
        var p = P(1, "a");
        h.Update([WithCpu(p, TimeSpan.Zero)], Now);
        h.Update([WithCpu(p, TimeSpan.FromSeconds(4))], Now.AddSeconds(2));
        h.Update([WithCpu(p, TimeSpan.FromSeconds(4))], Now.AddSeconds(4));
        Assert.Equal(5.0, h.Avg60(p.Key)!.Value, 3);
    }

    [Fact]
    public void SpanCoversAvailableWindow()
    {
        var h = new CpuHistory(Cores);
        var p = P(1, "a");
        Assert.Null(h.Span(p.Key));
        h.Update([p], Now);
        h.Update([p], Now.AddSeconds(2));
        h.Update([p], Now.AddSeconds(4));
        Assert.Equal(TimeSpan.FromSeconds(4), h.Span(p.Key));
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        var h = new CpuHistory(Cores);
        var p = P(1, "a");
        h.Update([p], Now);
        h.Update([p], Now.AddSeconds(2));
        h.Clear();
        h.Update([p], Now.AddSeconds(4));
        Assert.Null(h.Now(p.Key));
    }

    [Fact]
    public void DropsExitedProcesses()
    {
        var h = new CpuHistory(Cores);
        var a = P(1, "a");
        var b = P(2, "b");
        h.Update([a, b], Now);
        h.Update([a, b], Now.AddSeconds(2));
        h.Update([b], Now.AddSeconds(4));
        Assert.Null(h.Now(a.Key));
        Assert.NotNull(h.Now(b.Key));
    }

    [Fact]
    public void PidReuseIsANewKey()
    {
        var h = new CpuHistory(Cores);
        var old = P(1, "a", ageMin: 60);
        var reused = P(1, "a", ageMin: 0.01);
        h.Update([old], Now);
        h.Update([reused], Now.AddSeconds(2));
        Assert.Null(h.Now(reused.Key));
    }
}
