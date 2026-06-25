using Xunit;
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Tests;

public class IndexManagerMftTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"mft_{Guid.NewGuid():N}.db");

    [Fact]
    public void UpdateRootWith_applies_an_arbitrary_enumeration_like_the_crawler()
    {
        using var store = new IndexStore(_db); store.Initialize();
        var id = store.UpsertRoot(@"C:\Data");
        var mgr = new IndexManager(store, () => new Indexing.Crawler(), () => 1);

        // Emit two entries through the same onBatch contract the crawler uses.
        int Enumerate(Action<IReadOnlyList<FileEntry>> onBatch, CancellationToken ct, IProgress<CrawlProgress>? p)
        {
            onBatch(new[]
            {
                FileEntry.FromComponents(id, "a.txt", @"C:\Data", false, 5, 10),
                FileEntry.FromComponents(id, "b.txt", @"C:\Data", false, 9, 20),
            });
            return 2;
        }

        var result = mgr.UpdateRootWith(id, @"C:\Data", Enumerate, CancellationToken.None, null);

        Assert.Equal(2, result.Added);
        Assert.Equal(2, store.LoadAll().Count);
    }

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
        foreach (var s in new[] { "-wal", "-shm" }) if (File.Exists(_db + s)) File.Delete(_db + s);
    }
}
