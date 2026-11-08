using System.Runtime.InteropServices;

namespace Aldren.Windows;

/// <summary>
/// Clipboard text, read only when you ask (ARCHITECTURE §11): never monitored. Password managers mark what they copy as
/// private (the "ExcludeClipboardContentFromMonitorProcessing" and "CanIncludeInClipboardHistory = 0" formats); ALDREN
/// refuses to read it then.
/// </summary>
public static class ClipboardText
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    public static Task<string?> ReadAsync() => OnSta(() =>
    {
        Open();
        try
        {
            if (IsClipboardFormatAvailable(RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing")))
            {
                throw new InvalidOperationException("The clipboard holds something marked private (from a password manager), so I won't read it.");
            }

            var history = RegisterClipboardFormat("CanIncludeInClipboardHistory");
            if (IsClipboardFormatAvailable(history) && GetClipboardData(history) is var h && h != 0 && GlobalLock(h) is var p && p != 0)
            {
                var allowed = Marshal.ReadInt32(p);
                GlobalUnlock(h);
                if (allowed == 0)
                {
                    throw new InvalidOperationException("The clipboard holds something marked private, so I won't read it.");
                }
            }

            if (!IsClipboardFormatAvailable(CfUnicodeText))
            {
                return null;
            }

            var data = GetClipboardData(CfUnicodeText);
            var pointer = data == 0 ? 0 : GlobalLock(data);
            if (pointer == 0)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(data);
            }
        }
        finally
        {
            CloseClipboard();
        }
    });

    public static Task WriteAsync(string text) => OnSta<object?>(() =>
    {
        Open();
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var memory = GlobalAlloc(GmemMoveable, (nuint)bytes);
            var pointer = GlobalLock(memory);
            Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            Marshal.WriteInt16(pointer, text.Length * 2, 0);
            GlobalUnlock(memory);
            if (SetClipboardData(CfUnicodeText, memory) == 0)
            {
                throw new InvalidOperationException("Windows refused the clipboard text.");
            }

            return null;
        }
        finally
        {
            CloseClipboard();
        }
    });

    private static void Open()
    {
        // Another app may hold the clipboard for a moment.
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(0))
            {
                return;
            }

            Thread.Sleep(30);
        }

        throw new InvalidOperationException("Another app is holding the clipboard.");
    }

    private static Task<T> OnSta<T>(Func<T> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                result.SetResult(work());
            }
            catch (Exception ex)
            {
                result.SetException(ex);
            }
        })
        { IsBackground = true, Name = "ALDREN clipboard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint memory);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClipboardFormatW")] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
}
