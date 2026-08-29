using System.Diagnostics;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0047 slice 2 — the deterministic fact bundle behind a check-in.
///
/// Two things are being held down here. First that the facts are RIGHT: running vs exited, working
/// vs idle through the shared verdict, a bounded transcript tail, real commits and real working-tree
/// counts, and — the one that matters most when git misbehaves — that a git failure SAYS SO instead
/// of reporting zero commits, which would read as "the delegate has done nothing".
///
/// Second that the probe is READ-ONLY, and not by inspection: the database it is handed refuses
/// <c>SaveChanges</c> outright, and the git index's mtime is asserted unchanged across a status
/// probe — with the bare (non-<c>--no-optional-locks</c>) call used as a control to prove the test
/// can actually see a write.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class DelegateCheckProbeTests
{
    // ---- session and task facts ---------------------------------------------------------------

    [Test]
    public async Task a_session_mid_turn_is_reported_working()
    {
        var seed = await SeedAsync();
        // A prompt above the last turn end is exactly what IsWorkingAsync calls "mid-turn".
        await SeedTranscriptAsync(seed.SessionId,
            (TranscriptKinds.TurnEnd, null, null),
            (TranscriptKinds.UserPrompt, "go on then", null));

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Session.ShouldNotBeNull();
        facts.Session!.Status.ShouldBe(SessionStatus.Running);
        facts.Session.Working.ShouldBeTrue();
        facts.Session.TranscriptEntries.ShouldBe(2);
        facts.Session.SinceLastEntry.ShouldNotBeNull();
    }

    [Test]
    public async Task a_session_at_the_prompt_is_reported_idle()
    {
        var seed = await SeedAsync();
        await SeedTranscriptAsync(seed.SessionId,
            (TranscriptKinds.UserPrompt, "do the thing", null),
            (TranscriptKinds.AssistantText, "done", null),
            (TranscriptKinds.TurnEnd, null, null));

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Session!.Working.ShouldBeFalse();
    }

    [Test]
    public async Task an_exited_session_is_reported_as_exited_not_as_missing()
    {
        // "Stopped" and "there is no session" are different diagnoses and must not collapse.
        var seed = await SeedAsync(sessionStatus: SessionStatus.Stopped);

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Session.ShouldNotBeNull();
        facts.Session!.Status.ShouldBe(SessionStatus.Stopped);
        DelegateCheckProbe.RenderDigest(facts).ShouldContain("Stopped");
    }

    [Test]
    public async Task a_task_with_no_session_row_reports_none()
    {
        var seed = await SeedAsync();
        await using var db = CreateContext();
        var detached = await db.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id);
        detached.AgentSessionId = null;

        var facts = await Probe().GatherAsync(detached, CancellationToken.None);

        facts.Session.ShouldBeNull();
        facts.TranscriptTail.ShouldBeEmpty();
        DelegateCheckProbe.RenderDigest(facts).ShouldContain("SESSION: none");
    }

    [Test]
    public async Task the_task_facts_carry_the_age_against_the_declared_duration_and_the_check_number()
    {
        var seed = await SeedAsync(dispatchedMinutesAgo: 42, expectedMinutes: 15, checkCount: 2);

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Task.ExpectedDurationMinutes.ShouldBe(15);
        facts.Task.Age!.Value.TotalMinutes.ShouldBeGreaterThanOrEqualTo(41);
        facts.Task.CheckNumber.ShouldBe(
            2,
            "the sweep counts a check when it CLAIMS it — CheckCount already includes the one now "
            + "running, so adding one numbered the first check of a task #2");
        facts.Task.Settled.ShouldBeFalse();
    }

    [Test]
    public async Task an_unclaimed_task_still_reads_as_check_one_rather_than_zero()
    {
        // The floor. Every production check arrives through a claim that has already written
        // CheckCount >= 1; an ad-hoc probe of an unclaimed row must not announce itself as "#0".
        var seed = await SeedAsync(checkCount: 0);

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Task.CheckNumber.ShouldBe(1);
    }

    [Test]
    public async Task a_settled_task_says_so_from_its_own_row()
    {
        // The point of reading settlement from the ROW: a check also catches a completion whose
        // notification never reached the caller, which is a measured failure mode.
        var seed = await SeedAsync(status: AgentTaskStatus.Succeeded, result: "Done: 12 tests pass.");

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Task.Settled.ShouldBeTrue();
        facts.Task.HasResult.ShouldBeTrue();
        DelegateCheckProbe.RenderDigest(facts).ShouldContain("SETTLED");
    }

    // ---- transcript tail ----------------------------------------------------------------------

    [Test]
    public async Task the_transcript_tail_keeps_the_last_ten_entries_in_order_and_excerpts_them()
    {
        var seed = await SeedAsync();
        var entries = new List<(string Kind, string? Text, string? Tool)>();
        for (var i = 0; i < 14; i++)
            entries.Add((TranscriptKinds.AssistantText, $"line {i} " + new string('x', 400), null));
        entries.Add((TranscriptKinds.ToolCall, null, "Bash"));
        await SeedTranscriptAsync(seed.SessionId, [.. entries]);

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.TranscriptTail.Count.ShouldBe(10, "a tail is for shape, not for reading the work");
        facts.TranscriptTail[0].Sequence.ShouldBeLessThan(
            facts.TranscriptTail[^1].Sequence, "oldest first, so it reads as a narrative");
        facts.TranscriptTail[^1].Kind.ShouldBe(TranscriptKinds.ToolCall);
        facts.TranscriptTail[^1].Excerpt.ShouldBe("Bash", "a tool call's identity is its name");
        facts.TranscriptTail[0].Excerpt!.Length.ShouldBeLessThanOrEqualTo(
            201, "excerpted at 200 characters plus the ellipsis");
    }

    // ---- git --------------------------------------------------------------------------------

    [Test]
    public async Task git_facts_report_real_commits_and_real_working_tree_counts()
    {
        using var repo = new TempRepo();
        repo.Commit("first.txt", "one", "the first commit");
        var trunk = repo.CurrentBranch();
        repo.Run("checkout", "-q", "-b", "task/thing");
        repo.Commit("second.txt", "two", "the second commit");
        repo.Write("second.txt", "two, changed");
        repo.Write("brand-new.txt", "untracked");

        var seed = await SeedAsync(
            workingDirectory: repo.Path,
            dispatchedMinutesAgo: 30,
            worktreeBranch: "task/thing",
            mergeTarget: trunk);
        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Git.ShouldNotBeNull();
        facts.Git!.Scope.ShouldBe(DelegateCheckProbe.CheckGitEvidenceScope.TaskBranch);
        facts.Git.Unavailable.ShouldBeNull();
        facts.Git.Commits.Count.ShouldBe(1, "only what the task branch added over the merge target");
        facts.Git.Commits[0].ShouldContain("the second commit", customMessage: "newest first");
        facts.Git.ChangedFiles.ShouldBe(1);
        facts.Git.UntrackedFiles.ShouldBe(1);
    }

    [Test]
    public async Task a_worktree_task_logs_the_branch_against_its_merge_target()
    {
        // Commit messages are the durable report in this repo, so "what has landed on the branch"
        // is the highest-signal fact a check can carry.
        using var repo = new TempRepo();
        repo.Commit("base.txt", "base", "on the trunk");
        var trunk = repo.CurrentBranch();
        repo.Run("checkout", "-q", "-b", "task/thing");
        repo.Commit("work.txt", "work", "the delegate's own commit");

        var seed = await SeedAsync(
            workingDirectory: repo.Path, worktreeBranch: "task/thing", mergeTarget: trunk);
        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Git!.Scope.ShouldBe(DelegateCheckProbe.CheckGitEvidenceScope.TaskBranch);
        facts.Git.Range.ShouldBe($"{trunk}..task/thing");
        facts.Git.Commits.Count.ShouldBe(1, "only what the branch ADDED");
        facts.Git.Commits[0].ShouldContain("the delegate's own commit");
    }

    [Test]
    public async Task a_shared_task_omits_foreign_commits_and_working_tree_counts()
    {
        // CARD-0227: the live incident was a Shared Plan task whose check attributed an unrelated
        // merge onto the shared HEAD (CARD-0224, 23de792) as "PRODUCED - 1 commit created". The
        // probe must not surface that commit, or any working-tree count, as this task's evidence.
        using var repo = new TempRepo();
        repo.Commit("base.txt", "base", "on the trunk before dispatch");

        var seed = await SeedAsync(
            workingDirectory: repo.Path,
            dispatchedMinutesAgo: 30,
            workspace: WorkspaceMode.Shared);

        repo.Commit(
            "unrelated.txt",
            "from another card",
            "feat(herdr): CARD-0224 S1 relaunch or adopt into the pane we already had");
        repo.Write("dirty.txt", "unrelated working-tree edit");

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Git.ShouldNotBeNull();
        facts.Git!.Scope.ShouldBe(
            DelegateCheckProbe.CheckGitEvidenceScope.SharedWorkspaceUnattributable);
        facts.Git.Unavailable.ShouldBeNull();
        facts.Git.Range.ShouldBeNull();
        facts.Git.Commits.ShouldBeEmpty();

        var digest = DelegateCheckProbe.RenderDigest(facts);
        digest.ShouldContain(DelegateCheckProbe.SharedWorkspaceUnattributableExplanation);
        digest.ShouldNotContain("CARD-0224", customMessage:
            "the foreign merge subject is exactly the fact that produced the false PRODUCED");
        digest.ShouldNotContain("commits=");
        digest.ShouldNotContain("changed=");
        digest.ShouldNotContain("untracked=");
    }

    [Test]
    public async Task a_worktree_task_does_not_see_commits_that_advanced_its_merge_target()
    {
        // The same external advancement that poisons Shared HEAD must not leak into the task
        // branch's range: only what this branch added over the merge target is task-owned.
        using var repo = new TempRepo();
        repo.Commit("base.txt", "base", "on the trunk");
        var trunk = repo.CurrentBranch();
        repo.Run("checkout", "-q", "-b", "task/thing");
        repo.Commit("work.txt", "work", "the delegate's own commit");
        repo.Run("checkout", "-q", trunk);
        repo.Commit(
            "unrelated.txt",
            "from another card",
            "feat(herdr): CARD-0224 S1 relaunch or adopt into the pane we already had");
        repo.Run("checkout", "-q", "task/thing");

        var seed = await SeedAsync(
            workingDirectory: repo.Path, worktreeBranch: "task/thing", mergeTarget: trunk);
        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);
        var digest = DelegateCheckProbe.RenderDigest(facts);

        facts.Git!.Scope.ShouldBe(DelegateCheckProbe.CheckGitEvidenceScope.TaskBranch);
        facts.Git.Range.ShouldBe($"{trunk}..task/thing");
        facts.Git.Commits.Count.ShouldBe(1);
        facts.Git.Commits[0].ShouldContain("the delegate's own commit");
        facts.Git.Commits.ShouldNotContain(c => c.Contains("CARD-0224"));
        digest.ShouldContain("commits=1");
        digest.ShouldNotContain("CARD-0224");
        digest.ShouldNotContain(DelegateCheckProbe.SharedWorkspaceUnattributableExplanation);
    }

    [Test]
    public async Task a_worktree_task_missing_its_branch_coordinates_is_unavailable_not_shared()
    {
        using var repo = new TempRepo();
        repo.Commit("base.txt", "base", "on the trunk");
        repo.Commit("later.txt", "later", "a commit that Shared --since would have claimed");

        var seed = await SeedAsync(
            workingDirectory: repo.Path,
            dispatchedMinutesAgo: 30,
            workspace: WorkspaceMode.Worktree);
        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);
        var digest = DelegateCheckProbe.RenderDigest(facts);

        facts.Git.ShouldNotBeNull();
        facts.Git!.Scope.ShouldBe(DelegateCheckProbe.CheckGitEvidenceScope.TaskBranch);
        facts.Git.Unavailable.ShouldNotBeNull();
        facts.Git.Unavailable.ShouldContain("WorktreeBranch or MergeTargetRef");
        digest.ShouldContain("unavailable");
        digest.ShouldNotContain("commits=0", customMessage:
            "a missing task-branch range must never be rendered as zero work");
        digest.ShouldNotContain(DelegateCheckProbe.SharedWorkspaceUnattributableExplanation);
        digest.ShouldNotContain("a commit that Shared --since would have claimed");
    }

    [Test]
    public async Task a_directory_that_is_not_a_repository_has_no_git_section()
    {
        using var plain = new TempWorkspace();
        var seed = await SeedAsync(workingDirectory: plain.Path);

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Git.ShouldBeNull();
        DelegateCheckProbe.RenderDigest(facts).ShouldContain("GIT: not a repository");
    }

    [Test]
    public async Task a_git_failure_says_so_rather_than_reporting_zero_commits()
    {
        // THE failure that must not be quiet. "0 commits" and "git could not answer" lead to
        // opposite conclusions about a delegate, and a probe that renders them identically is worse
        // than one that has no git section at all.
        using var repo = new TempRepo();
        repo.Commit("first.txt", "one", "the first commit");

        var seed = await SeedAsync(
            workingDirectory: repo.Path,
            worktreeBranch: "branch-that-does-not-exist",
            mergeTarget: "neither-does-this-one");
        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.Git.ShouldNotBeNull();
        facts.Git!.Scope.ShouldBe(DelegateCheckProbe.CheckGitEvidenceScope.TaskBranch);
        facts.Git.Unavailable.ShouldNotBeNull();
        facts.Git.Unavailable.ShouldContain("commit log");
        var digest = DelegateCheckProbe.RenderDigest(facts);
        digest.ShouldContain("unavailable");
        digest.ShouldNotContain("commits=0", customMessage:
            "a failure must never be rendered as a count");
        digest.ShouldNotContain(DelegateCheckProbe.SharedWorkspaceUnattributableExplanation);
    }

    // ---- delegate-side queue and incidents ----------------------------------------------------

    [Test]
    public async Task a_stranded_pending_message_and_an_open_incident_are_gathered()
    {
        var seed = await SeedAsync();
        await SeedPendingMessageAsync(seed.SessionId, "the answer you were waiting for");
        await SeedIncidentAsync(seed.SessionId, "Delivery could not be verified.");

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);

        facts.PendingMessages.ShouldHaveSingleItem().Excerpt
            .ShouldContain("the answer you were waiting for");
        facts.Incidents.ShouldHaveSingleItem().Message.ShouldContain("could not be verified");
    }

    // ---- CARD-0089: dated, de-duplicated, labelled digest --------------------------------------

    [Test]
    public async Task an_incident_carries_its_time_and_age()
    {
        var clock = FrozenClock();
        var seed = await SeedAsync(checkCount: 1);
        await SeedIncidentAsync(
            seed.SessionId, "Message delivery could not be verified.",
            createdAt: clock.GetUtcNow().UtcDateTime.AddMinutes(-28));

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe(clock).GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("14:32:00Z (28m ago)");
        digest.ShouldContain("Warning DeliveryVerificationFailed");
        digest.ShouldNotContain("0m ago", customMessage:
            "a 28-minute-old incident must not render as if it were current");
    }

    [Test]
    public async Task incidents_older_than_the_previous_check_are_not_marked_new()
    {
        var clock = FrozenClock();
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(checkCount: 3);
        await SeedIncidentAsync(seed.SessionId, "returned to the queue", createdAt: now.AddMinutes(-28));
        await SeedCheckEventAsync(seed.Task.Id, at: now.AddMinutes(-23));

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe(clock).GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("none NEW since check #2 (14:37:00Z, 23m ago)");
        digest.ShouldNotContain("NEW 14:");
    }

    [Test]
    public async Task an_incident_after_the_previous_check_is_marked_new()
    {
        var clock = FrozenClock();
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(checkCount: 3);
        await SeedIncidentAsync(seed.SessionId, "something just happened", createdAt: now.AddMinutes(-5));
        await SeedCheckEventAsync(seed.Task.Id, at: now.AddMinutes(-23));

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe(clock).GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("1 NEW since check #2 (14:37:00Z, 23m ago)");
        digest.ShouldContain("NEW 14:55:00Z (5m ago)");
    }

    [Test]
    public async Task the_first_check_says_every_incident_is_new_to_the_reader()
    {
        var clock = FrozenClock();
        var seed = await SeedAsync(checkCount: 1);
        await SeedIncidentAsync(
            seed.SessionId, "first sight of this",
            createdAt: clock.GetUtcNow().UtcDateTime.AddMinutes(-10));

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe(clock).GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("(first check — all are new to you)");
        digest.ShouldNotContain("since check #");
        digest.ShouldNotContain("NEW 14:");
    }

    [Test]
    public async Task identical_incidents_collapse_to_one_line_with_a_count()
    {
        var clock = FrozenClock();
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(checkCount: 1);
        await SeedIncidentAsync(seed.SessionId, "The message has been returned to the queue.", createdAt: now.AddMinutes(-12));
        await SeedIncidentAsync(seed.SessionId, "The message has been returned to the queue.", createdAt: now.AddMinutes(-8));

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe(clock).GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("2 on this session");
        digest.ShouldContain("DeliveryVerificationFailed ×2:");
        digest.ShouldContain("14:52:00Z (8m ago)", customMessage: "the newest timestamp of the group");
        digest.ShouldNotContain("14:48:00Z", customMessage: "the older duplicate is collapsed, not listed");
    }

    [Test]
    public async Task a_parked_message_says_so_in_the_queue_block()
    {
        var clock = FrozenClock();
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync();
        var settings = new SupervisionSettings { DeliveryVerification = { MaxDeliveryAttempts = 4 } };
        await SeedPendingMessageAsync(
            seed.SessionId, "still waiting on a human",
            sequence: 1, createdAt: now.AddMinutes(-41));
        await SeedPendingMessageAsync(
            seed.SessionId, "the parked brief",
            sequence: 2, createdAt: now.AddMinutes(-41),
            deliveryAttempts: 4, lastDeliveryStartedAt: now.AddMinutes(-38));

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe(clock, settings).GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("PARKED 4/4 attempts, last tried 38m ago");
        var parkedAt = digest.IndexOf("#2 BRIEF", StringComparison.Ordinal);
        var liveAt = digest.IndexOf("#1 BRIEF", StringComparison.Ordinal);
        parkedAt.ShouldBeGreaterThan(0);
        liveAt.ShouldBeGreaterThan(parkedAt, "parked rows sort first — the row nothing will retry decides what the reader does next");
        digest.ShouldNotContain("PARKED 3/", customMessage: "the cap is read from settings, never redefined");
    }

    [Test]
    public async Task a_slash_command_is_labelled_control_plane_and_the_brief_is_not()
    {
        var seed = await SeedAsync();
        await SeedPendingMessageAsync(
            seed.SessionId,
            "/compact This session is being handed NEW, unrelated work.",
            sequence: 1);
        await SeedPendingMessageAsync(
            seed.SessionId,
            "CARD-0089: read the plan and implement the digest.",
            sequence: 2);

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe().GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("#1 control-plane (Delegation,");
        digest.ShouldContain("#2 BRIEF (Delegation,");
    }

    [Test]
    public async Task a_spilled_brief_is_still_labelled_brief()
    {
        var seed = await SeedAsync();
        await SeedPendingMessageAsync(
            seed.SessionId,
            $"{TypedBodySpill.PointerHeadline} It is 5,771 characters — too long to type into a terminal.");

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe().GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("BRIEF (Delegation,");
        digest.ShouldContain(TypedBodySpill.PointerHeadline);
        digest.ShouldNotContain("control-plane", customMessage:
            "a spilled brief is still the brief — the pointer is not plumbing just because it is short");
    }

    [Test]
    public async Task a_tool_call_shows_its_input()
    {
        var seed = await SeedAsync();
        await SeedTranscriptAsync(seed.SessionId,
            (TranscriptKinds.ToolCall, null, "Read",
                """{"file_path":"C:\\src\\Antiphon\\server\\Application\\Services\\DelegateCheckProbe.cs"}""",
                null));

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe().GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("ToolCall Read:");
        digest.ShouldContain("file_path");
        digest.ShouldContain("DelegateCheckProbe.cs");
    }

    [Test]
    public async Task a_tool_call_without_input_renders_as_it_does_today()
    {
        var seed = await SeedAsync();
        await SeedTranscriptAsync(seed.SessionId, (TranscriptKinds.ToolCall, null, "Bash"));

        var facts = await Probe().GatherAsync(seed.Task, CancellationToken.None);
        var digest = DelegateCheckProbe.RenderDigest(facts);

        facts.TranscriptTail.ShouldHaveSingleItem().Excerpt.ShouldBe("Bash");
        facts.TranscriptTail[0].ToolInput.ShouldBeNull();
        digest.ShouldContain($"#{facts.TranscriptTail[0].Sequence} ToolCall: Bash");
        digest.ShouldNotContain("ToolCall Bash");
    }

    [Test]
    public async Task a_repeated_tool_call_collapses_to_one_line_with_a_count()
    {
        var seed = await SeedAsync();
        var input = """{"file_path":"C:\\src\\Antiphon\\README.md"}""";
        await SeedTranscriptAsync(seed.SessionId,
            (TranscriptKinds.ToolCall, null, "Read", input, null),
            (TranscriptKinds.ToolCall, null, "Read", input, null),
            (TranscriptKinds.ToolCall, null, "Read", input, null),
            (TranscriptKinds.ToolCall, null, "Read", input, null));

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe().GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("ToolCall Read ×4:");
        digest.ShouldContain("#1 ");
        digest.ShouldNotContain("#2 ");
        digest.ShouldNotContain("#3 ");
        digest.ShouldNotContain("#4 ");
    }

    [Test]
    public async Task a_failed_tool_result_is_marked()
    {
        var seed = await SeedAsync();
        await SeedTranscriptAsync(seed.SessionId,
            (TranscriptKinds.ToolResult, "The file does not exist.", null, null, true));

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe().GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("ToolResult ERROR: The file does not exist.");
    }

    // ---- CARD-0074: capture time on line 1 ----------------------------------------------------

    [Test]
    public async Task the_digest_opens_with_the_capture_timestamp()
    {
        var clock = FrozenClock();
        var now = clock.GetUtcNow().UtcDateTime;
        var seed = await SeedAsync(checkCount: 1);
        await SeedIncidentAsync(
            seed.SessionId, "Message delivery could not be verified.",
            createdAt: now.AddMinutes(-28));
        await SeedPendingMessageAsync(
            seed.SessionId, "CARD-0089: the labelled brief.",
            sequence: 1, createdAt: now.AddMinutes(-3));

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe(clock).GatherAsync(seed.Task, CancellationToken.None));

        var first = digest.Split('\n')[0];
        first.ShouldStartWith("CAPTURED 2026-08-19T15:00:00Z");
        first.ShouldContain("a snapshot of that moment, not of now");

        var taskAt = digest.IndexOf("\nTASK ", StringComparison.Ordinal);
        taskAt.ShouldBeGreaterThan(0, "the TASK line is still there, below the capture stamp");
        digest.IndexOf("CAPTURED ", StringComparison.Ordinal).ShouldBeLessThan(taskAt);

        // CARD-0089 blocks are untouched — this slice adds a line above them.
        digest.ShouldContain("14:32:00Z (28m ago)");
        digest.ShouldContain("Warning DeliveryVerificationFailed");
        digest.ShouldContain("BRIEF (Delegation,");
        digest.ShouldContain("INCIDENTS:");
        digest.ShouldContain("DELEGATE QUEUE:");
    }

    // ---- the digest ---------------------------------------------------------------------------

    [Test]
    public async Task the_digest_renders_every_section()
    {
        using var repo = new TempRepo();
        repo.Commit("first.txt", "one", "the first commit");
        var seed = await SeedAsync(workingDirectory: repo.Path, dispatchedMinutesAgo: 30);
        await SeedTranscriptAsync(seed.SessionId,
            (TranscriptKinds.UserPrompt, "do the thing", null),
            (TranscriptKinds.ToolCall, null, "Read"),
            (TranscriptKinds.TurnEnd, null, null));
        await SeedPendingMessageAsync(seed.SessionId, "still queued");
        await SeedIncidentAsync(seed.SessionId, "something happened");

        var digest = DelegateCheckProbe.RenderDigest(
            await Probe().GatherAsync(seed.Task, CancellationToken.None));

        digest.ShouldContain("TASK ");
        digest.ShouldContain("SESSION ");
        digest.ShouldContain("TRANSCRIPT TAIL");
        digest.ShouldContain("GIT (");
        digest.ShouldContain("DELEGATE QUEUE:");
        digest.ShouldContain("INCIDENTS:");
        digest.ShouldContain("expected=");
        digest.ShouldContain("check#1");
        digest.ShouldContain("Read", customMessage: "the tool the delegate ran is part of the shape");
    }

    // ---- the read-only guarantee --------------------------------------------------------------

    [Test]
    public async Task the_probe_never_writes_to_the_database()
    {
        // Not a review promise — a mechanical one. This context throws on any SaveChanges, so a
        // probe that ever grew a write would fail here rather than in production.
        using var repo = new TempRepo();
        repo.Commit("first.txt", "one", "the first commit");
        var seed = await SeedAsync(workingDirectory: repo.Path);
        await SeedTranscriptAsync(seed.SessionId, (TranscriptKinds.UserPrompt, "do it", null));
        await SeedPendingMessageAsync(seed.SessionId, "queued");
        await SeedIncidentAsync(seed.SessionId, "noted");

        await using var refusing = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(TestDbFixture.ConnectionString, o => o.MigrationsAssembly("Antiphon.Server"))
                .AddInterceptors(new RefuseToSaveInterceptor())
                .Options);

        var probe = new DelegateCheckProbe(
            refusing, new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance), TimeProvider.System,
            Options.Create(new SupervisionSettings()));

        var facts = await probe.GatherAsync(seed.Task, CancellationToken.None);

        facts.Git.ShouldNotBeNull();
        facts.TranscriptTail.ShouldNotBeEmpty();
    }

    [Test]
    public async Task a_status_probe_leaves_the_git_index_untouched()
    {
        // A bare `git status` REFRESHES the index — a write — and the workspace being inspected
        // belongs to a running delegate. The control below proves this test can see that write;
        // the assertion proves the probe's own call does not make it.
        using var repo = new TempRepo();
        repo.Commit("tracked.txt", "content", "the first commit");
        var git = new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance);

        repo.MakeIndexStatDirty("tracked.txt");
        var beforeControl = repo.IndexWrittenAt();
        await git.GetChangesAsync(repo.Path, CancellationToken.None);
        repo.IndexWrittenAt().ShouldNotBe(
            beforeControl,
            "control: a bare `git status` really does rewrite the index, so the check below means something");

        repo.MakeIndexStatDirty("tracked.txt");
        var before = repo.IndexWrittenAt();
        var counts = await git.GetWorkingTreeCountsAsync(repo.Path, CancellationToken.None);

        counts.ShouldNotBeNull();
        repo.IndexWrittenAt().ShouldBe(before, "--no-optional-locks is what makes the probe an honest read");
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static readonly DateTimeOffset FrozenNow =
        new(2026, 8, 19, 15, 0, 0, TimeSpan.Zero);

    private static FakeTimeProvider FrozenClock() => new(FrozenNow);

    private static DelegateCheckProbe Probe(TimeProvider? time = null, SupervisionSettings? supervision = null) =>
        new(CreateContext(), new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
            time ?? TimeProvider.System, Options.Create(supervision ?? new SupervisionSettings()));

    private sealed record Seeded(AgentTask Task, Guid SessionId);

    private static async Task<Seeded> SeedAsync(
        string? workingDirectory = null,
        SessionStatus sessionStatus = SessionStatus.Running,
        AgentTaskStatus status = AgentTaskStatus.Dispatched,
        int dispatchedMinutesAgo = 5,
        int expectedMinutes = 10,
        int checkCount = 0,
        string? worktreeBranch = null,
        string? mergeTarget = null,
        string? result = null,
        WorkspaceMode? workspace = null)
    {
        var sessionId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var dispatched = DateTime.UtcNow.AddMinutes(-dispatchedMinutesAgo);
        var directory = workingDirectory ?? Path.GetTempPath();

        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = sessionStatus,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = dispatched,
        });
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentSessionId = Guid.NewGuid(),
            Title = "probe test task",
            Goal = "do the probed thing",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = workspace
                ?? (worktreeBranch is null ? WorkspaceMode.Shared : WorkspaceMode.Worktree),
            WorkingDirectory = directory,
            WorktreeBranch = worktreeBranch,
            MergeTargetRef = mergeTarget,
            AgentSessionId = sessionId,
            Status = status,
            ReplyTo = AgentTaskReplyTo.Session,
            ExpectedDurationMinutes = expectedMinutes,
            CheckCount = checkCount,
            Result = result,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
            CompletedAt = AgentTaskStatus.Succeeded == status ? DateTime.UtcNow : null,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return new Seeded(task, sessionId);
    }

    private static Task SeedTranscriptAsync(
        Guid sessionId, params (string Kind, string? Text, string? Tool)[] entries) =>
        SeedTranscriptAsync(sessionId, entries.Select(e => (e.Kind, e.Text, e.Tool, (string?)null, (bool?)null)).ToArray());

    private static async Task SeedTranscriptAsync(
        Guid sessionId, params (string Kind, string? Text, string? Tool, string? ToolInput, bool? IsError)[] entries)
    {
        var at = DateTime.UtcNow.AddMinutes(-2);
        var seq = 0L;
        await using var db = CreateContext();
        foreach (var (kind, text, tool, toolInput, isError) in entries)
        {
            seq++;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = seq,
                Kind = kind,
                Uuid = $"probe-{Guid.NewGuid():N}",
                Text = text,
                ToolName = tool,
                ToolInput = toolInput,
                ToolIsError = isError,
                StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
                Timestamp = at.AddSeconds(seq),
                CreatedAt = at.AddSeconds(seq),
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task SeedPendingMessageAsync(
        Guid sessionId,
        string body,
        long sequence = 1,
        QueuedMessageOrigin origin = QueuedMessageOrigin.Delegation,
        DateTime? createdAt = null,
        int deliveryAttempts = 0,
        DateTime? lastDeliveryStartedAt = null)
    {
        await using var db = CreateContext();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = body,
            Status = QueuedMessageStatus.Pending,
            Sequence = sequence,
            Origin = origin,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddMinutes(-3),
            DeliveryAttempts = deliveryAttempts,
            LastDeliveryStartedAt = lastDeliveryStartedAt,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedIncidentAsync(
        Guid sessionId,
        string message,
        DateTime? createdAt = null,
        AgentIncidentKind kind = AgentIncidentKind.DeliveryVerificationFailed,
        AlertSeverity severity = AlertSeverity.Warning)
    {
        var name = $"probe-{Guid.NewGuid():N}"[..16];
        await using var db = CreateContext();
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = name,
            WorkingDirectory = Path.GetTempPath(),
            Details = "Probe test delegate.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agent.Id,
            SessionId = sessionId,
            Kind = kind,
            Severity = severity,
            Message = message,
            CreatedAt = createdAt ?? DateTime.UtcNow.AddMinutes(-1),
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedCheckEventAsync(Guid taskId, DateTime at)
    {
        await using var db = CreateContext();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = AgentTaskEventType.Check,
            Detail = "previous check",
            At = at,
        });
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>A context that refuses to persist anything — the read-only guarantee, mechanised.</summary>
    private sealed class RefuseToSaveInterceptor : ISaveChangesInterceptor
    {
        public InterceptionResult<int> SavingChanges(
            DbContextEventData eventData, InterceptionResult<int> result) =>
            throw new InvalidOperationException("The check probe must never write.");

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default) =>
            throw new InvalidOperationException("The check probe must never write.");
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-probe").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a stray lock must not fail the test */ }
            catch (UnauthorizedAccessException) { /* git's read-only object files */ }
        }
    }

    /// <summary>A real repository on disk — git facts are only worth testing against real git.</summary>
    private sealed class TempRepo : IDisposable
    {
        private readonly TempWorkspace _workspace = new();

        public TempRepo()
        {
            Run("init", "-q", ".");
            Run("config", "user.email", "probe@antiphon.test");
            Run("config", "user.name", "probe");
            Run("config", "commit.gpgsign", "false");
        }

        public string Path => _workspace.Path;

        public void Write(string relativePath, string content) =>
            File.WriteAllText(System.IO.Path.Combine(Path, relativePath), content);

        public void Commit(string relativePath, string content, string message)
        {
            Write(relativePath, content);
            Run("add", relativePath);
            Run("commit", "-q", "-m", message);
        }

        public string CurrentBranch() => Run("branch", "--show-current").Trim();

        /// <summary>
        /// Give a tracked file an mtime git has not cached, so the index is stat-dirty and a plain
        /// <c>git status</c> would refresh (and rewrite) it. Deterministic — no sleeping on the
        /// filesystem's timestamp granularity.
        /// </summary>
        public void MakeIndexStatDirty(string relativePath) =>
            File.SetLastWriteTimeUtc(
                System.IO.Path.Combine(Path, relativePath), DateTime.UtcNow.AddMinutes(-Random.Shared.Next(5, 500)));

        public DateTime IndexWrittenAt() =>
            File.GetLastWriteTimeUtc(System.IO.Path.Combine(Path, ".git", "index"));

        public string Run(params string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = Path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
            return stdout;
        }

        public void Dispose() => _workspace.Dispose();
    }
}
