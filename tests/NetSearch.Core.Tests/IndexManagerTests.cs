using Xunit;
using NetSearch.Core.Indexing;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Tests;

public class IndexManagerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nsidx_{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"nsroot_{Guid.NewGuid():N}");

    public IndexManagerTests() => Directory.CreateDirectory(_root);

    private IndexStore NewStore()
    {
        var s = new IndexStore(_dbPath);
        s.Initialize();
        return s;
    }

    [Fact]
    public void RebuildRoot_indexes_all_current_files()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "y");
        using var store = NewStore();
        var id = store.UpsertRoot(_root);
        var mgr = new IndexManager(store, () => new Crawler(), () => 555);

        var result = mgr.RebuildRoot(id, _root, CancellationToken.None);

        Assert.Equal(2, result.Added);
        Assert.Equal(2, store.LoadAll().Count);
        Assert.Equal(555, store.GetRoots().Single().LastIndexed);
    }

    [Fact]
    public void UpdateRoot_detects_added_removed_and_changed()
    {
        var fa = Path.Combine(_root, "a.txt");
        var fb = Path.Combine(_root, "b.txt");
        File.WriteAllText(fa, "x");
        File.WriteAllText(fb, "y");

        using var store = NewStore();
        var id = store.UpsertRoot(_root);
        var mgr = new IndexManager(store, () => new Crawler(), () => 1);
        mgr.RebuildRoot(id, _root, CancellationToken.None);

        // Mutate the tree: delete b, add c, grow a.
        File.Delete(fb);
        File.WriteAllText(Path.Combine(_root, "c.txt"), "z");
        File.WriteAllText(fa, "xxxxxxxx");

        var result = mgr.UpdateRoot(id, _root, CancellationToken.None);

        Assert.Equal(1, result.Added);   // c.txt
        Assert.Equal(1, result.Removed); // b.txt
        Assert.True(result.Updated >= 1); // a.txt grew
        var names = store.LoadAll().Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "a.txt", "c.txt" }, names);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        foreach (var s in new[] { "-wal", "-shm" })
            if (File.Exists(_dbPath + s)) File.Delete(_dbPath + s);
    }
}
