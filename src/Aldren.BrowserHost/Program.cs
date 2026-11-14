using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

// ALDREN's browser host (ARCHITECTURE §12), ported from S6: Chrome starts it for the ALDREN Companion extension and talks
// over stdio in native-messaging frames; Core connects on a named pipe only the current user can open (one JSON message
// per line). Only known commands with an integer id pass, in either direction's size limit. Ends when Chrome closes stdin.
const string PipeName = "aldren-companion";
var origin = args.FirstOrDefault() ?? "unknown";
var fromChrome = Console.OpenStandardInput();
var toChrome = Console.OpenStandardOutput();
var clientLock = new Lock();
StreamWriter? client = null;

void ToClient(string line)
{
    lock (clientLock)
    {
        try
        {
            client?.WriteLine(line);
        }
        catch (IOException)
        {
        }
    }
}

var chromeSide = Task.Run(() =>
{
    while (Frames.Read(fromChrome) is { } json)
    {
        ToClient(json);
    }
});

var pipeSide = Task.Run(async () =>
{
    while (true)
    {
        await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync();
        using var reader = new StreamReader(pipe);
        lock (clientLock)
        {
            client = new StreamWriter(pipe) { AutoFlush = true };
        }

        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (Commands.Problem(line) is { } problem)
                {
                    ToClient(JsonSerializer.Serialize(new { id = Commands.IdOf(line), ok = false, result = new { }, error = problem }));
                    continue;
                }

                var frame = Frames.Frame(line);
                await toChrome.WriteAsync(frame);
                await toChrome.FlushAsync();
            }
        }
        catch (IOException)
        {
        }
        finally
        {
            lock (clientLock)
            {
                client = null;
            }
        }
    }
});

var ended = await Task.WhenAny(chromeSide, pipeSide);
_ = origin;
return ended.IsFaulted ? 1 : 0;

/// <summary>Chrome's native-messaging framing: a 4-byte little-endian length, then UTF-8 JSON (S6).</summary>
internal static class Frames
{
    public const int MaxMessageBytes = 1024 * 1024;

    public static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        return frame;
    }

    public static string? Read(Stream input)
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

/// <summary>Only these commands, shaped {"id": int, "cmd": name, "args": {}}, reach the extension.</summary>
internal static class Commands
{
    private static readonly HashSet<string> Known =
        ["ping", "tabs", "open", "activate", "close", "back", "read", "click", "fill", "scroll", "download", "focusBlock", "focusClear"];

    public static string? Problem(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) > Frames.MaxMessageBytes)
        {
            return "Command too large.";
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id) || !id.TryGetInt32(out _))
            {
                return "Each command needs an integer id.";
            }

            if (!root.TryGetProperty("cmd", out var cmd) || cmd.ValueKind != JsonValueKind.String || !Known.Contains(cmd.GetString()!))
            {
                return "Unknown command.";
            }

            return root.TryGetProperty("args", out var a) && a.ValueKind != JsonValueKind.Object ? "args must be an object." : null;
        }
        catch (JsonException)
        {
            return "Not JSON.";
        }
    }

    public static int IdOf(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("id", out var id) && id.TryGetInt32(out var n) ? n : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
