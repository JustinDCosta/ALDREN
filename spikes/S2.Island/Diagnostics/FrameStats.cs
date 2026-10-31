using System.Runtime.InteropServices;

namespace S2.Island.Diagnostics;

public readonly record struct FrameSample(ulong Refreshes, ulong Frames, ulong Missed, ulong Dropped, ulong Late, double RefreshHz);

/// <summary>
/// DWM composition counters (DwmGetCompositionTimingInfo). Since Windows 8.1 they cover the whole desktop on the
/// primary display, not one window; during a morph on an otherwise idle desktop, composed frames per refresh is the
/// share of refreshes that got a new frame.
/// </summary>
public static class FrameStats
{
    public static FrameSample Sample()
    {
        var info = new DWM_TIMING_INFO { cbSize = (uint)Marshal.SizeOf<DWM_TIMING_INFO>() };
        Marshal.ThrowExceptionForHR(DwmGetCompositionTimingInfo(0, ref info));
        var hz = info.rateRefresh.uiDenominator == 0 ? 0 : (double)info.rateRefresh.uiNumerator / info.rateRefresh.uiDenominator;
        return new FrameSample(info.cRefresh, info.cFrame, info.cFramesMissed, info.cFramesDropped, info.cFramesLate, hz);
    }

    public sealed class Tally
    {
        public ulong Refreshes { get; private set; }
        public ulong Frames { get; private set; }
        public ulong Missed { get; private set; }
        public ulong Dropped { get; private set; }
        public ulong Late { get; private set; }
        public double RefreshHz { get; private set; }

        public void Add(FrameSample before, FrameSample after)
        {
            Refreshes += after.Refreshes - before.Refreshes;
            Frames += after.Frames - before.Frames;
            Missed += after.Missed - before.Missed;
            Dropped += after.Dropped - before.Dropped;
            Late += after.Late - before.Late;
            RefreshHz = after.RefreshHz;
        }

        public double OnTimeShare => Refreshes == 0 ? 0 : Math.Min(1.0, (double)Frames / Refreshes);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct UNSIGNED_RATIO
    {
        public uint uiNumerator;
        public uint uiDenominator;
    }

    // dwmapi.h declares this with 1-byte packing.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DWM_TIMING_INFO
    {
        public uint cbSize;
        public UNSIGNED_RATIO rateRefresh;
        public ulong qpcRefreshPeriod;
        public UNSIGNED_RATIO rateCompose;
        public ulong qpcVBlank;
        public ulong cRefresh;
        public uint cDXRefresh;
        public ulong qpcCompose;
        public ulong cFrame;
        public uint cDXPresent;
        public ulong cRefreshFrame;
        public ulong cFrameSubmitted;
        public uint cDXPresentSubmitted;
        public ulong cFrameConfirmed;
        public uint cDXPresentConfirmed;
        public ulong cRefreshConfirmed;
        public uint cDXRefreshConfirmed;
        public ulong cFramesLate;
        public uint cFramesOutstanding;
        public ulong cFrameDisplayed;
        public ulong qpcFrameDisplayed;
        public ulong cRefreshFrameDisplayed;
        public ulong cFrameComplete;
        public ulong qpcFrameComplete;
        public ulong cFramePending;
        public ulong qpcFramePending;
        public ulong cFramesDisplayed;
        public ulong cFramesComplete;
        public ulong cFramesPending;
        public ulong cFramesAvailable;
        public ulong cFramesDropped;
        public ulong cFramesMissed;
        public ulong cRefreshNextDisplayed;
        public ulong cRefreshNextPresented;
        public ulong cRefreshesDisplayed;
        public ulong cRefreshesPresented;
        public ulong cRefreshStarted;
        public ulong cPixelsReceived;
        public ulong cPixelsDrawn;
        public ulong cBuffersEmpty;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(nint hwnd, ref DWM_TIMING_INFO info);
}
