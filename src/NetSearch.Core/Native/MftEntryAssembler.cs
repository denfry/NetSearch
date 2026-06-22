using NetSearch.Core.Models;

namespace NetSearch.Core.Native;

public static class MftEntryAssembler
{
    public static IEnumerable<FileEntry> Assemble(
        int rootId, string volumeRoot, string rootFilter,
        IReadOnlyDictionary<long, ParsedMftRecord> records)
    {
        var nodes = records.ToDictionary(
            kv => kv.Key, kv => new MftNode(kv.Value.Name, kv.Value.ParentRecordNumber, kv.Value.IsDir));
        var dirPaths = PathBuilder.BuildDirectoryPaths(volumeRoot, nodes);

        var sep = System.IO.Path.DirectorySeparatorChar;
        foreach (var (rec, r) in records)
        {
            if (rec == 5) continue; // the volume root itself is not an entry
            if (!dirPaths.TryGetValue(r.ParentRecordNumber, out var parentPath)) continue;

            var full = System.IO.Path.Combine(parentPath, r.Name);
            // Prefix match on a separator boundary so "C:\Me" does not capture "C:\MeToo".
            bool underRoot = full.Equals(rootFilter, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(rootFilter + sep, StringComparison.OrdinalIgnoreCase);
            if (!underRoot) continue;

            yield return FileEntry.FromComponents(rootId, r.Name, parentPath, r.IsDir, r.Size, r.ModifiedUnix);
        }
    }
}
