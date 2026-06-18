using NetSearch.Core.Models;

namespace NetSearch.Core.Indexing;

public sealed record CrawlResult(int Count, IReadOnlyList<string> Skipped);

public sealed class Crawler
{
    private readonly int _batchSize;

    public Crawler(int batchSize = 1000) => _batchSize = Math.Max(1, batchSize);

    public CrawlResult Crawl(int rootId, string rootPath, Action<IReadOnlyList<FileEntry>> onBatch, CancellationToken ct)
    {
        var skipped = new List<string>();
        var batch = new List<FileEntry>(_batchSize);
        var count = 0;

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
        }

        void Recurse(string dir)
        {
            ct.ThrowIfCancellationRequested();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(dir);
            }
            catch (Exception)
            {
                skipped.Add(dir);
                return;
            }

            foreach (var path in children)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                    var mod = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
                    var size = isDir ? 0 : info.Length;
                    Add(FileEntry.FromFileSystem(rootId, path, isDir, size, mod));
                    if (isDir) Recurse(path);
                }
                catch (Exception)
                {
                    skipped.Add(path);
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
        return new CrawlResult(count, skipped);
    }
}
