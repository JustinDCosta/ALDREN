namespace S5.Perception.Grounding;

public readonly record struct Point(double X, double Y);

public readonly record struct Rect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public Point Center => new(Left + Width / 2, Top + Height / 2);
}

public record UiElementInfo(int Id, string Name, string ControlType, Rect Bounds, bool IsEnabled);

/// <summary>Scores where a model points against UI Automation's own element bounds (the ground truth).</summary>
public static class GroundingScore
{
    /// <summary>Edges count as inside: a click on the border still lands on the control.</summary>
    public static bool PointInElement(Point p, UiElementInfo e) =>
        p.X >= e.Bounds.Left && p.X <= e.Bounds.Right && p.Y >= e.Bounds.Top && p.Y <= e.Bounds.Bottom;

    /// <summary>Share of questions where the predicted point lands on the expected element; no answer is a miss.</summary>
    public static double Accuracy(IReadOnlyList<(UiElementInfo Expected, Point? Predicted)> results) =>
        results.Count == 0 ? 0 : results.Count(r => r.Predicted is { } p && PointInElement(p, r.Expected)) / (double)results.Count;
}
