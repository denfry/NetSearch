using System.IO;

namespace NetSearch.Core.DiskUsage;

public readonly record struct DriveUsage(char Letter, string Label, string FileSystem, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public string RootPath => Letter + ":\\";
    public double UsedFraction => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;
}

public static class DriveProbe
{
    public static IReadOnlyList<DriveUsage> FixedDrives()
    {
        var list = new List<DriveUsage>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new DriveUsage(d.Name[0], string.IsNullOrEmpty(d.VolumeLabel) ? "Локальный диск" : d.VolumeLabel,
                    d.DriveFormat, d.TotalSize, d.AvailableFreeSpace));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }
}
