using static FanWatch.Tests.Fakes;

namespace FanWatch.Tests;

/// <summary>Regression: ending what looked like a Brave tab row ended Brave's main process and closed every window.</summary>
public class BrowserSafetyTests
{
    [Fact]
    public void MainBrowserProcessIsNotEndableFromItsRow() =>
        Assert.True(Chromium.IsWholeBrowser("brave", "browser"));

    [Theory]
    [InlineData("tab renderer")]
    [InlineData("GPU")]
    [InlineData("extension")]
    public void OtherBrowserProcessesStayEndable(string role) =>
        Assert.False(Chromium.IsWholeBrowser("brave", role));

    [Fact]
    public void NonBrowserMainIsUnaffected() =>
        Assert.False(Chromium.IsWholeBrowser("Code", null));

    [Fact]
    public void TabConfirmationNamesTheTabAndSaysBrowserSurvives() =>
        Assert.Equal("End the Brave tab \"My Office\" (PID 52812)? Only that tab stops: Brave shows \"Aw, Snap!\" in it and everything else keeps running.",
            Chromium.EndConfirmation("brave", 52812, "tab renderer", "tab: My Office"));

    [Fact]
    public void SeveralTabsInOneProcessAreCalledOut() =>
        Assert.Equal("End the Brave process for \"Gmail\" and 2 other tabs/pages (PID 7)? Those tabs stop: Brave shows \"Aw, Snap!\" in them and everything else keeps running.",
            Chromium.EndConfirmation("brave", 7, "tab renderer", "tab: Gmail + 2 more"));

    [Fact]
    public void UnnamedRendererConfirmation() =>
        Assert.Equal("End a Brave tab process (PID 53712)? Whichever tab uses it stops and shows \"Aw, Snap!\"; the rest of Brave keeps running.",
            Chromium.EndConfirmation("brave", 53712, "tab renderer", null));

    [Theory]
    [InlineData("GPU")]
    [InlineData("network")]
    public void ServiceConfirmationSaysItRestarts(string role) =>
        Assert.Equal($"End Brave's {role} process (PID 9)? Brave restarts it automatically; pages may flicker or reload.",
            Chromium.EndConfirmation("brave", 9, role, null));

    [Fact]
    public void NonBrowserHasNoSpecialConfirmation() =>
        Assert.Null(Chromium.EndConfirmation("php", 1, null, null));

    [Fact]
    public void BrowserGroupConfirmationSaysEverythingCloses()
    {
        var members = Enumerable.Range(1, 51).Select(i => P(i, "brave")).ToList();
        var row = new Row { Label = "brave (51)", Kind = RowKind.Group, Root = members[0], Members = members };
        Assert.Equal("Close Brave completely? This ends all 51 Brave processes: every window and every open tab.",
            new ProcessKiller().ConfirmText(row, _ => false));
    }
}
