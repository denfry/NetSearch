using Xunit;
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;
using NetSearch.Core.Native;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Tests;

public class IndexManagerUsnTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"usnmgr_{Guid.NewGuid():N}.db");

    [Fact]
    public void Apply_inserts_created_updates_changed_and_removes_deleted()
    {
        using var store = new IndexStore(_db); store.Initialize();
        var id = store.UpsertRoot(@"C:\Data");
        store.BulkUpsert(new[]
        {
            FileEntry.FromComponents(id, "old.txt", @"C:\Data", false, 1, 1, frn: 10),
            FileEntry.FromComponents(id, "stay.txt", @"C:\Data", false, 1, 1, frn: 11),
        });
        var mgr = new IndexManager(store, () => new Crawler(), () => 1);

        var changes = new[]
        {
            new UsnChange(10, 5, "old.txt", UsnReason.FileDelete),        // delete frn 10
            new UsnChange(20, 5, "new.txt", UsnReason.FileCreate),        // create frn 20
        };
        // Metadata re-reader: frn 20 now exists; frn 10 is gone.
        FileEntry? Read(long frn) => frn == 20
            ? FileEntry.FromComponents(id, "new.txt", @"C:\Data", false, 7, 9, frn: 20)
            : null;

        mgr.ApplyUsnDeltas(id, changes, Read);

        var names = store.LoadAll().Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "new.txt", "stay.txt" }, names);
    }

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
        foreach (var x in new[] { "-wal", "-shm" }) if (File.Exists(_db + x)) File.Delete(_db + x);
    }
}
