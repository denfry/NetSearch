using Xunit;
using NetSearch.Core.Cleaning;

namespace NetSearch.Core.Tests;

public class CleanScannerTests
{
    private sealed class FakeProvider : ICleanProvider
    {
        private readonly CleanItem[] _items;
        private readonly bool _throws;
        public FakeProvider(string id, bool throws, params CleanItem[] items)
        { Category = new CleanCategory(id, id, "", false, false, true); _items = items; _throws = throws; }
        public CleanCategory Category { get; }
        public IEnumerable<CleanItem> Scan(CancellationToken ct)
        { if (_throws) throw new IOException("boom"); return _items; }
    }

    [Fact]
    public void Scans_only_selected_categories_and_totals_sizes()
    {
        var scanner = new CleanScanner(new ICleanProvider[]
        {
            new FakeProvider("a", false, new CleanItem("p1", 100, false), new CleanItem("p2", 50, false)),
            new FakeProvider("b", false, new CleanItem("p3", 999, false)),
        });
        var result = scanner.Scan(new[] { "a" }, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal("a", result[0].Category.Id);
        Assert.Equal(150, result[0].TotalBytes);
    }

    [Fact]
    public void Provider_failure_yields_empty_category_not_exception()
    {
        var scanner = new CleanScanner(new ICleanProvider[] { new FakeProvider("a", true) });
        var result = scanner.Scan(new[] { "a" }, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal(0, result[0].TotalBytes);
        Assert.Empty(result[0].Items);
    }
}
