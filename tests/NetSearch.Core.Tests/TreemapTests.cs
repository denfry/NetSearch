using Xunit;
using NetSearch.Core.DiskUsage;

namespace NetSearch.Core.Tests;

public class TreemapTests
{
    [Fact]
    public void Returns_one_rect_per_weight_in_input_order()
    {
        var rects = Treemap.Squarify(100, 100, new double[] { 50, 30, 20 });
        Assert.Equal(3, rects.Count);
    }

    [Fact]
    public void Rects_cover_the_full_area_without_overflow()
    {
        var rects = Treemap.Squarify(200, 120, new double[] { 6, 6, 4, 3, 2, 1 });
        double area = rects.Sum(r => r.Width * r.Height);
        Assert.Equal(200d * 120d, area, 0); // total tiled area equals bounds
        foreach (var r in rects)
        {
            Assert.True(r.X >= -1e-6 && r.Y >= -1e-6);
            Assert.True(r.X + r.Width <= 200 + 1e-6);
            Assert.True(r.Y + r.Height <= 120 + 1e-6);
        }
    }

    [Fact]
    public void Nonpositive_weights_become_zero_rects()
    {
        var rects = Treemap.Squarify(100, 100, new double[] { 0, 100 });
        Assert.Equal(0, rects[0].Width * rects[0].Height);
        Assert.Equal(100d * 100d, rects[1].Width * rects[1].Height, 0);
    }

    [Fact]
    public void Empty_or_degenerate_bounds_return_zero_rects()
    {
        Assert.All(Treemap.Squarify(0, 100, new double[] { 1, 2 }),
            r => Assert.Equal(0, r.Width * r.Height));
        Assert.Empty(Treemap.Squarify(100, 100, System.Array.Empty<double>()));
    }
}
