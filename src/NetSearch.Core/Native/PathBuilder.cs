using System.IO;

namespace NetSearch.Core.Native;

public static class PathBuilder
{
    private const long RootRecord = 5;

    public static IReadOnlyDictionary<long, string> BuildDirectoryPaths(
        string volumeRoot, IReadOnlyDictionary<long, MftNode> nodes)
    {
        var result = new Dictionary<long, string>();
        var resolving = new HashSet<long>();

        string? Resolve(long rec)
        {
            if (rec == RootRecord) return volumeRoot + Path.DirectorySeparatorChar; // "C:\"
            if (result.TryGetValue(rec, out var cached)) return cached;
            if (!nodes.TryGetValue(rec, out var node) || !node.IsDir) return null;
            if (!resolving.Add(rec)) return null; // cycle

            var parentPath = Resolve(node.ParentRecordNumber);
            resolving.Remove(rec);
            if (parentPath is null) return null;

            var full = Path.Combine(parentPath, node.Name);
            result[rec] = full;
            return full;
        }

        foreach (var rec in nodes.Keys) Resolve(rec);
        result[RootRecord] = volumeRoot + Path.DirectorySeparatorChar;
        return result;
    }
}
