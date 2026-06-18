using Xunit;
using NetSearch.Core.Settings;

namespace NetSearch.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"nsset_{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var s = SettingsStore.Load(_path);
        Assert.Empty(s.Roots);
        Assert.Equal(0, s.AutoRefreshMinutes);
        Assert.Contains("txt", s.TextExtensions);
    }

    [Fact]
    public void Save_then_Load_roundtrips()
    {
        var s = new AppSettings
        {
            Roots = new List<string> { @"\\srv\share", @"Z:\" },
            AutoRefreshMinutes = 30,
            ContentMaxFileBytes = 1234,
        };
        SettingsStore.Save(_path, s);

        var loaded = SettingsStore.Load(_path);
        Assert.Equal(2, loaded.Roots.Count);
        Assert.Equal(30, loaded.AutoRefreshMinutes);
        Assert.Equal(1234, loaded.ContentMaxFileBytes);
    }

    [Fact]
    public void DefaultDir_is_under_localappdata()
    {
        Assert.Contains("NetSearch", SettingsStore.DefaultDir);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
