using System.Net;
using System.Net.Sockets;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Bind an <see cref="HttpListener"/> on loopback. HttpListener cannot take a
/// pre-opened socket, so the probe is held until the last moment and bind is
/// retried if the port is stolen in the remaining gap.
/// </summary>
internal static class EphemeralHttpListener
{
    public static string BindLoopback(HttpListener listener)
    {
        HttpListenerException? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            var baseUrl = $"http://localhost:{port}/";
            listener.Prefixes.Clear();
            listener.Prefixes.Add(baseUrl);
            try
            {
                probe.Stop();
                listener.Start();
                return baseUrl;
            }
            catch (HttpListenerException ex)
            {
                last = ex;
                try { probe.Stop(); }
                catch (Exception) { /* already stopped */ }
            }
        }

        throw new InvalidOperationException("Failed to bind an ephemeral HttpListener on loopback.", last);
    }
}
