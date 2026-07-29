using System.Diagnostics;
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;
using NetSearch.Core.Storage;

var rowCount = args.Length > 0 && int.TryParse(args[0], out var parsed)
    ? Math.Max(10_000, parsed)
    : 100_000;
const int batchSize = 4096;

var legacy = Measure("legacy dictionaries + batch upsert", rowCount, LegacyUpdate);
var staged = Measure("atomic single-transaction diff", rowCount, StagedUpdate);

Console.WriteLine();
Console.WriteLine($"Speedup: {legacy.Elapsed.TotalMilliseconds / staged.Elapsed.TotalMilliseconds:F2}x");
Console.WriteLine($"Managed allocation reduction: {1 - (double)staged.Allocated / legacy.Allocated:P1}");
return;

static Measurement Measure(
    string name,
    int rows,
    Action<IndexStore, int, IReadOnlyList<IReadOnlyList<FileEntry>>> update)
{
    var db = Path.Combine(Path.GetTempPath(), $"netsearch_bench_{Guid.NewGuid():N}.db");
    try
    {
        using var store = new IndexStore(db);
        store.Initialize();
        var rootId = store.UpsertRoot(@"C:\Benchmark");
        var batches = MakeBatches(rootId, rows);
        foreach (var batch in batches) store.BulkUpsert(batch);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        update(store, rootId, batches);
        timer.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        var measurement = new Measurement(timer.Elapsed, allocated);
        Console.WriteLine(
            $"{name,-36} {rows / timer.Elapsed.TotalSeconds,12:N0} rows/s  " +
            $"{allocated / 1024d / 1024d,8:N1} MiB allocated");
        return measurement;
    }
    finally
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = db + suffix;
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }
}

static IReadOnlyList<IReadOnlyList<FileEntry>> MakeBatches(int rootId, int rows)
{
    var result = new List<IReadOnlyList<FileEntry>>((rows + batchSize - 1) / batchSize);
    for (var start = 0; start < rows; start += batchSize)
    {
        var count = Math.Min(batchSize, rows - start);
        var batch = new List<FileEntry>(count);
        for (var i = start; i < start + count; i++)
        {
            var parent = $@"C:\Benchmark\d{i / 1000:D4}";
            batch.Add(FileEntry.FromComponents(
                rootId, $"file-{i:D8}.dat", parent, false,
                size: i % 20 == 0 ? i + 1 : i,
                modifiedUnix: 1_700_000_000 + i));
        }
        result.Add(batch);
    }
    return result;
}

// This is the pre-0.3 IndexManager.UpdateRootWith algorithm retained only as a
// benchmark baseline: two managed hash tables plus an upsert transaction per batch.
static void LegacyUpdate(
    IndexStore store, int rootId, IReadOnlyList<IReadOnlyList<FileEntry>> batches)
{
    var existing = store.LoadByRoot(rootId)
        .ToDictionary(entry => entry.FullPath, StringComparer.OrdinalIgnoreCase);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var batch in batches)
    {
        foreach (var entry in batch) seen.Add(entry.FullPath);
        store.BulkUpsert(batch);
    }
    store.RemoveByIds(existing
        .Where(pair => !seen.Contains(pair.Key))
        .Select(pair => pair.Value.Id));
}

static void StagedUpdate(
    IndexStore store, int rootId, IReadOnlyList<IReadOnlyList<FileEntry>> batches)
{
    var manager = new IndexManager(store, () => new Crawler(), () => 123);
    manager.UpdateRootWith(rootId, @"C:\Benchmark", (emit, _, _) =>
    {
        foreach (var batch in batches) emit(batch);
        return batches.Sum(batch => batch.Count);
    }, CancellationToken.None);
}

readonly record struct Measurement(TimeSpan Elapsed, long Allocated);
