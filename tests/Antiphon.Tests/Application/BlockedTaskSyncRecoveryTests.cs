using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BlockedTaskSyncRecoveryTests
{
    [Test]
    public async Task C1065_LeaseBusyDoesNotRetainPublishedIdleSeat()
    {
        foreach (var dirty in new[] { false, true })
        {
            // CARD-1082 D-8: park sync debt stays reachable with the settlement kill switch off.
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked,
                parking: true, syncRecovery: true, syncDebt: false);
            await f.CreateSourceAsync(remote: true);
            await f.AddParentAsync(busy: true);
            await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "published.txt"), "retained work");
            await f.GitAsync(f.SourcePath, "add", "published.txt");
            await f.GitAsync(f.SourcePath, "commit", "-m", "published work");
            await f.GitAsync(f.SourcePath, "push", "origin", "HEAD");
            var sha = await f.GitAsync(f.SourcePath, "rev-parse", "HEAD");
            if (dirty) await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "dirty.txt"), "unpublished bytes");
            var task = await f.TaskAsync();
            var leases = f.Harness.Provider.GetRequiredService<IRepositoryMutationLease>();
            await using (var lease = await leases.TryAcquireAsync(task.RepoPath!, default))
            {
                lease.ShouldNotBeNull("hold the actual desktop common-directory lease");
                f.Wire.LeaseBusyObserved = () => f.Clock.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
                await f.SettleAsync("done");
                task = await f.TaskAsync();
                task.Status.ShouldBe(AgentTaskStatus.Blocked);
                TaskProgressJson.TryReadEvidence(task.CompletionProgressEvidenceJson)!.RemoteSync!.Reason
                    .ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
                await f.HandleParkAsync();
                var park = await f.ParkAsync();
                await using var db = f.Db();
                (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId)).ShouldBe(1,
                    "G-118: report and completion obligation survive the lease refusal");
                if (dirty)
                {
                    f.Wire.ConditionalCommands.ShouldBe(0, "independent publication failure still vetoes release");
                    park.PublicationReceiptId.ShouldBeNull();
                    park.State.ShouldBe(AgentTaskParkState.Held);
                    continue;
                }
                park.State.ShouldBe(AgentTaskParkState.Parked, await f.ParkDiagnosticAsync());
                f.Wire.ConditionalCommands.ShouldBe(1, "G-118: sync debt cannot retain the qualified idle seat");
                park.SyncState.ShouldBe(AgentTaskParkSyncState.Pending);
                park.SyncSourceSha.ShouldBe(sha, "G-119: exact published source is durable");
                var report = task.Result;
                var proof = park.PublicationReceiptDigest;
                await f.RestartAsync();
                f.Harness.Provider.GetRequiredService<IOptions<BlockedTaskParkingOptions>>().Value.Enabled = false;
                using var scope = f.Harness.Provider.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                (await dispatcher.RecoverBlockedTaskSyncAsync(default)).ShouldBe(1, "accepted debt survives parking disable/restart");
                var pending = await f.ParkAsync();
                pending.SyncAttempts.ShouldBe(1);
                pending.SyncNextAttemptAt.ShouldBe(f.Now.AddMinutes(1));
                pending.SyncReasonCode.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
                pending.SyncSourceSha.ShouldBe(sha);
                pending.PublicationReceiptDigest.ShouldBe(proof);
                (await f.TaskAsync()).Result.ShouldBe(report);
                (await dispatcher.RecoverBlockedTaskSyncAsync(default)).ShouldBe(0, "future due is skipped");
            }
            f.Clock.Advance(TimeSpan.FromMinutes(1));
            using (var scope = f.Harness.Provider.CreateScope())
                (await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>()
                    .RecoverBlockedTaskSyncAsync(default)).ShouldBe(1);
            var ready = await f.ParkAsync();
            ready.SyncState.ShouldBe(AgentTaskParkSyncState.Ready);
            (await f.GitAsync(f.DesktopSourcePath, "rev-parse", "HEAD")).ShouldBe(sha);
            f.Wire.ConditionalCommands.ShouldBe(1);
            (await f.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Blocked);
            var detail = await GetAsync(f.Harness.Provider, f.TaskId);
            detail.ParkSync!.SourceSha.ShouldBe(sha);
            detail.ParkSync.State.ShouldBe("Ready");
            (await f.AttentionAsync()).Items.ShouldContain(i => i.Evidence != null
                && i.Evidence.Contains($"park={ready.Id:D}") && i.Evidence.Contains("sync=Ready"));
        }
    }

    [Test]
    public async Task C1065_SyncDebtRecoversWithoutMintingApproval()
    {
        foreach (var scenario in new[] { "valid", "advanced", "endpoint", "endpoint-race", "unknown-endpoint", "dirty", "sequencer", "diverged", "missing", "changed-episode" })
        {
            await using var w = await TaskParkPublicationTests.PublicationWorld.CreateAsync(remote: true);
            var sha = await w.CommitAsync("recovery.txt");
            (await w.PrepareAsync()).Evidence.ShouldNotBeNull();
            var park = await w.RowAsync();
            park.SyncSourceSha.ShouldBe(sha, "G-120: immutable source");
            park.SyncNextAttemptAt.ShouldNotBeNull("G-125: persisted due time");
            if (scenario == "advanced") { await w.CommitAsync("later.txt"); await w.PushAsync(); }
            if (scenario == "endpoint") await w.GitTextAsync(w.Repository, "remote", "set-url", "origin", Path.Combine(w.Root, "different.git"));
            if (scenario == "unknown-endpoint") await w.GitTextAsync(w.Repository, "remote", "remove", "origin");
            if (scenario == "dirty") await File.WriteAllTextAsync(Path.Combine(w.LocalPath, "dirty.txt"), "keep dirty bytes");
            if (scenario == "sequencer")
            {
                var gitDir = await w.GitTextAsync(w.LocalPath, "rev-parse", "--absolute-git-dir");
                await File.WriteAllTextAsync(Path.Combine(gitDir, "CHERRY_PICK_HEAD"), w.Baseline + "\n");
            }
            if (scenario == "diverged")
            {
                await File.WriteAllTextAsync(Path.Combine(w.LocalPath, "desktop.txt"), "desktop-only commit");
                await w.GitTextAsync(w.LocalPath, "add", "desktop.txt");
                await w.GitTextAsync(w.LocalPath, "commit", "-m", "desktop divergence");
            }
            if (scenario == "missing") await w.GitTextAsync(w.Path, "push", "origin", "--delete", w.FullRef);
            if (scenario == "changed-episode") await w.ChangeTaskAsync(t => t.Attempt++);
            var head = await w.GitTextAsync(w.LocalPath, "rev-parse", "HEAD");
            var original = await HistoryAsync(w);
            var clock = new FakeTimeProvider(new DateTimeOffset(park.SyncNextAttemptAt.Value));
            var bus = new SyncEvents(w.Fixture.Db);
            var observedGit = new TaskParkPublicationTests.ParkGit(w.Leases);
            var endpointChanged = false;
            if (scenario == "endpoint-race")
            {
                var other = Path.Combine(w.Root, "other.git");
                await w.GitTextAsync(w.Root, "clone", "--bare", w.Origin, other);
                observedGit.Before = async args =>
                {
                    // After ValidateCheckout's endpoint read, at the exact-ref observation's
                    // HasOrigin probe. A later checkout check would mask this guard unless we
                    // also assert that no fetch from the substituted endpoint happened.
                    if (!endpointChanged && args.SequenceEqual(new[] { "remote", "get-url", "origin" }))
                    { endpointChanged = true; await w.GitTextAsync(w.Repository, "remote", "set-url", "origin", other); }
                };
            }
            w.Git.Commands.Clear();
            await RecoverAsync(w, clock, bus, git: observedGit);
            var after = await w.RowAsync();
            if (scenario == "valid")
            {
                after.SyncState.ShouldBe(AgentTaskParkSyncState.Ready);
                after.SourceReadyAt.ShouldNotBeNull();
                (await w.GitTextAsync(w.LocalPath, "rev-parse", "HEAD")).ShouldBe(sha);
                bus.ReadyObserved.ShouldBe(1, "state commits before invalidation");
                await w.RestartAsync();
                (await RecoverAsync(w, clock, bus)).ShouldBe(0, "SourceReady changes once");
                (await w.RowAsync()).SourceReadyAt.ShouldBe(after.SourceReadyAt);
                var dto = await GetAsync(w.Fixture.Services, w.Fixture.TaskId);
                dto.ParkSync!.State.ShouldBe("Ready");
                dto.ParkSync.SourceSha.ShouldBe(sha);
                dto.Summary.Status.ShouldBe(AgentTaskStatus.Blocked, "G-127 no promotion");
            }
            else
            {
                after.SyncState.ShouldBe(AgentTaskParkSyncState.Held, scenario);
                var reason = scenario switch
                {
                    "advanced" => RemoteSettlementSyncReasons.TipNotReported,
                    "endpoint" or "endpoint-race" => RemoteSettlementSyncReasons.EndpointChanged,
                    "unknown-endpoint" => RemoteSettlementSyncReasons.EndpointAmbiguous,
                    "dirty" => RemoteSettlementSyncReasons.Dirty,
                    "sequencer" => RemoteSettlementSyncReasons.Sequencer,
                    "diverged" => RemoteSettlementSyncReasons.Diverged,
                    "missing" => RemoteSettlementSyncReasons.BranchNotPushed,
                    _ => "park_sync_episode_changed"
                };
                after.SyncReasonCode.ShouldBe(reason, scenario);
                (await w.GitTextAsync(w.LocalPath, "rev-parse", "HEAD")).ShouldBe(head, scenario);
                after.SourceReadyAt.ShouldBeNull(scenario);
                if (scenario == "dirty") (await File.ReadAllTextAsync(Path.Combine(w.LocalPath, "dirty.txt"))).ShouldBe("keep dirty bytes");
                if (scenario is "endpoint" or "endpoint-race" or "unknown-endpoint")
                    observedGit.Commands.ShouldNotContain(c => c[0] == "fetch", "G-121 no fetch from changed endpoint");
                if (scenario == "endpoint-race") endpointChanged.ShouldBeTrue("race must fire after the first checkout check");
            }
            after.SyncSourceSha.ShouldBe(sha);
            (await HistoryAsync(w)).ShouldBe(original, "G-127/G-128: task, report, evidence, outcomes, events and obligations are immutable");
        }

        // The production repository lock stays held across recreated services. Each pass makes
        // one attempt, persists 1/2/4/5/5 minutes, and performs no in-memory sleep loop.
        await using (var w = await TaskParkPublicationTests.PublicationWorld.CreateAsync(remote: true))
        {
            await w.CommitAsync("backoff.txt");
            (await w.PrepareAsync()).Evidence.ShouldNotBeNull();
            var clock = new FakeTimeProvider(new DateTimeOffset((await w.RowAsync()).SyncNextAttemptAt!.Value));
            var bus = new SyncEvents(w.Fixture.Db);
            await using (var lease = await w.Leases.TryAcquireAsync(w.Repository, default))
            {
                lease.ShouldNotBeNull();
                var attempt = 0;
                foreach (var minutes in new[] { 1, 2, 4, 5, 5 })
                {
                    await w.RestartAsync();
                    (await RecoverAsync(w, clock, bus)).ShouldBe(1);
                    var row = await w.RowAsync();
                    row.SyncAttempts.ShouldBe(++attempt);
                    row.SyncNextAttemptAt.ShouldBe(clock.GetUtcNow().UtcDateTime.AddMinutes(minutes), "G-125");
                    row.SyncReasonCode.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
                    (await RecoverAsync(w, clock, bus)).ShouldBe(0, "future due stays skipped after recreation");
                    clock.Advance(TimeSpan.FromMinutes(minutes));
                }
            }
            await RecoverAsync(w, clock, bus);
            (await w.RowAsync()).SyncState.ShouldBe(AgentTaskParkSyncState.Ready);
        }

        foreach (var cut in new[] { "claimed", "synced", "saved" })
        {
            await using var w = await TaskParkPublicationTests.PublicationWorld.CreateAsync(remote: true);
            var sha = await w.CommitAsync("crash.txt");
            (await w.PrepareAsync()).Evidence.ShouldNotBeNull();
            var before = await HistoryAsync(w);
            var clock = new FakeTimeProvider(new DateTimeOffset((await w.RowAsync()).SyncNextAttemptAt!.Value));
            var bus = new SyncEvents(w.Fixture.Db);
            var fired = false;
            await RecoverAsync(w, clock, bus, (boundary, _) =>
            { if (boundary == cut) { fired = true; throw new IOException("simulated crash"); } return Task.CompletedTask; });
            fired.ShouldBeTrue(cut);
            bus.ReadyObserved.ShouldBe(0, "invalidation was not reached");
            await w.RestartAsync();
            clock.Advance(TimeSpan.FromMinutes(1));
            await RecoverAsync(w, clock, bus);
            var after = await w.RowAsync();
            after.SyncState.ShouldBe(AgentTaskParkSyncState.Ready, cut);
            after.SyncSourceSha.ShouldBe(sha);
            (await HistoryAsync(w)).ShouldBe(before, "G-128 crash recovery must not mint evidence");
            (await GetAsync(w.Fixture.Services, w.Fixture.TaskId)).ParkSync!.State.ShouldBe("Ready");
        }

        // An old malformed obligation sorts before the valid debt in the same database/pass.
        await using (var w = await TaskParkPublicationTests.PublicationWorld.CreateAsync(remote: true))
        {
            await w.CommitAsync("neighbor.txt");
            (await w.PrepareAsync()).Evidence.ShouldNotBeNull();
            var row = await w.RowAsync();
            var poisonId = Guid.NewGuid();
            await using (var db = w.Fixture.Db())
            {
                db.AgentTaskParks.Add(new AgentTaskPark { Id = poisonId, TaskId = Guid.NewGuid(),
                    BlockEventId = Guid.NewGuid(), SyncState = AgentTaskParkSyncState.Pending,
                    SyncNextAttemptAt = row.SyncNextAttemptAt!.Value.AddMinutes(-1),
                    CreatedAt = row.CreatedAt, UpdatedAt = row.UpdatedAt, BlockedAt = row.BlockedAt });
                await db.SaveChangesAsync();
            }
            var clock = new FakeTimeProvider(new DateTimeOffset(row.SyncNextAttemptAt!.Value));
            await RecoverAsync(w, clock, new SyncEvents(w.Fixture.Db));
            await using var verify = w.Fixture.Db();
            (await verify.AgentTaskParks.SingleAsync(p => p.Id == poisonId)).SyncState.ShouldBe(AgentTaskParkSyncState.Held);
            (await w.RowAsync()).SyncState.ShouldBe(AgentTaskParkSyncState.Ready, "G-126: poison debt cannot starve neighbor");
        }
    }

    private static async Task<int> RecoverAsync(TaskParkPublicationTests.PublicationWorld w, FakeTimeProvider clock,
        SyncEvents bus, Func<string, CancellationToken, Task>? boundary = null,
        TaskParkPublicationTests.ParkGit? git = null)
    {
        await using var db = w.Fixture.Db();
        git ??= new TaskParkPublicationTests.ParkGit(w.Leases);
        var workspace = new RemoteWorkspaceService(w.Directory, w.Git, NullLogger<RemoteWorkspaceService>.Instance,
            git, w.Leases, w.Reservations) { Clock = clock };
        var service = new BlockedTaskSyncRecoveryService(db, workspace, w.Service(db), bus, clock,
            NullLogger<BlockedTaskSyncRecoveryService>.Instance) { BoundaryAsync = boundary };
        return await service.SweepAsync(default);
    }

    private static async Task<string> HistoryAsync(TaskParkPublicationTests.PublicationWorld w)
    {
        await using var db = w.Fixture.Db();
        return JsonSerializer.Serialize(new {
            Task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == w.Fixture.TaskId),
            Events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == w.Fixture.TaskId).OrderBy(e => e.Id).ToListAsync(),
            Outcomes = await db.StageOutcomes.AsNoTracking().OrderBy(e => e.Id).ToListAsync(),
            Notes = await db.AgentTaskLandNotifications.AsNoTracking().OrderBy(e => e.Id).ToListAsync()
        });
    }

    private sealed class SyncEvents(Func<AppDbContext> open) : IEventBus
    {
        public int ReadyObserved { get; private set; }
        public async Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default)
        {
            await using var db = open();
            if (await db.AgentTaskParks.AnyAsync(p => p.SyncState == AgentTaskParkSyncState.Ready && p.SourceReadyAt != null, ct))
                ReadyObserved++;
        }
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<AgentTaskDetailDto> GetAsync(IServiceProvider services, Guid taskId)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var credential = Guid.NewGuid().ToString("N");
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers["X-Test-Token"] != credential) { context.Response.StatusCode = 401; return; }
            await next(context);
        });
        app.MapGet("/api/agent-tasks/{id:guid}", async (Guid id, CancellationToken ct) =>
        {
            using var scope = services.CreateScope();
            var service = scope.ServiceProvider.GetService<AgentTaskService>() ?? new AgentTaskService(
                scope.ServiceProvider.GetRequiredService<AppDbContext>(),
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new DelegationSettings()), scope.ServiceProvider.GetRequiredService<IEventBus>(),
                new RecordingSessionStopper(), TimeProvider.System, NullLogger<AgentTaskService>.Instance);
            return await service.GetAsync(id, ct);
        });
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        (await http.GetAsync($"/api/agent-tasks/{taskId}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        http.DefaultRequestHeaders.Add("X-Test-Token", credential);
        return (await http.GetFromJsonAsync<AgentTaskDetailDto>($"/api/agent-tasks/{taskId}"))!;
    }
}
