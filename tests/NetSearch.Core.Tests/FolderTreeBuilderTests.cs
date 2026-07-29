using Xunit;
using NetSearch.Core.DiskUsage;
using NetSearch.Core.Models;

namespace NetSearch.Core.Tests;

public class FolderTreeBuilderTests
{
    private static FileEntry File(string parent, string name, long size)
        => FileEntry.FromComponents(0, name, parent, isDir: false, size, 0);
    private static FileEntry Dir(string parent, string name)
        => FileEntry.FromComponents(0, name, parent, isDir: true, 0, 0);

    [Fact]
    public void Aggregates_sizes_up_the_tree_and_sorts_children_desc()
    {
        var b = new FolderTreeBuilder(@"C:");
        b.AddBatch(new[]
        {
            Dir(@"C:", "Windows"),
            Dir(@"C:", "Users"),
            File(@"C:\Windows", "big.dll", 300),
            File(@"C:\Windows\System32", "small.dll", 50),
            Dir(@"C:\Windows", "System32"),
            File(@"C:\Users", "u.txt", 100),
        });
        var root = b.Build();

        Assert.Equal(450, root.SizeBytes);          // 300 + 50 + 100
        Assert.Equal(3, root.FileCount);
        // Children sorted by size desc: Windows (350) before Users (100).
        Assert.Equal("Windows", root.Children[0].Name);
        Assert.Equal(350, root.Children[0].SizeBytes);
        Assert.Equal("Users", root.Children[1].Name);
        // Nested folder aggregates.
        var sys = root.Children[0].Children.Single(c => c.Name == "System32");
        Assert.Equal(50, sys.SizeBytes);
        Assert.Equal(1, sys.FileCount);
    }

    [Fact]
    public void Fraction_of_parent_is_relative_size()
    {
        var b = new FolderTreeBuilder(@"C:");
        b.AddBatch(new[] { File(@"C:\A", "x", 75), File(@"C:\B", "y", 25), Dir(@"C:", "A"), Dir(@"C:", "B") });
        var root = b.Build();
        Assert.Equal(0.75, root.Children[0].FractionOfParent, 3);
    }

    [Fact]
    public void Entries_outside_root_are_ignored()
    {
        var b = new FolderTreeBuilder(@"C:\Target");
        b.AddBatch(new[] { File(@"D:\Other", "z", 999), File(@"C:\Target", "ok", 10) });
        var root = b.Build();
        Assert.Equal(10, root.SizeBytes);
    }
}
