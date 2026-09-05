using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0084 S4 — every surface that NAMES a task's tier names it on the ladder the task actually
/// runs. Before this, a dozen display paths called <c>ModelLevelAliases.ForClaude</c> unconditionally
/// because Claude was the only kind that existed, so the day a Grok delegate shipped, its own
/// completion note, handoff block, retry event and check digest all told it — and the operator, and
/// the check interpreter — that it was running <c>fable</c>.
///
/// <para>Two directions are asserted throughout, and the second is the one that keeps the sweep
/// honest: a Grok task must never read as a Claude model, and a Claude task's text must be
/// byte-identical to what it was before the helper existed. A sweep that quietly reworded the
/// Claude strings would be invisible here without that half.</para>
/// </summary>
[Category("Unit")]
public class ModelLevelAliasDisplayTests
{
    // ---- the helper itself ----------------------------------------------------------------------

    [Test]
    [Arguments(AgentModelLevel.Frontier, "fable")]
    [Arguments(AgentModelLevel.High, "opus")]
    [Arguments(AgentModelLevel.Medium, "sonnet")]
    [Arguments(AgentModelLevel.Low, "haiku")]
    public void the_claude_ladder_is_unchanged_through_the_kind_aware_helper(
        AgentModelLevel level, string expected)
    {
        // Back-compat, stated as an identity rather than a repeated literal table: every existing
        // ForClaude caller could be swapped for For(ClaudeCode, ...) with no observable change.
        ModelLevelAliases.For(AgentKind.ClaudeCode, level).ShouldBe(expected);
        ModelLevelAliases.For(AgentKind.ClaudeCode, level).ShouldBe(ModelLevelAliases.ForClaude(level));
    }

    [Test]
    [Arguments(AgentModelLevel.Frontier, "grok-4.6")]
    [Arguments(AgentModelLevel.High, "grok-4.6")]
    [Arguments(AgentModelLevel.Medium, "grok-4.6")]
    [Arguments(AgentModelLevel.Low, "grok-4.6")]
    public void the_grok_ladder_answers_for_the_grok_kind(AgentModelLevel level, string expected)
    {
        // CARD-0169: every level launches grok-4.6 - the ladder has no rungs left, by instruction.
        ModelLevelAliases.For(AgentKind.Grok, level).ShouldBe(expected);
        ModelLevelAliases.For(AgentKind.Grok, level).ShouldBe(ModelLevelAliases.ForGrok(level));
    }

    [Test]
    [Arguments(AgentModelLevel.Frontier, "gpt-6-astra")]
    [Arguments(AgentModelLevel.High, "gpt-5.6-terra")]
    [Arguments(AgentModelLevel.Medium, "gpt-5.6-luna")]
    [Arguments(AgentModelLevel.Low, "gpt-5.6-luna")]
    public void the_codex_ladder_answers_for_the_codex_kind(AgentModelLevel level, string expected)
    {
        // CARD-0099 S3 / CARD-0396. Astra > Sol > Terra > Luna is the CAPABILITY order (catalog
        // priority 1/6/7/8). Dispatch is conservative: Frontier is Astra, High stays Terra (Sol is
        // still a supported slug), Medium/Low share Luna.
        ModelLevelAliases.For(AgentKind.Codex, level).ShouldBe(expected);
        ModelLevelAliases.For(AgentKind.Codex, level).ShouldBe(ModelLevelAliases.ForCodex(level));
    }

    [Test]
    public void the_codex_ladder_pins_full_versioned_slugs_and_never_a_bare_tier_name()
    {
        // Measured 2026-08-20 against codex-cli 0.147.0 (`-m luna`) and 2026-09-05 against 0.153.4
        // (`-m astra`): both 400. Codex has no unversioned aliases, so a "family alias" of the kind
        // Claude and Grok use would never start a session. Frontier is `gpt-6-astra`, not `gpt-5.6-*`.
        foreach (var level in Enum.GetValues<AgentModelLevel>())
        {
            var slug = ModelLevelAliases.For(AgentKind.Codex, level);
            slug.ShouldStartWith("gpt-");
            slug.ShouldNotBe("astra");
            slug.ShouldNotBe("sol");
            slug.ShouldNotBe("terra");
            slug.ShouldNotBe("luna");
        }
    }

    [Test]
    public void no_kind_ever_displays_the_other_provider_s_family()
    {
        // The whole failure mode in one assertion, across the full ladder: whatever a Grok task
        // reads, it is never a Claude family name, and vice versa — and now the same for Codex,
        // which is the kind that proves ModelLevelAliases.For's doc-comment contract is real.
        foreach (var level in Enum.GetValues<AgentModelLevel>())
        {
            ModelLevelAliases.For(AgentKind.Grok, level).ShouldStartWith("grok-");
            ModelLevelAliases.For(AgentKind.Codex, level).ShouldStartWith("gpt-");
            var claude = ModelLevelAliases.For(AgentKind.ClaudeCode, level);
            claude.ShouldNotContain("grok");
            claude.ShouldNotContain("gpt-");
        }
    }

