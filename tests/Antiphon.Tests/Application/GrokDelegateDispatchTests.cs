using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0084 S3 — the persisted <see cref="AgentTask.AgentKind"/> actually decides which PROGRAM
/// runs the work.
///
/// <para>Three things have to hold at once and each is a separate failure mode. A Grok task must
/// launch grok.exe with Grok's own flags (<c>--rules</c>, no <c>--name</c>, a grok model alias) —
/// wrong flags mean a process that refuses to start, or worse one that starts with its contract
/// silently dropped. A Claude task must be launched EXACTLY as it was before this slice — this is
/// the compatibility half, and it is asserted on the argument list itself rather than on a summary
/// of it. And nothing may ever fall back: a kind with no configured definition fails the dispatch
/// with the gap named, because a caller who asked for Grok and quietly got a Claude has no way to
/// tell from the report.</para>
///
/// <para>The warm pool is the fourth: it hands out live processes, so a kind mismatch there is the
/// one that costs a real session. Those tests run the REAL dispatch tick.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class GrokDelegateDispatchTests
{
    // ---- launch spec ---------------------------------------------------------------------------

    [Test]
    public void a_grok_delegate_launches_the_grok_definition_with_rules_and_a_grok_model()
    {
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentKind.Grok, AgentModelLevel.High);

        var spec = SpecOf(dispatcher, task);
        var args = spec.Args.ToList();

        spec.DefinitionName.ShouldBe("grok", "resolved by KIND, not the default definition");
        spec.Kind.ShouldBe(AgentKind.Grok);
        // The definition's own template survives — the registry composes it ahead of the extras.
        args.ShouldContain("--always-approve");

        args.ShouldNotContain("--name", customMessage:
            "--name is Claude-only; grok.exe rejects it and the launch would never start");
        args[args.IndexOf("--model") + 1].ShouldBe("grok-4.6");
        args.ShouldContain("--rules");
        args.ShouldNotContain("--append-system-prompt", customMessage:
            "Grok's system-prompt channel is --rules; the bundle would be dropped in silence");
        // Same contract as Claude's: the bundles ride an ARGUMENT, so they survive compaction.
        args[args.IndexOf("--rules") + 1]
            .ShouldContain(InstructionBundles.TextOf(InstructionBundles.DelegateBasics));
    }

    [Test]
    public void grok_maps_frontier_and_high_to_the_same_alias_and_the_lower_tiers_to_the_older_one()
    {
        // The ladder Grok actually has, asserted through the launch path rather than the alias
        // table — this is what makes the escalation disclosure below true rather than decorative.
        var (dispatcher, _) = CreateHarness();

        ModelArgOf(dispatcher, TaskFor(AgentKind.Grok, AgentModelLevel.Frontier)).ShouldBe("grok-4.6");
        ModelArgOf(dispatcher, TaskFor(AgentKind.Grok, AgentModelLevel.High)).ShouldBe("grok-4.6");
        ModelArgOf(dispatcher, TaskFor(AgentKind.Grok, AgentModelLevel.Medium)).ShouldBe("grok-4.5");
        ModelArgOf(dispatcher, TaskFor(AgentKind.Grok, AgentModelLevel.Low)).ShouldBe("grok-4.5");
    }

    [Test]
    public void a_claude_delegates_launch_arguments_are_unchanged_by_this_slice()
    {
        // The compatibility promise, pinned as the exact argument SEQUENCE the pre-S3 code built:
        // --name then --model then --append-system-prompt, in that order, off the default
        // definition. Every existing delegation is this shape.
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentKind.ClaudeCode, AgentModelLevel.High);

        var spec = SpecOf(dispatcher, task);
        var args = spec.Args.ToList();

        spec.DefinitionName.ShouldBe("claude");
        spec.Kind.ShouldBe(AgentKind.ClaudeCode);
        var name = args.IndexOf("--name");
        var model = args.IndexOf("--model");
        var prompt = args.IndexOf("--append-system-prompt");
        name.ShouldBeGreaterThanOrEqualTo(0);
        args[name + 1].ShouldBe($"task-{DelegationReportFormatter.Short(task.Id)}");
        args[model + 1].ShouldBe("opus");
        model.ShouldBe(name + 2);
        prompt.ShouldBe(model + 2);
        args.ShouldNotContain("--rules");
    }

    [Test]
    public void a_kind_with_no_configured_definition_fails_the_launch_and_names_the_gap()
    {
        // Never a fallback to the default definition. The message is what the operator gets as the
        // task's failure reason, so it has to name the kind AND where to fix it.
        var (dispatcher, _) = CreateHarness(withGrokDefinition: false);
        var task = TaskFor(AgentKind.Grok, AgentModelLevel.High);

        var ex = Should.Throw<InvalidOperationException>(() => SpecOf(dispatcher, task));

        ex.Message.ShouldContain("Grok");
        ex.Message.ShouldContain("Agents:Definitions");
        ex.Message.ShouldContain("claude (ClaudeCode)", customMessage:
            "what IS configured, so the gap is diagnosable from the failure alone");
    }

    [Test]
    public void the_command_line_budget_guards_a_grok_launch_the_same_way()
    {
        // The guard is provider-neutral and runs BEFORE anything is added, so an over-budget
        // composition fails the launch rather than handing Grok half a contract via --rules.
        var (dispatcher, _) = CreateHarness(budgetChars: 200);
        var task = TaskFor(AgentKind.Grok, AgentModelLevel.High, AgentTaskKind.Worker, AgentTaskRole.Code);

        var ex = Should.Throw<InvalidOperationException>(() => SpecOf(dispatcher, task));

        ex.Message.ShouldContain(DelegationReportFormatter.Short(task.Id));
        ex.Message.ShouldContain("Nothing was truncated");
    }

    [Test]
    public void a_grok_launch_carries_the_same_provider_neutral_antiphon_environment()
    {
        // Correlation, reply routing and depth accounting all ride the env, and none of it is
        // Claude-shaped — a Grok delegate that could not call home would settle nothing.
        var (dispatcher, _) = CreateHarness();
        var task = TaskFor(AgentKind.Grok, AgentModelLevel.High);

        var grok = SpecOf(dispatcher, task);
        var claude = SpecOf(dispatcher, TaskFor(AgentKind.ClaudeCode, AgentModelLevel.High));

        foreach (var key in new[] { "ANTIPHON_API", "ANTIPHON_SESSION_ID", "ANTIPHON_AGENT_ID", "ANTIPHON_TASK_ID" })
            grok.Env.ShouldContainKey(key);
        // The registry's per-kind env is the only difference: Grok gets its telemetry opt-outs and
        // none of Claude's nesting markers, which would mean nothing to it.
        grok.Env.ShouldContainKey("GROK_TELEMETRY_ENABLED");
        grok.Env.ShouldNotContainKey("CLAUDECODE");
        claude.Env.ShouldContainKey("CLAUDECODE");
    }

    [Test]
    public void the_spec_a_grok_dispatch_builds_would_spawn_the_real_fakegrok_binary()
    {
        // End-to-end-ish rather than end-to-end: the spec is carried all the way to a REAL staged
        // executable (resolved through AgentExecutableResolver, the same step production takes),
        // and every flag fakegrok/grok.exe would actually receive is asserted — but nothing is
        // spawned, because the value here is the argv, and a spawned TUI would make it a headed
        // test that cannot run in CI. SessionMessageQueueGrokPtyIntegrationTests owns the pty half.
        var fakeGrok = Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.exe");
        if (!File.Exists(fakeGrok))
            throw new TUnit.Core.Exceptions.SkipTestException($"fakegrok.exe not staged at {fakeGrok}");

        var (dispatcher, _) = CreateHarness(grokExe: fakeGrok);
        var task = TaskFor(AgentKind.Grok, AgentModelLevel.Medium);

        var spec = SpecOf(dispatcher, task);

        spec.Exe.ShouldBe(fakeGrok);
        spec.Kind.ShouldBe(AgentKind.Grok);
        spec.Args.ShouldContain("--always-approve");
        spec.Args.ShouldContain("--no-alt-screen");
        spec.Args.ShouldContain("--rules");
        spec.Args.ShouldNotContain("--name");
        spec.Args.ToList()[spec.Args.ToList().IndexOf("--model") + 1].ShouldBe("grok-4.5");
        spec.Cwd.ShouldBe(task.WorkingDirectory);
    }

    // ---- the dispatch itself -------------------------------------------------------------------

    [Test]
    public async Task dispatching_a_grok_task_writes_a_grok_session_a_grok_pool_row_and_a_spilled_brief()
    {
        // The whole thread in one pass: the task's kind reaches the SESSION row (which is what the
        // brief's spill gate, the tailer and delivery all read), the pool ROW (which is what the
        // next task's claim reads), and the definition name (which is what gets launched).
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness();
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.Grok);

        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.Dispatched.ShouldBeGreaterThanOrEqualTo(1);
        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);

        var session = await verify.AgentSessions.AsNoTracking()
            .SingleAsync(s => s.Id == dispatched.AgentSessionId!.Value);
        session.AgentKind.ShouldBe(AgentKind.Grok);
        session.DefinitionName.ShouldBe("grok");

        var agent = await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == dispatched.AgentId!.Value);
        agent.Kind.ShouldBe(AgentKind.Grok);
        agent.IsPoolDelegate.ShouldBeTrue();

        // CARD-0084 S1, now reached through the real dispatch: Grok's composer joins every typed
        // line, so the brief goes out as a file and a pointer rather than as one run-on paragraph.
        var brief = await verify.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == session.Id).ToListAsync();
        brief.ShouldHaveSingleItem();
        brief[0].Body.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE");
        brief[0].Body.ShouldContain(DelegationReportFormatter.TaskMarker(task.Id));
    }

    [Test]
    public async Task dispatching_a_claude_task_still_writes_a_claude_session_and_pool_row()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness();
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.ClaudeCode);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        var session = await verify.AgentSessions.AsNoTracking()
            .SingleAsync(s => s.Id == dispatched.AgentSessionId!.Value);
        session.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        session.DefinitionName.ShouldBe("claude");
        var agent = await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == dispatched.AgentId!.Value);
        agent.Kind.ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    public async Task a_kind_this_installation_cannot_launch_fails_the_task_instead_of_running_claude()
    {
        // The loud failure, end to end: the task lands Failed with the configuration gap as its
        // reason, and — the part that matters — NO session was created for it.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness(withGrokDefinition: false);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.Grok);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldNotBeNull().ShouldContain("Grok");
        failed.AgentSessionId.ShouldBeNull("no session may exist for a program that cannot be launched");
    }

    // ---- mixed-kind warm pool ------------------------------------------------------------------

    [Test]
    public async Task a_warm_claude_delegate_does_not_take_a_grok_task()
    {
        // The expensive failure this slice exists to prevent. Same directory, same tier, warm and
        // reservation-free — everything the pool matches on EXCEPT the program — and a reuse here
        // would type a Grok brief into a live Claude and look like a clean dispatch until the
        // report never came.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness();
        var (warmClaude, claudeSession) = await SeedWarmAgentAsync(workspace.Path, AgentKind.ClaudeCode);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.Grok);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.AgentId.ShouldNotBe(warmClaude, "a cold start is the CORRECT outcome here");
        dispatched.AgentSessionId.ShouldNotBe(claudeSession);

        var spawned = await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == dispatched.AgentId!.Value);
        spawned.Kind.ShouldBe(AgentKind.Grok);

        // And the warm Claude is untouched — still idle, still claimable by the Claude work it is for.
        var untouched = await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == warmClaude);
        untouched.Status.ShouldBe(AgentStatus.Idle);
        untouched.PoolIdleSince.ShouldNotBeNull();
        (await verify.SessionQueuedMessages.AsNoTracking()
            .AnyAsync(m => m.AgentSessionId == claudeSession))
            .ShouldBeFalse("nothing was typed into the Claude session");
    }

    [Test]
    public async Task a_warm_grok_delegate_does_not_take_a_claude_task()
    {
        // The mirror, which matters just as much: Grok is the opt-in kind, so its warm delegates
        // are scarce, and letting the default kind consume one would strand the work it was for.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness();
        var (warmGrok, grokSession) = await SeedWarmAgentAsync(workspace.Path, AgentKind.Grok);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.ClaudeCode);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        dispatched.AgentId.ShouldNotBe(warmGrok);
        dispatched.AgentSessionId.ShouldNotBe(grokSession);
        var untouched = await verify.Agents.AsNoTracking().SingleAsync(a => a.Id == warmGrok);
        untouched.Status.ShouldBe(AgentStatus.Idle);
    }

    [Test]
    public async Task a_warm_grok_delegate_does_take_a_grok_task()
    {
        // The positive control. Without it the two refusals above would also pass if the pool
        // simply never reused anything.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness();
        var (warmGrok, grokSession) = await SeedWarmAgentAsync(workspace.Path, AgentKind.Grok);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.Grok);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var dispatched = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        dispatched.AgentId.ShouldBe(warmGrok);
        dispatched.AgentSessionId.ShouldBe(grokSession, "the LIVE session — no cold start");
        (await verify.SessionQueuedMessages.AsNoTracking()
            .AnyAsync(m => m.AgentSessionId == grokSession))
            .ShouldBeTrue("the brief rides the queue into the warm Grok");
    }

    [Test]
    public async Task the_warm_pool_cap_is_counted_per_directory_AND_kind()
    {
        // A cap counted per directory alone would let the commoner kind evict the rarer one: three
        // warm Claudes would retire the only warm Grok in that directory — a delegate no Claude
        // task could ever have claimed, killed to make room for Claudes that already had room.
        using var workspace = new TempWorkspace();
        var (dispatcher, stopper) = CreateDispatchHarness(poolMaxIdlePerDirectory: 1);
        var (claude, claudeSession) = await SeedWarmAgentAsync(workspace.Path, AgentKind.ClaudeCode);
        var (grok, grokSession) = await SeedWarmAgentAsync(workspace.Path, AgentKind.Grok);

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.Agents.AsNoTracking().AnyAsync(a => a.Id == claude)).ShouldBeTrue();
        (await verify.Agents.AsNoTracking().AnyAsync(a => a.Id == grok)).ShouldBeTrue();
        // Scoped to THESE two sessions, never to the kill list's length: the janitor sweeps every
        // warm row in the fixture database, and other suites' delegates are legitimately retired by
        // the same call (see CLAUDE.md — an unscoped count also asserts "nobody else has data").
        stopper.Killed.ShouldNotContain(claudeSession,
            "one of each kind is one deep in each pool, not two deep in one");
        stopper.Killed.ShouldNotContain(grokSession);
    }

    [Test]
    public async Task the_cap_still_retires_a_surplus_within_one_kind()
    {
        // The other half of the same rule — widening the group key must not have widened the cap.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness(poolMaxIdlePerDirectory: 1);
        await SeedWarmAgentAsync(workspace.Path, AgentKind.Grok, idleMinutes: 1);
        await SeedWarmAgentAsync(workspace.Path, AgentKind.Grok, idleMinutes: 2);

        await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var left = await verify.Agents.AsNoTracking()
            .CountAsync(a => a.WorkingDirectory == workspace.Path && a.Kind == AgentKind.Grok);
        left.ShouldBe(1);
    }

    // ---- escalation ----------------------------------------------------------------------------

    [Test]
    public async Task escalating_a_grok_task_says_it_buys_a_fresh_context_not_a_bigger_model()
    {
        // The ladder is kind-agnostic and stays — but on Grok, High -> Frontier moves no model at
        // all, and an event that only said "Escalated ... -> ..." would promise one that does not
        // exist. It must name Grok's aliases and disclose that both rungs are the same model.
        using var workspace = new TempWorkspace();
        var task = await SeedSettledTaskAsync(workspace.Path, AgentKind.Grok, AgentModelLevel.High);

        await using var db = CreateContext();
        await CreateService(db).EscalateAsync(task.Id, to: null, CancellationToken.None);

        var detail = await LatestEscalationDetailAsync(task.Id);
        detail.ShouldContain("grok-4.6");
        detail.ShouldNotContain("fable", customMessage: "a Grok task never runs a Claude model");
        detail.ShouldNotContain("opus");
        detail.ShouldContain("FRESH CONTEXT at the same model");
        detail.ShouldContain("Frontier");
    }

    [Test]
    public async Task escalating_a_grok_task_between_real_models_makes_no_such_disclosure()
    {
        // Medium -> High IS a model change on Grok (grok-4.5 -> grok-4.6), so the note must not
        // fire — a disclosure that appears on every escalation stops being read.
        using var workspace = new TempWorkspace();
        var task = await SeedSettledTaskAsync(workspace.Path, AgentKind.Grok, AgentModelLevel.Medium);

        await using var db = CreateContext();
        await CreateService(db).EscalateAsync(task.Id, to: null, CancellationToken.None);

        var detail = await LatestEscalationDetailAsync(task.Id);
        detail.ShouldContain("grok-4.5 -> grok-4.6");
        detail.ShouldNotContain("FRESH CONTEXT");
    }

    [Test]
    public async Task escalating_a_claude_task_reads_exactly_as_it_did_before()
    {
        using var workspace = new TempWorkspace();
        var task = await SeedSettledTaskAsync(workspace.Path, AgentKind.ClaudeCode, AgentModelLevel.High);

        await using var db = CreateContext();
        await CreateService(db).EscalateAsync(task.Id, to: null, CancellationToken.None);

        var detail = await LatestEscalationDetailAsync(task.Id);
        detail.ShouldStartWith("Escalated opus -> fable.");
        detail.ShouldNotContain("FRESH CONTEXT");
        detail.ShouldNotContain("grok");
    }

    // ---- dispatch event text (CARD-0084 S4) ----------------------------------------------------

    [Test]
    public async Task a_cold_grok_dispatch_records_a_grok_model_in_its_event()
    {
        // The Dispatched event is the durable record of what was started. It outlives the ephemeral
        // agent row it names, so "(sonnet)" on a Grok delegate is permanently wrong evidence — and
        // this is the event the board renders and a later reader reconstructs the run from.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness();
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.Grok);

        await dispatcher.TickAsync(CancellationToken.None);

        var detail = await LatestDispatchDetailAsync(task.Id);
        detail.ShouldStartWith("Dispatched to agent ");
        detail.ShouldContain("(grok-4.5)", customMessage: "the seeded task is Medium");
        detail.ShouldNotContain("sonnet");
    }

    [Test]
    public async Task a_cold_claude_dispatch_event_reads_exactly_as_it_did_before()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness();
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.ClaudeCode);

        await dispatcher.TickAsync(CancellationToken.None);

        var detail = await LatestDispatchDetailAsync(task.Id);
        detail.ShouldContain("(sonnet) in " + workspace.Path);
        // The alias, not the bare word: the harness's own temp directory is named after this
        // card and appears in the same string.
        detail.ShouldNotContain("grok-4");
    }

    [Test]
    public async Task a_warm_grok_reuse_records_a_grok_model_in_its_event()
    {
        // The reuse path builds its own event text — a separate call site, and the one that fires
        // on the cheap dispatches, so it is the text an operator sees most often.
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness();
        await SeedWarmAgentAsync(workspace.Path, AgentKind.Grok);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.Grok);

        await dispatcher.TickAsync(CancellationToken.None);

        var detail = await LatestDispatchDetailAsync(task.Id);
        detail.ShouldStartWith("Reused warm delegate ");
        detail.ShouldContain("(grok-4.5)");
        detail.ShouldNotContain("sonnet");
    }

    [Test]
    public async Task a_warm_claude_reuse_event_reads_exactly_as_it_did_before()
    {
        using var workspace = new TempWorkspace();
        var (dispatcher, _) = CreateDispatchHarness();
        await SeedWarmAgentAsync(workspace.Path, AgentKind.ClaudeCode);
        var task = await SeedQueuedTaskAsync(workspace.Path, AgentKind.ClaudeCode);

        await dispatcher.TickAsync(CancellationToken.None);

        var detail = await LatestDispatchDetailAsync(task.Id);
        detail.ShouldStartWith("Reused warm delegate ");
        detail.ShouldContain("(sonnet) in " + workspace.Path + " — no cold start");
        // The alias, not the bare word: the harness's own temp directory is named after this
        // card and appears in the same string.
        detail.ShouldNotContain("grok-4");
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<string> LatestDispatchDetailAsync(Guid taskId)
    {
        await using var db = CreateContext();
        var detail = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Dispatched)
            .OrderByDescending(e => e.At)
            .Select(e => e.Detail)
            .FirstOrDefaultAsync();
        return detail.ShouldNotBeNull();
    }

    private static AgentLaunchSpec SpecOf(AgentTaskDispatcher dispatcher, AgentTask task)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"task-{DelegationReportFormatter.Short(task.Id)}",
            Slug = $"task-{DelegationReportFormatter.Short(task.Id)}",
            WorkingDirectory = task.WorkingDirectory,
            Kind = task.AgentKind,
            IsPoolDelegate = true,
        };
        // The session row is the launch's own record of which program is being started — the
        // dispatcher writes it from the task one statement before it builds the spec.
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = task.AgentKind == AgentKind.Grok ? "grok" : "claude",
            AgentKind = task.AgentKind,
            Status = SessionStatus.Starting,
            Cwd = task.WorkingDirectory,
            Cols = 120,
            Rows = 30,
        };
        return dispatcher.BuildLaunchSpec(task, agent, session);
    }

    private static string ModelArgOf(AgentTaskDispatcher dispatcher, AgentTask task)
    {
        var args = SpecOf(dispatcher, task).Args.ToList();
        return args[args.IndexOf("--model") + 1];
    }

    private static AgentTask TaskFor(
        AgentKind agentKind,
        AgentModelLevel level,
        AgentTaskKind kind = AgentTaskKind.Worker,
        AgentTaskRole role = AgentTaskRole.Docs) => new()
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Role = role,
            AgentKind = agentKind,
            ModelLevel = level,
            Status = AgentTaskStatus.Queued,
            Goal = "make the composed launch arguments observable",
            WorkingDirectory = Path.GetTempPath(),
            Workspace = WorkspaceMode.Shared,
            CreatedAt = DateTime.UtcNow,
        };

    private static async Task<string> LatestEscalationDetailAsync(Guid taskId)
    {
        await using var db = CreateContext();
        var detail = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Escalated)
            .OrderByDescending(e => e.At)
            .Select(e => e.Detail)
            .FirstOrDefaultAsync();
        return detail.ShouldNotBeNull();
    }

    private static async Task<AgentTask> SeedSettledTaskAsync(
        string directory, AgentKind agentKind, AgentModelLevel level)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "escalate me",
            Goal = "escalate me",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Debug,
            AgentKind = agentKind,
            ModelLevel = level,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            // Queued, so the escalation needs no session to stop — the tier bump and its event are
            // the whole subject here.
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<AgentTask> SeedQueuedTaskAsync(string directory, AgentKind agentKind)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "do the next piece of work",
            Goal = "do the next piece of work",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            AgentKind = agentKind,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Ephemeral = true,
            Status = AgentTaskStatus.Queued,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedWarmAgentAsync(
        string directory, AgentKind kind, int idleMinutes = 5)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = kind == AgentKind.Grok ? "grok" : "claude",
            AgentKind = kind,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = $"task-{agentId:N}"[..13],
            Slug = $"task-{agentId:N}"[..13],
            WorkingDirectory = directory,
            Details = "Warm pool delegate.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.Medium,
            Kind = kind,
            IsPoolDelegate = true,
            // Well outside PoolReservedForCallerMinutes, so the reservation window is never what
            // decides these tests.
            PoolIdleSince = now.AddMinutes(-idleMinutes),
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static AgentTaskService CreateService(AppDbContext db) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings { AllowedRoots = [] }),
        new MockEventBus(),
        new RecordingSessionStopper(),
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance);

    private static (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) CreateHarness(
        int? budgetChars = null, bool withGrokDefinition = true, string? grokExe = null)
    {
        var (dispatcher, _, provider) = BuildHarness(budgetChars, withGrokDefinition, grokExe, null);
        return (dispatcher, provider);
    }

    private static (AgentTaskDispatcher Dispatcher, RecordingSessionStopper Stopper) CreateDispatchHarness(
        bool withGrokDefinition = true, int? poolMaxIdlePerDirectory = null)
    {
        var (dispatcher, stopper, _) = BuildHarness(null, withGrokDefinition, null, poolMaxIdlePerDirectory);
        return (dispatcher, stopper);
    }

    private static (AgentTaskDispatcher, RecordingSessionStopper, ServiceProvider) BuildHarness(
        int? budgetChars, bool withGrokDefinition, string? grokExe, int? poolMaxIdlePerDirectory)
    {
        var stopper = new RecordingSessionStopper();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        var delegation = new DelegationSettings
        {
            PoolReservedForCallerMinutes = 0,
            PoolIdleRetireMinutes = 600,
            PoolMaxIdlePerDirectory = poolMaxIdlePerDirectory ?? 8,
            // The fixture database is shared across suites; leftover Dispatched/Working rows from
            // other tests must never eat this harness's dispatch budget.
            MaxConcurrentTasks = 512,
        };
        if (budgetChars is int budget)
            delegation.CommandLineBudgetChars = budget;
        services.AddSingleton(Options.Create(delegation));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            if (withGrokDefinition)
            {
                s.Definitions["grok"] = new AgentDefinition
                {
                    Kind = "Grok",
                    // Bare and unresolvable by default so nothing can be spawned by the background
                    // launch queue; the fakegrok test points it at a real staged binary instead.
                    Exe = grokExe ?? "grok",
                    ArgsTemplate = ["--always-approve", "--no-alt-screen"],
                };
            }
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-grok-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (
            provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(),
            stopper,
            provider);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>A real directory on disk — the resolver verifies existence, so a fake path won't do.</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-grok-dispatch").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a delegate's stray file lock must not fail the test */ }
        }
    }
}
