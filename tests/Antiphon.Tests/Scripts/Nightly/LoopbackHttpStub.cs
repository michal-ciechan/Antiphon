using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S05: the C# twin of test-nightly-health.ps1's Start-C544WindmillStub / Stop-C544WindmillStub. An owned
/// loopback HTTP/1.1 server that serves any number of requests, one connection at a time, until Stop. Routes map a URL
/// path suffix (the query is ignored; "*" matches anything) to a JSON body (a string, served 200) or an HTTP status
/// (an int, served with "{}"); the first matching route in order wins, and no match is 404. FirstRequest holds the first
/// request's request line and headers; Stop returns every request line in order.
/// </summary>
public sealed class LoopbackHttpStub : IDisposable
{
    private readonly TcpListener _listener;
    private readonly IReadOnlyList<KeyValuePair<string, object>> _routes;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _requests = new();
    private readonly Task _loop;
    private string[] _first = [];
    private bool _stopped;

    private LoopbackHttpStub(IReadOnlyList<KeyValuePair<string, object>> routes)
    {
        _routes = routes;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(ServeAsync);
    }

    /// <summary>Starts a stub over the routes (path suffix -> string body or int status).</summary>
    public static LoopbackHttpStub Start(params (string Suffix, object Response)[] routes) =>
        new(routes.Select(route => new KeyValuePair<string, object>(route.Suffix, route.Response)).ToArray());

    /// <summary>The port it listens on (127.0.0.1).</summary>
    public int Port { get; }

    /// <summary>http://127.0.0.1:&lt;port&gt;.</summary>
    public string BaseUrl => "http://127.0.0.1:" + Port;

    /// <summary>The first request's request line and header lines.</summary>
    public IReadOnlyList<string> FirstRequest
    {
        get { lock (_requests) return _first; }
    }

    /// <summary>Stops listening and returns every request line received, in order.</summary>
    public IReadOnlyList<string> Stop()
    {
        if (!_stopped)
        {
            _stopped = true;
            _stop.Cancel();
            _listener.Stop();
            try { _loop.Wait(TimeSpan.FromSeconds(10)); }
            catch (AggregateException) { }
        }
        lock (_requests) return _requests.ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _stop.Dispose();
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) { break; }
            using (client)
            {
                try { await AnswerAsync(client); }
                catch (IOException) { }
                catch (SocketException) { }
            }
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var lines = new List<string>();
        while (true)
        {
            var line = await reader.ReadLineAsync(_stop.Token);
            if (string.IsNullOrEmpty(line)) break;
            lines.Add(line);
        }
        if (lines.Count == 0) return;
        lock (_requests)
        {
            _requests.Add(lines[0]);
            if (_first.Length == 0) _first = lines.ToArray();
        }
        var parts = lines[0].Split(' ');
        var path = parts.Length > 1 ? parts[1] : "/";
        var bare = path.Split('?')[0];
        var status = 404;
        var body = "{}";
        foreach (var route in _routes)
        {
            if (route.Key != "*" && !bare.EndsWith(route.Key, StringComparison.Ordinal)) continue;
            if (route.Value is int code) status = code;
            else
            {
                status = 200;
                body = (string)route.Value;
            }
            break;
        }
        var reason = status == 200 ? "OK" : status == 404 ? "Not Found" : "Error";
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, _stop.Token);
        await stream.WriteAsync(bytes, _stop.Token);
        await stream.FlushAsync(_stop.Token);
    }
}