    [Test]
    public void a_codex_task_s_completion_note_names_a_codex_model()
    {
        // The header is what the caller sees about who did the work. Naming fable here would
        // misattribute the run to Claude on the one surface the caller actually reads.
        var note = DelegationReportFormatter.BuildCompletionNote(
            NewTask(AgentKind.Codex, AgentModelLevel.Frontier), Settings, "Landed the change.");

        note.Body.ShouldContain("gpt-6-astra");
        note.Body.ShouldNotContain("fable");
    }

    [Test]
    public void a_codex_handoff_names_the_ladder_codex_actually_climbed()
    {
        // Typed into the next attempt's own composer, so a wrong alias here is a lie told to the
        // delegate about itself — the one reader with no way to check.
        var task = NewTask(AgentKind.Codex, AgentModelLevel.High);
        task.Attempt = 2;
        task.EscalatedFrom = AgentModelLevel.Medium;
        task.Result = "Got as far as the repro.";

        var handoff = DelegationReportFormatter.BuildHandoff(task).ShouldNotBeNull();

        handoff.ShouldContain("at gpt-5.6-luna, escalated to gpt-5.6-terra");
        handoff.ShouldNotContain("sonnet");
        handoff.ShouldNotContain("opus");
    }

    // ---- the completion note the CALLER reads ---------------------------------------------------

    [Test]
    public void a_grok_task_s_completion_note_names_a_grok_model()
    {
        // The header is what the caller sees about who did the work. Naming fable here would
        // misattribute the run — and the cost line beside it is priced off Grok's rates (S5), so
        // the two halves of the same header would disagree about which provider was billed.
        var note = DelegationReportFormatter.BuildCompletionNote(
            NewTask(AgentKind.Grok, AgentModelLevel.High), Settings, "Landed the change.");

        note.Body.ShouldContain("grok-4.6");
        note.Body.ShouldNotContain("opus");
    }

    [Test]
    public void a_claude_task_s_completion_note_reads_exactly_as_it_did_before()
    {
        var note = DelegationReportFormatter.BuildCompletionNote(
            NewTask(AgentKind.ClaudeCode, AgentModelLevel.Medium), Settings, "Landed the change.");

        note.Body.ShouldContain("sonnet");
        note.Body.ShouldNotContain("grok");
    }

    // ---- the handoff block the NEXT ATTEMPT reads -----------------------------------------------

    [Test]
    public void a_grok_handoff_names_the_ladder_grok_actually_climbed()
    {
        // This text is typed into the next attempt's own composer, so a wrong alias here is a lie
        // told to the delegate about itself — the one reader with no way to check.
        var task = NewTask(AgentKind.Grok, AgentModelLevel.Frontier);
        task.Attempt = 2;
        task.EscalatedFrom = AgentModelLevel.Medium;
        task.Result = "Got as far as the repro.";

        var handoff = DelegationReportFormatter.BuildHandoff(task).ShouldNotBeNull();

        // CARD-0169: every Grok level maps to grok-4.6 now, so an escalation's "from" and "to"
        // read the same alias - the ladder has no rungs left to distinguish.
        handoff.ShouldContain("at grok-4.6, escalated to grok-4.6");
        handoff.ShouldNotContain("sonnet");
        handoff.ShouldNotContain("fable");
    }

    [Test]
    public void a_claude_handoff_reads_exactly_as_it_did_before()
    {
        var task = NewTask(AgentKind.ClaudeCode, AgentModelLevel.Frontier);
        task.Attempt = 2;
        task.EscalatedFrom = AgentModelLevel.Medium;
        task.Result = "Got as far as the repro.";

        DelegationReportFormatter.BuildHandoff(task).ShouldNotBeNull()
            .ShouldContain("at sonnet, escalated to fable");
    }

    [Test]
    public void an_unescalated_grok_handoff_still_names_grok()
    {
        // The other arm of the same expression — a plain retry has no EscalatedFrom, and it was a
        // separate ForClaude call site.
        var task = NewTask(AgentKind.Grok, AgentModelLevel.Medium);
        task.Attempt = 3;
        task.FailureReason = "Stalled with no output.";

        DelegationReportFormatter.BuildHandoff(task).ShouldNotBeNull()
            .ShouldContain("ran at grok-4.6 and did not settle this");
    }

    // ---- the check digest an INTERPRETER reasons over -------------------------------------------

    [Test]
    public void the_check_digest_reports_the_tier_on_the_running_program_s_ladder()
    {
        // The digest is evidence, not decoration: an interpreter reads it to decide whether to
        // nudge, escalate or kill, and it is handed to the caller verbatim when no interpreter is
        // available. "tier=fable" on a Grok delegate would be a false fact in the premises.
        DelegateCheckProbe.RenderDigest(FactsFor(AgentKind.Grok, AgentModelLevel.High))
            .ShouldContain("tier=grok-4.6");

        // CARD-0169: Low also reads grok-4.6 now - every level does - but the digest must still
        // never lie and print another kind's alias here.
        DelegateCheckProbe.RenderDigest(FactsFor(AgentKind.Grok, AgentModelLevel.Low))
            .ShouldContain("tier=grok-4.6");
    }

