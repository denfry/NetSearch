namespace NetSearch.Core.DiskUsage;

public sealed class FolderNode
{
    private readonly List<FolderNode> _children = new();

    public FolderNode(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public string Name { get; }
    public string FullPath { get; }
    public FolderNode? Parent { get; internal set; }

    /// <summary>Aggregate size of this folder's whole subtree (logical bytes). Set by Build().</summary>
    public long SizeBytes { get; internal set; }
    /// <summary>Aggregate file count of this folder's whole subtree. Set by Build().</summary>
    public long FileCount { get; internal set; }

    public IReadOnlyList<FolderNode> Children => _children;
    internal List<FolderNode> MutableChildren => _children;

    // Bytes contributed by files directly in this folder (not subfolders); used during Build().
    internal long OwnFileBytes;
    internal long OwnFileCount;

    public string SizeText => SizeFormatter.Format(SizeBytes);

    public double FractionOfParent =>
        Parent is { SizeBytes: > 0 } p ? (double)SizeBytes / p.SizeBytes : 1.0;
}
