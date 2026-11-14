using System.Runtime.InteropServices;

namespace Aldren.Windows;

/// <summary>
/// Voice typing (Phase 9): text sent as Unicode key events to the window you're using. Only ever called for dictation, never
/// during gaming mode (the caller checks), never into password fields (the caller checks with UI Automation).
/// </summary>
public static class Keyboard
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;

    public static void TypeText(string text)
    {
        var inputs = new List<Input>(text.Length * 2);
        foreach (var c in text.Replace("\r\n", "\n"))
        {
            var ch = c == '\n' ? '\r' : c;
            inputs.Add(Key(ch, 0));
            inputs.Add(Key(ch, KeyEventKeyUp));
        }

        // In chunks, so a long dictation doesn't flood the target's input queue.
        foreach (var chunk in inputs.Chunk(200))
        {
            if (SendInput((uint)chunk.Length, chunk, Marshal.SizeOf<Input>()) != chunk.Length)
            {
                throw new InvalidOperationException("Windows didn't accept the typing (the target may be running as administrator).");
            }

            Thread.Sleep(10);
        }
    }

    private static Input Key(char c, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { Scan = c, Flags = KeyEventUnicode | flags } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    // Present so the union has the size Windows expects (mouse input is the largest member).
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint Data, Flags, Time;
        public nint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
