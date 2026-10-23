using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1518 S1 red witnesses. S2-S4 supply behavior; do not skip these tests.</summary>
[Category("Integration")]
public partial class PermissionWaitTests
{
    [Test]
    public async Task C1518_CapturedDialogNeedsDecisionBeforeStall()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        await f.AssertIncidentInputsAsync(ct);

        var tick = await f.TickAsync(ct);
        tick.SweepFailures.ShouldBe(0, "fixture: no sweep failure can stand in for the missing diagnostic");
        await f.AssertCustodyAsync(AgentTaskStatus.Working, ct);
        (await f.TaskAsync(ct)).CompletionNoteQueuedAt.ShouldBeNull();
        await using var db = f.Db();
        var warnings = await db.AgentTaskEvents.Where(e => e.AgentTaskId == f.TaskId
            && e.Type == AgentTaskEventType.Warning).ToListAsync(ct);
        // PC-1: removing the early pass must fail HERE, before either the 30m stall or 90m deadline.
        warnings.ShouldHaveSingleItem("V-1: the first eligible tick must persist one provider-input decision Warning");
        warnings[0].Detail.ShouldNotBeNull();
        warnings[0].Detail.ShouldContain("ProviderInputWait");
        warnings[0].Detail.ShouldNotContain(PermissionWaitFixture.CapturedRefusal);
        var notes = await db.AgentTaskLandNotifications.Where(n => n.TaskId == f.TaskId).ToListAsync(ct);
        var note = notes.ShouldHaveSingleItem("V-1: the opening Warning must own one durable caller note");
        // String pins compile before S3 appends the enum; they still exercise real persisted values.
        note.Kind.ToString().ShouldBe("ProviderInputWait");
        note.SourceEventId.ShouldBe(warnings[0].Id);
        note.RequestId.ShouldBeNull();
        note.ParentSessionId.ShouldBe(f.ParentSessionId);
        note.ReplyTo.ShouldBe(AgentTaskReplyTo.Session);
        var shortId = DelegationReportFormatter.Short(f.TaskId);
        note.Body.ShouldStartWith($"[antiphon-permission-wait:{shortId}] Decision needed for task {shortId}: Claude permission dialog observed at ");
        note.Body.ShouldContain($" in session {f.SessionId}.");
        note.Body.ShouldContain("The task remains Working. The outstanding ToolCall does not prove that execution started. Pending refinements: 1; receipt: not confirmed.");
        note.Body.ShouldContain("Inspect the current session and choose an authorized answer or cancellation. Antiphon has sent no approval or cancellation key. Refine may remain queued while the dialog is open; only a complete UserPrompt confirms delivery.");
        note.Body.ShouldContain("The existing 90-minute local-tool deadline remains in force. Check current task and session state before acting on this note.");
        note.Body.ShouldNotContain(PermissionWaitFixture.CapturedRefusal);

        f.Clock.Advance(TimeSpan.FromSeconds(60));
        (await f.TickAsync(ct)).SweepFailures.ShouldBe(0);
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == f.TaskId
            && e.Type == AgentTaskEventType.Warning, ct)).ShouldBe(1, "same episode at the next due scan");
        (await db.AgentTaskLandNotifications.CountAsync(n => n.TaskId == f.TaskId, ct)).ShouldBe(1);
        await f.AssertCustodyAsync(AgentTaskStatus.Working, ct);
    }

    [Test]
    public async Task C1518_NinetyMinuteBoundaryKeepsCustody()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var f = await PermissionWaitFixture.CreateAsync(ct);
        await f.AssertIncidentInputsAsync(ct);
        // Exercise the early producer too; its absence must not mask the deadline diagnosis red.
        (await f.TickAsync(ct)).SweepFailures.ShouldBe(0);
        (await f.TaskAsync(ct)).CompletionNoteQueuedAt.ShouldBeNull("an early note is not completion");
        f.Clock.AdvanceTo(new DateTimeOffset(f.DispatchedAt).AddMinutes(90).AddSeconds(-1));
        (await f.DeadlineAsync(ct)).ShouldBe(0, "V-4/89:59: the original budget has not elapsed");
        await f.AssertCustodyAsync(AgentTaskStatus.Working, ct);

        f.Clock.Advance(TimeSpan.FromSeconds(1));
        (await f.DeadlineAsync(ct)).ShouldBe(1, "V-4/90:00: the dialog must not pause the local-tool clock");
        await f.AssertCustodyAsync(AgentTaskStatus.Failed, ct);
        var failed = await f.TaskAsync(ct);
        await using var db = f.Db();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == f.TaskId
            && e.Type == AgentTaskEventType.Failed, ct)).ShouldBe(1);
        var callerNotes = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == f.ParentSessionId
            && m.SourceTaskId == f.TaskId).ToListAsync(ct);
        callerNotes.ShouldNotBeEmpty("the early diagnostic must not suppress the existing failure notification");
        callerNotes.ShouldContain(n => n.Body.Contains("Failed"));

        f.Clock.Advance(TimeSpan.FromSeconds(1));
        (await f.DeadlineAsync(ct)).ShouldBe(0, "a terminal task is not failed again at 90:01");
        await f.AssertCustodyAsync(AgentTaskStatus.Failed, ct);

        // PC-6 and PC-7 remain independent: deadline/custody assertions precede this diagnosis pin.
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason.ShouldContain("Mid-turn with a current Claude permission dialog.",
            customMessage: "V-4: a permission-blocked call must not be diagnosed as confirmed local execution");
        failed.FailureReason.ShouldContain("against its 90-minute budget");
        failed.FailureReason.ShouldContain("Tool execution is not confirmed.");
        failed.FailureReason.ShouldContain("The session was not killed; its runner seat may still be occupied.");
        failed.FailureReason.ShouldContain("New Refine calls on this task are refused.");
        failed.FailureReason.ShouldContain($"Preserve the worktree and pending queue, then inspect session {f.SessionId} and its runner seat before choosing recovery.");
        callerNotes.ShouldContain(n => n.Body.Contains("current Claude permission dialog"));
        // Fresh final observation supersedes an earlier affirmative cache, without pausing time.
        foreach (var state in new Antiphon.SessionRunner.Contracts.ProviderDialogState?[] { null, Antiphon.SessionRunner.Contracts.ProviderDialogState.Clear, Antiphon.SessionRunner.Contracts.ProviderDialogState.Unknown })
        {
            await using var changed = await PermissionWaitFixture.CreateAsync(ct);
            var prior = (await changed.Reader.ReadAsync(changed.TaskId, true, ct))!;
            changed.Reader.ObserveOverride = (_, _) => Task.FromResult(state is null ? null : prior.Observation with { State = state.Value });
            changed.Clock.AdvanceTo(new DateTimeOffset(changed.DispatchedAt).AddMinutes(90).AddSeconds(1));
            (await changed.DeadlineAsync(ct)).ShouldBe(1, "unknown/clear never extends a genuine execution deadline");
            var reason = (await changed.TaskAsync(ct)).FailureReason!;
            reason.ShouldContain("waiting on a local tool");
            reason.ShouldNotContain("current Claude permission dialog");
            if (state != Antiphon.SessionRunner.Contracts.ProviderDialogState.Clear) reason.ShouldContain("observation unavailable");
            await changed.AssertCustodyAsync(AgentTaskStatus.Failed, ct);
        }
    }
}
