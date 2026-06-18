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

    public string FullPath =>
        ParentPath.Length == 0 ? Name : Path.Combine(ParentPath, Name);

    public static FileEntry FromFileSystem(int rootId, string fullPath, bool isDir, long size, long modifiedUnix)
    {
        var name = Path.GetFileName(fullPath.TrimEnd('\\', '/'));
        var parent = Path.GetDirectoryName(fullPath.TrimEnd('\\', '/')) ?? "";
        var ext = isDir ? "" : Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        return new FileEntry
        {
            RootId = rootId,
            Name = name,
            NameLower = name.ToLowerInvariant(),
            ParentPath = parent,
            IsDir = isDir,
            Size = size,
            Ext = ext,
            Modified = modifiedUnix,
        };
    }
}
