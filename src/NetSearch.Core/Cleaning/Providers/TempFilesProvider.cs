using System.IO;

namespace NetSearch.Core.Cleaning.Providers;

public sealed class TempFilesProvider : ICleanProvider
{
    private readonly IReadOnlyList<string> _roots;
    public TempFilesProvider(IReadOnlyList<string> roots) => _roots = roots;

    public static TempFilesProvider ForSystem(bool elevated)
    {
        var roots = new List<string> { Path.GetTempPath() };
        if (elevated)
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
        return new TempFilesProvider(roots);
    }

    public CleanCategory Category => new(
        "temp", "Временные файлы", "%TEMP% и системная папка Temp",
        NeedsAdmin: false, UsesRecycleBin: true, DefaultSelected: true);

    public IEnumerable<CleanItem> Scan(CancellationToken ct) => CleanScanHelpers.TopLevelEntries(_roots, ct);
}
