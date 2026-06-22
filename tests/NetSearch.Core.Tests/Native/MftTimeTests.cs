using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class MftTimeTests
{
    [Fact]
    public void FileTime_for_unix_epoch_is_zero()
        => Assert.Equal(0, MftTime.FileTimeToUnixSeconds(116444736000000000L));

    [Fact]
    public void FileTime_one_day_after_epoch_is_86400()
        => Assert.Equal(86400, MftTime.FileTimeToUnixSeconds(116444736000000000L + 86400L * 10_000_000L));

    [Fact]
    public void Nonpositive_filetime_maps_to_zero()
        => Assert.Equal(0, MftTime.FileTimeToUnixSeconds(0));
}
