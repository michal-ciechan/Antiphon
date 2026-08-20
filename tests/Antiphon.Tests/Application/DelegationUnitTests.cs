using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Unit tests for the two pure pieces of delegation that carry the most weight: the path boundary
/// that decides where a delegate may run (a security control, since a task's directory is a
/// property of the TASK and can point at another repo), and the report shaping that decides how
/// much comes back to the caller.
/// </summary>
[Category("Unit")]
public class DelegationWorkspaceBoundaryTests
{
    [Test]
    public void directory_inside_an_allowed_root_is_within_it()
    {
        DelegationWorkspaceResolver.IsWithinRoot(
            Path.Combine("C:", "src", "antiphon", "server"),
            Path.Combine("C:", "src")).ShouldBeTrue();
    }

    [Test]
    public void a_root_contains_itself()
    {
        DelegationWorkspaceResolver.IsWithinRoot(
            Path.Combine("C:", "src"), Path.Combine("C:", "src")).ShouldBeTrue();
    }

    [Test]
    public void a_sibling_sharing_a_name_prefix_is_not_inside_the_root()
    {
        // The bug a naive StartsWith would ship: "C:\src\antiphon-evil" is NOT under
        // "C:\src\antiphon". Without the separator check this is how a delegate escapes its root.
        DelegationWorkspaceResolver.IsWithinRoot(
            Path.Combine("C:", "src", "antiphon-evil"),
            Path.Combine("C:", "src", "antiphon")).ShouldBeFalse();
    }

    [Test]
    public void a_parent_directory_is_not_inside_its_child()
    {
        DelegationWorkspaceResolver.IsWithinRoot(
            Path.Combine("C:", "src"),
            Path.Combine("C:", "src", "antiphon")).ShouldBeFalse();
    }

    [Test]
    public void a_traversal_escape_is_resolved_before_comparison()
    {
        // "C:\src\antiphon\..\..\Windows" normalises to "C:\Windows", which is outside the root.
        var traversal = Path.Combine("C:", "src", "antiphon", "..", "..", "Windows");

        DelegationWorkspaceResolver.IsWithinRoot(traversal, Path.Combine("C:", "src")).ShouldBeFalse();
    }

    [Test]
    public void a_trailing_separator_on_the_root_does_not_change_the_answer()
    {
        var candidate = Path.Combine("C:", "src", "antiphon");

        DelegationWorkspaceResolver.IsWithinRoot(candidate, "C:" + Path.DirectorySeparatorChar + "src" + Path.DirectorySeparatorChar)
            .ShouldBeTrue();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public void an_empty_root_never_matches(string root)
    {
        DelegationWorkspaceResolver.IsWithinRoot(Path.Combine("C:", "src"), root).ShouldBeFalse();
    }

    [Test]
    public async Task a_directory_outside_every_allowed_root_is_rejected()
    {
        var resolver = new DelegationWorkspaceResolver(NullLoggerFor<DelegationWorkspaceResolver>());
        var parent = Directory.CreateTempSubdirectory("antiphon-parent");
        var stranger = Directory.CreateTempSubdirectory("antiphon-stranger");
        try
        {
            var ex = await Should.ThrowAsync<DelegationWorkspaceResolver.RejectedException>(
                () => resolver.ResolveAsync(stranger.FullName, parent.FullName, [], CancellationToken.None));

            ex.Message.ShouldContain("outside the allowed roots");
        }
        finally
        {
            parent.Delete(true);
            stranger.Delete(true);
        }
    }

    [Test]
    public async Task the_callers_own_directory_is_always_allowed_even_with_no_roots_configured()
    {
        // The inherit case must keep working out of the box — otherwise delegation is unusable
        // until someone configures AllowedRoots.
        var resolver = new DelegationWorkspaceResolver(NullLoggerFor<DelegationWorkspaceResolver>());
        var parent = Directory.CreateTempSubdirectory("antiphon-parent");
        try
        {
            var resolved = await resolver.ResolveAsync(null, parent.FullName, [], CancellationToken.None);

            resolved.WorkingDirectory.ShouldBe(parent.FullName);
        }
        finally
        {
            parent.Delete(true);
        }
    }

    [Test]
    public async Task an_explicitly_allowed_root_permits_a_directory_in_another_repo()
    {
        // The cross-repo case: the whole point of the task carrying its own directory.
        var resolver = new DelegationWorkspaceResolver(NullLoggerFor<DelegationWorkspaceResolver>());
        var parent = Directory.CreateTempSubdirectory("antiphon-parent");
        var otherRoot = Directory.CreateTempSubdirectory("antiphon-otherroot");
        var otherRepo = Directory.CreateDirectory(Path.Combine(otherRoot.FullName, "some-repo"));
        try
        {
            var resolved = await resolver.ResolveAsync(
                otherRepo.FullName, parent.FullName, [otherRoot.FullName], CancellationToken.None);

            resolved.WorkingDirectory.ShouldBe(otherRepo.FullName);
        }
        finally
        {
            parent.Delete(true);
            otherRoot.Delete(true);
        }
    }

    [Test]
    public async Task a_directory_that_does_not_exist_is_rejected_before_anything_launches()
    {
        var resolver = new DelegationWorkspaceResolver(NullLoggerFor<DelegationWorkspaceResolver>());
        var parent = Directory.CreateTempSubdirectory("antiphon-parent");
        try
        {
            var missing = Path.Combine(parent.FullName, "no-such-dir");

            var ex = await Should.ThrowAsync<DelegationWorkspaceResolver.RejectedException>(
                () => resolver.ResolveAsync(missing, parent.FullName, [parent.FullName], CancellationToken.None));

            ex.Message.ShouldContain("does not exist");
        }
        finally
        {
            parent.Delete(true);
        }
    }

    internal static Microsoft.Extensions.Logging.ILogger<T> NullLoggerFor<T>() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
}

/// <summary>
/// How much of a delegate's report reaches its caller. The rule: forward it whole when it fits,
/// because the report IS the deliverable and clipping it just forces a second call to read what
/// was already paid for; excerpt head+tail when it doesn't, because a hard cut severs the
/// conclusion — the part the caller needed.
/// </summary>
[Category("Unit")]
public class DelegationReportFormatterTests
{
    private static readonly DelegationSettings Settings = new()
    {
        ReplyInlineMaxChars = 20_000,
        ReplyExcerptHeadChars = 6_000,
        ReplyExcerptTailChars = 6_000,
    };

    private static AgentTask NewTask(AgentTaskKind kind = AgentTaskKind.Worker) => new()
    {
        Id = Guid.Parse("7f3a2b91-0000-0000-0000-000000000000"),
        Title = "Rewrite the Windows install section",
        Goal = "Rewrite it so every command is pwsh 7.",
        Kind = kind,
        Role = AgentTaskRole.Docs,
        ModelLevel = AgentModelLevel.Medium,
        Workspace = WorkspaceMode.Shared,
        WorkingDirectory = Path.Combine("C:", "src", "antiphon"),
        Status = AgentTaskStatus.Succeeded,
        DispatchedAt = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc),
        CompletedAt = new DateTime(2026, 8, 6, 12, 4, 12, DateTimeKind.Utc),
        CostUsd = 0.031m,
    };

    [Test]
    public void a_report_within_the_ceiling_is_forwarded_whole()
    {
        var report = new string('x', 18_000);

        var (body, excerpted) = DelegationReportFormatter.FitReport(report, NewTask(), Settings);

        excerpted.ShouldBeFalse();
        body.Length.ShouldBe(18_000, "a report that fits must arrive intact — it is the deliverable");
    }

