using System.IO;

namespace NetSearch.Core.Cleaning.Providers;

public sealed class SystemDevCacheProvider : ICleanProvider
{
    private readonly IReadOnlyList<string> _roots;
    public SystemDevCacheProvider(IReadOnlyList<string> roots) => _roots = roots;

    public static SystemDevCacheProvider ForSystem(bool elevated)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>
        {
            Path.Combine(local, @"npm-cache"),
            Path.Combine(profile, @".nuget\packages\.tools"),
            Path.Combine(local, @"pip\cache"),
            Path.Combine(local, @"CrashDumps"),
        };
        if (elevated)
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            roots.Add(Path.Combine(win, @"SoftwareDistribution\Download"));
        }
        return new SystemDevCacheProvider(roots);
    }

    public CleanCategory Category => new(
        "sysdev", "Системный и dev-кэш", "Кэш обновлений Windows, дампы, кэш npm/NuGet/pip",
        NeedsAdmin: true, UsesRecycleBin: false, DefaultSelected: false);

    public IEnumerable<CleanItem> Scan(CancellationToken ct) => CleanScanHelpers.TopLevelEntries(_roots, ct);
}
