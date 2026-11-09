using System.Net;
using System.Net.Sockets;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Start an <see cref="HttpListener"/> on loopback. HttpListener cannot take a
/// pre-opened socket, so the probe is held until the last moment and bind is
/// retried if the port is stolen in the remaining gap. A failed Start closes its
/// listener, so every attempt binds a fresh one (CARD-1808).
/// </summary>
internal static class EphemeralHttpListener
{
    public static string BindLoopback(out HttpListener listener) => BindLoopback(out listener, ReserveLoopbackPort);

    /// <summary>Seam: <paramref name="reserve"/> yields each attempt's port and the release of its hold.</summary>
    internal static string BindLoopback(out HttpListener listener, Func<PortReservation> reserve)
    {
        HttpListenerException? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var reservation = reserve();
            var baseUrl = $"http://localhost:{reservation.Port}/";
            var candidate = new HttpListener();
            candidate.Prefixes.Add(baseUrl);
            try
            {
                reservation.Release();
                candidate.Start();
                listener = candidate;
                return baseUrl;
            }
            catch (HttpListenerException ex)
            {
                last = ex;
                candidate.Close();
            }
            catch
            {
                candidate.Close();
                throw;
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
