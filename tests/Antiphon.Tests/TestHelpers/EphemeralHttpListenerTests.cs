using System.Net;
using System.Net.Sockets;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1808: a failed <see cref="HttpListener.Start"/> closes that listener, so the
/// stolen-port retry must bind a fresh listener on every attempt.
/// </summary>
[Category("Unit")]
public sealed class EphemeralHttpListenerTests
{
    [Test]
    public async Task Stolen_port_retries_on_a_fresh_listener()
    {
        var thief = new TcpListener(IPAddress.Loopback, 0);
        thief.Start();
        var stolen = ((IPEndPoint)thief.LocalEndpoint).Port;
        var attempts = 0;
        HttpListener? listener = null;
        try
        {
            var baseUrl = EphemeralHttpListener.BindLoopback(out listener, () => attempts++ == 0
                ? new EphemeralHttpListener.PortReservation(stolen, () => { })
                : EphemeralHttpListener.ReserveLoopbackPort());

            attempts.ShouldBe(2, "the stolen port must fail the first Start and the second attempt must bind");
            listener.IsListening.ShouldBeTrue();
            new Uri(baseUrl).Port.ShouldNotBe(stolen);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var get = http.GetAsync(baseUrl + "probe");
            var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(30));
            context.Request.Url!.AbsolutePath.ShouldBe("/probe");
            context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            context.Response.Close();
            (await get).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
        finally
        {
            listener?.Close();
            thief.Stop();
        }
    }

    [Test]
    public void Exhausted_retries_report_the_bind_failure()
    {
        var thief = new TcpListener(IPAddress.Loopback, 0);
        thief.Start();
        var stolen = ((IPEndPoint)thief.LocalEndpoint).Port;
        var attempts = 0;
        try
        {
            var error = Should.Throw<InvalidOperationException>(() => EphemeralHttpListener.BindLoopback(out _, () =>
            {
                attempts++;
                return new EphemeralHttpListener.PortReservation(stolen, () => { });
            }));

            attempts.ShouldBe(8);
            error.GetType().ShouldBe(typeof(InvalidOperationException), "a disposed-listener retry is an ObjectDisposedException, not the exhausted bind");
            error.InnerException.ShouldBeOfType<HttpListenerException>();
        }
        finally
        {
            thief.Stop();
        }
    }
}
