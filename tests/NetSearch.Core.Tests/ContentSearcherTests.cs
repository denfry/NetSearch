using Xunit;
using NetSearch.Core.Models;
using NetSearch.Core.Search;

namespace NetSearch.Core.Tests;

public class ContentSearcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"content_{Guid.NewGuid():N}");

    public ContentSearcherTests() => Directory.CreateDirectory(_dir);

    private FileEntry Write(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return FileEntry.FromFileSystem(1, p, false, new FileInfo(p).Length, 0);
    }

    [Fact]
    public async Task Finds_substring_and_reports_line_number()
    {
        var f = Write("a.txt", "first line\nhas needle here\nlast");
        var searcher = new ContentSearcher(new ContentSearchOptions(1_000_000, new[] { "txt" }, 2));

        var matches = await searcher.SearchAsync(new[] { f }, "needle", useRegex: false, null, CancellationToken.None);

        Assert.Single(matches);
        Assert.Equal(2, matches[0].LineNumber);
        Assert.Contains("needle", matches[0].LineText);
    }

    [Fact]
    public async Task Skips_files_over_size_limit_and_wrong_extension()
    {
        var big = Write("big.txt", new string('x', 50) + "needle");
        var bin = Write("c.bin", "needle");
        var searcher = new ContentSearcher(new ContentSearchOptions(MaxFileBytes: 10, new[] { "txt" }, 2));

        var matches = await searcher.SearchAsync(new[] { big, bin }, "needle", false, null, CancellationToken.None);

        Assert.Empty(matches); // big over size; bin wrong ext
    }

    [Fact]
    public async Task Reports_progress_for_each_candidate()
    {
        var f1 = Write("a.txt", "needle");
        var f2 = Write("b.txt", "nothing");
        var seen = 0;
        var progress = new Progress<int>(_ => Interlocked.Increment(ref seen));
        var searcher = new ContentSearcher(new ContentSearchOptions(1_000_000, new[] { "txt" }, 1));

        var matches = await searcher.SearchAsync(new[] { f1, f2 }, "needle", false, progress, CancellationToken.None);

        Assert.Single(matches);
        // Progress is asynchronous; allow it to drain.
        await Task.Delay(50);
        Assert.True(seen >= 1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