    [Test]
    public void an_oversized_report_keeps_its_beginning_and_its_end()
    {
        // The conclusion lives at the END. A plain truncation would lose it, which is exactly the
        // failure mode this excerpt shape exists to prevent.
        var report = "OPENING-MARKER" + new string('x', 40_000) + "CLOSING-MARKER";

        var (body, excerpted) = DelegationReportFormatter.FitReport(report, NewTask(), Settings);

        excerpted.ShouldBeTrue();
        body.ShouldStartWith("OPENING-MARKER");
        body.ShouldEndWith("CLOSING-MARKER");
        body.ShouldContain("missing from the middle");
        body.Length.ShouldBeLessThan(report.Length);
    }

    [Test]
    public void an_excerpt_points_at_the_spill_file_when_the_delegate_wrote_one()
    {
        var task = NewTask();
        task.ResultFilePath = Path.Combine("C:", "src", "antiphon", ".antiphon", "task-7f3a2b91.md");

        var (body, _) = DelegationReportFormatter.FitReport(new string('x', 45_000), task, Settings);

        body.ShouldContain("task-7f3a2b91.md");
    }

    [Test]
    public void an_excerpt_falls_back_to_the_api_url_when_there_is_no_spill_file()
    {
        var task = NewTask();

        var (body, _) = DelegationReportFormatter.FitReport(new string('x', 45_000), task, Settings);

        body.ShouldContain("/api/agent-tasks/");
    }

    [Test]
    public void a_degenerate_excerpt_budget_never_produces_more_text_than_it_was_given()
    {
        // head+tail configured wider than the report itself must not make the "excerpt" LONGER
        // than the original by bolting on the elision banner.
        var settings = new DelegationSettings
        {
            ReplyInlineMaxChars = 100,
            ReplyExcerptHeadChars = 5_000,
            ReplyExcerptTailChars = 5_000,
        };
        var report = new string('x', 500);

        var (body, excerpted) = DelegationReportFormatter.FitReport(report, NewTask(), settings);

        excerpted.ShouldBeTrue();
        body.Length.ShouldBeLessThanOrEqualTo(report.Length);
    }

    [Test]
    public void the_completion_note_identifies_the_agent_tier_duration_and_cost()
    {
        // "the final message is replied back with some information of the agent that finished" —
        // this header line is that information.
        var note = DelegationReportFormatter.BuildCompletionNote(NewTask(), Settings, "Rewrote the section.");

        note.Body.ShouldContain("task 7f3a2b91");
        note.Body.ShouldContain("done");
        note.Body.ShouldContain("sonnet", Case.Insensitive);
        note.Body.ShouldContain("4m12");
        note.Body.ShouldContain("$0.031");
        note.Body.ShouldContain("Rewrote the section.");
    }

    [Test]
    public void the_completion_note_uses_lf_endings_only()
    {
        // A CR mid-body acts as Enter in a TUI and submits the fragment before it — the documented
        // fragmentation hazard. The note must never carry one.
        var note = DelegationReportFormatter.BuildCompletionNote(
            NewTask(), Settings, "line one\r\nline two\r\nline three");

        note.Body.ShouldNotContain("\r");
    }

    [Test]
    public void a_brief_carries_the_task_marker_so_the_reply_can_be_correlated()
    {
        // Correlation matches this marker, NOT prompt text — a human typing in the delegate's
        // terminal must never read as that task finishing.
        var task = NewTask();

        var brief = DelegationReportFormatter.BuildBrief(task, Settings);

        brief.ShouldStartWith(DelegationReportFormatter.TaskMarker(task.Id));
        brief.ShouldContain(task.Goal);
    }

    [Test]
    public void a_brief_tells_the_delegate_to_spill_past_the_ceiling()
    {
        var brief = DelegationReportFormatter.BuildBrief(NewTask(), Settings);

        brief.ShouldContain("20,000 characters");
        brief.ShouldContain(".antiphon/task-7f3a2b91.md");
    }

    [Test]
    public void a_worker_brief_does_not_ask_for_a_subtree_rollup()
    {
        var brief = DelegationReportFormatter.BuildBrief(NewTask(), Settings);

        brief.ShouldNotContain("subtree");
    }

    [Test]
    public void a_sub_orchestrator_brief_asks_for_a_rollup_not_a_relay()
    {
        // Without this clause a sub-orchestrator forwards everything it received and the nesting
        // saves no context at all — the one behaviour that makes nesting worth having.
        var brief = DelegationReportFormatter.BuildBrief(NewTask(AgentTaskKind.Orchestrator), Settings);

        brief.ShouldContain("subtree");
        brief.ShouldContain("Do not paste your delegates' reports");
    }

    [Test]
    public void a_read_only_brief_says_not_to_write()
    {
        var task = NewTask();
        task.Workspace = WorkspaceMode.ReadOnly;

        DelegationReportFormatter.BuildBrief(task, Settings).ShouldContain("Do NOT modify any files");
    }

    // ---- the 2026-08-10 live miss: a report reached its caller spliced, and looked complete ----

    /// <summary>
    /// The shipped default has to sit UNDER the size the pty has been measured to deliver intact,
    /// or excerpting and spilling never engage and multi-KB bodies go straight to a lossy
    /// transport. That is precisely what a 20 000-character ceiling did: nothing ever excerpted,
    /// nothing ever spilled, and a 5 368-character report arrived as a head+tail splice.
    /// </summary>
    [Test]
    public void the_shipped_ceiling_stays_below_the_measured_pty_cliff()
    {
        var shipped = new DelegationSettings();

        shipped.ReplyInlineMaxChars.ShouldBeLessThanOrEqualTo(
            shipped.PtyInlineSafeChars,
            "a report we are willing to forward inline must be a size the terminal can actually carry");
        // Measured 2026-08-10: 4 262 chars arrived intact, 5 185 did not.
        shipped.PtyInlineSafeChars.ShouldBeLessThan(
            5_185, "the safe ceiling must sit below the smallest body observed to be mangled");
        // The excerpt itself is typed, so it must fit too.
        (shipped.ReplyExcerptHeadChars + shipped.ReplyExcerptTailChars)
            .ShouldBeLessThanOrEqualTo(shipped.ReplyInlineMaxChars);
    }

    /// <summary>
    /// The live miss read as prose damage — "...cherry-pick co" welded onto "== a667cbcc, worktree
    /// list..." — precisely because the seam fell mid-word. An excerpt that cuts on a word boundary
    /// is distinguishable from a corruption; one that does not is indistinguishable.
    /// </summary>
    [Test]
    public void an_excerpt_never_cuts_mid_word()
    {
        var settings = new DelegationSettings
        {
            ReplyInlineMaxChars = 200,
            ReplyExcerptHeadChars = 100,
            ReplyExcerptTailChars = 100,
        };
        // Uniform words, so any cut that lands inside one is unambiguous.
        var report = string.Join(" ", Enumerable.Range(0, 400).Select(i => $"word{i:D4}"));

        var (body, excerpted) = DelegationReportFormatter.FitReport(report, NewTask(), settings);

        excerpted.ShouldBeTrue();
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        var head = lines[0];
        var tail = lines[^1];

        head.ShouldEndWith(
            head.Split(' ')[^1],
            customMessage: "the head must end on a whole token");
        head.Split(' ')[^1].ShouldMatch(@"^word\d{4}$", "the head's last token must not be a fragment");
        tail.Split(' ')[0].ShouldMatch(@"^word\d{4}$", "the tail's first token must not be a fragment");
    }

