using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Messaging.Tests;

/// <summary>
/// Bind Kestrel to 127.0.0.1:0 so the OS assigns the port at listen time — no
/// reserve-then-release window for another test to steal.
/// </summary>
internal static class KestrelLoopback
{
    public static void ListenEphemeral(IWebHostBuilder webHost) =>
        webHost.ConfigureKestrel(k =>
        {
            // ListenLocalhost(0) refuses dynamic ports; bind 127.0.0.1:0 explicitly.
            k.Listen(IPAddress.Loopback, 0);
        });

    public static string BoundUrl(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not expose server addresses.");
        var address = addresses.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel bound no address.");
        return $"http://127.0.0.1:{new Uri(address).Port}";
    }
}
