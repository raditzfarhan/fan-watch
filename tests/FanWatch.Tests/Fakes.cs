namespace FanWatch.Tests;

internal static class Fakes
{
    public static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    public const int Cores = 20;

    public static ProcSample P(int pid, string name, int parent = 0, double ageMin = 60, double cpuMin = 0, long memMb = 50) =>
        new(pid, parent, name, Now - TimeSpan.FromMinutes(ageMin), TimeSpan.FromMinutes(cpuMin), memMb * 1024 * 1024);
}
