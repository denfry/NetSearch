using Xunit;
using NetSearch.Core.DiskUsage;

namespace NetSearch.Core.Tests;

public class SizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(3221225472, "3.0 GB")]
    public void Format_renders_human_readable(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes));

    [Fact]
    public void Format_clamps_negative_to_zero() => Assert.Equal("0 B", SizeFormatter.Format(-5));
}
