using Xunit;
using NetSearch.Core.Models;

namespace NetSearch.Core.Tests;

public class FileEntryTests
{
    [Fact]
    public void FromFileSystem_derives_name_namelower_parent_and_ext()
    {
        var e = FileEntry.FromFileSystem(
            rootId: 1,
            fullPath: @"\\server\share\Docs\Report.PDF",
            isDir: false,
            size: 1234,
            modifiedUnix: 1700000000);

        Assert.Equal("Report.PDF", e.Name);
        Assert.Equal("report.pdf", e.NameLower);
        Assert.Equal(@"\\server\share\Docs", e.ParentPath);
        Assert.Equal("pdf", e.Ext);
        Assert.False(e.IsDir);
        Assert.Equal(1234, e.Size);
        Assert.Equal(1700000000, e.Modified);
        Assert.Equal(@"\\server\share\Docs\Report.PDF", e.FullPath);
    }

    [Fact]
    public void FromFileSystem_directory_has_empty_ext()
    {
        var e = FileEntry.FromFileSystem(2, @"C:\Temp\SubFolder", isDir: true, size: 0, modifiedUnix: 0);
        Assert.True(e.IsDir);
        Assert.Equal("", e.Ext);
        Assert.Equal("SubFolder", e.Name);
    }
}
