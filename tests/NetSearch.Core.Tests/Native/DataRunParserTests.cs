using Xunit;
using NetSearch.Core.Native;

namespace NetSearch.Core.Tests.Native;

public class DataRunParserTests
{
    [Fact]
    public void Single_run()
    {
        // 0x21: len field 1 byte, offset field 2 bytes; length=0x08; offset=0x0010
        var runs = DataRunParser.Parse(new byte[] { 0x21, 0x08, 0x10, 0x00, 0x00 });
        Assert.Single(runs);
        Assert.Equal(8, runs[0].ClusterCount);
        Assert.Equal(0x10, runs[0].Lcn);
    }

    [Fact]
    public void Second_run_offset_is_signed_delta_from_previous()
    {
        // run1: len 0x08 @ lcn +0x20 ; run2: len 0x04 @ delta -0x10 (0xF0 sign-extended)
        var runs = DataRunParser.Parse(new byte[] { 0x11, 0x08, 0x20, 0x11, 0x04, 0xF0, 0x00 });
        Assert.Equal(2, runs.Count);
        Assert.Equal(0x20, runs[0].Lcn);
        Assert.Equal(0x10, runs[1].Lcn); // 0x20 + (-0x10)
    }

    [Fact]
    public void Sparse_hole_has_lcn_minus_one_and_keeps_running_lcn()
    {
        // run1 @ +0x20 ; sparse run (offset len 0) length 0x03 ; run3 len 0x02 delta +0x05
        var runs = DataRunParser.Parse(new byte[] { 0x11, 0x08, 0x20, 0x01, 0x03, 0x11, 0x02, 0x05, 0x00 });
        Assert.Equal(3, runs.Count);
        Assert.Equal(-1, runs[1].Lcn);
        Assert.Equal(0x25, runs[2].Lcn); // 0x20 + 0x05, hole did not move base
    }
}
