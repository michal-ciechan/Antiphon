using System.Net;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Server;
using Hangfire.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0298: Hangfire worker stays off in Program test boots; storage, recurring registration,
/// and the dashboard behave as configured. CARD-0658: the dashboard needs the operator credential
/// (header token or a live dashboard session cookie); the client address is never consulted.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class HangfireStartupSafetyTests
{
    private readonly AntiphonWebAppFactory _factory;

    public HangfireStartupSafetyTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Assembly_guard_and_factory_override_disable_the_Hangfire_worker()
    {
        Environment.GetEnvironmentVariable(ProductionRunnerGuard.HangfireServerEnabledEnvVar)
            .ShouldBe("false");
        _factory.Services.GetService<IBackgroundProcessingServer>().ShouldBeNull();
        _factory.Services.GetServices<IHostedService>()
            .Any(s => (s.GetType().FullName ?? "").Contains("Hangfire", StringComparison.OrdinalIgnoreCase)
                      || (s.GetType().Name ?? "").Contains("BackgroundJobServer", StringComparison.OrdinalIgnoreCase))
            .ShouldBeFalse();
        // CARD-0336: ListCalls / ZombieCensus.Calls are process-wide on the shared
        // AntiphonWebAppFactory. Isolation is 0; a full session is not. Keep the env-var /
        // IBackgroundProcessingServer / no Hangfire hosted-service pins.
        await Task.CompletedTask;
    }

    [Test]
    public async Task Configured_in_memory_storage_expires_after_eight_days()
    {
        var storage = _factory.Services.GetRequiredService<JobStorage>().ShouldBeOfType<InMemoryStorage>();
        storage.Options.MaxExpirationTime.ShouldBe(TimeSpan.FromDays(8));
        HangfireConfiguration.CreateStorageOptions(new HangfireSettings { HistoryRetentionDays = 8 })
            .MaxExpirationTime.ShouldBe(TimeSpan.FromDays(8));
        await Task.CompletedTask;
    }

    [Test]
    public async Task Recurring_census_job_is_not_registered_when_the_worker_is_disabled()
    {
        var storage = _factory.Services.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        connection.GetRecurringJobs().ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Recurring_census_job_is_re_added_on_a_fresh_in_memory_host_with_London_daily_cron()
    {
        var settings = new ZombieCensusSettings();
        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        var manager = new RecurringJobManager(storage);
        HangfireConfiguration.AddOrUpdateCensusJob(manager, settings);
        using var connection = storage.GetConnection();
        var job = connection.GetRecurringJobs().ShouldHaveSingleItem();
        job.Id.ShouldBe("antiphon:zombie-census");
        job.Cron.ShouldBe("30 9 * * *");
        job.TimeZoneId.ShouldBe("Europe/London");

        await Task.CompletedTask;
    }

    [Test]
    public async Task AddOrUpdateCensusJob_works_from_a_DI_resolved_manager_without_priming_JobStorage_Current()
    {
        // Regression: Program.cs resolves IRecurringJobManager from DI (mirrored here via a plain
        // ServiceCollection, not the static RecurringJob API) - AddHangfire's UseInMemoryStorage
        // does not synchronously set the JobStorage.Current global, so the static call crashed
        // every real server startup with "Current JobStorage instance has not been initialized
        // yet" even though this class's other test above passed by manually priming that global.
        var services = new ServiceCollection();
        services.AddHangfire(config =>
            config.UseInMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings())));
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IRecurringJobManager>();
        var settings = new ZombieCensusSettings();

        HangfireConfiguration.AddOrUpdateCensusJob(manager, settings);

        var storage = provider.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        connection.GetRecurringJobs().ShouldHaveSingleItem().Id.ShouldBe("antiphon:zombie-census");
    }

    [Test]
    public async Task Recurring_residue_job_is_re_added_on_a_fresh_in_memory_host_with_London_daily_cron()
    {
        var settings = new WorktreeResidueSettings();
        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        var manager = new RecurringJobManager(storage);
        HangfireConfiguration.AddOrUpdateWorktreeResidueJob(manager, settings);
        using var connection = storage.GetConnection();
        var job = connection.GetRecurringJobs().ShouldHaveSingleItem();
        job.Id.ShouldBe("antiphon:worktree-residue");
        job.Cron.ShouldBe("0 10 * * *");
        job.TimeZoneId.ShouldBe("Europe/London");

        await Task.CompletedTask;
    }

    [Test]
    public async Task AddOrUpdateWorktreeResidueJob_works_from_a_DI_resolved_manager_without_priming_JobStorage_Current()
    {
        var services = new ServiceCollection();
        services.AddHangfire(config =>
            config.UseInMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings())));
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IRecurringJobManager>();
        var settings = new WorktreeResidueSettings();

        HangfireConfiguration.AddOrUpdateWorktreeResidueJob(manager, settings);

        var storage = provider.GetRequiredService<JobStorage>();
        using var connection = storage.GetConnection();
        connection.GetRecurringJobs().ShouldHaveSingleItem().Id.ShouldBe("antiphon:worktree-residue");
    }

    [Test]
    public async Task Request_without_a_credential_is_refused_whatever_its_address()
    {
        var context = await SendDashboardAsync(IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.5"));

        context.Response.StatusCode.ShouldBeOneOf(401, 403);
    }

    // CARD-0658: the dashboard used to admit every loopback request, and every request Kestrel
    // receives is loopback (Aspire's DCP proxy, Vite, Caddy). The credential decides; the address
    // and X-Forwarded-* decide nothing in either direction.

    [Test]
    public async Task C658_Loopback_proxy_shaped_request_without_credential_is_refused()
    {
        var context = await SendDashboardAsync(IPAddress.Loopback, IPAddress.Loopback);

        context.Response.StatusCode.ShouldBeOneOf(401, 403);
        (await ReadBodyAsync(context)).ShouldNotContain("Hangfire Dashboard");
    }

    [Test]
    public async Task C658_Header_token_grants_the_dashboard_from_a_non_loopback_address()
    {
        var token = OperatorTokenFile.ReadOrCreate(_factory.OperatorTokenPath);

        var context = await SendDashboardAsync(
            IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.5"),
            request => request.Headers[OperatorTokenFile.Header] = token);

        context.Response.StatusCode.ShouldBeOneOf(200, 302);
    }

    [Test]
    public async Task C658_Wrong_header_token_is_refused_from_loopback()
    {
        var context = await SendDashboardAsync(
            IPAddress.Loopback, IPAddress.Loopback,
            request => request.Headers[OperatorTokenFile.Header] = "not-the-token");

        context.Response.StatusCode.ShouldBeOneOf(401, 403);
    }

    [Test]
    public async Task C658_Login_link_sets_a_dashboard_cookie_that_grants_once_redeemed()
    {
        var token = OperatorTokenFile.ReadOrCreate(_factory.OperatorTokenPath);

        var issued = await _factory.Server.SendAsync(ctx =>
        {
            ctx.Request.Method = "POST";
            ctx.Request.Path = OperatorEndpoints.SessionsPath;
            ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
            ctx.Connection.LocalIpAddress = IPAddress.Loopback;
            ctx.Request.Headers[OperatorTokenFile.Header] = token;
        });
        issued.Response.StatusCode.ShouldBe(200);
        var issuedBody = await ReadBodyAsync(issued);
        issuedBody.ShouldNotContain(token);
        var loginPath = JsonDocument.Parse(issuedBody).RootElement.GetProperty("loginPath").GetString()!;
        loginPath.ShouldStartWith(OperatorEndpoints.LoginPath + "?nonce=");
        var nonce = loginPath[(OperatorEndpoints.LoginPath + "?nonce=").Length..];
        nonce.Length.ShouldBe(64);

        var login = await SendLoginAsync(nonce);
        login.Response.StatusCode.ShouldBe(302);
        login.Response.Headers.Location.ToString().ShouldBe("/hangfire");
        var setCookie = login.Response.Headers.SetCookie.ToString();
        setCookie.ShouldStartWith(OperatorDashboardSessions.CookieName + "=");
        var lowered = setCookie.ToLowerInvariant();
        lowered.ShouldContain("httponly");
        lowered.ShouldContain("samesite=strict");
        lowered.ShouldContain("path=/hangfire");
        lowered.ShouldContain("max-age=43200");
        lowered.ShouldNotContain("secure");
        (await ReadBodyAsync(login)).ShouldNotContain(nonce);
        var cookie = setCookie.Split(';')[0];

        var dashboard = await SendDashboardAsync(
            IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.5"),
            request => request.Headers.Cookie = cookie);
        dashboard.Response.StatusCode.ShouldBeOneOf(200, 302);

        var again = await SendLoginAsync(nonce);
        again.Response.StatusCode.ShouldBe(403);
        JsonDocument.Parse(await ReadBodyAsync(again)).RootElement.GetProperty("code").GetString()
            .ShouldBe("operator_login_invalid");
        again.Response.Headers.SetCookie.Count.ShouldBe(0);
    }

    private Task<HttpContext> SendLoginAsync(string nonce) =>
        _factory.Server.SendAsync(ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.Path = OperatorEndpoints.LoginPath;
            ctx.Request.QueryString = new QueryString("?nonce=" + nonce);
            ApplyProxyShape(ctx, IPAddress.Loopback, IPAddress.Loopback);
        });

    /// <summary>A GET /hangfire shaped as the public vhost would deliver it.</summary>
    private Task<HttpContext> SendDashboardAsync(
        IPAddress remote, IPAddress local, Action<HttpRequest>? configure = null) =>
        _factory.Server.SendAsync(ctx =>
        {
            ctx.Request.Method = "GET";
            ctx.Request.Path = "/hangfire";
            ApplyProxyShape(ctx, remote, local);
            configure?.Invoke(ctx.Request);
        });

    private static void ApplyProxyShape(HttpContext ctx, IPAddress remote, IPAddress local)
    {
        ctx.Connection.RemoteIpAddress = remote;
        ctx.Connection.LocalIpAddress = local;
        ctx.Request.Host = new HostString("localhost:17202");
        ctx.Request.Headers["X-Forwarded-For"] = "100.64.0.7";
        ctx.Request.Headers["X-Forwarded-Host"] = "antiphon.desktop.codeperf.net";
    }

    // TestServer hands back an unread, forward-only response body.
    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
