using System.Net.Sockets;
using System.Net.WebSockets;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0716 D-4. One name for why the runner is about to register again.
/// CARD-0717 R2 narrows permanent auth, config and protocol failures here.
/// </summary>
public static class PhoneHomeReconnectReason
{
    public static string Classify(Exception exception, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        HttpRequestException? http = null;
        SocketException? socket = null;
        WebSocketException? webSocket = null;
        PhoneHomeTransportException? transport = null;
        var sawTimeout = false;
        var sawCancel = false;

        foreach (var current in Walk(exception))
        {
            switch (current)
            {
                case HttpRequestException httpEx:
                    http ??= httpEx;
                    break;
                case SocketException socketEx:
                    socket ??= socketEx;
                    break;
                case WebSocketException webSocketEx:
                    webSocket ??= webSocketEx;
                    break;
                case PhoneHomeTransportException transportEx:
                    transport ??= transportEx;
                    break;
                case TimeoutException:
                    sawTimeout = true;
                    break;
                case OperationCanceledException:
                    sawCancel = true;
                    break;
            }
        }

        if (http?.StatusCode is { } status)
            return $"http_{(int)status}";
        if (socket is not null)
            return $"connect_{socket.SocketErrorCode}";
        if (sawTimeout)
            return "registration_timeout";
        if (sawCancel && !stoppingToken.IsCancellationRequested)
            return "connect_timeout";
        if (webSocket is not null)
            return $"ws_{webSocket.WebSocketErrorCode}";
        if (transport is { Code: PhoneHomeProblemTypes.EventOverflow })
            return "overflow";
        return $"other:{exception.GetType().Name}";
    }

    private static IEnumerable<Exception> Walk(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;
            if (current is AggregateException aggregate)
            {
                for (var i = aggregate.InnerExceptions.Count - 1; i >= 0; i--)
                    pending.Push(aggregate.InnerExceptions[i]);
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
    }
}
