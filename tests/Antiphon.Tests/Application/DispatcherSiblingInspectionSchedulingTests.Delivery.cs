using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class DispatcherSiblingInspectionSchedulingTests
{
    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("queue-inserted")]
    [Arguments("lost-wakeup")]
    public async Task C1349_LaterTaskReceivesBriefBeforeInspectionCompletes(string scenario)
    {
        using var repo = new ScratchGitRepo("c1349-recipient");
        await repo.CommitFileAsync("seed.txt", "seed");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var git = new InspectionGit { Block = true };
        var slot = new InspectionAdapterSlot();
        await using var harness = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, ValidateScopes = true, ConnectionString = schema.ConnectionString,
            Delegation = new DelegationSettings { MaxConcurrentTasks = 32, AllowedRoots = ["/", "C:\\"] },
            ConfigureServices = services => ConfigureInspectionDelivery(services, repo, git, clock, slot),
        });
        slot.Adapter = harness.Adapter;
        harness.Adapter.RegisterOnStart = harness.Runtime;
        var ready = scenario == "eligible" ? null
            : new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Adapter.ReadyHold = ready;
        harness.Adapter.OnSubmitted = async submitted =>
        {
            var sid = harness.Adapter.StartedSessionId.ShouldNotBeNull();
            await BridgeQueueHarness.InsertEntryAsync(sid, TranscriptKinds.UserPrompt, submitted,
                timestamp: DateTime.UtcNow, connectionString: schema.ConnectionString);
            await BridgeQueueHarness.InsertEntryAsync(sid, TranscriptKinds.TurnEnd, stopReason: "end_turn",
                connectionString: schema.ConnectionString);
        };
        var laterId = Guid.NewGuid();
        Guid firstId;
        await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            var card = await DispatchSiblingInspectionTests.CardAsync(seed);
            var source = await DispatchSiblingInspectionTests.SourceAsync(seed, repo, card.Id, "source", "master");
            source.Status = AgentTaskStatus.Blocked;
            var first = QueueTask(repo, card.Id); firstId = first.Id;
            first.CreatedAt = DateTime.UtcNow.AddMinutes(-2);
            var later = new AgentTask { Id = laterId, RootTaskId = laterId, Title = "later recipient",
                Goal = "c1349 complete later brief café", Role = AgentTaskRole.Custom, AgentKind = AgentKind.Raw,
                Workspace = WorkspaceMode.Shared, WorkingDirectory = repo.Path, Status = AgentTaskStatus.Queued,
                Ephemeral = true, ReplyTo = AgentTaskReplyTo.None, CreatedAt = DateTime.UtcNow,
                ConcurrencyToken = Guid.NewGuid() };
            seed.AddRange(first, later); await seed.SaveChangesAsync();
            await seed.AgentTasks.Where(t => t.Id == laterId || t.Id == firstId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AgentKind, AgentKind.Raw));
        }
        var worker = harness.Provider.GetRequiredService<CardSiblingInspectionQueue>();
        var hosted = new CardSiblingInspectionHostedService(worker);
        await hosted.StartAsync(default);
        try
        {
            await using (var scope = harness.Provider.CreateAsyncScope())
            {
                var tick = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
                await git.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await tick.WaitAsync(TimeSpan.FromSeconds(10));
            }
            await using var read = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var first = await read.AgentTasks.SingleAsync(t => t.Id == firstId);
            first.Status.ShouldBe(AgentTaskStatus.Queued); first.WorktreePath.ShouldBeNull(); first.AgentSessionId.ShouldBeNull();
            var later = await read.AgentTasks.SingleAsync(t => t.Id == laterId);
            later.Status.ShouldBe(AgentTaskStatus.Dispatched, later.FailureReason);
            var recipient = later.AgentSessionId.ShouldNotBeNull();
            var session = await read.AgentSessions.SingleAsync(s => s.Id == recipient);
            session.Cwd.ShouldBe(repo.Path); session.StartedAt.ShouldNotBe(default);
            var queued = await read.SessionQueuedMessages.SingleAsync(m => m.ExecutionTaskId == laterId);
            queued.AgentSessionId.ShouldBe(recipient); queued.Body.ShouldContain(DelegationReportFormatter.TaskMarker(laterId));
            var launches = harness.Provider.GetRequiredService<AgentSessionLaunchQueue>();
            if (scenario == "busy")
            {
                await QueuedReceiptAssertions.HoldRecipientBusyAsync(harness, schema.ConnectionString, recipient);
                ready!.TrySetResult(true);
                await launches.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            }
            else if (ready is not null)
            {
                await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(schema.ConnectionString, harness,
                    queued, recipient, false, scenario);
                ready.TrySetResult(true);
            }
            await launches.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            harness.Adapter.StartedSessionId.ShouldBe(recipient, "the production launch owns this actual recipient");
            if (scenario is "eligible" or "busy")
                await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(schema.ConnectionString, harness,
                    queued, recipient, scenario == "busy", busyBeforeDelivery: scenario == "busy");
            var prompts = await read.TranscriptEntries.AsNoTracking()
                .Where(p => p.AgentSessionId == recipient && p.Kind == TranscriptKinds.UserPrompt).ToListAsync();
            prompts.ShouldHaveSingleItem().Text.ShouldContain(DelegationReportFormatter.TaskMarker(laterId));
            PromptSubmissionMatch.IsCompleteIn(queued.Body, prompts[0].Text!).ShouldBeTrue();
            (await read.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.ExecutionTaskId == laterId)).Id.ShouldBe(queued.Id);
            git.Release.Task.IsCompleted.ShouldBeFalse(); git.Blocked.ShouldBe(1);
            (await read.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == firstId)).AgentSessionId.ShouldBeNull();
        }
        finally
        {
            ready?.TrySetResult(true);
            git.Block = false; git.Release.TrySetResult();
            await hosted.StopAsync(default);
            await harness.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
        }
    }

    internal static void ConfigureInspectionDelivery(IServiceCollection services, ScratchGitRepo repo,
        InspectionGit git, TimeProvider inspectionClock, InspectionAdapterSlot slot)
    {
        services.RemoveAll<IOptionsMonitor<AgentRegistrySettings>>(); services.RemoveAll<AgentRegistry>();
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(
            new AgentRegistrySettings { DefaultDefinition = "fake", Definitions =
                { ["fake"] = new AgentDefinition { Kind = "Raw", Exe = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh" } } }));
        services.AddSingleton<AgentRegistry>();
        services.RemoveAll<IAgentProtocolAdapterFactory>(); services.AddSingleton<IAgentProtocolAdapterFactory>(slot);
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.RemoveAll<IWorktreeManager>();
        services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = repo.WorktreeRoot });
        services.RemoveAll<ILandingGit>(); services.AddSingleton<ILandingGit>(git);
        services.AddScoped(sp => new CardSiblingInspectionService(sp.GetRequiredService<AppDbContext>(), git,
            sp.GetRequiredService<IOptions<GitSettings>>(), inspectionClock, sp.GetRequiredService<GitProcessGate>()));
        services.AddSingleton(sp => new CardSiblingInspectionQueue(sp.GetRequiredService<IServiceScopeFactory>(),
            inspectionClock, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CardSiblingInspectionQueue>>()));
        services.AddScoped<AgentTaskService>(); services.AddScoped<AgentTaskDispatcher>();
    }
    internal static AgentTask QueueTask(ScratchGitRepo repo, Guid cardId)
    {
        var id = Guid.NewGuid();
        return new AgentTask { Id = id, RootTaskId = id, Title = "inspection worker", Goal = "complete c1349 inspected task",
            Role = AgentTaskRole.Code, AgentKind = AgentKind.Raw, Workspace = WorkspaceMode.Worktree,
            RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Target, WorkingDirectory = repo.Path, RepoPath = repo.Path,
            CardId = cardId, Status = AgentTaskStatus.Queued, ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DateTime.UtcNow, ConcurrencyToken = Guid.NewGuid() };
    }
    internal sealed class InspectionAdapterSlot : IAgentProtocolAdapterFactory
    {
        public FakeAgentProtocolAdapter? Adapter { get; set; }
        public IAgentProtocolAdapter Create(AgentKind kind) => Adapter ?? throw new InvalidOperationException("adapter not installed");
    }
}
