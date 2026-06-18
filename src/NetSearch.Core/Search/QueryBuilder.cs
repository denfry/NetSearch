using System.Globalization;

namespace NetSearch.Core.Search;

public static class QueryBuilder
{
    private static readonly char[] Separators = { ',', ';', ' ', '\t' };

    public static IReadOnlyList<string> ParseExtensions(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                  .Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
                  .Where(x => x.Length > 0)
                  .ToList();
    }

    public static long? ParseSize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToUpperInvariant();
        long mult = 1;
        foreach (var (suffix, m) in new[] { ("GB", 1024L * 1024 * 1024), ("MB", 1024L * 1024), ("KB", 1024L), ("B", 1L) })
        {
            if (s.EndsWith(suffix))
            {
                mult = m;
                s = s[..^suffix.Length].Trim();
                break;
            }
        }
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n * mult
            : null;
    }

    public static SearchQuery Build(
        string text, SearchMode mode,
        string minSize, string maxSize,
        DateTimeOffset? after, DateTimeOffset? before,
        string extensions, EntryKind kind)
    {
        return new SearchQuery
        {
            Text = text ?? "",
            Mode = mode,
            MinSize = ParseSize(minSize),
            MaxSize = ParseSize(maxSize),
            ModifiedAfterUnix = after?.ToUnixTimeSeconds(),
            ModifiedBeforeUnix = before?.ToUnixTimeSeconds(),
            Extensions = ParseExtensions(extensions),
            Kind = kind,
        };
    }
}
