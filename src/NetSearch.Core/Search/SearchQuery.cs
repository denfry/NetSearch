namespace NetSearch.Core.Search;

public enum SearchMode { Substring, Wildcard, Regex }
public enum EntryKind { All, FilesOnly, FoldersOnly }

public sealed record SearchQuery
{
    public string Text { get; init; } = "";
    public SearchMode Mode { get; init; } = SearchMode.Substring;
    public long? MinSize { get; init; }
    public long? MaxSize { get; init; }
    public long? ModifiedAfterUnix { get; init; }
    public long? ModifiedBeforeUnix { get; init; }
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
    public EntryKind Kind { get; init; } = EntryKind.All;
}
