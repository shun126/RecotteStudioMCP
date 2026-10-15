using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RecotteStudio.Core.Tests;

/// <summary>Denies the current user listing a directory until disposed, so it can be deleted afterwards.</summary>
[SupportedOSPlatform("windows")]
internal sealed class InaccessibleDirectory : IDisposable
{
    private readonly DirectoryInfo directory;
    private readonly FileSystemAccessRule rule = new(WindowsIdentity.GetCurrent().User!,
        FileSystemRights.ListDirectory | FileSystemRights.ReadData, AccessControlType.Deny);

    public InaccessibleDirectory(string path)
    {
        directory = Directory.CreateDirectory(path);
        DirectorySecurity security = directory.GetAccessControl();
        security.AddAccessRule(rule);
        directory.SetAccessControl(security);
    }

    /// <summary>Gets whether the denial took effect; a privileged account can still read the directory.</summary>
    public bool IsDenied
    {
        get
        {
            try { Directory.EnumerateFileSystemEntries(directory.FullName).Any(); return false; }
            catch (UnauthorizedAccessException) { return true; }
        }
    }

    public void Dispose()
    {
        DirectorySecurity security = directory.GetAccessControl();
        security.RemoveAccessRule(rule);
        directory.SetAccessControl(security);
    }
}
