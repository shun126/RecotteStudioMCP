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
