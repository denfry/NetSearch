using System.IO;

namespace NetSearch.Core.Cleaning.Providers;

public sealed class BrowserCacheProvider : ICleanProvider
{
    private readonly IReadOnlyList<string> _roots;
    public BrowserCacheProvider(IReadOnlyList<string> roots) => _roots = roots;

    public static BrowserCacheProvider ForSystem()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new List<string>
        {
            Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache"),
            Path.Combine(local, @"Microsoft\Edge\User Data\Default\Code Cache"),
            Path.Combine(local, @"Google\Chrome\User Data\Default\Cache"),
            Path.Combine(local, @"Google\Chrome\User Data\Default\Code Cache"),
            Path.Combine(local, @"Microsoft\Windows\Explorer"), // thumbcache_*.db / iconcache_*.db
        };
        return new BrowserCacheProvider(roots);
    }

    public CleanCategory Category => new(
        "browser", "Кэш браузеров и эскизов", "Кэш Edge/Chrome и кэш эскизов проводника",
        NeedsAdmin: false, UsesRecycleBin: true, DefaultSelected: true);

    public IEnumerable<CleanItem> Scan(CancellationToken ct) => CleanScanHelpers.TopLevelEntries(_roots, ct);
}
