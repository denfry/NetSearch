using NetSearch.Core.Cleaning.Native;

namespace NetSearch.Core.Cleaning.Providers;

public sealed class RecycleBinProvider : ICleanProvider
{
    public CleanCategory Category => new(
        "recyclebin", "Корзина", "Очистить корзину на всех дисках",
        NeedsAdmin: false, UsesRecycleBin: false, DefaultSelected: true);

    public IEnumerable<CleanItem> Scan(CancellationToken ct)
    {
        long size = OperatingSystem.IsWindows() ? ShellFileOps.QueryRecycleBinSize() : 0;
        if (size > 0)
            yield return new CleanItem(CleanItem.RecycleBinSentinel, size, true);
    }
}
