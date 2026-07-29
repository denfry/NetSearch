namespace NetSearch.Core.Cleaning;

public sealed record CategoryScan(CleanCategory Category, IReadOnlyList<CleanItem> Items, long TotalBytes);

public sealed class CleanScanner
{
    private readonly IReadOnlyList<ICleanProvider> _providers;
    public CleanScanner(IReadOnlyList<ICleanProvider> providers) => _providers = providers;

    public IReadOnlyList<CategoryScan> Scan(IEnumerable<string> selectedCategoryIds, CancellationToken ct)
    {
        var selected = new HashSet<string>(selectedCategoryIds, StringComparer.OrdinalIgnoreCase);
        var results = new List<CategoryScan>();
        foreach (var p in _providers)
        {
            if (!selected.Contains(p.Category.Id)) continue;
            List<CleanItem> items;
            try { items = p.Scan(ct).ToList(); }
            catch (OperationCanceledException) { throw; }
            catch { items = new List<CleanItem>(); }
            results.Add(new CategoryScan(p.Category, items, items.Sum(i => i.SizeBytes)));
        }
        return results;
    }
}
