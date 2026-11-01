using System.IO.Pipes;
using System.Text.Json;

namespace S6.Host;

/// <summary>
/// What Chrome starts: relays between the extension (stdin/stdout, native-messaging frames) and one ALDREN client on a named
/// pipe only the current user can open (one JSON message per line). Ends when Chrome closes stdin.
/// </summary>
internal static class Relay
{
    public const string PipeName = "aldren-bridge-probe";

    public static async Task<int> RunAsync(string origin, Stream fromChrome, Stream toChrome)
    {
        Log.Write($"host started for {origin}");
        var clientLock = new object();
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
                    // the client went away; the pipe loop notices and waits for the next one
                }
            }
        }

        var chromeSide = Task.Run(() =>
        {
            while (NativeMessaging.ReadFrame(fromChrome) is { } json)
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
                Log.Write("client connected");
                using var reader = new StreamReader(pipe);
                lock (clientLock)
                {
                    client = new StreamWriter(pipe) { AutoFlush = true };
                }

                try
                {
                    while (await reader.ReadLineAsync() is { } line)
                    {
                        if (CommandCheck.Problem(line) is { } problem)
                        {
                            Log.Write($"refused a command: {problem}");
                            ToClient(JsonSerializer.Serialize(new { id = IdOf(line), ok = false, result = new { }, error = problem }));
                            continue;
                        }

                        var frame = NativeMessaging.Frame(line);
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

                    Log.Write("client disconnected");
                }
            }
        });

        var ended = await Task.WhenAny(chromeSide, pipeSide);
        Log.Write(ended.IsFaulted ? $"host stopped: {ended.Exception!.GetBaseException().Message}" : "Chrome closed the connection");
        return ended.IsFaulted ? 1 : 0;
    }

    private static int IdOf(string line)
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

/// <summary>Events only, never message contents (they can hold page text). Chrome discards the host's stderr, hence a file.</summary>
internal static class Log
{
    private static readonly string File = Path.Combine(Probe.ResultsFolder, "host.log");
    private static readonly object Gate = new();

    public static void Write(string text)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Probe.ResultsFolder);
            System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {text}{Environment.NewLine}");
        }
    }
}
