namespace FanWatch.Tests;

public class ProcessImageTests
{
    [Fact]
    public void FindsOwnExePath() =>
        Assert.Equal(Environment.ProcessPath, ProcessImage.PathOf(Environment.ProcessId), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void MissingProcessGivesNull() => Assert.Null(ProcessImage.PathOf(999_999));
}
