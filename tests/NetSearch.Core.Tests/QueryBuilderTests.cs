using Xunit;
using NetSearch.Core.Search;

namespace NetSearch.Core.Tests;

public class QueryBuilderTests
{
    [Theory]
    [InlineData("pdf, docx; png", new[] { "pdf", "docx", "png" })]
    [InlineData(".PDF .Doc", new[] { "pdf", "doc" })]
    [InlineData("", new string[0])]
    public void ParseExtensions_normalizes(string raw, string[] expected)
    {
        Assert.Equal(expected, QueryBuilder.ParseExtensions(raw).ToArray());
    }

    [Theory]
    [InlineData("1024", 1024L)]
    [InlineData("1KB", 1024L)]
    [InlineData("2 MB", 2L * 1024 * 1024)]
    [InlineData("", null)]
    [InlineData("garbage", null)]
    public void ParseSize_handles_units(string raw, long? expected)
    {
        Assert.Equal(expected, QueryBuilder.ParseSize(raw));
    }

    [Fact]
    public void Build_assembles_a_complete_query()
    {
        var after = DateTimeOffset.FromUnixTimeSeconds(1000);
        var q = QueryBuilder.Build(
            text: "report", mode: SearchMode.Wildcard,
            minSize: "1KB", maxSize: "",
            after: after, before: null,
            extensions: "pdf, docx", kind: EntryKind.FilesOnly);

        Assert.Equal("report", q.Text);
        Assert.Equal(SearchMode.Wildcard, q.Mode);
        Assert.Equal(1024, q.MinSize);
        Assert.Null(q.MaxSize);
        Assert.Equal(1000, q.ModifiedAfterUnix);
        Assert.Equal(new[] { "pdf", "docx" }, q.Extensions.ToArray());
        Assert.Equal(EntryKind.FilesOnly, q.Kind);
    }
}
