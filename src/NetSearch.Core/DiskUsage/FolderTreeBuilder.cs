using NetSearch.Core.Models;

namespace NetSearch.Core.DiskUsage;

/// <summary>
/// Folds a stream of <see cref="FileEntry"/> into an aggregated folder-size tree (WizTree-style).
/// Files add their bytes to the folder named by their <see cref="FileEntry.ParentPath"/>; Build()
/// sums each subtree and sorts children largest-first.
/// </summary>
public sealed class FolderTreeBuilder
{
    private readonly string _rootPath;
    private readonly FolderNode _root;
    private readonly Dictionary<string, FolderNode> _byPath =
        new(StringComparer.OrdinalIgnoreCase);

    public FolderTreeBuilder(string rootPath)
    {
        _rootPath = Trim(rootPath);
        _root = new FolderNode(LeafName(_rootPath), _rootPath);
        _byPath[_rootPath] = _root;
    }

    public void AddBatch(IReadOnlyList<FileEntry> batch)
    {
        foreach (var e in batch) Add(e);
    }

    public void Add(FileEntry e)
    {
        if (e.IsDir)
        {
            GetOrCreate(Trim(e.FullPath));
        }
        else
        {
            var folder = GetOrCreate(Trim(e.ParentPath));
            if (folder is null) return;
            folder.OwnFileBytes += e.Size;
            folder.OwnFileCount += 1;
        }
    }

    public FolderNode Build()
    {
        Sum(_root);
        return _root;
    }

    private (long size, long count) Sum(FolderNode node)
    {
        long size = node.OwnFileBytes, count = node.OwnFileCount;
        foreach (var child in node.MutableChildren)
        {
            var (cs, cc) = Sum(child);
            size += cs;
            count += cc;
        }
        node.SizeBytes = size;
        node.FileCount = count;
        node.MutableChildren.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return (size, count);
    }

    // Returns the node for fullPath, creating it and any missing ancestors up to the root.
    // Returns null if fullPath is not under the configured root.
    private FolderNode? GetOrCreate(string fullPath)
    {
        if (_byPath.TryGetValue(fullPath, out var existing)) return existing;
        if (!IsUnderRoot(fullPath)) return null;

        var parentPath = ParentOf(fullPath);
        var parent = parentPath is null ? _root : GetOrCreate(parentPath);
        if (parent is null) return null;

        var node = new FolderNode(LeafName(fullPath), fullPath) { Parent = parent };
        parent.MutableChildren.Add(node);
        _byPath[fullPath] = node;
        return node;
    }

    private bool IsUnderRoot(string path) =>
        path.Equals(_rootPath, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(_rootPath + "\\", StringComparison.OrdinalIgnoreCase);

    private string? ParentOf(string path)
    {
        if (path.Equals(_rootPath, StringComparison.OrdinalIgnoreCase)) return null;
        var idx = path.LastIndexOf('\\');
        if (idx < 0) return null;
        var parent = path[..idx];
        return parent.Length == 0 ? _rootPath : parent;
    }

    private static string Trim(string p) => p.TrimEnd('\\', '/');

    private static string LeafName(string path)
    {
        var idx = path.LastIndexOf('\\');
        return idx >= 0 && idx < path.Length - 1 ? path[(idx + 1)..] : path;
    }
}
