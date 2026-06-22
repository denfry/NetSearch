using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace NetSearch.Core.Native;

[SupportedOSPlatform("windows")]
public sealed class WindowsEnvironmentProbe : IEnvironmentProbe
{
    public bool IsWindowsElevated
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public bool TryGetVolume(string rootPath, out VolumeInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(rootPath) || rootPath.StartsWith(@"\\")) { info = new VolumeInfo("", false, true, '\0'); return true; }
        var root = Path.GetPathRoot(Path.GetFullPath(rootPath));
        if (string.IsNullOrEmpty(root)) return false;
        try
        {
            var di = new DriveInfo(root);
            info = new VolumeInfo(di.DriveFormat, di.DriveType == DriveType.Fixed, false, root[0]);
            return true;
        }
        catch { return false; }
    }
}
