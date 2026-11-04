namespace FanWatch.Tests;

public class ChromiumTests
{
    [Theory]
    [InlineData("brave", @"""C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe""", "browser")]
    [InlineData("brave", @"brave.exe --type=renderer --extension-process --renderer-client-id=7", "extension")]
    [InlineData("brave", @"brave.exe --type=renderer --renderer-client-id=12", "tab renderer")]
    [InlineData("Code", @"Code.exe --type=renderer --enable-sandbox", "window renderer")]
    [InlineData("brave", @"brave.exe --type=gpu-process --gpu-preferences=abc", "GPU")]
    [InlineData("brave", @"brave.exe --type=utility --utility-sub-type=network.mojom.NetworkService", "network")]
    [InlineData("brave", @"brave.exe --type=utility --utility-sub-type=audio.mojom.AudioService", "audio")]
    [InlineData("brave", @"brave.exe --type=utility --utility-sub-type=storage.mojom.StorageService", "storage")]
    [InlineData("brave", @"brave.exe --type=utility --utility-sub-type=video_capture.mojom.VideoCaptureService", "video capture")]
    [InlineData("brave", @"brave.exe --type=utility --utility-sub-type=on_device_model.mojom.OnDeviceModelService", "utility")]
    [InlineData("brave", @"brave.exe --type=crashpad-handler --database=x", "crash reporter")]
    public void RoleFromCommandLine(string name, string cmd, string expected) =>
        Assert.Equal(expected, Chromium.Role(name, cmd));

    [Fact]
    public void NonChromiumHasNoRole() => Assert.Null(Chromium.Role("php", "php -S 127.0.0.1:8000"));

    [Fact]
    public void NullCommandLineHasNoRole() => Assert.Null(Chromium.Role("brave", null));

    [Fact]
    public void InfoRowsWithSameTitleHaveDifferentKeys()
    {
        var root = Fakes.P(1, "brave");
        var a = new Row { Label = "🗂 TikTok", Kind = RowKind.Info, Root = root, Members = [], Ordinal = 0 };
        var b = new Row { Label = "🗂 TikTok", Kind = RowKind.Info, Root = root, Members = [], Ordinal = 1 };
        Assert.NotEqual(a.Key, b.Key);
    }
}
