using Xunit;
using NetSearch.Core.Models;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Tests;

public class IndexStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"netsearch_{Guid.NewGuid():N}.db");

    private IndexStore NewStore()
    {
        var s = new IndexStore(_dbPath);
        s.Initialize();
        return s;
    }

    private static FileEntry Entry(int rootId, string fullPath, bool isDir = false, long size = 10, long mod = 100)
        => FileEntry.FromFileSystem(rootId, fullPath, isDir, size, mod);

    [Fact]
    public void UpsertRoot_is_idempotent_and_returns_stable_id()
    {
        using var s = NewStore();
        var id1 = s.UpsertRoot(@"\\srv\share");
        var id2 = s.UpsertRoot(@"\\srv\share");
        Assert.Equal(id1, id2);
        Assert.Single(s.GetRoots());
    }

    [Fact]
    public void BulkUpsert_then_LoadAll_roundtrips_entries()
    {
        using var s = NewStore();
        var root = s.UpsertRoot(@"C:\Data");
        s.BulkUpsert(new[]
        {
            Entry(root, @"C:\Data\a.txt", size: 5, mod: 111),
            Entry(root, @"C:\Data\sub", isDir: true, size: 0, mod: 222),
        });

        var all = s.LoadAll().OrderBy(e => e.Name).ToList();
        Assert.Equal(2, all.Count);
        Assert.Equal("a.txt", all[0].Name);
        Assert.True(all[0].Id > 0);
        Assert.Equal(5, all[0].Size);
        Assert.True(all[1].IsDir);
    }

    [Fact]
    public void BulkUpsert_updates_existing_entry_on_conflict()
    {
        using var s = NewStore();
        var root = s.UpsertRoot(@"C:\Data");
        s.BulkUpsert(new[] { Entry(root, @"C:\Data\a.txt", size: 5, mod: 111) });
        s.BulkUpsert(new[] { Entry(root, @"C:\Data\a.txt", size: 99, mod: 222) });

        var all = s.LoadAll();
        Assert.Single(all);
        Assert.Equal(99, all[0].Size);
        Assert.Equal(222, all[0].Modified);
    }

    [Fact]
    public void RemoveByIds_deletes_only_targeted_rows()
    {
        using var s = NewStore();
        var root = s.UpsertRoot(@"C:\Data");
        s.BulkUpsert(new[] { Entry(root, @"C:\Data\a.txt"), Entry(root, @"C:\Data\b.txt") });
        var all = s.LoadAll();
        var idA = all.First(e => e.Name == "a.txt").Id;

        s.RemoveByIds(new[] { idA });

        var remaining = s.LoadAll();
        Assert.Single(remaining);
        Assert.Equal("b.txt", remaining[0].Name);
    }

    [Fact]
    public void Data_survives_reopening_the_same_db_file()
    {
        var root = 0;
        using (var s = NewStore())
        {
            root = s.UpsertRoot(@"C:\Data");
            s.BulkUpsert(new[] { Entry(root, @"C:\Data\a.txt") });
        }
        using var s2 = new IndexStore(_dbPath);
        s2.Initialize();
        Assert.Single(s2.LoadAll());
    }

    [Fact]
    public void OpenWithRecovery_recreates_corrupt_db_and_is_usable()
    {
        // Write garbage bytes to simulate a corrupt database
        var corruptPath = Path.Combine(Path.GetTempPath(), $"netsearch_corrupt_{Guid.NewGuid():N}.db");
        File.WriteAllBytes(corruptPath, new byte[] { 0x00, 0xFF, 0xAB, 0xCD, 0x01, 0x02, 0x03 });

        IndexStore? store = null;
        try
        {
            store = IndexStore.OpenWithRecovery(corruptPath, out var recovered);
            Assert.True(recovered);

            // Verify the returned store is usable
            var roots = store.GetRoots();
            Assert.Empty(roots);

            var rootId = store.UpsertRoot(@"C:\RecoveryTest");
            store.BulkUpsert(new[]
            {
                FileEntry.FromFileSystem(rootId, @"C:\RecoveryTest\file.txt", false, 42, 999)
            });
            var all = store.LoadAll();
            Assert.Single(all);
            Assert.Equal("file.txt", all[0].Name);
            Assert.Equal(42, all[0].Size);
        }
        finally
        {
            store?.Dispose();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var p = corruptPath + suffix;
                try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
            }
        }
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix);
    }
}
