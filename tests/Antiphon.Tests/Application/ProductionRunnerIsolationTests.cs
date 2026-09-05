using System.Net;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0204: booting a shared test host launches nothing on any session-runner.
///
/// <para>Before this card, every <see cref="AntiphonWebAppFactory"/> boot ran the real
/// <c>AgentTaskCheckHostedService</c>, which provisioned the check interpreter and started it at
/// once through the real <c>SessionRunner:BaseUrl</c> — the production daemon on 17204 — with the
/// factory's <c>cmd.exe</c> definition. The session row lived in this host's throwaway schema; the
/// detached <c>Antiphon.PtyHost</c> holding an interactive <c>cmd.exe</c> lived forever. One
/// <c>HealthEndpointTests</c> run reproduced it. These pins hold each layer of the fix in place.</para>
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public class ProductionRunnerIsolationTests
{
    private readonly AntiphonWebAppFactory _factory;

    public ProductionRunnerIsolationTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task Booting_the_shared_host_starts_no_session_anywhere()
    {
        // Force the host up (ClassDataSource builds it lazily) and let the startup hosted services
        // run their first pass: the check hosted service provisions on StartAsync, so by the time
        // /api/version answers, any launch it was going to make has been attempted.
        using var client = _factory.CreateClient();
        (await client.GetAsync("/api/version")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var runner = _factory.Services.GetRequiredService<ISessionRunnerClient>();
        runner.ShouldBeSameAs(_factory.SessionRunner,
            "the host's ISessionRunnerClient must be the refusing fake, not the HTTP client");
        _factory.SessionRunner.LaunchAttempts.ShouldBeEmpty(
            "no hosted service may try to start a session when the host boots");

        var settings = _factory.Services.GetRequiredService<IOptions<SessionRunnerSettings>>().Value;
        settings.BaseUrl.ShouldBe(ProductionRunnerGuard.DeadRunnerBaseUrl);
        settings.BaseUrl.ShouldNotContain("17204");
        settings.Enabled.ShouldBeFalse("the event pump has no runner to stream from");
    }

    [Test]
    public async Task The_check_interpreter_is_off_and_its_directory_is_not_the_production_one()
    {
        using var client = _factory.CreateClient();
        (await client.GetAsync("/api/version")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var delegation = _factory.Services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        delegation.CheckInterpreterEnabled.ShouldBeFalse(
            "starting the check interpreter is the launch that leaked a cmd.exe pty-host per boot");
        CheckInterpreterProvisioner.ResolveWorkingDirectory(delegation)
            .ShouldNotBe(@"C:\logs\antiphon\check-interpreter",
                "a test host that re-enables the interpreter must still not share the production directory");

        // Disabled means the provisioner never wrote the agent: the slug from appsettings is absent
        // from this host's own schema.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slug = CheckInterpreterProvisioner.Slug(delegation);
        (await db.Agents.AnyAsync(a => a.Slug == slug)).ShouldBeFalse(
            $"the check interpreter '{slug}' must not be provisioned in a test host");
    }

    [Test]
    public async Task The_diagnose_seat_is_off_and_its_directory_is_not_the_production_one()
    {
        using var client = _factory.CreateClient();
        (await client.GetAsync("/api/version")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var delegation = _factory.Services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        delegation.DiagnoseEnabled.ShouldBeFalse(
            "starting the diagnose seat is the same launch leak as the check interpreter (CARD-0352)");
        DiagnoseProvisioner.ResolveWorkingDirectory(delegation)
            .ShouldNotBe(@"C:\logs\antiphon\diagnose",
                "a test host that re-enables diagnose must still not share the production directory");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slug = DiagnoseProvisioner.Slug(delegation);
        (await db.Agents.AnyAsync(a => a.Slug == slug)).ShouldBeFalse(
            $"the diagnose seat '{slug}' must not be provisioned in a test host");
    }

    [Test]
    public async Task The_output_distiller_is_off_and_its_directory_is_not_the_production_one()
    {
        using var client = _factory.CreateClient();
        (await client.GetAsync("/api/version")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var delegation = _factory.Services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        delegation.OutputDistillerEnabled.ShouldBeFalse(
            "starting the output distiller is the same launch leak as the check interpreter (CARD-0330)");
        OutputDistillerProvisioner.ResolveWorkingDirectory(delegation)
            .ShouldNotBe(@"C:\logs\antiphon\output-distiller",
                "a test host that re-enables the distiller must still not share the production directory");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slug = OutputDistillerProvisioner.Slug(delegation);
        (await db.Agents.AnyAsync(a => a.Slug == slug)).ShouldBeFalse(
            $"the output distiller '{slug}' must not be provisioned in a test host");
    }
}
