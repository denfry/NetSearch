using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class PathBuilderTests
{
    [Fact]
    public void Root_and_nested_directories_resolve_to_parent_path_form()
    {
        var nodes = new Dictionary<long, MftNode>
        {
            [5]  = new("", 5, true),                 // volume root
            [10] = new("Users", 5, true),
            [11] = new("Me", 10, true),
            [12] = new("note.txt", 11, false),       // a file — not a directory path
        };

        var dirs = PathBuilder.BuildDirectoryPaths("C:", nodes);

        Assert.Equal(@"C:\", dirs[5]);
        Assert.Equal(@"C:\Users", dirs[10]);
        Assert.Equal(@"C:\Users\Me", dirs[11]);
        Assert.False(dirs.ContainsKey(12)); // files are not directory paths
    }

    [Fact]
    public void Orphans_and_cycles_are_dropped()
    {
        var nodes = new Dictionary<long, MftNode>
        {
            [5]  = new("", 5, true),
            [20] = new("Ghost", 999, true),  // parent missing → orphan
            [30] = new("A", 31, true),       // cycle 30↔31
            [31] = new("B", 30, true),
        };
        var dirs = PathBuilder.BuildDirectoryPaths("C:", nodes);
        Assert.True(dirs.ContainsKey(5));
        Assert.False(dirs.ContainsKey(20));
        Assert.False(dirs.ContainsKey(30));
        Assert.False(dirs.ContainsKey(31));
    }
}
