using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Antiphon.TestSupport;
using Npgsql;
using Shouldly;
using TUnit.Core;
using Rig = Antiphon.Tests.Application.RemoteWorkspacePreparerTests.Rig;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1677 S1 CP-5. The server half of runner worktree retention against the scripted phone-home peer
/// (shared Postgres, isolated schema, fake clock): selection, the allow-list and capability gates, the
/// per-tick bound, pacing, the recheck before each send, recording, and the unmatched-name report. Every
/// V-30 case seeds one eligible control task beside the ineligible one, so a case can never pass because
/// nothing was sent. The V-39 cases drive a real runner dispatcher and engine (F-4).
/// </summary>
[Category("Integration")]
[Timeout(45_000)]
public sealed class C1677RetentionServerTests
{
    private static RunnerCapabilitiesDto RetentionRunner(bool capable = true, bool outputDrop = false)
    {
        List<string> features = [];
        if (capable)
            features.Add(RunnerCapabilityFeatures.WorkspaceRetentionV1);
        if (outputDrop)
            features.Add(RunnerCapabilityFeatures.WorkspaceOutputDropV1);
        return new("InboxConhost", "inbox", "test", false, Features: features, VerificationCustodyBackend: null);
    }

    internal sealed class World : IAsyncDisposable
    {
        public Rig Rig { get; init; } = null!;
        public RunnerWorktreeRetentionSettings Settings { get; init; } = null!;

        /// <summary>CARD-1804: the settings monitor every tick reads (its <see cref="Monitor.Hold"/> parks the background tick).</summary>
        public Monitor SettingsMonitor { get; init; } = null!;
        public List<WorkspaceRetentionEntry> Inventory { get; } = [];

        /// <summary>CARD-1794: the inventory answer's Complete flag (an incomplete inventory proves no absence).</summary>
        public bool InventoryComplete { get; set; } = true;
        public Func<PhoneHomeWorkspaceRetainRequest, PhoneHomeFrame, PhoneHomeFrame?> Retain { get; set; } = (_, frame) => Answer(frame, Removed());

        /// <summary>CARD-1895. A drop answer so a send cannot hang. Bytes are the scripted saving.</summary>
        public Func<PhoneHomeWorkspaceOutputDropRequest, PhoneHomeFrame, PhoneHomeFrame?> Drop { get; set; } = (request, frame) =>
        {
            var dry = request.Mode == WorkspaceOutputDropModes.DryRun;
            return Answer(frame, new PhoneHomeWorkspaceOutputDropResponse(
                dry ? WorkspaceOutputDropCodes.WouldDrop : WorkspaceOutputDropCodes.Dropped,
                dry ? WorkspaceOutputDropCodes.WouldDrop : WorkspaceOutputDropCodes.Dropped,
                Bytes: 4096, Directories: 1));
        };

        public static async Task<World> StartAsync(bool capable = true, Action<IServiceCollection>? configure = null, bool outputDrop = false)
        {
            var settings = new RunnerWorktreeRetentionSettings
            {
                Enabled = true, Execute = true, PauseBetweenSeconds = 0, MinTerminalHours = 12, MaxRemovalsPerTick = 25,
                // CARD-1895. Off unless this world is an output-drop world, so the seven scripted retention tests send no drop.
                OutputDrop = new RunnerOutputDropSettings
                {
                    Mode = outputDrop ? RunnerOutputDropMode.Execute : RunnerOutputDropMode.Off,
                },
            };
            var monitor = new Monitor(settings);
            var rig = await Rig.StartAsync(capabilities: RetentionRunner(capable, outputDrop), configure: services =>
            {
                services.AddSingleton<IOptionsMonitor<RunnerWorktreeRetentionSettings>>(monitor);
                services.AddSingleton<RunnerWorktreeRetentionTicks>();
                services.AddScoped<RunnerWorktreeRetentionService>();
                configure?.Invoke(services);
            });
            settings.Runners.Add(rig.RunnerId);
            var world = new World { Rig = rig, Settings = settings, SettingsMonitor = monitor };
            rig.Peer.Reply = world.Answer;
            return world;
        }

        private PhoneHomeFrame? Answer(PhoneHomeFrame frame)
        {
            if (frame.Kind != PhoneHomeFrameKind.Request)
                return null;
            if (frame.Operation == PhoneHomeOperation.WorkspaceRetentionInventory)
                return Answer(frame, new PhoneHomeWorkspaceRetentionInventoryResponse(Inventory.ToList(), InventoryComplete));
            if (frame.Operation == PhoneHomeOperation.WorkspaceRetain)
                return Retain(frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceRetainRequest>(PhoneHomeFraming.Json)!, frame);
            if (frame.Operation == PhoneHomeOperation.WorkspaceDropOutputs)
                return Drop(frame.Payload!.Value.Deserialize<PhoneHomeWorkspaceOutputDropRequest>(PhoneHomeFraming.Json)!, frame);
            return null;
        }

        public static PhoneHomeFrame Answer(PhoneHomeFrame request, object payload) =>
            new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
                JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

        public async Task<IReadOnlyList<RunnerWorktreeRetentionService.TickResult>> TickAsync(CancellationToken ct = default)
        {
            using var scope = Rig.Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<RunnerWorktreeRetentionService>().RunTickAsync(ct);
        }

        /// <summary>The operator preview route's call: one DryRun tick for this rig's runner.</summary>
        public async Task<IReadOnlyList<RunnerWorktreeRetentionService.TickResult>> PreviewAsync(CancellationToken ct = default)
        {
            using var scope = Rig.Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<RunnerWorktreeRetentionService>().RunPreviewAsync(Rig.RunnerId, ct);
        }

        /// <summary>CARD-1796: the process's tick turn and the execute route's background tick.</summary>
        public RunnerWorktreeRetentionTicks Ticks => Rig.Provider.GetRequiredService<RunnerWorktreeRetentionTicks>();

        /// <summary>
        /// CARD-1796: the execute route's own call with the operator credential (or <paramref name="header"/>), off the
        /// test thread, so a route that ran the tick on the request would show as a reply that never comes.
        /// </summary>
        public Task<RunnerWorktreeRetentionTicks.ExecuteStarted> ExecuteRouteAsync(string? runnerId = null, string? header = null,
            RunnerWorktreeRetentionTicks? ticks = null)
        {
            var operatorSettings = new PhoneHomeRunnerSettings { OperatorTokenPath = Rig.Host.OperatorTokenPath };
            var token = Antiphon.Server.Infrastructure.Security.OperatorTokenFile.ReadOrCreate(operatorSettings.OperatorTokenPath);
            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            if (header != "")
                http.Request.Headers[Antiphon.Server.Infrastructure.Security.OperatorTokenFile.Header] = header ?? token;
            return Task.Run(() =>
            {
                Monitor.RouteFlow.Value = true;
                return Antiphon.Server.Api.Endpoints.AgentTaskEndpoints.ExecuteRunnerRetention(
                    runnerId ?? Rig.RunnerId, http, operatorSettings, ticks ?? Ticks);
            });
        }

        public async Task<WorktreeResidueRun> RunAsync(Guid? runId)
        {
            await using var db = Rig.NewDb();
            return await db.WorktreeResidueRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        }

        public List<PhoneHomeWorkspaceRetainRequest> Requests() =>
            Rig.Peer.Incoming.ToArray()
                .Where(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == PhoneHomeOperation.WorkspaceRetain)
                .Select(f => f.Payload!.Value.Deserialize<PhoneHomeWorkspaceRetainRequest>(PhoneHomeFraming.Json)!)
                .ToList();

        public List<PhoneHomeWorkspaceOutputDropRequest> DropRequests() =>
            Rig.Peer.Incoming.ToArray()
                .Where(f => f.Kind == PhoneHomeFrameKind.Request && f.Operation == PhoneHomeOperation.WorkspaceDropOutputs)
                .Select(f => f.Payload!.Value.Deserialize<PhoneHomeWorkspaceOutputDropRequest>(PhoneHomeFraming.Json)!)
                .ToList();

        public static string PathOf(Guid id) => "/work/worktrees/task-" + id.ToString("N")[..8];

        /// <summary>
        /// F-5: a Succeeded Worktree task on this runner, finished <paramref name="ago"/> ago, report stored. CARD-1794:
        /// its mirror is listed in the runner inventory unless <paramref name="present"/> is false (a mirror already gone).
        /// </summary>
        public async Task<Guid> EligibleAsync(TimeSpan? ago = null, Action<AgentTask>? tweak = null, bool present = true)
        {
            var completedAgo = ago ?? TimeSpan.FromHours(24);
            string? runnerId = null;
            var id = await Rig.SeedAsync(task =>
            {
                task.Status = AgentTaskStatus.Succeeded;
                task.CompletedAt = Rig.Now - completedAgo;
                task.RemoteWorktreePath = PathOf(task.Id);
                task.Result = "done";
                tweak?.Invoke(task);
                runnerId = task.RunnerId;
            });
            // Only this rig runner's own mirrors are on its disk (another runner's task is listed nowhere here).
            if (present && runnerId == Rig.RunnerId)
                Inventory.Add(new WorkspaceRetentionEntry("worktrees", "task-" + id.ToString("N")[..8], true, false, true, false, null));
            return id;
        }

        /// <summary>CARD-1794: the operator's one-shot execute route's call for this rig's runner.</summary>
        public async Task<IReadOnlyList<RunnerWorktreeRetentionService.TickResult>> ExecuteOnceAsync(string? runnerId = null, CancellationToken ct = default)
        {
            using var scope = Rig.Provider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<RunnerWorktreeRetentionService>().RunExecuteOnceAsync(runnerId ?? Rig.RunnerId, ct);
        }

        /// <summary>CARD-1804: the rows one run wrote that name no task (stop, failure and cancel rows).</summary>
        public async Task<List<WorktreeResidueRunCandidate>> RunRowsAsync(Guid? runId)
        {
            await using var db = Rig.NewDb();
            return await db.WorktreeResidueRunCandidates.AsNoTracking()
                .Where(r => r.RunId == runId && r.TaskId == null && r.Lane == RunnerWorktreeRetentionService.LaneMirror)
                .OrderBy(r => r.EvaluatedAt).ToListAsync();
        }

        public async Task<List<WorktreeResidueRunCandidate>> RowsAsync(Guid? taskId = null)
        {
            await using var db = Rig.NewDb();
            return await db.WorktreeResidueRunCandidates.AsNoTracking()
                .Where(r => taskId == null || r.TaskId == taskId)
                .OrderBy(r => r.EvaluatedAt).ToListAsync();
        }

        public ValueTask DisposeAsync() => Rig.DisposeAsync();
    }

    private static PhoneHomeWorkspaceRetainResponse Removed() =>
        new(WorkspaceRetentionCodes.Removed, HeadSha: new string('a', 40), OriginTip: new string('a', 40), PhaseAMs: 3, PhaseBMs: 7);

