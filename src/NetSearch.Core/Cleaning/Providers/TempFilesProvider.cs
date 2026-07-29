using System.IO;

namespace NetSearch.Core.Cleaning.Providers;

public sealed class TempFilesProvider : ICleanProvider
{
    private readonly IReadOnlyList<string> _roots;
    public TempFilesProvider(IReadOnlyList<string> roots) => _roots = roots;

    public static TempFilesProvider ForSystem(bool elevated)
    {
        var roots = new List<string> { Path.GetTempPath() };
        if (elevated)
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
        return new TempFilesProvider(roots);
    }

    public CleanCategory Category => new(
        "temp", "Временные файлы", "%TEMP% и системная папка Temp",
        NeedsAdmin: false, UsesRecycleBin: true, DefaultSelected: true);

    /// <summary>
    /// Minimum age before a top-level temp entry is considered safe to delete, mirroring
    /// Windows Disk Cleanup's multi-day threshold. Avoids sweeping up files/directories
    /// created moments ago, e.g. by an installer or process that is still mid-run.
    /// </summary>
    private static readonly TimeSpan MinAge = TimeSpan.FromHours(24);

    public IEnumerable<CleanItem> Scan(CancellationToken ct)
    {
        var cutoffUtc = DateTime.UtcNow - MinAge;
        foreach (var root in _roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(root); }
            catch { continue; }
            foreach (var path in entries)
            {
                ct.ThrowIfCancellationRequested();

                // %TEMP%\.net is where self-contained single-file .NET apps extract to at
                // runtime, including this app's own currently-running instance (if
                // published self-contained) and any other running .NET app's — never safe
                // to delete blindly.
                if (string.Equals(Path.GetFileName(path), ".net", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsRecentlyModified(path, cutoffUtc)) continue;

                if (CleanScanHelpers.TryDescribe(path) is { } item) yield return item;
            }
        }
    }

    private static bool IsRecentlyModified(string path, DateTime cutoffUtc)
    {
        try
        {
            if (Directory.Exists(path)) return Directory.GetLastWriteTimeUtc(path) >= cutoffUtc;
            var fi = new FileInfo(path);
            if (fi.Exists) return fi.LastWriteTimeUtc >= cutoffUtc;
        }
        catch { }
        return false;
    }
}