    [Test]
    public void the_check_digest_for_a_claude_delegate_is_unchanged()
    {
        var digest = DelegateCheckProbe.RenderDigest(FactsFor(AgentKind.ClaudeCode, AgentModelLevel.High));

        // The whole line, not just the alias: the digest's shape is a parsed contract for the
        // interpreter, so the kind-aware tier must not have reordered or renamed its neighbours.
        digest.ShouldContain("status=Dispatched kind=Worker role=Code tier=opus attempt=1/2");
        digest.ShouldNotContain("grok");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static readonly DelegationSettings Settings = new()
    {
        ReplyInlineMaxChars = 20_000,
        ReplyExcerptHeadChars = 6_000,
        ReplyExcerptTailChars = 6_000,
    };

    private static AgentTask NewTask(AgentKind agentKind, AgentModelLevel level) => new()
    {
        Id = Guid.Parse("7f3a2b91-0000-0000-0000-000000000000"),
        Title = "Rewrite the Windows install section",
        Goal = "Rewrite it so every command is pwsh 7.",
        Kind = AgentTaskKind.Worker,
        Role = AgentTaskRole.Docs,
        AgentKind = agentKind,
        ModelLevel = level,
        Workspace = WorkspaceMode.Shared,
        WorkingDirectory = Path.Combine("C:", "src", "antiphon"),
        Status = AgentTaskStatus.Succeeded,
        DispatchedAt = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc),
        CompletedAt = new DateTime(2026, 8, 6, 12, 4, 12, DateTimeKind.Utc),
        CostUsd = 0.031m,
    };

    private static DelegateCheckProbe.CheckFacts FactsFor(AgentKind agentKind, AgentModelLevel level) =>
        new(
            At: new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc),
            Task: new DelegateCheckProbe.CheckTaskFacts(
                Id: Guid.Parse("7f3a2b91-0000-0000-0000-000000000000"),
                ShortId: "7f3a2b91",
                Title: "do the probed thing",
                Kind: AgentTaskKind.Worker,
                AgentKind: agentKind,
                Role: AgentTaskRole.Code,
                ModelLevel: level,
                Status: AgentTaskStatus.Dispatched,
                Settled: false,
                Attempt: 1,
                MaxAttempts: 2,
                DispatchedAt: new DateTime(2026, 8, 18, 8, 50, 0, DateTimeKind.Utc),
                RepliedAt: null,
                Age: TimeSpan.FromMinutes(10),
                ExpectedDurationMinutes: 20,
                CheckNumber: 1,
                HasResult: false,
                FailureReason: null),
            Session: null,
            TranscriptTail: [],
            Git: null,
            PendingMessages: [],
            Incidents: []);
}

/// <summary>
/// CARD-0084 S4, the half that needs a database: the task EVENTS a retry writes. The event feed is
/// the durable record of what happened to a task — it outlives the session, and it is what the board
/// and any later reader see — so a Grok retry recorded as "Retried at opus" is a permanently wrong
/// row, not a transient screen glitch. The escalation events are pinned next door in
/// <c>GrokDelegateDispatchTests</c> (S3 shipped them); this is the retry path they do not cover.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class DelegationRetryEventKindTests
{
    [Test]
    public async Task retrying_a_grok_task_records_a_grok_model_in_the_event()
    {
        var task = await SeedAsync(AgentKind.Grok, AgentModelLevel.Medium);

        await using var db = CreateContext();
        await CreateService(db).RetryAsync(task.Id, CancellationToken.None);

        var detail = await LatestRetryDetailAsync(task.Id);
        detail.ShouldBe("Retried at grok-4.6.");
    }

    [Test]
    public async Task retrying_a_claude_task_reads_exactly_as_it_did_before()
    {
        var task = await SeedAsync(AgentKind.ClaudeCode, AgentModelLevel.Medium);

        await using var db = CreateContext();
        await CreateService(db).RetryAsync(task.Id, CancellationToken.None);

        var detail = await LatestRetryDetailAsync(task.Id);
        detail.ShouldBe("Retried at sonnet.");
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static async Task<string> LatestRetryDetailAsync(Guid taskId)
    {
        await using var db = CreateContext();
        var detail = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Retried)
            .OrderByDescending(e => e.At)
            .Select(e => e.Detail)
            .FirstOrDefaultAsync();
        return detail.ShouldNotBeNull();
    }

    private static async Task<AgentTask> SeedAsync(AgentKind agentKind, AgentModelLevel level)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "retry me",
            Goal = "retry me",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            AgentKind = agentKind,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            // Anything but Queued — RetryAsync refuses a task that has not run yet. Failed needs no
            // session to stop, so the event text is the whole subject here.
            Status = AgentTaskStatus.Failed,
            WorkingDirectory = Path.GetTempPath(),
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AgentTaskService CreateService(AppDbContext db) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings { AllowedRoots = [] }),
        new MockEventBus(),
        new RecordingSessionStopper(),
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
}