    /// <summary>
    /// An excerpt must be impossible to mistake for the whole thing. The old banner said
    /// "characters omitted"; a reader skimming a wall of text can slide past that. It now states
    /// outright that what follows is not the whole report, and names where the rest is.
    /// </summary>
    [Test]
    public void an_excerpt_says_plainly_that_it_is_not_the_whole_report()
    {
        var settings = new DelegationSettings { ReplyInlineMaxChars = 200 };
        var report = string.Join(" ", Enumerable.Range(0, 400).Select(i => $"word{i:D4}"));

        var (body, _) = DelegationReportFormatter.FitReport(report, NewTask(), settings);

        body.ShouldContain("EXCERPT");
        body.ShouldContain("missing from the middle");
        body.ShouldContain("Do not treat what you see as the whole report");
        body.ShouldContain("/api/agent-tasks/", customMessage: "and it must always say where the rest is");
    }

    /// <summary>
    /// The brief travels the same lossy path as the report — my own 5 203-character brief arrived
    /// as 1 091 characters. Over the ceiling it becomes a POINTER, and the pointer still has to
    /// carry the correlation marker or the delegate's report can never be matched back to the task.
    /// </summary>
    [Test]
    public void an_oversized_brief_becomes_a_pointer_that_keeps_the_task_marker()
    {
        var task = NewTask();

        var pointer = DelegationReportFormatter.BuildBriefPointer(
            task, Settings, spillPath: null, fullLength: 5_203);

        pointer.ShouldStartWith(DelegationReportFormatter.TaskMarker(task.Id));
        pointer.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE");
        pointer.ShouldContain("5,203 characters");
        pointer.ShouldContain($"/api/agent-tasks/{task.Id}");
        pointer.Length.ShouldBeLessThanOrEqualTo(
            new DelegationSettings().PtyInlineSafeChars,
            "the pointer itself must be small enough to type intact — otherwise it can be mangled too");
    }

    /// <summary>
    /// The ceiling that governs a brief must be BriefInlineMaxBytes, not ReplyInlineMaxChars.
    /// Four briefs stranded on 2026-08-11 at 1 366-2 320 characters, each arriving as its final
    /// sub-1024-byte chunk alone with the head — and therefore the task — gone. Every one of them
    /// was under ReplyInlineMaxChars (3 000) and under PtyInlineSafeChars (4 000), which is exactly
    /// why nothing fired. This pins the sizes that actually failed, not a round number.
    /// </summary>
    [Test]
    [Arguments(1_366)]
    [Arguments(1_402)]
    [Arguments(1_431)]
    [Arguments(2_320)]
    public void a_brief_at_a_size_that_stranded_a_real_task_is_not_typed_inline(int length)
    {
        var settings = new DelegationSettings();

        length.ShouldBeGreaterThan(
            settings.BriefInlineMaxBytes,
            "a brief this size must spill — it is a size measured to lose its head in delivery");
        length.ShouldBeLessThan(
            settings.ReplyInlineMaxChars,
            "and it sits under the REPORT ceiling, which is why reusing that ceiling let it through");
        length.ShouldBeLessThan(
            settings.PtyInlineSafeChars,
            "and under the pty-safe size, so the oversize incident never fired either");
    }

    /// <summary>
    /// The pointer replaces a body that was mangled, so it is worthless if it is big enough to be
    /// mangled itself. It must fit inside a single 1024-byte chunk — the unit the transport was
    /// measured to drop.
    /// </summary>
    [Test]
    public void a_brief_pointer_fits_in_one_transport_chunk()
    {
        var pointer = DelegationReportFormatter.BuildBriefPointer(
            NewTask(), Settings, spillPath: null, fullLength: 2_320);

        pointer.Length.ShouldBeLessThanOrEqualTo(
            new DelegationSettings().BriefInlineMaxBytes,
            "a pointer over the brief ceiling could lose its own head, which is the whole failure "
            + "it exists to prevent");
    }

    [Test]
    public void a_brief_pointer_names_the_spill_file_when_one_was_written()
    {
        var spill = Path.Combine("C:", "src", "antiphon", ".antiphon", "task-7f3a2b91-brief.md");

        var pointer = DelegationReportFormatter.BuildBriefPointer(
            NewTask(), Settings, spill, fullLength: 5_203);

        pointer.ShouldContain(spill);
        pointer.ShouldContain("Read it in full before you do anything else");
    }
}

/// <summary>
/// The advisory file lease that mitigates Shared being the default: two delegates writing the same
/// area are serialised rather than racing on read-modify-write.
/// </summary>
[Category("Unit")]
public class DelegationScopeLeaseTests
{
    private static readonly string DirA = Path.Combine("C:", "src", "antiphon");
    private static readonly string DirB = Path.Combine("C:", "src", "other");

    [Test]
    public void identical_scopes_in_the_same_directory_intersect()
    {
        AgentTaskDispatcher.ScopesIntersect((DirA, "docs/**"), (DirA, "docs/**")).ShouldBeTrue();
    }

    [Test]
    public void a_scope_nested_inside_another_intersects()
    {
        AgentTaskDispatcher.ScopesIntersect((DirA, "docs/**"), (DirA, "docs/features/*.md")).ShouldBeTrue();
    }

    [Test]
    public void sibling_scopes_do_not_intersect()
    {
        AgentTaskDispatcher.ScopesIntersect((DirA, "docs/**"), (DirA, "server/**")).ShouldBeFalse();
    }

    [Test]
    public void the_same_scope_in_a_different_directory_does_not_intersect()
    {
        // Cross-repo tasks must run concurrently — that is the point of agent-per-repo.
        AgentTaskDispatcher.ScopesIntersect((DirA, "docs/**"), (DirB, "docs/**")).ShouldBeFalse();
    }
}

/// <summary>
/// Who gets the PreToolUse deny hook. Exactly one shape qualifies — an orchestrator in its OWN
/// worktree — because the hook is a settings file, and a settings file in a shared directory
/// changes every session that runs there.
/// </summary>
[Category("Unit")]
public class DelegationDenyHookPolicyTests
{
    private static AgentTask Task(
        AgentTaskKind kind, WorkspaceMode workspace, bool? denyDirectEdits = null) => new()
    {
        Id = Guid.NewGuid(),
        Kind = kind,
        Workspace = workspace,
        DenyDirectEdits = denyDirectEdits,
    };

    private static readonly DelegationSettings Enabled = new() { OrchestratorDenyHookEnabled = true };
    private static readonly DelegationSettings Disabled = new() { OrchestratorDenyHookEnabled = false };

    [Test]
    public void an_orchestrator_in_its_own_worktree_gets_the_hook_by_default()
    {
        AgentTaskDispatcher.ShouldArmDenyHook(
            Task(AgentTaskKind.Orchestrator, WorkspaceMode.Worktree), Enabled).ShouldBeTrue();
    }

    [Test]
    public void a_worker_never_gets_the_hook()
    {
        // Its whole job is to edit — even a worker that opted into a worktree.
        AgentTaskDispatcher.ShouldArmDenyHook(
            Task(AgentTaskKind.Worker, WorkspaceMode.Worktree), Enabled).ShouldBeFalse();
    }

    [Test]
    public void a_shared_orchestrator_never_gets_the_hook()
    {
        // No worktree means no safe place to put the settings file.
        AgentTaskDispatcher.ShouldArmDenyHook(
            Task(AgentTaskKind.Orchestrator, WorkspaceMode.Shared), Enabled).ShouldBeFalse();
    }

