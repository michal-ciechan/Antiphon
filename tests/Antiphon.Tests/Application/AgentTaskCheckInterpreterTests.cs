using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
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
/// CARD-0047 slice 4C — a check hands its bundle to the standing specialist and delivers what comes
/// back, or delivers today's digest and says why it could not.
///
/// <para><b>The fallback is the contract, not a convenience.</b> Disabled, unprovisioned, busy,
/// uncreatable, slow, failed, empty — every one of those still delivers the slice-3 digest, with a
/// prefix naming the reason. There is exactly ONE new path (an interpretation arrived), and each
/// test below that is not that path proves the digest still went out.</para>
///
/// <para>Two recursion guards are pinned EXPLICITLY rather than inferred from
/// <c>ReplyTo = None</c>: an interpretation task is never armed with a <c>NextCheckAt</c>, and the
/// check sweep never selects one even when a row somehow carries one. Without both, a check would
/// check a check, and each of those would create another interpretation.</para>
///
/// <para>The wait is driven by a <see cref="FakeTimeProvider"/>: the check service polls the
/// interpretation task's row on a 2 s timer, so the tests settle (or refuse to settle) that row and
/// then push the clock, rather than sleeping through a 60 s budget.</para>
///
/// <para>Both the sweep and <c>TickAsync</c> are global over the shared fixture database, so this
/// class takes <c>[NotInParallel]</c> with NO group key (CLAUDE.md) and every assertion is scoped to
/// rows it created.</para>
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AgentTaskCheckInterpreterTests
{
    // ---- the one new path ----------------------------------------------------------------------

    [Test]
    public async Task a_settled_interpretation_replaces_the_digest_in_the_note()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var run = Task.Run(() => h.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None));
        var interpretation = await h.WaitForInterpretationAsync(specialist.Id);
        const string reading =
            "On track — three commits in the last 6 minutes; tests ran green (no action needed).";
        await h.SettleInterpretationAsync(
            interpretation.Id,
            result: reading,
            costUsd: 0.0031m);
        await h.PumpClockAsync(run);

        (await run).ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);
        var note = (await h.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();
        note.ShouldStartWith("[check ", customMessage: "the envelope is untouched by the interpretation");
        var header = note.Split('\n')[0];
        header.ShouldContain(DelegationReportFormatter.Short(seed.Task.Id));
        header.ShouldContain("#");
        header.ShouldContain("elapsed ");
        header.ShouldContain("running/");
        header.ShouldContain("activity");
        header.ShouldContain(" ago");
        note.Split('\n').Skip(1).First(l => l.Length > 0).ShouldBe(reading);
        note.ShouldNotContain(AgentTaskCheckService.InterpreterDownMarker);
        note.ShouldContain("three commits in the last 6 minutes");
        note.ShouldNotContain("unverified digest", customMessage: "this one WAS read");
        note.ShouldNotContain("[antiphon-task:", customMessage:
            "marker scrubbing still applies to whatever ends up in the body");
        note.ShouldNotContain("TASK ", customMessage:
            "the interpretation REPLACES the digest in the note — the digest stays on the timeline");

        // The timeline keeps the evidence, and names what watching cost.
        await using var verify = CreateContext();
        var check = (await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == seed.Task.Id && e.Type == AgentTaskEventType.Check)
            .ToListAsync()).ShouldHaveSingleItem();
        check.Detail.ShouldContain($"interpreter: task {DelegationReportFormatter.Short(interpretation.Id)}");
        check.Detail.ShouldContain("$0.0031");
        check.Detail.ShouldContain("TASK ", customMessage: "and the digest itself, which is the evidence");
    }

    [Test]
    public void a_live_session_with_no_entry_shows_activity_never()
    {
        using var h = new Harness();
        var task = HeaderTask();
        var facts = HeaderFacts(withSession: true, sinceLastEntry: null);
        var note = h.Checks.BuildNote(task, facts, digest: "CAPTURED — digest body");

        note.Split('\n')[0].ShouldBe(
            $"[check {DelegationReportFormatter.Short(task.Id)} #3] channel tests | elapsed 11m/10m | running/working | activity never");
    }

    [Test]
    public void a_captured_entry_puts_activity_age_on_the_header()
    {
        using var h = new Harness();
        var task = HeaderTask();
        var facts = HeaderFacts(withSession: true, sinceLastEntry: TimeSpan.Zero);
        var note = h.Checks.BuildNote(
            task, facts, digest: "CAPTURED — digest body",
            interpretation: "On track — fixed a stale test pin; now verifying larger channel suites (no action needed).");

        var header = note.Split('\n')[0];
        header.ShouldBe(
            $"[check {DelegationReportFormatter.Short(task.Id)} #3] channel tests | elapsed 11m/10m | running/working | activity 0m ago");
        note.Split('\n').Skip(1).First(l => l.Length > 0)
            .ShouldBe("On track — fixed a stale test pin; now verifying larger channel suites (no action needed).");
    }

    [Test]
    public void a_note_with_no_session_does_not_invent_activity()
    {
        using var h = new Harness();
        var task = HeaderTask();
        var facts = HeaderFacts(withSession: false, sinceLastEntry: null);
        var note = h.Checks.BuildNote(task, facts, digest: "CAPTURED — digest body");

        note.Split('\n')[0].ShouldBe(
            $"[check {DelegationReportFormatter.Short(task.Id)} #3] channel tests | elapsed 11m/10m | no session");
    }

    [Test]
    public void a_check_header_after_a_reply_names_both_clocks()
    {
        using var h = new Harness();
        var task = HeaderTask();
        var now = DateTime.UtcNow;
        var facts = HeaderFacts(
            withSession: false,
            sinceLastEntry: null,
            at: now,
            dispatchedAt: now - TimeSpan.FromHours(2) - TimeSpan.FromMinutes(24),
            repliedAt: now - TimeSpan.FromSeconds(34),
            taskAge: TimeSpan.FromSeconds(34));
        var note = h.Checks.BuildNote(task, facts, digest: "CAPTURED — digest body");

        note.Split('\n')[0].ShouldBe(
            $"[check {DelegationReportFormatter.Short(task.Id)} #3] channel tests | elapsed 0m/10m | after reply; dispatched 2h24m ago | no session");
    }

    [Test]
    public void an_empty_title_renders_as_delegated_task()
    {
        using var h = new Harness();
        var task = HeaderTask();
        task.Title = "  \n  ";
        var note = h.Checks.BuildNote(task, HeaderFacts(withSession: false, sinceLastEntry: null), digest: "CAPTURED — digest body");

        var header = note.Split('\n')[0];
        header.ShouldBe(
            $"[check {DelegationReportFormatter.Short(task.Id)} #3] Delegated task | elapsed 11m/10m | no session");
        header.ShouldStartWith(AgentTaskCheckService.HeaderPrefix);
    }

    [Test]
    public void a_final_check_keeps_the_budget_phrase_on_the_bounded_first_line()
    {
        using var h = new Harness();
        var task = HeaderTask();
        task.NextCheckAt = null;
        var note = h.Checks.BuildNote(task, HeaderFacts(withSession: true, sinceLastEntry: null), digest: "CAPTURED — digest body");

        var header = note.Split('\n')[0];
        header.ShouldBe(
            $"[check {DelegationReportFormatter.Short(task.Id)} #3] channel tests | elapsed 11m/10m | running/working | activity never | final check - the 10-check budget is spent");
        header.Length.ShouldBeLessThanOrEqualTo(AgentTaskCheckService.HeaderTitleMaxChars + 160);
    }

    [Test]
    public void a_direct_api_length_title_is_clipped_at_a_word_boundary()
    {
        using var h = new Harness();
        var task = HeaderTask();
        task.Title = string.Join(" ", Enumerable.Repeat("titleword", 30));
        task.Title.Length.ShouldBe(299);
        var note = h.Checks.BuildNote(task, HeaderFacts(withSession: false, sinceLastEntry: null), digest: "CAPTURED — digest body");

        var header = note.Split('\n')[0];
        var identity = "titleword titleword titleword titleword titleword titleword...";
        identity.Length.ShouldBeLessThanOrEqualTo(AgentTaskCheckService.HeaderTitleMaxChars);
        header.ShouldBe(
            $"[check {DelegationReportFormatter.Short(task.Id)} #3] {identity} | elapsed 11m/10m | no session");
        header.ShouldNotContain("titleword titleword titleword titleword titleword titleword titleword");
        header.ShouldStartWith(AgentTaskCheckService.HeaderPrefix);
    }

    [Test]
    public async Task a_300_char_api_title_is_clipped_on_the_header()
    {
        using var h = new Harness();
        var title = new string('x', 300);
        var created = await h.Tasks.CreateAsync(
            new CreateAgentTaskRequest(Goal: "do the checked thing", Title: title, Role: AgentTaskRole.Code),
            new AgentTaskService.Caller(null, null, h.Scratch),
            CancellationToken.None);

        created.CardId.ShouldBeNull();
        var row = await h.ReloadAsync(created.Id);
        row.Title.Length.ShouldBe(300);
        var task = HeaderTask();
        task.Id = row.Id;
        task.Title = row.Title;
        var note = h.Checks.BuildNote(task, HeaderFactsFor(task), digest: "CAPTURED — digest body");

        var header = note.Split('\n')[0];
        var identity = new string('x', 61) + "...";
        header.ShouldBe(
            $"[check {DelegationReportFormatter.Short(created.Id)} #3] {identity} | elapsed 11m/10m | no session");
        header.ShouldStartWith(AgentTaskCheckService.HeaderPrefix);
        identity.Length.ShouldBe(AgentTaskCheckService.HeaderTitleMaxChars);
    }

    [Test]
    public async Task a_long_multi_line_title_through_create_is_clipped_on_the_header()
    {
        using var h = new Harness();
        var title =
            "Investigate the long-running check header dump that repeats the entire goal\n"
            + "paragraph across several lines until the first check overflows the composer";
        var created = await h.Tasks.CreateAsync(
            new CreateAgentTaskRequest(Goal: "do the checked thing", Title: title, Role: AgentTaskRole.Code),
            new AgentTaskService.Caller(null, null, h.Scratch),
            CancellationToken.None);

        created.CardId.ShouldBeNull("an unbound task still has to clip; CARD-NNNN is S3");
        var row = await h.ReloadAsync(created.Id);
        row.Title.ShouldContain('\n');
        row.Title.Length.ShouldBeGreaterThan(AgentTaskCheckService.HeaderTitleMaxChars);

        var task = HeaderTask();
        task.Id = row.Id;
        task.Title = row.Title;
        task.CardId = row.CardId;
        var note = h.Checks.BuildNote(task, HeaderFactsFor(task), digest: "CAPTURED — digest body");

        var header = note.Split('\n')[0];
        header.ShouldStartWith($"[check {DelegationReportFormatter.Short(row.Id)} #3]");
        header.ShouldContain("...");
        header.ShouldNotContain('\n');
        header.ShouldNotContain("overflows the composer");
        header.ShouldNotContain("CARD-");
        header.Length.ShouldBeLessThan(row.Title.ReplaceLineEndings(" ").Length);
        AgentTaskCheckService.ClipHeaderTitle(row.Title).Length
            .ShouldBeLessThanOrEqualTo(AgentTaskCheckService.HeaderTitleMaxChars);
        header.ShouldContain(AgentTaskCheckService.ClipHeaderTitle(row.Title));
    }

    [Test]
    public void clip_header_title_normalizes_blanks_and_hard_cuts_unbroken_text()
    {
        AgentTaskCheckService.ClipHeaderTitle(null).ShouldBe("Delegated task");
        AgentTaskCheckService.ClipHeaderTitle("").ShouldBe("Delegated task");
        AgentTaskCheckService.ClipHeaderTitle(" \n\t ").ShouldBe("Delegated task");
        AgentTaskCheckService.ClipHeaderTitle("short").ShouldBe("short");
        AgentTaskCheckService.ClipHeaderTitle(new string('a', 64)).ShouldBe(new string('a', 64));
        AgentTaskCheckService.ClipHeaderTitle(new string('a', 65))
            .ShouldBe(new string('a', 61) + "...");
        AgentTaskCheckService.ClipHeaderTitle("hello\nworld this is a short one")
            .ShouldBe("hello world this is a short one");
    }

    /// <summary>
    /// The interpretation task's own shape (§1.1, §1.6). Its own root so its cost sums into nobody's
    /// tree and the per-root ceiling keeps meaning "what the delegated work cost"; pinned to the
    /// specialist; ReplyTo=None so its answer is never delivered anywhere as a second note.
    /// </summary>
    [Test]
    public async Task the_interpretation_task_is_its_own_root_pinned_and_answers_to_nobody()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedDelegateAsync();
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var run = Task.Run(() => h.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None));
        var interpretation = await h.WaitForInterpretationAsync(specialist.Id);
        await h.SettleInterpretationAsync(interpretation.Id, "DOING — it is mid-turn.", 0m);
        await h.PumpClockAsync(run);
        await run;

        var row = await h.ReloadAsync(interpretation.Id);
        row.RootTaskId.ShouldBe(row.Id, "its own root — nobody's budget carries it");
        row.ParentTaskId.ShouldBeNull();
        row.ParentSessionId.ShouldBeNull();
        row.Depth.ShouldBe(0);
        row.Role.ShouldBe(AgentTaskRole.Check);
        row.ReplyTo.ShouldBe(AgentTaskReplyTo.None, "its answer is read off the row, never delivered");
        row.AgentId.ShouldBe(specialist.Id);
        row.ModelLevel.ShouldBe(AgentModelLevel.Low);
        row.Ephemeral.ShouldBeFalse("ephemeral would DELETE the standing specialist when this settles");
        row.WorkingDirectory.ShouldBe(specialist.WorkingDirectory);
        row.Title.ShouldContain(DelegationReportFormatter.Short(seed.Task.Id), customMessage:
            "correlation survives the board hiding it: the title names the checked task");
        row.Goal.ShouldContain("TASK ", customMessage: "the bundle is the brief");
        row.Goal.ShouldNotContain("[antiphon-task:", customMessage:
            "no live marker of anyone else's task may ride into the specialist's session");
        row.Goal.ShouldContain(CheckInterpretation.OutputFormatReminder);
        row.Goal.ShouldContain("never `blocked`");
    }

    // ---- CARD-0035 slice 5: the reading is STORED, not just delivered ---------------------------

    /// <summary>
    /// The best explanation this system produces used to be thrown away. The specialist's reading
    /// reached the caller's note (a message body nothing can query) and the interpretation task's own
    /// <c>Result</c> row (correlated to the checked task by TITLE TEXT — no FK), so no surface could
    /// answer "what did the interpreter make of THIS task". The Check event already belongs to the
    /// task, which is why storing it here needs no table and no key.
    /// </summary>
    [Test]
    public async Task the_check_event_stores_the_reading_above_the_digest()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var run = Task.Run(() => h.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None));
        var interpretation = await h.WaitForInterpretationAsync(specialist.Id);
        await h.SettleInterpretationAsync(
            interpretation.Id,
            result: "STALLED — three commits in the first 6 minutes, then 40 minutes of nothing.\n"
                + "The delegate is idle at the prompt with a finished branch and never reported.",
            costUsd: 0.0031m);
        await h.PumpClockAsync(run);
        await run;

        await using var verify = CreateContext();
        var check = (await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == seed.Task.Id && e.Type == AgentTaskEventType.Check)
            .ToListAsync()).ShouldHaveSingleItem();

        // Read back through the SAME helper the attention projection uses — a stored reading nothing
        // can find again is exactly the state this slice exists to end.
        var reading = AgentTaskCheckService.TryReadInterpretation(check.Detail);
        reading.ShouldNotBeNull();
        reading!.ShouldContain("STALLED — three commits");
        reading.ShouldContain("never reported", customMessage: "verbatim, across its own line breaks");

        check.Detail.ShouldContain("interpreter: task ", customMessage: "the cost line is unchanged");
        check.Detail.ShouldContain("TASK ", customMessage:
            "and the digest — the EVIDENCE for the reading — is still there, below it");
        check.Detail.IndexOf(AgentTaskCheckService.ReadingHeading, StringComparison.Ordinal)
            .ShouldBeLessThan(
                check.Detail.IndexOf(AgentTaskCheckService.DigestHeading, StringComparison.Ordinal),
                "the judgement reads first; the counters are what you check it against");
    }

    [Test]
    public async Task a_looks_stuck_reading_still_round_trips_through_the_checked_task_event()
    {
        // CARD-0302: LOOKS STUCK is evidence on the checked task, never the Check row's Status.
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var run = Task.Run(() => h.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None));
        var interpretation = await h.WaitForInterpretationAsync(specialist.Id);
        await h.SettleInterpretationAsync(
            interpretation.Id,
            result: "LOOKS STUCK — last tool 28m ago and the session is idle at the prompt.",
            costUsd: 0.002m);
        await h.PumpClockAsync(run);
        await run;

        await using var verify = CreateContext();
        var check = (await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == seed.Task.Id && e.Type == AgentTaskEventType.Check)
            .ToListAsync()).ShouldHaveSingleItem();
        var reading = AgentTaskCheckService.TryReadInterpretation(check.Detail);
        reading.ShouldNotBeNull();
        reading!.ShouldContain("LOOKS STUCK — last tool 28m ago");
    }

    /// <summary>
    /// A degraded check stores what it always stored, byte for byte, and reads back as "no reading".
    /// That is what makes the change retroactively harmless: every pre-slice event on the live
    /// database still parses as digest-only rather than as an empty reading.
    /// </summary>
    [Test]
    public async Task a_degraded_check_stores_the_digest_alone_and_reads_back_as_no_reading()
    {
        using var h = new Harness(s => s.CheckInterpreterMaxBacklog = 1);
        var specialist = await h.EnsureSpecialistAsync();
        await h.SeedPendingInterpretationAsync(specialist.Id, AgentTaskStatus.Queued);
        var seed = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        (await h.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        await using var verify = CreateContext();
        var check = (await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == seed.Task.Id && e.Type == AgentTaskEventType.Check)
            .ToListAsync()).ShouldHaveSingleItem();

        check.Detail.ShouldStartWith("CAPTURED ", customMessage: "digest-only (no reading heading); CARD-0074 stamps line 1");
        check.Detail.ShouldContain("TASK ");
        check.Detail.ShouldNotContain(AgentTaskCheckService.ReadingHeading);
        AgentTaskCheckService.TryReadInterpretation(check.Detail).ShouldBeNull();
    }

    /// <summary>
    /// The digest keeps its own 1800-char budget and the reading gets a separate one. Sharing would
    /// mean a long reading ate the evidence it is a reading OF, which is the half a sceptical reader
    /// actually needs. CARD-0089 raised the digest half from 900 so longer dated/collapsed lines
    /// still leave the card-thread tail inside the stored head.
    /// </summary>
    [Test]
    public void the_two_halves_are_budgeted_apart_and_the_reading_survives_a_long_digest()
    {
        var digest = string.Join("\n", Enumerable.Range(0, 400).Select(i => $"TASK line {i}"));
        var reading = new string('r', 5_000);

        var detail = AgentTaskCheckService.ComposeEventDetail(reading, "interpreter: task abcd1234, $0.0031", digest);

        detail.ShouldStartWith("interpreter: task abcd1234");
        detail.Length.ShouldBeLessThan(3_000, "bounded — the column is 4000 and both halves are capped (1800+600)");
        detail.ShouldContain("…", customMessage: "the 1800-char digest budget still truncates");
        detail.ShouldNotContain("TASK line 399", customMessage: "truncation eats the digest tail, not the reading");
        var read = AgentTaskCheckService.TryReadInterpretation(detail);
        read.ShouldNotBeNull();
        read!.ShouldStartWith("rrrr", customMessage: "the reading survives whole-ish, from its head");
        detail.IndexOf(AgentTaskCheckService.ReadingHeading).ShouldBeLessThan(
            detail.IndexOf(AgentTaskCheckService.DigestHeading),
            "the interpreter's reading stays above the digest");
        detail.ShouldContain("TASK line 0", customMessage: "and the digest still opens with its facts");
    }

    /// <summary>
    /// A reading that happens to contain the heading text must not be able to cut its own read-back
    /// short — the boundary is a parsing contract over prose nobody controls.
    /// </summary>
    [Test]
    public void a_reading_that_quotes_the_headings_still_reads_back_whole()
    {
        var detail = AgentTaskCheckService.ComposeEventDetail(
            $"It says {AgentTaskCheckService.DigestHeading} and then stops.\nSecond line.",
            eventLine: null,
            digest: "TASK deadbeef: the real digest");

        var read = AgentTaskCheckService.TryReadInterpretation(detail);
        read.ShouldNotBeNull();
        read!.ShouldContain("Second line.", customMessage: "the whole reading, not the half before a quote");
        detail.ShouldContain("TASK deadbeef", customMessage: "and the real digest is still below it");
    }

    // ---- every other path is the digest, degraded ------------------------------------------------

    [Test]
    public async Task an_interpretation_that_never_settles_degrades_and_the_queued_task_is_cancelled()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var run = Task.Run(() => h.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None));
        var interpretation = await h.WaitForInterpretationAsync(specialist.Id);
        await h.PumpClockAsync(run); // nothing settles it; the budget simply runs out

        (await run).ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered, "the note still goes out");
        var note = (await h.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();
        note.ShouldStartWith("[check ");
        note.ShouldContain("(unverified digest — interpreter unavailable: no reading within 60s)");
        note.ShouldContain("TASK ", customMessage: "and it carries the whole digest, as it always did");
        note.Split('\n')[0].ShouldContain(AgentTaskCheckService.InterpreterDownMarker,
            customMessage: "a skim of the first line has to surface that the specialist is down");
        (await h.InterpreterIncidentsAsync(specialist.Id)).ShouldHaveSingleItem().Severity
            .ShouldBe(AlertSeverity.Warning);

        (await h.ReloadAsync(interpretation.Id)).Status.ShouldBe(
            AgentTaskStatus.Canceled,
            "an interpretation that never left the queue is withdrawn — nobody will read it");
    }

    [Test]
    public async Task a_backlog_at_the_cap_degrades_without_creating_anything()
    {
        // One specialist, many delegates due at once. Past the bound a check degrades IMMEDIATELY
        // rather than waiting its full budget behind a pile.
        using var h = new Harness(s => s.CheckInterpreterMaxBacklog = 2);
        var specialist = await h.EnsureSpecialistAsync();
        await h.SeedPendingInterpretationAsync(specialist.Id, AgentTaskStatus.Queued);
        await h.SeedPendingInterpretationAsync(specialist.Id, AgentTaskStatus.Working);
        var seed = await h.SeedDelegateAsync();
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        (await h.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        var note = (await h.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();
        note.ShouldContain("(unverified digest — interpreter busy)");
        note.ShouldNotContain(AgentTaskCheckService.InterpreterDownMarker,
            customMessage: "busy is load, not a dead specialist");
        (await h.InterpretationCountAsync(specialist.Id)).ShouldBe(2, "no third one was created");
        (await h.InterpreterIncidentsAsync(specialist.Id)).ShouldBeEmpty(
            "interpreter busy must not raise CheckInterpreterUnavailable");
        (await h.InterpreterAlertsAsync(specialist.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task the_feature_switch_gives_exactly_the_slice_three_note()
    {
        // Not "degraded" — OFF. CheckInterpreterEnabled=false must be today's behaviour to the
        // byte, which is the property that makes it a safe switch to reach for at 3am.
        using var h = new Harness(s => s.CheckInterpreterEnabled = false);
        var seed = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        (await h.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        var note = (await h.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();
        note.ShouldStartWith("[check ");
        note.ShouldNotContain("unverified digest", customMessage: "no prefix — nothing was skipped");
        note.ShouldNotContain(AgentTaskCheckService.InterpreterDownMarker);
        note.ShouldContain("TASK ");
        await using var verify = CreateContext();
        (await verify.AgentTasks.AnyAsync(t => t.Role == AgentTaskRole.Check && t.CreatedAt >= h.StartedAt))
            .ShouldBeFalse("and no interpretation task was created at all");
        (await verify.Agents.AnyAsync(a => a.Slug == h.SpecialistSlug))
            .ShouldBeFalse("nor was a specialist provisioned");
    }

    [Test]
    public async Task a_provisioner_that_throws_degrades()
    {
        using var h = new Harness();
        // The specialist already exists — Ensure throws on reconcile, which is the live shape.
        // Without a row there is nowhere to hang an AgentIncident (AgentId is required).
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        (await h.BrokenProvisionerChecks().RunCheckAsync(seed.Task.Id, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        var note = (await h.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();
        note.ShouldContain("(unverified digest — interpreter unavailable: could not be provisioned)");
        note.ShouldContain("TASK ");
        note.Split('\n')[0].ShouldContain(AgentTaskCheckService.InterpreterDownMarker);
        var incident = (await h.InterpreterIncidentsAsync(specialist.Id)).ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.Kind.ShouldBe(AgentIncidentKind.CheckInterpreterUnavailable);
        incident.Message.ShouldContain("could not be provisioned");
        var alert = (await h.InterpreterAlertsAsync(specialist.Id)).ShouldHaveSingleItem();
        alert.Severity.ShouldBe(AlertSeverity.Warning);
        alert.DedupKey.ShouldBe(AgentTaskCheckService.InterpreterUnavailableDedupKey(specialist.Id));
    }

    [Test]
    [Arguments(AgentTaskStatus.Succeeded, "", "the interpretation was empty")]
    [Arguments(AgentTaskStatus.Succeeded, "   \n  ", "the interpretation was empty")]
    [Arguments(AgentTaskStatus.Failed, "never got there", "the interpretation failed")]
    public async Task a_settled_interpretation_with_nothing_usable_degrades(
        AgentTaskStatus status, string result, string expectedReason)
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var seed = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var run = Task.Run(() => h.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None));
        var interpretation = await h.WaitForInterpretationAsync(specialist.Id);
        await h.SettleInterpretationAsync(interpretation.Id, result, 0.0007m, status);
        await h.PumpClockAsync(run);
        await run;

        var note = (await h.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();
        note.ShouldContain($"(unverified digest — interpreter unavailable: {expectedReason})");
        note.ShouldContain("TASK ", customMessage: "the digest is the floor and the floor never moves");
        note.ShouldContain(AgentTaskCheckService.InterpreterDownMarker);
        (await h.InterpreterIncidentsAsync(specialist.Id)).ShouldHaveSingleItem().Severity
            .ShouldBe(AlertSeverity.Warning);
    }

    // ---- CARD-0079 slice 2: the fallback is an incident, not a parenthetical ---------------------

    /// <summary>
    /// A fleet of due checks against a dead specialist is one outage. One incident per check would
    /// bury the first finding the way CARD-0003's uncorrelated-report log did.
    /// </summary>
    [Test]
    public async Task a_burst_of_unavailable_checks_raises_one_incident_per_specialist()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var first = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(first.DelegateSessionId, first.Task.Id);
        var second = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(second.DelegateSessionId, second.Task.Id);
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var broken = h.BrokenProvisionerChecks();
        (await broken.RunCheckAsync(first.Task.Id, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);
        (await broken.RunCheckAsync(second.Task.Id, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        (await h.InterpreterIncidentsAsync(specialist.Id)).Count.ShouldBe(1,
            "two due checks in the same minute are one outage");
        (await h.InterpreterAlertsAsync(specialist.Id)).Count.ShouldBe(1);

        foreach (var caller in new[] { first.CallerSessionId, second.CallerSessionId })
        {
            var note = (await h.NotesToCallerAsync(caller)).ShouldHaveSingleItem();
            note.ShouldContain("(unverified digest — interpreter unavailable: could not be provisioned)");
            note.ShouldContain(AgentTaskCheckService.InterpreterDownMarker);
        }
    }

    [Test]
    public async Task the_unavailable_incident_re_fires_after_the_dedup_window()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var first = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(first.DelegateSessionId, first.Task.Id);
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var broken = h.BrokenProvisionerChecks();
        await broken.RunCheckAsync(first.Task.Id, CancellationToken.None);
        (await h.InterpreterIncidentsAsync(specialist.Id)).Count.ShouldBe(1);

        h.Clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        var second = await h.SeedDelegateAsync();
        await h.SeedDelegateTranscriptAsync(second.DelegateSessionId, second.Task.Id);
        await broken.RunCheckAsync(second.Task.Id, CancellationToken.None);

        (await h.InterpreterIncidentsAsync(specialist.Id)).Count.ShouldBe(2,
            "a later outage after the window is a new finding, not a silent repeat");
    }

    // ---- recursion guards ------------------------------------------------------------------------

    [Test]
    public async Task an_interpretation_task_is_never_armed_for_a_check()
    {
        // Guard one. Checks that checked checks would create an interpretation per interpretation.
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync(withLiveSession: true);
        var seed = await h.SeedDelegateAsync();
        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var run = Task.Run(() => h.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None));
        var interpretation = await h.WaitForInterpretationAsync(specialist.Id);
        await h.SettleInterpretationAsync(interpretation.Id, "DOING — mid-turn.", 0m);
        await h.PumpClockAsync(run);
        await run;

        // Through the REAL dispatch path, which is where ArmFirstCheck runs.
        await h.Dispatcher.TickAsync(CancellationToken.None);

        var row = await h.ReloadAsync(interpretation.Id);
        row.NextCheckAt.ShouldBeNull("nothing may arm a check on an interpretation");
        row.CheckCount.ShouldBe(0);
    }

    [Test]
    public async Task the_check_sweep_never_selects_an_interpretation_even_if_one_is_armed()
    {
        // Guard two, and the reason it is separate: guard one is about what we WRITE, this is about
        // what we READ. A row that somehow carried a NextCheckAt must still never be claimed.
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var armed = await h.SeedPendingInterpretationAsync(specialist.Id, AgentTaskStatus.Dispatched);
        await h.ArmCheckByHandAsync(armed.Id, h.SeedCallerSessionId);

        await h.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        h.DrainQueue().ShouldNotContain(armed.Id, "the sweep's filter excludes the Check role outright");
        (await h.ReloadAsync(armed.Id)).CheckCount.ShouldBe(0, "and no check was spent on it");
    }

    // ---- the concurrency cap ---------------------------------------------------------------------

    [Test]
    public async Task an_interpretation_dispatches_at_the_cap_while_ordinary_work_waits()
    {
        // The cap bounds concurrent Claude PROCESSES. A pinned Check task is delivered into a
        // session that is already running, so it spawns none — and a system at the cap must not
        // starve every interpretation and silently degrade all checks exactly when the operator
        // most wants eyes on the fleet.
        using var h = new Harness(s => s.MaxConcurrentTasks = 1);
        var specialist = await h.EnsureSpecialistAsync(withLiveSession: true);
        await h.SeedActiveOrdinaryTaskAsync();
        var ordinary = await h.SeedQueuedOrdinaryTaskAsync();
        var interpretation = await h.SeedPendingInterpretationAsync(specialist.Id, AgentTaskStatus.Queued);

        await h.Dispatcher.TickAsync(CancellationToken.None);

        (await h.ReloadAsync(interpretation.Id)).Status.ShouldBe(
            AgentTaskStatus.Dispatched, "the interpretation bypasses the cap — it spawns nothing");
        (await h.ReloadAsync(ordinary.Id)).Status.ShouldBe(
            AgentTaskStatus.Queued, "and the cap still holds for work that WOULD spawn a process");
    }

    // ---- the board -------------------------------------------------------------------------------

    [Test]
    public async Task the_board_hides_interpretation_rows_unless_they_are_asked_for()
    {
        using var h = new Harness();
        var specialist = await h.EnsureSpecialistAsync();
        var interpretation = await h.SeedPendingInterpretationAsync(specialist.Id, AgentTaskStatus.Queued);

        var hidden = await h.Tasks.ListAsync(interpretation.RootTaskId, null, false, CancellationToken.None);
        var shown = await h.Tasks.ListAsync(interpretation.RootTaskId, null, true, CancellationToken.None);

        hidden.ShouldBeEmpty("one row per interpreted check would bury the delegations board");
        shown.ShouldHaveSingleItem().Id.ShouldBe(interpretation.Id);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static AgentTask HeaderTask() => new()
    {
        Id = Guid.Parse("639b197e-0000-0000-0000-000000000000"),
        Title = "channel tests",
        Goal = "g",
        Kind = AgentTaskKind.Worker,
        Role = AgentTaskRole.Code,
        Workspace = WorkspaceMode.Shared,
        WorkingDirectory = Path.GetTempPath(),
        Status = AgentTaskStatus.Dispatched,
        CreatedAt = DateTime.UtcNow,
        ExpectedDurationMinutes = 10,
        NextCheckAt = DateTime.UtcNow.AddMinutes(10),
    };

    private static DelegateCheckProbe.CheckFacts HeaderFactsFor(AgentTask task) =>
        HeaderFacts(withSession: false, sinceLastEntry: null, id: task.Id, title: task.Title);

    private static DelegateCheckProbe.CheckFacts HeaderFacts(
        bool withSession,
        TimeSpan? sinceLastEntry,
        DateTime? at = null,
        DateTime? dispatchedAt = null,
        DateTime? repliedAt = null,
        TimeSpan? taskAge = null,
        Guid? id = null,
        string? title = null)
    {
        var now = at ?? DateTime.UtcNow;
        var taskId = id ?? HeaderTask().Id;
        var task = new DelegateCheckProbe.CheckTaskFacts(
            taskId,
            DelegationReportFormatter.Short(taskId),
            title ?? "channel tests",
            AgentTaskKind.Worker,
            AgentKind.ClaudeCode,
            AgentTaskRole.Code,
            AgentModelLevel.Frontier,
            AgentTaskStatus.Dispatched,
            Settled: false,
            Attempt: 1,
            MaxAttempts: 3,
            DispatchedAt: dispatchedAt ?? now.AddMinutes(-11),
            RepliedAt: repliedAt,
            Age: taskAge ?? TimeSpan.FromMinutes(11),
            ExpectedDurationMinutes: 10,
            CheckNumber: 3,
            HasResult: false,
            FailureReason: null);
        DelegateCheckProbe.CheckSessionFacts? session = withSession
            ? new(
                Guid.NewGuid(),
                SessionStatus.Running,
                Working: true,
                TranscriptEntries: sinceLastEntry is null ? 0 : 2,
                LastEntryAt: sinceLastEntry is { } age ? now - age : null,
                SinceLastEntry: sinceLastEntry)
            : null;
        return new DelegateCheckProbe.CheckFacts(
            now, task, session, [], Git: null, [], []);
    }

    private sealed record Seeded(AgentTask Task, Guid DelegateSessionId, Guid CallerSessionId);

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly string _scratch;
        private readonly DelegationSettings _settings;

        public Harness(Action<DelegationSettings>? configure = null)
        {
            _scratch = Directory.CreateTempSubdirectory("antiphon-interp-wire").FullName;
            SpecialistSlug = $"check-interp-{Guid.NewGuid():N}"[..24];
            _settings = new DelegationSettings
            {
                MaxConcurrentTasks = 512,
                CheckInterpreterAgentSlug = SpecialistSlug,
                CheckInterpreterWorkingDirectory = _scratch,
                // The sweeps are global over the shared fixture database; keep this harness's tick
                // away from rows it did not create.
                RolePolicy = new(StringComparer.OrdinalIgnoreCase),
                FinalMessageGraceSeconds = 0,
                SubagentGraceMinutes = 0,
                PoolIdleRetireMinutes = 525_600,
                PoolMaxIdlePerDirectory = int.MaxValue,
            };
            configure?.Invoke(_settings);

            Clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            StartedAt = Clock.GetUtcNow().UtcDateTime;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddSingleton(Options.Create(_settings));
            services.AddOptions<AgentRegistrySettings>().Configure(s =>
            {
                s.DefaultDefinition = "claude";
                s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            });
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<ISessionRunnerClient, BridgeQueueHarness.EmptyRunnerClient>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-interp-wt"),
            });
            services.AddScoped<AgentTaskService>();
            services.AddSingleton<AgentTaskCheckQueue>();
            services.AddScoped<DelegateCheckProbe>();
            // No AgentControlService: the provisioner's start is best-effort and optional, and this
            // harness never launches anything.
            services.AddScoped<CheckInterpreterProvisioner>();
            services.AddScoped<IAlertService, AlertService>();
            services.AddScoped<IAlertRouter, NullAlertRouter>();
            services.AddScoped<AgentTaskCheckService>();
            services.AddSingleton<AgentTaskReplyService>();
            services.AddScoped<AgentTaskDispatcher>();

            _provider = services.BuildServiceProvider();
            Dispatcher = _provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            Checks = _provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskCheckService>();
            Tasks = _provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskService>();
            Queue = _provider.GetRequiredService<AgentTaskCheckQueue>();
        }

        public FakeTimeProvider Clock { get; }
        public DateTime StartedAt { get; }
        public string Scratch => _scratch;
        public string SpecialistSlug { get; }
        public AgentTaskDispatcher Dispatcher { get; }
        public AgentTaskCheckService Checks { get; }
        public AgentTaskService Tasks { get; }
        public AgentTaskCheckQueue Queue { get; }
        public Guid SeedCallerSessionId { get; private set; }

        public void Dispose()
        {
            _provider.Dispose();
            try { Directory.Delete(_scratch, recursive: true); }
            catch (IOException) { }
        }

        /// <summary>A check service whose provisioner is built over a DISPOSED context, so it throws.</summary>
        public AgentTaskCheckService BrokenProvisionerChecks()
        {
            var dead = CreateContext();
            dead.Dispose();
            return new AgentTaskCheckService(
                CreateContext(),
                _provider.CreateScope().ServiceProvider.GetRequiredService<DelegateCheckProbe>(),
                _provider.GetRequiredService<SessionMessageQueueService>(),
                Options.Create(_settings),
                new MockEventBus(),
                Clock,
                NullLogger<AgentTaskCheckService>.Instance,
                ptyProfile: null,
                interpreter: new CheckInterpreterProvisioner(
                    dead, Options.Create(_settings), Clock,
                    NullLogger<CheckInterpreterProvisioner>.Instance),
                alerts: _provider.CreateScope().ServiceProvider.GetRequiredService<IAlertService>());
        }

        public async Task<List<AgentIncident>> InterpreterIncidentsAsync(Guid specialistId)
        {
            await using var db = CreateContext();
            return await db.AgentIncidents.AsNoTracking()
                .Where(i => i.AgentId == specialistId
                    && i.Kind == AgentIncidentKind.CheckInterpreterUnavailable
                    && i.CreatedAt >= StartedAt)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Alert>> InterpreterAlertsAsync(Guid specialistId)
        {
            await using var db = CreateContext();
            var key = AgentTaskCheckService.InterpreterUnavailableDedupKey(specialistId);
            return await db.Alerts.AsNoTracking()
                .Where(a => a.AgentId == specialistId && a.DedupKey == key && a.CreatedAt >= StartedAt)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();
        }

        public async Task<Agent> EnsureSpecialistAsync(bool withLiveSession = false)
        {
            await using var scope = _provider.CreateAsyncScope();
            var agent = await scope.ServiceProvider.GetRequiredService<CheckInterpreterProvisioner>()
                .EnsureAsync(CancellationToken.None);
            agent.ShouldNotBeNull();

            if (!withLiveSession)
                return agent;

            var sessionId = Guid.NewGuid();
            await using var db = CreateContext();
            db.AgentSessions.Add(NewSession(sessionId, SessionStatus.Running, DateTime.UtcNow, _scratch));
            var row = await db.Agents.SingleAsync(a => a.Id == agent.Id);
            row.PersistentSessionId = sessionId.ToString("D");
            await db.SaveChangesAsync();
            return await db.Agents.AsNoTracking().SingleAsync(a => a.Id == agent.Id);
        }

        /// <summary>Real-time wait for the row the check service creates on its own thread.</summary>
        public async Task<AgentTask> WaitForInterpretationAsync(Guid specialistId)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                await using var db = CreateContext();
                var row = await db.AgentTasks.AsNoTracking()
                    .Where(t => t.AgentId == specialistId
                        && t.Role == AgentTaskRole.Check
                        && t.CreatedAt >= StartedAt
                        && t.Status == AgentTaskStatus.Queued)
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefaultAsync();
                if (row is not null)
                    return row;
                await Task.Delay(25);
            }

            throw new TimeoutException("The check never created an interpretation task.");
        }

        /// <summary>
        /// Push the check service's 2 s poll timer until its run completes. Real time only enters
        /// through the small yield that lets the timer continuation run.
        /// </summary>
        public async Task PumpClockAsync(Task run)
        {
            for (var spins = 0; !run.IsCompleted && spins < 400; spins++)
            {
                Clock.Advance(TimeSpan.FromSeconds(2));
                await Task.Delay(15);
            }

            if (!run.IsCompleted)
                throw new TimeoutException("RunCheckAsync never finished waiting for its interpretation.");
        }

        public async Task SettleInterpretationAsync(
            Guid id, string result, decimal costUsd,
            AgentTaskStatus status = AgentTaskStatus.Succeeded)
        {
            await using var db = CreateContext();
            var row = await db.AgentTasks.SingleAsync(t => t.Id == id);
            row.Status = status;
            row.Result = result;
            row.CostUsd = costUsd;
            row.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task<int> InterpretationCountAsync(Guid specialistId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.CountAsync(
                t => t.AgentId == specialistId && t.Role == AgentTaskRole.Check);
        }

        public List<Guid> DrainQueue()
        {
            var ids = new List<Guid>();
            while (Queue.TryDequeue(out var id))
                ids.Add(id);
            return ids;
        }

        public async Task<AgentTask> ReloadAsync(Guid taskId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        }

        public async Task<IReadOnlyList<string>> NotesToCallerAsync(Guid callerSessionId)
        {
            await using var db = CreateContext();
            return await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == callerSessionId && m.Origin == QueuedMessageOrigin.Check)
                .OrderBy(m => m.Sequence)
                .Select(m => m.Body)
                .ToListAsync();
        }

        public async Task<Seeded> SeedDelegateAsync(int expectedMinutes = 10)
        {
            var callerSessionId = Guid.NewGuid();
            var delegateSessionId = Guid.NewGuid();
            var id = Guid.NewGuid();
            var dispatched = DateTime.UtcNow.AddMinutes(-expectedMinutes - 1);
            SeedCallerSessionId = callerSessionId;

            await using var db = CreateContext();
            db.AgentSessions.Add(NewSession(callerSessionId, SessionStatus.Running, dispatched, Path.GetTempPath()));
            db.AgentSessions.Add(NewSession(delegateSessionId, SessionStatus.Running, dispatched, Path.GetTempPath()));
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                ParentSessionId = callerSessionId,
                ReplyTo = AgentTaskReplyTo.Session,
                Title = "check interpreter test delegate",
                Goal = "do the checked thing",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                AgentSessionId = delegateSessionId,
                Status = AgentTaskStatus.Dispatched,
                ExpectedDurationMinutes = expectedMinutes,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
                NextCheckAt = DateTime.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync();

            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
            return new Seeded(task, delegateSessionId, callerSessionId);
        }

        public async Task SeedDelegateTranscriptAsync(Guid sessionId, Guid taskId)
        {
            var at = DateTime.UtcNow.AddMinutes(-4);
            await using var db = CreateContext();
            db.TranscriptEntries.AddRange(
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 1,
                    Kind = TranscriptKinds.UserPrompt,
                    Uuid = $"delegate-{Guid.NewGuid():N}",
                    Role = "user",
                    Text = DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the checked thing.",
                    Timestamp = at,
                    CreatedAt = at,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 2,
                    Kind = TranscriptKinds.AssistantText,
                    Uuid = $"delegate-{Guid.NewGuid():N}",
                    Role = "assistant",
                    Text = "Reading the spec first.",
                    Timestamp = at.AddSeconds(5),
                    CreatedAt = at.AddSeconds(5),
                });
            await db.SaveChangesAsync();
        }

        /// <summary>An interpretation task already on the specialist — the backlog and cap fixtures.</summary>
        public async Task<AgentTask> SeedPendingInterpretationAsync(Guid specialistId, AgentTaskStatus status)
        {
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "check #1 on task deadbeef",
                Goal = "interpret this bundle",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Check,
                ReplyTo = AgentTaskReplyTo.None,
                ModelLevel = AgentModelLevel.Low,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = _scratch,
                AgentId = specialistId,
                Ephemeral = false,
                Status = status,
                CreatedAt = DateTime.UtcNow.AddSeconds(-90),
                DispatchedAt = status == AgentTaskStatus.Queued ? null : DateTime.UtcNow.AddSeconds(-60),
            };
            await using var db = CreateContext();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        /// <summary>Arm a check on a row by hand — the sweep filter's negative control.</summary>
        public async Task ArmCheckByHandAsync(Guid taskId, Guid callerSessionId)
        {
            await using var db = CreateContext();
            if (callerSessionId == Guid.Empty)
            {
                callerSessionId = Guid.NewGuid();
                db.AgentSessions.Add(NewSession(
                    callerSessionId, SessionStatus.Running, DateTime.UtcNow, Path.GetTempPath()));
            }

            var row = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            row.ReplyTo = AgentTaskReplyTo.Session;
            row.ParentSessionId = callerSessionId;
            row.NextCheckAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        public async Task SeedActiveOrdinaryTaskAsync()
        {
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "already running",
                Goal = "already running",
                Role = AgentTaskRole.Docs,
                ReplyTo = AgentTaskReplyTo.None,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = _scratch,
                Status = AgentTaskStatus.Working,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                DispatchedAt = DateTime.UtcNow.AddMinutes(-5),
            });
            await db.SaveChangesAsync();
        }

        public async Task<AgentTask> SeedQueuedOrdinaryTaskAsync()
        {
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "waiting for a slot",
                Goal = "waiting for a slot",
                Role = AgentTaskRole.Docs,
                ReplyTo = AgentTaskReplyTo.None,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = _scratch,
                Status = AgentTaskStatus.Queued,
                CreatedAt = DateTime.UtcNow,
            };
            await using var db = CreateContext();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        private static AgentSession NewSession(Guid id, SessionStatus status, DateTime at, string cwd) => new()
        {
            Id = id,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = status,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = at,
            StartedAt = at,
            LastSeenAt = at,
        };
    }
}
