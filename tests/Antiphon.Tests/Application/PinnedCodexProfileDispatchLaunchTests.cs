using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Tui;
using Antiphon.Tests.Agents;
using Antiphon.Tests.AgentTui;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0140 S4 / T10 — the argv the adapter factory actually receives for a pinned, stopped
/// Codex-profile standing agent is Codex's, and the session carries provenance. This is the
/// test that would have caught probe 1 at the level probe 1 was measured.
/// </summary>
[Category("Integration")]
[NotInParallel(["MessageQueue", "AgentQueue"])]
public class PinnedCodexProfileDispatchLaunchTests
{
    [Test]
    public async Task T10_pinned_stopped_Codex_agent_argv_is_codex_and_session_carries_provenance()
    {
        await using var h = await CreateHarnessAsync();
        var factory = Factory(h);
        var exe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var (profileId, revisionId) = await SeedCodexProfileAsync(exe);
        var agentId = await SeedStoppedCodexAgentAsync(h, profileId, modelId: "gpt-5.6-terra");
        var taskId = await SeedQueuedPinAsync(h, agentId);

        using (var scope = h.Provider.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            await dispatcher.TickAsync(CancellationToken.None);
        }

        await h.Provider.GetRequiredService<AgentSessionLaunchQueue>()
            .WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        // TickAsync is a global sweep over the shared fixture DB, so leftover Queued rows from a
        // sibling test may also launch. Identify THIS pin by the revision arguments only the
        // managed Codex profile puts on the command line.
        factory.Created.ShouldNotBeEmpty();
        var adapter = factory.Created.Single(a =>
            a.StartedArgs.Contains("--dangerously-bypass-approvals-and-sandbox"));
        var args = adapter.StartedArgs.ToList();
        args.ShouldContain("--no-alt-screen");
        args.ShouldContain("--dangerously-bypass-approvals-and-sandbox");
        args.ShouldContain("-c");
        args.ShouldContain(a => a.StartsWith("developer_instructions=", StringComparison.Ordinal));
        args.ShouldNotContain("--append-system-prompt");
        args.ShouldNotContain("--name");
        args.Count(a => a == "--model").ShouldBe(1);
        args[args.IndexOf("--model") + 1].ShouldBe("gpt-5.6-terra");

        await using var db = BridgeQueueHarness.CreateContext();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched);
        task.AgentSessionId.ShouldNotBeNull();
        var session = await db.AgentSessions.AsNoTracking()
            .SingleAsync(s => s.Id == task.AgentSessionId!.Value);
        session.AgentKind.ShouldBe(AgentKind.Codex);
        session.DefinitionName.ShouldBe("codex");
        session.TuiProfileRevisionId.ShouldBe(revisionId);
        session.EffectiveModelId.ShouldBe("gpt-5.6-terra");
    }

    private static RegisteringAdapterFactory Factory(BridgeQueueHarness h) =>
        (RegisteringAdapterFactory)h.Provider.GetRequiredService<IAgentProtocolAdapterFactory>();

    private static Task<BridgeQueueHarness> CreateHarnessAsync() =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = false,
            Delegation = new DelegationSettings
            {
                MaxConcurrentTasks = 512,
                PoolIdleRetireMinutes = 525_600,
                PoolMaxIdlePerDirectory = int.MaxValue,
                RolePolicy = new(StringComparer.OrdinalIgnoreCase),
                FinalMessageGraceSeconds = 0,
                SubagentGraceMinutes = 0,
            },
            ConfigureServices = services =>
            {
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
                    {
                        DefaultDefinition = "fake",
                        Definitions =
                        {
                            ["fake"] = new AgentDefinition
                            {
                                Kind = "Codex",
                                Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                            },
                            ["codex"] = new AgentDefinition
                            {
                                Kind = "Codex",
                                Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                            },
                            ["claude"] = new AgentDefinition
                            {
                                Kind = "ClaudeCode",
                                Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                            },
                        },
                    }));
                services.AddSingleton<IAgentProtocolAdapterFactory>(sp =>
                    new RegisteringAdapterFactory(sp.GetRequiredService<AgentSessionRuntime>()));
                services.AddSingleton<RecordingLaunchSecretProtector>();
                services.AddSingleton<IAgentTuiSecretProtector>(sp =>
                    sp.GetRequiredService<RecordingLaunchSecretProtector>());
                services.AddSingleton<AgentTuiMetrics>();
                services.AddSingleton<AgentTuiRunnerCatalog>();
                services.AddScoped<AgentTuiLaunchResolver>();
                services.AddSingleton<DelegationWorkspaceResolver>();
                // BridgeQueueHarness already holds GitWorkspaceService and its NoWorktreeManager; the
                // helper's TryAdd keeps both and fills in the rest of the graph (CARD-0297).
                services.AddDelegationWorktreeGraph(new GitSettings
                {
                    WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-t10-wt"),
                });
                services.AddScoped<AgentTaskService>();
                services.AddScoped<IDelegateSessionStopper>(sp =>
                    sp.GetRequiredService<AgentSessionService>());
                services.AddScoped<AgentTaskDispatcher>();
            },
        });

    private static async Task<(Guid ProfileId, Guid RevisionId)> SeedCodexProfileAsync(string exe)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"t10-codex-{Guid.NewGuid():N}"[..20],
            Kind = AgentKind.Codex,
            IsEnabled = true,
            IsDefault = false,
            Source = AgentTuiProfileSource.Operator,
            SourceDefinitionName = "codex",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var revision = new AgentTuiProfileRevision
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            RevisionNumber = 1,
            Executable = exe,
            ArgumentsJson = JsonSerializer.Serialize(new[]
            {
                "--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox",
            }),
            DiscoveryArgumentsJson = "[]",
            VersionArgumentsJson = "[]",
            AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
            NonSecretEnvironmentJson = "{}",
            SecretEnvironmentNamesJson = "[]",
            ModelArgumentName = "--model",
            Guidance = "CARD-0140 T10",
            CreatedAt = now,
        };
        await using var db = BridgeQueueHarness.CreateContext();
        db.AgentTuiProfiles.Add(profile);
        db.AgentTuiProfileRevisions.Add(revision);
        await db.SaveChangesAsync();
        profile.ActiveRevisionId = revision.Id;
        db.AgentTuiModels.Add(new AgentTuiModel
        {
            Id = Guid.NewGuid(),
            ProfileId = profile.Id,
            Identifier = "gpt-5.6-terra",
            DisplayName = "gpt-5.6-terra",
            Source = AgentTuiModelSource.Operator,
            Availability = AgentTuiModelAvailability.Verified,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (profile.Id, revision.Id);
    }

    private static async Task<Guid> SeedStoppedCodexAgentAsync(
        BridgeQueueHarness h, Guid profileId, string modelId)
    {
        var now = DateTime.UtcNow;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "CARD-0140 T10 Codex",
            Slug = $"t10-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = Path.Combine(h.TempRoot, "workspace"),
            Details = "Stopped standing Codex agent.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.High,
            ModelId = modelId,
            Kind = AgentKind.Codex,
            TuiProfileId = profileId,
            AlwaysOn = false,
            IsPoolDelegate = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await using var db = BridgeQueueHarness.CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task<Guid> SeedQueuedPinAsync(BridgeQueueHarness h, Guid agentId)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "CARD-0140 T10 pin",
            Goal = "CARD-0140 T10 pin",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            AgentKind = AgentKind.Codex,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.Combine(h.TempRoot, "workspace"),
            AgentId = agentId,
            Ephemeral = false,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = BridgeQueueHarness.CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class RegisteringAdapterFactory(AgentSessionRuntime runtime) : IAgentProtocolAdapterFactory
    {
        public List<FakeAgentProtocolAdapter> Created { get; } = [];

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            var adapter = new FakeAgentProtocolAdapter { RegisterOnStart = runtime };
            Created.Add(adapter);
            return adapter;
        }
    }
}
