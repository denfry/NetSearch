namespace NetSearch.Core.Native;

public static class IndexStrategySelector
{
    public static IndexBackend Select(string rootPath, IEnvironmentProbe probe)
    {
        if (!OperatingSystem.IsWindows() || !probe.IsWindowsElevated) return IndexBackend.Crawler;
        if (!probe.TryGetVolume(rootPath, out var v)) return IndexBackend.Crawler;
        if (v.IsUnc || !v.IsFixed) return IndexBackend.Crawler;
        return string.Equals(v.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase)
            ? IndexBackend.Mft : IndexBackend.Crawler;
    }
}
