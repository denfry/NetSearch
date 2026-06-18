using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using NetSearch.Core.Models;

namespace NetSearch.Core.Search;

public sealed record ContentSearchOptions(long MaxFileBytes, IReadOnlyList<string> TextExtensions, int MaxParallelism);

public sealed record ContentMatch(FileEntry Entry, int LineNumber, string LineText);

public sealed class ContentSearcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private readonly ContentSearchOptions _options;

    public ContentSearcher(ContentSearchOptions options) => _options = options;

    public async Task<IReadOnlyList<ContentMatch>> SearchAsync(
        IReadOnlyList<FileEntry> entries, string text, bool useRegex,
        IProgress<int>? progress, CancellationToken ct)
    {
        var allowedExt = new HashSet<string>(
            _options.TextExtensions.Select(x => x.Trim().TrimStart('.').ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        var candidates = entries.Where(e =>
            !e.IsDir &&
            e.Size <= _options.MaxFileBytes &&
            (allowedExt.Count == 0 || allowedExt.Contains(e.Ext))).ToList();

        Regex? re = null;
        if (useRegex)
        {
            try { re = new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout); }
            catch (ArgumentException) { return Array.Empty<ContentMatch>(); }
        }

        var results = new ConcurrentBag<ContentMatch>();
        var scanned = 0;

        await Parallel.ForEachAsync(
            candidates,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.MaxParallelism), CancellationToken = ct },
            async (entry, token) =>
            {
                var match = await ScanFileAsync(entry, text, re, token).ConfigureAwait(false);
                if (match is not null) results.Add(match);
                progress?.Report(Interlocked.Increment(ref scanned));
            }).ConfigureAwait(false);

        return results.ToList();
    }

    private static async Task<ContentMatch?> ScanFileAsync(FileEntry entry, string text, Regex? re, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(entry.FullPath);
            var lineNo = 0;
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                lineNo++;
                var hit = re is null
                    ? line.Contains(text, StringComparison.OrdinalIgnoreCase)
                    : SafeMatch(re, line);
                if (hit) return new ContentMatch(entry, lineNo, line.Trim());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* unreadable file: skip */ }
        return null;
    }

    private static bool SafeMatch(Regex re, string input)
    {
        try { return re.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
