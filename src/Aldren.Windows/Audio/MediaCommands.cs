using System.Runtime.InteropServices;

namespace Aldren.Windows.Audio;

public enum MediaAction
{
    PlayPause,
    Next,
    Previous,
    Stop,
}

/// <summary>
/// Media transport as WM_APPCOMMAND messages to the shell, which forwards them to the current media session. No keystrokes
/// are synthesised, so nothing reaches a focused game.
/// </summary>
public static partial class MediaCommands
{
    private const uint WmAppCommand = 0x0319;

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string className, string? windowName);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hwnd, uint msg, nint wParam, nint lParam);

    public static bool Send(MediaAction action)
    {
        var shell = FindWindow("Shell_TrayWnd", null);
        if (shell == 0)
        {
            return false;
        }

        var command = action switch
        {
            MediaAction.PlayPause => 14,
            MediaAction.Next => 11,
            MediaAction.Previous => 12,
            MediaAction.Stop => 13,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        SendMessage(shell, WmAppCommand, shell, command << 16);
        return true;
    }
}
