using Xunit;
using NetSearch.Core.DiskUsage;

namespace NetSearch.Core.Tests;

public class DiskUsageScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"du_{Guid.NewGuid():N}");

    public DiskUsageScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "big"));
        Directory.CreateDirectory(Path.Combine(_root, "small"));
        File.WriteAllBytes(Path.Combine(_root, "big", "a.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(_root, "big", "b.bin"), new byte[500]);
        File.WriteAllBytes(Path.Combine(_root, "small", "c.bin"), new byte[100]);
    }

    [Fact]
    public void ScanDirectory_builds_aggregated_sorted_tree()
    {
        var node = new DiskUsageScanner().ScanDirectory(_root, CancellationToken.None, null);
        Assert.Equal(1600, node.SizeBytes);
        Assert.Equal("big", node.Children[0].Name);    // 1500 sorts before 100
        Assert.Equal(1500, node.Children[0].SizeBytes);
        Assert.Equal("small", node.Children[1].Name);
    }

    [Fact]
    public void DriveProbe_lists_at_least_the_system_drive()
    {
        var drives = DriveProbe.FixedDrives();
        Assert.NotEmpty(drives);
        Assert.All(drives, d => Assert.True(d.TotalBytes >= 0 && d.UsedBytes >= 0));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
