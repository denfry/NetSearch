using Xunit;
using NetSearch.Core.Models;
using NetSearch.Core.Storage;

namespace NetSearch.Core.Tests;

public class IndexStoreUsnTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"usn_{Guid.NewGuid():N}.db");

    [Fact]
    public void Usn_state_roundtrips_per_root()
    {
        using var s = new IndexStore(_db); s.Initialize();
        var id = s.UpsertRoot(@"C:\Data");
        Assert.False(s.TryGetUsnState(id, out _, out _));
        s.SetUsnState(id, journalId: 777, nextUsn: 12345);
        Assert.True(s.TryGetUsnState(id, out var j, out var n));
        Assert.Equal(777, j);
        Assert.Equal(12345, n);
    }

    [Fact]
    public void RemoveByFrn_deletes_matching_rows_only()
    {
        using var s = new IndexStore(_db); s.Initialize();
        var id = s.UpsertRoot(@"C:\Data");
        s.BulkUpsert(new[]
        {
            FileEntry.FromComponents(id, "a.txt", @"C:\Data", false, 1, 1, frn: 100),
            FileEntry.FromComponents(id, "b.txt", @"C:\Data", false, 1, 1, frn: 200),
        });
        s.RemoveByFrn(id, new long[] { 100 });
        var names = s.LoadAll().Select(e => e.Name).ToList();
        Assert.Equal(new[] { "b.txt" }, names);
    }

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
        foreach (var x in new[] { "-wal", "-shm" }) if (File.Exists(_db + x)) File.Delete(_db + x);
    }
}
