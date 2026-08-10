using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
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
        body.ShouldContain("characters omitted");
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
