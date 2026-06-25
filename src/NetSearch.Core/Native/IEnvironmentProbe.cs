namespace NetSearch.Core.Native;

public interface IEnvironmentProbe
{
    bool IsWindowsElevated { get; }
    bool TryGetVolume(string rootPath, out VolumeInfo info);
}
