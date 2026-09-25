using System.Net;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

/// <summary>
/// CARD-0658: the operator dashboard login routes. Requests are shaped as the public vhost
/// delivers them (loopback peer, rewritten Host, X-Forwarded-*); the token comes from the
/// factory's own file, never the machine's.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class OperatorDashboardLoginTests
{
    private readonly AntiphonWebAppFactory _factory;

    public OperatorDashboardLoginTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Sessions_without_the_header_is_403_operator_token_required()
    {
        var context = await SendAsync("POST", OperatorEndpoints.SessionsPath);

        context.Response.StatusCode.ShouldBe(403);
        (await CodeAsync(context)).ShouldBe("operator_token_required");
    }

    [Test]
    public async Task Sessions_with_a_wrong_token_is_403()
    {
        var context = await SendAsync(
            "POST", OperatorEndpoints.SessionsPath,
            configure: ctx => ctx.Request.Headers[OperatorTokenFile.Header] = "not-the-token");

        context.Response.StatusCode.ShouldBe(403);
        (await CodeAsync(context)).ShouldBe("operator_token_required");
    }

    [Test]
    public async Task Login_with_an_unknown_nonce_is_403_and_sets_no_cookie()
    {
        var unknown = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant().PadRight(64, '0');

        var context = await SendAsync("GET", OperatorEndpoints.LoginPath, "?nonce=" + unknown);

        context.Response.StatusCode.ShouldBe(403);
        context.Response.Headers.SetCookie.Count.ShouldBe(0);
        (await CodeAsync(context)).ShouldBe("operator_login_invalid");
    }

    [Test]
    public async Task Login_over_https_marks_the_cookie_Secure()
    {
        var setCookie = await LoginSetCookieAsync(ctx => ctx.Request.Scheme = "https");

        setCookie.ToLowerInvariant().ShouldContain("secure");
    }

    [Test]
    public async Task Login_through_the_local_https_proxy_marks_the_cookie_Secure()
    {
        // CARD-0676: Caddy terminates TLS and reaches Kestrel over plain http from loopback.
        var setCookie = await LoginSetCookieAsync(ctx =>
        {
            ctx.Request.Scheme = "http";
            ctx.Request.Headers["X-Forwarded-Proto"] = "https";
        });

        setCookie.ToLowerInvariant().ShouldContain("secure");
    }

    [Test]
    public async Task Login_over_http_or_with_a_forwarded_proto_from_a_remote_peer_is_not_Secure()
    {
        var plain = await LoginSetCookieAsync(ctx => ctx.Request.Scheme = "http");
        var remote = await LoginSetCookieAsync(ctx =>
        {
            ctx.Request.Scheme = "http";
            ctx.Connection.RemoteIpAddress = IPAddress.Parse("8.8.8.8");
            ctx.Request.Headers["X-Forwarded-Proto"] = "https";
        });

        plain.ToLowerInvariant().ShouldNotContain("secure");
        remote.ToLowerInvariant().ShouldNotContain("secure");
    }

    /// <summary>Issue a login link with the factory's token, redeem it, and return the Set-Cookie header.</summary>
    private async Task<string> LoginSetCookieAsync(Action<HttpContext> configureLogin)
    {
        var token = OperatorTokenFile.ReadOrCreate(_factory.OperatorTokenPath);
        var issued = await SendAsync(
            "POST", OperatorEndpoints.SessionsPath,
            configure: ctx => ctx.Request.Headers[OperatorTokenFile.Header] = token);
        issued.Response.StatusCode.ShouldBe(200);
        var loginPath = JsonDocument.Parse(await ReadBodyAsync(issued)).RootElement
            .GetProperty("loginPath").GetString()!;
        var query = loginPath[OperatorEndpoints.LoginPath.Length..];

        var login = await SendAsync("GET", OperatorEndpoints.LoginPath, query, configureLogin);

        login.Response.StatusCode.ShouldBe(302);
        var setCookie = login.Response.Headers.SetCookie.ToString();
        setCookie.ShouldStartWith(OperatorDashboardSessions.CookieName + "=");
        return setCookie;
    }

    private Task<HttpContext> SendAsync(
        string method, string path, string? query = null, Action<HttpContext>? configure = null) =>
        _factory.Server.SendAsync(ctx =>
        {
            ctx.Request.Method = method;
            ctx.Request.Path = path;
            if (query is not null)
                ctx.Request.QueryString = new QueryString(query);
            ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
            ctx.Connection.LocalIpAddress = IPAddress.Loopback;
            ctx.Request.Host = new HostString("localhost:17202");
            ctx.Request.Headers["X-Forwarded-For"] = "100.64.0.7";
            ctx.Request.Headers["X-Forwarded-Host"] = "antiphon.desktop.codeperf.net";
            configure?.Invoke(ctx);
        });

    private static async Task<string?> CodeAsync(HttpContext context) =>
        JsonDocument.Parse(await ReadBodyAsync(context)).RootElement.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
