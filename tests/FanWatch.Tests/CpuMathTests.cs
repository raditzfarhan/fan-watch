namespace FanWatch.Tests;

public class CpuMathTests
{
    [Fact] public void OneCoreForHoursOnTwentyCores_IsFivePercent() =>
        Assert.Equal(5.0, CpuMath.SinceStartPercent(TimeSpan.FromHours(11), TimeSpan.FromHours(11), 20), 3);

    [Fact] public void ZeroAge_IsZero() =>
        Assert.Equal(0, CpuMath.SinceStartPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 20));

    [Fact] public void Interval_ClampsAt100() =>
        Assert.Equal(100, CpuMath.IntervalPercent(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(2), 20));

    [Fact] public void Interval_TwoCoresForTwoSeconds() =>
        Assert.Equal(10.0, CpuMath.IntervalPercent(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), 20), 3);
}