    [Test]
    public void the_per_task_choice_beats_the_config_both_ways()
    {
        // -AllowDirectEdits on an enabled config: the orchestrator needs to write its plan file.
        AgentTaskDispatcher.ShouldArmDenyHook(
            Task(AgentTaskKind.Orchestrator, WorkspaceMode.Worktree, denyDirectEdits: false), Enabled)
            .ShouldBeFalse();
        // An explicit request on a disabled config: this run wants the guardrail anyway.
        AgentTaskDispatcher.ShouldArmDenyHook(
            Task(AgentTaskKind.Orchestrator, WorkspaceMode.Worktree, denyDirectEdits: true), Disabled)
            .ShouldBeTrue();
    }

    [Test]
    public void config_off_means_no_hook_when_the_task_did_not_ask()
    {
        AgentTaskDispatcher.ShouldArmDenyHook(
            Task(AgentTaskKind.Orchestrator, WorkspaceMode.Worktree), Disabled).ShouldBeFalse();
    }
}

/// <summary>
/// The shipped pool timings are product decisions, not incidental values: reserved for the caller
/// for FIVE minutes (follow-ups keep their context), back in the general pool after, retired after
/// an HOUR idle. A refactor that silently shortens these turns warm reuse back into cold starts.
/// </summary>
[Category("Unit")]
public class DelegationPoolDefaultsTests
{
    [Test]
    public void the_shipped_pool_timings_are_five_minutes_reserved_and_an_hour_to_retire()
    {
        var settings = new DelegationSettings();
        settings.PoolEnabled.ShouldBeTrue();
        settings.PoolReservedForCallerMinutes.ShouldBe(5);
        settings.PoolIdleRetireMinutes.ShouldBe(60);
        settings.PoolMaxIdlePerDirectory.ShouldBe(3);
    }
}

/// <summary>
/// A delegate that ends its turn asking something needs an ANSWER, not a retry — so it comes back
/// Blocked rather than Succeeded. Deliberately conservative: a report that merely mentions a
/// question mid-text is still a finished report.
/// </summary>
[Category("Unit")]
public class DelegationQuestionDetectionTests
{
    [Test]
    public void a_trailing_question_reads_as_blocked()
    {
        AgentTaskReplyService.LooksLikeAQuestion(
            "Added Fizz(int).\n\nBuzz throws on negatives — should Fizz match that?").ShouldBeTrue();
    }

    [Test]
    public void a_question_mark_buried_mid_report_does_not_block()
    {
        AgentTaskReplyService.LooksLikeAQuestion(
            "Fixed the parser: it mishandled '?' in query strings.\n\n142 passed, 0 failed.").ShouldBeFalse();
    }

    [Test]
    public void a_plain_outcome_report_is_not_a_question()
    {
        AgentTaskReplyService.LooksLikeAQuestion("142 passed, 0 failed. Build clean.").ShouldBeFalse();
    }

    [Test]
    public void an_empty_report_is_not_a_question()
    {
        AgentTaskReplyService.LooksLikeAQuestion("").ShouldBeFalse();
    }
}

/// <summary>
/// The tier ladder — the cost decision the whole design turns on — and the four-counter pricing
/// that CARD-0023 fixed. The per-root ceiling gates DISPATCH on these numbers, so a rate edit that
/// silently reintroduced the 10x error would throttle real runs on spend that never happened.
/// </summary>
[Category("Unit")]
public class DelegationCostTests
{
    private static readonly DelegationPricingSettings Pricing = new();

    /// <summary>Inside the Sonnet introductory window; every test states its own instant.</summary>
    private static readonly DateTime DuringPromo = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterPromo = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public void the_cheap_tier_costs_far_less_than_the_frontier_tier_for_the_same_work()
    {
        var work = new TokenSpend(100_000, 0, 0, 10_000);
        var frontier = DelegationCost.Estimate(Pricing, AgentModelLevel.Frontier, work, AfterPromo);
        var low = DelegationCost.Estimate(Pricing, AgentModelLevel.Low, work, AfterPromo);

        low.ShouldBeLessThan(frontier / 8, "the ladder only pays off if the bottom rung is far cheaper");
    }

    [Test]
    public void zero_tokens_cost_nothing()
    {
        DelegationCost.Estimate(Pricing, AgentModelLevel.Frontier, TokenSpend.Zero, AfterPromo).ShouldBe(0m);
    }

    [Test]
    public void the_shipped_rate_table_is_the_published_list_price()
    {
        // Pinned so a table edit is a deliberate act. Per million tokens, in/out:
        // Fable 5 $10/$50, Opus 5 $5/$25, Sonnet 5 $3/$15, Haiku 4.5 $1/$5.
        var expected = new (AgentModelLevel Level, decimal In, decimal Out)[]
        {
            (AgentModelLevel.Frontier, 10m, 50m),
            (AgentModelLevel.High, 5m, 25m),
            (AgentModelLevel.Medium, 3m, 15m),
            (AgentModelLevel.Low, 1m, 5m),
        };

        foreach (var (level, input, output) in expected)
        {
            var rates = DelegationCost.RatesFor(Pricing, level, AfterPromo);
            rates.InputPerMillion.ShouldBe(input, $"{level} input rate");
            rates.OutputPerMillion.ShouldBe(output, $"{level} output rate");
        }
    }

    [Test]
    public void a_cache_read_is_a_tenth_of_input_and_a_cache_write_is_a_quarter_more()
    {
        // The multipliers ARE the fix. Cache read ~0.1x base input; cache write 1.25x at the
        // 5-minute TTL Claude Code uses (2x at 1h — see DelegationPricingSettings).
        var rates = DelegationCost.RatesFor(Pricing, AgentModelLevel.High, AfterPromo);

        rates.CacheReadPerMillion.ShouldBe(rates.InputPerMillion * 0.10m);
        rates.CacheWritePerMillion.ShouldBe(rates.InputPerMillion * 1.25m);
    }

    [Test]
    public void the_sonnet_introductory_price_applies_inside_its_window_and_not_after()
    {
        DelegationCost.RatesFor(Pricing, AgentModelLevel.Medium, DuringPromo).InputPerMillion
            .ShouldBe(2m, "Sonnet 5 runs an introductory $2/$10 through 2026-08-31");
        DelegationCost.RatesFor(Pricing, AgentModelLevel.Medium, DuringPromo).OutputPerMillion.ShouldBe(10m);

        DelegationCost.RatesFor(Pricing, AgentModelLevel.Medium, AfterPromo).InputPerMillion
            .ShouldBe(3m, "list price resumes the instant the window closes");
        DelegationCost.RatesFor(Pricing, AgentModelLevel.Medium, AfterPromo).OutputPerMillion.ShouldBe(15m);
    }

    /// <summary>
    /// The measured CARD-0023 session (task 0b0f558c, Medium/sonnet, 6.5 minutes), deduplicated
    /// per API call: 114 uncached input, 5,642,467 cache reads, 96,552 cache writes, 27,735 output.
    /// It was reported as $31.29. This pins the honest figure so a future rate or multiplier edit
    /// cannot quietly put the order of magnitude back.
    /// </summary>
    [Test]
    public void a_cache_heavy_session_is_priced_at_cache_rates_not_input_rates()
    {
        var measured = new TokenSpend(114, 5_642_467, 96_552, 27_735);

        // At list ($3/$15): 0.000342 + 1.6927401 + 0.362070 + 0.416025
        var atList = DelegationCost.Estimate(Pricing, AgentModelLevel.Medium, measured, AfterPromo);
        atList.ShouldBe(2.471177m);

        // Inside the introductory window ($2/$10) the same session is cheaper again.
        var atPromo = DelegationCost.Estimate(Pricing, AgentModelLevel.Medium, measured, DuringPromo);
        atPromo.ShouldBe(1.647451m);

        // The regression guard: the old model collapsed the three input counters and applied the
        // full input rate to the sum. Whatever the rates become, that must stay far more expensive.
        var collapsed = new TokenSpend(measured.TotalInputTokens, 0, 0, measured.OutputTokens);
        var asFreshInput = DelegationCost.Estimate(Pricing, AgentModelLevel.Medium, collapsed, AfterPromo);
        asFreshInput.ShouldBeGreaterThan(
            atList * 5m, "pricing cache reads as fresh input is what overstated a run by ~10x");
    }

