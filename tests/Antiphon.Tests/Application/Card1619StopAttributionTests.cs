using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NativeLinuxOnly("CARD-1619 stop attribution through actual Linux PTY and server consumers")]
[ParallelLimiter<ProcessSpawnLimit>]
public class Card1619StopAttributionTests
{
    [Test]
    public async Task Matching_live_issuer_intent_is_qualified_Antiphon_stop()
    {
        var (native, json) = await Card1619NativeExitFixture.CaptureAsync(9, requested: true);
        await using var owned = native;
        var evt = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionExited, json)!.Exited!;
        evt.ExitStopIntent.ShouldNotBeNull();
        evt.ExitStopIntent.Provenance.ShouldBe(ProcessStopProvenance.LiveIssuer);
        await VerifyAsync(native, evt, "stopped by Antiphon (SIGKILL;");
    }

    [Test]
    public async Task No_intent_means_sender_unknown()
    {
        var (native, json) = await Card1619NativeExitFixture.CaptureAsync(15);
        await using var owned = native;
        await VerifyAsync(native, RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionExited, json)!.Exited!,
            "killed by signal 15 (SIGTERM); sender unknown");
    }

    [Test]
    public async Task Mismatched_generation_or_signal_is_not_attributed()
    {
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Running);
        await using var db = s.CreateContext();
        var row = await db.AgentSessions.SingleAsync(x => x.Id == task.SessionId);
        var intent = Intent(row.Id, row.StartedAt) with { AttemptedSignal = 15 };
        SessionExitEvidence.Apply(row, 0, 9, intent).ShouldBeTrue();
        SessionExitPolicy.Describe(row.ExitCode, row.ExitSignal, row.TerminationSource,
            SessionExitEvidence.ReadIntent(row.ExitStopIntentJson)).ShouldNotContain("stopped by Antiphon");
        SessionExitEvidence.Bound(intent with { AcceptedStartedAt = row.StartedAt.AddSeconds(1) }, row.Id, row.StartedAt).ShouldBeNull();
        SessionExitEvidence.Bound(intent with { SessionId = Guid.NewGuid() }, row.Id, row.StartedAt).ShouldBeNull();
        SessionExitPolicy.Describe(0, 9, intent: intent with { Target = ProcessStopTarget.Host, AttemptedSignal = 9 })
            .ShouldNotContain("stopped by Antiphon");
    }

    [Test]
    public async Task Requested_only_and_failed_intents_remain_uncertain()
    {
        var id = Guid.NewGuid(); var generation = DateTime.UtcNow;
        foreach (var outcome in new[] { ProcessStopOutcome.Requested, ProcessStopOutcome.Failed })
        {
            var intent = Intent(id, generation) with { Outcome = outcome };
            SessionExitPolicy.Describe(0, 9, intent: intent).ShouldNotContain("stopped by Antiphon");
            SessionExitPolicy.Describe(0, 0, intent: intent).ShouldContain("process exited with exit code 0");
        }
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Failed);
        await using var db = s.CreateContext();
        var row = await db.AgentSessions.SingleAsync(x => x.Id == task.SessionId);
        SessionExitEvidence.Apply(row, 0, 9, Intent(row.Id, row.StartedAt) with { Outcome = ProcessStopOutcome.Failed });
        await db.SaveChangesAsync();
        (await s.AttentionItemsAsync()).Single(x => x.Kind == AttentionKind.DeadSession).Evidence.ShouldNotContain("stopped by Antiphon");
    }

    [Test]
    public async Task Late_intent_enrichment_preserves_known_exit()
    {
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Failed);
        await using var db = s.CreateContext();
        var row = await db.AgentSessions.SingleAsync(x => x.Id == task.SessionId);
        var intent = Intent(row.Id, row.StartedAt);
        SessionExitEvidence.Apply(row, 0, 9, intent with { Provenance = ProcessStopProvenance.RecoveredUnverified });
        SessionExitEvidence.Apply(row, null, null, intent).ShouldBeTrue();
        SessionExitEvidence.ReadIntent(row.ExitStopIntentJson)!.Provenance.ShouldBe(ProcessStopProvenance.LiveIssuer);
        SessionExitPolicy.Describe(row.ExitCode, row.ExitSignal, intent: SessionExitEvidence.ReadIntent(row.ExitStopIntentJson))
            .ShouldContain("stop record not durable");
        SessionExitEvidence.Apply(row, null, null, intent with { Durable = true });
        SessionExitPolicy.Describe(row.ExitCode, row.ExitSignal, intent: SessionExitEvidence.ReadIntent(row.ExitStopIntentJson))
            .ShouldContain("matching stop request recorded");
        SessionExitEvidence.Apply(row, null, null, intent with { Provenance = ProcessStopProvenance.RecoveredUnverified });
        SessionExitEvidence.ReadIntent(row.ExitStopIntentJson)!.Provenance.ShouldBe(ProcessStopProvenance.LiveIssuer);
        row.ExitCode.ShouldBe(0); row.ExitSignal.ShouldBe(9);
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task Operator_and_policy_sources_are_not_overwritten()
    {
        var (native, json) = await Card1619NativeExitFixture.CaptureAsync(9, requested: true);
        await using var owned = native;
        var evt = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionExited, json)!.Exited!;
        foreach (var source in new[] { SessionTerminationSource.OperatorRequest, SessionTerminationSource.PolicyRefresh })
            await VerifyAsync(native, evt, "stopped by Antiphon (SIGKILL;", source);
    }

    [Test]
    public async Task Planted_record_self_signal_stays_unconfirmed_in_task_and_attention()
    {
        var (native, json) = await Card1619NativeExitFixture.CaptureAsync(9, planted: true);
        await using var owned = native;
        var evt = RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionExited, json)!.Exited!;
        evt.ExitStopIntent.ShouldNotBeNull();
        evt.ExitStopIntent.Provenance.ShouldBe(ProcessStopProvenance.RecoveredUnverified);
        await VerifyAsync(native, evt, "killed by signal 9 (SIGKILL); sender unconfirmed (untrusted stop record)");
    }

    [Test]
    public async Task Server_request_without_issuer_is_request_only()
    {
        var (native, json) = await Card1619NativeExitFixture.CaptureAsync(9);
        await using var owned = native;
        await VerifyAsync(native, RunnerContractMapper.ParseEvent(SessionRunnerEventNames.SessionExited, json)!.Exited!,
            "Antiphon stop requested; killed by signal 9 (SIGKILL); sender unconfirmed", SessionTerminationSource.OperatorRequest);
    }

    private static ProcessStopIntentDto Intent(Guid id, DateTime generation) => new() {
        SessionId = id, AcceptedStartedAt = generation, RequestId = Guid.NewGuid(), IssuerBootId = Guid.NewGuid(),
        HostInstanceId = Guid.NewGuid(), Target = ProcessStopTarget.Child, TargetPid = 1234,
        TargetStartedAtUtc = generation, AttemptedSignal = 9, Outcome = ProcessStopOutcome.Issued,
        Provenance = ProcessStopProvenance.LiveIssuer, Path = ProcessStopPath.ProcessTree };

    private static async Task VerifyAsync(Card1619NativeExitFixture native, SessionRunnerExitedEvent evt,
        string expected, SessionTerminationSource source = SessionTerminationSource.Unknown)
    {
        await using var s = await Card1619ConsumerFixture.Scenario.CreateAsync();
        var task = await s.AddTaskAsync(AgentTaskStatus.Working, SessionStatus.Running,
            nativeId: native.Id, nativeGeneration: native.Generation, terminationSource: source);
        var h = s.Harness(native.Id);
        (await s.Runtime.ObserveExitAsync(evt, default)).ShouldBe(SessionExitDisposition.Applied);
        await using (var db = s.CreateContext())
        {
            var row = await db.AgentSessions.SingleAsync(x => x.Id == native.Id);
            row.ExitSignal.ShouldBe(evt.ExitSignal);
            if (source != SessionTerminationSource.Unknown) row.TerminationSource.ShouldBe(source);
            if (evt.ExitStopIntent is not null) row.ExitStopIntentJson.ShouldNotBeNull();
        }
        var attention = (await s.AttentionItemsAsync()).Single(x => x.Kind == AttentionKind.DeadSession);
        attention.Evidence.ShouldContain(expected);
        await s.PastGraceAsync(h);
        var failed = await s.ReadTaskAsync(task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain(expected);
        if (!expected.StartsWith("stopped by Antiphon"))
        { attention.Evidence.ShouldNotContain("stopped by Antiphon"); failed.FailureReason.ShouldNotContain("stopped by Antiphon"); }
    }
}
