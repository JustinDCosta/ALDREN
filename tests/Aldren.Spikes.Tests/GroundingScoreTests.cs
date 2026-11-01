using S5.Perception.Grounding;

namespace Aldren.Spikes.Tests;

public class GroundingScoreTests
{
    private static UiElementInfo Button(double left, double top, double width, double height) =>
        new(1, "Save", "Button", new Rect(left, top, width, height), true);

    [Fact]
    public void Point_on_edge_counts_as_inside()
    {
        var save = Button(10, 10, 100, 40);

        Assert.True(GroundingScore.PointInElement(new Point(10, 10), save));
        Assert.True(GroundingScore.PointInElement(new Point(110, 50), save));
        Assert.False(GroundingScore.PointInElement(new Point(110.5, 30), save));
    }

    [Fact]
    public void Null_prediction_counts_as_miss()
    {
        Assert.Equal(0.0, GroundingScore.Accuracy([(Button(0, 0, 10, 10), null)]));
    }

    [Fact]
    public void Accuracy_of_three_hits_in_four_is_0_75()
    {
        var save = Button(0, 0, 10, 10);

        var accuracy = GroundingScore.Accuracy([
            (save, new Point(5, 5)), (save, new Point(1, 1)), (save, new Point(10, 10)), (save, new Point(50, 50)),
        ]);

        Assert.Equal(0.75, accuracy);
    }
}
