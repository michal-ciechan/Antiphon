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

    /// <summary>Start a listener on a free loopback port.</summary>
    public static HttpListener StartLoopback(out string baseUrl) => StartLoopback(out baseUrl, ReserveLoopbackPort);

    /// <summary>Seam: <paramref name="reserve"/> yields each attempt's port and the release of its hold.</summary>
    internal static HttpListener StartLoopback(out string baseUrl, Func<PortReservation> reserve)
    {
        var listener = new HttpListener();
        HttpListenerException? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var reservation = reserve();
            baseUrl = $"http://localhost:{reservation.Port}/";
            listener.Prefixes.Clear();
            listener.Prefixes.Add(baseUrl);
            try
            {
                reservation.Release();
                listener.Start();
                return listener;
            }
            catch (HttpListenerException ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException("Failed to bind an ephemeral HttpListener on loopback.", last);
    }

    internal static PortReservation ReserveLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return new PortReservation(((IPEndPoint)probe.LocalEndpoint).Port, probe.Stop);
    }

    internal sealed record PortReservation(int Port, Action Release);
}
