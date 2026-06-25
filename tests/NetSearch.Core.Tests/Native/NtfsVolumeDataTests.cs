using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class NtfsVolumeDataTests
{
    [Fact]
    public void Parses_geometry_from_documented_offsets()
    {
        var buf = new byte[0x50];
        BitConverter.GetBytes(4096).CopyTo(buf, 0x2C);        // BytesPerCluster
        BitConverter.GetBytes(1024).CopyTo(buf, 0x30);        // BytesPerFileRecordSegment
        BitConverter.GetBytes(786432L).CopyTo(buf, 0x40);     // MftStartLcn

        var g = NtfsVolumeData.Parse(buf);

        Assert.Equal(4096, g.BytesPerCluster);
        Assert.Equal(1024, g.BytesPerFileRecordSegment);
        Assert.Equal(786432L, g.MftStartLcn);
    }
}
