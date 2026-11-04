namespace FanWatch.Tests;

public class ProtectionTests
{
    [Fact] public void SvchostProtected() => Assert.True(Protection.IsProtected("svchost", 1234));
    [Fact] public void CaseInsensitive() => Assert.True(Protection.IsProtected("MSMPENG", 1234));
    [Fact] public void MemoryCompressionProtected() => Assert.True(Protection.IsProtected("Memory Compression", 1234));
    [Fact] public void SelfProtected() => Assert.True(Protection.IsProtected("testhost", Environment.ProcessId));
    [Fact] public void BraveNotProtected() => Assert.False(Protection.IsProtected("brave", 1234));
    [Fact] public void DefenderHint() =>
        Assert.Equal("Defender is scanning files: often caused by builds or installs", Protection.HintFor("MsMpEng"));
    [Fact] public void NoHintForBrave() => Assert.Null(Protection.HintFor("brave"));
}
