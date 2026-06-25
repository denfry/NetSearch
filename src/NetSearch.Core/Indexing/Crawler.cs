using System.Collections.Concurrent;
using NetSearch.Core.Models;

namespace NetSearch.Core.Indexing;

public sealed record CrawlResult(int Count, IReadOnlyList<string> Skipped);

public sealed record CrawlProgress(int Count, string CurrentDirectory);

public sealed class Crawler
{
    private const int ReportInterval = 256;
    private readonly int _batchSize;
    private readonly int _parallelism;

    public Crawler(int batchSize = 1000, int parallelism = 1)
    {
        _batchSize = Math.Max(1, batchSize);
        _parallelism = Math.Max(1, parallelism);
    }

    public CrawlResult Crawl(
        int rootId,
        string rootPath,
        Action<IReadOnlyList<FileEntry>> onBatch,
        CancellationToken ct,
        IProgress<CrawlProgress>? progress = null)
    {
        if (!Directory.Exists(rootPath))
            return new CrawlResult(0, new List<string> { rootPath });

        ct.ThrowIfCancellationRequested();

        // Directories are the unit of parallel work: over SMB the dominant cost is the
        // per-directory round-trip latency, so fanning several enumerations out at once
        // hides that latency almost linearly until the link saturates. Each worker pulls a
        // directory off the shared queue, enumerates it, and pushes any child directories
        // back on — a classic work-stealing breadth-first crawl.
        var sync = new object();
        var skipped = new List<string>();
        var queue = new ConcurrentQueue<string>();
        queue.Enqueue(rootPath);

        var count = 0;
        var lastReport = 0;
        var pending = 1;                 // directories queued or in flight; crawl ends at 0
        var currentDir = rootPath;

        // onBatch / progress / skipped are always invoked under `sync`, so the consumer
        // (e.g. IndexManager's add/remove bookkeeping) stays correct without being
        // thread-safe itself, even though enumeration runs on several threads at once.
        void Emit(List<FileEntry> batch)
        {
            if (batch.Count == 0) return;
            lock (sync)
            {
                onBatch(batch);
                count += batch.Count;
                if (count - lastReport >= ReportInterval)
                {
                    lastReport = count;
                    progress?.Report(new CrawlProgress(count, currentDir));
                }
            }
        }

        void Skip(string path) { lock (sync) skipped.Add(path); }

        void Worker()
        {
            var batch = new List<FileEntry>(_batchSize);
            var spin = new SpinWait();
            while (true)
            {
                if (!queue.TryDequeue(out var dir))
                {
                    if (Volatile.Read(ref pending) == 0) break;
                    spin.SpinOnce();      // back off while peers are still discovering work
                    continue;
                }
                spin = new SpinWait();

                try
                {
                    ct.ThrowIfCancellationRequested();
                    currentDir = dir;

                    // EnumerateFileSystemInfos fills Attributes / LastWriteTimeUtc / Length
                    // from the single directory listing, so there is no extra per-file
                    // metadata round-trip. Materialise inside the try: an I/O fault *during*
                    // enumeration (common over SMB) then skips just this directory.
                    DirectoryInfo di;
                    List<FileSystemInfo> children;
                    try
                    {
                        di = new DirectoryInfo(dir);
                        children = di.EnumerateFileSystemInfos().ToList();
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { Skip(dir); continue; }

                    // Every child shares this one parent string (== Path.GetDirectoryName of each
                    // child's full path), so a directory of 10 000 files allocates the parent once.
                    var parent = di.FullName;

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
                            batch.Add(FileEntry.FromComponents(rootId, info.Name, parent, isDir, size, mod));
                            if (batch.Count >= _batchSize) { Emit(batch); batch = new List<FileEntry>(_batchSize); }

                            // Index the reparse point itself but never descend through it:
                            // junctions / symlinks / DFS links can form cycles.
                            if (isDir && !isReparse)
                            {
                                Interlocked.Increment(ref pending);
                                queue.Enqueue(info.FullName);
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception) { Skip(info.FullName); }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref pending);
                }
            }
            Emit(batch);
        }

        if (_parallelism == 1)
        {
            Worker();
        }
        else
        {
            var workers = new Task[_parallelism];
            for (var i = 0; i < _parallelism; i++)
                workers[i] = Task.Run(Worker, ct);
            try
            {
                Task.WaitAll(workers);
            }
            catch (AggregateException ae) when (ae.InnerExceptions.All(e => e is OperationCanceledException))
            {
                throw new OperationCanceledException(ct);
            }
        }

        ct.ThrowIfCancellationRequested();
        progress?.Report(new CrawlProgress(count, currentDir)); // final report carries the total
        return new CrawlResult(count, skipped);
    }
}
