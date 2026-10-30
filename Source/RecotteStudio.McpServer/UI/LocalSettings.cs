using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecotteStudio.McpServer.UI;

internal static class LocalSettings
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecotteStudioMCP");
    private static readonly string TokenPath = Path.Combine(DirectoryPath, "access-token.dat");
    private static readonly string WorkspacePath = Path.Combine(DirectoryPath, "workspace.json");
    private static readonly string FfmpegPath = Path.Combine(DirectoryPath, "ffmpeg.json");
    private static readonly string HardwareEncodingPath = Path.Combine(DirectoryPath, "hardware-encoding.json");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RecotteStudioMCP HTTP token v1");

    public static string? LoadWorkspace()
    {
        if (!File.Exists(WorkspacePath)) return null;
        return JsonSerializer.Deserialize<string?>(File.ReadAllText(WorkspacePath));
    }

    public static void SaveWorkspace(string? path)
    {
        Directory.CreateDirectory(DirectoryPath);
        WriteAtomically(WorkspacePath, JsonSerializer.Serialize(path));
    }

    public static string? LoadFfmpegPath()
    {
        if (!File.Exists(FfmpegPath)) return null;
        return JsonSerializer.Deserialize<string?>(File.ReadAllText(FfmpegPath));
    }

    public static void SaveFfmpegPath(string? path)
    {
        Directory.CreateDirectory(DirectoryPath);
        WriteAtomically(FfmpegPath, JsonSerializer.Serialize(path));
    }

    /// <summary>Returns the saved hardware encoding setting; Auto when none is saved or the file is unreadable.</summary>
    public static HardwareEncoding LoadHardwareEncoding()
    {
        if (!File.Exists(HardwareEncodingPath)) return HardwareEncoding.Auto;
        try
        {
            string? name = JsonSerializer.Deserialize<string?>(File.ReadAllText(HardwareEncodingPath));
            return Enum.TryParse(name, true, out HardwareEncoding value) && Enum.IsDefined(value) ? value : HardwareEncoding.Auto;
        }
        catch (JsonException) { return HardwareEncoding.Auto; }
    }

    public static void SaveHardwareEncoding(HardwareEncoding value)
    {
        Directory.CreateDirectory(DirectoryPath);
        WriteAtomically(HardwareEncodingPath, JsonSerializer.Serialize(value.ToString()));
    }

    public static string LoadOrCreateToken()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (File.Exists(TokenPath))
        {
            byte[] encrypted = File.ReadAllBytes(TokenPath);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser));
        }
        return RegenerateToken();
    }

    public static string RegenerateToken()
    {
        Directory.CreateDirectory(DirectoryPath);
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
        string temporary = TokenPath + ".tmp";
        File.WriteAllBytes(temporary, encrypted);
        File.Move(temporary, TokenPath, true);
        return token;
    }

    private static void WriteAtomically(string path, string value)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, value);
        File.Move(temporary, path, true);
    }
}
