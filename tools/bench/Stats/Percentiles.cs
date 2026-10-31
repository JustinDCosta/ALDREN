namespace Aldren.Bench.Stats;

public record Percentiles(double P50, double P90, double P95, double Max)
{
    public static Percentiles Of(IReadOnlyList<double> samples)
    {
        if (samples.Count == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        var sorted = samples.Order().ToArray();
        return new Percentiles(At(sorted, 0.50), At(sorted, 0.90), At(sorted, 0.95), sorted[^1]);
    }

    // Linear interpolation between the closest ranks (the same method as Excel's PERCENTILE.INC).
    private static double At(double[] sorted, double p)
    {
        var h = (sorted.Length - 1) * p;
        var lower = (int)Math.Floor(h);
        var upper = (int)Math.Ceiling(h);
        return sorted[lower] + (h - lower) * (sorted[upper] - sorted[lower]);
    }
}