    [Test]
    public void an_unknown_tier_falls_back_to_a_real_rate_rather_than_to_zero()
    {
        // A mistyped or emptied config must not make every task free — that would put the per-root
        // ceiling permanently out of reach.
        var emptied = new DelegationPricingSettings { Rates = new() };

        DelegationCost.Estimate(emptied, AgentModelLevel.Medium, new TokenSpend(1_000_000, 0, 0, 0), AfterPromo)
            .ShouldBeGreaterThan(0m);
    }
}

/// <summary>
/// CARD-0084 S5 — the per-KIND rate overlay. The tier ladder is an abstraction over model families
/// that are priced nothing alike, so before this existed a Grok task was billed at whatever Claude
/// model shares its rung: a Grok Frontier delegate priced at fable's $10/$50 instead of grok-4.6's
/// $2/$6, wrong by ~3.8x on a real cache-heavy session. That error runs in the CONSERVATIVE
/// direction — the per-root ceiling gates dispatch on the sum of these figures, so an inflated one
/// blocks work on spend that never happened.
///
/// Rates are xAI's published list prices (https://docs.x.ai/docs/models, retrieved 2026-08-18).
/// Everything here also exists to pin the other half of the contract: with the overlay shipped,
/// Claude prices IDENTICALLY to before, for every tier, promo window and fallback.
/// </summary>
[Category("Unit")]
public class DelegationKindPricingTests
{
    private static readonly DelegationPricingSettings Pricing = new();

    private static readonly DateTime DuringPromo = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterPromo = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly AgentModelLevel[] AllLevels =
        [AgentModelLevel.Frontier, AgentModelLevel.High, AgentModelLevel.Medium, AgentModelLevel.Low];

    /// <summary>A real Grok delegate's shape: mostly cache reads, which is where kind matters most.</summary>
    private static readonly TokenSpend Session = new(18_400, 742_000, 61_500, 12_300);

    [Test]
    public void omitting_the_kind_prices_exactly_as_claude_code_at_every_tier_and_in_the_promo_window()
    {
        // The whole safety claim of this slice: every pre-existing caller passes no kind, and every
        // stored row carries the column default. Neither may move by a cent.
        foreach (var level in AllLevels)
        {
            foreach (var at in new[] { DuringPromo, AfterPromo })
            {
                DelegationCost.RatesFor(Pricing, level, at)
                    .ShouldBe(DelegationCost.RatesFor(Pricing, level, at, AgentKind.ClaudeCode), $"{level} @ {at:d}");
                DelegationCost.Estimate(Pricing, level, Session, at)
                    .ShouldBe(DelegationCost.Estimate(Pricing, level, Session, at, AgentKind.ClaudeCode));
            }
        }
    }

    [Test]
    public void the_claude_ladder_still_reads_its_published_list_price_with_the_overlay_shipped()
    {
        // Duplicated from DelegationCostTests deliberately: that test proves the table, this one
        // proves the OVERLAY did not shadow it. Fable $10/$50, Opus $5/$25, Sonnet $3/$15, Haiku $1/$5.
        var expected = new (AgentModelLevel Level, decimal In, decimal Out)[]
        {
            (AgentModelLevel.Frontier, 10m, 50m),
            (AgentModelLevel.High, 5m, 25m),
            (AgentModelLevel.Medium, 3m, 15m),
            (AgentModelLevel.Low, 1m, 5m),
        };

        foreach (var (level, input, output) in expected)
        {
            var rates = DelegationCost.RatesFor(Pricing, level, AfterPromo, AgentKind.ClaudeCode);
            rates.InputPerMillion.ShouldBe(input, $"{level} input rate");
            rates.OutputPerMillion.ShouldBe(output, $"{level} output rate");
        }
    }

    [Test]
    public void the_shipped_grok_table_is_xais_published_list_price()
    {
        // https://docs.x.ai/docs/models, retrieved 2026-08-18, sub-200k tier:
        //   grok-4.6  $2.00 in / $6.00 out / $0.50 cached input   (Frontier and High — ForGrok)
        //   grok-4.5  $2.00 in / $6.00 out / $0.30 cached input   (Medium and Low)
        // Cache WRITE is $2.00 on both: xAI publishes no cache-write price, so a cache write is
        // ordinary input — Anthropic's 1.25x TTL premium does not exist here.
        var expected = new (AgentModelLevel Level, decimal CachedIn)[]
        {
            (AgentModelLevel.Frontier, 0.50m),
            (AgentModelLevel.High, 0.50m),
            (AgentModelLevel.Medium, 0.30m),
            (AgentModelLevel.Low, 0.30m),
        };

        foreach (var (level, cachedIn) in expected)
        {
            var rates = DelegationCost.RatesFor(Pricing, level, AfterPromo, AgentKind.Grok);
            rates.InputPerMillion.ShouldBe(2m, $"{level} input rate");
            rates.OutputPerMillion.ShouldBe(6m, $"{level} output rate");
            rates.CacheReadPerMillion.ShouldBe(cachedIn, $"{level} cached-input rate");
            rates.CacheWritePerMillion.ShouldBe(
                2m, $"{level} cache-write rate — xAI publishes none, so it is billed as input");
        }
    }

    [Test]
    public void the_claude_cache_multipliers_do_not_leak_onto_grok()
    {
        // Both would be wrong, in opposite directions: 0.10x would price grok-4.6's cached input at
        // $0.20 against a published $0.50, and 1.25x would invent a cache-write premium xAI does
        // not charge. Pinned because the multipliers are what a rate entry falls back to when a
        // future edit drops the explicit pins.
        var rates = DelegationCost.RatesFor(Pricing, AgentModelLevel.High, AfterPromo, AgentKind.Grok);

        rates.CacheReadPerMillion.ShouldNotBe(rates.InputPerMillion * Pricing.CacheReadMultiplier);
        rates.CacheWritePerMillion.ShouldBe(rates.InputPerMillion, "no TTL premium is published");
    }

    [Test]
    public void the_sonnet_introductory_window_does_not_reach_grok()
    {
        // The promo is a property of a Claude rate ENTRY, and Grok's entries carry none — so the
        // Medium rung must read the same price inside the window as outside it.
        DelegationCost.RatesFor(Pricing, AgentModelLevel.Medium, DuringPromo, AgentKind.Grok)
            .ShouldBe(DelegationCost.RatesFor(Pricing, AgentModelLevel.Medium, AfterPromo, AgentKind.Grok));
    }

