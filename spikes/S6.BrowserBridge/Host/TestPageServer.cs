using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace S6.Host;

/// <summary>
/// Serves the test page on 127.0.0.1 under a random path, so only whoever was given the URL can load it; everything else is a 404.
/// </summary>
internal sealed class TestPageServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly byte[] _page;
    private readonly string _path;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private TestPageServer(string pageFile)
    {
        _page = File.ReadAllBytes(pageFile);
        _path = $"/{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}/";
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _loop = AcceptAsync();
    }

    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{_path}";

    public static TestPageServer Start(string pageFile) => new(pageFile);

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient connection;
            try
            {
                connection = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _ = ServeAsync(connection); // Chrome opens spare connections that never send anything, so each gets its own task
        }
    }

    private async Task ServeAsync(TcpClient connection)
    {
        using (connection)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(timeout.Token) ?? "";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token)))
                {
                    // skip the headers
                }

                var parts = requestLine.Split(' ');
                var found = parts.Length >= 2 && parts[0] == "GET" && (parts[1] == _path || parts[1] == _path + "index.html");
                var body = found ? _page : "Not found"u8.ToArray();
                var head = $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Type: {(found ? "text/html; charset=utf-8" : "text/plain")}\r\n" +
                           $"Content-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), timeout.Token);
                await stream.WriteAsync(body, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
        _stop.Dispose();
    }
}
