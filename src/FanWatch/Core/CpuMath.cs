namespace FanWatch.Core;

/// <summary>CPU percentages, always as % of the whole machine (all logical cores), clamped to [0, 100].</summary>
public static class CpuMath
{
    public static double SinceStartPercent(TimeSpan cpu, TimeSpan age, int cores) => Percent(cpu, age, cores);

    public static double IntervalPercent(TimeSpan cpuDelta, TimeSpan wall, int cores) => Percent(cpuDelta, wall, cores);

    private static double Percent(TimeSpan cpu, TimeSpan wall, int cores)
    {
        if (wall <= TimeSpan.Zero || cores <= 0) return 0;
        return Math.Clamp(cpu.TotalSeconds / wall.TotalSeconds / cores * 100, 0, 100);
    }
}
