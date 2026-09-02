using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0324 S5: 409 <c>provider_sign_in_required</c> at create for registry-Grok when
/// the store is Absent. The override queues; the dispatcher remains the launch backstop.
/// </summary>
[Category("Integration")]
public class ProviderSignInRequiredCreateTests
{
    [Test]
    public async Task Create_returns_409_provider_sign_in_required_when_the_store_is_Absent()
    {
        using var workspace = new TempDir("antiphon-signin-create-ws");
        using var grokHome = new TempDir("antiphon-signin-create-home");
        await using var db = CreateContext();
        var service = CreateService(db, workspace.Path, grokHome.Path);

        var ex = await Should.ThrowAsync<ProviderSignInRequiredException>(
            () => service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "run on grok", Role: AgentTaskRole.Code)
                {
                    AgentKind = AgentKind.Grok,
                },
                new AgentTaskService.Caller(null, null, workspace.Path),
                CancellationToken.None));

        ex.Code.ShouldBe("provider_sign_in_required");
        ex.GrokHome.ShouldBe(grokHome.Path);
        ex.Extensions.ShouldNotBeNull()["remedy"].ShouldBe("grok login");
        ex.Extensions["agentKind"].ShouldBe("Grok");

        await using var verify = CreateContext();
        (await verify.AgentTasks.CountAsync(t => t.Goal == "run on grok"))
            .ShouldBe(0, "a refused create must not leave a queued row");
    }

    [Test]
    public async Task AllowUnauthenticatedProvider_queues_the_task()
    {
        using var workspace = new TempDir("antiphon-signin-create-ws");
        using var grokHome = new TempDir("antiphon-signin-create-home");
        await using var db = CreateContext();
        var service = CreateService(db, workspace.Path, grokHome.Path);

        var created = await service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "queue anyway", Role: AgentTaskRole.Code)
            {
                AgentKind = AgentKind.Grok,
                AllowUnauthenticatedProvider = true,
            },
            new AgentTaskService.Caller(null, null, workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.AgentKind.ShouldBe(AgentKind.Grok);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == created.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        await verify.AgentTaskEvents.Where(e => e.AgentTaskId == created.Id).ExecuteDeleteAsync();
        await verify.AgentTasks.Where(t => t.Id == created.Id).ExecuteDeleteAsync();
    }

    private static AgentTaskService CreateService(AppDbContext db, string workspace, string grokHome)
    {
        var settings = new DelegationSettings
        {
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 5.00m,
            AllowedRoots = [workspace],
        };
        var registry = new AgentRegistrySettings
        {
            GrokCredentialProbeEnabled = true,
            Definitions =
            {
                ["grok"] = new AgentDefinition
                {
                    Kind = "Grok",
                    Exe = "grok",
                    Env = new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
                },
            },
        };
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            registrySettings: Options.Create(registry));
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempDir(string prefix) : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory(prefix).FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
