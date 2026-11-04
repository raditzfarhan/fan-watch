using System.Runtime.InteropServices;

namespace FanWatch.Core;

/// <summary>Battery state. PluggedIn = on mains power; Charging = the battery is actually taking charge.</summary>
public sealed record BatteryState(int Percent, bool PluggedIn, bool Charging);

public sealed record SystemSnapshot(double CpuPercent, long RamUsedBytes, long RamTotalBytes, BatteryState? Battery);

public static class SystemStats
{
    public static SystemSnapshot Take(TimeSpan idleDelta, TimeSpan wall, int cores)
    {
        double cpu = wall > TimeSpan.Zero ? 100 - CpuMath.IntervalPercent(idleDelta, wall, cores) : 0;

        var mem = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref mem);
        long total = (long)mem.TotalPhys;
        long used = total - (long)mem.AvailPhys;

        var power = SystemInformation.PowerStatus;
        var status = power.BatteryChargeStatus;
        bool hasBattery = (status & BatteryChargeStatus.NoSystemBattery) == 0 && status != BatteryChargeStatus.Unknown;
        BatteryState? battery = hasBattery
            ? new BatteryState((int)Math.Round(power.BatteryLifePercent * 100),
                PluggedIn: power.PowerLineStatus == PowerLineStatus.Online,
                Charging: (status & BatteryChargeStatus.Charging) != 0)
            : null;

        return new SystemSnapshot(Math.Clamp(cpu, 0, 100), used, total, battery);
    }

    public static string? PowerText(BatteryState? b) => b switch
    {
        null => null,
        { Charging: true } => $"Charging {b.Percent}%",
        { PluggedIn: true } => $"Plugged in {b.Percent}%",
        _ => $"Battery {b.Percent}%",
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
