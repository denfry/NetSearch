using Xunit;
using NetSearch.Core.Native;
using NetSearch.Core.Models;

namespace NetSearch.Core.Tests.Native;

public class MftEntryAssemblerTests
{
    [Fact]
    public void Builds_entries_under_the_requested_root_only()
    {
        var records = new Dictionary<long, ParsedMftRecord>
        {
            [5]  = new(true, 5, "", 0, 0),
            [10] = new(true, 5, "Users", 0, 100),
            [11] = new(true, 10, "Me", 0, 100),
            [12] = new(false, 11, "note.txt", 7, 200),
            [40] = new(false, 5, "pagefile.sys", 999, 0), // outside C:\Users\Me
        };

        var entries = MftEntryAssembler
            .Assemble(rootId: 3, volumeRoot: "C:", rootFilter: @"C:\Users\Me", records)
            .ToList();

        Assert.Contains(entries, e => e.Name == "note.txt" && e.Size == 7
            && e.ParentPath == @"C:\Users\Me" && e.FullPath == @"C:\Users\Me\note.txt");
        Assert.Contains(entries, e => e.Name == "Me" && e.IsDir);
        Assert.DoesNotContain(entries, e => e.Name == "pagefile.sys");
    }

    [Fact]
    public void Sibling_directory_sharing_a_name_prefix_is_excluded()
    {
        var records = new Dictionary<long, ParsedMftRecord>
        {
            [5]  = new(true, 5, "", 0, 0),
            [10] = new(true, 5, "Me", 0, 0),
            [11] = new(false, 10, "in.txt", 1, 0),
            [20] = new(true, 5, "MeToo", 0, 0),    // shares the "Me" prefix
            [21] = new(false, 20, "out.txt", 1, 0),
        };

        var entries = MftEntryAssembler.Assemble(1, "C:", @"C:\Me", records).ToList();

        Assert.Contains(entries, e => e.Name == "in.txt");
        Assert.DoesNotContain(entries, e => e.Name == "out.txt");
    }
}
