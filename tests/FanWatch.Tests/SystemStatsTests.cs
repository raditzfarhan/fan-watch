namespace FanWatch.Tests;

public class SystemStatsTests
{
    [Fact] public void ChargingWhenBatteryIsActuallyCharging() =>
        Assert.Equal("Charging 79%", SystemStats.PowerText(new BatteryState(79, PluggedIn: true, Charging: true)));

    [Fact] public void PluggedInButHeldAtLimitIsNotCharging() =>
        Assert.Equal("Plugged in 80%", SystemStats.PowerText(new BatteryState(80, PluggedIn: true, Charging: false)));

    [Fact] public void OnBattery() =>
        Assert.Equal("Battery 55%", SystemStats.PowerText(new BatteryState(55, PluggedIn: false, Charging: false)));

    [Fact] public void NoBatteryShowsNothing() =>
        Assert.Null(SystemStats.PowerText(null));
}
