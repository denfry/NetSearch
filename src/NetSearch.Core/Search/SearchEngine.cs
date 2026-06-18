using System.Text.RegularExpressions;
using NetSearch.Core.Models;

namespace NetSearch.Core.Search;

public static class SearchEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

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

        var result = new List<FileEntry>();
        foreach (var e in source)
        {
            if (query.Kind == EntryKind.FilesOnly && e.IsDir) continue;
            if (query.Kind == EntryKind.FoldersOnly && !e.IsDir) continue;
            if (query.MinSize is { } min && e.Size < min) continue;
            if (query.MaxSize is { } max && e.Size > max) continue;
            if (query.ModifiedAfterUnix is { } after && e.Modified < after) continue;
            if (query.ModifiedBeforeUnix is { } before && e.Modified > before) continue;
            if (exts.Count > 0 && !exts.Contains(e.Ext)) continue;
            if (!nameMatch(e)) continue;
            result.Add(e);
        }
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
