using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace S5.Perception.Grounding;

/// <summary>Draws a numbered red box over each element (the number is its id). WPF rendering: call on an STA thread.</summary>
internal static class MarksRenderer
{
    public static byte[] Render(byte[] png, IReadOnlyList<UiElementInfo> elements)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(png);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        int width = image.PixelWidth, height = image.PixelHeight;
        var pen = new Pen(Brushes.Red, 2);
        pen.Freeze();
        var typeface = new Typeface("Segoe UI Semibold");
        var size = Math.Max(13, width / 110.0); // stays readable after the vision encoder downscales large windows

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new System.Windows.Rect(0, 0, width, height));
            foreach (var e in elements)
            {
                var box = new System.Windows.Rect(e.Bounds.Left, e.Bounds.Top, e.Bounds.Width, e.Bounds.Height);
                dc.DrawRectangle(null, pen, box);
                var label = new FormattedText(e.Id.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, size, Brushes.White, 1.0);
                var tag = new System.Windows.Rect(box.Left, Math.Max(0, box.Top - label.Height), label.Width + 6, label.Height);
                dc.DrawRectangle(Brushes.Red, null, tag);
                dc.DrawText(label, new System.Windows.Point(tag.Left + 3, tag.Top));
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
