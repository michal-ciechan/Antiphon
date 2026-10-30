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
        await s.PastGraceAsync(s.Harness());
        (await s.ReadTaskAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Working);
        await using var verify = s.CreateContext();
        (await verify.AgentSessions.SingleAsync(x => x.Id == task.SessionId)).Status.ShouldBe(SessionStatus.Stopping);
    }

    [Test]
    public async Task Pool_and_terminal_seat_do_not_release_from_code_zero()
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync();
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
    }

    [Test]
    public void Launch_cleanup_does_not_steal_a_normal_exit()
    {
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
    }

    [Test]
    public async Task Api_attempt_and_diagnostics_preserve_optional_signal()
    {
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Failed);
        await SetExitAsync(s, task.SessionId, 0, 9);
        await using var db = s.CreateContext();
        var row = await db.AgentSessions.SingleAsync(x => x.Id == task.SessionId);
        var card = new Card { AgentSessions = [row] };
        BoardService.ToCardDto(card).Sessions.Single().ExitSignal.ShouldBe(9);
        var body = CardReviewService.BuildPullRequestBody(card, new RunAttempt { ExitCode = 0, ExitSignal = 9 });
        body.ShouldContain("killed by signal 9 (SIGKILL)");
        var runner = new Card1619ConsumerFixture.FakeRunnerClient(s.ConnectionString);
        var attention = new AttentionService(db, runner, Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()), TimeProvider.System, NullLogger<AttentionService>.Instance);
        var service = new DiagnosticsBundleService(db, null!, attention, runner, TimeProvider.System, NullLogger<DiagnosticsBundleService>.Instance);
        await using var stream = await service.BuildAsync(new BugReportRequest(Route: "/agents", SessionId: task.SessionId), null, default);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("session.json")!.Open());
        using var json = JsonDocument.Parse(await reader.ReadToEndAsync());
        json.RootElement.GetProperty("exitSignal").GetInt32().ShouldBe(9);
        json.RootElement.GetProperty("exitCode").GetInt32().ShouldBe(0);
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
