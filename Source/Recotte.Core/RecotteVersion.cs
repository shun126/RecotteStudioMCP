namespace Recotte.Core;

/// <summary>Represents the Recotte Studio version stored in a project.</summary>
public readonly record struct RecotteVersion(int Major, int Minor, int Build, int Revision)
{
    /// <summary>Gets the initially supported Recotte Studio version.</summary>
    public static RecotteVersion SupportedVersion { get; } = new(1, 8, 5, 0);

    /// <summary>Parses a four-component Recotte Studio version.</summary>
    public static bool TryParse(string? value, out RecotteVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Split('.');
        if (parts.Length != 4 || !int.TryParse(parts[0], out int major) ||
            !int.TryParse(parts[1], out int minor) || !int.TryParse(parts[2], out int build) ||
            !int.TryParse(parts[3], out int revision) || major < 0 || minor < 0 || build < 0 || revision < 0)
        {
            return false;
        }

        version = new RecotteVersion(major, minor, build, revision);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Major}.{Minor}.{Build}.{Revision}";
}
