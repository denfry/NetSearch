using NetSearch.Core.Indexing;
using NetSearch.Core.Native;

namespace NetSearch.Core.DiskUsage;

/// <summary>
/// Scans a drive into a <see cref="FolderNode"/> tree. Uses the fast MFT enumerator when the
/// process is elevated and the volume is fixed NTFS; otherwise (and on any MFT failure) it falls
/// back to the recursive <see cref="Crawler"/> — the same transparent fallback the indexer uses.
/// </summary>
public sealed class DiskUsageScanner
{
    public FolderNode Scan(string driveRoot, CancellationToken ct, IProgress<CrawlProgress>? progress = null)
    {
        if (OperatingSystem.IsWindows())
        {
            var probe = new WindowsEnvironmentProbe();
            if (IndexStrategySelector.Select(driveRoot, probe) == IndexBackend.Mft)
            {
                try { return ScanViaMft(driveRoot, ct, progress); }
                catch (OperationCanceledException) { throw; }
                catch { /* fall through to the crawler */ }
            }
        }
        return ScanDirectory(driveRoot, ct, progress);
    }

    public FolderNode ScanDirectory(string path, CancellationToken ct, IProgress<CrawlProgress>? progress)
    {
        var builder = new FolderTreeBuilder(path);
        new Crawler(parallelism: 4).Crawl(0, path, builder.AddBatch, ct, progress);
        return builder.Build();
    }

    private static FolderNode ScanViaMft(string driveRoot, CancellationToken ct, IProgress<CrawlProgress>? progress)
    {
        var builder = new FolderTreeBuilder(driveRoot);
        new MftEnumerator().Enumerate(0, driveRoot, builder.AddBatch, ct, progress);
        return builder.Build();
    }
}
