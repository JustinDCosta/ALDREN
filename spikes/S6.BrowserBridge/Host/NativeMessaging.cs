using System.Buffers.Binary;
using System.Text;

namespace S6.Host;

/// <summary>Chrome's native-messaging framing: a 4-byte little-endian length, then that many bytes of UTF-8 JSON.</summary>
public static class NativeMessaging
{
    /// <summary>Chrome refuses host messages over 1 MB; the host holds incoming ones to the same limit.</summary>
    public const int MaxMessageBytes = 1024 * 1024;

    public static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        return frame;
    }

    /// <summary>The next message, or null when Chrome has closed the stream.</summary>
    public static string? ReadFrame(Stream input)
    {
        var header = new byte[4];
        var read = input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (read == 0)
        {
            return null;
        }

        if (read < header.Length)
        {
            throw new EndOfStreamException("The stream ended inside a message header.");
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length > MaxMessageBytes)
        {
            throw new InvalidDataException($"A {length}-byte message is over the {MaxMessageBytes}-byte limit.");
        }

        var body = new byte[length];
        input.ReadExactly(body);
        return Encoding.UTF8.GetString(body);
    }
}
