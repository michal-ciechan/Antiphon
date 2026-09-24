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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0633 V-15. Several silent runner tasks ahead of an eligible local recipient must not hold
/// the serial tick: one tick still delivers the local brief. The phone-home host owns the fake
/// clock; the queue harness stays on the system clock.
/// </summary>
[Category("Integration")]
public sealed class DispatcherRemotePrepStarvationTests
{
    [Test]
    [Arguments("after-receipt", false)]
    [Arguments("after-receipt", true)]
    [Arguments("queue-inserted", false)]
    [Arguments("lost-wakeup", false)]
    public async Task Silent_remote_tasks_ahead_do_not_delay_an_eligible_local_recipient(string cut, bool busy)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        peer.SilentFor(PhoneHomeOperation.WorkspaceMirror);
        var capture = new MutationDispatchTestsCapture();
        await using var harness = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            ConnectionString = schema.ConnectionString,
            Delegation = new DelegationSettings
            {
                MaxConcurrentTasks = 32,
                AllowedRoots = ["C:\\", "/"],
            },
            ConfigureServices = services => Configure(services, host, capture),
        });

        var workspace = Path.Combine(harness.TempRoot, "workspace");
        var localId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await SeedAsync(schema.ConnectionString, workspace, host.AllowedRunnerId, localId, now);

        using (var scope = harness.Provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                .TickAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(20));
        }

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var local = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == localId);
        local.Status.ShouldBe(AgentTaskStatus.Dispatched, local.FailureReason);
        var sessionId = local.AgentSessionId.ShouldNotBeNull();

        var remotes = await db.AgentTasks.AsNoTracking()
            .Where(t => t.RunnerId == host.AllowedRunnerId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
        remotes.Count.ShouldBe(3);
        foreach (var remote in remotes)
        {
            remote.Status.ShouldBe(AgentTaskStatus.Queued);
            var held = await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == remote.Id && e.Type == AgentTaskEventType.Held)
                .SingleAsync();
            held.Detail.ShouldStartWith(DispatchHoldDetails.RemoteMirrorRequestedPrefix);
        }

        peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(3);

        await harness.Provider.GetRequiredService<AgentSessionLaunchQueue>()
            .WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        var queued = await db.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(m => m.ExecutionTaskId == localId);
        await QueuedReceiptAssertions.ConfirmQueuedReceiptAsync(
            schema.ConnectionString, harness, queued, sessionId, busy, cut);

        await Task.Delay(100);
        clock.Advance(PhoneHomeLiveConnection.RequestTimeoutFor(PhoneHomeOperation.WorkspaceMirror)
            + TimeSpan.FromSeconds(1));
        live.NoteHeartbeat(clock.GetUtcNow());
        host.Directory.MarkRecovered(live);
        await harness.Provider.GetRequiredService<RemoteWorkspacePreparer>()
            .WhenIdleAsync()
            .WaitAsync(TimeSpan.FromSeconds(15));

        await using var after = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var warned = 0;
        foreach (var remote in remotes)
        {
            var row = await after.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == remote.Id);
            row.DispatchNotBeforeAt.ShouldNotBeNull();
            warned += await after.AgentTaskEvents.CountAsync(e =>
                e.AgentTaskId == remote.Id && e.Type == AgentTaskEventType.Warning);
        }

        warned.ShouldBe(3);
    }

    private static void Configure(
        IServiceCollection services, PhoneHomeTestHost host, MutationDispatchTestsCapture capture)
    {
        services.RemoveAll<IOptionsMonitor<AgentRegistrySettings>>();
        services.RemoveAll<AgentRegistry>();
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
            new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
            {
                DefaultDefinition = "fake",
                GrokCredentialProbeEnabled = false,
                Definitions =
                {
                    ["fake"] = new AgentDefinition
                    {
                        Kind = "Raw",
                        Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    },
                    ["grok"] = new AgentDefinition
                    {
                        Kind = "Grok",
                        Exe = "grok.exe",
                        ArgsTemplate = ["--always-approve", "--no-alt-screen"],
                    },
                },
            }));
        services.AddSingleton<AgentRegistry>();
        services.RemoveAll<IAgentProtocolAdapterFactory>();
        services.AddSingleton<IAgentProtocolAdapterFactory>(capture);
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-c633-starve-{Guid.NewGuid():N}"),
        });
        services.RemoveAll<ILandingGit>();
        services.AddSingleton<ILandingGit, PushGit>();
        services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = host.AllowedRunnerId,
            AllowDelegatedTasks = true,
            HostWorkspaceRoot = @"C:\src\Antiphon",
            CallbackOrigin = "https://antiphon.desktop.codeperf.net",
            SharedSecret = "x",
            ClaudeAuthProbeEnabled = false,
        }));
        services.AddSingleton<PhoneHomeLaunchPolicy>();
        services.AddSingleton<ISessionRunnerDirectory>(host.Directory);
        services.AddSingleton<RemoteWorkspaceService>();
        services.AddSingleton<RemoteWorkspacePreparer>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
    }

    private static async Task SeedAsync(
        string connection, string workspace, string runnerId, Guid localId, DateTime now)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        for (var minutes = 3; minutes >= 1; minutes--)
        {
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "remote " + minutes,
                Goal = "remote",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Custom,
                AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = workspace,
                WorktreePath = workspace,
                WorktreeBranch = "feat/remote-" + minutes,
                RunnerId = runnerId,
                Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = now.AddMinutes(-minutes),
                ConcurrencyToken = Guid.NewGuid(),
            });
        }

        db.AgentTasks.Add(new AgentTask
        {
            Id = localId,
            RootTaskId = localId,
            Title = "local recipient",
            Goal = "reply locally",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            AgentKind = AgentKind.Raw,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workspace,
            Ephemeral = true,
            Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
    }

    private sealed class PushGit : ILandingGit
    {
        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
            Task.FromResult(arguments[0] switch
            {
                "rev-parse" => new LandingGitResult(0, new string('1', 40), ""),
                "push" => new LandingGitResult(0, "", ""),
                _ => throw new NotSupportedException(arguments[0]),
            });

        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) => throw new NotSupportedException();
    }
}
