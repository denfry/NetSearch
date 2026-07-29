using Xunit;
using NetSearch.Core.Cleaning;
using NetSearch.Core.Cleaning.Providers;

namespace NetSearch.Core.Tests;

public class CleanProvidersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"clean_{Guid.NewGuid():N}");

    private static readonly DateTime Old = DateTime.UtcNow - TimeSpan.FromDays(2);

    public CleanProvidersTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "cacheA"));
        File.WriteAllBytes(Path.Combine(_root, "loose.tmp"), new byte[200]);
        File.WriteAllBytes(Path.Combine(_root, "cacheA", "x.dat"), new byte[300]);
        File.WriteAllBytes(Path.Combine(_root, "cacheA", "y.dat"), new byte[100]);

        // TempFilesProvider now excludes anything modified in the last 24h, so age these
        // fixture entries to keep the pre-existing tests exercising "old, eligible" files.
        File.SetLastWriteTimeUtc(Path.Combine(_root, "loose.tmp"), Old);
        Directory.SetLastWriteTimeUtc(Path.Combine(_root, "cacheA"), Old);
    }

    [Fact]
    public void TopLevelEntries_lists_files_and_dirs_with_aggregate_sizes()
    {
        var items = CleanScanHelpers.TopLevelEntries(new[] { _root }, CancellationToken.None).ToList();
        Assert.Equal(2, items.Count); // loose.tmp + cacheA
        Assert.Equal(200, items.Single(i => i.Path.EndsWith("loose.tmp")).SizeBytes);
        var dir = items.Single(i => i.IsDir);
        Assert.Equal(400, dir.SizeBytes); // 300 + 100
    }

    [Fact]
    public void Provider_scans_its_injected_roots()
    {
        var provider = new TempFilesProvider(new[] { _root });
        var total = provider.Scan(CancellationToken.None).Sum(i => i.SizeBytes);
        Assert.Equal(600, total);
        Assert.Equal("temp", provider.Category.Id);
        Assert.True(provider.Category.DefaultSelected);
    }

    [Fact]
    public void Missing_roots_are_skipped()
    {
        var items = CleanScanHelpers.TopLevelEntries(
            new[] { Path.Combine(_root, "nope") }, CancellationToken.None).ToList();
        Assert.Empty(items);
    }

    [Fact]
    public void TempFilesProvider_excludes_dotnet_extraction_dir()
    {
        var dotnetDir = Path.Combine(_root, ".net");
        Directory.CreateDirectory(dotnetDir);
        File.WriteAllBytes(Path.Combine(dotnetDir, "app.dll"), new byte[50]);
        Directory.SetLastWriteTimeUtc(dotnetDir, Old);

        var provider = new TempFilesProvider(new[] { _root });
        var items = provider.Scan(CancellationToken.None).ToList();

        Assert.DoesNotContain(items, i => i.Path.EndsWith(".net"));
    }

    [Fact]
    public void TempFilesProvider_excludes_recently_modified_entries()
    {
        var freshFile = Path.Combine(_root, "fresh.tmp");
        File.WriteAllBytes(freshFile, new byte[10]);
        // Left at its natural (just-now) LastWriteTimeUtc.

        var freshDir = Path.Combine(_root, "freshDir");
        Directory.CreateDirectory(freshDir);
        File.WriteAllBytes(Path.Combine(freshDir, "z.dat"), new byte[10]);
        // Left at its natural (just-now) LastWriteTimeUtc.

        var provider = new TempFilesProvider(new[] { _root });
        var items = provider.Scan(CancellationToken.None).ToList();

        Assert.DoesNotContain(items, i => i.Path.EndsWith("fresh.tmp"));
        Assert.DoesNotContain(items, i => i.Path.EndsWith("freshDir"));
        // Old fixture entries (aged in the constructor) are still included.
        Assert.Contains(items, i => i.Path.EndsWith("loose.tmp"));
        Assert.Contains(items, i => i.Path.EndsWith("cacheA"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
