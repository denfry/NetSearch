using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class IndexStrategySelectorTests
{
    private sealed class FakeProbe : IEnvironmentProbe
    {
        public bool IsWindowsElevated { get; init; }
        public VolumeInfo? Volume { get; init; }
        public bool TryGetVolume(string rootPath, out VolumeInfo info)
        { info = Volume ?? default; return Volume is not null; }
    }

    [Fact]
    public void Mft_when_elevated_local_fixed_ntfs()
    {
        var probe = new FakeProbe { IsWindowsElevated = true, Volume = new VolumeInfo("NTFS", true, false, 'C') };
        Assert.Equal(IndexBackend.Mft, IndexStrategySelector.Select(@"C:\Data", probe));
    }

    [Theory]
    [InlineData(false, "NTFS", true, false)]  // not elevated
    [InlineData(true,  "exFAT", true, false)] // not NTFS
    [InlineData(true,  "NTFS", false, false)] // not fixed
    [InlineData(true,  "NTFS", true,  true)]  // UNC
    public void Crawler_otherwise(bool elevated, string fs, bool fixedDrive, bool unc)
    {
        var probe = new FakeProbe { IsWindowsElevated = elevated, Volume = new VolumeInfo(fs, fixedDrive, unc, 'C') };
        Assert.Equal(IndexBackend.Crawler, IndexStrategySelector.Select(@"C:\Data", probe));
    }

    [Fact]
    public void Crawler_when_volume_unknown()
        => Assert.Equal(IndexBackend.Crawler,
            IndexStrategySelector.Select(@"\\srv\share", new FakeProbe { IsWindowsElevated = true }));
}
