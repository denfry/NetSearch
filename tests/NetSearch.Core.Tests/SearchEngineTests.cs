using Xunit;
using NetSearch.Core.Models;
using NetSearch.Core.Search;

namespace NetSearch.Core.Tests;

public class SearchEngineTests
{
    private static FileEntry E(string fullPath, bool isDir = false, long size = 100, long mod = 1000)
        => FileEntry.FromFileSystem(1, fullPath, isDir, size, mod);

    private readonly IReadOnlyList<FileEntry> _data = new[]
    {
        E(@"C:\d\Report.pdf", size: 500, mod: 2000),
        E(@"C:\d\report_draft.docx", size: 50, mod: 1000),
        E(@"C:\d\photo.PNG", size: 9000, mod: 3000),
        E(@"C:\d\Archive", isDir: true, size: 0, mod: 1500),
    };

    [Fact]
    public void Substring_is_case_insensitive()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { Text = "report" });
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Empty_text_returns_all()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { Text = "" });
        Assert.Equal(4, r.Count);
    }

    [Fact]
    public void Wildcard_matches_extension_pattern()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { Text = "*.pdf", Mode = SearchMode.Wildcard });
        Assert.Single(r);
        Assert.Equal("Report.pdf", r[0].Name);
    }

    [Fact]
    public void Regex_matches_and_invalid_regex_returns_empty()
    {
        var ok = SearchEngine.Search(_data, new SearchQuery { Text = @"^report", Mode = SearchMode.Regex });
        Assert.Equal(2, ok.Count);
        var bad = SearchEngine.Search(_data, new SearchQuery { Text = "(", Mode = SearchMode.Regex });
        Assert.Empty(bad);
    }

    [Fact]
    public void Size_filter_restricts_results()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { MinSize = 1000 });
        Assert.Single(r);
        Assert.Equal("photo.PNG", r[0].Name);
    }

    [Fact]
    public void Date_filter_restricts_results()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { ModifiedAfterUnix = 1800 });
        Assert.Equal(2, r.Count); // pdf(2000), png(3000)
    }

    [Fact]
    public void Extension_filter_is_case_insensitive()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { Extensions = new[] { "png" } });
        Assert.Single(r);
        Assert.Equal("photo.PNG", r[0].Name);
    }

    [Fact]
    public void Kind_filter_selects_folders_only()
    {
        var r = SearchEngine.Search(_data, new SearchQuery { Kind = EntryKind.FoldersOnly });
        Assert.Single(r);
        Assert.True(r[0].IsDir);
    }
}
