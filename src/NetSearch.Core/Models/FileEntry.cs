namespace NetSearch.Core.Models;

public sealed record FileEntry
{
    public long Id { get; init; }
    public int RootId { get; init; }
    public required string Name { get; init; }
    public required string NameLower { get; init; }
    public required string ParentPath { get; init; }
    public bool IsDir { get; init; }
    public long Size { get; init; }
    public required string Ext { get; init; }
    public long Modified { get; init; }
    public long? Frn { get; init; }

    public string FullPath =>
        ParentPath.Length == 0 ? Name : Path.Combine(ParentPath, Name);

    /// <summary>
    /// Builds an entry from parts already known to the caller. The crawler uses this with the
    /// directory's own path as <paramref name="parentPath"/>, so every file in one directory
    /// shares a single parent string (instead of re-deriving and re-allocating an identical
    /// path per file) and no per-file path parsing is needed.
    /// </summary>
    public static FileEntry FromComponents(int rootId, string name, string parentPath, bool isDir, long size, long modifiedUnix, long? frn = null)
    {
        return new FileEntry
        {
            RootId = rootId,
            Name = name,
            NameLower = name.ToLowerInvariant(),
            ParentPath = parentPath,
            IsDir = isDir,
            Size = size,
            Ext = isDir ? "" : Path.GetExtension(name).TrimStart('.').ToLowerInvariant(),
            Modified = modifiedUnix,
            Frn = frn,
        };
    }

    public static FileEntry FromFileSystem(int rootId, string fullPath, bool isDir, long size, long modifiedUnix)
    {
        var trimmed = fullPath.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        var parent = Path.GetDirectoryName(trimmed) ?? "";
        return FromComponents(rootId, name, parent, isDir, size, modifiedUnix);
    }
}
