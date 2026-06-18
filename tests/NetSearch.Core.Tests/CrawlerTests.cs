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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
