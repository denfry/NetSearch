using NetSearch.Core.Models;

namespace NetSearch.Core.Indexing;

public sealed record CrawlResult(int Count, IReadOnlyList<string> Skipped);

public sealed record CrawlProgress(int Count, string CurrentDirectory);

public sealed class Crawler
{
    private const int ReportInterval = 256;
    private readonly int _batchSize;

    public Crawler(int batchSize = 1000) => _batchSize = Math.Max(1, batchSize);

    public CrawlResult Crawl(
        int rootId,
        string rootPath,
        Action<IReadOnlyList<FileEntry>> onBatch,
        CancellationToken ct,
        IProgress<CrawlProgress>? progress = null)
    {
        var skipped = new List<string>();
        var batch = new List<FileEntry>(_batchSize);
        var count = 0;
        var lastReport = 0;
        var currentDir = rootPath;

        void Flush()
        {
            if (batch.Count == 0) return;
            onBatch(batch);
            batch = new List<FileEntry>(_batchSize);
        }

        void Add(FileEntry e)
        {
            batch.Add(e);
            count++;
            if (batch.Count >= _batchSize) Flush();
            if (count - lastReport >= ReportInterval)
            {
                lastReport = count;
                progress?.Report(new CrawlProgress(count, currentDir));
            }
        }

        void Recurse(string dir)
        {
            ct.ThrowIfCancellationRequested();
            currentDir = dir;

            // EnumerateFileSystemInfos returns FileSystemInfo objects whose Attributes,
            // LastWriteTimeUtc and (for files) Length are already populated from the single
            // directory enumeration — so we avoid a separate per-file metadata round-trip,
            // which over SMB is the dominant cost.
            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(dir).EnumerateFileSystemInfos();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                skipped.Add(dir);
                return;
            }

            foreach (var info in children)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var attrs = info.Attributes;
                    var isDir = (attrs & FileAttributes.Directory) != 0;
                    var isReparse = (attrs & FileAttributes.ReparsePoint) != 0;
                    var mod = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
                    var size = isDir ? 0L : ((FileInfo)info).Length;
                    Add(FileEntry.FromFileSystem(rootId, info.FullName, isDir, size, mod));

                    // The reparse point itself is indexed, but we never descend through it:
                    // junctions / symlinks / DFS links can form cycles that would otherwise
                    // make the crawl run forever.
                    if (isDir && !isReparse) Recurse(info.FullName);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    skipped.Add(info.FullName);
                }
            }
        }

        if (!Directory.Exists(rootPath))
        {
            skipped.Add(rootPath);
            return new CrawlResult(0, skipped);
        }

        Recurse(rootPath);
        Flush();
        progress?.Report(new CrawlProgress(count, currentDir)); // final progress
        return new CrawlResult(count, skipped);
    }
}
