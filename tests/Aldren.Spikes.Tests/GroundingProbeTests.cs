using S5.Perception.Grounding;
using Xunit;

namespace Aldren.Spikes.Tests;

public class GroundingProbeTests
{
    [Fact]
    public void ParsePoint_prefers_point_2d_over_earlier_numbers()
    {
        var point = GroundingProbe.ParsePoint("On the 0-1000 scale: [{\"point_2d\": [500, 250], \"label\": \"Save\"}]", 666, 400);

        Assert.Equal(new Point(333, 100), point);
    }

    [Fact]
    public void ParsePoint_scales_0_1000_to_pixels()
    {
        var point = GroundingProbe.ParsePoint("125, 230", 666, 393);

        Assert.NotNull(point);
        Assert.Equal(83.25, point.Value.X, 3);
        Assert.Equal(90.39, point.Value.Y, 3);
    }
}