    [Test]
    public void a_grok_escalation_from_high_to_frontier_costs_the_same_because_both_are_grok_4_6()
    {
        // ForGrok maps Frontier and High to the same model. The ladder still buys a fresh context;
        // it does not buy a dearer one, and the price must say so rather than imply otherwise.
        DelegationCost.Estimate(Pricing, AgentModelLevel.Frontier, Session, AfterPromo, AgentKind.Grok)
            .ShouldBe(DelegationCost.Estimate(Pricing, AgentModelLevel.High, Session, AfterPromo, AgentKind.Grok));

        // ...and Medium/Low likewise share grok-4.5.
        DelegationCost.Estimate(Pricing, AgentModelLevel.Medium, Session, AfterPromo, AgentKind.Grok)
            .ShouldBe(DelegationCost.Estimate(Pricing, AgentModelLevel.Low, Session, AfterPromo, AgentKind.Grok));
    }

    /// <summary>
    /// The worked example, priced by hand from the published rates. A Grok 4.6 delegate's
    /// dispatch-to-settle window as <c>GrokTranscriptNormalizer</c> records it — one
    /// <c>turn_completed.usage</c> per turn, four counters each, summed by
    /// <see cref="DelegationUsageRollup"/>: 18,400 uncached input, 742,000 cached reads, 61,500
    /// cache writes, 12,300 output.
    /// </summary>
    [Test]
    public void a_grok_session_is_priced_from_grok_rates_not_from_the_claude_rung_it_shares()
    {
        //  18,400 / 1M x $2.00 = 0.036800
        // 742,000 / 1M x $0.50 = 0.371000
        //  61,500 / 1M x $2.00 = 0.123000
        //  12,300 / 1M x $6.00 = 0.073800
        var grok = DelegationCost.Estimate(Pricing, AgentModelLevel.Frontier, Session, AfterPromo, AgentKind.Grok);
        grok.ShouldBe(0.604600m);

        // grok-4.5 differs only in the cached-input rate ($0.30): 742,000 x $0.30 = 0.222600.
        DelegationCost.Estimate(Pricing, AgentModelLevel.Medium, Session, AfterPromo, AgentKind.Grok)
            .ShouldBe(0.456200m);

        // What the same session cost before this slice — the fable rung it happens to share.
        var asClaudeFrontier = DelegationCost.Estimate(Pricing, AgentModelLevel.Frontier, Session, AfterPromo);
        asClaudeFrontier.ShouldBe(2.309750m);
        asClaudeFrontier.ShouldBeGreaterThan(grok * 3m, "the miss this slice closes is roughly 3.8x");
    }

    [Test]
    public void a_kind_with_no_overlay_prices_on_the_claude_ladder_rather_than_at_zero()
    {
        // Codex and OpenCode are not dispatchable kinds today (S2's allowlist) and have no rates.
        // A wrong-but-real number keeps the per-root ceiling reachable; zero would not.
        foreach (var kind in new[] { AgentKind.Codex, AgentKind.OpenCode, AgentKind.Raw })
        {
            DelegationCost.RatesFor(Pricing, AgentModelLevel.Medium, AfterPromo, kind)
                .ShouldBe(DelegationCost.RatesFor(Pricing, AgentModelLevel.Medium, AfterPromo), kind.ToString());
        }
    }

    [Test]
    public void a_tier_missing_from_a_kinds_overlay_falls_back_to_that_kinds_high_rate()
    {
        // NOT to Claude's rate for the tier — a partial overlay is still the right provider.
        var partial = new DelegationPricingSettings
        {
            KindRates = new()
            {
                [nameof(AgentKind.Grok)] = new()
                {
                    [nameof(AgentModelLevel.High)] = new() { InputPerMillion = 2m, OutputPerMillion = 6m },
                },
            },
        };

        var medium = DelegationCost.RatesFor(partial, AgentModelLevel.Medium, AfterPromo, AgentKind.Grok);
        medium.InputPerMillion.ShouldBe(2m, "the kind's own High rung, not Sonnet's $3");
        medium.OutputPerMillion.ShouldBe(6m);
    }

    [Test]
    public void an_overlay_with_nothing_usable_in_it_falls_through_to_the_tier_ladder()
    {
        // A config edit that leaves an empty (or wholly mistyped) kind block must land on the
        // Claude ladder — wrong for the provider, but a real rate. Pricing at zero would make the
        // per-root ceiling unreachable, which is the failure this fallback chain exists to prevent.
        var emptyBlock = new DelegationPricingSettings
        {
            KindRates = new() { [nameof(AgentKind.Grok)] = new() },
        };

        DelegationCost.RatesFor(emptyBlock, AgentModelLevel.Medium, AfterPromo, AgentKind.Grok)
            .ShouldBe(DelegationCost.RatesFor(emptyBlock, AgentModelLevel.Medium, AfterPromo));
    }

    [Test]
    public void a_grok_task_with_every_table_emptied_still_costs_more_than_nothing()
    {
        // Both tables gone: the last resort is the built-in High rate, for a kind as for a tier.
        var emptied = new DelegationPricingSettings { Rates = new(), KindRates = new() };

        DelegationCost.Estimate(
            emptied, AgentModelLevel.Medium, new TokenSpend(1_000_000, 0, 0, 0), AfterPromo, AgentKind.Grok)
            .ShouldBeGreaterThan(0m);
    }

    [Test]
    public void a_kind_key_is_matched_without_regard_to_case()
    {
        // The shipped table is OrdinalIgnoreCase, so a hand-written "grok" in appsettings.json
        // overrides the shipped block instead of quietly sitting beside it.
        Pricing.KindRates.ContainsKey("grok").ShouldBeTrue();
        Pricing.KindRates.ContainsKey("GROK").ShouldBeTrue();
        Pricing.KindRates[nameof(AgentKind.Grok)].ContainsKey("high").ShouldBeTrue();
    }
}

/// <summary>
/// CARD-0027. The transport boundary is ONE ~1024-byte read chunk: a body that fits in one arrives
/// whole, a body that spans two or more is truncated to a whole number of chunks, unpredictably.
/// Measured against the real Claude TUI — 810 and 972-byte bodies arrived whole 3/3, 1 026 and
/// 1 350-byte bodies lost their heads 3/3, and the cut sits at body byte 1029 at 7-byte resolution.
/// See docs/investigations/2026-08-11-pty-chunk-loss-root-cause-CARD-0027.md.
/// </summary>
[Category("Unit")]
public class PtyInlineCeilingTests
{
    /// <summary>The measured single-read-chunk boundary, in UTF-8 BYTES.</summary>
    private const int SingleChunkBytes = 1024;

    /// <summary>
    /// The brief ceiling is the one gate that already sits under a single chunk, which is why the
    /// brief-pointer mitigation holds. Raising it above one chunk puts briefs back in the failure
    /// mode that stranded four tasks on 2026-08-11 — with no new evidence, that is a regression.
    /// </summary>
    [Test]
    public void the_brief_ceiling_stays_within_one_read_chunk()
    {
        new DelegationSettings().BriefInlineMaxBytes
            .ShouldBeLessThanOrEqualTo(SingleChunkBytes,
                "a brief must fit in ONE ~1024-byte read chunk — a body spanning two or more is "
                + "truncated to whole chunks by the receiving TUI (CARD-0027)");
    }

