namespace FanWatch.Tests;

public sealed class ClaudeSessionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fanwatch-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 11, 3, 12, 16, 57, DateTimeKind.Utc);

    public ClaudeSessionsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private void WriteSession(int pid, string json) => File.WriteAllText(Path.Combine(_dir, $"{pid}.json"), json);

    private static string Session(DateTime start) =>
        $$"""{"pid":44380,"cwd":"C:\\laragon\\www\\my-office","procStart":"{{start.ToFileTimeUtc()}}","name":"my-office-e9"}""";

    [Fact]
    public void ReadsCwdLastSegment()
    {
        WriteSession(44380, Session(Start));
        Assert.Equal("my-office", new ClaudeSessions(_dir).ProjectFor(44380, Start));
    }

    [Fact]
    public void IgnoresFileWhenProcStartMismatch()
    {
        WriteSession(44380, Session(Start.AddHours(-1)));
        Assert.Null(new ClaudeSessions(_dir).ProjectFor(44380, Start));
    }

    [Fact]
    public void FileWithoutProcStartIsAccepted()
    {
        WriteSession(7, """{"cwd":"C:\\work\\api"}""");
        Assert.Equal("api", new ClaudeSessions(_dir).ProjectFor(7, Start));
    }

    [Fact]
    public void MissingFileIsNull() => Assert.Null(new ClaudeSessions(_dir).ProjectFor(1, Start));

    [Fact]
    public void MalformedJsonFallsBack()
    {
        WriteSession(5, "{not json");
        Assert.Null(new ClaudeSessions(_dir).ProjectFor(5, Start));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("42")]
    public void NonObjectRootFallsBack(string json)
    {
        WriteSession(6, json);
        Assert.Null(new ClaudeSessions(_dir).ProjectFor(6, Start));
    }

    [Fact]
    public void PruneForgetsExitedSessions()
    {
        WriteSession(44380, Session(Start));
        var cs = new ClaudeSessions(_dir);
        Assert.Equal("my-office", cs.ProjectFor(44380, Start));
        cs.Prune(new HashSet<ProcKey>());
        Assert.Equal(0, cs.CachedCount);
    }

    [Fact]
    public void MissingDirIsNull() =>
        Assert.Null(new ClaudeSessions(Path.Combine(_dir, "nope")).ProjectFor(1, Start));
}
