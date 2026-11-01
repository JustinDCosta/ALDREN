namespace Aldren.Common.Shell;

public readonly record struct RectInt(int X, int Y, int Width, int Height);

public static class IslandPlacement
{
    /// <summary>Top centre of the work area, sizes in DIPs scaled to physical pixels.</summary>
    public static RectInt Place(RectInt workArea, double scale, int widthDip, int heightDip, int topMarginDip = 8)
    {
        var width = Scale(widthDip, scale);
        var height = Scale(heightDip, scale);
        return new RectInt(workArea.X + (workArea.Width - width) / 2, workArea.Y + Scale(topMarginDip, scale), width, height);
    }

    private static int Scale(int dip, double scale) => (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);
}
