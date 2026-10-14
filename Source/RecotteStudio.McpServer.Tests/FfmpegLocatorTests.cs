namespace RecotteStudio.McpServer.Tests;

public sealed class FfmpegLocatorTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"recotte-ffmpeg-{Guid.NewGuid():N}");

    public FfmpegLocatorTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void ConfiguredPathWinsOverSearchPath()
    {
        string configured = CreateFile("configured", "ffmpeg.exe");
        string searched = Path.GetDirectoryName(CreateFile("searched", "ffmpeg.exe"))!;

        FfmpegResolution resolution = new FfmpegLocator(configured, () => new[] { searched }).Resolve();

        Assert.Equal(configured, resolution.ExecutablePath);
        Assert.Equal(FfmpegSource.Configured, resolution.Source);
    }

    [Fact]
    public void WithoutConfigurationTheFirstSearchPathMatchIsUsed()
    {
        string empty = Directory.CreateDirectory(Path.Combine(root, "empty")).FullName;
        string first = CreateFile("first", "ffmpeg.exe");
        CreateFile("second", "ffmpeg.exe");

        FfmpegResolution resolution = new FfmpegLocator(null, () => new[] { "", "bad\0path", empty,
            Path.GetDirectoryName(first)!, Path.Combine(root, "second") }).Resolve();

        Assert.Equal(first, resolution.ExecutablePath);
        Assert.Equal(FfmpegSource.SearchPath, resolution.Source);
    }

    [Fact]
    public void NothingFoundIsReportedWithoutThrowing()
    {
        FfmpegResolution resolution = new FfmpegLocator(null, () => new[] { root }).Resolve();

        Assert.False(resolution.Found);
        Assert.Null(resolution.Source);
        Assert.NotNull(resolution.Message);
    }

    [Fact]
    public void AMissingConfiguredPathDoesNotFallBackToSearchPath()
    {
        string searched = Path.GetDirectoryName(CreateFile("searched", "ffmpeg.exe"))!;

        FfmpegResolution resolution = new FfmpegLocator(Path.Combine(root, "missing", "ffmpeg.exe"), () => new[] { searched }).Resolve();

        Assert.False(resolution.Found);
    }

    [Fact]
    public void ConfiguredPathMustBeAnExistingExe()
    {
        Assert.Null(FfmpegLocator.ValidateConfiguredPath(CreateFile("ok", "ffmpeg.exe")));
        Assert.NotNull(FfmpegLocator.ValidateConfiguredPath(CreateFile("text", "ffmpeg.txt")));
        Assert.NotNull(FfmpegLocator.ValidateConfiguredPath(Path.Combine(root, "missing.exe")));
        Assert.NotNull(FfmpegLocator.ValidateConfiguredPath("bad\0path.exe"));
    }

    [Fact]
    public void ConfiguredPathCanBeChangedWhileInUse()
    {
        string configured = CreateFile("configured", "ffmpeg.exe");
        FfmpegLocator locator = new(null, () => Array.Empty<string>());
        Assert.False(locator.Resolve().Found);

        locator.ConfiguredPath = configured;

        Assert.Equal(configured, locator.Resolve().ExecutablePath);
    }

    [Fact]
    public async Task ProbeRejectsAnExecutableThatIsNotFfmpeg()
    {
        string whoami = Path.Combine(Environment.SystemDirectory, "whoami.exe");

        FfmpegProbeResult result = await FfmpegLocator.ProbeAsync(whoami);

        Assert.False(result.Success);
        Assert.Null(result.Version);
    }

    [Fact]
    public async Task ProbeReportsAFileThatCannotStart()
    {
        FfmpegProbeResult result = await FfmpegLocator.ProbeAsync(CreateFile("broken", "ffmpeg.exe"));

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task ProbeAcceptsTheInstalledFfmpeg()
    {
        if (InstalledFfmpeg.Find() is not string installed) return; // ffmpeg is optional and absent on CI.

        FfmpegProbeResult result = await FfmpegLocator.ProbeAsync(installed);

        Assert.True(result.Success, result.Message);
        Assert.False(string.IsNullOrWhiteSpace(result.Version));
    }

    private string CreateFile(string directory, string name)
    {
        string path = Path.Combine(Directory.CreateDirectory(Path.Combine(root, directory)).FullName, name);
        File.WriteAllText(path, "not an executable");
        return path;
    }
}
