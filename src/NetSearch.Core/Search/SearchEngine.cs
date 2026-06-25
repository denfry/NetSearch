using System.Text.RegularExpressions;
using NetSearch.Core.Models;

namespace NetSearch.Core.Search;

public static class SearchEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Above this many entries the linear O(N) filter is worth spreading across cores; below it
    // the PLINQ partitioning overhead dominates. AsOrdered keeps results in index order so the
    // displayed list stays stable between runs.
    private const int ParallelThreshold = 20_000;

    public static IReadOnlyList<FileEntry> Search(IReadOnlyList<FileEntry> source, SearchQuery query)
    {
        Func<FileEntry, bool> nameMatch;
        try
        {
            nameMatch = BuildNameMatcher(query);
        }
        catch (ArgumentException)
        {
            return Array.Empty<FileEntry>(); // invalid regex
        }

        var exts = new HashSet<string>(
            query.Extensions.Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
                            .Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        bool Matches(FileEntry e)
        {
            if (query.Kind == EntryKind.FilesOnly && e.IsDir) return false;
            if (query.Kind == EntryKind.FoldersOnly && !e.IsDir) return false;
            if (query.MinSize is { } min && e.Size < min) return false;
            if (query.MaxSize is { } max && e.Size > max) return false;
            if (query.ModifiedAfterUnix is { } after && e.Modified < after) return false;
            if (query.ModifiedBeforeUnix is { } before && e.Modified > before) return false;
            if (exts.Count > 0 && !exts.Contains(e.Ext)) return false;
            if (!nameMatch(e)) return false;
            return true;
        }

        // Regex instances are thread-safe for matching, and `exts` is only read here, so the
        // predicate is safe to evaluate in parallel.
        if (source.Count >= ParallelThreshold)
            return source.AsParallel().AsOrdered().Where(Matches).ToList();

        var result = new List<FileEntry>(Math.Min(source.Count, 1024));
        foreach (var e in source)
            if (Matches(e)) result.Add(e);
        return result;
    }

    private static Func<FileEntry, bool> BuildNameMatcher(SearchQuery query)
    {
        if (string.IsNullOrEmpty(query.Text))
            return _ => true;

        switch (query.Mode)
        {
            case SearchMode.Substring:
                var needle = query.Text.ToLowerInvariant();
                return e => e.NameLower.Contains(needle);

            case SearchMode.Wildcard:
                var wild = "^" + Regex.Escape(query.Text)
                    .Replace("\\*", ".*").Replace("\\?", ".") + "$";
                var wre = new Regex(wild, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                return e => SafeMatch(wre, e.Name);

            case SearchMode.Regex:
                var re = new Regex(query.Text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
                return e => SafeMatch(re, e.Name);

            default:
                return _ => true;
        }
    }

    private static bool SafeMatch(Regex re, string input)
    {
        try { return re.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
