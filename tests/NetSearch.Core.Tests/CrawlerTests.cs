using System.Diagnostics;
using Xunit;
using NetSearch.Core.Indexing;
using NetSearch.Core.Models;

namespace NetSearch.Core.Tests;

public class CrawlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"crawl_{Guid.NewGuid():N}");

    public CrawlerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(_root, "sub", "b.log"), "world!!");
    }

    private List<FileEntry> CrawlAll(out CrawlResult result)
    {
        var collected = new List<FileEntry>();
        var crawler = new Crawler(batchSize: 1);
        result = crawler.Crawl(1, _root, batch => collected.AddRange(batch), CancellationToken.None);
        return collected;
    }

    [Fact]
    public void Crawl_emits_all_files_and_directories()
    {
        var all = CrawlAll(out var result);
        var names = all.Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Contains("a.txt", names);
        Assert.Contains("b.log", names);
        Assert.Contains("sub", names);
        Assert.Equal(all.Count, result.Count);
    }

    [Fact]
    public void Crawl_sets_size_and_is_dir_correctly()
    {
        var all = CrawlAll(out _);
        var file = all.First(e => e.Name == "a.txt");
        var dir = all.First(e => e.Name == "sub");
        Assert.False(file.IsDir);
        Assert.Equal(5, file.Size); // "hello"
        Assert.True(dir.IsDir);
    }

    [Fact]
    public void Crawl_of_missing_root_records_it_as_skipped()
    {
        var crawler = new Crawler();
        var missing = Path.Combine(_root, "does-not-exist");
        var result = crawler.Crawl(1, missing, _ => { }, CancellationToken.None);
        Assert.Contains(missing, result.Skipped);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void Crawl_honors_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // already cancelled before crawl starts
        var crawler = new Crawler(batchSize: 1);
        Assert.ThrowsAny<OperationCanceledException>(() =>
            crawler.Crawl(1, _root, _ => { }, cts.Token));
    }

    [Fact]
    public void Crawl_does_not_recurse_into_reparse_point_to_avoid_cycles()
    {
        var link = Path.Combine(_root, "loop");
        if (!TryCreateJunction(link, _root))
            return; // environment cannot create junctions (no NTFS / privilege) — skip

        try
        {
            var collected = new List<FileEntry>();
            var crawler = new Crawler(batchSize: 1);
            var result = crawler.Crawl(1, _root, batch => collected.AddRange(batch), CancellationToken.None);

            // The junction itself is indexed as a directory entry...
            Assert.Contains(collected, e => e.Name == "loop" && e.IsDir);
            // ...but the crawl never descends through it (no cyclic "\loop\" paths).
            Assert.DoesNotContain(collected,
                e => e.ParentPath.Contains(@"\loop", StringComparison.OrdinalIgnoreCase));
            Assert.True(result.Count < 100, $"crawl should terminate, got {result.Count} entries");
        }
        finally
        {
            // Remove the junction (not its target) before the fixture deletes _root.
            RunCmd($"/c rmdir \"{link}\"");
        }
    }

    [Fact]
    public void Parallel_crawl_finds_every_entry_exactly_once()
    {
        // Wider/deeper tree on top of the fixture (sub, a.txt, sub/b.log).
        for (var i = 0; i < 8; i++)
        {
            var d = Path.Combine(_root, $"d{i}");
            Directory.CreateDirectory(d);
            for (var j = 0; j < 5; j++)
                File.WriteAllText(Path.Combine(d, $"f{j}.txt"), "x");
        }

        var collected = new List<FileEntry>();
        var crawler = new Crawler(batchSize: 4, parallelism: 4);
        // onBatch is serialised by the crawler, so appending without a lock is safe.
        var result = crawler.Crawl(1, _root, batch => collected.AddRange(batch), CancellationToken.None);

        Assert.Equal(result.Count, collected.Count);
        // No directory enumerated twice → no duplicate entries.
        Assert.Equal(collected.Count, collected.Select(e => e.FullPath).Distinct().Count());
        // Everything the sequential crawl finds, the parallel crawl finds too.
        var sequential = new List<FileEntry>();
        new Crawler(batchSize: 4, parallelism: 1)
            .Crawl(1, _root, b => sequential.AddRange(b), CancellationToken.None);
        Assert.Equal(
            sequential.Select(e => e.FullPath).OrderBy(p => p),
            collected.Select(e => e.FullPath).OrderBy(p => p));
    }

    [Fact]
    public void Parallel_crawl_honors_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var crawler = new Crawler(batchSize: 1, parallelism: 4);
        Assert.ThrowsAny<OperationCanceledException>(() =>
            crawler.Crawl(1, _root, _ => { }, cts.Token));
    }

    [Fact]
    public void Crawl_reports_progress_with_running_count()
    {
        var progress = new RecordingProgress<CrawlProgress>();
        var collected = new List<FileEntry>();
        var crawler = new Crawler(batchSize: 1);
        var result = crawler.Crawl(1, _root, b => collected.AddRange(b), CancellationToken.None, progress);

        Assert.NotEmpty(progress.Items);
        Assert.Equal(result.Count, progress.Items[^1].Count);          // final report carries the total
        Assert.False(string.IsNullOrEmpty(progress.Items[^1].CurrentDirectory));
    }

    private static bool TryCreateJunction(string link, string target)
    {
        var exit = RunCmd($"/c mklink /J \"{link}\" \"{target}\"");
        return exit == 0 && Directory.Exists(link);
    }

    private static int RunCmd(string args)
    {
        var psi = new ProcessStartInfo("cmd.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public readonly List<T> Items = new();
        public void Report(T value) => Items.Add(value);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
