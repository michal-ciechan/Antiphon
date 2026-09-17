using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Antiphon.NightlyWatchdog;

/// <summary>
/// CARD-0545 D-9 snapshot: the watchdog's read-only heartbeat and ledger summary. Carries no secret,
/// no token and no raw chat id (the destination appears only as <c>sha256(chatId)[..16]</c>). Capped
/// at 64 KB; <c>truncated</c> says when recent notifications were cut.
/// </summary>
public static class SnapshotBuilder
{
    public const int MaxBytes = 64 * 1024;

    public static string Build(Ledger ledger, WatchdogOptions options)
    {
        var heartbeat = ledger.Heartbeat();
        var state = heartbeat.StateJson is { } json ? JsonNode.Parse(json) as JsonObject : null;
        var notifications = ledger.Notifications();
        var byNid = notifications.ToDictionary(n => n.Nid, StringComparer.Ordinal);
        var chatHash = WatchdogOptions.DestinationHash(options.DestinationChatId);
        var numeric = long.TryParse(options.DestinationChatId, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out _);
        var qualified = numeric && (heartbeat.DestinationHash is null || heartbeat.DestinationHash == chatHash);

        var openOutages = new JsonArray();
        foreach (var outage in ledger.Outages().Where(o => o.ClosedAt is null))
        {
            openOutages.Add(new JsonObject
            {
                ["outageId"] = outage.OutageId,
                ["kind"] = outage.Kind,
                ["dueDay"] = outage.DueDay,
                ["openedAt"] = Ledger.Iso(outage.OpenedAt),
                ["failureNid"] = outage.FailureNid,
                ["failureState"] = outage.FailureNid is { } nid && byNid.TryGetValue(nid, out var n) ? n.State : null,
            });
        }

        var receipts = ledger.Receipts().GroupBy(r => r.Nid).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var recent = new List<JsonObject>();
        foreach (var notification in notifications.Reverse())
        {
            var attempts = ledger.Attempts(notification.Nid);
            var accepted = attempts.LastOrDefault(a => a.Accepted);
            var readAt = receipts.TryGetValue(notification.Nid, out var rows)
                ? rows.Select(r => r.ReadObservedAt).FirstOrDefault(r => r is not null) : null;
            recent.Add(new JsonObject
            {
                ["nid"] = notification.Nid,
                ["kind"] = notification.Kind,
                ["outageId"] = notification.OutageId,
                ["state"] = notification.State,
                ["attempts"] = attempts.Count,
                ["acceptedAt"] = accepted?.AcceptedAt is { } at ? Ledger.Iso(at) : null,
                ["messageId"] = accepted?.MessageId,
                ["receivedAt"] = notification.ReceivedAt is { } received ? Ledger.Iso(received) : null,
                ["readObservedAt"] = readAt is { } read ? Ledger.Iso(read) : null,
            });
        }

        JsonObject? lastDueDay = heartbeat.LastDueDay is { } due
            ? new JsonObject
            {
                ["dueDay"] = due.DueDay, ["jobId"] = due.JobId, ["status"] = due.Status, ["nativeRunId"] = due.NativeRunId, ["sha"] = due.Sha,
            }
            : null;

        string Render(int count, bool truncated)
        {
            var snapshot = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["instanceId"] = heartbeat.InstanceId,
                ["version"] = options.Version,
                ["configHash"] = options.ConfigHash(),
                ["namespace"] = heartbeat.Namespace,
                ["heartbeatAt"] = heartbeat.HeartbeatAt is { } beat ? Ledger.Iso(beat) : null,
                ["tickSeconds"] = options.TickSeconds,
                ["windmill"] = state?["windmill"]?.DeepClone() ?? new JsonObject { ["reachable"] = false, ["lastOkAt"] = null, ["version"] = null },
                ["desktopWorker"] = state?["desktopWorker"]?.DeepClone() ?? new JsonObject { ["seen"] = false, ["lastPingAt"] = null },
                ["schedule"] = state?["schedule"]?.DeepClone() ?? new JsonObject { ["present"] = false, ["enabled"] = null, ["scriptHash"] = null },
                ["reader"] = state?["reader"]?.DeepClone() ?? new JsonObject { ["available"] = false, ["lastReadAt"] = null, ["held"] = false },
                ["destination"] = new JsonObject { ["qualified"] = qualified, ["hash"] = chatHash },
                ["openOutages"] = openOutages.DeepClone(),
                ["recentNotifications"] = new JsonArray(recent.Take(count).Select(r => (JsonNode)r.DeepClone()).ToArray()),
                ["lastDueDay"] = lastDueDay?.DeepClone(),
                ["truncated"] = truncated,
            };
            return snapshot.ToJsonString();
        }

        var count = recent.Count;
        var text = Render(count, false);
        while (Encoding.UTF8.GetByteCount(text) > MaxBytes && count > 0)
        {
            count = count * 9 / 10;
            text = Render(count, true);
        }
        return text;
    }
}

/// <summary>
/// Minimal read-only HTTP/1.1 listener on exactly the configured bind (a private-network address in
/// production). <c>GET /snapshot.json</c> only; other paths 404, other methods 405.
/// </summary>
public sealed class SnapshotServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<string> _snapshot;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accept;

    public SnapshotServer(string bind, Func<string> snapshot)
    {
        var (host, port) = WatchdogOptions.SplitBind(bind);
        var address = host is "0.0.0.0" or "*" or "+" ? IPAddress.Any : host == "::" ? IPAddress.IPv6Any : IPAddress.Parse(host);
        _listener = new TcpListener(address, port);
        _listener.Start();
        _snapshot = snapshot;
        _accept = Task.Run(AcceptLoopAsync);
    }

    public EndPoint BoundEndpoint => _listener.LocalEndpoint;
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 10_000;
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(requestLine)) return;
                string? line;
                var contentLength = 0;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
                }
                if (contentLength is > 0 and <= 4096)
                {
                    var discard = new char[contentLength];
                    var read = 0;
                    while (read < contentLength)
                    {
                        var n = await reader.ReadAsync(discard.AsMemory(read));
                        if (n == 0) break;
                        read += n;
                    }
                }
                var parts = requestLine.Split(' ');
                var method = parts[0];
                var path = parts.Length > 1 ? parts[1].Split('?')[0] : "/";
                var (status, reason, body) = path != "/snapshot.json"
                    ? (404, "Not Found", "{\"error\":\"not found\"}")
                    : method != "GET"
                        ? (405, "Method Not Allowed", "{\"error\":\"method not allowed\"}")
                        : (200, "OK", _snapshot());
                var bytes = Encoding.UTF8.GetBytes(body);
                var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bytes.Length}\r\n" +
                    (status == 405 ? "Allow: GET\r\n" : "") + "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
            catch (IOException) { }
            catch (SocketException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _accept; } catch (Exception) { }
        _stop.Dispose();
    }
}
