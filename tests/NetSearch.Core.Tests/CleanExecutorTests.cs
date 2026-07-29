using Xunit;
using NetSearch.Core.Cleaning;

namespace NetSearch.Core.Tests;

public class CleanExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"cexec_{Guid.NewGuid():N}");

    public CleanExecutorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Delete_permanent_removes_files_and_dirs_and_reports_freed()
    {
        var file = Path.Combine(_root, "f.tmp");
        var dir = Path.Combine(_root, "d");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(file, new byte[120]);
        File.WriteAllBytes(Path.Combine(dir, "inner.bin"), new byte[80]);

        var items = new[] { new CleanItem(file, 120, false), new CleanItem(dir, 80, true) };
        var outcome = new CleanExecutor().Delete(items, useRecycleBin: false, CancellationToken.None);

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(dir));
        Assert.Equal(2, outcome.DeletedCount);
        Assert.Equal(200, outcome.FreedBytes);
        Assert.Empty(outcome.Skipped);
    }

    [Fact]
    public void Locked_file_is_skipped_not_fatal()
    {
        var locked = Path.Combine(_root, "locked.tmp");
        using var stream = new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var ok = Path.Combine(_root, "ok.tmp");
        File.WriteAllBytes(ok, new byte[10]);

        var items = new[] { new CleanItem(locked, 50, false), new CleanItem(ok, 10, false) };
        var outcome = new CleanExecutor().Delete(items, useRecycleBin: false, CancellationToken.None);

        Assert.Equal(1, outcome.DeletedCount);
        Assert.Equal(10, outcome.FreedBytes);
        Assert.Contains(locked, outcome.Skipped);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }
}
