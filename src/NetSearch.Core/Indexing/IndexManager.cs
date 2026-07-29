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

    public IndexResult RebuildRoot(int rootId, string rootPath, CancellationToken ct,
        IProgress<CrawlProgress>? progress = null)
    {
        _store.DeleteEntriesForRoot(rootId);
        var added = 0;
        var crawl = _crawlerFactory().Crawl(rootId, rootPath, batch =>
        {
            _store.BulkUpsert(batch);
            added += batch.Count;
        }, ct, progress);
        _store.SetRootIndexed(rootId, _clock());
        return new IndexResult(added, 0, 0, crawl.Skipped.Count);
    }

    public IndexResult UpdateRoot(int rootId, string rootPath, CancellationToken ct,
        IProgress<CrawlProgress>? progress = null)
        => UpdateRootWith(rootId, rootPath,
            (onBatch, token, prog) => _crawlerFactory().Crawl(rootId, rootPath, onBatch, token, prog).Count,
            ct, progress);

    public IndexResult UpdateRootWith(int rootId, string rootPath,
        Func<Action<IReadOnlyList<FileEntry>>, CancellationToken, IProgress<CrawlProgress>?, int> enumerate,
        CancellationToken ct, IProgress<CrawlProgress>? progress = null)
    {
        var existing = _store.LoadRootState(rootId).ToDictionary(
            entry => new EntryKey(entry.ParentPath, entry.Name),
            EntryKeyComparer.Instance);
        var added = 0;
        var updated = 0;
        using var scan = _store.BeginRootScan(rootId);
        enumerate(batch =>
        {
            foreach (var entry in batch)
            {
                if (!existing.Remove(new EntryKey(entry.ParentPath, entry.Name), out var old))
                    added++;
                else if (old.Size != entry.Size || old.Modified != entry.Modified
                         || old.IsDir != entry.IsDir || old.Frn != entry.Frn)
                    updated++;
            }
            scan.AddBatch(batch);
        }, ct, progress);
        ct.ThrowIfCancellationRequested();
        var removed = scan.Complete(existing.Values.Select(entry => entry.Id), _clock());
        return new IndexResult(added, updated, removed, 0);
    }

    private readonly record struct EntryKey(string ParentPath, string Name);

    private sealed class EntryKeyComparer : IEqualityComparer<EntryKey>
    {
        public static EntryKeyComparer Instance { get; } = new();

        public bool Equals(EntryKey x, EntryKey y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.ParentPath, y.ParentPath)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

        public int GetHashCode(EntryKey obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ParentPath),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }

    public void ApplyUsnDeltas(int rootId, IReadOnlyList<NetSearch.Core.Native.UsnChange> changes,
        Func<long, Models.FileEntry?> readEntryByFrn)
    {
        var frns = changes.Select(c => c.Frn).Distinct().ToList();
        // Old rows for every touched FRN go first (handles delete + rename-away cleanly).
        _store.RemoveByFrn(rootId, frns);

        var fresh = new List<Models.FileEntry>();
        foreach (var frn in frns)
        {
            var entry = readEntryByFrn(frn);
            if (entry is not null) fresh.Add(entry);
        }
        if (fresh.Count > 0) _store.BulkUpsert(fresh);
        _store.SetRootIndexed(rootId, _clock());
    }
}