    internal sealed class Monitor(RunnerWorktreeRetentionSettings value) : IOptionsMonitor<RunnerWorktreeRetentionSettings>
    {
        /// <summary>CARD-1804: set on the execute route's own flow (the background tick does not inherit it).</summary>
        public static readonly AsyncLocal<bool> RouteFlow = new();

        /// <summary>CARD-1804: while set and unsignalled, a read outside the route's flow (the background tick) waits for it.</summary>
        public ManualResetEventSlim? Hold { get; set; }

        public RunnerWorktreeRetentionSettings CurrentValue
        {
            get
            {
                if (Hold is { } hold && !RouteFlow.Value)
                    hold.Wait(TimeSpan.FromSeconds(30));
                return value;
            }
        }

        public RunnerWorktreeRetentionSettings Get(string? name) => value;
        public IDisposable? OnChange(Action<RunnerWorktreeRetentionSettings, string?> listener) => null;
    }

    [Test]
    [Arguments("queued")]
    [Arguments("dispatched")]
    [Arguments("working")]
    [Arguments("blocked")]
    [Arguments("settling")]
    [Arguments("land-pending")]
    [Arguments("sync-debt-pending")]
    [Arguments("sync-debt-ready")]
    [Arguments("sync-debt-held")]
    [Arguments("slot-bound")]
    [Arguments("source-landing")]
    [Arguments("repair-source")]
    [Arguments("mutation")]
    [Arguments("path-shared")]
    [Arguments("session-created")]
    [Arguments("session-starting")]
    [Arguments("session-running")]
    [Arguments("session-stopping")]
    [Arguments("report-unpreserved")]
    [Arguments("leaf-mismatch")]
    [Arguments("not-direct-child")]
    [Arguments("other-runner")]
    [Arguments("branch-null")]
    [Arguments("leaf-newline")]
    [Arguments("leaf-carriage-return")]
    [Arguments("leaf-line-separator")]
    [Arguments("leaf-trailing-space")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Each_ineligible_task_alone_is_recorded_and_never_sent(string mode)
    {
        // V-30 (G-42..G-64, I-1, I-5, I-11). One fact differs from the eligible shape; the control is still sent.
        await using var w = await World.StartAsync();
        var control = await w.EligibleAsync();
        Guid? landing = null;
        if (mode == "source-landing")
            landing = await w.Rig.SeedLandingAsync(await w.Rig.SeedOwnerAsync());
        var sessionStatus = mode switch
        {
            "session-created" => SessionStatus.Created,
            "session-starting" => SessionStatus.Starting,
            "session-running" => SessionStatus.Running,
            "session-stopping" => SessionStatus.Stopping,
            _ => (SessionStatus?)null,
        };
        Guid? sessionId = sessionStatus is null ? null : await SeedSessionAsync(w.Rig, sessionStatus.Value);
        var subject = await w.EligibleAsync(tweak: task =>
        {
            switch (mode)
            {
                case "queued": task.Status = AgentTaskStatus.Queued; break;
                case "dispatched": task.Status = AgentTaskStatus.Dispatched; break;
                case "working": task.Status = AgentTaskStatus.Working; break;
                case "blocked": task.Status = AgentTaskStatus.Blocked; break;
                case "settling": task.CompletedAt = w.Rig.Now - TimeSpan.FromHours(12) + TimeSpan.FromMinutes(1); break;
                case "land-pending": task.LandRequestedAt = w.Rig.Now; break;
                case "slot-bound": task.RemoteWorkspaceSlotId = "ws-02"; break;
                case "source-landing": task.SourceLandingOperationId = landing; task.Role = AgentTaskRole.Code; break;
                case "repair-source": task.RepairSourceTaskId = Guid.NewGuid(); task.Role = AgentTaskRole.Code; break;
                case "mutation": task.Role = AgentTaskRole.Mutation; break;
                case "report-unpreserved":
                    task.Result = null;
                    task.ResultFilePath = World.PathOf(task.Id) + "/.antiphon/task-" + task.Id.ToString("N")[..8] + ".md";
                    break;
                case "leaf-mismatch": task.RemoteWorktreePath = "/work/worktrees/task-ffffffff"; break;
                case "not-direct-child": task.RemoteWorktreePath = "/work/worktrees/x/task-" + task.Id.ToString("N")[..8]; break;
                case "other-runner": task.RunnerId = "server2-temp"; break;
                case "branch-null": task.WorktreeBranch = null; break;
                // CARD-1677 S1 hardening (review d0b57b9c A-3): `$` matches before a final newline.
                case "leaf-newline": task.RemoteWorktreePath = World.PathOf(task.Id) + "\n"; break;
                case "leaf-carriage-return": task.RemoteWorktreePath = World.PathOf(task.Id) + "\r"; break;
                case "leaf-line-separator": task.RemoteWorktreePath = World.PathOf(task.Id) + "\u2028"; break;
                case "leaf-trailing-space": task.RemoteWorktreePath = World.PathOf(task.Id) + " "; break;
            }
            if (sessionId is not null)
                task.AgentSessionId = sessionId;
        });
        if (mode.StartsWith("sync-debt-", StringComparison.Ordinal))
            await SeedSyncDebtAsync(w.Rig, subject, mode switch
            {
                "sync-debt-pending" => AgentTaskSyncDebtState.Pending,
                "sync-debt-ready" => AgentTaskSyncDebtState.Ready,
                _ => AgentTaskSyncDebtState.Held,
            });
        if (mode == "path-shared")
            await w.Rig.SeedAsync(task =>
            {
                task.Status = AgentTaskStatus.Working;
                task.RemoteWorktreePath = World.PathOf(subject);
            });
        var before = (await w.Rig.ReadTaskAsync(subject)).RemoteWorktreePath;

        await w.TickAsync();

        var request = w.Requests().ShouldHaveSingleItem("only the control is sent");
        request.TaskId.ShouldBe(control);
        (await w.Rig.ReadTaskAsync(subject)).RemoteWorktreePath.ShouldBe(before);
        var rows = await w.RowsAsync(subject);
        if (mode == "other-runner")
        {
            rows.ShouldBeEmpty("another runner's task is in neither scope");
            return;
        }
        var row = rows.ShouldHaveSingleItem();
        row.Lane.ShouldBe(RunnerWorktreeRetentionService.LaneMirror);
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe(mode switch
        {
            "queued" or "dispatched" or "working" or "blocked" => "live_owner",
            "settling" => "settling",
            "land-pending" => "land_pending",
            "sync-debt-pending" or "sync-debt-ready" or "sync-debt-held" => "sync_debt",
            "slot-bound" => "slot_managed",
            "mutation" => "mutation_excluded",
            "path-shared" => "path_shared",
            "session-created" or "session-starting" or "session-running" or "session-stopping" => "live_session",
            "report-unpreserved" => "report_unpreserved",
            "leaf-newline" or "leaf-carriage-return" or "leaf-line-separator" => "name_control_character",
            _ => "outside_scope",
        });
    }

    [Test]
    [Arguments(AgentTaskStatus.Succeeded)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Terminal_statuses_at_the_age_floor_are_sent(AgentTaskStatus status)
    {
        // V-30b. The no-false-refusal control: CompletedAt exactly MinTerminalHours ago is eligible.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync(TimeSpan.FromHours(12), task => task.Status = status);
        await w.TickAsync();
        w.Requests().ShouldHaveSingleItem().TaskId.ShouldBe(id);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Disabled_job_contacts_no_runner()
    {
        // V-31 (G-65). No retention frame of any kind, inventory included.
        await using var w = await World.StartAsync();
        w.Settings.Enabled = false;
        await w.EligibleAsync();
        (await w.TickAsync()).ShouldBeEmpty();
        await Task.Delay(200);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(0);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetentionInventory).ShouldBe(0);
        (await w.RowsAsync()).ShouldBeEmpty();
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Execute_off_sends_dry_run_only()
    {
        // V-31b (G-66).
        await using var w = await World.StartAsync();
        w.Settings.Execute = false;
        w.Retain = (_, frame) => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.WouldRemove));
        var id = await w.EligibleAsync();
        await w.TickAsync();
        w.Requests().ShouldHaveSingleItem().Mode.ShouldBe(WorkspaceRetainModes.DryRun);
        (await w.RowsAsync(id)).ShouldHaveSingleItem().ReasonCode.ShouldBe(WorkspaceRetentionCodes.WouldRemove);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
    }

    [Test]
    [Arguments("unlisted")]
    [Arguments("not-phone-home")]
    [Arguments("no-capability")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Only_listed_capable_phone_home_runners_are_contacted(string shape)
    {
        // V-31c (G-67, G-68, G-69, D-11).
        await using var w = await World.StartAsync(capable: shape != "no-capability");
        var id = await w.EligibleAsync();
        if (shape == "unlisted")
            w.Settings.Runners.Clear();
        if (shape == "not-phone-home")
        {
            w.Settings.Runners.Clear();
            w.Settings.Runners.Add("desktop");
            await w.EligibleAsync(tweak: task => task.RunnerId = "desktop");
        }

        var results = await w.TickAsync();
        await Task.Delay(200);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(0);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetentionInventory).ShouldBe(0);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
        if (shape == "unlisted")
        {
            results.ShouldBeEmpty();
            (await w.RowsAsync()).ShouldBeEmpty();
            return;
        }
        var row = (await w.RowsAsync()).ShouldHaveSingleItem();
        row.ReasonCode.ShouldBe("runner_not_eligible");
        row.RunnerId.ShouldBe(shape == "not-phone-home" ? "desktop" : w.Rig.RunnerId);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Execute_requests_are_bounded_per_tick()
    {
        // V-32 (G-70). Five eligible, a bound of two: the two oldest, in order.
        await using var w = await World.StartAsync();
        w.Settings.MaxRemovalsPerTick = 2;
        var ids = new List<Guid>();
        for (var hours = 20; hours <= 24; hours++)
            ids.Add(await w.EligibleAsync(TimeSpan.FromHours(hours)));
        await w.TickAsync();
        w.Requests().Select(r => r.TaskId).ShouldBe([ids[4], ids[3]]);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Second_request_waits_for_the_first_reply()
    {
        // V-32b (G-71). One request at a time.
        await using var w = await World.StartAsync();
        await w.EligibleAsync(TimeSpan.FromHours(30));
        await w.EligibleAsync(TimeSpan.FromHours(20));
        w.Rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceRetain);
        using var cancel = new CancellationTokenSource();
        var tick = w.TickAsync(cancel.Token);
        await w.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 1);
        await Task.Delay(2000);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(1);
        await cancel.CancelAsync();
        var canceled = false;
        try { await tick.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (OperationCanceledException) { canceled = true; }
        canceled.ShouldBeTrue("the silent request is abandoned only by cancellation");
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Requests_are_paced()
    {
        // V-32c (G-72). The pause runs on the fake clock.
        await using var w = await World.StartAsync();
        w.Settings.PauseBetweenSeconds = 5;
        var first = await w.EligibleAsync(TimeSpan.FromHours(30));
        await w.EligibleAsync(TimeSpan.FromHours(20));
        var tick = w.TickAsync();
        await w.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 1);
        // The first outcome is recorded before the pause starts; only then does "no second request" mean pacing.
        for (var i = 0; i < 100 && (await w.RowsAsync(first)).Count == 0; i++)
            await Task.Delay(100);
        await Task.Delay(1000);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(1, "the second request waits for the pause");
        // The pause timer is armed after the first outcome is recorded; under load that can follow the 1 s
        // window, so the clock moves one pause at a time until the second request is sent.
        for (var i = 0; i < 20 && w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain) < 2; i++)
        {
            w.Rig.Clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(250);
        }
        await w.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 2);
        await tick.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Reopened_task_is_rechecked_before_its_turn()
    {
        // V-33 (G-73). The second candidate is reopened while the first request is in flight.
        await using var w = await World.StartAsync();
        await w.EligibleAsync(TimeSpan.FromHours(30));
        var second = await w.EligibleAsync(TimeSpan.FromHours(20));
        w.Retain = (_, frame) =>
        {
            using (var db = w.Rig.NewDb())
                db.AgentTasks.Where(t => t.Id == second).ExecuteUpdate(s => s.SetProperty(t => t.Status, AgentTaskStatus.Queued));
            return World.Answer(frame, Removed());
        };
        await w.TickAsync();
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(1);
        (await w.RowsAsync(second)).ShouldHaveSingleItem().ReasonCode.ShouldBe("live_owner");
        (await w.Rig.ReadTaskAsync(second)).RemoteWorktreePath.ShouldBe(World.PathOf(second));
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Kept_trees_do_not_starve_newer_removable_ones()
    {
        // Review c238f9ca D-1. Thirty trees the runner keeps (dirty) are older than five removable ones, with the
        // shipped bound of 25: the kept ones are not re-sent while backed off, so the removable ones go on tick 2.
        await using var w = await World.StartAsync();
        var kept = new HashSet<Guid>();
        for (var i = 0; i < 30; i++)
            kept.Add(await w.EligibleAsync(TimeSpan.FromHours(100 - i)));
        var removable = new List<Guid>();
        for (var i = 0; i < 5; i++)
            removable.Add(await w.EligibleAsync(TimeSpan.FromHours(20 - i)));
        w.Retain = (request, frame) => kept.Contains(request.TaskId)
            ? World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, WorkspaceRetentionCodes.Dirty))
            : World.Answer(frame, Removed());

        await w.TickAsync();
        var first = w.Requests().Select(r => r.TaskId).ToList();
        first.Count.ShouldBe(25);
        first.ShouldAllBe(id => kept.Contains(id), "the 25 oldest are the kept ones");

        await w.Rig.AdvanceAsync(TimeSpan.FromMinutes(30));
        var results = await w.TickAsync();
        var second = w.Requests().Skip(first.Count).Select(r => r.TaskId).ToList();
        second.ShouldNotContain(id => first.Contains(id), "a kept tree is backed off, not re-sent every tick");
        second.ShouldBe(kept.Except(first).Concat(removable).ToList(), ignoreOrder: true);
        foreach (var id in removable)
            (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBeNull();
        var result = results.ShouldHaveSingleItem();
        result.Examined.ShouldBe(35);
        result.Eligible.ShouldBe(35);
        result.BackedOff.ShouldBe(25);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Dry_run_census_covers_the_whole_population()
    {
        // Review c238f9ca D-1. In DryRun nothing clears, so a would_remove moves the task behind the never-sent
        // ones: two ticks with a bound of 2 examine all three trees.
        await using var w = await World.StartAsync();
        w.Settings.Execute = false;
        w.Settings.MaxRemovalsPerTick = 2;
        var ids = new List<Guid>();
        for (var hours = 30; hours >= 20; hours -= 5)
            ids.Add(await w.EligibleAsync(TimeSpan.FromHours(hours)));
        w.Retain = (_, frame) => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.WouldRemove));

        (await w.TickAsync()).ShouldHaveSingleItem().Examined.ShouldBe(3);
        w.Requests().Select(r => r.TaskId).ShouldBe([ids[0], ids[1]]);
        await w.TickAsync();
        w.Requests().Select(r => r.TaskId).Skip(2).First().ShouldBe(ids[2], "the never-sent tree goes first");
        w.Requests().Select(r => r.TaskId).Distinct().ShouldBe(ids, ignoreOrder: true);
        w.Requests().ShouldAllBe(r => r.Mode == WorkspaceRetainModes.DryRun);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Kept_tree_backs_off_until_its_reason_changes()
    {
        // Review c238f9ca D-1. A keep backs the tree off (60 min, doubling per repeat); a changed reason is
        // re-sent on the next tick; a repeat of the new reason backs off again.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync();
        var reasons = new Queue<string>([WorkspaceRetentionCodes.Dirty, WorkspaceRetentionCodes.LiveProcess, WorkspaceRetentionCodes.LiveProcess]);
        w.Retain = (_, frame) => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, reasons.Dequeue()));

