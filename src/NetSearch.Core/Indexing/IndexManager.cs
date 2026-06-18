using NetSearch.Core.Models;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Indexing;

public sealed record IndexResult(int Added, int Updated, int Removed, int Skipped);

public sealed class IndexManager
{
    private readonly IndexStore _store;
    private readonly Func<Crawler> _crawlerFactory;
    private readonly Func<long> _clock;

    public IndexManager(IndexStore store, Func<Crawler> crawlerFactory, Func<long>? clock = null)
    {
        _store = store;
        _crawlerFactory = crawlerFactory;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public IndexResult RebuildRoot(int rootId, string rootPath, CancellationToken ct)
    {
        _store.DeleteEntriesForRoot(rootId);
        var added = 0;
        var crawl = _crawlerFactory().Crawl(rootId, rootPath, batch =>
        {
            _store.BulkUpsert(batch);
            added += batch.Count;
        }, ct);
        _store.SetRootIndexed(rootId, _clock());
        return new IndexResult(added, 0, 0, crawl.Skipped.Count);
    }

    public IndexResult UpdateRoot(int rootId, string rootPath, CancellationToken ct)
    {
        var existing = _store.LoadByRoot(rootId)
            .ToDictionary(e => e.FullPath, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var updated = 0;

        var crawl = _crawlerFactory().Crawl(rootId, rootPath, batch =>
        {
            foreach (var e in batch)
            {
                seen.Add(e.FullPath);
                if (!existing.TryGetValue(e.FullPath, out var old))
                    added++;
                else if (old.Size != e.Size || old.Modified != e.Modified || old.IsDir != e.IsDir)
                    updated++;
            }
            _store.BulkUpsert(batch);
        }, ct);

        var removedIds = existing
            .Where(kv => !seen.Contains(kv.Key))
            .Select(kv => kv.Value.Id)
            .ToList();
        _store.RemoveByIds(removedIds);

        _store.SetRootIndexed(rootId, _clock());
        return new IndexResult(added, updated, removedIds.Count, crawl.Skipped.Count);
    }
}
