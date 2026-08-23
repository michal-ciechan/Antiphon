using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The reply path: a delegate's finished turn becomes the task's result and a note for its parent.
///
/// The load-bearing behaviour here is the MARKER gate. Correlation matches the
/// <c>[antiphon-task:id]</c> marker carried in the brief, never prompt text — so a human typing in
/// a delegate's terminal can never be mistaken for that task finishing.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskReplyIntegrationTests
{
    [Test]
    public async Task a_marked_turn_settles_the_task_and_stores_the_report_verbatim()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        const string report = "Added Fizz(int) in Numbers.cs (+11 lines). 142 passed, 0 failed.";

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.", report);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(report, "the report is the deliverable — it is stored untouched");
        settled.CompletedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task an_unmarked_turn_leaves_the_task_running()
    {
        // A human typed in the delegate's terminal. Without the marker gate this would end the task
        // with the wrong text and send that to the caller as the result.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(sessionId, "what files are in this directory?", "Here's the listing: ...");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched, "a human's turn is not the delegate's report");
        stored.Result.ShouldBeNull();
    }

    [Test]
    public async Task another_tasks_marker_does_not_settle_this_task()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(Guid.NewGuid()) + "\n\nA different task entirely.",
            "Did the other thing.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    /// <summary>
    /// The 2026-08-11 live miss, replayed end to end: three delegates ran, did real work and
    /// reported, and their tasks sat Dispatched overnight because the brief reached them with its
    /// HEAD missing — and the correlation marker was only ever at the head.
    ///
    /// Aligning what was queued against what each delegate recorded put the cut at byte 1024n-2
    /// with only the FINAL chunk surviving: a 1 420-character brief arrived as its last 380
    /// characters. The prior investigation had concluded head and tail always survive; these four
    /// deliveries are the counter-example, and the tail is the only fragment that survived all of
    /// them. Hence a marker at BOTH ends. Red before that change: no marker anywhere in the
    /// prompt, so every turn-end failed the gate and nothing ever settled the task.
    /// </summary>
    [Test]
    public async Task a_brief_that_lost_its_head_in_the_pty_still_settles_the_task()
    {
        using var workspace = new TempWorkspace();
        // A goal the size of the ones that actually stranded: their briefs came to 1 384-2 338
        // characters, which is one chunk boundary in and squarely inside the ceiling — this is
        // ordinary delegation, not an oversized body anything already guards against.
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, configure: t =>
            t.Goal = "Delegated tasks never settle. "
                + string.Join(" ", Enumerable.Range(0, 110).Select(i => $"context{i:D3}")));

        var brief = DelegationReportFormatter.BuildBrief(task, new DelegationSettings());
        brief.Length.ShouldBeGreaterThan(1024, "the loss only happens to a brief past the first chunk");
        brief.Length.ShouldBeLessThan(
            new DelegationSettings().PtyInlineSafeChars,
            "and this is a brief every existing size guard considers safe");
        var arrived = DropEverythingBeforeTheFinalPtyChunk(brief);

        arrived.ShouldNotContain(
            "role=", customMessage: "the head — metadata line and its marker — must really be gone");
        arrived.Length.ShouldBeLessThan(brief.Length);

        await SeedTurnAsync(sessionId, arrived, "Fixed it in Numbers.cs. 142 passed, 0 failed.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(
            AgentTaskStatus.Succeeded,
            "a delegate that reported must settle even when only the tail of its brief arrived");
        settled.Result.ShouldBe("Fixed it in Numbers.cs. 142 passed, 0 failed.");
    }

    /// <summary>
    /// The other half of the same miss (CARD-0003): when a turn CANNOT be correlated, that has to
    /// leave a mark. It was logged at Debug under an Information file sink, so the single event
    /// explaining three dead tasks was written precisely nowhere.
    /// </summary>
    [Test]
    public async Task a_report_that_cannot_be_correlated_raises_an_incident()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.AgentId = agentId);

        // A finished-looking turn whose prompt carries no marker at all.
        await SeedTurnAsync(sessionId, "the brief, with its head eaten", "Done — here is the report.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched, "it still must not settle on an unmarked turn");

        var incident = await verify.AgentIncidents
            .SingleAsync(i => i.SessionId == sessionId
                && i.Kind == AgentIncidentKind.DelegateReportUncorrelated);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.Message.ShouldContain(DelegationReportFormatter.Short(task.Id));
    }

    [Test]
    public async Task the_uncorrelated_incident_is_raised_once_not_once_per_turn()
    {
        // A stranded delegate keeps ending turns. One finding repeated on every one of them is
        // noise that buries the first.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.AgentId = agentId);
        var service = CreateService();

        await SeedTurnAsync(sessionId, "headless brief", "First report.");
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);
        await SeedTurnAsync(sessionId, "headless brief", "Second report.");
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentIncidents.CountAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated))
            .ShouldBe(1);
    }

    [Test]
    public async Task two_tasks_on_one_session_each_get_their_own_uncorrelated_incident()
    {
        // CARD-0117 S1: the recorder's once-per-session dedup used to swallow a later task's
        // finding. One incident per task; a different, later task gets its own row.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (first, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.AgentId = agentId);
        var service = CreateService();

        await SeedTurnAsync(sessionId, "headless brief", "First report.");
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using (var db = CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == first.Id);
            row.Status = AgentTaskStatus.Failed;
            row.CompletedAt = DateTime.UtcNow;
            // Stamp the first incident in the past so the later task's DispatchedAt is strictly
            // after it — equal timestamps would let S1's `>= DispatchedAt` window pick it up.
            var incident = await db.AgentIncidents.SingleAsync(
                i => i.SessionId == sessionId
                    && i.Kind == AgentIncidentKind.DelegateReportUncorrelated);
            incident.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        var second = await SeedFollowUpTaskAsync(workspace.Path, sessionId, agentId);
        await SeedTurnAsync(sessionId, "headless brief two", "Second report.");
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var incidents = await verify.AgentIncidents
            .Where(i => i.SessionId == sessionId
                && i.Kind == AgentIncidentKind.DelegateReportUncorrelated)
            .ToListAsync();
        incidents.Count.ShouldBe(2, "each task gets its own incident row");
        incidents.ShouldContain(i => i.Message.Contains(DelegationReportFormatter.Short(first.Id)));
        incidents.ShouldContain(i => i.Message.Contains(DelegationReportFormatter.Short(second.Id)));
    }

    /// <summary>
    /// The measured shape of the 2026-08-11 loss: everything before the last whole 1024-byte chunk
    /// is dropped, cutting at byte 1024n-2. Nudged off a UTF-8 continuation byte so the surviving
    /// fragment is still text — em-dashes are why the character offsets read 986 where the byte
    /// offset was 1022.
    /// </summary>
    private static string DropEverythingBeforeTheFinalPtyChunk(string body)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        const int chunk = 1024;
        if (bytes.Length <= chunk)
            return body;

        var cut = bytes.Length / chunk * chunk - 2;
        while (cut < bytes.Length && (bytes[cut] & 0xC0) == 0x80)
            cut++;
        return System.Text.Encoding.UTF8.GetString(bytes, cut, bytes.Length - cut);
    }

    [Test]
    public async Task a_turn_with_no_assistant_text_yet_leaves_the_task_running()
    {
        // Claude sometimes writes the turn's stop marker BEFORE its reply text. Settling here would
        // record an empty report; the AssistantText's own arrival re-triggers settlement.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), assistantText: null);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
    }

    // ---- CARD-0046: settle on the response that ENDED the turn, not on the record that announced it ----

    /// <summary>
    /// The root defect, verbatim. Claude Code writes ONE API response as several JSONL records — a
    /// signature-only <c>thinking</c> record, then the <c>text</c> record — and stamps EVERY one of
    /// them with the response's <c>stop_reason</c>. The thinking record's text is empty in all 1 936
    /// thinking blocks on this machine, so it normalizes to a BARE TurnEnd and nothing else, and
    /// settlement fired on it while the report was still 0.01-1.2 s from being persisted.
    ///
    /// Six delegates lost their verdicts that way on 2026-08-13/14 (4 573-6 296 characters each) and
    /// their callers received the mid-turn preamble instead. Nothing had stopped early; the reports
    /// are still in TranscriptEntries.
    ///
    /// Closed by IDENTITY — the two records share one <c>message.id</c>, persisted as
    /// <c>ApiCallId</c> — never by timing.
    /// </summary>
    [Test]
    public async Task a_turn_end_whose_own_response_has_not_written_its_text_does_not_settle()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedSplitTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            narration: "I'll start by reading the spec.",
            finalMessage: null);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(
            AgentTaskStatus.Dispatched,
            "the turn-ending response has not written its text yet — its narration is not the report");
        stored.Result.ShouldBeNull();
    }

    [Test]
    public async Task the_final_messages_arrival_settles_the_task_with_the_report()
    {
        // The other half: deferring is only correct because the text record's own arrival
        // re-triggers settlement (AgentSessionRuntime :219 → :350). Nothing else has to happen.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        var service = CreateService();

        var apiCallId = await SeedSplitTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            narration: "I'll start by reading the spec.",
            finalMessage: null);
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await AppendFinalMessageAsync(sessionId, apiCallId, "Verdict: keep as is. 142 passed, 0 failed.");
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldContain(
            "Verdict: keep as is.", customMessage: "the verdict is what the caller was owed");
    }

    [Test]
    public async Task a_turn_end_with_no_api_call_id_settles_as_it_always_did()
    {
        // The explicit regression guard for the legacy/synthetic path: a SessionRestartBoundary, an
        // older row, a fake that emits no message.id. There is no response identity to wait for, so
        // there is nothing to defer on — and the other 25 tests in this file take the same route.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.", turnEndApiCallId: null);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe("Done.");
    }

    /// <summary>
    /// The backstop the deferral must have: a response CAN end a turn having written no text at all
    /// — 1 in 180 in the measured corpus (opus session cefed08a, a lone thinking record with
    /// <c>end_turn</c> followed 106 ms later by "API Error: Connection lost mid-response"). Without
    /// the grace that task sits Dispatched until the 10-minute delivery watchdog kills it.
    /// </summary>
    [Test]
    public async Task a_response_that_never_writes_text_settles_after_the_grace_window()
    {
        using var workspace = new TempWorkspace();
        var parentSessionId = await SeedSessionAsync(workspace.Path);
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, parentSessionId, configure: t => t.AgentId = agentId);
        var settings = new DelegationSettings { ReplyInlineMaxChars = 20_000, FinalMessageGraceSeconds = 120 };
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await SeedSplitTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            narration: "I'll start by reading the spec.",
            finalMessage: null);

        var service = CreateService(settings: settings, timeProvider: clock);
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);
        await using (var midGrace = CreateContext())
        {
            (await midGrace.AgentTasks.SingleAsync(t => t.Id == task.Id))
                .Status.ShouldBe(AgentTaskStatus.Dispatched, "inside the grace it is still waiting");
        }

        clock.Advance(TimeSpan.FromSeconds(121));
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(
            AgentTaskStatus.Succeeded, "past the grace, settling on the preamble beats stranding the task");
        settled.Result.ShouldBe("I'll start by reading the spec.");

        // Slice 3: Succeeded, but never SILENTLY Succeeded. Three surfaces, because the whole
        // character of CARD-0046 was that every one of them said the task was fine.
        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateFinalMessageMissing);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.Message.ShouldContain(DelegationReportFormatter.Short(task.Id));
        incident.Message.ShouldContain("31 characters", customMessage: "and what the report was built from");

        var warned = await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning)
            .SingleAsync();
        warned.Detail.ShouldContain("never wrote its own text within 120s");
        warned.Detail.ShouldContain("31 characters");

        var note = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSessionId)
            .SingleAsync();
        note.Body.ShouldContain(
            "may be PREAMBLE", customMessage: "the CALLER is the one who must not act on it as a verdict");
        note.Body.ShouldContain("I'll start by reading the spec.", customMessage: "the text is still forwarded");
        note.Body.IndexOf("may be PREAMBLE", StringComparison.Ordinal)
            .ShouldBeLessThan(
                note.Body.IndexOf("I'll start by reading the spec.", StringComparison.Ordinal),
                "a caveat read after the report is a caveat read too late");
    }

    /// <summary>
    /// The other half of the backstop, and the one the spec argues hardest about: a turn that
    /// produced NO text at all must FAIL, not succeed with an empty report and not sit Dispatched.
    /// This is the cefed08a shape — a lone <c>end_turn</c> thinking record followed 106 ms later by
    /// "API Error: Connection lost mid-response". Failing is the correct verdict for it; Succeeded
    /// would tell the caller the work is done and hand it nothing.
    /// </summary>
    [Test]
    public async Task a_turn_with_no_text_at_all_at_grace_fails_the_task()
    {
        using var workspace = new TempWorkspace();
        var parentSessionId = await SeedSessionAsync(workspace.Path);
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, parentSessionId, configure: t => t.AgentId = agentId);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(timeProvider: clock);

        // Prompt, then a TurnEnd carrying a response id and nothing else. No narration anywhere.
        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            assistantText: null, turnEndApiCallId: $"msg_{Guid.NewGuid():N}");

        await service.OnTurnEndAsync(sessionId, CancellationToken.None);
        await using (var midGrace = CreateContext())
        {
            (await midGrace.AgentTasks.SingleAsync(t => t.Id == task.Id))
                .Status.ShouldBe(AgentTaskStatus.Dispatched, "inside the grace it is still waiting");
        }

        clock.Advance(TimeSpan.FromSeconds(121));
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.Result.ShouldBeNull("there was never a report to store");
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason!.ShouldContain("no report at all");
        failed.FailureReason.ShouldContain(
            sessionId.ToString(), customMessage: "the work may be real — name where to read it");
        failed.CompletedAt.ShouldNotBeNull();

        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Failed)).ShouldBe(1);

        // The caller must HEAR about it, not discover it on the board.
        var note = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSessionId)
            .SingleAsync();
        note.Body.ShouldContain("no report at all");

        (await verify.AgentIncidents.CountAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateFinalMessageMissing))
            .ShouldBe(1);

        // The response died; the SESSION did not. A live Shared delegate is as reusable as after any
        // success — and something has to free it, or it leaks Busy forever.
        (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Idle);
    }

    [Test]
    public async Task the_final_message_missing_incident_is_raised_once_per_session()
    {
        // Same reason the uncorrelated one is deduped: a delegate in this state keeps ending turns,
        // and the same finding on every one of them buries the first.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (first, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.AgentId = agentId);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(timeProvider: clock);

        await SeedSplitTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(first.Id),
            narration: "Reading the spec now.", finalMessage: null);
        clock.Advance(TimeSpan.FromSeconds(121));
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        // The warm delegate takes a SECOND task in the same session and does it again.
        var second = await SeedFollowUpTaskAsync(workspace.Path, sessionId, agentId);
        await SeedSplitTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(second.Id),
            narration: "Reading the other spec now.", finalMessage: null);
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == second.Id))
            .Status.ShouldBe(AgentTaskStatus.Succeeded, "the second task still settles");
        (await verify.AgentIncidents.CountAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateFinalMessageMissing))
            .ShouldBe(1);
    }

    /// <summary>
    /// The live shape, replayed from the real JSONL: session 7f9d06a5 (task ff320d72), response
    /// <c>msg_011Ce2Xog1xCJs9P</c>, whose thinking record is stamped 11:05:43 and whose 5 850-char
    /// report is stamped 11:06:01 — both written to the file together at the end of the response.
    /// Settlement fired 0.73 s before the report row was persisted and the caller received 289
    /// characters of "I'll start by…" instead.
    ///
    /// Driven through the three invocations the runtime really makes, in arrival order: the bare
    /// TurnEnd, then the text record, then the text record's own TurnEnd sibling. Exactly one
    /// settlement may come out of it.
    /// </summary>
    [Test]
    public async Task the_live_split_response_tail_settles_once_with_the_report()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        var (apiCallId, finalMessage) = LiveSplitResponseFixture();
        finalMessage.Length.ShouldBe(5_850, "the report that was discarded, verbatim");
        var service = CreateService();

        // seq 97: the thinking record's bare TurnEnd. Nothing may settle here.
        await SeedLiveSplitTailAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), apiCallId);
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);
        await using (var midTurn = CreateContext())
        {
            (await midTurn.AgentTasks.SingleAsync(t => t.Id == task.Id))
                .Status.ShouldBe(AgentTaskStatus.Dispatched);
        }

        // seq 98-99: the text record (re-triggers settlement) and its duplicate TurnEnd sibling.
        await AppendFinalMessageAsync(sessionId, apiCallId, finalMessage);
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(
            finalMessage,
            "the report IS the final message — the three 'I'll start by…' sentences ff320d72's caller "
            + "actually received are narration, and they are not part of it");
        var completed = await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Completed)
            .ToListAsync();
        completed.Count.ShouldBe(1, "the duplicate TurnEnd sibling must not settle the task a second time");
        completed[0].Detail.ShouldContain(
            "of mid-turn narration not included",
            customMessage: "what the report left out is on the record, never silent");
    }

    /// <summary>
    /// The two real JSONL lines of <c>msg_011Ce2Xog1xCJs9P</c>, through the production normalizer:
    /// the signature-only thinking record yields a bare TurnEnd, the text record yields the report.
    /// Reading them rather than restating them is what keeps this test honest about the shape.
    /// </summary>
    private static (string ApiCallId, string FinalMessage) LiveSplitResponseFixture()
    {
        var lines = File.ReadAllLines(
                Path.Combine(AppContext.BaseDirectory, "Agents", "Fixtures", "split-final-response.jsonl"))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        var parts = lines.SelectMany(Antiphon.SessionRunner.TranscriptNormalizer.Normalize).ToList();

        var bare = parts.Where(p => p.Kind == TranscriptKinds.TurnEnd).ToList();
        bare.Count.ShouldBe(2, "both records of one response carry its stop_reason");
        var text = parts.Single(p => p.Kind == TranscriptKinds.AssistantText);
        bare.ShouldAllBe(p => p.ApiCallId == text.ApiCallId);
        return (text.ApiCallId!, text.Text!);
    }

    /// <summary>seq 97's shape: the marked brief, mid-turn narration, then the bare TurnEnd.</summary>
    private static async Task SeedLiveSplitTailAsync(Guid sessionId, string marker, string apiCallId)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        db.TranscriptEntries.Add(NewEntry(
            sessionId, ++seq, TranscriptKinds.UserPrompt, marker + "\n\nPlan CARD-0046."));
        // ff320d72's stored Result was literally these three sentences joined.
        foreach (var narration in new[]
        {
            "I'll start by reading the card.",
            "I'll now measure the record shapes.",
            "I'll write the plan.",
        })
        {
            var chatter = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, narration);
            chatter.ApiCallId = $"msg_{Guid.NewGuid():N}";
            db.TranscriptEntries.Add(chatter);
        }

        var bareEnd = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        bareEnd.StopReason = "end_turn";
        bareEnd.ApiCallId = apiCallId;
        db.TranscriptEntries.Add(bareEnd);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The report is the FINAL MESSAGE, not a join of everything the delegate said while working
    /// (CARD-0046 slice 2). Joining the whole turn is what put "I'll start by reading the spec." at
    /// the top of six callers' reports — and it is also what the head+tail excerpt excerpted, and
    /// what <c>LooksLikeAQuestion</c> read the last line of.
    /// </summary>
    [Test]
    public async Task the_report_is_the_turn_ending_responses_own_text()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        const string finalMessage = "Verdict: the ceiling is correct. 142 passed, 0 failed.";

        await SeedSplitTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            narration: "I'll start by reading the spec.",
            finalMessage: finalMessage);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(finalMessage);
        settled.Result.ShouldNotContain(
            "I'll start by", customMessage: "narration under a DIFFERENT api call is not the report");

        // The discarded narration is named, so a delegate that front-loads its findings is visible
        // rather than silently trimmed.
        var completed = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Completed);
        completed.Detail.ShouldBe(
            $"Delegate reported {finalMessage.Length:N0} characters (final message; "
            + "31 characters of mid-turn narration not included).");
    }

    /// <summary>
    /// One API response can carry several text blocks, and they are all the final message. Order is
    /// the sequence they were written in — a report reassembled backwards is a corrupted report.
    /// </summary>
    [Test]
    public async Task a_response_split_over_several_text_blocks_is_joined_in_order()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        var apiCallId = await SeedSplitTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            narration: "I'll start by reading the spec.",
            finalMessage: null);
        await AppendFinalMessageAsync(sessionId, apiCallId, "Outcome: shipped.");
        await AppendFinalMessageAsync(sessionId, apiCallId, "Files: Numbers.cs (+11).");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Result.ShouldBe("Outcome: shipped.\n\nFiles: Numbers.cs (+11).");
    }

    [Test]
    public async Task a_delegate_that_asks_a_question_comes_back_blocked_not_finished()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(task.Id),
            "Added Fizz(int).\n\nBuzz throws on negatives — should Fizz match that?");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Blocked, "it needs an answer, not a retry");
    }

    [Test]
    public async Task a_report_under_the_ceiling_is_not_spilled_to_a_file()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), new string('x', 18_000));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.ResultFilePath.ShouldBeNull();
        settled.Result!.Length.ShouldBe(18_000);
        Directory.Exists(Path.Combine(workspace.Path, ".antiphon")).ShouldBeFalse();
    }

    [Test]
    public async Task an_oversized_report_is_backstopped_to_a_file_by_the_server()
    {
        // The delegate was told to spill and didn't. The server writes the file itself, so the
        // excerpt the caller receives has somewhere real to point — and the full text survives.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        var huge = new string('y', 25_000);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), huge);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.ResultFilePath.ShouldNotBeNull();
        File.Exists(settled.ResultFilePath).ShouldBeTrue();
        (await File.ReadAllTextAsync(settled.ResultFilePath!)).Length.ShouldBe(25_000);
        settled.Result!.Length.ShouldBe(25_000, "the task row always keeps the untouched original");
    }

    [Test]
    public async Task a_spill_file_the_delegate_wrote_itself_is_used_as_is()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        var spillPath = Path.Combine(
            workspace.Path, ".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(spillPath)!);
        await File.WriteAllTextAsync(spillPath, "THE DELEGATE'S OWN FULL DETAIL");

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), new string('z', 25_000));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.ResultFilePath.ShouldBe(spillPath);
        (await File.ReadAllTextAsync(spillPath))
            .ShouldBe("THE DELEGATE'S OWN FULL DETAIL", "the delegate's own file must not be overwritten");
    }

    [Test]
    public async Task the_completion_note_is_delivered_into_the_parents_session()
    {
        // The whole point: the caller learns the outcome without reading a transcript.
        using var workspace = new TempWorkspace();
        var parentSessionId = await SeedSessionAsync(workspace.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, parentSessionId);

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Rewrote the section. 34 lines changed.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var queued = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSessionId)
            .ToListAsync();

        queued.Count.ShouldBe(1);
        queued[0].Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued[0].ConversationKey.ShouldBe($"task:{task.RootTaskId:N}", "same-root results coalesce");
        queued[0].Body.ShouldContain("Rewrote the section. 34 lines changed.");
        queued[0].Body.ShouldContain(DelegationReportFormatter.Short(task.Id));
        queued[0].Body.Contains('\r').ShouldBeFalse("a CR mid-body would submit the fragment before it");
    }

    /// <summary>
    /// The 2026-08-10 live miss, at its exact size, through the SHIPPED settings.
    ///
    /// Task 0b0f558c stored a complete 5 368-character report and an EMPTY ResultFilePath, and its
    /// caller received a head+tail splice joined mid-word. Nothing had excerpted it — with
    /// ReplyInlineMaxChars at 20 000, FitReport returned the report untouched and
    /// ResolveSpillFileAsync returned null before doing anything, so a 5.4 KB body went straight to
    /// a pty that drops whole 1024-byte chunks out of the middle of anything much over 4 300
    /// characters. The ceiling now sits under that cliff, so this size spills and the caller gets a
    /// small, clearly-marked excerpt that names where the rest lives.
    /// </summary>
    [Test]
    public async Task a_five_kilobyte_report_spills_and_the_caller_gets_a_marked_excerpt()
    {
        using var workspace = new TempWorkspace();
        var parentSessionId = await SeedSessionAsync(workspace.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, parentSessionId);
        var shipped = new DelegationSettings();

        // The live-miss report: a recognisable opening and a recognisable conclusion.
        var report = "Both commits confirmed on origin/master. "
            + string.Join(" ", Enumerable.Range(0, 700).Select(i => $"detail{i:D4}"))
            + " Final state: git status clean, HEAD == origin/master == a667cbcc.";
        report.Length.ShouldBeGreaterThan(shipped.PtyInlineSafeChars, "this must be a body the pty could mangle");

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), report);
        await CreateService(settings: shipped).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);

        settled.Result.ShouldBe(report, "the task row always keeps the untouched original");
        settled.ResultFilePath.ShouldNotBeNull("a report this size must have somewhere real to point");
        File.Exists(settled.ResultFilePath).ShouldBeTrue();
        (await File.ReadAllTextAsync(settled.ResultFilePath!)).ShouldBe(report);

        // Scoped to this task's parent session — the fixture's database is shared.
        var note = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSessionId)
            .SingleAsync();

        note.Body.Length.ShouldBeLessThanOrEqualTo(
            shipped.PtyInlineSafeChars,
            "what we actually type must be small enough for the terminal to carry intact");
        note.Body.ShouldContain("EXCERPT", customMessage: "the caller must be told this is not the whole report");
        note.Body.ShouldContain(settled.ResultFilePath!, customMessage: "and where the whole report is");
        note.Body.ShouldContain("Both commits confirmed", customMessage: "the opening survives");
        note.Body.ShouldContain("a667cbcc", customMessage: "and so does the conclusion");
    }

    [Test]
    public async Task a_task_with_no_parent_session_settles_without_delivering_anywhere()
    {
        // The manual entry point: the result lands on the board only.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, parentSessionId: null);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        // Scoped to THIS task — the fixture's database is shared, so a global count would pick up
        // rows other tests legitimately left behind.
        var shortId = DelegationReportFormatter.Short(task.Id);
        (await verify.SessionQueuedMessages.CountAsync(m => m.Body.Contains(shortId))).ShouldBe(0);
    }

    [Test]
    public async Task token_spend_is_rolled_up_onto_the_task()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.", inputTokens: 50_000, outputTokens: 4_000);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.TokensIn.ShouldBe(50_000);
        settled.TokensOut.ShouldBe(4_000);
        settled.CostUsd.ShouldBeGreaterThan(0m, "the per-root ceiling can only work if spend is recorded");
        settled.CostPricingVersion.ShouldBe(
            DelegationCost.PricingVersion, "a freshly priced row must not read as a legacy estimate");
    }

    [Test]
    public async Task the_three_input_counters_are_kept_apart_and_priced_apart()
    {
        // CARD-0023: collapsing them and applying the input rate to the total prices a cache READ
        // — about a tenth of base input — as fresh input. Claude Code re-reads its whole cached
        // prefix every turn, so that term dominates and the run reads ~10x its real cost.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.",
            inputTokens: 1_000, outputTokens: 2_000,
            cacheReadTokens: 5_000_000, cacheCreationTokens: 100_000);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.TokensIn.ShouldBe(1_000, "TokensIn is UNCACHED input — the cache counters have their own columns");
        settled.CacheReadTokens.ShouldBe(5_000_000);
        settled.CacheCreationTokens.ShouldBe(100_000);
        settled.TokensOut.ShouldBe(2_000);

        // Whatever the rates are, the same tokens billed as fresh input must cost far more.
        var spend = new TokenSpend(
            settled.TokensIn, settled.CacheReadTokens, settled.CacheCreationTokens, settled.TokensOut);
        var collapsed = new TokenSpend(spend.TotalInputTokens, 0, 0, spend.OutputTokens);
        var pricing = new DelegationPricingSettings();
        var asFreshInput = DelegationCost.Estimate(pricing, settled.ModelLevel, collapsed, DateTime.UtcNow);

        settled.CostUsd.ShouldBeLessThan(asFreshInput / 5m, "cache reads must not be priced as fresh input");
        settled.CostUsd.ShouldBe(
            DelegationCost.Estimate(pricing, settled.ModelLevel, spend, settled.CompletedAt!.Value));
    }

    /// <summary>
    /// CARD-0084 S5, end to end through the settle path: the kind on the TASK row, not the tier
    /// alone, picks the rate table. Before this, a Grok delegate was billed at whatever Claude
    /// model shares its rung — Frontier means fable ($10/$50) and grok-4.6 ($2/$6) alike — so the
    /// per-root ceiling saw ~3.8x the real spend and would throttle a run on money never spent.
    ///
    /// The counters are the Grok-shaped ones: <c>GrokTranscriptNormalizer</c> reads
    /// <c>turn_completed.usage</c>'s inputTokens / cachedReadTokens / cacheCreationTokens /
    /// outputTokens into the same four columns Claude's usage lands in, so the rollup needs no
    /// per-kind work — only the price does.
    /// </summary>
    [Test]
    public async Task a_grok_delegates_spend_is_priced_at_grok_rates_not_at_the_claude_rung_it_shares()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, configure: t =>
        {
            t.AgentKind = AgentKind.Grok;
            t.ModelLevel = AgentModelLevel.Frontier;
        });

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.",
            inputTokens: 18_400, outputTokens: 12_300,
            cacheReadTokens: 742_000, cacheCreationTokens: 61_500);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.CostPricingVersion.ShouldBe(
            DelegationCost.PricingVersion, "the costing MODEL did not change — only the rate lookup widened");

        var spend = new TokenSpend(
            settled.TokensIn, settled.CacheReadTokens, settled.CacheCreationTokens, settled.TokensOut);
        var pricing = new DelegationPricingSettings();

        // xAI published list (docs.x.ai/docs/models, 2026-08-18), grok-4.6 sub-200k:
        // 18,400 x $2 + 742,000 x $0.50 + 61,500 x $2 + 12,300 x $6, per million.
        settled.CostUsd.ShouldBe(0.604600m);
        settled.CostUsd.ShouldBe(
            DelegationCost.Estimate(pricing, settled.ModelLevel, spend, settled.CompletedAt!.Value, AgentKind.Grok));

        var asClaude = DelegationCost.Estimate(pricing, settled.ModelLevel, spend, settled.CompletedAt!.Value);
        asClaude.ShouldBe(2.309750m);
        settled.CostUsd.ShouldBeLessThan(
            asClaude / 3m, "pricing Grok on the fable rung is the overstatement this slice removes");
    }

    /// <summary>
    /// The other half of the contract: a Claude task settles at exactly the figure it did before
    /// the kind overlay existed. <see cref="AgentKind.ClaudeCode"/> is the column's default, so
    /// this is also what every stored row written before S2 prices at.
    /// </summary>
    [Test]
    public async Task a_claude_delegates_spend_is_unmoved_by_the_kind_overlay()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.",
            inputTokens: 18_400, outputTokens: 12_300,
            cacheReadTokens: 742_000, cacheCreationTokens: 61_500);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.AgentKind.ShouldBe(AgentKind.ClaudeCode, "the seeded default — nothing opted this task in");

        var spend = new TokenSpend(
            settled.TokensIn, settled.CacheReadTokens, settled.CacheCreationTokens, settled.TokensOut);
        // The kind-free overload IS the pre-CARD-0084 call. Same task, same number.
        settled.CostUsd.ShouldBe(DelegationCost.Estimate(
            new DelegationPricingSettings(), settled.ModelLevel, spend, settled.CompletedAt!.Value));
    }

    [Test]
    public async Task usage_repeated_across_one_api_calls_entries_is_counted_once()
    {
        // Every JSONL line of one API call repeats that call's usage verbatim. Summing per entry
        // multiplied the measured session by ~1.8x on top of the mispricing — and ~3x across the
        // whole dev database.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.",
            inputTokens: 700, outputTokens: 400,
            cacheReadTokens: 90_000, cacheCreationTokens: 3_000,
            entriesPerApiCall: 4);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.TokensIn.ShouldBe(700, "four entries, one API call — the usage is the call's, not each line's");
        settled.CacheReadTokens.ShouldBe(90_000);
        settled.CacheCreationTokens.ShouldBe(3_000);
        settled.TokensOut.ShouldBe(400);
    }

    [Test]
    public async Task spend_from_before_the_task_was_dispatched_is_not_charged_to_it()
    {
        // A warm pool delegate's session outlives its first task, and a session can adopt another's
        // transcript entirely (CARD-0006) — so a whole-session sum bills one task for another's
        // tokens, twice over against the per-root ceiling.
        using var workspace = new TempWorkspace();
        // Dispatched ten minutes ago, so both turns sit unambiguously on their side of the bound
        // (settle's upper bound is "now").
        var dispatched = DateTime.UtcNow.AddMinutes(-10);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);

        await SeedTurnAsync(
            sessionId, "an earlier task in this same session", "That one is finished.",
            inputTokens: 900_000, outputTokens: 40_000, cacheReadTokens: 8_000_000,
            timestamp: dispatched.AddMinutes(-5));
        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.",
            inputTokens: 300, outputTokens: 120, cacheReadTokens: 45_000,
            timestamp: dispatched.AddMinutes(1));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.TokensIn.ShouldBe(300, "only this task's own window counts");
        settled.CacheReadTokens.ShouldBe(45_000);
        settled.TokensOut.ShouldBe(120);
    }

    // ---- the walk-back is bounded at dispatch, and compaction's records are not prompts ---------

    /// <summary>
    /// The 2026-08-14 live miss, replayed at its measured record order (session c8d07c43, task
    /// bcc982b7; fbcd6af2 / 861eaefb is the same shape 14 minutes later).
    ///
    /// A brief routed to a still-warm delegate is preceded by a focused <c>/compact</c>, so for the
    /// minutes that compaction takes the newest TurnEnd on the session is still the PREVIOUS task's.
    /// Every AssistantText re-runs extraction (AgentSessionRuntime :219 → :350), so that stale turn
    /// — a real, complete report, for a different task — was read as an unattributable report for
    /// THIS one. The incident it raised is what <c>FailNeverStartedAsync</c> acts on, and it killed
    /// the task at the 10-minute mark while the delegate was still working; it later left finished
    /// work uncommitted. The brief was never mangled: it is sitting intact at seq 137.
    /// </summary>
    [Test]
    public async Task a_turn_that_ended_before_this_task_was_dispatched_is_not_a_report_for_it()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var dispatched = DateTime.UtcNow.AddMinutes(-10);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => { t.AgentId = agentId; t.DispatchedAt = dispatched; });

        await SeedWarmReuseAfterCompactionAsync(sessionId, dispatched, task.Id);

        // The delegate is working: its narration, not a turn end. This is the call that fired.
        await SeedEntryAsync(
            sessionId, TranscriptKinds.AssistantText, "Now implementing slice 2.",
            dispatched.AddMinutes(4));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched, "the delegate is still working");
        stored.Result.ShouldBeNull("the previous task's report is not this task's");

        (await verify.AgentIncidents.AnyAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated))
            .ShouldBeFalse(
                "a turn that ended before this task was dispatched is not an unattributable report "
                + "— and that incident is what the 10-minute watchdog kills the task on");
    }

    /// <summary>
    /// The other half: once the delegate really does end a turn, the walk-back has to reach past
    /// everything the compaction wrote and find the brief. Four USER records sit between them that
    /// nobody typed as a prompt — the raw echo of the typed command line, the
    /// <c>&lt;command-name&gt;</c> wrapper, the <c>&lt;local-command-stdout&gt;</c> result and the
    /// synthetic continuation prompt (CARD-0041).
    /// </summary>
    [Test]
    public async Task a_report_after_the_reuse_compaction_settles_against_the_brief()
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-10);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);
        const string report = "Shipped and pushed — 3 commits. 187 passed, 0 failed.";

        await SeedWarmReuseAfterCompactionAsync(sessionId, dispatched, task.Id);
        await SeedResponseAsync(sessionId, report, dispatched.AddMinutes(5));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(report);
    }

    /// <summary>
    /// The same records, but written by a delegate that compacted itself MID-task — after its
    /// brief, to make room to finish. Nothing bounds that one out, so the walk-back must skip the
    /// records rather than stop at them, or the report lands on the raw <c>/compact</c> line and
    /// the task dies unattributable with the brief three records further back.
    /// </summary>
    [Test]
    public async Task a_compaction_the_delegate_runs_mid_task_still_settles_against_the_brief()
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-20);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);
        const string report = "Verdict: keep as is. The guard is sound.";

        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nReview the move guard.",
            dispatched.AddMinutes(1));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.AssistantText, "Reading the spec.", dispatched.AddMinutes(2));
        await SeedCompactionRecordsAsync(
            sessionId, dispatched.AddMinutes(5), "/compact Keep only the review context.");
        await SeedResponseAsync(sessionId, report, dispatched.AddMinutes(8));

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(report, "the report is the turn-ending response, not the /compact line");
    }

    /// <summary>
    /// The gate the bound must not weaken: a human typing in the delegate's terminal AFTER dispatch
    /// is still an unattributable report, and still has to be loud (CARD-0003).
    /// </summary>
    [Test]
    public async Task an_unmarked_turn_after_dispatch_is_still_an_uncorrelated_report()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var dispatched = DateTime.UtcNow.AddMinutes(-10);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => { t.AgentId = agentId; t.DispatchedAt = dispatched; });

        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt, "the brief, with its head eaten",
            dispatched.AddMinutes(1));
        await SeedResponseAsync(sessionId, "Done — here is the report.", dispatched.AddMinutes(2));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await verify.AgentIncidents.AnyAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated))
            .ShouldBeTrue();
    }

    // ---- CARD-0135: a queued brief is a turn prompt, for settlement too ----------------------

    [Test]
    public async Task a_turn_opened_by_a_queued_brief_settles_the_task()
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-10);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);
        const string report = "Added Fizz(int) in Numbers.cs (+11 lines). 142 passed, 0 failed.";

        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            dispatched.AddMinutes(1));
        await SeedResponseAsync(sessionId, report, dispatched.AddMinutes(2));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(report);
    }

    [Test]
    public async Task a_queued_prompt_after_the_brief_caps_the_report_window()
    {
        // CARD-0068 discipline across both kinds: the nextPrompt cap is computed over the same
        // span the walk-back reads. Without QueuedUserPrompt in that span the later assistant
        // text (same batch, after the queued row) would be attributed to the brief.
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-10);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);
        const string report = "The report that belongs to this brief.";
        const string later = "Later-turn text that must not be attributed to the brief.";

        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            dispatched.AddMinutes(1));
        await SeedEntryAsync(sessionId, TranscriptKinds.AssistantText, report, dispatched.AddMinutes(2));
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, dispatched.AddMinutes(2));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            "a completion note that queued while the brief's turn was ending",
            dispatched.AddMinutes(3));
        await SeedEntryAsync(sessionId, TranscriptKinds.AssistantText, later, dispatched.AddMinutes(3));

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(report);
        settled.Result.ShouldNotContain(later);
    }

    [Test]
    public async Task a_queued_prompt_without_the_marker_is_an_uncorrelated_report_not_a_settle_on_the_brief()
    {
        // Verdict §4 / D8's second direction: today the walk-back skips the queued row and
        // settles the task on a turn that answered something else (a completion note, a human
        // message). Widening lands on the queued row, the marker gate fails, and the turn is
        // an honest UncorrelatedReport.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var dispatched = DateTime.UtcNow.AddMinutes(-10);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => { t.AgentId = agentId; t.DispatchedAt = dispatched; });

        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            dispatched.AddMinutes(1));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            "a completion note from a child, no task marker",
            dispatched.AddMinutes(2));
        await SeedResponseAsync(sessionId, "Someone else's answer.", dispatched.AddMinutes(3));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched, "must not settle on a turn that answered the note");
        (await verify.AgentIncidents.AnyAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated))
            .ShouldBeTrue();
    }

    [Test]
    public async Task a_task_notification_is_still_a_notification_not_a_queued_turn_prompt()
    {
        // Guards CARD-0046 slice 4: a <task-notification> is a typed USER record, never a
        // queued_command attachment, and span.Notifications must keep seeing it after the
        // QueuedUserPrompt widening.
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-10);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);
        var at = dispatched.AddMinutes(1);

        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.", at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            $"{TranscriptKinds.TaskNotificationPrefix}\n<summary>done</summary>\n</task-notification>",
            at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt, "a queued completion note", at);

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, sessionId, dispatched, CancellationToken.None);
        span.Notifications.ShouldHaveSingleItem().Text.ShouldContain("<task-notification>");
        span.TurnPrompts.Count.ShouldBe(2);
        span.TurnPrompts.ShouldNotContain(p => p.Text != null && p.Text.Contains("<task-notification>"));
        span.TurnPrompts.ShouldContain(p => p.Kind == TranscriptKinds.QueuedUserPrompt);
        span.TurnPrompts.ShouldContain(p => p.Kind == TranscriptKinds.UserPrompt);
    }

    // ---- a turn that launched background subagents is not finished (CARD-0046 slice 4) ----------

    /// <summary>
    /// The 26421cf2 case (session ac09cffd, seqs 1-35). A Review delegate fanned out four
    /// background <c>Agent</c> calls, each answered instantly with "Async agent launched
    /// successfully", wrote "Four review agents are running in parallel — …" and ended its turn FOR
    /// REAL (seq 18 text, seq 19 TurnEnd, one ApiCallId — this one is not the split shape, so
    /// slice 1 does not help it). Settlement harvested the announcement at 07:44:10, priced the
    /// task and released the delegate; the actual 6 195-character verdict was written at 07:48:06
    /// into a task that no longer existed.
    /// </summary>
    [Test]
    public async Task a_turn_that_launched_background_agents_does_not_settle_on_its_announcement()
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-5);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);

        var launched = await SeedSubagentFanOutAsync(sessionId, dispatched, task.Id);
        launched.Count.ShouldBe(4);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(
            AgentTaskStatus.Dispatched, "the work was handed to subagents, not done");
        stored.Result.ShouldBeNull();
    }

    /// <summary>
    /// The hazard slice 4 creates and must close in the same commit: each notification arrives as a
    /// USER record and ends a turn of its own, so with notifications left in the walk-back the
    /// marker gate fails on every one of them, an incident is raised, and the delivery watchdog
    /// kills the task at ten minutes — the exact death this whole change exists to stop.
    /// </summary>
    [Test]
    public async Task a_task_notification_turn_is_not_an_uncorrelated_report()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var dispatched = DateTime.UtcNow.AddMinutes(-5);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => { t.AgentId = agentId; t.DispatchedAt = dispatched; });

        var launched = await SeedSubagentFanOutAsync(sessionId, dispatched, task.Id);
        await SeedSubagentNotificationAsync(
            sessionId, launched[2], "The allocator review came back clean.", dispatched.AddMinutes(2));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched, "three of four are still running");
        (await verify.AgentIncidents.AnyAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated))
            .ShouldBeFalse("the brief still owns this span — a notification is not a prompt");
    }

    /// <summary>
    /// All four report, the delegate folds them into a verdict, and THAT settles the task — once.
    /// Pairing is by the <c>toolu_…</c> id each notification names, so three of four is
    /// unambiguously "one still running" rather than a count that could be satisfied by anything.
    /// </summary>
    [Test]
    public async Task the_last_subagent_notification_settles_the_task_with_the_verdict()
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-6);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);
        const string verdict = "Verdict: keep as is — no real problem found.";
        var service = CreateService();

        var launched = await SeedSubagentFanOutAsync(sessionId, dispatched, task.Id);
        for (var i = 0; i < 3; i++)
        {
            await SeedSubagentNotificationAsync(
                sessionId, launched[i], $"Reviewer {i} came back clean.",
                dispatched.AddMinutes(2 + i));
            await service.OnTurnEndAsync(sessionId, CancellationToken.None);
        }

        await using (var mid = CreateContext())
        {
            (await mid.AgentTasks.SingleAsync(t => t.Id == task.Id))
                .Status.ShouldBe(AgentTaskStatus.Dispatched, "the fourth has not reported");
        }

        await SeedSubagentNotificationAsync(sessionId, launched[3], verdict, dispatched.AddMinutes(5));
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(verdict);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Completed))
            .ShouldBe(1, "exactly one settlement");
    }

    /// <summary>
    /// A SYNCHRONOUS Agent call returns the subagent's answer as its ToolResult, with no launch
    /// marker on it — the work is already in the turn, so nothing is being waited for.
    /// </summary>
    [Test]
    public async Task a_synchronous_agent_call_settles_normally()
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-5);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);

        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nReview the commit.",
            dispatched.AddMinutes(1));
        var toolUseId = $"toolu_{Guid.NewGuid():N}";
        await SeedToolCallAsync(sessionId, TranscriptKinds.AgentToolName, toolUseId, dispatched.AddMinutes(2));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.ToolResult,
            "The subagent's whole answer, returned inline: nothing to fix.", dispatched.AddMinutes(2),
            toolUseId: toolUseId);
        await SeedResponseAsync(sessionId, "Verdict: keep as is.", dispatched.AddMinutes(3));

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe("Verdict: keep as is.");
    }

    /// <summary>
    /// A background subagent can die without ever notifying, and nothing would come back for the
    /// task. Past the grace it settles on what there is — and says so on all three surfaces, because
    /// "Four review agents are running in parallel" reads exactly like a finished report.
    /// </summary>
    [Test]
    public async Task a_subagent_that_never_reports_settles_after_the_subagent_grace()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var dispatched = DateTime.UtcNow.AddMinutes(-5);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, parentSessionId: await SeedSessionAsync(workspace.Path),
            configure: t => { t.AgentId = agentId; t.DispatchedAt = dispatched; });
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await SeedSubagentFanOutAsync(sessionId, dispatched, task.Id);
        clock.Advance(TimeSpan.FromMinutes(31));
        await CreateService(timeProvider: clock).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(
            AgentTaskStatus.Succeeded, "the delegate did what it could — stranding it helps nobody");
        settled.Result.ShouldContain("Four review agents are running in parallel");

        var warning = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("4 background subagent(s)");

        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == sessionId
                && i.Kind == AgentIncidentKind.DelegateSubagentsNeverReported);
        incident.Severity.ShouldBe(AlertSeverity.Warning);

        var note = await verify.SessionQueuedMessages
            .SingleAsync(m => m.AgentSessionId == task.ParentSessionId);
        note.Body.ShouldContain(
            "may be its ANNOUNCEMENT", customMessage: "the CALLER is the one who has to know");
    }

    [Test]
    public async Task a_session_running_no_task_is_ignored()
    {
        using var workspace = new TempWorkspace();
        var sessionId = await SeedSessionAsync(workspace.Path);

        await SeedTurnAsync(sessionId, "just a chat", "sure thing");

        // Must be a clean no-op — every ordinary agent session hits this path on every turn-end.
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
    }

    // ---- delegate release: pool or retire ----------------------------------------------------

    [Test]
    public async Task a_settled_shared_delegate_goes_warm_instead_of_dying()
    {
        // The whole point of the pool: the next task in this directory takes over a live Claude
        // instead of paying a cold start - so settle must NOT kill it.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, "task-warm");
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path,
            configure: t => { t.Ephemeral = true; t.AgentId = agentId; t.AgentName = "task-warm"; });
        await BindAgentSessionAsync(agentId, sessionId);

        var factory = new TestScopeFactory();
        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        factory.Stopper.Killed.ShouldBeEmpty("a warm delegate's session is the asset being kept");
        await using var verify = CreateContext();
        var agent = await verify.Agents.SingleAsync(a => a.Id == agentId);
        agent.Status.ShouldBe(AgentStatus.Idle);
        agent.PoolIdleSince.ShouldNotBeNull();
        agent.PoolReservedForRootTaskId.ShouldBe(
            task.RootTaskId, "reserved for its own run first, so follow-ups keep their context");
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).AgentName
            .ShouldBe("task-warm", "the snapshot keeps naming who ran the work");
    }

    [Test]
    public async Task a_settled_worktree_delegate_still_retires()
    {
        // Its directory dies with the merge - there is nothing for a warm session to sit in.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, "task-wt");
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, configure: t =>
        {
            t.Ephemeral = true;
            t.AgentId = agentId;
            t.Workspace = WorkspaceMode.Worktree;
            // No WorktreePath on purpose: merge-back reports Failed (nothing recorded), the task
            // still settles, and release must retire rather than pool.
        });
        await BindAgentSessionAsync(agentId, sessionId);

        var factory = new TestScopeFactory();
        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        factory.Stopper.Killed.ShouldBe([sessionId]);
        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == agentId)).ShouldBeFalse();
    }

    [Test]
    public async Task a_users_standing_agent_is_never_pooled_or_deleted()
    {
        // Pinning a task to your own agent must not hand that agent to the pool's lifecycle.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, "my-agent", poolDelegate: false);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => { t.Ephemeral = false; t.AgentId = agentId; });
        await BindAgentSessionAsync(agentId, sessionId);

        var factory = new TestScopeFactory();
        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        factory.Stopper.Killed.ShouldBeEmpty();
        await using var verify = CreateContext();
        var agent = await verify.Agents.SingleAsync(a => a.Id == agentId);
        agent.PoolIdleSince.ShouldBeNull("a standing agent has no pool state");
    }

    [Test]
    public async Task a_blocked_delegate_keeps_its_session_and_agent()
    {
        // Blocked means the conversation continues - killing the session here would orphan the
        // -Reply path and force a cold retry of work that only needed an answer.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, "task-blocked");
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => { t.Ephemeral = true; t.AgentId = agentId; });
        await BindAgentSessionAsync(agentId, sessionId);

        var factory = new TestScopeFactory();
        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Should negatives throw?");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        factory.Stopper.Killed.ShouldBeEmpty();
        await using var verify = CreateContext();
        var agent = await verify.Agents.SingleAsync(a => a.Id == agentId);
        agent.PoolIdleSince.ShouldBeNull("a Blocked delegate is still MID-conversation, not warm");
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Blocked);
    }

    /// <summary>The pool checks the agent's session pointer - bind it like dispatch would have.</summary>
    private static async Task BindAgentSessionAsync(Guid agentId, Guid sessionId)
    {
        await using var db = CreateContext();
        var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
        agent.PersistentSessionId = sessionId.ToString("D");
        await db.SaveChangesAsync();
    }

    // ---- worktree merge-back on settle -----------------------------------------------------

    [Test]
    public async Task a_succeeded_worktree_task_lands_its_branch_and_says_so_in_the_note()
    {
        using var repo = new ScratchGitRepo("antiphon-reply-merge");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");
        var factory = new TestScopeFactory(repo.WorktreeRoot);

        var parentSessionId = await SeedSessionAsync(repo.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(repo.Path, parentSessionId, t =>
        {
            t.Workspace = WorkspaceMode.Worktree;
            t.RepoPath = repo.Path;
            t.MergeTargetRef = "feat/parent";
        });
        await CreateWorktreeForAsync(factory, task);
        await File.WriteAllTextAsync(Path.Combine(TaskWorktreePath(task)!, "feature.md"), "the work\n");

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Wrote feature.md.");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await repo.GitReadAsync("show", "feat/parent:feature.md")).ShouldBe("the work\n");
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Merged)).ShouldBeTrue();

        var note = await verify.SessionQueuedMessages
            .SingleAsync(m => m.AgentSessionId == parentSessionId);
        note.Body.ShouldContain("merged → feat/parent", customMessage: "the caller must learn the branch landed");
    }

    [Test]
    public async Task a_merge_conflict_blocks_the_task_and_spawns_a_merge_delegate()
    {
        // "Done" work that cannot land is not done. The task blocks, and the conflict goes to a
        // Merge-role delegate working in the conflicted worktree — never an automatic resolution.
        using var repo = new ScratchGitRepo("antiphon-reply-conflict");
        await repo.CommitFileAsync("shared.md", "original\n");
        await repo.GitAsync("branch", "feat/parent");
        var factory = new TestScopeFactory(repo.WorktreeRoot);

        var parentSessionId = await SeedSessionAsync(repo.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(repo.Path, parentSessionId, t =>
        {
            t.Workspace = WorkspaceMode.Worktree;
            t.RepoPath = repo.Path;
            t.MergeTargetRef = "feat/parent";
        });
        await CreateWorktreeForAsync(factory, task);
        await File.WriteAllTextAsync(Path.Combine(TaskWorktreePath(task)!, "shared.md"), "delegate version\n");
        await repo.GitAsync("checkout", "feat/parent");
        await repo.CommitFileAsync("shared.md", "target version\n");

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Rewrote shared.md.");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var blocked = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        blocked.Status.ShouldBe(AgentTaskStatus.Blocked);
        blocked.FailureReason.ShouldContain("conflict");

        var merge = await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == task.Id);
        merge.Role.ShouldBe(AgentTaskRole.Merge);
        merge.ModelLevel.ShouldBe(AgentModelLevel.High, "conflict resolution is High-tier work by policy");
        merge.WorkingDirectory.ShouldBe(TaskWorktreePath(task), "it resolves IN the conflicted worktree");
        merge.ParentSessionId.ShouldBe(parentSessionId, "its report goes to the same caller");
        merge.Goal.ShouldContain("shared.md");
    }

    [Test]
    public async Task a_finished_merge_delegate_unblocks_its_conflicted_parent()
    {
        // The loop-closer: without it, a conflicted task stays Blocked forever after its conflict
        // was actually resolved.
        using var workspace = new TempWorkspace();
        var conflictedId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = conflictedId,
                RootTaskId = conflictedId,
                Title = "The conflicted task",
                Goal = "original work",
                Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = workspace.Path,
                WorktreePath = workspace.Path,
                WorktreeBranch = "feat/card-task-x",
                MergeTargetRef = "master",
                Status = AgentTaskStatus.Blocked,
                FailureReason = "Rebase onto master conflicted in 1 file(s).",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var (merge, mergeSessionId) = await SeedDispatchedTaskAsync(workspace.Path, configure: t =>
        {
            t.RootTaskId = conflictedId;
            t.ParentTaskId = conflictedId;
            t.Role = AgentTaskRole.Merge;
            t.ModelLevel = AgentModelLevel.High;
        });

        await SeedTurnAsync(
            mergeSessionId, DelegationReportFormatter.TaskMarker(merge.Id),
            "Resolved shared.md keeping the task's version; master fast-forwarded.");
        await CreateService().OnTurnEndAsync(mergeSessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == merge.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        var parent = await verify.AgentTasks.SingleAsync(t => t.Id == conflictedId);
        parent.Status.ShouldBe(AgentTaskStatus.Succeeded, "the conflict it was blocked on no longer exists");
        parent.FailureReason.ShouldBeNull();
    }

    private static async Task CreateWorktreeForAsync(TestScopeFactory factory, AgentTask seeded)
    {
        // The dispatcher's move, replayed: create the worktree and persist its coordinates.
        var worktrees = factory.ServiceProvider.GetRequiredService<DelegationWorktreeService>();
        await worktrees.CreateForTaskAsync(seeded, CancellationToken.None);
        await using var db = CreateContext();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Id);
        row.WorktreePath = seeded.WorktreePath;
        row.WorktreeBranch = seeded.WorktreeBranch;
        await db.SaveChangesAsync();
    }

    private static string? TaskWorktreePath(AgentTask task) => task.WorktreePath;

    // ---- a turn killed by an API error must never settle as done (CARD-0071 S3) ------------

    /// <summary>
    /// The 2026-08-17 live miss, replayed under S5a-3: tasks ee0a18a5 and 27e20988 were killed by
    /// the account session limit, and both settled <c>Succeeded</c> with "You've hit your usage
    /// limit…" stored as their Result. The error text is still not a report. A retryable class
    /// (Wall) now defers: the task stays Working, no parent failure note, the delegate is not
    /// released.
    /// </summary>
    [Test]
    public async Task a_retryable_api_error_defers_the_task_and_never_stores_the_error_text()
    {
        using var workspace = new TempWorkspace();
        var parentSessionId = await SeedSessionAsync(workspace.Path);
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, parentSessionId, configure: t => t.AgentId = agentId);

        await SeedApiErrorStubTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var deferred = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        deferred.Status.ShouldBe(AgentTaskStatus.Working, "a retryable death keeps the task; the resume owns it");
        deferred.Result.ShouldBeNull("the error text is not a report and must never be stored as one");
        deferred.FailureReason.ShouldBeNull();
        deferred.CompletedAt.ShouldBeNull();

        var ev = (await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.ApiErrorDeferred)
            .ToListAsync()).ShouldHaveSingleItem();
        ev.Detail.ShouldContain("Wall");
        ev.Detail.ShouldContain("resume scheduled");

        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == parentSessionId))
            .ShouldBe(0, "do not tell the parent the task failed while a resume is scheduled");

        // The delegate still owns the session for the resumed turn.
        (await verify.Agents.SingleAsync(a => a.Id == agentId)).Status.ShouldBe(AgentStatus.Running);
    }

    [Test]
    public async Task real_narration_beside_the_stub_still_does_not_settle_on_it()
    {
        // A turn that did real visible work and THEN died on the API: the narration is not the
        // verdict (CARD-0046 already established that) and the death outranks it — settling
        // Succeeded on mid-turn text would hide that the turn never finished.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedApiErrorStubTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            narration: "I'll start by reading the spec.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Working);
        stored.Result.ShouldBeNull("neither the error text nor the narration is this turn's report");
    }

    [Test]
    public async Task a_second_on_turn_end_on_the_same_stub_adds_nothing()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedApiErrorStubTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id));
        var svc = CreateService();
        await svc.OnTurnEndAsync(sessionId, CancellationToken.None);
        await svc.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.ApiErrorDeferred))
            .ShouldBe(1, "the recovery row is the idempotency marker; a second pass must not re-enter");
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Working);
    }

    [Test]
    public async Task the_api_error_incident_is_warning_when_the_agent_is_not_channel_bound()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.AgentId = agentId);

        await SeedApiErrorStubTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.ApiErrorTurnDied);
        incident.Severity.ShouldBe(
            AlertSeverity.Warning, "a Wall death with nobody waiting on a channel is loud, not critical");
        incident.Message.ShouldContain(DelegationReportFormatter.Short(task.Id));
        incident.Message.ShouldContain("NOT stored", customMessage: "the incident says what did not happen");
    }

    [Test]
    public async Task the_api_error_incident_is_critical_when_the_agent_is_channel_bound()
    {
        // The CARD-0055/0067 severity rule: a channel binding means a real person is on the other
        // end of this agent, and its death just went silent at them.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.AgentId = agentId);
        await using (var db = CreateContext())
        {
            db.ChatChannels.Add(new ChatChannel
            {
                Id = Guid.NewGuid(),
                Provider = "telegram",
                ExternalId = $"chat-{Guid.NewGuid():N}",
                Kind = ChatChannelKind.Direct,
                AgentId = agentId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await SeedApiErrorStubTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.ApiErrorTurnDied);
        incident.Severity.ShouldBe(AlertSeverity.Critical);
    }

    [Test]
    public async Task a_needs_human_error_is_critical_even_without_a_channel()
    {
        // authentication_failed: nothing automatic will ever fix it and no retry will ever be
        // scheduled — a human is the only recovery, so the incident must reach one.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.AgentId = agentId);

        await SeedApiErrorStubTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id),
            errorText: "API Error: 401 authentication_error — OAuth token has expired.",
            apiErrorClass: "authentication_failed", apiErrorStatus: null);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.ApiErrorTurnDied);
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .FailureReason!.ShouldContain("NeedsHuman");
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Failed, "NeedsHuman never schedules a resume");
    }

    [Test]
    public async Task a_parked_recovery_fails_the_task_naming_exhaustion()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await using (var db = CreateContext())
        {
            var now = DateTime.UtcNow;
            for (var i = 0; i < 2; i++)
            {
                db.ApiErrorRecoveries.Add(new ApiErrorRecovery
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    StubSequence = i + 1,
                    Classification = ApiErrorClassification.Wall,
                    ApiErrorClass = "rate_limit",
                    ApiErrorStatus = 429,
                    DetectedAt = now.AddMinutes(-60 * (2 - i)),
                    AttemptCount = 1,
                    ResolvedAt = now,
                    ResolvedReason = ApiErrorRecoveryReasons.Replaced,
                });
            }
            await db.SaveChangesAsync();
        }

        await SeedApiErrorStubTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason!.ShouldContain("WallParked");
        failed.Result.ShouldBeNull();
    }

    [Test]
    public async Task a_dirty_shared_checkout_is_named_in_the_api_error_incident()
    {
        // Spec §D6: auto-salvage of a dead task's uncommitted work is rejected (on a shared
        // checkout the dirt cannot be safely attributed), so the incident carries the exposure for
        // the human who decides instead.
        using var repo = new ScratchGitRepo("antiphon-reply-apierror");
        await repo.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "uncommitted-work.cs"), "the dead task's edits\n");

        var agentId = await SeedAgentAsync(repo.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            repo.Path, configure: t => t.AgentId = agentId);

        await SeedApiErrorStubTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.ApiErrorTurnDied);
        incident.Message.ShouldContain(
            "uncommitted-work.cs", customMessage: "git status --short of the shared checkout rides the incident");
    }

    [Test]
    public async Task an_unmarked_stub_turn_is_not_an_uncorrelated_report()
    {
        // The stub's error string must not count as assistant text ANYWHERE: without the marker the
        // turn is not ours, and "API Error: 429…" must not read as a finished-looking report either
        // — that incident is what the delivery watchdog kills tasks on.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"delegate-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.AgentId = agentId);

        await SeedApiErrorStubTurnAsync(sessionId, "a prompt with no marker on it");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched, "an unmarked turn settles nothing, dead or not");
        (await verify.AgentIncidents.AnyAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.ApiErrorTurnDied))
            .ShouldBeFalse("the guard is scoped to the MARKED turn");
        (await verify.AgentIncidents.AnyAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated))
            .ShouldBeFalse("an error string is not a report somebody failed to attribute");
    }

    /// <summary>
    /// The measured API-error stub shape (sessions 19b6bdbb / 3c8cef08, 2026-08-17; CARD-0072
    /// sweep): ONE synthetic assistant record, normalized to an AssistantText carrying the error
    /// string plus a <c>stop_sequence</c> TurnEnd, with S1's three fields stamped on both rows.
    /// </summary>
    private static async Task SeedApiErrorStubTurnAsync(
        Guid sessionId, string prompt,
        string errorText =
            "API Error: 429 You've hit your usage limit. Your limit will reset at 6:10pm (Europe/London).",
        string apiErrorClass = "rate_limit", int? apiErrorStatus = 429,
        string? narration = null)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        db.TranscriptEntries.Add(NewEntry(sessionId, ++seq, TranscriptKinds.UserPrompt, prompt));
        if (narration is not null)
        {
            var chatter = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, narration);
            chatter.ApiCallId = $"msg_{Guid.NewGuid():N}";
            db.TranscriptEntries.Add(chatter);
        }

        var stubText = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, errorText);
        stubText.IsApiError = true;
        stubText.ApiErrorClass = apiErrorClass;
        stubText.ApiErrorStatus = apiErrorStatus;
        db.TranscriptEntries.Add(stubText);

        var stubEnd = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        stubEnd.StopReason = "stop_sequence";
        stubEnd.IsApiError = true;
        stubEnd.ApiErrorClass = apiErrorClass;
        stubEnd.ApiErrorStatus = apiErrorStatus;
        db.TranscriptEntries.Add(stubEnd);
        await db.SaveChangesAsync();
    }

    // ---- helpers ---------------------------------------------------------------------------

    // Most cases pin the ceiling explicitly so they stay readable as the shipped default moves;
    // pass `settings` to exercise what actually ships.
    private static AgentTaskReplyService CreateService(
        TestScopeFactory? factory = null,
        DelegationSettings? settings = null,
        TimeProvider? timeProvider = null)
    {
        settings ??= new DelegationSettings { ReplyInlineMaxChars = 20_000 };
        return new AgentTaskReplyService(
            factory ?? new TestScopeFactory(),
            Options.Create(settings),
            new MockEventBus(),
            timeProvider ?? TimeProvider.System,
            NullLogger<AgentTaskReplyService>.Instance);
    }

    private static async Task<(AgentTask Task, Guid SessionId)> SeedDispatchedTaskAsync(
        string workingDirectory, Guid? parentSessionId = null, Action<AgentTask>? configure = null)
    {
        var sessionId = await SeedSessionAsync(workingDirectory);
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentSessionId = parentSessionId,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            Title = "Seeded delegate",
            Goal = "Do the thing.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = DateTime.UtcNow,
            DispatchedAt = DateTime.UtcNow,
        };
        configure?.Invoke(task);

        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return (task, sessionId);
    }

    /// <summary>
    /// A second task on a session and agent that already exist — the warm-pool reuse shape, and the
    /// only way to make one session produce the same incident twice.
    /// </summary>
    private static async Task<AgentTask> SeedFollowUpTaskAsync(
        string workingDirectory, Guid sessionId, Guid agentId)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ReplyTo = AgentTaskReplyTo.None,
            Title = "Follow-up on a warm delegate",
            Goal = "Do the other thing.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            AgentSessionId = sessionId,
            AgentId = agentId,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = DateTime.UtcNow,
            DispatchedAt = DateTime.UtcNow,
        };

        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<Guid> SeedAgentAsync(string workingDirectory, string name, bool poolDelegate = true)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = name,
            WorkingDirectory = workingDirectory,
            Details = "Pool delegate.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = poolDelegate,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task<Guid> SeedSessionAsync(string cwd)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            CardId = null,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    /// <summary>
    /// A prompt, optional assistant text, then a TurnEnd — the shape a real turn leaves.
    /// <paramref name="entriesPerApiCall"/> models the real JSONL shape: a single API
    /// call emits several entries (text, tool call, tool result...) that all carry the same
    /// ApiCallId and REPEAT its usage numbers verbatim — so anything summing per entry overcounts.
    ///
    /// <paramref name="turnEndApiCallId"/> defaults to NULL, which is the legacy shape and the
    /// reason CARD-0046's deferral leaves all these tests on their existing path: with no response
    /// identity on the boundary there is nothing to wait for. Pass one to exercise the identity gate
    /// (see <see cref="SeedSplitTurnAsync"/> for the split shape it was written for).
    /// </summary>
    private static async Task SeedTurnAsync(
        Guid sessionId, string prompt, string? assistantText, int? inputTokens = null, int? outputTokens = null,
        int? cacheReadTokens = null, int? cacheCreationTokens = null, int entriesPerApiCall = 1,
        DateTime? timestamp = null, string? turnEndApiCallId = null)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        db.TranscriptEntries.Add(NewEntry(sessionId, ++seq, TranscriptKinds.UserPrompt, prompt));
        if (assistantText is not null)
        {
            var apiCallId = $"msg_{Guid.NewGuid():N}";
            for (var i = 0; i < entriesPerApiCall; i++)
            {
                var entry = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, assistantText);
                entry.ApiCallId = apiCallId;
                entry.Timestamp = timestamp;
                entry.InputTokens = inputTokens;
                entry.OutputTokens = outputTokens;
                entry.CacheReadTokens = cacheReadTokens;
                entry.CacheCreationTokens = cacheCreationTokens;
                db.TranscriptEntries.Add(entry);
            }
        }
        var end = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        end.StopReason = "end_turn";
        end.ApiCallId = turnEndApiCallId;
        db.TranscriptEntries.Add(end);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The measured split shape (CARD-0046 §1.2, session 7f9d06a5): mid-turn narration under its own
    /// API call, then the turn-ending response written as TWO JSONL records sharing ONE
    /// <c>message.id</c> — a signature-only thinking record, which normalizes to a BARE TurnEnd, and
    /// 0.01-1.2 s later the text record plus its own (deduped) TurnEnd sibling.
    ///
    /// <paramref name="finalMessage"/> null stops after the bare TurnEnd — the instant settlement
    /// used to fire in. Returns the turn-ending response's ApiCallId so a test can land its text
    /// afterwards, exactly as the tailer does.
    /// </summary>
    private static async Task<string> SeedSplitTurnAsync(
        Guid sessionId, string prompt, string narration, string? finalMessage)
    {
        var finalCallId = $"msg_{Guid.NewGuid():N}";
        await using (var db = CreateContext())
        {
            var seq = await db.TranscriptEntries
                .Where(t => t.AgentSessionId == sessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0;

            db.TranscriptEntries.Add(NewEntry(sessionId, ++seq, TranscriptKinds.UserPrompt, prompt));

            // An EARLIER API call: the "I'll start by…" narration between tool calls. Non-empty, so
            // the "no text yet — leave it running" guard never fired.
            var chatter = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, narration);
            chatter.ApiCallId = $"msg_{Guid.NewGuid():N}";
            db.TranscriptEntries.Add(chatter);

            // The thinking record of the turn-ending response: a TurnEnd carrying that response's id
            // and NOTHING else.
            var bareEnd = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
            bareEnd.StopReason = "end_turn";
            bareEnd.ApiCallId = finalCallId;
            db.TranscriptEntries.Add(bareEnd);
            await db.SaveChangesAsync();
        }

        if (finalMessage is not null)
            await AppendFinalMessageAsync(sessionId, finalCallId, finalMessage);
        return finalCallId;
    }

    /// <summary>The text record of an already-ended response, and its duplicate TurnEnd sibling.</summary>
    private static async Task AppendFinalMessageAsync(Guid sessionId, string apiCallId, string finalMessage)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        var text = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, finalMessage);
        text.ApiCallId = apiCallId;
        db.TranscriptEntries.Add(text);

        var end = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        end.StopReason = "end_turn";
        end.ApiCallId = apiCallId;
        db.TranscriptEntries.Add(end);
        await db.SaveChangesAsync();
    }

    /// <summary>One entry appended at the session's next sequence, with a real record timestamp.</summary>
    private static async Task SeedEntryAsync(
        Guid sessionId, string kind, string? text, DateTime timestamp, string? apiCallId = null,
        string? toolUseId = null, string? toolName = null)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        var entry = NewEntry(sessionId, seq + 1, kind, text);
        entry.Timestamp = timestamp;
        entry.ApiCallId = apiCallId;
        entry.ToolUseId = toolUseId;
        entry.ToolName = toolName;
        if (kind == TranscriptKinds.TurnEnd)
            entry.StopReason = "end_turn";
        db.TranscriptEntries.Add(entry);
        await db.SaveChangesAsync();
    }

    private static Task SeedToolCallAsync(Guid sessionId, string toolName, string toolUseId, DateTime at) =>
        SeedEntryAsync(
            sessionId, TranscriptKinds.ToolCall, null, at,
            apiCallId: $"msg_{Guid.NewGuid():N}", toolUseId: toolUseId, toolName: toolName);

    /// <summary>
    /// The measured background fan-out (session ac09cffd seqs 1-19): the brief, a line of narration,
    /// four <c>Agent</c> ToolCalls each answered instantly by the async-launch marker, then the
    /// announcement and a REAL turn end whose text has already landed under the same ApiCallId —
    /// so nothing in slice 1 defers it. Returns the four <c>toolu_…</c> ids, in launch order.
    /// </summary>
    private static async Task<List<string>> SeedSubagentFanOutAsync(
        Guid sessionId, DateTime dispatchedAt, Guid taskId)
    {
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(taskId) + "\n\nJudge whether commit ce48f50 is correct.",
            dispatchedAt.AddMinutes(1));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.AssistantText,
            "The commit is large but well-scoped. I'll fan out four parallel review agents.",
            dispatchedAt.AddMinutes(1));

        var launched = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var toolUseId = $"toolu_{Guid.NewGuid():N}";
            launched.Add(toolUseId);
            await SeedToolCallAsync(
                sessionId, TranscriptKinds.AgentToolName, toolUseId, dispatchedAt.AddMinutes(1));
            await SeedEntryAsync(
                sessionId, TranscriptKinds.ToolResult,
                TranscriptKinds.AsyncAgentLaunchMarker
                + " successfully. (This tool result is internal metadata — never quote or paste any "
                + "part of it, including the agentId below, into a user-facing reply.)",
                dispatchedAt.AddMinutes(1), toolUseId: toolUseId);
        }

        await SeedResponseAsync(
            sessionId,
            "Four review agents are running in parallel — proposal fidelity, the state model, the "
            + "allocator, and menu safety. I'll synthesize when they report.",
            dispatchedAt.AddMinutes(1));
        return launched;
    }

    /// <summary>
    /// One background subagent reporting: the <c>&lt;task-notification&gt;</c> USER record naming the
    /// launch it answers, then the turn the delegate takes in response — a BARE TurnEnd first (the
    /// notification turns really are the split shape, ac09cffd seqs 21-23) and then its text.
    /// </summary>
    private static async Task SeedSubagentNotificationAsync(
        Guid sessionId, string toolUseId, string delegateReply, DateTime at)
    {
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            $"{TranscriptKinds.TaskNotificationPrefix}\n<task-id>a548067d72b9d6de9</task-id>\n"
            + $"<tool-use-id>{toolUseId}</tool-use-id>\n<status>completed</status>\n"
            + "<result>The subagent's own report.</result>\n</task-notification>",
            at);

        var apiCallId = $"msg_{Guid.NewGuid():N}";
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, at, apiCallId);
        await SeedEntryAsync(sessionId, TranscriptKinds.AssistantText, delegateReply, at, apiCallId);
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, at, apiCallId);
    }

    /// <summary>An assistant response: its text and the TurnEnd sibling that shares its message id.</summary>
    private static async Task SeedResponseAsync(Guid sessionId, string text, DateTime timestamp)
    {
        var apiCallId = $"msg_{Guid.NewGuid():N}";
        await SeedEntryAsync(sessionId, TranscriptKinds.AssistantText, text, timestamp, apiCallId);
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, timestamp, apiCallId);
    }

    /// <summary>
    /// What a manual <c>/compact</c> actually leaves behind, in the ARRIVAL order measured on
    /// session c8d07c43 (seqs 132-136): the raw echo of the typed line first, then the boundary,
    /// then the synthetic continuation prompt, then the two wrapper records — whose own timestamps
    /// run backwards against their sequences, which is why nothing here may lean on ordering by
    /// time. Five records, four of them USER, none of them a prompt anybody typed (CARD-0041).
    /// </summary>
    private static async Task SeedCompactionRecordsAsync(Guid sessionId, DateTime at, string typed)
    {
        var args = typed["/compact".Length..].TrimStart();
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, typed, at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.CompactBoundary,
            $"Context compacted {TranscriptKinds.ManualCompactMarker}", at.AddMinutes(2));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            TranscriptKinds.CompactionContinuationPromptPrefix
            + " that ran out of context. The summary below covers…", at.AddMinutes(2));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "<command-name>/compact</command-name>\n            <command-message>compact</command-message>\n"
            + $"            <command-args>{args}</command-args>", at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "<local-command-stdout>Compacted (ctrl+o to see full summary)</local-command-stdout>",
            at.AddMinutes(2));
    }

    /// <summary>
    /// The warm-reuse shape end to end: a PREVIOUS task's finished turn (before this task existed),
    /// then the focused <c>/compact</c> the reuse path sends, then this task's brief — which is
    /// exactly where session c8d07c43 was when its task was failed as unattributable.
    /// </summary>
    private static async Task SeedWarmReuseAfterCompactionAsync(
        Guid sessionId, DateTime dispatchedAt, Guid taskId)
    {
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(Guid.NewGuid()) + "\n\nThe task before this one.",
            dispatchedAt.AddMinutes(-38));
        await SeedResponseAsync(
            sessionId, "Plan delivered: docs/superpowers/specs/2026-08-14-card-0046.md",
            dispatchedAt.AddMinutes(-30));

        await SeedCompactionRecordsAsync(
            sessionId, dispatchedAt.AddMinutes(1),
            "/compact This session is being handed NEW, unrelated work. Keep only context useful for: X");

        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(taskId) + "\n\nImplement slices 2 and 3.",
            dispatchedAt.AddMinutes(3));
    }

    private static TranscriptEntry NewEntry(Guid sessionId, long sequence, string kind, string? text) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        CreatedAt = DateTime.UtcNow,
    };

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>
    /// The reply service is a singleton that opens a DI scope per operation. This supplies the two
    /// services it resolves — a real DbContext and a queue whose runtime is never actually driven
    /// (delivery is asserted through the persisted queue rows, not a live pty).
    /// </summary>
    private sealed class TestScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly ServiceProvider _provider;

        /// <summary>Records what the settle path asked to stop — the ephemeral-cleanup assertion.</summary>
        public RecordingSessionStopper Stopper { get; } = new();

        public TestScopeFactory(string? worktreeRoot = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IEventBus, MockEventBus>();
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<ApiErrorRecoveryService>();
            // The settle path's collaborators: merge-back, the Merge-task spawner, ephemeral cleanup.
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IDelegateSessionStopper>(Stopper);
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddScoped<AgentTaskService>();
            services.AddSingleton(Options.Create(new GitSettings
            {
                WorktreeBasePath = worktreeRoot ?? Path.Combine(Path.GetTempPath(), "antiphon-reply-wt"),
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24,
            }));
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IWorktreeManager,
                Antiphon.Server.Infrastructure.Git.WorktreeManager>();
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IGitService,
                Antiphon.Server.Infrastructure.Git.GitService>();
            services.AddScoped<DelegationWorktreeService>();
            _provider = services.BuildServiceProvider();
        }

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => _provider;
        public object? GetService(Type serviceType) => _provider.GetService(serviceType);
        public void Dispose() { }

        private sealed class TempWorkspaceMarker;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-reply-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
