namespace NetSearch.Core.Cleaning;

public readonly record struct CleanItem(string Path, long SizeBytes, bool IsDir)
{
    /// <summary>Sentinel path meaning "empty the Recycle Bin" (handled specially by the executor).</summary>
    public const string RecycleBinSentinel = "::recyclebin::";
}
