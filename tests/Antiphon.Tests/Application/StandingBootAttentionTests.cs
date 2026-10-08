using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1156 S4 (option A): the current-state standing-boot attention projection and the cold
/// incident prune, over an isolated PostgreSQL database with a <c>FakeTimeProvider</c>, through
/// <c>AttentionServiceTests.BuildService</c> and the real <c>AgentSupervisorService.PruneIncidentsAsync</c>
/// built by <c>AgentSupervisionTests.BuildHarness</c> on the same database. Design V-8, V-9 and
/// V-10 in <c>docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-test-design.md</c>.
/// The projection is read-only: every method asserts zero writes on its context and zero runner
/// calls beyond <c>GetAsync</c>'s inherited one list.
/// </summary>
[Category("Integration")]
public class StandingBootAttentionTests
{
    /// <summary>Wording of the retired CARD-0312 restart ladder, absent from every standing row.</summary>
    private static readonly string[] RetiredWording =
        ["restart ladder", "stopped restarting", "kill", "retry", "Boot prompt confirmed", "composer holds"];

    /// <summary>
    /// V-8. One derived LivenessProbeFailed row per current episode, independent of incident
    /// history: no-incident (no receipt at all), failed-save (the sweep's writer faulted),
    /// warning-at-eight (Warning, prompt age, boot due and operator due in the evidence),
    /// error-at-twenty (Error, the operator wording: inspect, wait, reply, or explicitly Stop and
    /// Start/resume; actions OpenAgent and OpenDrawer; never Retry/Cancel), older-than-24-hours (a
    /// 25-hour-old receipt still projects the current row), pruned-history (receipts deleted; the row
    /// remains), legacy-and-current (an old <c>bootSeq=</c> receipt on the same session yields one
    /// row with the new wording and no "restart ladder" text).
    /// </summary>
    [Test]
    [Arguments("no-incident")]
    [Arguments("failed-save")]
    [Arguments("warning-at-eight")]
    [Arguments("error-at-twenty")]
    [Arguments("older-than-24-hours")]
    [Arguments("pruned-history")]
    [Arguments("legacy-and-current")]
    public async Task C1156_Current_boot_attention_survives_optional_history(string history)
    {
        var options = history switch
        {
            "older-than-24-hours" => new StandingBootWatchOptions
            {
                PromptAge = TimeSpan.FromHours(25), StartedAge = TimeSpan.FromHours(26),
            },
            // Starts before the boot due, so the fake clock can walk forward onto it.
            "warning-at-eight" => new StandingBootWatchOptions { PromptAge = TimeSpan.FromMinutes(7) },
            _ => new StandingBootWatchOptions(),
        };
        await using var f = await StandingBootWatchFixture.CreateAsync(options);
        var before = await f.SnapshotAsync();
        var bootDue = f.PromptAt.AddMinutes(8);
        var operatorDue = f.PromptAt.AddMinutes(20);
        var expected = AlertSeverity.Warning;
        var receipts = 1;

        switch (history)
        {
            case "no-incident":
                receipts = 0;
                break;
            case "failed-save":
            {
                var fault = new IncidentInsertFault();
                f.WriterInterceptors = [fault];
                (await f.SweepAsync()).ShouldBe(0, f.Warnings());
                fault.Fired.ShouldBeTrue("control: the writer's insert must actually fault");
                f.Warnings().ShouldContain("Could not record standing boot receipt");
                receipts = 0;
                break;
            }
            case "warning-at-eight":
                f.At(bootDue.AddSeconds(-1));
                (await LivenessRowsAsync(f)).ShouldBeEmpty("one second before the boot due nothing is due");
                f.At(bootDue);
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                break;
            case "error-at-twenty":
                f.At(operatorDue.AddSeconds(-1));
                (await LivenessRowsAsync(f)).Single().Severity
                    .ShouldBe(AlertSeverity.Warning, "control: one second before the operator due the row is a Warning");
                f.At(operatorDue);
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                expected = AlertSeverity.Error;
                break;
            case "older-than-24-hours":
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                await ShiftReceiptsAsync(f, TimeSpan.FromHours(-25));
                expected = AlertSeverity.Error;
                break;
            case "pruned-history":
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                await using (var db = f.Read())
                {
                    (await db.AgentIncidents.Where(i => i.SessionId == f.SessionId).ExecuteDeleteAsync())
                        .ShouldBe(1, "control: the receipt existed and is gone");
                }

                receipts = 0;
                break;
            case "legacy-and-current":
                await AddIncidentAsync(f, BootReplyWatchdogService.EpisodeKey(1), AlertSeverity.Warning,
                    f.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1),
                    "Boot prompt confirmed at sequence 1; restart ladder engaged; stopped restarting after two.");
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(history), history, null);
        }

        var now = f.Clock.GetUtcNow().UtcDateTime;
        var all = await ProjectAsync(f);
        var rows = all.Where(i => i.Kind == AttentionKind.LivenessProbeFailed).ToList();
        rows.Count.ShouldBe(1, $"{history}: exactly one current row, whatever the history");
        var row = rows[0];
        row.Severity.ShouldBe(expected, history);
        row.AgentId.ShouldBe(f.AgentId);
        row.TaskId.ShouldBeNull();
        row.Title.ShouldStartWith("sbw-");
        row.SinceUtc.ShouldBe(f.PromptAt);
        row.Actions.ShouldBe([AttentionAction.OpenAgent, AttentionAction.OpenDrawer],
            "the agent's own controls; never a task-only Retry or Cancel");
        row.Headline.ShouldContain($"no model reply {StandingBootWatchPolicy.Describe(now - f.PromptAt)} after the prompt");
        row.Headline.ShouldStartWith(expected == AlertSeverity.Error
            ? "Standing boot stall needs an operator decision"
            : "Standing boot stall detected");
        row.Evidence.ShouldContain("Inspect the session or its transcript, then choose: keep waiting, reply "
            + "through the session, or explicitly Stop and Start/resume the agent.");
        row.Evidence.ShouldContain("Detection only: the session keeps running and keeps its seat; nothing is "
            + "stopped, typed, restarted or latched automatically, and no deadline ends this episode.");
        row.Evidence.ShouldContain($"Prompt #1 (UserPrompt) at {f.PromptAt:u}, {StandingBootWatchPolicy.Describe(now - f.PromptAt)} ago");
        row.Evidence.ShouldContain($"Boot notice due {bootDue:u}.");
        row.Evidence.ShouldContain($"Operator decision due {operatorDue:u}.");
        foreach (var retired in RetiredWording)
        {
            (row.Headline + "\n" + row.Evidence).ShouldNotContain(retired, Case.Insensitive,
                $"{history}: the retired restart-ladder wording must not reach a standing row");
        }

        (row.Headline + row.Evidence).ShouldNotContain(StandingBootWatchFixture.PromptCanary);
        all.ShouldNotContain(i => i.Kind == AttentionKind.RecentCriticalIncident && i.AgentId == f.AgentId,
            "the current episode's receipt is the row's own evidence, never a second row under another name");

        (await f.ReceiptsAsync()).Count.ShouldBe(receipts, $"{history}: the projection never writes a receipt");
        (await f.SnapshotAsync()).ShouldBe(before, "detection changes no custody or supervision state");
        f.AssertNothingDestructive();
    }

    /// <summary>
    /// V-9. The five real model kinds (assistant, thinking, tool-call, tool-result, turn-end) past
    /// the prompt clear the derived row; terminal-session (Stopped with EndedAt) and
    /// replaced-launch (a new LaunchResumedAt past the old prompt, with its own unanswered prompt)
    /// clear the old episode, and the replacement can independently appear. Receipts remain in the
    /// table in every argument.
    /// </summary>
    [Test]
    [Arguments("assistant")]
    [Arguments("thinking")]
    [Arguments("tool-call")]
    [Arguments("tool-result")]
    [Arguments("turn-end")]
    [Arguments("terminal-session")]
    [Arguments("replaced-launch")]
    public async Task C1156_Positive_resolution_clears_only_the_current_episode(string resolution)
    {
        await using var f = await StandingBootWatchFixture.CreateAsync();
        (await f.SweepAsync()).ShouldBe(1, f.Warnings());
        var receipt = (await f.ReceiptsAsync()).Single();
        (await LivenessRowsAsync(f)).Count.ShouldBe(1, "control: the unresolved episode projects");

        var refinedAt = f.PromptAt.AddMinutes(2);
        switch (resolution)
        {
            case "assistant":
            case "thinking":
            case "tool-call":
            case "tool-result":
            case "turn-end":
                await f.AddEntryAsync(ModelKind(resolution), "late first token", f.PromptAt.AddMinutes(8.5));
                break;
            case "terminal-session":
                await using (var db = f.Read())
                {
                    var now = f.Clock.GetUtcNow().UtcDateTime;
                    await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.Status, SessionStatus.Stopped)
                        .SetProperty(s => s.EndedAt, (DateTime?)now));
                }

                break;
            case "replaced-launch":
                await using (var db = f.Read())
                {
                    var resumed = StandingBootWatchFixture.Pg(f.PromptAt.AddMinutes(1));
                    await db.AgentSessions.Where(s => s.Id == f.SessionId).ExecuteUpdateAsync(u => u
                        .SetProperty(s => s.LaunchResumedAt, (DateTime?)resumed));
                }

                await f.AddEntryAsync(TranscriptKinds.UserPrompt, "the resumed brief", refinedAt);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(resolution), resolution, null);
        }

        (await LivenessRowsAsync(f)).ShouldBeEmpty($"{resolution}: the old episode's row is gone");
        (await f.ReceiptsAsync()).Select(r => r.Id)
            .ShouldBe([receipt.Id], "history remains; only the derived row resolves");

        if (resolution == "replaced-launch")
        {
            f.At(refinedAt.AddMinutes(8));
            var replacement = (await LivenessRowsAsync(f)).ShouldHaveSingleItem("the replacement episode appears once due");
            replacement.Severity.ShouldBe(AlertSeverity.Warning,
                "the old episode's receipts never escalate the new one");
            replacement.SinceUtc.ShouldBe(refinedAt);
            replacement.Evidence.ShouldContain($"Prompt #2 (UserPrompt) at {refinedAt:u}");
            replacement.Evidence.ShouldContain($"Boot notice due {refinedAt.AddMinutes(8):u}.");
        }

        f.AssertNothingDestructive();
    }

    /// <summary>
    /// V-10. The real cold prune with the shipped age cutoff and a lowered per-agent cap:
    /// age-cutoff (a 31-day-old current-episode receipt survives while an unrelated 31-day-old
    /// incident is deleted), agent-cap (cap 2 with six newer unrelated incidents: both current
    /// receipts survive, unrelated rows are capped), unknown-current-read (a read fault while
    /// computing the protected keys retains every <c>standingBoot:v1</c> receipt and prunes the
    /// rest), positive-resolution (after a model reply the same receipts are deleted by ordinary
    /// retention). After each prune a sweep tick cannot re-mint a retained receipt.
    /// </summary>
    [Test]
    [Arguments("age-cutoff")]
    [Arguments("agent-cap")]
    [Arguments("unknown-current-read")]
    [Arguments("positive-resolution")]
    public async Task C1156_Prune_preserves_active_dedup_and_releases_resolved_history(string pressure)
    {
        await using var f = await StandingBootWatchFixture.CreateAsync();
        (await f.SweepAsync()).ShouldBe(1, f.Warnings());
        f.At(f.PromptAt.AddMinutes(21));
        (await f.SweepAsync()).ShouldBe(1, f.Warnings());
        var current = (await f.ReceiptsAsync()).Select(r => r.Id).ToList();
        current.Count.ShouldBe(2, "control: both stages of the current episode are on record");

        var now = f.Clock.GetUtcNow().UtcDateTime;
        var old = now.AddDays(-31);
        // A receipt of an earlier, long-resolved episode on the same session: never protected.
        var resolvedEpisode = await AddIncidentAsync(
            f, "standingBoot:v1;g=1;l=1;p=0;stage=detected", AlertSeverity.Warning,
            pressure == "agent-cap" ? now.AddHours(-1) : old, "an earlier episode");
        var unrelated = new List<Guid>();
        if (pressure == "agent-cap")
        {
            for (var i = 0; i < 6; i++)
                unrelated.Add(await AddUnrelatedAsync(f, now.AddHours(1).AddMinutes(i)));
        }
        else
        {
            unrelated.Add(await AddUnrelatedAsync(f, old));
            await ShiftReceiptsAsync(f, TimeSpan.FromDays(-31), current);
        }

        if (pressure == "positive-resolution")
            await f.AddEntryAsync(TranscriptKinds.AssistantText, "answered at last", now.AddMinutes(-1));

        var fault = pressure == "unknown-current-read" ? new CandidateReadFault() : null;
        var settings = pressure == "agent-cap" ? new SupervisionSettings { IncidentCapPerAgent = 2 } : new SupervisionSettings();
        var (removed, log) = await PruneAsync(f, settings, fault);

        var remaining = await RemainingAsync(f);
        string[] expected;
        switch (pressure)
        {
            case "age-cutoff":
                expected = Ids(current);
                removed.ShouldBe(2, "the unrelated and the resolved-episode rows past retention");
                break;
            case "agent-cap":
                expected = Ids([.. current, unrelated[4], unrelated[5]]);
                removed.ShouldBe(5, "four older unrelated rows and the resolved-episode receipt are capped");
                break;
            case "unknown-current-read":
                fault!.Fired.ShouldBeTrue("control: the candidate read must actually fault");
                log.ShouldContain(l => l.StartsWith("[Warning]") && l.Contains("Could not read the current standing boot episodes"),
                    string.Join('\n', log));
                expected = Ids([.. current, resolvedEpisode]);
                removed.ShouldBe(1, "uncertainty retains every standing receipt; the unrelated row still prunes");
                break;
            case "positive-resolution":
                expected = [];
                removed.ShouldBe(4, "an answered episode's receipts are ordinary history again");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pressure), pressure, null);
        }

        remaining.ShouldBe(expected, ignoreOrder: true, $"{pressure}: the rows the prune kept");

        // The retained receipts are live dedup: the next tick finds them and mints nothing.
        (await f.SweepAsync()).ShouldBe(0, f.Warnings());
        (await RemainingAsync(f)).ShouldBe(expected, ignoreOrder: true, $"{pressure}: no receipt was re-minted");
        f.AssertNothingDestructive();
    }

    /// <summary>
    /// Repair of Review 3fe22492 F-1. The receipt read is optional history: a fault on it must not
    /// abort the attention feed. The real <c>GetAsync</c> with a one-shot fault on the standing
    /// receipt SELECT still returns the current episode's row from its clock alone and every
    /// unrelated row (an unrelated Crash incident on the same agent); a Warning is logged.
    /// detected-on-record: a Warning receipt exists, the row is the ordinary Warning.
    /// operator-on-record-unreadable: an operator receipt exists but the clock has stepped back
    /// below the operator due; with the receipt unreadable the row falls back to the clock's
    /// Warning (unreadable history is never a hidden row and never a stop).
    /// </summary>
    [Test]
    [Arguments("detected-on-record")]
    [Arguments("operator-on-record-unreadable")]
    public async Task C1156_Optional_receipt_read_fault_keeps_current_attention(string record)
    {
        await using var f = await StandingBootWatchFixture.CreateAsync();
        (await f.SweepAsync()).ShouldBe(1, f.Warnings());
        if (record == "operator-on-record-unreadable")
        {
            f.At(f.PromptAt.AddMinutes(21));
            (await f.SweepAsync()).ShouldBe(1, f.Warnings());
        }

        // The operator case observes the episode below its operator due: a clock stepped back.
        DateTime? at = record == "operator-on-record-unreadable" ? f.PromptAt.AddMinutes(9) : null;
        await AddCrashAsync(f, f.Clock.GetUtcNow().UtcDateTime.AddMinutes(-30));
        var fault = new ReceiptReadFault();
        var all = await ProjectAsync(f, fault, at);

        fault.Fired.ShouldBeTrue("control: the optional receipt read must actually fault");
        var row = all.Where(i => i.Kind == AttentionKind.LivenessProbeFailed)
            .ShouldHaveSingleItem($"{record}: the current episode still projects from its clock");
        row.Severity.ShouldBe(AlertSeverity.Warning, record);
        row.AgentId.ShouldBe(f.AgentId);
        row.SinceUtc.ShouldBe(f.PromptAt);
        row.Headline.ShouldStartWith("Standing boot stall detected");
        all.ShouldContain(i => i.Kind == AttentionKind.RecentCriticalIncident && i.Headline.Contains("Crash"),
            "the rest of the feed is still returned");
        f.LogEntries().ShouldContain(
            e => e.Level == LogLevel.Warning && e.Message.Contains("Could not read the standing boot receipts")
                && e.Exception != null,
            string.Join('\n', f.LogEntries().Select(e => $"[{e.Level}] {e.Message}")));
        f.AssertNothingDestructive();
    }

    /// <summary>
    /// Repair of Review 3fe22492 F-2. A legacy <c>bootSeq=</c> Error row on a session the standing
    /// projection covers, or on a live session an open task owns, is suppressed in the recent-incident
    /// sweep as well as on the attention feed, so the retired restart wording never reappears there.
    /// covered-due: the AlwaysOn taskless session's current episode is due (one standing row);
    /// covered-not-due: its prompt is 5 minutes old (no boot row at all); task-owned: a non-AlwaysOn
    /// session a Working task owns. In every case the same agent's legacy Error on a dead session
    /// stays ordinary recent-incident history (exactly one Error, about that session).
    /// </summary>
    [Test]
    [Arguments("covered-due")]
    [Arguments("covered-not-due")]
    [Arguments("task-owned")]
    public async Task C1156_Legacy_error_history_is_suppressed_with_its_attention_row(string owner)
    {
        var options = owner switch
        {
            "covered-not-due" => new StandingBootWatchOptions { PromptAge = TimeSpan.FromMinutes(5) },
            "task-owned" => new StandingBootWatchOptions { AlwaysOn = false },
            _ => new StandingBootWatchOptions(),
        };
        await using var f = await StandingBootWatchFixture.CreateAsync(options);
        var now = f.Clock.GetUtcNow().UtcDateTime;
        if (owner == "task-owned")
            await AddWorkingTaskAsync(f);
        var dead = await AddDeadSessionAsync(f);

        await AddIncidentAsync(f, BootReplyWatchdogService.EpisodeKey(1), AlertSeverity.Error, now.AddMinutes(-1),
            "Boot prompt confirmed at sequence 1; restart ladder engaged; stopped restarting after two.");
        await AddIncidentAsync(f, BootReplyWatchdogService.EpisodeKey(1), AlertSeverity.Error, now.AddHours(-2),
            "dead-session history: stopped restarting after two.", sessionId: dead);

        var all = await ProjectAsync(f);
        var boot = all.Where(i => i.Kind == AttentionKind.LivenessProbeFailed).ToList();
        if (owner == "covered-due")
        {
            boot.ShouldHaveSingleItem("the current episode is the one boot row").Headline
                .ShouldStartWith("Standing boot stall detected");
        }
        else
        {
            boot.ShouldBeEmpty($"{owner}: the legacy row is suppressed and no standing stage is due");
        }

        var history = all.Where(i => i.Kind == AttentionKind.RecentCriticalIncident
                && i.Headline.Contains(nameof(AgentIncidentKind.LivenessProbeFailed)))
            .ShouldHaveSingleItem($"{owner}: only the dead session's legacy Error remains history");
        history.SessionId.ShouldBe(dead, owner);
        history.Headline.ShouldBe($"{AlertSeverity.Error} {AgentIncidentKind.LivenessProbeFailed} in the last 24h.", owner);
        foreach (var item in all.Where(i => i.SessionId == f.SessionId
            && i.Kind is AttentionKind.LivenessProbeFailed or AttentionKind.RecentCriticalIncident))
        {
            foreach (var retired in RetiredWording)
            {
                (item.Headline + "\n" + item.Evidence).ShouldNotContain(retired, Case.Insensitive,
                    $"{owner}: {item.Kind} on the suppressed session shows retired wording");
            }
        }

        f.AssertNothingDestructive();
    }

    /// <summary>
    /// Repair of Review 3fe22492 F-3. Once the operator stage is on record for the episode it stays
    /// the displayed stage when the clock steps back: the real receipt is recorded at prompt+21,
    /// then the same unchanged episode is projected at prompt+9 (control, between the dues),
    /// below-boot-due (prompt+7) and before-prompt (prompt-1). A model reply still resolves it.
    /// </summary>
    [Test]
    [Arguments("below-boot-due")]
    [Arguments("before-prompt")]
    public async Task C1156_Recorded_operator_stage_survives_clock_rollback(string rollback)
    {
        await using var f = await StandingBootWatchFixture.CreateAsync();
        f.At(f.PromptAt.AddMinutes(21));
        (await f.SweepAsync()).ShouldBe(1, f.Warnings());
        (await f.ReceiptsAsync()).Select(r => r.FailureReason).ShouldContain(r => r!.EndsWith("stage=operator"),
            "control: the operator stage is on record");
        var receipts = (await f.ReceiptsAsync()).Count;

        // A clock stepped back: the attention read observes an earlier time than the receipt.
        (await LivenessRowsAsync(f, f.PromptAt.AddMinutes(9))).ShouldHaveSingleItem().Severity
            .ShouldBe(AlertSeverity.Error, "control: between the dues the recorded stage holds");

        var at = rollback == "below-boot-due" ? f.PromptAt.AddMinutes(7) : f.PromptAt.AddMinutes(-1);
        var all = await ProjectAsync(f, at: at);
        var row = all.Where(i => i.Kind == AttentionKind.LivenessProbeFailed)
            .ShouldHaveSingleItem($"{rollback}: a recorded operator stage is never hidden by a clock step");
        row.Severity.ShouldBe(AlertSeverity.Error, rollback);
        row.Headline.ShouldStartWith("Standing boot stall needs an operator decision");
        row.SinceUtc.ShouldBe(f.PromptAt);
        all.ShouldNotContain(i => i.Kind == AttentionKind.RecentCriticalIncident && i.AgentId == f.AgentId,
            "the recorded receipt is the row's own evidence");

        await f.AddEntryAsync(TranscriptKinds.AssistantText, "answered", f.PromptAt.AddSeconds(30));
        (await LivenessRowsAsync(f, at)).ShouldBeEmpty($"{rollback}: a model reply still resolves the episode");
        (await f.ReceiptsAsync()).Count.ShouldBe(receipts, "the projection never writes a receipt");
        f.AssertNothingDestructive();
    }

    /// <summary>
    /// Repair of Review 67673f16 F-4. The standing row reads only the latest prompt record's kind,
    /// sequence and time; it never matches that record against the queued request, so it makes no
    /// delivery claim either way. partial-prefix: the only UserPrompt holds the first 12 characters
    /// of the still-Pending queued body (zero complete matching records); complete-match: the
    /// UserPrompt holds the whole body (one complete matching record). The row and the sweep's
    /// receipt message read the same in both.
    /// </summary>
    [Test]
    [Arguments("partial-prefix")]
    [Arguments("complete-match")]
    public async Task C1156_Standing_row_makes_no_delivery_claim(string prompt)
    {
        await using var f = await StandingBootWatchFixture.CreateAsync();
        string body;
        await using (var db = f.Read())
        {
            body = (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.SessionId)).Body;
            if (prompt == "partial-prefix")
            {
                (await db.TranscriptEntries
                        .Where(t => t.AgentSessionId == f.SessionId && t.Kind == TranscriptKinds.UserPrompt)
                        .ExecuteUpdateAsync(u => u.SetProperty(t => t.Text, body.Substring(0, 12))))
                    .ShouldBe(1, "control: exactly the one prompt record is cut to a prefix");
            }

            var records = await db.TranscriptEntries
                .Where(t => t.AgentSessionId == f.SessionId && t.Kind == TranscriptKinds.UserPrompt)
                .Select(t => t.Text)
                .ToListAsync();
            records.Count.ShouldBe(1, "control: one prompt record");
            records.Count(t => t == body).ShouldBe(prompt == "partial-prefix" ? 0 : 1,
                $"control: {prompt} complete matching records");
        }

        (await f.SweepAsync()).ShouldBe(1, f.Warnings());
        var now = f.Clock.GetUtcNow().UtcDateTime;
        var row = (await LivenessRowsAsync(f)).ShouldHaveSingleItem(prompt);
        row.Evidence.ShouldContain($"Prompt #1 (UserPrompt) at {f.PromptAt:u}, "
            + $"{StandingBootWatchPolicy.Describe(now - f.PromptAt)} ago; no assistant, thinking, tool or "
            + "turn-end row since.\n", customMessage: "the prompt line states the record and the silence only");
        var receipt = (await f.ReceiptsAsync()).ShouldHaveSingleItem().Message;
        foreach (var text in new[] { row.Headline, row.Evidence, receipt })
        {
            foreach (var claim in DeliveryClaims)
            {
                text.ShouldNotContain(claim, Case.Insensitive,
                    $"{prompt}: the standing row cannot verify delivery, so it never asserts or denies it");
            }
        }

        f.AssertNothingDestructive();
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>Delivery verdicts only complete matching UserPrompt evidence may carry (F-4).</summary>
    private static readonly string[] DeliveryClaims =
        ["deliver", "not the problem", "received", "accepted", "reached the", "confirmed"];

    private static string ModelKind(string resolution) => resolution switch
    {
        "assistant" => TranscriptKinds.AssistantText,
        "thinking" => TranscriptKinds.Thinking,
        "tool-call" => TranscriptKinds.ToolCall,
        "tool-result" => TranscriptKinds.ToolResult,
        "turn-end" => TranscriptKinds.TurnEnd,
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, null),
    };

    /// <summary>
    /// The real <see cref="AttentionService.GetAsync"/> on this fixture's database and clock, every
    /// row about the fixture's session or agent. Asserts the read wrote nothing and asked the runner
    /// exactly the one inherited list question.
    /// </summary>
    private static async Task<List<AttentionItemDto>> ProjectAsync(
        StandingBootWatchFixture f, IInterceptor? fault = null, DateTime? at = null)
    {
        var counter = new FullCommandCounter();
        await using var db = new AppDbContext(fault is null
            ? TestDbFixture.CreateDbContextOptions(f.ConnectionString, counter)
            : TestDbFixture.CreateDbContextOptions(f.ConnectionString, counter, fault));
        var lists = f.Runner.Lists;
        var result = await AttentionServiceTests.BuildService(
                f.Runner, timeProvider: at is { } observed ? new FakeTimeProvider(new DateTimeOffset(observed, TimeSpan.Zero)) : f.Clock,
                db: db, logger: f.Logger<AttentionService>())
            .GetAsync(CancellationToken.None);
        counter.Commands.Where(IsWrite).ShouldBeEmpty("the projection is read-only:\n" + counter.Roster());
        db.ChangeTracker.HasChanges().ShouldBeFalse("nothing is staged during a GET");
        (f.Runner.Lists - lists).ShouldBe(1, "no runner call beyond GetAsync's inherited list");
        return result.Items.Where(i => i.SessionId == f.SessionId || i.AgentId == f.AgentId).ToList();
    }

    private static async Task<List<AttentionItemDto>> LivenessRowsAsync(StandingBootWatchFixture f, DateTime? at = null) =>
        (await ProjectAsync(f, at: at)).Where(i => i.Kind == AttentionKind.LivenessProbeFailed).ToList();

    private static bool IsWrite(string sql)
    {
        var head = sql.TrimStart();
        return head.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
            || head.StartsWith("MERGE", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ShiftReceiptsAsync(StandingBootWatchFixture f, TimeSpan by, IReadOnlyList<Guid>? ids = null)
    {
        await using var db = f.Read();
        var rows = await db.AgentIncidents
            .Where(i => i.SessionId == f.SessionId && i.FailureReason != null
                && i.FailureReason.StartsWith(StandingBootWatchPolicy.KeyPrefix))
            .ToListAsync();
        if (ids is not null)
            rows = rows.Where(r => ids.Contains(r.Id)).ToList();
        rows.ShouldNotBeEmpty("control: there are receipts to age");
        foreach (var row in rows)
            row.CreatedAt = StandingBootWatchFixture.Pg(row.CreatedAt + by);
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> AddIncidentAsync(
        StandingBootWatchFixture f, string failureReason, AlertSeverity severity, DateTime at, string message,
        Guid? sessionId = null)
    {
        await using var db = f.Read();
        var id = Guid.NewGuid();
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = id,
            AgentId = f.AgentId,
            SessionId = sessionId ?? f.SessionId,
            Kind = AgentIncidentKind.LivenessProbeFailed,
            Severity = severity,
            Message = message,
            FailureReason = failureReason,
            CreatedAt = StandingBootWatchFixture.Pg(at),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> AddUnrelatedAsync(StandingBootWatchFixture f, DateTime at)
    {
        await using var db = f.Read();
        var id = Guid.NewGuid();
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = id,
            AgentId = f.AgentId,
            SessionId = f.SessionId,
            Kind = AgentIncidentKind.Recovered,
            Severity = AlertSeverity.Info,
            Message = "unrelated history",
            CreatedAt = StandingBootWatchFixture.Pg(at),
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>An unrelated Error incident on the fixture's agent: its own RecentCriticalIncident row.</summary>
    private static async Task AddCrashAsync(StandingBootWatchFixture f, DateTime at)
    {
        await using var db = f.Read();
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = f.AgentId,
            SessionId = f.SessionId,
            Kind = AgentIncidentKind.Crash,
            Severity = AlertSeverity.Error,
            Message = "unrelated crash history",
            CreatedAt = StandingBootWatchFixture.Pg(at),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>An open (Working) task bound to the fixture's session.</summary>
    private static async Task AddWorkingTaskAsync(StandingBootWatchFixture f)
    {
        await using var db = f.Read();
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "open task on the session",
            Goal = "owns the boot silence",
            Role = AgentTaskRole.Code,
            AgentKind = f.Setup.Kind,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            AgentId = f.AgentId,
            AgentSessionId = f.SessionId,
            Status = AgentTaskStatus.Working,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = f.StartedAt,
            DispatchedAt = f.StartedAt,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>An earlier, ended session of the fixture's agent: its legacy rows are plain history.</summary>
    private static async Task<Guid> AddDeadSessionAsync(StandingBootWatchFixture f)
    {
        await using var db = f.Read();
        var id = Guid.NewGuid();
        var started = StandingBootWatchFixture.Pg(f.StartedAt.AddHours(-3));
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            DefinitionName = "standing-boot",
            AgentKind = f.Setup.Kind,
            Status = SessionStatus.Stopped,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = started,
            StartedAt = started,
            EndedAt = StandingBootWatchFixture.Pg(f.StartedAt.AddMinutes(-1)),
            LastSeenAt = started,
            StandingAgentId = f.AgentId,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<string[]> RemainingAsync(StandingBootWatchFixture f)
    {
        await using var db = f.Read();
        return Ids(await db.AgentIncidents.Where(i => i.AgentId == f.AgentId).Select(i => i.Id).ToListAsync());
    }

    private static string[] Ids(IEnumerable<Guid> ids) => ids.Select(i => i.ToString("D")).OrderBy(i => i).ToArray();

    private static async Task<(int Removed, List<string> Log)> PruneAsync(
        StandingBootWatchFixture f, SupervisionSettings settings, IInterceptor? fault)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"c1156-prune-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(
                tempRoot, [], supervision: settings, connectionString: f.ConnectionString,
                configureDb: fault is null ? null : o => o.AddInterceptors(fault));
            var removed = await harness.Supervisor().PruneIncidentsAsync(CancellationToken.None);
            List<string> log;
            lock (harness.SupervisorLog)
                log = [.. harness.SupervisorLog];
            harness.Runner.KillCalls.ShouldBe(0, "the prune touches no session");
            return (removed, log);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup.
            }
        }
    }

    /// <summary>Faults the receipt writer's INSERT, so the sweep's save fails and nothing is recorded.</summary>
    private sealed class IncidentInsertFault : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Throw(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Throw(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Throw(DbCommand command)
        {
            if (!command.CommandText.Contains("INSERT INTO \"AgentIncidents\"", StringComparison.Ordinal))
                return;
            Fired = true;
            throw new InvalidOperationException("c1156: injected receipt insert fault");
        }
    }

    /// <summary>Faults the attention projection's optional standing-receipt read, once.</summary>
    private sealed class ReceiptReadFault : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText;
            if (!Fired
                && sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("FROM \"AgentIncidents\"", StringComparison.Ordinal)
                && sql.Contains(StandingBootWatchPolicy.KeyPrefix, StringComparison.Ordinal))
            {
                Fired = true;
                throw new InvalidOperationException("c1156: injected standing receipt read fault");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Faults the prune's protected-key candidate read (sessions joined to standing receipts).</summary>
    private sealed class CandidateReadFault : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText;
            if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("FROM \"AgentSessions\"", StringComparison.Ordinal)
                && sql.Contains("\"AgentIncidents\"", StringComparison.Ordinal))
            {
                Fired = true;
                throw new InvalidOperationException("c1156: injected candidate read fault");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
