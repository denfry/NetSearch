using Xunit;
using NetSearch.Core.Search;

namespace NetSearch.Core.Tests;

public class FileTypeGroupsTests
{
    [Theory]
    [InlineData("PDF", "pdf")]
    [InlineData("Word", "doc, docx")]
    [InlineData("Excel", "xls, xlsx")]
    [InlineData("Фото", "jpg, jpeg, png, gif, bmp")]
    [InlineData("unknown", "")]
    [InlineData("", "")]
    public void Extensions_maps_known_groups(string group, string expected)
    {
        Assert.Equal(expected, FileTypeGroups.Extensions(group));
    }
}