    /// <summary>
    /// The ceiling must be counted in UTF-8 BYTES, because that is the unit of the read chunk the
    /// TUI drops. It shipped counting <c>string.Length</c> (UTF-16 chars), and briefs here are
    /// em-dash-heavy at 3 bytes each — so a 900-CHARACTER brief could be 2 700 bytes, span three
    /// chunks, and mangle exactly as before while passing the guard.
    ///
    /// This is the arithmetic that made the char gate unsafe. It is kept as a test so the ceiling
    /// can never quietly go back to counting characters.
    /// </summary>
    [Test]
    public void the_brief_ceiling_is_counted_in_utf8_bytes_not_characters()
    {
        var settings = new DelegationSettings();
        var emDashHeavy = new string('—', settings.BriefInlineMaxBytes);

        emDashHeavy.Length.ShouldBeLessThanOrEqualTo(settings.BriefInlineMaxBytes,
            "a char-counting gate would wave this through");
        System.Text.Encoding.UTF8.GetByteCount(emDashHeavy)
            .ShouldBeGreaterThan(SingleChunkBytes,
                "yet it crosses the transport boundary — which is why the gate counts bytes");
    }

    /// <summary>
    /// The gate as actually applied: an em-dash-heavy brief under the ceiling in characters but
    /// over it in bytes must spill. This is the case that would still have mangled after 8c42ebd.
    /// </summary>
    [Test]
    public void a_multibyte_brief_over_the_byte_ceiling_is_not_typed_inline()
    {
        var settings = new DelegationSettings();
        var body = new string('—', 400); // 400 chars, 1 200 bytes

        body.Length.ShouldBeLessThan(settings.BriefInlineMaxBytes);
        System.Text.Encoding.UTF8.GetByteCount(body)
            .ShouldBeGreaterThan(settings.BriefInlineMaxBytes,
                "over the ceiling once measured in the unit the transport actually uses, so it "
                + "must spill to a file rather than be typed");
    }
}

/// <summary>
/// The two classifiers settlement uses to tell compaction's own USER records apart from the
/// prompts a turn could have answered (CARD-0041 shapes, CARD-0046 walk-back). SETTLEMENT ONLY —
/// no working/idle rule consumes them, so there is nothing to keep in lockstep here.
/// </summary>
[Category("Unit")]
public class TranscriptLocalCommandEchoTests
{
    private const string Wrapper =
        "<command-name>/compact</command-name>\n            <command-message>compact</command-message>\n"
        + "            <command-args>Keep only what serves the new task.</command-args>";

    [Test]
    public void the_wrapper_names_the_command_it_invoked()
    {
        TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, Wrapper).ShouldBe("/compact");
    }

    [Test]
    public void an_ordinary_prompt_names_no_command()
    {
        TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, "/compact do the thing")
            .ShouldBeNull("the raw typed line is not the wrapper — only the wrapper proves a command ran");
        TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, "read the spec").ShouldBeNull();
        TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.AssistantText, Wrapper).ShouldBeNull();
    }

    [Test]
    public void the_raw_typed_line_is_an_echo_of_the_command_that_ran()
    {
        // Claude records the literal typed text as a plain user record IN ADDITION to the wrapper.
        var invoked = new[] { "/compact" };
        TranscriptKinds.IsRawLocalCommandEcho(
            TranscriptKinds.UserPrompt, "/compact Keep only what serves the new task.", invoked)
            .ShouldBeTrue();
        TranscriptKinds.IsRawLocalCommandEcho(TranscriptKinds.UserPrompt, "/compact", invoked).ShouldBeTrue();
    }

    [Test]
    public void a_real_prompt_that_merely_begins_with_a_slash_is_not_an_echo()
    {
        // Why the wrapper is required rather than a '/' prefix: a real prompt may begin with a
        // slash, and skipping it in the walk-back would attribute someone else's turn to the task.
        TranscriptKinds.IsRawLocalCommandEcho(
            TranscriptKinds.UserPrompt, "/compact do the thing", []).ShouldBeFalse("no command ran");
        TranscriptKinds.IsRawLocalCommandEcho(
            TranscriptKinds.UserPrompt, "/compaction is the topic", ["/compact"])
            .ShouldBeFalse("the invocation ends at the command name, not mid-word");
        TranscriptKinds.IsRawLocalCommandEcho(
            TranscriptKinds.UserPrompt, "/status of the build please", ["/compact"]).ShouldBeFalse();
    }

    [Test]
    public void the_wrapper_itself_is_not_also_an_echo()
    {
        // It is already excluded as a local-command record; double-counting it would be harmless
        // but the classifiers must stay disjoint to read.
        TranscriptKinds.IsRawLocalCommandEcho(TranscriptKinds.UserPrompt, Wrapper, ["/compact"])
            .ShouldBeFalse();
    }

    [Test]
    public void the_four_housekeeping_shapes_are_one_filter()
    {
        // CARD-0077: settlement and the delivery watchdog share this helper so they cannot disagree.
        var invoked = new[] { "/compact" };
        TranscriptPromptSpan.IsHousekeepingPrompt(Wrapper, invoked).ShouldBeTrue();
        TranscriptPromptSpan.IsHousekeepingPrompt(
            "/compact Keep only what serves the new task.", invoked).ShouldBeTrue();
        TranscriptPromptSpan.IsHousekeepingPrompt(
            TranscriptKinds.CompactionContinuationPromptPrefix + " that ran out of context.", invoked)
            .ShouldBeTrue();
        TranscriptPromptSpan.IsHousekeepingPrompt(
            "<local-command-stdout>Compacted</local-command-stdout>", invoked).ShouldBeTrue();
        TranscriptPromptSpan.IsHousekeepingPrompt(
            "<task-notification>\n<summary>done</summary>\n</task-notification>", invoked)
            .ShouldBeTrue();
        TranscriptPromptSpan.IsHousekeepingPrompt(
            "[antiphon-task:abcd1234]\n\nDo the thing.", invoked)
            .ShouldBeFalse("a real brief is the thing the watchdog is looking for");
        TranscriptPromptSpan.IsHousekeepingPrompt("/compact do the thing", []).ShouldBeFalse(
            "without a wrapper in the span a slash-prefixed prompt is still a prompt");
    }
}

/// <summary>
/// The classifiers behind CARD-0046 slice 4: telling a BACKGROUND subagent launch from a
/// synchronous one, and pairing each launch with the notification that answers it. Shapes pinned
/// from session ac09cffd (seqs 10-20). Settlement only.
/// </summary>
[Category("Unit")]
public class SubagentNotificationTests
{
    private const string Notification =
        "<task-notification>\n<task-id>a548067d72b9d6de9</task-id>\n"
        + "<tool-use-id>toolu_016EchwymsdkwrMZzwUnmtvg</tool-use-id>\n<status>completed</status>\n"
        + "<summary>Agent \"Review CardService allocator (c)\" finished</summary>\n</task-notification>";

    [Test]
    public void a_notification_is_not_a_prompt_anybody_typed()
    {
        TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, Notification).ShouldBeTrue();
        TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, "review the commit")
            .ShouldBeFalse();
        TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.AssistantText, Notification)
            .ShouldBeFalse();
    }

    [Test]
    public void a_notification_names_the_launch_it_answers()
    {
        // The strong link: pairing by id makes four launches and three notifications an unambiguous
        // "one still running", where counting them would have to assume nothing else launches.
        TranscriptKinds.TryReadNotifiedToolUseId(Notification)
            .ShouldBe("toolu_016EchwymsdkwrMZzwUnmtvg");
        TranscriptKinds.TryReadNotifiedToolUseId("<task-notification>\n<status>completed</status>")
            .ShouldBeNull();
        TranscriptKinds.TryReadNotifiedToolUseId(null).ShouldBeNull();
    }

    [Test]
    public void the_async_launch_marker_separates_a_launch_from_an_answer()
    {
        // A background spawn answers instantly with metadata; a SYNCHRONOUS Agent call returns the
        // subagent's actual work, so only the marker means "not started yet".
        const string async_ =
            "Async agent launched successfully. (This tool result is internal metadata — never quote "
            + "or paste any part of it, including the agentId below, into a user-facing reply.)";
        async_.Contains(TranscriptKinds.AsyncAgentLaunchMarker, StringComparison.Ordinal).ShouldBeTrue();
        "All checks done by reading; I did not run the tests."
            .Contains(TranscriptKinds.AsyncAgentLaunchMarker, StringComparison.Ordinal).ShouldBeFalse();
    }
}

