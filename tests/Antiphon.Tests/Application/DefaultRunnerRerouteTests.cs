using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0659 V-4 (D-5). A task's host is fixed at create; a later model decision may move a remote
/// task between Grok, Claude Code and Codex, and never onto the desktop. Explicit
/// incompatible reroutes refuse without touching the row; automatic ones (queued rewalk, routing
/// resume, usage wall) Block with a stable <c>runner_kind_unsupported</c> detail that keeps the
/// runner, the original kind and the pin, and do so once per transition. The Block and the
/// parent's note commit together (review 5de2b154).
/// </summary>
[Category("Integration")]
public sealed class DefaultRunnerRerouteTests
{
    private const string Runner = "server2";

    [Test]
    public async Task C796_Explicit_desktop_codex_reroute_keeps_host()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Codex, AgentModelLevel.Frontier));
        foreach (var status in new[] { AgentTaskStatus.Queued, AgentTaskStatus.Blocked })
        {
            var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, status,
                runnerId: null);
            await using var beforeDb = CreateContext(schema);
            var before = await beforeDb.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            await using var rerouteDb = CreateContext(schema);
            var summary = await TaskService(rerouteDb, workspace.Path)
                .RerouteAsync(taskId, AgentKind.Codex, AgentModelLevel.Frontier, CancellationToken.None);
            summary.AgentKind.ShouldBe(AgentKind.Codex);
            await using var verify = CreateContext(schema);
            var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            stored.Status.ShouldBe(AgentTaskStatus.Queued);
            stored.FailureReason.ShouldBeNull();
            stored.AgentKind.ShouldBe(AgentKind.Codex);
            stored.ModelLevel.ShouldBe(before.ModelLevel);
            stored.Complexity.ShouldBe(before.Complexity);
            stored.RoutingPinId.ShouldBeNull("an explicit reroute ends chain governance");
            stored.ExplicitAgentKind.ShouldBeNull("the explicit API ends chain governance without a new create pin");
            stored.ExplicitModelLevel.ShouldBeNull();
            stored.RequiredPlatform.ShouldBe(before.RequiredPlatform);
            stored.RequirementSource.ShouldBe(before.RequirementSource);
            stored.RunnerDefaultsRevision.ShouldBe(before.RunnerDefaultsRevision);
            stored.RunnerId.ShouldBe(before.RunnerId);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(1);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked)).ShouldBe(0);
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == taskId)).ShouldBe(0);
        }
    }

    [Test]
    public async Task C796_Queued_walk_selects_desktop_codex()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Codex, AgentModelLevel.Frontier), (AgentKind.Grok, AgentModelLevel.Frontier));
        await SeedHoldAsync(schema, "fable");
        var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, AgentTaskStatus.Queued,
            runnerId: null, workspace: WorkspaceMode.Shared);
        var result = await CreateDispatcher(schema, eligible: false).Dispatcher.TickAsync(CancellationToken.None);
        result.BlockedRoutingExhausted.ShouldBe(0);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        AssertDesktopCodexSelected(stored, pin.Id, dispatched: true);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted))
            .ShouldBe(1);
        (await verify.AgentSessions.CountAsync(s => s.Id == stored.AgentSessionId)).ShouldBe(1);
    }

    [Test]
    public async Task C796_Blocked_resume_selects_desktop_codex()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Codex, AgentModelLevel.Frontier), (AgentKind.Grok, AgentModelLevel.Frontier));
        await SeedHoldAsync(schema, "fable");
        var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, AgentTaskStatus.Blocked,
            runnerId: null, workspace: WorkspaceMode.Shared);
        var result = await CreateDispatcher(schema, eligible: false).Dispatcher.TickAsync(CancellationToken.None);
        result.ResumedRoutingBlocked.ShouldBe(1);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        AssertDesktopCodexSelected(stored, pin.Id, dispatched: true);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted))
            .ShouldBe(1);
    }

    [Test]
    public async Task C796_Usage_wall_selects_desktop_codex()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Codex, AgentModelLevel.Frontier), (AgentKind.Grok, AgentModelLevel.Frontier));
        await SeedHoldAsync(schema, "fable");
        var sessionId = Guid.NewGuid();
        var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, AgentTaskStatus.Working,
            sessionId: sessionId, runnerId: null);
        await using var db = CreateContext(schema);
        var stopper = new RecordingSessionStopper();
        var decision = await TaskService(db, workspace.Path, stopper).RerouteOnWallAsync(
            await db.AgentTasks.SingleAsync(t => t.Id == taskId), "fable", "usage wall", false, CancellationToken.None);
        decision.Kind.ShouldBe(AgentTaskService.WallRerouteKind.Rerouted);
        stopper.Killed.ShouldBe([sessionId]);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        AssertDesktopCodexSelected(stored, pin.Id);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted))
            .ShouldBe(1);
    }

    [Test]
    public async Task C772_Allowed_kind_changes_preserve_host()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        foreach (var (runner, next) in new (string?, AgentKind)[]
                 { (Runner, AgentKind.Codex), (null, AgentKind.Grok) })
        {
            var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, null, AgentTaskStatus.Queued,
                runnerId: runner);
            await using var db = CreateContext(schema);
            var summary = await TaskService(db, workspace.Path).RerouteAsync(
                taskId, next, AgentModelLevel.High, CancellationToken.None);
            summary.AgentKind.ShouldBe(next);
            await using var verify = CreateContext(schema);
            var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            stored.RunnerId.ShouldBe(runner);
            stored.Status.ShouldBe(AgentTaskStatus.Queued);
        }
    }

    private static void AssertDesktopCodexSelected(AgentTask task, Guid pinId, bool dispatched = false)
    {
        task.Status.ShouldBe(dispatched ? AgentTaskStatus.Dispatched : AgentTaskStatus.Queued,
            task.FailureReason);
        if (task.FailureReason is { } reason)
            reason.ShouldNotContain("codex_desktop_unqualified");
        task.AgentKind.ShouldBe(AgentKind.Codex);
        task.RunnerId.ShouldBeNull();
        task.RoutingPinId.ShouldBe(pinId);
        if (dispatched) task.AgentSessionId.ShouldNotBeNull();
        else task.AgentSessionId.ShouldBeNull();
    }

    [Test]
    public async Task C796_Remote_legacy_kind_block_and_parent_note_commit_together()
    {
        foreach (var cut in new[] { BlockNoteCut.CrashAfterSave, BlockNoteCut.EnqueueFails })
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var parent = await BridgeQueueHarness.CreateAsync(new()
            { AlwaysOn = false, ConnectionString = schema.ConnectionString });
            using var workspace = new TempWorkspace();
            var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, null, AgentTaskStatus.Queued,
                kind: AgentKind.OpenCode, parentSessionId: parent.SessionId);
            var fault = new BlockNoteFault(cut, taskId, parent.SessionId);
            try
            {
                await CreateDispatcher(schema, eligible: false, interceptors: [fault]).Dispatcher.TickAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is SimulatedCutException || ex.InnerException is SimulatedCutException) { }
            fault.Fired.ShouldBeTrue();
            await using (var read = CreateContext(schema))
            {
                var stored = await read.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                var notes = await read.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == taskId);
                var events = await read.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked);
                if (cut == BlockNoteCut.CrashAfterSave)
                {
                    stored.Status.ShouldBe(AgentTaskStatus.Blocked);
                    notes.ShouldBe(1);
                    events.ShouldBe(1);
                }
                else
                {
                    stored.Status.ShouldBe(AgentTaskStatus.Queued);
                    notes.ShouldBe(0);
                    events.ShouldBe(0);
                }
            }
            await CreateDispatcher(schema, eligible: false).Dispatcher.TickAsync(CancellationToken.None);
            await AssertParentReceiptAsync(schema, parent, taskId, "runner_kind_unsupported");
        }
    }

    [Test]
    public async Task C796_Repeated_desktop_codex_resume_is_idempotent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await BridgeQueueHarness.CreateAsync(new()
        { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        using var workspace = new TempWorkspace();
        var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.Codex, AgentModelLevel.Frontier), (AgentKind.Grok, AgentModelLevel.Frontier));
        await SeedHoldAsync(schema, "fable");
        var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, AgentTaskStatus.Blocked,
            parentSessionId: parent.SessionId, runnerId: null, workspace: WorkspaceMode.Shared);
        for (var i = 0; i < 3; i++)
            await CreateDispatcher(schema, eligible: false).Dispatcher.TickAsync(CancellationToken.None);
        await using var read = CreateContext(schema);
        AssertDesktopCodexSelected(await read.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId), pin.Id, dispatched: true);
        (await read.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked)).ShouldBe(0);
        (await read.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == taskId)).ShouldBe(0);
        (await read.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(1);
    }

    [Test]
    public async Task C796_Remote_legacy_kind_block_note_reaches_parent()
    {
        foreach (var busy in new[] { false, true })
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var parent = await BridgeQueueHarness.CreateAsync(new()
            { AlwaysOn = false, ConnectionString = schema.ConnectionString });
            using var workspace = new TempWorkspace();
            if (busy)
            {
                await parent.InsertTurnAsync("prior question", "prior answer");
                await parent.MarkWorkingAsync();
            }
            var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, null, AgentTaskStatus.Queued,
                kind: AgentKind.OpenCode, parentSessionId: parent.SessionId);
            await CreateDispatcher(schema, eligible: false).Dispatcher.TickAsync(CancellationToken.None);
            await using (var read = CreateContext(schema))
            {
                var stored = await read.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                stored.Status.ShouldBe(AgentTaskStatus.Blocked);
                stored.FailureReason.ShouldContain("runner_kind_unsupported");
                (await read.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == taskId)).ShouldBe(1);
            }
            if (busy)
            {
                parent.Adapter.SubmittedBodies.ShouldBeEmpty();
                await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
                await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
            }
            await AssertParentReceiptAsync(schema, parent, taskId, "runner_kind_unsupported");
        }
    }

    [Test]
    public async Task C796_Remote_legacy_kind_block_note_recovers_after_restart()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await BridgeQueueHarness.CreateAsync(new()
        { AlwaysOn = false, ConnectionString = schema.ConnectionString, PreserveDatabaseOnDispose = true });
        using var workspace = new TempWorkspace();
        var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, null, AgentTaskStatus.Queued,
            kind: AgentKind.OpenCode, parentSessionId: parent.SessionId);
        var fault = new BlockNoteFault(BlockNoteCut.CrashAfterSave, taskId, parent.SessionId);
        try
        {
            await CreateDispatcher(schema, eligible: false, interceptors: [fault]).Dispatcher.TickAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is SimulatedCutException || ex.InnerException is SimulatedCutException) { }
        fault.Fired.ShouldBeTrue();
        await CreateDispatcher(schema, eligible: false).Dispatcher.TickAsync(CancellationToken.None);
        await using var recovered = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, ConnectionString = schema.ConnectionString,
            AttachSessionId = parent.SessionId, AttachAgentId = parent.AgentId,
            PreserveDatabaseOnDispose = true,
        });
        await AssertParentReceiptAsync(schema, recovered, taskId, "runner_kind_unsupported");
    }

    [Test]
    public async Task C796_Usage_wall_exhaustion_note_reaches_parent()
    {
        foreach (var cut in new[] { BlockNoteCut.CrashAfterSave, BlockNoteCut.EnqueueFails })
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var parent = await BridgeQueueHarness.CreateAsync(new()
            { AlwaysOn = false, ConnectionString = schema.ConnectionString });
            using var workspace = new TempWorkspace();
            var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                (AgentKind.Codex, AgentModelLevel.Frontier));
            await SeedHoldAsync(schema, "fable");
            await SeedHoldAsync(schema, "gpt-6-astra", AgentKind.Codex);
            var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, AgentTaskStatus.Working,
                sessionId: Guid.NewGuid(), parentSessionId: parent.SessionId, runnerId: null);
            var fault = new BlockNoteFault(cut, taskId, parent.SessionId);
            var intercepted = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(schema.ConnectionString).AddInterceptors(fault).Options;
            await using (var db = new AppDbContext(intercepted))
            {
                try
                {
                    await TaskService(db, workspace.Path).RerouteOnWallAsync(
                        await db.AgentTasks.SingleAsync(t => t.Id == taskId),
                        "fable", "usage wall", false, CancellationToken.None);
                }
                catch (Exception ex) when (ex is SimulatedCutException || ex.InnerException is SimulatedCutException) { }
            }
            fault.Fired.ShouldBeTrue(cut + ": the wall did not reach the fault cut");
            await using (var read = CreateContext(schema))
            {
                var stored = await read.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                var notes = await read.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == taskId);
                var events = await read.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
                    && e.Type == AgentTaskEventType.Blocked);
                if (cut == BlockNoteCut.CrashAfterSave)
                {
                    stored.Status.ShouldBe(AgentTaskStatus.Blocked);
                    stored.FailureReason.ShouldNotBeNull().ShouldStartWith(ComplexityRoutingService.RoutingExhaustedPrefix);
                    stored.RoutingPinId.ShouldBe(pin.Id);
                    notes.ShouldBe(1);
                    events.ShouldBe(1);
                }
                else
                {
                    stored.Status.ShouldBe(AgentTaskStatus.Working);
                    notes.ShouldBe(0);
                    events.ShouldBe(0);
                }
            }
            if (cut == BlockNoteCut.EnqueueFails)
            {
                await using var retryDb = CreateContext(schema);
                var retry = await TaskService(retryDb, workspace.Path).RerouteOnWallAsync(
                    await retryDb.AgentTasks.SingleAsync(t => t.Id == taskId),
                    "fable", "usage wall", false, CancellationToken.None);
                retry.Kind.ShouldBe(AgentTaskService.WallRerouteKind.Blocked);
            }
            await AssertParentReceiptAsync(schema, parent, taskId, ComplexityRoutingService.RoutingExhaustedPrefix);
        }
    }

    private static async Task AssertParentReceiptAsync(IsolatedTestSchema schema, BridgeQueueHarness parent, Guid taskId, string expectedReason)
    {
        await parent.Queue.FlushIfIdleAsync(parent.SessionId, CancellationToken.None);
        var submitted = parent.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        submitted.ShouldContain(expectedReason);
        submitted.ShouldContain(DelegationReportFormatter.Short(taskId));
        await using var read = CreateContext(schema);
        var note = await read.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.SourceTaskId == taskId);
        note.AgentSessionId.ShouldBe(parent.SessionId);
        note.Body.ShouldBe(submitted);
        note.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        var prompts = await read.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == parent.SessionId && e.Kind == TranscriptKinds.UserPrompt)
            .Select(e => e.Text)
            .ToListAsync();
        prompts.Count(p => p is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, p)).ShouldBe(1,
            "a complete recipient UserPrompt, rather than a queue row, proves delivery");
    }

    [Test]
    public async Task Explicit_incompatible_reroute_is_refused_without_mutation()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.Codex, AgentModelLevel.Frontier));

        foreach (var status in new[] { AgentTaskStatus.Blocked, AgentTaskStatus.Queued })
        {
            var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, status);
            await using var db = CreateContext(schema);

            // OpenCode is not a delegate kind, so the refusal is validation and happens
            // before the runner-kind conflict. The row is still untouched.
            var refused = await Should.ThrowAsync<ValidationException>(() => TaskService(db, workspace.Path)
                .RerouteAsync(taskId, AgentKind.OpenCode, AgentModelLevel.Frontier, CancellationToken.None));
            refused.Code.ShouldBe("validation_failed", status.ToString());
            string.Join(" ", refused.Errors.SelectMany(pair => pair.Value))
                .ShouldContain("not a delegate kind", Case.Sensitive, status.ToString());

            await using var verify = CreateContext(schema);
            var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            stored.Status.ShouldBe(status);
            stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);
            stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
            stored.RunnerId.ShouldBe(Runner);
            stored.RoutingPinId.ShouldBe(pin.Id, "a refused reroute does not end chain governance");
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId)).ShouldBe(0,
                status + ": a refusal writes no event");
        }
    }

    [Test]
    public async Task Queued_rewalk_blocks_incompatible_host_before_prep()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.Codex, AgentModelLevel.Frontier));
        await SeedHoldAsync(schema, "fable");
        var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, AgentTaskStatus.Queued);
        // Codex is a supported worker, so the rewalk keeps the runner. The runner is offline,
        // which holds the task without a launch.
        var world = CreateDispatcher(schema, eligible: false);

        var result = await world.Dispatcher.TickAsync(CancellationToken.None);

        result.BlockedRoutingExhausted.ShouldBe(0);
        result.Dispatched.ShouldBe(0);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.AgentKind.ShouldBe(AgentKind.Codex);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        stored.RunnerId.ShouldBe(Runner);
        stored.RoutingPinId.ShouldBe(pin.Id);
        stored.WorktreePath.ShouldBeNull("an offline runner holds; it does not cut a worktree");
        stored.RemoteWorktreePath.ShouldBeNull();
        stored.AgentSessionId.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted))
            .ShouldBe(1);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(0);
        (await verify.AgentSessions.CountAsync()).ShouldBe(0, "no session anywhere, desktop or runner");
        world.Directory.ResolveCalls.ShouldNotContain((string?)null, "nothing was prepared against the desktop runner");

        // A persisted/legacy mismatch (no rewalk involved) is fenced before any claim as well. CARD-0660:
        // an explicitly placed Codex task is no longer a mismatch, so the fenced kind is OpenCode.
        var legacyId = await SeedRemoteTaskAsync(schema, workspace.Path, routingPinId: null, AgentTaskStatus.Queued,
            kind: AgentKind.OpenCode);
        var second = await CreateDispatcher(schema, eligible: true).Dispatcher.TickAsync(CancellationToken.None);
        second.Dispatched.ShouldBe(0);
        await using var verify2 = CreateContext(schema);
        var legacy = await verify2.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == legacyId);
        legacy.Status.ShouldBe(AgentTaskStatus.Blocked);
        legacy.AgentKind.ShouldBe(AgentKind.OpenCode);
        legacy.RunnerId.ShouldBe(Runner);
        legacy.FailureReason.ShouldNotBeNull().ShouldContain("runner_kind_unsupported");
        legacy.WorktreePath.ShouldBeNull();
        (await verify2.AgentSessions.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Routing_resume_does_not_loop_on_incompatible_candidate()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.Codex, AgentModelLevel.Frontier));
        await SeedHoldAsync(schema, "fable");
        var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, AgentTaskStatus.Blocked);

        var first = await CreateDispatcher(schema, eligible: false).Dispatcher.TickAsync(CancellationToken.None);
        first.ResumedRoutingBlocked.ShouldBe(1, "Codex is a supported worker, so the held Claude head resumes onto it");
        first.Dispatched.ShouldBe(0);

        for (var tick = 2; tick <= 3; tick++)
        {
            var result = await CreateDispatcher(schema, eligible: false).Dispatcher.TickAsync(CancellationToken.None);
            result.ResumedRoutingBlocked.ShouldBe(0, $"tick {tick}: the resume already published Codex");
            result.Dispatched.ShouldBe(0, $"tick {tick}");
        }

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Queued, "the runner is offline, so the resumed task is held");
        stored.AgentKind.ShouldBe(AgentKind.Codex);
        stored.RunnerId.ShouldBe(Runner);
        stored.RoutingPinId.ShouldBe(pin.Id);
        stored.AgentSessionId.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(0);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted))
            .ShouldBe(1, "one resume, none per later tick");
        (await verify.AgentSessions.CountAsync()).ShouldBe(0);

        await using (var db = CreateContext(schema))
        {
            db.ModelAvailabilityHolds.RemoveRange(db.ModelAvailabilityHolds);
            await db.SaveChangesAsync();
        }

        var freed = await CreateDispatcher(schema, eligible: false).Dispatcher.TickAsync(CancellationToken.None);
        freed.ResumedRoutingBlocked.ShouldBe(0, "clearing the Claude hold does not walk the task back off Codex");
        await using var verify2 = CreateContext(schema);
        var resumed = await verify2.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        resumed.Status.ShouldBe(AgentTaskStatus.Queued);
        resumed.AgentKind.ShouldBe(AgentKind.Codex);
        resumed.RunnerId.ShouldBe(Runner);
        resumed.AgentSessionId.ShouldBeNull();
    }

    [Test]
    public async Task Usage_wall_blocks_and_releases_existing_process()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.Codex, AgentModelLevel.Frontier));
        var sessionId = Guid.NewGuid();
        var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, AgentTaskStatus.Working, sessionId: sessionId);
        await SeedHoldAsync(schema, "fable");

        await using var db = CreateContext(schema);
        var stopper = new RecordingSessionStopper();
        var tasks = TaskService(db, workspace.Path, stopper);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);

        var decision = await tasks.RerouteOnWallAsync(
            task, "fable", "fable hit a usage wall", sessionLimitHasScheduledResume: false, CancellationToken.None);

        decision.Kind.ShouldBe(AgentTaskService.WallRerouteKind.Rerouted);
        stopper.Killed.ShouldBe([sessionId], "the walled process is released through the existing stop contract");
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.AgentKind.ShouldBe(AgentKind.Codex);
        stored.RunnerId.ShouldBe(Runner);
        stored.RoutingPinId.ShouldBe(pin.Id);
        stored.AgentSessionId.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(0);
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Rerouted))
            .ShouldBe(1);
    }

    [Test]
    public async Task Compatible_reroute_preserves_host_and_pin_order()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var pin = await SeedPinAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier), (AgentKind.Grok, AgentModelLevel.Frontier));
        var candidatesBefore = pin.CandidatesJson;
        await SeedHoldAsync(schema, "fable");

        // Queued rewalk Claude -> Grok keeps the runner and the pin. The dispatcher runs with the
        // default changed to the desktop: placement is never recalculated from later config.
        var queuedId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, AgentTaskStatus.Queued);
        var world = CreateDispatcher(schema, eligible: false, defaultRunnerId: "local");
        var result = await world.Dispatcher.TickAsync(CancellationToken.None);

        result.Dispatched.ShouldBe(0, "an offline runner holds; it is never a desktop launch");
        await using (var verify = CreateContext(schema))
        {
            var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == queuedId);
            stored.Status.ShouldBe(AgentTaskStatus.Queued);
            stored.AgentKind.ShouldBe(AgentKind.Grok);
            stored.RunnerId.ShouldBe(Runner);
            stored.RoutingPinId.ShouldBe(pin.Id);
            stored.AgentSessionId.ShouldBeNull();
            (await verify.RoutingPins.AsNoTracking().SingleAsync(p => p.Id == pin.Id)).CandidatesJson
                .ShouldBe(candidatesBefore, "the pin's candidate order is untouched");
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == queuedId && e.Type == AgentTaskEventType.Rerouted))
                .ShouldBe(1);
            (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == queuedId && e.Type == AgentTaskEventType.Blocked))
                .ShouldBe(0);
            (await verify.AgentSessions.CountAsync()).ShouldBe(0);
        }

        // Explicit reroute to a compatible kind keeps the runner too.
        var blockedId = await SeedRemoteTaskAsync(schema, workspace.Path, pin.Id, AgentTaskStatus.Blocked);
        await using var db = CreateContext(schema);
        var summary = await TaskService(db, workspace.Path)
            .RerouteAsync(blockedId, AgentKind.Grok, AgentModelLevel.High, CancellationToken.None);
        summary.AgentKind.ShouldBe(AgentKind.Grok);
        await using var verify2 = CreateContext(schema);
        var rerouted = await verify2.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == blockedId);
        rerouted.Status.ShouldBe(AgentTaskStatus.Queued);
        rerouted.RunnerId.ShouldBe(Runner);
        (await verify2.AgentTaskEvents.CountAsync(e => e.AgentTaskId == blockedId && e.Type == AgentTaskEventType.Rerouted))
            .ShouldBe(1);
    }

    [Test]
    public async Task Runner_kind_block_and_parent_note_commit_together()
    {
        // Review 5de2b154. Every other routing-exhausted Block stages its parent note as a
        // SessionQueuedMessages row in the SAME save as the Blocked state (the queue row is the
        // durable outbox the delivery loop drains after a restart). A runner_kind_unsupported Block
        // must do the same: a crash after the save keeps both, and a failed enqueue saves neither,
        // so the next tick can still Block the task and tell the parent once.
        foreach (var cut in new[] { BlockNoteCut.CrashAfterSave, BlockNoteCut.EnqueueFails })
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            using var workspace = new TempWorkspace();
            var parentSessionId = await SeedParentSessionAsync(schema, workspace.Path);
            // A persisted kind the runner cannot run (OpenCode; CARD-0660 admits an explicit Codex):
            // the pre-claim fence Blocks it on the first tick.
            var taskId = await SeedRemoteTaskAsync(schema, workspace.Path, routingPinId: null, AgentTaskStatus.Queued,
                kind: AgentKind.OpenCode, parentSessionId: parentSessionId);
            var fault = new BlockNoteFault(cut, taskId, parentSessionId);

            try
            {
                await CreateDispatcher(schema, eligible: true, interceptors: [fault]).Dispatcher.TickAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is SimulatedCutException || ex.InnerException is SimulatedCutException)
            {
                // The process "died" at the cut; what survives is whatever was committed.
            }

            fault.Fired.ShouldBeTrue(cut + ": the fault never reached its cut, so the test proved nothing");
            await using (var verify = CreateContext(schema))
            {
                var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                var notes = await verify.SessionQueuedMessages.CountAsync(
                    m => m.AgentSessionId == parentSessionId && m.SourceTaskId == taskId);
                var blockedEvents = await verify.AgentTaskEvents.CountAsync(
                    e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked);
                if (cut == BlockNoteCut.CrashAfterSave)
                {
                    stored.Status.ShouldBe(AgentTaskStatus.Blocked, cut.ToString());
                    notes.ShouldBe(1, cut + ": the committed Block carries its parent note");
                    blockedEvents.ShouldBe(1, cut.ToString());
                }
                else
                {
                    stored.Status.ShouldBe(AgentTaskStatus.Queued, cut + ": no Block is committed without its note");
                    stored.FailureReason.ShouldBeNull(cut.ToString());
                    notes.ShouldBe(0, cut.ToString());
                    blockedEvents.ShouldBe(0, cut.ToString());
                }
            }

            // Restart: a fresh dispatcher with no fault. Either way the parent is told exactly once.
            await CreateDispatcher(schema, eligible: true).Dispatcher.TickAsync(CancellationToken.None);
            await using (var verify = CreateContext(schema))
            {
                var stored = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
                stored.Status.ShouldBe(AgentTaskStatus.Blocked, cut + " after restart");
                stored.FailureReason.ShouldNotBeNull().ShouldContain("runner_kind_unsupported");
                var notes = await verify.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.AgentSessionId == parentSessionId && m.SourceTaskId == taskId)
                    .ToListAsync();
                notes.Count.ShouldBe(1, cut + " after restart: one note, never lost and never duplicated");
                notes[0].Status.ShouldBe(QueuedMessageStatus.Pending, cut.ToString());
                notes[0].Origin.ShouldBe(QueuedMessageOrigin.Delegation, cut.ToString());
                (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Blocked))
                    .ShouldBe(1, cut + " after restart");
                (await verify.AgentSessions.CountAsync(s => s.Id != parentSessionId)).ShouldBe(0, cut.ToString());
            }
        }
    }

    private static void AssertRunnerKindBlocked(AgentTask stored, Guid pinId)
    {
        stored.Status.ShouldBe(AgentTaskStatus.Blocked);
        stored.FailureReason.ShouldNotBeNull().ShouldStartWith(ComplexityRoutingService.RoutingExhaustedPrefix,
            customMessage: "the existing routing-exhausted prefix lets the operator reroute with the existing API");
        stored.FailureReason.ShouldContain("runner_kind_unsupported");
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode, "the original kind is preserved");
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        stored.RunnerId.ShouldBe(Runner, "never migrated to the desktop");
        stored.RoutingPinId.ShouldBe(pinId, "the pin list is preserved");
    }

    private static async Task<RoutingPin> SeedPinAsync(
        IsolatedTestSchema schema, params (AgentKind Kind, AgentModelLevel Level)[] candidates)
    {
        await using var db = CreateContext(schema);
        var pin = new RoutingPin
        {
            Id = Guid.NewGuid(),
            Role = AgentTaskRole.Code,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Required,
            CandidatesJson = RoutingCandidate.Serialize(candidates.Select(c => new RoutingCandidate(c.Kind, c.Level)).ToList()),
            Reason = "c659 reroute pin",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.RoutingPins.Add(pin);
        await db.SaveChangesAsync();
        return pin;
    }

    private static async Task SeedHoldAsync(IsolatedTestSchema schema, string alias, AgentKind kind = AgentKind.ClaudeCode)
    {
        await using var db = CreateContext(schema);
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual,
            HitAt = DateTime.UtcNow,
            Reason = "c659 hold",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedRemoteTaskAsync(
        IsolatedTestSchema schema,
        string directory,
        Guid? routingPinId,
        AgentTaskStatus status,
        AgentKind kind = AgentKind.ClaudeCode,
        Guid? sessionId = null,
        Guid? parentSessionId = null,
        string? runnerId = Runner,
        WorkspaceMode workspace = WorkspaceMode.Worktree)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext(schema);
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "c659 remote " + status,
            Goal = "code it",
            Role = AgentTaskRole.Code,
            AgentKind = kind,
            ModelLevel = AgentModelLevel.Frontier,
            RoutingPinId = routingPinId,
            Workspace = workspace,
            WorkingDirectory = directory,
            RunnerId = runnerId,
            Status = status,
            AgentSessionId = sessionId,
            ParentSessionId = parentSessionId,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            FailureReason = status == AgentTaskStatus.Blocked
                ? ComplexityRoutingService.RoutingExhaustedPrefix + "stage Code pin (human, required) - fable held"
                : null,
            CreatedAt = DateTime.UtcNow,
            DispatchedAt = status == AgentTaskStatus.Working ? DateTime.UtcNow : null,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedParentSessionAsync(IsolatedTestSchema schema, string directory)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext(schema);
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            Status = SessionStatus.Running,
            Cwd = directory,
            AgentKind = AgentKind.ClaudeCode,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private enum BlockNoteCut
    {
        CrashAfterSave,
        EnqueueFails,
    }

    private sealed class SimulatedCutException(string cut) : Exception("simulated cut: " + cut);

    /// <summary>
    /// The two failure cuts around a runner_kind_unsupported Block. <c>CrashAfterSave</c> kills the
    /// dispatcher immediately after the first save that commits this task's Blocked event, so
    /// anything staged for a later save is lost. <c>EnqueueFails</c> fails the parent-note enqueue
    /// at its read of the parent's queue sequence, before the note row is staged.
    /// </summary>
    private sealed class BlockNoteFault(BlockNoteCut cut, Guid taskId, Guid parentSessionId)
        : DbCommandInterceptor, ISaveChangesInterceptor
    {
        private bool _blockSaving;

        public bool Fired { get; private set; }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _blockSaving = cut == BlockNoteCut.CrashAfterSave && !Fired
                && eventData.Context!.ChangeTracker.Entries<AgentTaskEvent>().Any(e =>
                    e.State == EntityState.Added && e.Entity.AgentTaskId == taskId
                    && e.Entity.Type == AgentTaskEventType.Blocked);
            return ValueTask.FromResult(result);
        }

        public ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!_blockSaving)
                return ValueTask.FromResult(result);
            _blockSaving = false;
            Fired = true;
            throw new SimulatedCutException("crash after the Blocked save");
        }

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            FailEnqueue(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            FailEnqueue(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void FailEnqueue(System.Data.Common.DbCommand command)
        {
            if (cut != BlockNoteCut.EnqueueFails || Fired
                || !command.CommandText.Contains("\"SessionQueuedMessages\"", StringComparison.Ordinal)
                || !command.CommandText.Contains("max(", StringComparison.OrdinalIgnoreCase)
                || !command.Parameters.Cast<System.Data.Common.DbParameter>()
                    .Any(p => p.Value is Guid id && id == parentSessionId))
                return;
            Fired = true;
            throw new SimulatedCutException("parent note enqueue failed");
        }
    }

    private static AgentTaskService TaskService(AppDbContext db, string directory, RecordingSessionStopper? stopper = null)
    {
        var settings = Options.Create(new DelegationSettings { AllowedRoots = [directory] });
        var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            settings,
            new MockEventBus(),
            stopper ?? new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: availability,
            routingPins: new RoutingPinService(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance),
            complexityRouting: new ComplexityRoutingService(db, settings, TimeProvider.System, availability));
    }

    private sealed record DispatcherWorld(AgentTaskDispatcher Dispatcher, DefaultRunnerKit.FakeRunnerDirectory Directory);

    /// <summary>
    /// The real dispatcher with the real pin/walk services, a controlled runner directory (the
    /// remote hold answers from it) and every kind defined, so an incompatible launch would not be
    /// stopped by a missing definition instead of by the fence under test.
    /// </summary>
    private static DispatcherWorld CreateDispatcher(
        IsolatedTestSchema schema, bool eligible, string? defaultRunnerId = Runner, IInterceptor[]? interceptors = null)
    {
        var directory = new DefaultRunnerKit.FakeRunnerDirectory(eligible, fault: null, grokAuth: null);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(schema.ConnectionString);
            if (interceptors is not null)
                o.AddInterceptors(interceptors);
        });
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 512,
            DefaultRunnerId = defaultRunnerId,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            s.Definitions["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok" };
            s.Definitions["codex"] = new AgentDefinition { Kind = "Codex", Exe = "codex" };
        });
        services.AddSingleton<ISessionRunnerDirectory>(directory);
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c659-reroute"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<RoutingPinService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<ComplexityRoutingService>();
        services.AddScoped<AgentTaskDispatcher>();
        var dispatcher = services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<AgentTaskDispatcher>();
        return new DispatcherWorld(dispatcher, directory);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c659-reroute").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
