using Aldren.Bench.Stats;

namespace Aldren.Bench.Tests;

public class PercentilesTests
{
    [Fact]
    public void Percentiles_of_1_to_100()
    {
        // Reversed on purpose: the samples must be sorted before interpolating.
        var samples = Enumerable.Range(1, 100).Select(i => (double)i).Reverse().ToList();

        var p = Percentiles.Of(samples);

        Assert.Equal(50.5, p.P50, 6);
        Assert.Equal(90.1, p.P90, 6);
        Assert.Equal(95.05, p.P95, 6);
        Assert.Equal(100, p.Max);
    }

    [Fact]
    public void Single_sample_is_every_percentile()
    {
        Assert.Equal(new Percentiles(42, 42, 42, 42), Percentiles.Of(new[] { 42.0 }));
    }

    [Fact]
    public void Empty_samples_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Percentiles.Of(Array.Empty<double>()));
    }
}
