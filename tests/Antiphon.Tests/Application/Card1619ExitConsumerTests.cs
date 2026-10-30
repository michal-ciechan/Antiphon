using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NativeLinuxOnly("CARD-1619 real Linux exit policy and consumers")]
[ParallelLimiter<ProcessSpawnLimit>]
public class Card1619ExitConsumerTests
{
    [Test]
    [Arguments(9, 0, "killed by signal 9 (SIGKILL); sender unknown")]
    [Arguments(15, 0, "killed by signal 15 (SIGTERM); sender unknown")]
    [Arguments(0, 0, "process exited with exit code 0")]
    [Arguments(0, 23, "process exited with exit code 23")]
    public async Task Native_exit_reaches_failure_and_attention_text(int signal, int code, string clause)
    {
        var (native, json) = await Card1619NativeExitFixture.CaptureAsync(signal, code);
        await using var lifetime = native;
        // Replay the real native snapshot through the production terminal adapter. Its legacy
        // value may differ, but the runtime persistence seam must still read actual evidence.
        var snapshot = new RunnerContractMapper().Map(native.Snapshot);
        await AssertAdapterAsync(snapshot);
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Running,
            nativeId: native.Id, nativeGeneration: native.Generation);
        var h = s.Harness(native.Id);
        var evt = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionExited, json)!.Exited!;
        (await s.Runtime.ObserveExitAsync(evt, default)).ShouldBe(SessionExitDisposition.Applied);
        await using (var db = s.CreateContext())
        {
            var row = await db.AgentSessions.SingleAsync(x => x.Id == native.Id);
            row.ExitCode.ShouldBe(code);
            row.ExitSignal.ShouldBe(signal);
            row.Status.ShouldBe(signal > 0 || code != 0 ? SessionStatus.Failed : SessionStatus.Stopped);
            (await db.Agents.SingleAsync(x => x.Id == task.AgentId)).Status.ShouldBe(
                signal > 0 || code != 0 ? AgentStatus.Failed : AgentStatus.Stopped);
        }
        var attention = (await s.AttentionItemsAsync()).Single(x => x.Kind == AttentionKind.DeadSession);
        attention.Evidence.ShouldContain(clause);
        attention.Evidence.ShouldNotContain("exited on its own");
        await s.PastGraceAsync(h);
        var failed = await s.ReadTaskAsync(task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed, "an exit alone is never a successful task report");
        failed.FailureReason.ShouldContain(clause);
        failed.FailureReason.ShouldNotContain("exited on its own");
        h.Runner.Killed.ShouldBeEmpty();
        // The accepted snapshot must classify exactly like the live native envelope.
        await using var reconciled = await Card1619ConsumerFixture.Scenario.CreateAsync();
        await reconciled.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Running,
            nativeId: native.Id, nativeGeneration: native.Generation);
        await using var scan = reconciled.CreateContext();
        var runner = new SessionReconciliationServiceTests.FakeRunnerClient { Sessions = [new RunnerContractMapper().Map(native.Snapshot)] };
        await SessionReconciliationServiceTests.BuildService(scan, runner, new MockEventBus(), launchResumeEnabled: false).ScanAsync(default);
        scan.ChangeTracker.Clear();
        var recovered = await scan.AgentSessions.SingleAsync(x => x.Id == native.Id);
        recovered.Status.ShouldBe(signal > 0 || code != 0 ? SessionStatus.Failed : SessionStatus.Stopped);
        var text = AgentTaskLiveness.ClassifyFailure(native.Id, new(recovered.Status, recovered.EndedAt,
            recovered.FailureReason, recovered.TerminationSource, recovered.ExitCode, ExitSignal: recovered.ExitSignal), true);
        text.Reason.ShouldContain(clause);
        if (signal == 9)
        {
            await using var policy = await Card1619ConsumerFixture.Scenario.CreateAsync();
            await policy.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Running,
                nativeId: native.Id, nativeGeneration: native.Generation);
            policy.Harness(native.Id);
            await policy.Runtime.ObserveExitAsync(evt with { ExitReason = AgentExitReason.CpuSpinKilled }, default);
            await using var db = policy.CreateContext();
            (await db.AgentSessions.SingleAsync(x => x.Id == native.Id)).Status.ShouldBe(SessionStatus.Stopped,
                "CPU-spin reclaim is a resumable policy stop, never a successful task execution");
        }
    }

    [Test]
    public async Task Report_before_signal_death_still_settles()
    {
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Failed);
        await SetExitAsync(s, task.SessionId, 0, 9);
        await s.AddMarkedReportAsync(task.SessionId, task.Id);
        var h = s.Harness(task.SessionId);
        await s.PastGraceAsync(h);
        var row = await s.ReadTaskAsync(task.Id);
        row.Status.ShouldBe(AgentTaskStatus.Succeeded);
        row.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        h.Runner.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task Signal_death_without_report_never_succeeds()
    {
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Failed);
        await SetExitAsync(s, task.SessionId, 0, 9);
        await s.AddTranscriptNoiseAsync(task.SessionId);
        await s.PastGraceAsync(s.Harness(task.SessionId));
        var row = await s.ReadTaskAsync(task.Id);
        row.Status.ShouldBe(AgentTaskStatus.Failed);
        row.ReportEvidence.ShouldNotBe(AgentTaskReportEvidence.Marked);
        row.FailureReason.ShouldContain("killed by signal 9 (SIGKILL)");
    }

    [Test]
    public async Task Check_and_supervisor_keep_signal_evidence()
    {
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Failed, role: AgentTaskRole.Check,
            replyTo: AgentTaskReplyTo.None, isPoolDelegate: false);
        await SetExitAsync(s, task.SessionId, 0, 15);
        await using (var db = s.CreateContext())
        {
            var agent = await db.Agents.SingleAsync(x => x.Id == task.AgentId);
            agent.AlwaysOn = true;
            agent.Status = AgentStatus.Failed;
            await db.SaveChangesAsync();
        }
        var root = Path.Combine(Path.GetTempPath(), "card1619-supervisor-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(root, [], connectionString: s.ConnectionString);
            await harness.Supervisor().TickAsync(default);
            await using var db = s.CreateContext();
            var incident = await db.AgentIncidents.SingleAsync(x => x.AgentId == task.AgentId && x.Kind == AgentIncidentKind.Crash);
            incident.ExitSignal.ShouldBe(15);
            incident.Message.ShouldContain("killed by signal 15 (SIGTERM)");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        await s.PastGraceAsync(s.Harness(task.SessionId));
        (await s.ReadTaskAsync(task.Id)).FailureReason.ShouldContain("killed by signal 15 (SIGTERM)");
        (await s.ParentNoteBodiesAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task Stop_stuck_remains_pending_without_observed_exit()
    {
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Stopping,
            terminationSource: SessionTerminationSource.SystemRequest);
        await using (var db = s.CreateContext())
        {
            var session = await db.AgentSessions.SingleAsync(x => x.Id == task.SessionId);
            session.LastSeenAt = DateTime.UtcNow.AddMinutes(-6);
            session.ExitCode = 0; // compatibility/policy hint is not an observation of death
            await db.SaveChangesAsync();
        }
        var attention = new AttentionService(s.CreateContext(), new Card1619ConsumerFixture.FakeRunnerClient(s.ConnectionString),
            Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()), TimeProvider.System,
            NullLogger<AttentionService>.Instance);
        var item = (await attention.GetAsync(default)).Items.Single(x => x.Kind == AttentionKind.SessionStopStuck && x.SessionId == task.SessionId);
        item.Evidence.ShouldContain("signal unknown");
        item.Evidence.ShouldContain("exit unobserved");
        item.Evidence.ShouldNotContain("process ended");
        await s.PastGraceAsync(s.Harness());
        (await s.ReadTaskAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Working);
        await using var verify = s.CreateContext();
        (await verify.AgentSessions.SingleAsync(x => x.Id == task.SessionId)).Status.ShouldBe(SessionStatus.Stopping);
    }

    [Test]
    public async Task Pool_and_terminal_seat_do_not_release_from_code_zero()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
        f.Harness.Provider.GetRequiredService<IOptions<DelegationSettings>>().Value.PoolEnabled = false;
        await f.EditAsync((task, _) => task.Workspace = WorkspaceMode.Shared);
        f.Wire.Qualified = f.Wire.Qualified with { Status = TerminalSeatQualificationStatus.Working, Token = null };
        await using (var db = f.Db())
        {
            var row = await db.AgentSessions.SingleAsync(x => x.Id == f.SessionId);
            row.ExitCode = 0;
            row.ExitSignal = 9;
            await db.SaveChangesAsync();
        }
        await f.ReleaseFromSettlementAsync();
        await f.SweepAsync(janitor: true);
        f.Wire.ConditionalCommands.ShouldBe(0);
        f.Wire.ForceCommands.ShouldBe(0);
        f.RecordedStops.Killed.ShouldBeEmpty();
        await using var verify = f.Db();
        (await verify.Agents.AnyAsync(x => x.Id == f.AgentId)).ShouldBeTrue();
        (await verify.RunnerSeatReleases.SingleAsync()).ReasonCode.ShouldBe("Working");
        await using var warm = await RunnerSeatReleaseFixture.CreateAsync();
        await warm.EditAsync((task, _) => task.Workspace = WorkspaceMode.Shared);
        await using (var db = warm.Db())
        {
            var session = await db.AgentSessions.SingleAsync(x => x.Id == warm.SessionId);
            session.ExitCode = 0;
            session.ExitSignal = 9;
            await db.SaveChangesAsync();
        }
        await warm.ReleaseFromSettlementAsync();
        await warm.SweepAsync(janitor: true);
        warm.Wire.ConditionalCommands.ShouldBe(0);
        warm.RecordedStops.Killed.ShouldBeEmpty();
        await using var warmed = warm.Db();
        (await warmed.RunnerSeatReleases.SingleAsync()).ReasonCode.ShouldBe("WarmPool");
        (await warmed.Agents.SingleAsync(x => x.Id == warm.AgentId)).PoolIdleSince.ShouldNotBeNull();
    }

    [Test]
    public async Task Launch_cleanup_does_not_steal_a_normal_exit()
    {
        // A structured ready-gate kill returns compatibility -1 while the event stores actual
        // zero/signal 9. Exercise the real launch catch, not only the pure ownership predicate.
        await using var store = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var seed = await store.AddTaskAsync(AgentTaskStatus.Dispatched, SessionStatus.Starting);
        var inner = new Antiphon.Tests.Agents.FakeAgentProtocolAdapter { ReadyResult = false };
        var structured = new StructuredAdapter(inner, new AgentExitObservation(0, 9));
        await using var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions {
            AlwaysOn = false, ConnectionString = store.ConnectionString,
            ConfigureServices = services => services.AddSingleton<IAgentProtocolAdapterFactory>(new SingleAdapterFactory(structured))
        });
        inner.OnKillGeneration = async () =>
        {
            await using var db = store.CreateContext();
            var target = await db.AgentSessions.SingleAsync(x => x.Id == seed.SessionId);
            await harness.Runtime.ObserveExitAsync(new SessionRunnerExitedEvent(seed.SessionId, 0,
                AgentExitReason.KilledByRequest, 0, target.StartedAt, ExitSignal: 9), default);
        };
        using (var scope = harness.Provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<AgentSessionService>();
            var spec = new AgentLaunchSpec("raw", AgentKind.Raw, "/bin/sh", [], new Dictionary<string, string>(),
                Path.GetTempPath(), 80, 24);
            var thrown = await Should.ThrowAsync<Exception>(() => service.LaunchInteractiveAsync(seed.SessionId,
                seed.AgentId, spec, null, false, null, default));
            thrown.GetBaseException().Message.ShouldContain(AgentSessionService.NotReadyBase);
        }
        (await structured.Exited).ShouldBe(-1);
        await using (var db = store.CreateContext())
        {
            var killedRow = await db.AgentSessions.SingleAsync(x => x.Id == seed.SessionId);
            killedRow.FailureReason.ShouldBe(AgentSessionService.NotReadyBase);
            killedRow.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
            killedRow.ExitCode.ShouldBe(0);
            killedRow.ExitSignal.ShouldBe(9);
        }


        // Exercise the actual ownership predicate, keeping its generation/time/cause checks.
        var generation = DateTime.UtcNow.AddMinutes(-1);
        var type = typeof(AgentSessionService).GetNestedType("LaunchCleanupKill", BindingFlags.NonPublic)!;
        var ctor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(x => x.GetParameters().Length >= 5);
        object?[] args = ctor.GetParameters().Length == 5
            ? [true, generation, generation, AgentExitReason.KilledByRequest, 0]
            : [true, generation, generation, AgentExitReason.KilledByRequest, 0, 9];
        var kill = ctor.Invoke(args);
        var method = typeof(AgentSessionService).GetMethod("LaunchKillOwnsProcessExit", BindingFlags.Static | BindingFlags.NonPublic)!;
        var row = new AgentSession { StartedAt = generation, EndedAt = DateTime.UtcNow,
            TerminationSource = SessionTerminationSource.ProcessExit, ExitCode = 0, ExitSignal = 0 };
        ((bool)method.Invoke(null, [row, kill, generation])!).ShouldBeFalse("known normal exit cannot belong to a signal kill");
        row.ExitSignal = 9;
        ((bool)method.Invoke(null, [row, kill, generation])!).ShouldBeTrue("compare actual zero/signal 9, never compatibility -1");
        row.ExitCode = 1;
        row.ExitSignal = 0;
        var windowsKill = ctor.Invoke([true, generation, generation, AgentExitReason.KilledByRequest, 1, 0]);
        ((bool)method.Invoke(null, [row, windowsKill, generation])!).ShouldBeTrue("Windows no-signal kill ownership remains unchanged");
    }

    [Test]
    public async Task Api_attempt_and_diagnostics_preserve_optional_signal()
    {
        var failures = new List<Exception>();
        foreach (var check in new Func<Task>[] { AssertUnknownRecoveryAsync, AssertOptionalProjectionAsync,
            async () =>
            {
                await AssertAdapterAsync(new SessionRunnerSessionDto(Guid.NewGuid(), null, DateTime.UtcNow,
                    "Exited", null, AgentExitReason.Unknown, 0));
                // Windows producers report signal zero with the original signed code. This portable
                // consumer witness complements the still-owed native modern/inbox CP-2.
                await AssertAdapterAsync(new SessionRunnerSessionDto(Guid.NewGuid(), null, DateTime.UtcNow,
                    "Exited", -1, AgentExitReason.KilledByRequest, 0, ExitSignal: 0));
            } })
        {
            try { await check(); } catch (Exception ex) { failures.Add(ex); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private static async Task AssertOptionalProjectionAsync()
    {
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Failed);
        await SetExitAsync(s, task.SessionId, 0, 9);
        await using var db = s.CreateContext();
        var row = await db.AgentSessions.SingleAsync(x => x.Id == task.SessionId);
        var card = new Card { AgentSessions = [row] };
        BoardService.ToCardDto(card).Sessions.Single().ExitSignal.ShouldBe(9);
        var body = CardReviewService.BuildPullRequestBody(card, new RunAttempt { ExitCode = 0, ExitSignal = 9 });

        var runner = new Card1619ConsumerFixture.FakeRunnerClient(s.ConnectionString);
        var attention = new AttentionService(db, runner, Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()), TimeProvider.System, NullLogger<AttentionService>.Instance);
        var service = new DiagnosticsBundleService(db, null!, attention, runner, TimeProvider.System, NullLogger<DiagnosticsBundleService>.Instance);
        await using var stream = await service.BuildAsync(new BugReportRequest(Route: "/agents", SessionId: task.SessionId), null, default);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("session.json")!.Open());
        using var json = JsonDocument.Parse(await reader.ReadToEndAsync());
        body.ShouldSatisfyAllConditions(
            value => value.ShouldContain("killed by signal 9 (SIGKILL)"),
            _ => json.RootElement.TryGetProperty("exitSignal", out var signal).ShouldBeTrue("diagnostic archive carries the actual signal"),
            _ => json.RootElement.GetProperty("exitCode").GetInt32().ShouldBe(0));
        json.RootElement.GetProperty("exitSignal").GetInt32().ShouldBe(9);
    }

    private static async Task AssertUnknownRecoveryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "card1619-unknown-" + Guid.NewGuid().ToString("N"));
        var settings = new Antiphon.SessionRunner.SessionRunnerSettings { SessionLogPath = root, CpuWatchdogEnabled = false };
        try
        {
            Directory.CreateDirectory(settings.PtyHostManifestDir);
            var id = Guid.NewGuid();
            var generation = DateTime.UtcNow;
            new Antiphon.PtyHost.Protocol.PtyHostManifest {
                SessionId = id, PipeName = "unused-" + id.ToString("N"), HostPid = 0,
                HostStartTimeUtc = generation, CreatedAtUtc = generation, ExitedAtUtc = generation,
                AcceptedStartedAt = generation, ExitReason = "ProcessVanished"
            }.SaveAtomic(Antiphon.PtyHost.Protocol.PtyHostManifest.PathFor(settings.PtyHostManifestDir, id));
            await using var runtime = new Antiphon.SessionRunner.SessionRunnerRuntime(Options.Create(settings),
                NullLogger<Antiphon.SessionRunner.SessionRunnerRuntime>.Instance);
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await runtime.AdoptOrphanedHostsAsync(new Antiphon.SessionRunner.SystemProcessLivenessProbe(), ct.Token);
            var observed = runtime.Get(id);
            observed.Status.ShouldBe("Exited");
            observed.ExitCode.ShouldBeNull("a missing wait result is not a native -1");
            observed.ExitSignal.ShouldBeNull();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class SingleAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class StructuredAdapter(Antiphon.Tests.Agents.FakeAgentProtocolAdapter inner,
        AgentExitObservation observation) : IAgentProtocolAdapter
    {
        public Task<int> Exited => CompatibilityAsync();
        private async Task<int> CompatibilityAsync() { await inner.Exited; return observation.ExitSignal is > 0 ? -1 : observation.ExitCode ?? -1; }
        public Task<AgentExitObservation> Completion => ObserveAsync();
        private async Task<AgentExitObservation> ObserveAsync() { await inner.Exited; return observation; }
        public int? Pid => inner.Pid;
        public AgentExitReason ExitReason => inner.ExitReason;
        public string? AuditDirectory => null;
        public event Action<string>? OnTextDelta { add { inner.OnTextDelta += value; } remove { inner.OnTextDelta -= value; } }
        public Task StartAsync(AgentLaunchSpec spec, CancellationToken ct) => inner.StartAsync(spec, ct);
        public Task<bool> KillAsync(TimeSpan timeout, CancellationToken ct) => inner.KillAsync(timeout, ct);
        public Task<AdapterGenerationKill> KillGenerationAsync(DateTime generation, TimeSpan timeout, CancellationToken ct) => inner.KillGenerationAsync(generation, timeout, ct);
        public Task SendPromptAsync(string prompt, CancellationToken ct) => inner.SendPromptAsync(prompt, ct);
        public Task<bool> WaitForFirstPromptOutputAsync(TimeSpan timeout, CancellationToken ct) => inner.WaitForFirstPromptOutputAsync(timeout, ct);
        public Task SendInputAsync(string input, CancellationToken ct) => inner.SendInputAsync(input, ct);
        public Task ResizeAsync(int cols, int rows, CancellationToken ct) => inner.ResizeAsync(cols, rows, ct);
        public Task<bool> WaitForReadyAsync(CancellationToken ct) => inner.WaitForReadyAsync(ct);
        public Task<AgentTurnResult> WaitForTurnCompleteAsync(CancellationToken ct) => inner.WaitForTurnCompleteAsync(ct);
        public string SnapshotRawOutput() => inner.SnapshotRawOutput();
        public Task<string> SnapshotRawOutputAsync(CancellationToken ct) => inner.SnapshotRawOutputAsync(ct);
        public string SnapshotRenderedScreen() => inner.SnapshotRenderedScreen();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private static async Task AssertAdapterAsync(SessionRunnerSessionDto observed)
    {
        var runner = new SessionReconciliationServiceTests.FakeRunnerClient {
            Sessions = [observed with { Status = "Running", ExitCode = null, ExitSignal = null }] };
        await using var adapter = new RunnerRawAdapter(runner);
        await adapter.AttachAsync(observed.SessionId, default);
        runner.Sessions = [observed];
        var actual = await adapter.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        actual.ExitCode.ShouldBe(observed.ExitCode);
        actual.ExitSignal.ShouldBe(observed.ExitSignal);
        var compatibility = await adapter.Exited;
        await using var store = await Card1619ConsumerFixture.Scenario.CreateAsync();
        store.Harness();
        store.Runtime.Register(observed.SessionId, adapter);
        var persisted = await store.Runtime.GetSessionAsync(observed.SessionId, default);
        persisted.Status.ShouldBe("Exited");
        compatibility.ShouldSatisfyAllConditions(
            _ => compatibility.ShouldBe(observed.ExitSignal is > 0 ? -1 : observed.ExitCode ?? -1),
            _ => persisted.ExitCode.ShouldBe(observed.ExitCode, "compatibility -1 is never a native code"),
            _ => persisted.ExitSignal.ShouldBe(observed.ExitSignal));
    }

    private static async Task SetExitAsync(Card1619ConsumerFixture.Scenario s, Guid id, int code, int signal)
    {
        await using var db = s.CreateContext();
        var row = await db.AgentSessions.SingleAsync(x => x.Id == id);
        row.ExitCode = code;
        row.ExitSignal = signal;
        row.TerminationSource = SessionTerminationSource.ProcessExit;
        row.EndedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
