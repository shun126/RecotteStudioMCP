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
        DateTimeOffset built = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Assert.Equal(TimeSpan.Zero, built.Offset);
        Assert.InRange(built, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }
}
