using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Antiphon.NightlyWatchdog;

/// <summary>
/// CARD-0545 D-11 F-3 qualification stub: a loopback Windmill imitation for the qualification instance.
/// Modes: <c>ok</c> (healthy answers), <c>refuse</c> (listener stopped), <c>503</c>, <c>timeout</c>
/// (accept and never answer). The mode is re-read from <c>modeFile</c> on every request so an operator
/// can flip it without restarting. Never used by the production instance.
/// </summary>
public static class WindmillStub
{
    public static async Task<int> RunAsync(string bind, string modeFile, TextWriter output, CancellationToken ct)
    {
        var (host, port) = WatchdogOptions.SplitBind(bind);
        TcpListener? listener = null;
        await output.WriteLineAsync($"windmill stub on {bind}; mode file {modeFile}");
        while (!ct.IsCancellationRequested)
        {
            var mode = File.Exists(modeFile) ? (await File.ReadAllTextAsync(modeFile, ct)).Trim() : "ok";
            if (mode == "refuse")
            {
                listener?.Stop();
                listener = null;
                await Task.Delay(1000, ct);
                continue;
            }
            if (listener is null)
            {
                listener = new TcpListener(IPAddress.Parse(host), port);
                listener.Start();
            }
            using var accept = CancellationTokenSource.CreateLinkedTokenSource(ct);
            accept.CancelAfter(1000);
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(accept.Token); }
            catch (OperationCanceledException) { continue; }
            _ = Task.Run(async () =>
            {
                using (client)
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var request = await reader.ReadLineAsync() ?? "";
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                    if (mode == "timeout") { await Task.Delay(TimeSpan.FromMinutes(2)); return; }
                    var path = request.Split(' ').ElementAtOrDefault(1) ?? "/";
                    var (status, body) = mode == "503" ? (503, "{}") : Answer(path);
                    var bytes = Encoding.UTF8.GetBytes(body);
                    var head = $"HTTP/1.1 {status} X\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                    await stream.WriteAsync(bytes);
                }
            });
        }
        listener?.Stop();
        return 0;
    }

    private static (int Status, string Body) Answer(string path) => path switch
    {
        _ when path.StartsWith("/api/version", StringComparison.Ordinal) => (200, "\"stub\""),
        _ when path.StartsWith("/api/users/whoami", StringComparison.Ordinal) => (200, "{\"username\":\"stub\"}"),
        _ when path.Contains("/scripts/get/", StringComparison.Ordinal) => (200, "{\"hash\":\"stub\"}"),
        _ when path.Contains("/schedules/get/", StringComparison.Ordinal) => (200, "{\"enabled\":true}"),
        _ when path.StartsWith("/api/workers/list", StringComparison.Ordinal) => (200, "[{\"worker_group\":\"desktop\",\"last_ping\":1}]"),
        _ when path.Contains("/jobs/list", StringComparison.Ordinal) => (200, "[]"),
        _ => (404, "{}"),
    };
}
