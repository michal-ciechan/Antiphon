using System.Net.Sockets;
using System.Net.WebSockets;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// CARD-0716 D-6: the websocket and socket error names a handshake fault carries.
/// Both the desktop ended line and the runner's ended line print this pair.
/// </summary>
public static class PhoneHomeTransportFault
{
    public static (string WsError, string SocketError) Describe(WebSocketException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return (exception.WebSocketErrorCode.ToString(), FindSocket(exception)?.SocketErrorCode.ToString() ?? "none");
    }

    private static SocketException? FindSocket(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is SocketException socket)
                return socket;
            exception = exception.InnerException;
        }

        return null;
    }
}
