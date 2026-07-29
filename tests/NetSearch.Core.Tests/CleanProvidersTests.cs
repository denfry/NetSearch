using Xunit;
using NetSearch.Core.Cleaning;
using NetSearch.Core.Cleaning.Providers;

namespace NetSearch.Core.Tests;

public class CleanProvidersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"clean_{Guid.NewGuid():N}");

    public CleanProvidersTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "cacheA"));
        File.WriteAllBytes(Path.Combine(_root, "loose.tmp"), new byte[200]);
        File.WriteAllBytes(Path.Combine(_root, "cacheA", "x.dat"), new byte[300]);
        File.WriteAllBytes(Path.Combine(_root, "cacheA", "y.dat"), new byte[100]);
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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
