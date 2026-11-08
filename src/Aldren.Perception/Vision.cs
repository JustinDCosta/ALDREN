using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WinRT;

namespace Aldren.Perception;

public sealed record OcrLine(string Text, Box Bounds);

/// <summary>
/// Pixels when UI Automation isn't enough (ARCHITECTURE §11, levels 4–6): one frame of a window through Windows Graphics
/// Capture (the yellow capture border shows while it's taken: the "looking" indicator), Windows OCR on it, and PNG copies,
/// downscaled or marked with numbered boxes, for the local vision model. Ported from S5.
/// </summary>
public static class Vision
{
    public static async Task<SoftwareBitmap> CaptureAsync(nint hwnd, CancellationToken ct)
    {
        var item = CreateItemForWindow(hwnd);
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(CreateDevice(), DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        var first = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        pool.FrameArrived += (p, _) =>
        {
            var frame = p.TryGetNextFrame();
            if (frame is not null && !first.TrySetResult(frame))
            {
                frame.Dispose();
            }
        };
        session.StartCapture();
        using var captured = await first.Task.WaitAsync(TimeSpan.FromSeconds(3), ct);
        return await SoftwareBitmap.CreateCopyFromSurfaceAsync(captured.Surface, BitmapAlphaMode.Premultiplied);
    }

    /// <summary>OCR lines, with bounds in the captured image's pixels.</summary>
    public static async Task<IReadOnlyList<OcrLine>> ReadAsync(SoftwareBitmap bitmap)
    {
        var engine = OcrEngine.TryCreateFromLanguage(new Language("en-US")) ?? OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? throw new InvalidOperationException("No OCR language is installed in Windows.");
        using var gray = bitmap.BitmapPixelFormat is BitmapPixelFormat.Gray8 or BitmapPixelFormat.Bgra8
            ? null
            : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
        var result = await engine.RecognizeAsync(gray ?? bitmap);
        return result.Lines.Select(l =>
        {
            var words = l.Words.Select(w => w.BoundingRect).ToList();
            var left = words.Min(w => w.X);
            var top = words.Min(w => w.Y);
            return new OcrLine(l.Text, new Box(left, top, words.Max(w => w.X + w.Width) - left, words.Max(w => w.Y + w.Height) - top));
        }).ToList();
    }

    /// <summary>A PNG no wider than <paramref name="maxWidth"/>: big windows cost the vision model seconds per megapixel.</summary>
    public static async Task<(byte[] Png, double Scale)> ToPngAsync(SoftwareBitmap bitmap, int maxWidth)
    {
        var scale = Math.Min(1.0, (double)maxWidth / bitmap.PixelWidth);
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        if (scale < 1.0)
        {
            encoder.BitmapTransform.ScaledWidth = (uint)Math.Round(bitmap.PixelWidth * scale);
            encoder.BitmapTransform.ScaledHeight = (uint)Math.Round(bitmap.PixelHeight * scale);
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return (bytes, scale);
    }

    /// <summary>
    /// Numbered red boxes over a few candidate controls (ADR-0013: the model answers "#3", not coordinates). Boxes are
    /// given in screen pixels and drawn relative to the window's top-left corner. WPF drawing, on its own STA thread.
    /// </summary>
    public static Task<byte[]> MarkAsync(byte[] png, IReadOnlyList<(int Number, Box Bounds)> marks, Box window, double scale)
    {
        var result = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                result.SetResult(Draw(png, marks, window, scale));
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        })
        { IsBackground = true, Name = "ALDREN marks" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    private static byte[] Draw(byte[] png, IReadOnlyList<(int Number, Box Bounds)> marks, Box window, double scale)
    {
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(png);
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.Red, 2);
        var typeface = new System.Windows.Media.Typeface("Segoe UI Semibold");
        var size = Math.Max(13, image.PixelWidth / 90.0);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image, new System.Windows.Rect(0, 0, image.PixelWidth, image.PixelHeight));
            foreach (var (number, b) in marks)
            {
                var box = new System.Windows.Rect((b.Left - window.Left) * scale, (b.Top - window.Top) * scale, b.Width * scale, b.Height * scale);
                dc.DrawRectangle(null, pen, box);
                var label = new System.Windows.Media.FormattedText(number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight, typeface, size, System.Windows.Media.Brushes.White, 1.0);
                var tag = new System.Windows.Rect(box.Left, Math.Max(0, box.Top - label.Height), label.Width + 6, label.Height);
                dc.DrawRectangle(System.Windows.Media.Brushes.Red, null, tag);
                dc.DrawText(label, new System.Windows.Point(tag.Left + 3, tag.Top));
            }
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static IDirect3DDevice CreateDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out var device, out _, out var context));
        try
        {
            var dxgiDeviceId = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(device, in dxgiDeviceId, out var dxgiDevice));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
                try
                {
                    return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(device);
        }
    }

    private static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        var interop = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem").AsInterface<IGraphicsCaptureItemInterop>();
        var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        var pointer = interop.CreateForWindow(hwnd, in itemId);
        try
        {
            return GraphicsCaptureItem.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow(nint window, in Guid iid);

        nint CreateForMonitor(nint monitor, in Guid iid);
    }

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels,
        uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint context);

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
