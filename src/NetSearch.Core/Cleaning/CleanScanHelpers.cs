using System.IO;

namespace NetSearch.Core.Cleaning;

public static class CleanScanHelpers
{
    /// <summary>Yields each immediate child (file or directory) of every existing root, with size.</summary>
    public static IEnumerable<CleanItem> TopLevelEntries(IEnumerable<string> roots, CancellationToken ct)
    {
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(root); }
            catch { continue; }
            foreach (var path in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (TryDescribe(path) is { } item) yield return item;
            }
        }
    }

    public static CleanItem? TryDescribe(string path)
    {
        try
        {
            if (Directory.Exists(path)) return new CleanItem(path, DirectorySize(path), true);
            var fi = new FileInfo(path);
            if (fi.Exists) return new CleanItem(path, fi.Length, false);
        }
        catch { }
        return null;
    }

    public static long DirectorySize(string dir)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return total;
    }
}