        await w.TickAsync();
        w.Requests().Count.ShouldBe(1);
        await w.Rig.AdvanceAsync(TimeSpan.FromMinutes(30));
        (await w.TickAsync()).ShouldHaveSingleItem().BackedOff.ShouldBe(1);
        w.Requests().Count.ShouldBe(1, "dirty again within its backoff is not re-sent");
        await w.Rig.AdvanceAsync(TimeSpan.FromMinutes(30));
        await w.TickAsync();
        w.Requests().Count.ShouldBe(2, "the backoff has passed");
        await w.TickAsync();
        w.Requests().Count.ShouldBe(3, "the reason changed (dirty -> live_process), so it is re-sent at once");
        await w.TickAsync();
        w.Requests().Count.ShouldBe(3, "the same reason again backs off");
        (await w.RowsAsync(id)).Where(r => r.OperationId != null).Select(r => r.ReasonCode)
            .ShouldBe([WorkspaceRetentionCodes.Dirty, WorkspaceRetentionCodes.LiveProcess, WorkspaceRetentionCodes.LiveProcess]);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Kept_tree_backoff_doubles_per_repeat_of_the_same_reason()
    {
        // CARD-1684 S18 (guard (h)). A first keep backs off 60 min; a second keep of the same reason backs off
        // 120 min: a tick 61 min after it is still backed off, one 121 min after it is sent.
        await using var w = await World.StartAsync();
        await w.EligibleAsync();
        w.Retain = (_, frame) => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, WorkspaceRetentionCodes.Dirty));

        await w.TickAsync();
        w.Requests().Count.ShouldBe(1);
        await w.Rig.AdvanceAsync(TimeSpan.FromMinutes(60));
        await w.TickAsync();
        w.Requests().Count.ShouldBe(2, "the first backoff (60 min) has passed");
        await w.Rig.AdvanceAsync(TimeSpan.FromMinutes(61));
        (await w.TickAsync()).ShouldHaveSingleItem().BackedOff.ShouldBe(1);
        w.Requests().Count.ShouldBe(2, "the second keep of the same reason backs off 120 min, not 60");
        await w.Rig.AdvanceAsync(TimeSpan.FromMinutes(60));
        await w.TickAsync();
        w.Requests().Count.ShouldBe(3, "121 min after the second keep its backoff has passed");
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Kept_tree_backoff_is_capped_at_kept_recheck_max_hours()
    {
        // CARD-1684 S19 (guard (h)). Six keeps of the same reason: 60, 120, 240, 480 and 960 min, then
        // 60 x 2^5 = 1920 min capped at KeptRecheckMaxHours (24 h). The seventh request goes 24 h after the sixth.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync();
        w.Retain = (_, frame) => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, WorkspaceRetentionCodes.Dirty));

        await w.TickAsync();
        foreach (var minutes in new[] { 60, 120, 240, 480, 960 })
        {
            await w.Rig.AdvanceAsync(TimeSpan.FromMinutes(minutes));
            await w.TickAsync();
        }
        w.Requests().Count.ShouldBe(6, "each uncapped backoff passed exactly");
        await using (var db = w.Rig.NewDb())
        {
            var cursor = await db.WorktreeResidueCandidateCursors.AsNoTracking()
                .SingleAsync(c => c.CandidateKey == RunnerWorktreeRetentionService.SendCursorKey(id));
            cursor.NotBefore.ShouldNotBeNull().ShouldBe(w.Rig.Now.AddHours(w.Settings.KeptRecheckMaxHours), TimeSpan.FromSeconds(1));
        }
        await w.Rig.AdvanceAsync(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        await w.TickAsync();
        w.Requests().Count.ShouldBe(6, "still within the capped backoff");
        await w.Rig.AdvanceAsync(TimeSpan.FromMinutes(1));
        await w.TickAsync();
        w.Requests().Count.ShouldBe(7, "the capped backoff (24 h) has passed");
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Changed_task_facts_resend_a_backed_off_tree_at_once()
    {
        // Review c238f9ca D-1. The backoff belongs to the facts it was answered for: a task that ran again and
        // finished again (a new CompletedAt) is re-sent at once.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync();
        w.Retain = (_, frame) => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, WorkspaceRetentionCodes.Unpushed));
        await w.TickAsync();
        await w.TickAsync();
        w.Requests().Count.ShouldBe(1, "backed off");
        await using (var db = w.Rig.NewDb())
            await db.AgentTasks.Where(t => t.Id == id)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.CompletedAt, w.Rig.Now - TimeSpan.FromHours(13)));
        await w.TickAsync();
        w.Requests().Count.ShouldBe(2);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Preview_is_dry_run_even_when_execute_is_on()
    {
        // Review c238f9ca D-3 (a). With the shipped Execute=true the preview route still sends DryRun only.
        await using var w = await World.StartAsync();
        w.Settings.Execute.ShouldBeTrue();
        var id = await w.EligibleAsync();
        w.Retain = (_, frame) => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.WouldRemove));
        var result = (await w.PreviewAsync()).ShouldHaveSingleItem();
        result.Sent.ShouldBe(1);
        w.Requests().ShouldHaveSingleItem().Mode.ShouldBe(WorkspaceRetainModes.DryRun);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
        await using var db = w.Rig.NewDb();
        var run = await db.WorktreeResidueRuns.AsNoTracking().SingleAsync(r => r.Id == result.RunId);
        run.Execute.ShouldBeFalse();
        run.Preview.ShouldBeTrue();
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Preview_of_an_unlisted_runner_contacts_nothing()
    {
        // CARD-1684 S09 (guard (e)). The preview route honours the Runners allow-list: a preview for a capable,
        // phone-home runner that is not listed returns no result, sends no frame and writes no run.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync();
        w.Settings.Runners.Clear();
        w.Settings.Runners.Add("other-runner");
        w.Retain = (_, frame) => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.WouldRemove));

        (await w.PreviewAsync()).ShouldBeEmpty();
        await Task.Delay(200);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(0);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetentionInventory).ShouldBe(0);
        (await w.RowsAsync()).ShouldBeEmpty();
        await using var db = w.Rig.NewDb();
        (await db.WorktreeResidueRuns.AsNoTracking().CountAsync()).ShouldBe(0);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Tick_stops_starting_requests_past_its_budget()
    {
        // Review c238f9ca A-2. Past MaxTickMinutes the tick sends no further request; the rest stay for later.
        await using var w = await World.StartAsync();
        w.Settings.MaxTickMinutes = 1;
        w.Settings.PauseBetweenSeconds = 5;
        var first = await w.EligibleAsync(TimeSpan.FromHours(30));
        var second = await w.EligibleAsync(TimeSpan.FromHours(20));
        var tick = w.TickAsync();
        await w.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 1);
        for (var i = 0; i < 100 && (await w.RowsAsync(first)).Count == 0; i++)
            await Task.Delay(100);
        // Past the budget while the pause runs; the pause timer may arm after the first move, so keep moving.
        await w.Rig.AdvanceAsync(TimeSpan.FromSeconds(61));
        for (var i = 0; i < 20 && !tick.IsCompleted && w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain) < 2; i++)
            await w.Rig.AdvanceAsync(TimeSpan.FromSeconds(5));
        var result = (await tick.WaitAsync(TimeSpan.FromSeconds(15))).ShouldHaveSingleItem();
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(1);
        result.Stopped.ShouldBe("tick_budget_exhausted");
        (await w.RowsAsync()).ShouldContain(r => r.ReasonCode == "tick_budget_exhausted" && r.Outcome == nameof(WorktreeResidueCandidateOutcome.Deferred));
        (await w.Rig.ReadTaskAsync(second)).RemoteWorktreePath.ShouldBe(World.PathOf(second));
    }

    [Test]
    [Arguments("enabled")]
    [Arguments("execute")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Switching_off_stops_a_running_tick(string setting)
    {
        // Review c238f9ca A-3. Enabled=false (the kill switch) or Execute=false, set while a tick runs, stops it
        // before its next request.
        await using var w = await World.StartAsync();
        await w.EligibleAsync(TimeSpan.FromHours(30));
        var second = await w.EligibleAsync(TimeSpan.FromHours(20));
        w.Retain = (_, frame) =>
        {
            if (setting == "enabled")
                w.Settings.Enabled = false;
            else
                w.Settings.Execute = false;
            return World.Answer(frame, Removed());
        };
        var result = (await w.TickAsync()).ShouldHaveSingleItem();
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(1);
        result.Stopped.ShouldBe(setting == "enabled" ? "tick_stopped_disabled" : "tick_stopped_execute_off");
        (await w.Rig.ReadTaskAsync(second)).RemoteWorktreePath.ShouldBe(World.PathOf(second));
    }

    [Test]
    [Arguments("removed")]
    [Arguments("kept")]
    [Arguments("lost")]
    [Arguments("already-absent")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Outcome_recording(string outcome)
    {
        // V-34 (G-74, G-75, G-77, I-14).
        await using var w = await World.StartAsync();
        w.Retain = (_, frame) => outcome switch
        {
            "removed" => World.Answer(frame, Removed()),
            "kept" => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, WorkspaceRetentionCodes.Dirty)),
            "lost" => new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: "transport_lost", ErrorDetail: "lost", StatusCode: 503),
            _ => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.AlreadyAbsent, RetiringResidue: false)),
        };
        var id = await w.EligibleAsync();
        await w.TickAsync();
        var task = await w.Rig.ReadTaskAsync(id);
        var row = (await w.RowsAsync(id)).ShouldHaveSingleItem();
        var events = (await w.Rig.EventsAsync(id, AgentTaskEventType.Warning))
            .Where(e => e.Detail.StartsWith(RunnerWorktreeRetentionService.EventPrefix, StringComparison.Ordinal)).ToList();
        switch (outcome)
        {
            case "removed":
                task.RemoteWorktreePath.ShouldBeNull();
                row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Removed));
                row.ReasonCode.ShouldBe(WorkspaceRetentionCodes.Removed);
                var note = events.ShouldHaveSingleItem().Detail;
                note.ShouldContain(World.PathOf(id));
                note.ShouldContain("phaseAMs 3");
                note.ShouldContain("phaseBMs 7");
                // I-12: the service has no desktop worktree dependency to call.
                typeof(RunnerWorktreeRetentionService).GetConstructors().SelectMany(c => c.GetParameters())
                    .Select(p => p.ParameterType).ShouldNotContain(typeof(IWorktreeManager));
                break;
            case "kept":
                task.RemoteWorktreePath.ShouldBe(World.PathOf(id));
                row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
                row.ReasonCode.ShouldBe(WorkspaceRetentionCodes.Dirty);
                events.ShouldBeEmpty();
                break;
            case "lost":
                task.RemoteWorktreePath.ShouldBe(World.PathOf(id));
                row.ReasonCode.ShouldBe("unknown");
                row.Outcome.ShouldNotBe(nameof(WorktreeResidueCandidateOutcome.Removed));
                events.ShouldBeEmpty();
                break;
            default:
                task.RemoteWorktreePath.ShouldBeNull();
                row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Removed));
                row.ReasonCode.ShouldBe(WorkspaceRetentionCodes.AlreadyAbsent);
                break;
        }
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Unmatched_inventory_names_are_reported_not_sent()
    {
        // V-35 (G-76). The 17-folder shape: reported with an owner hint, never sent.
        await using var w = await World.StartAsync();
        var control = await w.EligibleAsync();
        var owner = Guid.Parse("cffa0c4b-0000-4000-8000-000000001677");
        await w.Rig.SeedAsync(task => { task.Id = owner; task.RootTaskId = owner; task.Status = AgentTaskStatus.Succeeded; task.CompletedAt = w.Rig.Now; });
        foreach (var n in new[] { 1, 2 })
        {
            var twin = Guid.Parse($"eeeeeeee-0000-4000-8000-00000000000{n}");
            await w.Rig.SeedAsync(task => { task.Id = twin; task.RootTaskId = twin; });
        }
        w.Inventory.Add(new WorkspaceRetentionEntry("worktrees", "task-cffa0c4b-uA", true, false, true, false, null));
        w.Inventory.Add(new WorkspaceRetentionEntry("worktrees", "task-eeeeeeee", true, false, true, false, null));

        await w.TickAsync();

        w.Requests().ShouldHaveSingleItem().TaskId.ShouldBe(control);
        var rows = (await w.RowsAsync()).Where(r => r.Lane == RunnerWorktreeRetentionService.LaneInventory).ToList();
        var unknown = rows.Where(r => r.ReasonCode == "identity_unknown").ShouldHaveSingleItem();
        unknown.Path.ShouldBe("/work/worktrees/task-cffa0c4b-uA");
        unknown.TaskId.ShouldBe(owner);
        unknown.Branch.ShouldBe("owner_hint:task-cffa0c4b:Succeeded");
        rows.Where(r => r.ReasonCode == "identity_ambiguous").ShouldHaveSingleItem().Path.ShouldBe("/work/worktrees/task-eeeeeeee");
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Control_character_inventory_names_are_reported_not_sent()
    {
        // CARD-1677 S1 hardening (review d0b57b9c A-3). An inventory name that is a known task leaf plus a
        // control character is not that task and no owner prefix: it is held as such and never sent.
        // Control: the eligible task itself is still sent, and its own exact leaf is not reported.
        await using var w = await World.StartAsync();
        var control = await w.EligibleAsync();
        var leaf = "task-" + control.ToString("N")[..8];
        w.Inventory.Add(new WorkspaceRetentionEntry("worktrees", leaf, true, false, true, false, null));
        foreach (var suffix in new[] { "\n", "\r", "\t", "\u2028", "\u2029" })
            w.Inventory.Add(new WorkspaceRetentionEntry("worktrees", leaf + suffix, true, false, true, false, null));

        await w.TickAsync();

        w.Requests().ShouldHaveSingleItem().TaskId.ShouldBe(control);
        var rows = (await w.RowsAsync()).Where(r => r.Lane == RunnerWorktreeRetentionService.LaneInventory).ToList();
        rows.Count.ShouldBe(5, "the exact leaf is known; each suffixed name is reported once");
        rows.ShouldAllBe(r => r.ReasonCode == "name_control_character" && r.TaskId == null && r.Branch == null);
        rows.Select(r => r.Path).Order(StringComparer.Ordinal).ShouldBe(
            new[] { "\n", "\r", "\t", "\u2028", "\u2029" }.Select(x => "/work/worktrees/" + leaf + x).Order(StringComparer.Ordinal));
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task Known_leaf_with_a_trailing_space_is_reported_not_skipped()
    {
        // CARD-1677 S1 hardening mutation XS (task cde14651). An inventory name that is a known task leaf plus a
        // trailing space is not that leaf: it is held and reported with the owner hint, never silently taken
        // for the known leaf. Control: the eligible task itself is still sent, and its own exact leaf is not reported.
        await using var w = await World.StartAsync();
        var control = await w.EligibleAsync();
        var leaf = "task-" + control.ToString("N")[..8];
        w.Inventory.Add(new WorkspaceRetentionEntry("worktrees", leaf, true, false, true, false, null));
        w.Inventory.Add(new WorkspaceRetentionEntry("worktrees", leaf + " ", true, false, true, false, null));

        await w.TickAsync();

        w.Requests().ShouldHaveSingleItem().TaskId.ShouldBe(control);
        var rows = (await w.RowsAsync()).Where(r => r.Lane == RunnerWorktreeRetentionService.LaneInventory).ToList();
        var row = rows.ShouldHaveSingleItem("the exact leaf is known; the space-suffixed name is reported once");
        row.Path.ShouldBe("/work/worktrees/" + leaf + " ");
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe("identity_unknown");
        row.TaskId.ShouldBe(control);
        row.Branch.ShouldNotBeNull().ShouldStartWith("owner_hint:" + leaf + ":");
    }

    [NativeLinuxOnly("CARD-1677 Linux-only by construction: the real runner engine's holder scan reads /proc; off Linux it is incomplete and retention keeps the tree (holder_scan_unknown)")]
    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public async Task Retain_round_trip_real_runner(CancellationToken ct)
    {
        // V-39 (F-4). Producer -> client -> socket -> real runner dispatcher -> real engine -> real git.
        await using var world = await C1677RetentionTransportWorld.StartAsync();
        var (id, path) = await world.SeedMirroredTaskAsync();
        await world.TickAsync(ct);
        Directory.Exists(path).ShouldBeFalse();
        world.Registered(path).ShouldBeFalse();
        (await world.Server.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBeNull();
        (await world.Server.RowsAsync(id)).ShouldHaveSingleItem().Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Removed));
    }

    [NativeLinuxOnly("CARD-1677 Linux-only by construction: the real runner engine's holder scan reads /proc; off Linux it is incomplete and retention keeps the tree (holder_scan_unknown)")]
    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public async Task Busy_runner_retain_is_serialised(CancellationToken ct)
    {
        // V-39b. While one request is outstanding the tick sends no second; delivered, it removes once.
        await using var world = await C1677RetentionTransportWorld.StartAsync();
        var (id, path) = await world.SeedMirroredTaskAsync();
        await world.SeedMirroredTaskAsync("b1677002");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Hold = gate.Task;
        var tick = world.TickAsync(ct);
        await world.Server.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 1);
        await Task.Delay(1500, ct);
        world.Server.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(1);
        gate.SetResult();
        await tick.WaitAsync(TimeSpan.FromSeconds(30), ct);
        world.Server.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(2);
        Directory.Exists(path).ShouldBeFalse();
        world.Moves(path).ShouldBe(1);
    }

    [NativeLinuxOnly("CARD-1677 Linux-only by construction: the real runner engine's holder scan reads /proc; off Linux it is incomplete and retention keeps the tree (holder_scan_unknown)")]
    [Test]
    [Arguments("reply-lost")]
    [Arguments("crash-between-phases")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public async Task Retain_recovery_at_each_handoff(string handoff, CancellationToken ct)
    {
        // V-39c (D-1 recovery rows). A lost reply is unknown with the path kept, then already_absent;
        // a crash after the move is unknown, then a phase-B-only retry of the residue.
        await using var world = await C1677RetentionTransportWorld.StartAsync();
        var (id, path) = await world.SeedMirroredTaskAsync();
        if (handoff == "reply-lost")
            world.DropNextReply = true;
        else
            world.CrashAfterMoveOnce = true;

        await world.TickAsync(ct);
        (await world.Server.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(path, "an unknown outcome keeps the path");
        (await world.Server.RowsAsync(id)).ShouldHaveSingleItem().ReasonCode.ShouldBe("unknown");

        await world.TickAsync(ct);
        (await world.Server.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBeNull();
        var second = (await world.Server.RowsAsync(id)).Last();
        second.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Removed));
        second.ReasonCode.ShouldBe(handoff == "reply-lost" ? WorkspaceRetentionCodes.AlreadyAbsent : WorkspaceRetentionCodes.Removed);
        Directory.Exists(path).ShouldBeFalse();
        world.RetiringEntries().ShouldBeEmpty();
        world.Moves(path).ShouldBe(1, "a residue is never moved again");
        world.Removes().ShouldBe(1, "phase B ran exactly once");
    }

    [NativeLinuxOnly("CARD-1677 Linux-only by construction: the real runner engine's holder scan reads /proc; off Linux it is incomplete and retention keeps the tree (holder_scan_unknown)")]
    [Test]
    [Arguments("bin/appsettings.Development.json")]
    [Arguments("bin/.env")]
    [Arguments("node_modules/.env")]
    [Arguments("dist/credentials/token")]
    [Arguments("bin/tls.pem")]
    [Arguments("bin/tls.pem/token")]
    [Arguments("bin/id_ed25519")]
    [Arguments("bin/.aws/credentials")]
    [Arguments("bin/.docker/config.json")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    public async Task Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay(string relative, CancellationToken ct)
    {
        // CARD-1718 A-D3 round 2 (review 47d31366 B-1, B-2). Producer to the real runner retention
        // path. Each secret is alone, and survives the first pass and a lost-reply replay.
        await using var world = await C1677RetentionTransportWorld.StartAsync();
        var (id, path) = await world.SeedMirroredTaskAsync();
        world.PublishIgnoredSecret(path, "feat/card-task-a1677001", relative);
        var bytes = File.ReadAllBytes(Path.Combine(path, relative));
        world.DropNextReply = true;

        await world.TickAsync(ct);
        (await world.Server.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(path);
        (await world.Server.RowsAsync(id)).ShouldHaveSingleItem().ReasonCode.ShouldBe("unknown");

        await world.TickAsync(ct);
        (await world.Server.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(path);
        var rows = await world.Server.RowsAsync(id);
        rows.Count.ShouldBe(2);
        rows.Last().Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        rows.Last().ReasonCode.ShouldBe("ignored_secret");
        world.Registered(path).ShouldBeTrue();
        world.Moves(path).ShouldBe(0);
        world.Removes().ShouldBe(0);
        Directory.Exists(path).ShouldBeTrue();
        File.ReadAllBytes(Path.Combine(path, relative)).ShouldBe(bytes);
    }

    [NativeLinuxOnly("The real retention runtime uses ProcWorkspaceSlotProcessProbe and scratch Git repositories")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.ProcessSpawnLimit>]
    [Test]
    public async Task Ignored_nested_repository_secret_survives_real_transport(CancellationToken ct)
    {
        await using var world = await C1677RetentionTransportWorld.StartAsync();
        var (id, path) = await world.SeedMirroredTaskAsync();
        var nested = path + "/bin/tool";
        Directory.CreateDirectory(nested);
        C0824SlotTransportWorld.Git(nested, "init", "-q");
        C0824SlotTransportWorld.Git(nested, "-c", "user.email=x@y", "-c", "user.name=x", "commit", "-q", "--allow-empty", "-m", "seed");
        Directory.CreateDirectory(nested + "/credentials");
        File.WriteAllText(nested + "/credentials/token", "transport-nested-secret\n");
        var bytes = File.ReadAllBytes(nested + "/credentials/token");
        C0824SlotTransportWorld.Git(path, "ls-files", "--others", "--ignored", "--exclude-standard", "-z").ShouldBe("bin/tool/\0");

        await world.TickAsync(ct);

        (await world.Server.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(path);
        var row = (await world.Server.RowsAsync(id)).ShouldHaveSingleItem();
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        row.ReasonCode.ShouldBe("ignored_secret");
        world.Registered(path).ShouldBeTrue();
        File.ReadAllBytes(nested + "/credentials/token").ShouldBe(bytes);
        world.Moves(path).ShouldBe(0);
        world.Removes().ShouldBe(0);
    }

    private static PhoneHomeWorkspaceRetainResponse AlreadyAbsent() =>
        new(WorkspaceRetentionCodes.AlreadyAbsent, RetiringResidue: false);

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1794_absent_mirrors_do_not_spend_the_removal_budget()
    {
        // CARD-1794 (1). Three old finished tasks whose mirrors are gone (not in a complete inventory) and two
        // newer present ones, with a bound of two: the present trees are sent first, oldest first, and the gone
        // ones are retired after them on their own bound instead of eating the two removals.
        await using var w = await World.StartAsync();
        w.Settings.MaxRemovalsPerTick = 2;
        var gone = new List<Guid>();
        foreach (var hours in new[] { 100, 90, 80 })
            gone.Add(await w.EligibleAsync(TimeSpan.FromHours(hours), present: false));
        var older = await w.EligibleAsync(TimeSpan.FromHours(30));
        var newer = await w.EligibleAsync(TimeSpan.FromHours(20));
        var goneSet = gone.Select(World.PathOf).ToHashSet(StringComparer.Ordinal);
        w.Retain = (request, frame) => World.Answer(frame, goneSet.Contains(request.Path) ? AlreadyAbsent() : Removed());

        var result = (await w.TickAsync()).ShouldHaveSingleItem();

        w.Requests().Select(r => r.TaskId).ShouldBe([older, newer, gone[0], gone[1], gone[2]], "present-first-then-gone");
        foreach (var id in gone.Append(older).Append(newer))
            (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBeNull();
        result.Sent.ShouldBe(2, "only present trees count against MaxRemovalsPerTick");
        result.AbsentSent.ShouldBe(3);
        (await w.RowsAsync(newer)).ShouldHaveSingleItem().ReasonCode.ShouldBe(WorkspaceRetentionCodes.Removed);
        (await w.RowsAsync(gone[2])).ShouldHaveSingleItem().ReasonCode.ShouldBe(WorkspaceRetentionCodes.AlreadyAbsent);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1794_gone_mirrors_have_their_own_bound_and_unsent_ones_are_queued()
    {
        // CARD-1794 (1). MaxAbsentPerTick=1: one gone mirror is retired after the present tree, the other is queued
        // with a row (it no longer vanishes from the census), and nothing is sent for it.
        await using var w = await World.StartAsync();
        w.Settings.MaxAbsentPerTick = 1;
        var firstGone = await w.EligibleAsync(TimeSpan.FromHours(100), present: false);
        var secondGone = await w.EligibleAsync(TimeSpan.FromHours(90), present: false);
        var present = await w.EligibleAsync(TimeSpan.FromHours(20));
        w.Retain = (request, frame) => World.Answer(frame, request.TaskId == present ? Removed() : AlreadyAbsent());

        var result = (await w.TickAsync()).ShouldHaveSingleItem();

        w.Requests().Select(r => r.TaskId).ShouldBe([present, firstGone]);
        (await w.Rig.ReadTaskAsync(secondGone)).RemoteWorktreePath.ShouldBe(World.PathOf(secondGone));
        var queued = (await w.RowsAsync(secondGone)).ShouldHaveSingleItem();
        queued.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        queued.ReasonCode.ShouldBe("queued_next_tick");
        result.Queued.ShouldBe(1);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1794_eligible_trees_past_the_bound_get_a_queued_row_in_the_preview()
    {
        // CARD-1794 (1). The preview names every eligible tree: the ones past MaxRemovalsPerTick are queued rows,
        // not missing rows, and the runner sees one DryRun request.
        await using var w = await World.StartAsync();
        w.Settings.MaxRemovalsPerTick = 1;
        var sent = await w.EligibleAsync(TimeSpan.FromHours(40));
        var waiting = await w.EligibleAsync(TimeSpan.FromHours(30));
        w.Retain = (_, frame) => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.WouldRemove));

        var result = (await w.PreviewAsync()).ShouldHaveSingleItem();

        w.Requests().ShouldHaveSingleItem().TaskId.ShouldBe(sent);
        (await w.RowsAsync(waiting)).ShouldHaveSingleItem().ReasonCode.ShouldBe("queued_next_tick");
        result.Queued.ShouldBe(1);
        result.Eligible.ShouldBe(2);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1794_incomplete_inventory_proves_no_absence()
    {
        // CARD-1794 (1) control. An incomplete inventory cannot show a mirror is gone: every eligible task counts
        // against the one removal bound, oldest first, as before.
        await using var w = await World.StartAsync();
        w.Settings.MaxRemovalsPerTick = 2;
        w.InventoryComplete = false;
        var oldest = await w.EligibleAsync(TimeSpan.FromHours(100), present: false);
        var middle = await w.EligibleAsync(TimeSpan.FromHours(90), present: false);
        await w.EligibleAsync(TimeSpan.FromHours(20));

        var result = (await w.TickAsync()).ShouldHaveSingleItem();

        w.Requests().Select(r => r.TaskId).ShouldBe([oldest, middle]);
        result.AbsentSent.ShouldBe(0);
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1794_one_shot_execute_sends_execute_while_the_schedule_is_dry_run()
    {
        // CARD-1794 (4). The operator's one-shot reclaim executes for one allow-listed runner even while the scheduled
        // job is in DryRun (Execute=false); the runner still verifies every tree.
        await using var w = await World.StartAsync();
        w.Settings.Execute = false;
        var id = await w.EligibleAsync();

        var result = (await w.ExecuteOnceAsync()).ShouldHaveSingleItem();

        w.Requests().ShouldHaveSingleItem().Mode.ShouldBe(WorkspaceRetainModes.Execute);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBeNull();
        result.Sent.ShouldBe(1);
        await using var db = w.Rig.NewDb();
        var run = await db.WorktreeResidueRuns.AsNoTracking().SingleAsync(r => r.Id == result.RunId);
        run.Execute.ShouldBeTrue();
        run.Preview.ShouldBeFalse();
    }

    [Test]
    [Arguments("disabled")]
    [Arguments("unlisted")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1794_one_shot_execute_obeys_the_kill_switch_and_the_allow_list(string shape)
    {
        // CARD-1794 (4) control. Enabled=false, or a runner Runners does not name, sends nothing.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync();
        if (shape == "disabled")
            w.Settings.Enabled = false;

        var results = await w.ExecuteOnceAsync(shape == "unlisted" ? "server2-temp" : null);

        results.ShouldBeEmpty();
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(0);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1794_tick_result_names_the_effective_age_floor()
    {
        // CARD-1794 (2). The deployed preview held every task younger than 24 h as settling under a shipped floor of
        // 12 h; the tick result now names the floor the server actually applied, so an override is visible.
        await using var w = await World.StartAsync();
        w.Settings.MinTerminalHours = 24;
        var held = await w.EligibleAsync(TimeSpan.FromHours(13));
        var sent = await w.EligibleAsync(TimeSpan.FromHours(25));
        w.Retain = (_, frame) => World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.WouldRemove));

        var result = (await w.PreviewAsync()).ShouldHaveSingleItem();

        result.MinTerminalHours.ShouldBe(24);
        w.Requests().ShouldHaveSingleItem().TaskId.ShouldBe(sent);
        (await w.RowsAsync(held)).ShouldHaveSingleItem().ReasonCode.ShouldBe("settling");
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1794_report_kept_in_the_task_record_releases_an_unbranched_report_file()
    {
        // CARD-1794 (3) policy pin. The report file .antiphon/task-<id8>.md is not in the branch, but the task record
        // holds the report (Result): the mirror is a candidate. Without the record it stays report_unpreserved.
        await using var w = await World.StartAsync();
        var kept = await w.EligibleAsync(tweak: task =>
            task.ResultFilePath = World.PathOf(task.Id) + "/.antiphon/task-" + task.Id.ToString("N")[..8] + ".md");
        var unpreserved = await w.EligibleAsync(tweak: task =>
        {
            task.Result = null;
            task.ResultFilePath = World.PathOf(task.Id) + "/.antiphon/task-" + task.Id.ToString("N")[..8] + ".md";
        });

        await w.TickAsync();

        w.Requests().ShouldHaveSingleItem().TaskId.ShouldBe(kept);
        (await w.RowsAsync(unpreserved)).ShouldHaveSingleItem().ReasonCode.ShouldBe("report_unpreserved");
    }

    [Test]
    [Arguments("no-token")]
    [Arguments("wrong-token")]
    [Arguments("operator-token")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1794_execute_route_requires_the_operator_credential(string shape)
    {
        // CARD-1794 (4). POST .../runner-retention/{runnerId}/execute runs the one-shot reclaim only for the operator
        // credential; any other caller is 403 operator_token_required and nothing reaches the runner. CARD-1796: the
        // accepted call answers with its run, and the tick runs in the background.
        await using var w = await World.StartAsync();
        w.Settings.Execute = false;
        var id = await w.EligibleAsync();

        if (shape != "operator-token")
        {
            var refused = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ForbiddenException>(
                w.ExecuteRouteAsync(header: shape == "no-token" ? "" : "wrong-" + Guid.NewGuid().ToString("N")));
            refused.Message.ShouldContain("operator");
            w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(0);
            (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
            return;
        }

        var started = await w.ExecuteRouteAsync().WaitAsync(TimeSpan.FromSeconds(15));
        await w.Ticks.Background.WaitAsync(TimeSpan.FromSeconds(15));
        started.RunnerId.ShouldBe(w.Rig.RunnerId);
        w.Requests().ShouldHaveSingleItem().Mode.ShouldBe(WorkspaceRetainModes.Execute);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBeNull();
        var run = await w.RunAsync(started.RunId);
        run.Execute.ShouldBeTrue();
        run.Removed.ShouldBe(1);
        run.FinishedAt.ShouldNotBeNull();
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1796_scheduled_tick_waits_for_a_running_execute_tick()
    {
        // CARD-1796 (review e4276bff A-1). The one-shot execute and the scheduled tick census the same population; run
        // together, both inserted the same cursor key and the unique index aborted one after its sends. The scheduled tick
        // now waits for the running tick's turn: it contacts the runner only once the execute tick has finished.
        await using var w = await World.StartAsync();
        w.Settings.Execute = false;
        var first = await w.EligibleAsync(TimeSpan.FromHours(30));
        var second = await w.EligibleAsync(TimeSpan.FromHours(20));
        w.Rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceRetain);
        var execute = w.ExecuteOnceAsync();
        var held = await w.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 1);

        var scheduled = w.TickAsync();
        await Task.Delay(1500);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetentionInventory).ShouldBe(1, "the scheduled tick has not started");
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(1);
        scheduled.IsCompleted.ShouldBeFalse();

        w.Rig.Peer.Speak(PhoneHomeOperation.WorkspaceRetain);
        await w.Rig.Peer.EmitAsync(World.Answer(held, Removed()));
        var executed = (await execute.WaitAsync(TimeSpan.FromSeconds(15))).ShouldHaveSingleItem();
        var dry = (await scheduled.WaitAsync(TimeSpan.FromSeconds(15))).ShouldHaveSingleItem();

        executed.Sent.ShouldBe(2);
        dry.Sent.ShouldBe(0, "the execute tick retired both mirrors before the scheduled tick read the population");
        w.Requests().Select(r => r.TaskId).ShouldBe([first, second]);
        (await w.RunAsync(executed.RunId)).FinishedAt.ShouldNotBeNull();
        (await w.RunAsync(dry.RunId)).FinishedAt.ShouldNotBeNull();
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1796_a_running_tick_refuses_a_second_execute_and_a_preview()
    {
        // CARD-1796 (A-1). While a tick runs, the execute and preview routes refuse 409 tick_in_progress; neither runs a
        // second census nor waits on the request for the running one.
        await using var w = await World.StartAsync();
        await w.EligibleAsync(TimeSpan.FromHours(30));
        w.Rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceRetain);
        using var cancel = new CancellationTokenSource();
        var scheduled = w.TickAsync(cancel.Token);
        await w.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 1);

        var execute = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            w.ExecuteRouteAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        execute.Code.ShouldBe(RunnerWorktreeRetentionTicks.TickInProgressCode);
        var preview = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            w.PreviewAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        preview.Code.ShouldBe(RunnerWorktreeRetentionTicks.TickInProgressCode);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetentionInventory).ShouldBe(1);
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(1);

        await cancel.CancelAsync();
        try { await scheduled.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (OperationCanceledException) { }
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1796_execute_route_answers_with_its_run_before_the_tick_ends()
    {
        // CARD-1796 (A-2). The paced execute tick ran on the request, so a client or proxy timeout cancelled it mid-tick.
        // The route now answers at once with the run id; the run is readable while the tick runs and finishes on its own.
        await using var w = await World.StartAsync();
        w.Settings.Execute = false;
        var id = await w.EligibleAsync();
        w.Rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceRetain);

        var started = await w.ExecuteRouteAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var held = await w.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 1);
        started.RunnerId.ShouldBe(w.Rig.RunnerId);
        var running = await w.RunAsync(started.RunId);
        running.FinishedAt.ShouldBeNull("the run is readable while its tick runs");
        running.Execute.ShouldBeTrue();
        w.Ticks.Background.IsCompleted.ShouldBeFalse();

        w.Rig.Peer.Speak(PhoneHomeOperation.WorkspaceRetain);
        await w.Rig.Peer.EmitAsync(World.Answer(held, Removed()));
        await w.Ticks.Background.WaitAsync(TimeSpan.FromSeconds(15));
        var finished = await w.RunAsync(started.RunId);
        finished.FinishedAt.ShouldNotBeNull();
        finished.Removed.ShouldBe(1);
        w.Requests().ShouldHaveSingleItem().Mode.ShouldBe(WorkspaceRetainModes.Execute);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBeNull();
    }

    [Test]
    [Arguments("disabled")]
    [Arguments("unlisted")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1796_execute_route_refuses_while_off_or_for_an_unlisted_runner(string shape)
    {
        // CARD-1796 (A-2). A background tick cannot answer [] any more: the kill switch and the allow-list refuse at the
        // route (409), so no run id is handed out for a tick that will not run. Nothing reaches the runner.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync();
        if (shape == "disabled")
            w.Settings.Enabled = false;

        var refused = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            w.ExecuteRouteAsync(shape == "unlisted" ? "server2-temp" : null).WaitAsync(TimeSpan.FromSeconds(10)));

        refused.Code.ShouldBe(shape == "disabled" ? "retention_disabled" : "runner_not_listed");
        await w.Ticks.Background.WaitAsync(TimeSpan.FromSeconds(15));
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(0);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
    }

    [Test]
    [Arguments("enabled")]
    [Arguments("execute")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1796_a_stopped_tick_queues_its_unsent_remainder(string setting)
    {
        // CARD-1796 (review e4276bff A-4). A stop (kill switch, Execute off, tick budget) left the unsent in-bound
        // candidates with only the run's Deferred stop row; each is now a queued_next_tick census row, as the past-bound
        // tasks are, and the tick result counts them.
        await using var w = await World.StartAsync();
        var first = await w.EligibleAsync(TimeSpan.FromHours(30));
        var second = await w.EligibleAsync(TimeSpan.FromHours(25));
        var third = await w.EligibleAsync(TimeSpan.FromHours(20));
        w.Retain = (_, frame) =>
        {
            if (setting == "enabled")
                w.Settings.Enabled = false;
            else
                w.Settings.Execute = false;
            return World.Answer(frame, Removed());
        };

        var result = (await w.TickAsync()).ShouldHaveSingleItem();

        w.Requests().ShouldHaveSingleItem().TaskId.ShouldBe(first);
        result.Stopped.ShouldBe(setting == "enabled" ? "tick_stopped_disabled" : "tick_stopped_execute_off");
        result.Queued.ShouldBe(2);
        foreach (var unsent in new[] { second, third })
        {
            var row = (await w.RowsAsync(unsent)).ShouldHaveSingleItem();
            row.ReasonCode.ShouldBe("queued_next_tick");
            row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
        }
        (await w.RowsAsync()).ShouldContain(r => r.ReasonCode == result.Stopped && r.Outcome == nameof(WorktreeResidueCandidateOutcome.Deferred));
    }

    private sealed class StoppingLifetime : Microsoft.Extensions.Hosting.IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => _stopping.Cancel();
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1804_a_second_server_process_refuses_while_this_one_ticks()
    {
        // CARD-1804 (review 437ceb6c A-1). The tick turn was per process, so a second server process (its own turn, its own
        // connection, the same database) censused the same population at once. The tick now also holds a database-wide
        // advisory lock: the other process's preview and execute refuse 409 tick_in_progress, and its scheduled tick skips
        // without a census. Once this process's tick ends, the other process ticks normally.
        await using var w = await World.StartAsync();
        await w.EligibleAsync(TimeSpan.FromHours(30));
        w.Rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceRetain);
        var tick = w.TickAsync();
        var held = await w.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 1);

        // The other process: its own tick turn, its own scope and connection.
        var other = new RunnerWorktreeRetentionTicks(w.Rig.Provider.GetRequiredService<IServiceScopeFactory>(), w.SettingsMonitor,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RunnerWorktreeRetentionTicks>.Instance);
        async Task<T> OtherAsync<T>(Func<RunnerWorktreeRetentionService, Task<T>> call)
        {
            await using var scope = w.Rig.Provider.CreateAsyncScope();
            return await call(ActivatorUtilities.CreateInstance<RunnerWorktreeRetentionService>(scope.ServiceProvider, other));
        }

        var preview = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            OtherAsync(s => s.RunPreviewAsync(w.Rig.RunnerId, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(10)));
        preview.Code.ShouldBe(RunnerWorktreeRetentionTicks.TickInProgressCode);
        var execute = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
            w.ExecuteRouteAsync(ticks: other).WaitAsync(TimeSpan.FromSeconds(10)));
        execute.Code.ShouldBe(RunnerWorktreeRetentionTicks.TickInProgressCode);
        (await OtherAsync(s => s.RunTickAsync(CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(10)))
            .ShouldBeEmpty("the other process skips its scheduled tick while this one holds the lock");
        other.TryEnter().ShouldBeTrue("a refused or skipped tick gives its process turn back");
        other.Exit();
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetentionInventory).ShouldBe(1, "only this process censused");
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(1);
        await using (var db = w.Rig.NewDb())
            (await db.WorktreeResidueRuns.CountAsync()).ShouldBe(1, "a refused or skipped tick writes no run");

        w.Rig.Peer.Speak(PhoneHomeOperation.WorkspaceRetain);
        await w.Rig.Peer.EmitAsync(World.Answer(held, Removed()));
        (await tick.WaitAsync(TimeSpan.FromSeconds(15))).ShouldHaveSingleItem().Sent.ShouldBe(1);
        var after = (await OtherAsync(s => s.RunTickAsync(CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(15))).ShouldHaveSingleItem();
        (await w.RunAsync(after.RunId)).FinishedAt.ShouldNotBeNull();
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetentionInventory).ShouldBe(2, "the lock is released when the tick ends");
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1804_execute_route_saves_its_run_before_it_answers_and_a_switched_off_tick_finishes_it()
    {
        // CARD-1804 (A-2). The 202 came before the run row existed (a GET right after it could 404), and a kill switch
        // flipped between the route's check and the tick body left a run id with no run at all. The run now exists when
        // the route answers; a tick that then finds retention off finishes it with a Deferred tick_stopped_disabled row.
        await using var w = await World.StartAsync();
        w.Settings.Execute = false;
        var id = await w.EligibleAsync();
        using var hold = new ManualResetEventSlim(false);
        w.SettingsMonitor.Hold = hold;

        var started = await w.ExecuteRouteAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var pending = await w.RunAsync(started.RunId);
        pending.FinishedAt.ShouldBeNull();
        pending.Execute.ShouldBeTrue();
        pending.Scope.ShouldBe("runner-retention:" + w.Rig.RunnerId);
        w.Ticks.Background.IsCompleted.ShouldBeFalse("the background tick is parked on its settings read");

        w.Settings.Enabled = false;
        hold.Set();
        await w.Ticks.Background.WaitAsync(TimeSpan.FromSeconds(15));
        (await w.RunAsync(started.RunId)).FinishedAt.ShouldNotBeNull();
        var row = (await w.RunRowsAsync(started.RunId)).ShouldHaveSingleItem();
        row.ReasonCode.ShouldBe("tick_stopped_disabled");
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(0);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
    }

    [Test]
    [Arguments("execute-route")]
    [Arguments("scheduled")]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1804_a_failed_tick_finishes_its_run_with_tick_failed(string shape)
    {
        // CARD-1804 (A-2). A tick that threw left its run with no FinishedAt and no failure row. Here the send cursor the
        // tick inserts after a kept answer collides with one another writer saved meanwhile (the cross-process cursor
        // race), so the tick's save throws; the run is still finished, with a Deferred tick_failed row.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync();
        w.Retain = (_, frame) =>
        {
            using var db = w.Rig.NewDb();
            db.WorktreeResidueCandidateCursors.Add(new WorktreeResidueCandidateCursor
            {
                Id = Guid.NewGuid(), CandidateKey = RunnerWorktreeRetentionService.SendCursorKey(id), LastEvaluatedAt = w.Rig.Now,
            });
            db.SaveChanges();
            return World.Answer(frame, new PhoneHomeWorkspaceRetainResponse(WorkspaceRetentionCodes.Kept, WorkspaceRetentionCodes.Dirty));
        };

        Guid? runId;
        if (shape == "execute-route")
        {
            runId = (await w.ExecuteRouteAsync().WaitAsync(TimeSpan.FromSeconds(10))).RunId;
            await w.Ticks.Background.WaitAsync(TimeSpan.FromSeconds(15));
        }
        else
        {
            await Should.ThrowAsync<DbUpdateException>(w.TickAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            await using var db = w.Rig.NewDb();
            runId = (await db.WorktreeResidueRuns.AsNoTracking().SingleAsync()).Id;
        }

        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(1);
        (await w.RunAsync(runId)).FinishedAt.ShouldNotBeNull();
        var row = (await w.RunRowsAsync(runId)).ShouldHaveSingleItem();
        row.ReasonCode.ShouldBe("tick_failed");
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        w.Ticks.TryEnter().ShouldBeTrue("a failed tick gives the turn back");
        w.Ticks.Exit();
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1804_host_stop_awaits_a_running_execute_tick_and_its_run_is_finished_cancelled()
    {
        // CARD-1804 (A-2). Nothing awaited the background execute tick on host stop, and a shutdown-cancelled tick left its
        // run unfinished. Host stop now waits for the tick, which finishes its run with a Deferred tick_cancelled row.
        var lifetime = new StoppingLifetime();
        await using var w = await World.StartAsync(configure: services =>
            services.AddSingleton<Microsoft.Extensions.Hosting.IHostApplicationLifetime>(lifetime));
        w.Settings.Execute = false;
        var id = await w.EligibleAsync();
        w.Rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceRetain);
        var started = await w.ExecuteRouteAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await w.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 1);

        lifetime.StopApplication();
        await ((Microsoft.Extensions.Hosting.IHostedService)w.Ticks).StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));

        w.Ticks.Background.IsCompleted.ShouldBeTrue("host stop waited for the running tick");
        (await w.RunAsync(started.RunId)).FinishedAt.ShouldNotBeNull();
        var row = (await w.RunRowsAsync(started.RunId)).ShouldHaveSingleItem();
        row.ReasonCode.ShouldBe("tick_cancelled");
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1804_a_preview_cancelled_by_its_request_finishes_its_run_cancelled()
    {
        // CARD-1804 (A-3). The preview runs on its request, so a client or proxy timeout cancels it mid-tick; that is
        // accepted explicitly: the cancel no longer leaves an unfinished run, it finishes with a Deferred tick_cancelled
        // row and the turn is free for the next tick.
        await using var w = await World.StartAsync();
        await w.EligibleAsync(TimeSpan.FromHours(30));
        w.Rig.Peer.SilentFor(PhoneHomeOperation.WorkspaceRetain);
        using var aborted = new CancellationTokenSource();
        var preview = w.PreviewAsync(aborted.Token);
        await w.Rig.WaitForRequestsAsync(PhoneHomeOperation.WorkspaceRetain, 1);

        await aborted.CancelAsync();
        Exception? thrown = null;
        try { await preview.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (Exception ex) { thrown = ex; }
        thrown.ShouldBeAssignableTo<OperationCanceledException>();

        WorktreeResidueRun run;
        await using (var db = w.Rig.NewDb())
            run = await db.WorktreeResidueRuns.AsNoTracking().SingleAsync();
        run.Preview.ShouldBeTrue();
        run.FinishedAt.ShouldNotBeNull();
        var row = (await w.RunRowsAsync(run.Id)).ShouldHaveSingleItem();
        row.ReasonCode.ShouldBe("tick_cancelled");
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        w.Ticks.TryEnter().ShouldBeTrue("a cancelled preview gives the turn back");
        w.Ticks.Exit();
    }

    private const string TickLockId = "antiphon.runner-worktree-retention.tick";

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1812_a_failed_unlock_does_not_return_the_pooled_connection_still_holding_the_lock()
    {
        // CARD-1812 (R-1). pg_advisory_unlock throws while the session is still healthy and still holds the lock.
        // CloseConnectionAsync must not return that connection to the pool: Npgsql's DISCARD ALL is lazy, so the
        // lock would stay held until the handle is reused. The release warning names the tick id.
        var logs = new LogCapture();
        await using var w = await World.StartAsync(configure: services => services.AddSingleton<ILoggerProvider>(logs));
        await using var scope = w.Rig.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<RunnerWorktreeRetentionService>();
        var db = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        w.Ticks.TryEnter().ShouldBeTrue();
        try
        {
            (await service.TryLockAcrossProcessesAsync()).ShouldBeTrue();
            await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS c1812_trap");
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE OR REPLACE FUNCTION c1812_trap.pg_advisory_unlock(bigint) RETURNS boolean
                LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'c1812 unlock refused'; END $$;
                """);
            await db.Database.ExecuteSqlRawAsync("SET search_path = c1812_trap, pg_catalog");
            await service.UnlockAcrossProcessesAsync();
        }
        finally
        {
            w.Ticks.Exit();
        }

        logs.Items.Any(e => e.Level == LogLevel.Warning && e.Category.Contains(nameof(RunnerWorktreeRetentionService), StringComparison.Ordinal)
                && e.Message.Contains("tick lock release failed", StringComparison.Ordinal))
            .ShouldBeTrue("the unlock must fail on the live session; saw: " + logs.Render());
        logs.Items.Any(e => e.Level == LogLevel.Warning && e.Category.Contains(nameof(RunnerWorktreeRetentionService), StringComparison.Ordinal)
                && e.Message.Contains(TickLockId, StringComparison.Ordinal))
            .ShouldBeTrue("the release warning must name the tick id; saw: " + logs.Render());
        // CARD-1815 (A-2). pg_terminate_backend(pg_backend_pid()) succeeds by killing this backend, and PostgreSQL
        // reports that kill as FATAL 57P01. That exception is the break working, so it must not be logged as a failure.
        logs.Items.Any(e => e.Level == LogLevel.Warning && e.Category.Contains(nameof(RunnerWorktreeRetentionService), StringComparison.Ordinal)
                && e.Message.Contains("tick lock break failed", StringComparison.Ordinal))
            .ShouldBeFalse("a 57P01 break succeeded; saw: " + logs.Render());

        var probeCs = new NpgsqlConnectionStringBuilder(w.Rig.Schema.ConnectionString) { Pooling = false }.ConnectionString;
        await using var probe = new NpgsqlConnection(probeCs);
        await probe.OpenAsync();
        await using var command = probe.CreateCommand();
        command.CommandText = $"SELECT pg_try_advisory_lock(hashtextextended('{TickLockId}', 0))";
        var acquired = await command.ExecuteScalarAsync() is true;
        if (acquired)
        {
            command.CommandText = $"SELECT pg_advisory_unlock(hashtextextended('{TickLockId}', 0))";
            await command.ExecuteScalarAsync();
        }

        acquired.ShouldBeTrue("a failed unlock must not leave the advisory lock on a pooled connection");
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1812_a_skipped_scheduled_tick_warns_with_the_tick_id()
    {
        // CARD-1812 (R-1). A scheduled tick that finds the lock held writes no run. That skip was Information and did
        // not name the tick, so a lock stuck in the pool was invisible. It is a Warning that names the tick id.
        var logs = new LogCapture();
        await using var w = await World.StartAsync(configure: services => services.AddSingleton<ILoggerProvider>(logs));
        var holderCs = new NpgsqlConnectionStringBuilder(w.Rig.Schema.ConnectionString) { Pooling = false }.ConnectionString;
        await using var holder = new NpgsqlConnection(holderCs);
        await holder.OpenAsync();
        await using var hold = holder.CreateCommand();
        hold.CommandText = $"SELECT pg_try_advisory_lock(hashtextextended('{TickLockId}', 0))";
        (await hold.ExecuteScalarAsync() is true).ShouldBeTrue();
        try
        {
            (await w.TickAsync().WaitAsync(TimeSpan.FromSeconds(20))).ShouldBeEmpty();
            await using var db = w.Rig.NewDb();
            (await db.WorktreeResidueRuns.CountAsync()).ShouldBe(0);
            logs.Items.Any(e => e.Level == LogLevel.Warning && e.Category.Contains(nameof(RunnerWorktreeRetentionService), StringComparison.Ordinal)
                    && e.Message.Contains(TickLockId, StringComparison.Ordinal)
                    && e.Message.Contains("skipped", StringComparison.OrdinalIgnoreCase))
                .ShouldBeTrue("a skipped tick must warn with the tick id; saw: " + logs.Render());
        }
        finally
        {
            hold.CommandText = $"SELECT pg_advisory_unlock(hashtextextended('{TickLockId}', 0))";
            await hold.ExecuteScalarAsync();
        }
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1812_an_aborted_execute_save_finishes_the_run_and_does_not_send()
    {
        // CARD-1812 (R-2). The execute route saved its run with the request abort token. A cancel in the window before
        // the 202 left either no finisher or an unfinished row. The save ignores that token, and an abort before the
        // background tick starts finishes the run with tick_cancelled and does not send.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            w.Ticks.StartExecuteAsync(w.Rig.RunnerId, aborted.Token).WaitAsync(TimeSpan.FromSeconds(15)));
        await w.Ticks.Background.WaitAsync(TimeSpan.FromSeconds(15));

        WorktreeResidueRun run;
        await using (var db = w.Rig.NewDb())
            run = await db.WorktreeResidueRuns.AsNoTracking().SingleAsync();
        run.Execute.ShouldBeTrue();
        run.FinishedAt.ShouldNotBeNull();
        var row = (await w.RunRowsAsync(run.Id)).ShouldHaveSingleItem();
        row.ReasonCode.ShouldBe("tick_cancelled");
        row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
        w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(0);
        (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
        w.Ticks.TryEnter().ShouldBeTrue("an aborted execute gives the turn back");
        w.Ticks.Exit();
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1812_a_lost_tick_lock_sends_no_destructive_retain()
    {
        // CARD-1812 (R-3). The session lock can be gone while this connection is still up (released, or noticed only
        // at the next query after a drop). An Execute WorkspaceRetain is destructive: the tick re-checks ownership
        // and does not send when this session no longer holds the lock. The run finishes with tick_lock_lost and the
        // path stays. A retain already in flight is still the runner's to refuse.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync();
        await using var scope = w.Rig.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<RunnerWorktreeRetentionService>();
        var db = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        w.Ticks.TryEnter().ShouldBeTrue();
        try
        {
            (await service.TryLockAcrossProcessesAsync()).ShouldBeTrue();
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_unlock(hashtextextended('antiphon.runner-worktree-retention.tick', 0))");
            var result = (await service.RunEnteredAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20))).ShouldHaveSingleItem();
            result.Sent.ShouldBe(0);
            result.Stopped.ShouldBe("tick_lock_lost");
            (await w.RunAsync(result.RunId)).FinishedAt.ShouldNotBeNull();
            var row = (await w.RunRowsAsync(result.RunId)).Single(r => r.ReasonCode == "tick_lock_lost");
            row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
            w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(0);
            (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
        }
        finally
        {
            await service.UnlockAcrossProcessesAsync();
            w.Ticks.Exit();
        }
    }

    [Test]
    [ParallelLimiter<Antiphon.Tests.TestHelpers.WorkspaceRigConcurrencyLimit>]
    public async Task C1815_a_foreign_unpooled_tick_lock_is_not_this_session()
    {
        // CARD-1815 (A-1). The re-check must be this backend (pid = pg_backend_pid()). A second unpooled session that
        // holds the same tick key after this session released it is not this session's lock. Without that filter the
        // tick would see the other holder and send a destructive retain. The run stops tick_lock_lost and the path stays.
        await using var w = await World.StartAsync();
        var id = await w.EligibleAsync();
        await using var scope = w.Rig.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<RunnerWorktreeRetentionService>();
        var db = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        var holderCs = new NpgsqlConnectionStringBuilder(w.Rig.Schema.ConnectionString) { Pooling = false }.ConnectionString;
        NpgsqlConnection? holder = null;
        w.Ticks.TryEnter().ShouldBeTrue();
        try
        {
            (await service.TryLockAcrossProcessesAsync()).ShouldBeTrue();
            await using (var ownPid = db.Database.GetDbConnection().CreateCommand())
            {
                ownPid.CommandText = "SELECT pg_backend_pid()";
                var own = await ownPid.ExecuteScalarAsync();
                await db.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_unlock(hashtextextended('antiphon.runner-worktree-retention.tick', 0))");
                holder = new NpgsqlConnection(holderCs);
                await holder.OpenAsync();
                await using var hold = holder.CreateCommand();
                hold.CommandText = $"SELECT pg_try_advisory_lock(hashtextextended('{TickLockId}', 0))";
                (await hold.ExecuteScalarAsync() is true).ShouldBeTrue("the other unpooled session must hold the tick key");
                hold.CommandText = "SELECT pg_backend_pid()";
                var other = await hold.ExecuteScalarAsync();
                Convert.ToInt64(other).ShouldNotBe(Convert.ToInt64(own), "the holder must be a different backend from this tick session");
            }

            var result = (await service.RunEnteredAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20))).ShouldHaveSingleItem();
            result.Sent.ShouldBe(0);
            result.Stopped.ShouldBe("tick_lock_lost");
            (await w.RunAsync(result.RunId)).FinishedAt.ShouldNotBeNull();
            var row = (await w.RunRowsAsync(result.RunId)).Single(r => r.ReasonCode == "tick_lock_lost");
            row.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Deferred));
            w.Rig.Peer.RequestCount(PhoneHomeOperation.WorkspaceRetain).ShouldBe(0);
            (await w.Rig.ReadTaskAsync(id)).RemoteWorktreePath.ShouldBe(World.PathOf(id));
        }
        finally
        {
            try
            {
                if (holder is not null)
                {
                    await using var release = holder.CreateCommand();
                    release.CommandText = $"SELECT pg_advisory_unlock(hashtextextended('{TickLockId}', 0))";
                    await release.ExecuteScalarAsync();
                }
            }
            finally
            {
                if (holder is not null)
                    await holder.DisposeAsync();
                await service.UnlockAcrossProcessesAsync();
                w.Ticks.Exit();
            }
        }
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public List<Item> Items { get; } = [];
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }
        public string Render() => string.Join(" | ", Items.Select(e => e.Level + ":" + e.Message));

        internal sealed record Item(LogLevel Level, string Category, string Message);

        private sealed class Logger(LogCapture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner.Items)
                    owner.Items.Add(new Item(level, category, formatter(state, exception)));
            }
        }
    }

    private static async Task<Guid> SeedSessionAsync(Rig rig, SessionStatus status)
    {
        var id = Guid.NewGuid();
        await using var db = rig.NewDb();
        db.AgentSessions.Add(new AgentSession
        {
            Id = id, DefinitionName = "grok", AgentKind = AgentKind.Grok, Status = status, Cwd = rig.WorkspacePath,
            Cols = 80, Rows = 24, CreatedAt = rig.Now, StartedAt = rig.Now, LastSeenAt = rig.Now,
            RunnerId = rig.RunnerId, RunnerStoreId = rig.Host.StoreId, RunnerCwd = "/work",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedSyncDebtAsync(Rig rig, Guid taskId, AgentTaskSyncDebtState state)
    {
        await using var db = rig.NewDb();
        db.AgentTaskSyncDebts.Add(new AgentTaskSyncDebt
        {
            Id = Guid.NewGuid(), TaskId = taskId, Attempt = 1, SettlementEventId = Guid.NewGuid(), State = state,
            ReasonCode = "runner_sync_lease_busy", CreatedAt = rig.Now, UpdatedAt = rig.Now,
        });
        await db.SaveChangesAsync();
    }
}