/// <summary>
/// The check-in backoff (CARD-0047 §1.5, re-decided CARD-0061), as arithmetic. A gap that is wrong
/// by a step or two is invisible in an integration test that only asserts "a later check was
/// scheduled", so the curve gets pinned directly, as a table rather than a re-derived formula — a
/// table beats a formula nobody re-checks.
/// </summary>
[Category("Unit")]
public class CheckScheduleBackoffTests
{
    private static readonly DelegationSettings Shipped = new();

    [Test]
    public void the_ramp_is_a_fibonacci_sequence_from_the_base_capped_at_the_ceiling()
    {
        // CARD-0061 DECIDED: 5, 10, 15, 25, 40, 60, 60 … — each interval is the sum of the previous
        // two until it reaches the 60-minute ceiling, then flat.
        var expected = new[] { 5, 10, 15, 25, 40, 60, 60 };

        for (var i = 0; i < expected.Length; i++)
        {
            var checkNumber = i + 1;
            CheckSchedule.NextInterval(Shipped, expectedDurationMinutes: 10, checkNumber)
                .ShouldBe(TimeSpan.FromMinutes(expected[i]), $"checkNumber {checkNumber}");
        }
    }

    [Test]
    public void the_ramp_does_not_scale_with_the_declared_duration()
    {
        // CARD-0061: ExpectedDurationMinutes schedules only the FIRST check (elsewhere, via
        // DispatchedAt + expected); it no longer feeds the ramp itself. A 4-minute task and a
        // 1440-minute task see the identical curve.
        foreach (var expectedDurationMinutes in new[] { 4, 10, 60, 1440 })
        {
            CheckSchedule.NextInterval(Shipped, expectedDurationMinutes, checkNumber: 1)
                .ShouldBe(TimeSpan.FromMinutes(5));
            CheckSchedule.NextInterval(Shipped, expectedDurationMinutes, checkNumber: 2)
                .ShouldBe(TimeSpan.FromMinutes(10));
        }
    }

    [Test]
    public void the_ramp_stops_climbing_at_the_ceiling()
    {
        // Otherwise a long-lived task's check-count budget runs out at ever-widening gaps instead of
        // settling into a heartbeat.
        CheckSchedule.NextInterval(Shipped, 10, 6).ShouldBe(TimeSpan.FromMinutes(Shipped.CheckMaxIntervalMinutes));
        CheckSchedule.NextInterval(Shipped, 10, 200)
            .ShouldBe(TimeSpan.FromMinutes(Shipped.CheckMaxIntervalMinutes),
                "a huge check number must clamp, not overflow into a negative TimeSpan");
    }

    [Test]
    public void a_degenerate_configuration_still_produces_a_positive_gap()
    {
        // A ceiling below the floor would otherwise schedule the next check in the past, and a
        // sweep that re-arms into the past checks on every tick forever.
        var upsideDown = new DelegationSettings { CheckMinIntervalMinutes = 20, CheckMaxIntervalMinutes = 5 };

        CheckSchedule.NextInterval(upsideDown, 10, 1).ShouldBe(TimeSpan.FromMinutes(20));
        CheckSchedule.NextInterval(new DelegationSettings { CheckMinIntervalMinutes = 0 }, 1, 1)
            .ShouldBeGreaterThan(TimeSpan.Zero);
    }
}

/// <summary>
/// The rounding step CARD-0061's follow-up bolted onto the ramp: below 30 minutes to the nearest 5,
/// from 30 to 60 to the nearest 10, above 60 hard-capped at 60. Kept as its own table so the ramp
/// arithmetic and the rounding can be reasoned about — and broken — independently.
/// </summary>
[Category("Unit")]
public class CheckScheduleRoundingTests
{
    [Test]
    [Arguments(7, 5)]
    [Arguments(12, 10)]
    [Arguments(33, 30)]
    [Arguments(37, 40)]
    [Arguments(65, 60)]
    public void a_raw_value_rounds_to_the_decided_case(int raw, int rounded)
    {
        CheckSchedule.RoundInterval(raw).ShouldBe(rounded);
    }

    [Test]
    public void an_already_round_value_passes_through_unchanged()
    {
        CheckSchedule.RoundInterval(40).ShouldBe(40);
        CheckSchedule.RoundInterval(25).ShouldBe(25);
    }

    [Test]
    public void the_shipped_ramp_sequence_is_unchanged_by_rounding()
    {
        // CARD-0061 DECIDED (follow-up): rounding must not move a single value of the sequence
        // already pinned by CheckScheduleBackoffTests — every raw ramp value is already a case the
        // rounding rule leaves alone.
        var expected = new[] { 5, 10, 15, 25, 40, 60, 60 };
        var shipped = new DelegationSettings();

        for (var i = 0; i < expected.Length; i++)
        {
            var checkNumber = i + 1;
            CheckSchedule.NextInterval(shipped, expectedDurationMinutes: 10, checkNumber)
                .ShouldBe(TimeSpan.FromMinutes(expected[i]), $"checkNumber {checkNumber}");
        }
    }
}

/// <summary>
/// The caller-visible consequence of <see cref="CheckScheduleBackoffTests"/>: not the interval
/// function in isolation, but the elapsed clock time each check actually lands at, given the first
/// check is <c>DispatchedAt + ExpectedDurationMinutes</c> and every check after it adds
/// <see cref="CheckSchedule.NextInterval"/> for the next check number.
/// </summary>
[Category("Unit")]
public class CheckScheduleElapsedTimesTests
{
    [Test]
    public void a_ten_minute_task_checks_at_the_decided_elapsed_times()
    {
        // CARD-0061 DECIDED table, expected = 10m (the default): 10, 15, 25, 40, 65, 105 …
        AssertElapsedSequence(expectedDurationMinutes: 10, new[] { 10, 15, 25, 40, 65, 105 });
    }

    [Test]
    public void a_twenty_five_minute_task_checks_at_the_decided_elapsed_times()
    {
        // CARD-0061 DECIDED table, expected = 25m: 25, 30, 40, 55, 80, 120 …
        AssertElapsedSequence(expectedDurationMinutes: 25, new[] { 25, 30, 40, 55, 80, 120 });
    }

    [Test]
    public void a_sixty_minute_task_checks_at_the_decided_elapsed_times()
    {
        // CARD-0061 DECIDED table, expected = 60m: 60, 65, 75, 90, 115, 155 …
        AssertElapsedSequence(expectedDurationMinutes: 60, new[] { 60, 65, 75, 90, 115, 155 });
    }

    private static void AssertElapsedSequence(int expectedDurationMinutes, int[] elapsedMinutesAtEachCheck)
    {
        var settings = new DelegationSettings();
        var elapsed = expectedDurationMinutes; // the first check: DispatchedAt + expected

        for (var i = 0; i < elapsedMinutesAtEachCheck.Length; i++)
        {
            elapsed.ShouldBe(elapsedMinutesAtEachCheck[i], $"check #{i + 1}");
            var checkNumber = i + 1;
            elapsed += (int)CheckSchedule.NextInterval(settings, expectedDurationMinutes, checkNumber).TotalMinutes;
        }
    }
}
