using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0822. Covered writes regenerate the instructions file and deliver one WhenIdle
/// System note. The harness does not register the hosted sweep.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class OrchestratorInstructionsRefreshTests
{
    [Test]
    public async Task Runner_defaults_put_rewrites_the_file_and_delivers_one_notice_to_an_idle_standing_orchestrator()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);

        await using var scope = h.Provider.CreateAsyncScope();
        var before = await StateAsync(scope.ServiceProvider);
        var defaults = scope.ServiceProvider.GetRequiredService<RunnerDefaultSettingsService>();
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(
            new PutRunnerDefaultsRequest(current.Revision, "desktop", [], "switch to desktop", "Human"),
            null,
            CancellationToken.None);
        await Instructions(h).WhenIdleAsync();

        var row = await StateAsync(scope.ServiceProvider);
        row.ShouldNotBeNull();
        row.Revision.ShouldBe((before?.Revision ?? 0) + 1);
        var file = File.ReadAllText(row.WrittenPath);
        file.ShouldStartWith($"[orchestrator-instructions v{row.Version} rev {row.Revision}]");
        file.ShouldContain("desktop");
        file.ShouldContain("switch to desktop");
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        var body = h.Adapter.SubmittedBodies[0];
        body.ShouldStartWith("[System note from Antiphon:");
        body.ShouldContain(row.WrittenPath);
        body.ShouldContain("caps:");
        (await SessionVersionAsync(h)).ShouldBe(row.Version);
    }

    [Test]
    [Arguments("host-budget")]
    [Arguments("hold-upsert")]
    [Arguments("hold-clear")]
    [Arguments("routing-pin")]
    [Arguments("runner-eligibility")]
    [Arguments("dispatch-concurrency")]
    public async Task Covered_writes_each_regenerate_and_notify(string kind)
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        var section = await CoveredWriteAsync(h, kind);

        var row = await ReadStateAsync(h);
        row.Revision.ShouldBeGreaterThan(0);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        h.Adapter.SubmittedBodies[0].ShouldContain(section);
        h.Adapter.SubmittedBodies[0].ShouldContain(row.WrittenPath);
    }

    [Test]
    public async Task An_orchestrator_task_session_receives_the_notice_when_idle()
    {
        await using var h = await CreateHarnessAsync();
        await MarkPoolDelegateAsync(h);
        await AddTaskAsync(h, AgentTaskKind.Orchestrator, AgentTaskStatus.Working);

        await using var scope = h.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HostBudgetService>()
            .UpsertAsync("local", 3, "raise local", CancellationToken.None);
        await Instructions(h).WhenIdleAsync();

        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        h.Adapter.SubmittedBodies[0].ShouldContain("caps:");
    }

    [Test]
    public async Task Launch_records_the_current_version_on_the_session_and_the_env_carries_the_path_and_url()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        await Instructions(h).ReconcileNowAsync("startup");
        var row = await ReadStateAsync(h);

        await using var scope = h.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
        var composition = await scope.ServiceProvider.GetRequiredService<AgentSessionLaunchComposer>()
            .ComposeForAgentAsync(agent, CancellationToken.None);
        composition.ExtraEnv.ShouldContainKey(OrchestratorInstructionsLaunch.PathVariable);
        composition.ExtraEnv.ShouldContainKey(OrchestratorInstructionsLaunch.UrlVariable);
        composition.ExtraEnv[OrchestratorInstructionsLaunch.PathVariable].ShouldBe(h.Delegation.OrchestratorInstructions.Path);
        composition.ExtraEnv[OrchestratorInstructionsLaunch.UrlVariable].ShouldEndWith("/api/orchestrator-instructions");
        composition.OrchestratorInstructionsVersion.ShouldBe(row.Version);

        var dispatcher = Dispatcher(h, scope.ServiceProvider);
        var orchestrator = dispatcher.BuildEnv(TaskOf(AgentTaskKind.Orchestrator), agent, new AgentSession { Id = h.SessionId });
        orchestrator.ShouldContainKey(OrchestratorInstructionsLaunch.PathVariable);
        orchestrator.ShouldContainKey(OrchestratorInstructionsLaunch.UrlVariable);
        var worker = dispatcher.BuildEnv(TaskOf(AgentTaskKind.Worker), agent, new AgentSession { Id = h.SessionId });
        worker.ShouldNotContainKey(OrchestratorInstructionsLaunch.PathVariable);
        worker.ShouldNotContainKey(OrchestratorInstructionsLaunch.UrlVariable);
    }

    [Test]
    public async Task Sweep_regenerates_after_a_direct_row_edit_without_a_trigger()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.Off;
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.HostBudgets.Add(new HostBudget
            {
                HostId = "local",
                MaxInFlight = 2,
                Reason = "seed",
                UpdatedAt = h.Now,
                Revision = 1,
            });
            await db.SaveChangesAsync();
        }

        await Instructions(h).ReconcileNowAsync("startup");
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.All;
        var before = await ReadStateAsync(h);
        await using (var scope = h.Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().HostBudgets
                .Where(budget => budget.HostId == "local")
                .ExecuteUpdateAsync(u => u.SetProperty(budget => budget.MaxInFlight, 9));
        }

        await Instructions(h).ReconcileNowAsync("sweep");
        var after = await ReadStateAsync(h);
        after.Revision.ShouldBe(before.Revision + 1);
        after.Version.ShouldNotBe(before.Version);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        h.Adapter.SubmittedBodies[0].ShouldContain("caps:");
        (await SessionVersionAsync(h)).ShouldBe(after.Version);
    }

    [Test]
    public async Task Startup_regenerates_and_a_pre_feature_session_gets_one_notice()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        (await SessionVersionAsync(h)).ShouldBeNull();

        await Instructions(h).ReconcileNowAsync("startup");

        var row = await ReadStateAsync(h);
        File.Exists(row.WrittenPath).ShouldBeTrue();
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        (await SessionVersionAsync(h)).ShouldBe(row.Version);
    }

    [Test]
    public async Task An_unchanged_write_neither_rewrites_nor_notifies()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        await using var scope = h.Provider.CreateAsyncScope();
        var defaults = scope.ServiceProvider.GetRequiredService<RunnerDefaultSettingsService>();
        var current = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(
            new PutRunnerDefaultsRequest(current.Revision, "desktop", [], "switch to desktop", "Human"),
            null,
            CancellationToken.None);
        await Instructions(h).WhenIdleAsync();
        var row = await StateAsync(scope.ServiceProvider);
        var bytes = await File.ReadAllBytesAsync(row!.WrittenPath);
        var mtime = File.GetLastWriteTimeUtc(row.WrittenPath);

        var again = await defaults.GetAsync(CancellationToken.None);
        await defaults.PutAsync(
            new PutRunnerDefaultsRequest(again.Revision, "desktop", [], "switch to desktop", "Human"),
            null,
            CancellationToken.None);
        await Instructions(h).WhenIdleAsync();

        (await File.ReadAllBytesAsync(row.WrittenPath)).ShouldBe(bytes);
        File.GetLastWriteTimeUtc(row.WrittenPath).ShouldBe(mtime);
        (await StateAsync(scope.ServiceProvider))!.Revision.ShouldBe(row.Revision);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
    }

    [Test]
    public async Task A_busy_session_gets_the_notice_after_its_turn_ends_and_a_second_change_replaces_the_pending_row()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        await h.MarkWorkingAsync();
        await using var scope = h.Provider.CreateAsyncScope();
        var budgets = scope.ServiceProvider.GetRequiredService<HostBudgetService>();
        await budgets.UpsertAsync("local", 2, "first budget", CancellationToken.None);
        await Instructions(h).WhenIdleAsync();
        await budgets.UpsertAsync("local", 4, "second budget", CancellationToken.None);
        await Instructions(h).WhenIdleAsync();

        await using var db = NewDb(h);
        var pending = await db.SessionQueuedMessages.SingleAsync(message =>
            message.AgentSessionId == h.SessionId && message.Status == QueuedMessageStatus.Pending);
        pending.NoteHeader.ShouldBe(OrchestratorInstructionsService.NoteHeader);
        pending.Body.ShouldContain("changed more than once since v");
        pending.Body.ShouldContain("caps:");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();

        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        h.Adapter.SubmittedBodies[0].ShouldContain("changed more than once since v");
    }

    [Test]
    public async Task A_worker_delegate_session_receives_nothing()
    {
        await using var h = await CreateHarnessAsync();
        await MarkPoolDelegateAsync(h);
        await AddTaskAsync(h, AgentTaskKind.Worker, AgentTaskStatus.Working);
        await using var scope = h.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HostBudgetService>()
            .UpsertAsync("local", 3, "raise local", CancellationToken.None);
        await Instructions(h).WhenIdleAsync();

        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        await using var db = NewDb(h);
        (await db.SessionQueuedMessages.AnyAsync(message => message.AgentSessionId == h.SessionId)).ShouldBeFalse();
    }

    [Test]
    public async Task Secret_bearing_rows_never_reach_the_file_or_the_notice()
    {
        const string apiKey = "c822-api-key-sentinel";
        const string tuiSecret = "c822-tui-secret-sentinel";
        const string llmKey = "c822-llm-key-sentinel";
        const string capability = "c822c822c822c822c822c822c822c822c822c822c822c822c822c822c822c822";
        const string launchEnv = "c822-launch-env-sentinel";

        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        await using (var db = NewDb(h))
        {
            var now = h.Now;
            db.ApiKeys.Add(new ApiKey
            {
                Id = Guid.NewGuid(),
                Name = "c822-key",
                Ciphertext = apiKey,
                ProtectionVersion = "test",
                CreatedAt = now,
                UpdatedAt = now,
            });
            var profileId = Guid.NewGuid();
            db.AgentTuiProfiles.Add(new AgentTuiProfile
            {
                Id = profileId,
                DisplayName = "c822",
                Kind = AgentKind.ClaudeCode,
                IsEnabled = true,
                Source = AgentTuiProfileSource.Operator,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.AgentTuiSecrets.Add(new AgentTuiSecret
            {
                Id = Guid.NewGuid(),
                ProfileId = profileId,
                Name = "C822_TUI",
                Ciphertext = tuiSecret,
                ProtectionVersion = "test",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.LlmProviders.Add(new LlmProvider
            {
                Id = Guid.NewGuid(),
                Name = "c822",
                ProviderType = ProviderType.Anthropic,
                ApiKey = llmKey,
                BaseUrl = "http://127.0.0.1",
                DefaultModel = "unused",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.DelegationCapabilities.Add(new DelegationCapability
            {
                Id = Guid.NewGuid(),
                Name = "c822",
                TokenHash = capability,
                RootsJson = "[]",
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
            await db.Agents.Where(agent => agent.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(agent => agent.LaunchEnvJson, "{\"C822\":\"" + launchEnv + "\"}"));
        }

        await Instructions(h).ReconcileNowAsync("startup");
        var row = await ReadStateAsync(h);
        var file = await File.ReadAllTextAsync(row.WrittenPath);
        foreach (var sentinel in new[] { apiKey, tuiSecret, llmKey, capability, launchEnv })
        {
            file.ShouldNotContain(sentinel);
            row.Body.ShouldNotContain(sentinel);
            h.Adapter.SubmittedBodies.ShouldAllBe(body => !body.Contains(sentinel, StringComparison.Ordinal));
        }
    }

    [Test]
    public async Task A_refused_render_keeps_the_previous_file_and_records_a_warning_without_a_notice()
    {
        await using var h = await CreateHarnessAsync();
        await AttachBundleAsync(h);
        await Instructions(h).ReconcileNowAsync("startup");
        var row = await ReadStateAsync(h);
        var bytes = await File.ReadAllBytesAsync(row.WrittenPath);
        var notices = h.Adapter.SubmittedBodies.Count;

        h.Delegation.OrchestratorInstructions.StandingInstructions = ["{{key:x}}"];
        await Instructions(h).ReconcileNowAsync("operator");

        (await File.ReadAllBytesAsync(row.WrittenPath)).ShouldBe(bytes);
        h.Adapter.SubmittedBodies.Count.ShouldBe(notices);
        await using var db = NewDb(h);
        (await db.AgentIncidents.AnyAsync(incident =>
            incident.Kind == AgentIncidentKind.OrchestratorInstructionsWriteFailed
            && incident.Severity == AlertSeverity.Warning)).ShouldBeTrue();
    }

    private static async Task<string> CoveredWriteAsync(BridgeQueueHarness h, string kind)
    {
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.Off;
        if (kind == "hold-clear")
        {
            await using var seed = h.Provider.CreateAsyncScope();
            await seed.ServiceProvider.GetRequiredService<ModelAvailability>().UpsertManualAsync(
                "ClaudeCode", "opus", DateTimeOffset.UtcNow.AddHours(4), "hold opus", CancellationToken.None);
            await Instructions(h).WhenIdleAsync();
        }

        await Instructions(h).ReconcileNowAsync("startup");
        h.Delegation.OrchestratorInstructions.Notify = OrchestratorInstructionsNotify.All;
        await using var scope = h.Provider.CreateAsyncScope();
        switch (kind)
        {
            case "host-budget":
                await scope.ServiceProvider.GetRequiredService<HostBudgetService>()
                    .UpsertAsync("local", 3, "raise local", CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "caps:";
            case "hold-upsert":
                await scope.ServiceProvider.GetRequiredService<ModelAvailability>().UpsertManualAsync(
                    "ClaudeCode", "opus", DateTimeOffset.UtcNow.AddHours(4), "hold opus", CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "holds:";
            case "hold-clear":
                await scope.ServiceProvider.GetRequiredService<ModelAvailability>()
                    .ClearAsync("ClaudeCode", "opus", CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "holds:";
            case "routing-pin":
                await scope.ServiceProvider.GetRequiredService<RoutingPinService>().UpsertAsync(
                    new PutRoutingPinRequest(
                        AgentTaskRole.Code,
                        Provenance: RoutingPinProvenance.Human,
                        AgentKind: AgentKind.ClaudeCode,
                        ModelLevel: AgentModelLevel.Frontier,
                        Reason: "stage pin"),
                    null,
                    CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "pins:";
            case "runner-eligibility":
                await using (var db = NewDb(h))
                {
                    db.HostBudgets.Add(new HostBudget
                    {
                        HostId = "local",
                        MaxInFlight = 1,
                        Reason = "direct",
                        UpdatedAt = h.Now,
                        Revision = 1,
                    });
                    await db.SaveChangesAsync();
                }

                new CompositeRunnerEligibilityObserver(
                    new AlarmWakeQueue(),
                    h.Provider.GetRequiredService<IOrchestratorInstructionsSignals>()).Changed("desktop");
                await Instructions(h).WhenIdleAsync();
                return "runners:";
            case "dispatch-concurrency":
                await scope.ServiceProvider.GetRequiredService<DispatchConcurrencySettingsService>().PutGlobalAsync(
                    new PutDispatchConcurrencyRequest(
                        1,
                        null,
                        JsonDocument.Parse("""{"roles":{"Code":{"maxParallel":4}}}""").RootElement.Clone(),
                        "code four",
                        "Human"),
                    null,
                    CancellationToken.None);
                await Instructions(h).WhenIdleAsync();
                return "caps:";
            default:
                throw new InvalidOperationException(kind);
        }
    }

    private static async Task<BridgeQueueHarness> CreateHarnessAsync()
    {
        var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = new DelegationSettings();
        var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConnectionString = schema.ConnectionString,
            Delegation = settings,
            ConfigureServices = services =>
            {
                services.AddSingleton<OrchestratorInstructionsService>();
                services.AddSingleton<IOrchestratorInstructionsSignals>(sp =>
                    sp.GetRequiredService<OrchestratorInstructionsService>());
                services.AddScoped<OrchestratorInstructionsSnapshotBuilder>();
                services.AddScoped<RunnerDefaultSettingsService>();
                services.AddScoped<DispatchConcurrencySettingsService>();
                services.AddScoped<HostBudgetService>();
                services.AddScoped<RoutingPinService>();
                services.AddSingleton<PhoneHomeRunnerDirectory>(sp => new PhoneHomeRunnerDirectory(
                    sp.GetRequiredService<ISessionRunnerClient>(),
                    Options.Create(new PhoneHomeRunnerSettings { Enabled = false }),
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<TimeProvider>()));
                services.AddSingleton<ISessionRunnerDirectory>(sp => sp.GetRequiredService<PhoneHomeRunnerDirectory>());
            },
        });
        settings.OrchestratorInstructions.Path = Path.Combine(harness.TempRoot, "ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md");
        harness.AfterDispose = schema;
        return harness;
    }

    private static async Task AttachBundleAsync(BridgeQueueHarness h)
    {
        await using var db = NewDb(h);
        db.AgentBundleAttachments.Add(new AgentBundleAttachment
        {
            AgentId = h.AgentId,
            BundleKey = InstructionBundles.Orchestrator,
            Position = 0,
            CreatedAt = h.Now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task MarkPoolDelegateAsync(BridgeQueueHarness h)
    {
        await using var db = NewDb(h);
        await db.Agents.Where(agent => agent.Id == h.AgentId)
            .ExecuteUpdateAsync(u => u.SetProperty(agent => agent.IsPoolDelegate, true));
    }

    private static async Task AddTaskAsync(BridgeQueueHarness h, AgentTaskKind kind, AgentTaskStatus status)
    {
        await using var db = NewDb(h);
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "instructions",
            Goal = "notice",
            Kind = kind,
            Status = status,
            AgentSessionId = h.SessionId,
            WorkingDirectory = h.TempRoot,
            CreatedAt = h.Now,
        });
        await db.SaveChangesAsync();
    }

    private static OrchestratorInstructionsService Instructions(BridgeQueueHarness h) =>
        h.Provider.GetRequiredService<OrchestratorInstructionsService>();

    private static AppDbContext NewDb(BridgeQueueHarness h) =>
        new(TestDbFixture.CreateDbContextOptions(h.ConnectionString));

    private static async Task<OrchestratorInstructionsState> ReadStateAsync(BridgeQueueHarness h)
    {
        await using var db = NewDb(h);
        return await db.OrchestratorInstructionsStates.AsNoTracking()
            .SingleAsync(state => state.Id == OrchestratorInstructionsState.FleetId);
    }

    private static Task<OrchestratorInstructionsState?> StateAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        return db.OrchestratorInstructionsStates
            .SingleOrDefaultAsync(state => state.Id == OrchestratorInstructionsState.FleetId);
    }

    private static async Task<string?> SessionVersionAsync(BridgeQueueHarness h)
    {
        await using var db = NewDb(h);
        return await db.AgentSessions.Where(session => session.Id == h.SessionId)
            .Select(session => session.OrchestratorInstructionsVersion)
            .SingleAsync();
    }

    private static AgentTaskDispatcher Dispatcher(BridgeQueueHarness h, IServiceProvider services) =>
        new(
            services.GetRequiredService<AppDbContext>(),
            h.Provider.GetRequiredService<AgentRegistry>(),
            h.Provider.GetRequiredService<AgentSessionLaunchQueue>(),
            h.Queue,
            null!,
            null!,
            null!,
            services.GetRequiredService<IOptions<DelegationSettings>>(),
            h.EventBus,
            h.Clock,
            NullLogger<AgentTaskDispatcher>.Instance);

    private static AgentTask TaskOf(AgentTaskKind kind)
    {
        var id = Guid.NewGuid();
        return new AgentTask { Id = id, RootTaskId = id, Kind = kind, Title = "env", Goal = "env" };
    }
}
