using System.Globalization;
using System.Reflection;
using RecotteStudio.McpServer;

namespace RecotteStudio.McpServer.Tests;

public sealed class AppVersionTests
{
    [Fact]
    public void BuildTimeIsEmbeddedInTheAssemblyRatherThanReadFromTheFile()
    {
        string? value = typeof(McpServerRegistration).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(item => item.Key == "BuildTime")?.Value;

        Assert.NotNull(value);
        // A cached build can legitimately be older than one day when tests run with --no-build.
        // Incremental builds regenerate this metadata in the project's SetBuildTimestamp target.
        Assert.True(DateTimeOffset.TryParseExact(value, "o", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out DateTimeOffset built));
        Assert.Equal(TimeSpan.Zero, built.Offset);
        Assert.InRange(built, DateTimeOffset.UnixEpoch, DateTimeOffset.UtcNow.AddMinutes(1));
    }
}
