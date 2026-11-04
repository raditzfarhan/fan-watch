using System.Diagnostics;

namespace FanWatch.Tests;

public class NtProcessSourceTests
{
    [Fact]
    public void ReadsOwnProcess()
    {
        var me = new NtProcessSource().Read().Single(p => p.Pid == Environment.ProcessId);
        Assert.True(me.CpuTime > TimeSpan.Zero);
        Assert.True(me.PrivateMemory > 0);
        Assert.InRange(me.StartUtc, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddSeconds(1));
        Assert.True(me.ParentPid > 0);
        Assert.False(me.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MatchesDotNetProcessInfo()
    {
        var me = new NtProcessSource().Read().Single(p => p.Pid == Environment.ProcessId);
        var dn = Process.GetCurrentProcess();
        Assert.Equal(dn.ProcessName, me.Name, ignoreCase: true);
        // Private working set (Task Manager's "Memory" column): real, and smaller than the full working set.
        Assert.InRange(me.PrivateMemory, 1024 * 1024, dn.WorkingSet64 - 1);
        Assert.InRange((me.StartUtc - dn.StartTime.ToUniversalTime()).Duration().TotalSeconds, 0, 1);
    }

    [Fact]
    public void ReadsProtectedProcessWithoutThrowing()
    {
        var csrss = new NtProcessSource().Read().Where(p => p.Name.Equals("csrss", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(csrss);
        Assert.Contains(csrss, p => p.CpuTime > TimeSpan.Zero);
    }

    [Fact]
    public void ExcludesIdleButReportsItsCpu()
    {
        var src = new NtProcessSource();
        var all = src.Read();
        Assert.DoesNotContain(all, p => p.Pid == 0);
        Assert.True(src.IdleCpuTime > TimeSpan.Zero);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(5000)]
    public void TruncatedBufferStopsSafely(int length)
    {
        var src = new NtProcessSource();
        int full = src.Read().Count;
        var partial = src.ParseLastBuffer(length);
        Assert.InRange(partial.Count, 0, full);
    }

    [Fact]
    public void SecondReadIsCheap()
    {
        var src = new NtProcessSource();
        src.Read();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++) src.Read();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"100 reads took {sw.Elapsed}");
    }
}
