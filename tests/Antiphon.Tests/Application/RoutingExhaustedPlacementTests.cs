using System.Data.Common;
using Antiphon.Server.Application.Dtos;
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

/// <summary>CARD-1021: real create, durable projections, dispatcher and parent queue; no live provider.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RoutingExhaustedPlacementTests
{
    [Test]
    [Arguments("windows", RequiredPlatform.Windows, false)]
    [Arguments("windows", RequiredPlatform.Linux, false)]
    [Arguments("linux", RequiredPlatform.Windows, false)]
    [Arguments("linux", RequiredPlatform.Linux, false)]
    [Arguments("windows", RequiredPlatform.Windows, true)]
    [Arguments("windows", RequiredPlatform.Linux, true)]
    [Arguments("linux", RequiredPlatform.Windows, true)]
    [Arguments("linux", RequiredPlatform.Linux, true)]
    public async Task C1021_exhausted_automatic_placement_is_platform_symmetric(
        string local, RequiredPlatform platform, bool chain)
    {
        await using var s = await Scenario.CreateAsync(chain, local);
        var want = platform == RequiredPlatform.Windows ? "windows" : "linux";
        await s.DefaultsAsync(want == local ? "server2" : "desktop");
        var before = await s.IdsAsync();
        var created = await s.CreateAsync(s.Request with { RequiredPlatform = platform });
        created.Status.ShouldBe(AgentTaskStatus.Blocked, "durable-blocked");
        var task = await s.AssertBlockedAsync(created.Id);
        task.RunnerId.ShouldBe(want == local ? null : "server2", "platform-runner");
        task.ObservedPlatform.ShouldBe(want, "observed-platform");
        task.RequiredPlatform.ShouldBe(platform);
        task.RequirementSource.ShouldBe(RequirementSource.Request);
        task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.Fallback, "selection-source");
        task.RunnerDefaultsRevision.ShouldBe(s.Revision, "defaults-revision");
        task.PlacementReason.ShouldBe("platform_match");
        created.Routing.ShouldNotBeNull();
        created.Routing.Candidates.Select(x => x.Outcome).ShouldBe(["skipped", "skipped"]);
        created.Routing.Candidates.ShouldAllBe(x => x.Reason == "held (manual, no re-enable time)");
        (await s.IdsAsync()).Tasks.Except(before.Tasks).ShouldBe([created.Id], "one-new-task");
        await using var read = s.Context();
        var audit = await read.AgentTaskEvents.SingleAsync(x => x.AgentTaskId == created.Id && x.Type == AgentTaskEventType.Created);
        audit.Detail.ShouldContain("source=fallback");
        audit.Detail.ShouldContain("reason=platform_match");
        if (chain)
        {
            var list = await read.ComplexityChains.AsNoTracking().SingleAsync();
            list.CandidatesJson.ShouldBe(s.ListJson);
            list.Provenance.ShouldBe(RoutingPinProvenance.Human);
            list.Reason.ShouldBe(Scenario.ListReason);
        }
        else
        {
            var list = await read.RoutingPins.AsNoTracking().SingleAsync();
            list.CandidatesJson.ShouldBe(s.ListJson);
            list.Provenance.ShouldBe(RoutingPinProvenance.Human);
            list.Reason.ShouldBe(Scenario.ListReason);
        }
    }

    [Test]
    [Arguments("kind")]
    [Arguments("global")]
    [Arguments("fallback")]
    public async Task C1021_any_exhaustion_honors_kind_then_global_default(string preference)
    {
        await using var s = await Scenario.CreateAsync(false);
        await s.DefaultsAsync(preference == "kind" ? "desktop" : preference == "global" ? "server2" : null,
            preference == "kind" ? "server2" : null);
        var created = await s.CreateAsync(s.Request with { RequiredPlatform = RequiredPlatform.Any });
        var task = await s.AssertBlockedAsync(created.Id);
        task.RunnerId.ShouldBe(preference == "fallback" ? null : "server2", "any-runner");
        task.RunnerSelectionSource.ShouldBe(preference switch
        {
            "kind" => RunnerSelectionSource.KindDefault,
            "global" => RunnerSelectionSource.GlobalDefault,
            _ => (RunnerSelectionSource?)null,
        }, "selection-source");
        task.RunnerDefaultsRevision.ShouldBe(s.Revision, "defaults-revision");
        created.Routing!.Candidates.ShouldNotContain(x => x.Outcome == "chosen");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1021_bound_exhaustion_is_visible_and_remains_blocked(bool chain)
    {
        await using var s = await Scenario.CreateAsync(chain);
        await using var parent = await s.ParentAsync();
        await s.SeedCardAsync();
        var created = await s.CreateAsync(s.Request with { Card = s.CardId.ToString() }, parent.SessionId);
        var task = await s.AssertBlockedAsync(created.Id);
        task.RoutingPinId.ShouldBe(chain ? null : s.ListId, "routing-pin-id");
        task.Complexity.ShouldBe(chain ? TaskComplexity.Hard : null, "complexity");
        task.CardId.ShouldBe(s.CardId, "card-binding");
        await s.AssertNoteAsync(created.Id, parent.SessionId);
        var originalSessions = await s.SessionIdsAsync();
        for (var tick = 0; tick < 3; tick++)
        {
            var result = await s.TickAsync();
            result.ResumedRoutingBlocked.ShouldBe(0);
            await s.AssertBlockedAsync(created.Id);
        }
        s.Observer.Transitions.Where(t => t.Id == created.Id).ShouldAllBe(t => t.Status == AgentTaskStatus.Blocked,
            "durable-blocked: no transient requeue while held");
        (await s.SessionIdsAsync()).ShouldBe(originalSessions, "no-session");
        s.AssertNoLaunch();
        await using var read = s.Context();
        var detail = await s.Kit.Service(read).GetAsync(created.Id, CancellationToken.None);
        detail.Summary.Status.ShouldBe(AgentTaskStatus.Blocked);
        var attention = await new AttentionService(read, s.Directory.Client,
            Options.Create(new SupervisionSettings()), Options.Create(s.Kit.Settings), TimeProvider.System,
            NullLogger<AttentionService>.Instance).GetAsync(CancellationToken.None);
        var row = attention.Items.Single(x => x.Kind == AttentionKind.RoutingExhausted && x.TaskId == created.Id);
        row.Severity.ShouldBe(AlertSeverity.Error, "routing-attention");
        row.CardId.ShouldBe(s.CardId, "routing-attention");
        row.BoardId.ShouldBe(s.BoardId, "routing-attention");
        row.Evidence.ShouldContain("CARD-1021");
        var options = Options.Create(s.Kit.Settings);
        var pipeline = await new AgentTaskPipelineStatusService(read, options,
            new AreaMapLoader(options, NullLogger<AreaMapLoader>.Instance), TimeProvider.System).GetAsync(CancellationToken.None);
        var blocked = pipeline.Stages.Single(x => x.Role == AgentTaskRole.Code).Blocked.Single(x => x.TaskId == created.Id);
        blocked.RoutingExhausted.ShouldBeTrue("pipeline-blocked");
        blocked.Card!.Id.ShouldBe(s.CardId);
        var git = new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance);
        var thread = await new CardThreadService(read,
            new PlanCatalogService(TimeProvider.System, NullLogger<PlanCatalogService>.Instance, git),
            git, TimeProvider.System, NullLogger<CardThreadService>.Instance).GetAsync(s.CardId, CancellationToken.None);
        thread.Tasks.ShouldContain(x => x.Id == created.Id && x.Status == AgentTaskStatus.Blocked, "thread-blocked");
        var card = await read.Cards.AsNoTracking().SingleAsync();
        card.Status.ShouldBe(CardStatus.Backlog);
        card.BoardColumnId.ShouldBe(s.ColumnId);
        (await read.CardRevisions.CountAsync()).ShouldBe(0);
    }

    [Test]
    [Arguments(false, RequiredPlatform.Windows)]
    [Arguments(false, RequiredPlatform.Linux)]
    [Arguments(true, RequiredPlatform.Windows)]
    [Arguments(true, RequiredPlatform.Linux)]
    public async Task C1021_refuse_if_exhausted_stays_opt_in(bool chain, RequiredPlatform platform)
    {
        await using var s = await Scenario.CreateAsync(chain);
        var request = s.Request with { RequiredPlatform = platform, RefuseIfExhausted = true };
        var before = await s.IdsAsync();
        var error = await Should.ThrowAsync<RoutingExhaustedException>(() => s.CreateAsync(request), "opt-in-refusal");
        error.StatusCode.ShouldBe(409);
        error.Code.ShouldBe("routing_exhausted");
        error.Routing.Candidates.Select(x => x.AgentKind).ShouldBe([AgentKind.Grok, AgentKind.ClaudeCode]);
        error.Routing.Candidates.ShouldAllBe(x => x.Outcome == "skipped" && x.Reason == "held (manual, no re-enable time)");
        await s.AssertIdsAsync(before, "opt-in-refusal");
        var accepted = await s.CreateAsync(request with { RefuseIfExhausted = false });
        await s.AssertBlockedAsync(accepted.Id);
    }

    [Test]
    [Arguments("explicit-mismatch")]
    [Arguments("explicit-unknown")]
    [Arguments("shared")]
    [Arguments("wrong-os")]
    [Arguments("preferred-ineligible")]
    [Arguments("fallback-ineligible")]
    [Arguments("preferred-missing-feature")]
    [Arguments("fallback-missing-feature")]
    public async Task C1021_real_placement_refusals_remain(string problem)
    {
        await using var s = await Scenario.CreateAsync(false);
        var request = s.Request;
        var code = RunnerPlatformProblems.Unavailable;
        switch (problem)
        {
            case "explicit-mismatch": request = request with { RunnerId = "local" }; code = RunnerPlatformProblems.Mismatch; break;
            case "explicit-unknown": request = request with { RunnerId = "server2" }; s.Directory.RemotePlatform = null; code = RunnerPlatformProblems.Unknown; break;
            case "shared": request = request with { Workspace = WorkspaceMode.Shared }; break;
            case "wrong-os": s.Directory.RemotePlatform = "windows"; break;
            default:
                if (problem.StartsWith("fallback")) await s.DefaultsAsync(null);
                if (problem.EndsWith("ineligible")) s.Directory.Eligible = false;
                else s.Directory.Feature = false;
                break;
        }
        var before = await s.IdsAsync();
        var error = await Should.ThrowAsync<ConflictException>(() => s.CreateAsync(request), "placement-refusal");
        error.StatusCode.ShouldBe(409);
        error.Code.ShouldBe(code, "placement-refusal");
        await s.AssertIdsAsync(before, "placement-refusal");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1021_returning_capacity_keeps_host_and_platform(bool chain)
    {
        await using var s = await Scenario.CreateAsync(chain);
        var created = await s.CreateAsync();
        await s.AssertBlockedAsync(created.Id);
        // Only after placement: leave the approved recovery visible in Queued, before launch.
        s.Directory.Eligible = false;
        await using (var db = s.Context())
            await db.ModelAvailabilityHolds.Where(x => x.Kind == AgentKind.ClaudeCode).ExecuteDeleteAsync();
        (await s.TickAsync()).ResumedRoutingBlocked.ShouldBe(1);
        for (var tick = 0; tick < 2; tick++) (await s.TickAsync()).ResumedRoutingBlocked.ShouldBe(0);
        await using var read = s.Context();
        var task = await read.AgentTasks.AsNoTracking().SingleAsync();
        task.Id.ShouldBe(created.Id, "recovery-task-id");
        task.RunnerId.ShouldBe("server2", "recovery-host");
        task.RequiredPlatform.ShouldBe(RequiredPlatform.Linux, "recovery-platform");
        task.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        task.ModelLevel.ShouldBe(AgentModelLevel.High);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.FailureReason.ShouldBeNull();
        task.AgentSessionId.ShouldBeNull();
        (await read.AgentTaskEvents.CountAsync(x => x.AgentTaskId == created.Id && x.Type == AgentTaskEventType.Rerouted)).ShouldBe(1);
        var transition = s.Observer.Transitions.Last(x => x.Id == created.Id && x.Status == AgentTaskStatus.Queued);
        transition.Runner.ShouldBe("server2", "recovery-host");
        transition.Platform.ShouldBe(RequiredPlatform.Linux, "recovery-platform");
        s.AssertNoLaunch();
    }

    [Test]
    [Arguments(AgentKind.Grok)]
    [Arguments(AgentKind.Codex)]
    [Arguments(AgentKind.ClaudeCode)]
    public async Task C1021_exhausted_create_skips_provider_probe_and_launch(AgentKind head)
    {
        await using var s = await Scenario.CreateAsync(false, head: head);
        var registry = new AgentRegistrySettings { GrokCredentialProbeEnabled = true };
        registry.Definitions["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok" };
        s.Kit.PhoneHome.CodexAuthProbeEnabled = true;
        AgentTaskCreatedDto? created = null;
        try { created = await s.CreateAsync(registry: registry); }
        catch (ProviderSignInRequiredException) { /* Assert the probe before the successful-create obligation. */ }
        s.Directory.Client.Providers.Count.ShouldBe(0, "provider-probe-count");
        created.ShouldNotBeNull("durable-blocked");
        (await s.AssertBlockedAsync(created.Id)).RunnerId.ShouldBe("server2");
        s.AssertNoLaunch();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task C1021_blocked_parent_note_reaches_busy_or_idle_recipient(bool chain, bool busy)
    {
        await using var s = await Scenario.CreateAsync(chain);
        await using var parent = await s.ParentAsync();
        await using var decoy = await s.ParentAsync();
        if (busy) await parent.MarkWorkingAsync();
        var floor = await s.TranscriptFloorAsync(parent.SessionId);
        var created = await s.CreateAsync(parentSession: parent.SessionId);
        var note = await s.AssertNoteAsync(created.Id, parent.SessionId);
        await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        if (busy)
        {
            parent.Adapter.SubmittedBodies.ShouldBeEmpty("busy-no-prompt");
            (await s.PromptsAsync(parent.SessionId)).ShouldBeEmpty("busy-no-prompt");
            await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
        }
        await s.AssertReceiptAsync(note, parent, floor, "complete-parent-receipt");
        await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        await s.TickAsync();
        await s.AssertReceiptAsync(note, parent, floor, "single-parent-receipt");
        parent.Adapter.SubmittedBodies.Count.ShouldBe(1, "single-parent-receipt");
        (await s.PromptsAsync(decoy.SessionId)).ShouldBeEmpty();
        decoy.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    [Test]
    [Arguments(false, "before-save")]
    [Arguments(true, "before-save")]
    [Arguments(false, "enqueue-fails")]
    [Arguments(true, "enqueue-fails")]
    [Arguments(false, "after-commit-before-wake")]
    [Arguments(true, "after-commit-before-wake")]
    [Arguments(false, "attempt-before-input")]
    [Arguments(true, "attempt-before-input")]
    [Arguments(false, "submit-before-verdict")]
    [Arguments(true, "submit-before-verdict")]
    public async Task C1021_blocked_parent_note_recovers_at_each_handoff(bool chain, string cut)
    {
        await using var s = await Scenario.CreateAsync(chain);
        var fault = new CutFault(cut);
        var parent = await s.ParentAsync(fault);
        var sessionId = parent.SessionId;
        var agentId = parent.AgentId;
        var floor = await s.TranscriptFloorAsync(sessionId);
        var before = await s.IdsAsync();
        Guid taskId;
        SessionQueuedMessage note;
        var submittedBeforeRestart = 0;
        try
        {
            if (cut is "before-save" or "enqueue-fails" or "after-commit-before-wake")
            {
                await Should.ThrowAsync<CutException>(() => s.CreateAsync(parentSession: sessionId, fault: fault));
                fault.Fired.ShouldBeTrue("cut-fired");
                if (cut is "before-save" or "enqueue-fails")
                {
                    await s.AssertIdsAsync(before, "atomic-before-commit");
                    taskId = (await s.CreateAsync(parentSession: sessionId)).Id;
                }
                else
                {
                    // The create committed. Recover the known durable identity; never resubmit POST.
                    taskId = (await s.IdsAsync()).Tasks.Except(before.Tasks).ShouldHaveSingleItem();
                }
            }
            else
            {
                taskId = (await s.CreateAsync(parentSession: sessionId)).Id;
                await Should.ThrowAsync<CutException>(() => parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None));
                fault.Fired.ShouldBeTrue("cut-fired");
            }
            await s.AssertBlockedAsync(taskId);
            note = await s.AssertNoteAsync(taskId, sessionId);
            if (cut is "attempt-before-input" or "submit-before-verdict")
            {
                note.Status.ShouldBe(QueuedMessageStatus.Sent);
                note.DeliveryAttempts.ShouldBe(1);
                note.DeliveryVerdict.ShouldBeNull();
                parent.Adapter.SubmittedBodies.Count.ShouldBe(cut == "attempt-before-input" ? 0 : 1);
                (await s.PromptsAsync(sessionId)).Count.ShouldBe(cut == "attempt-before-input" ? 0 : 1);
            }
            submittedBeforeRestart = parent.Adapter.SubmittedBodies.Count;
        }
        finally { await parent.DisposeAsync(); }

        // Harness disposal preserves this isolated database. Restart attaches to the same recipient.
        // Advance only the clock through the documented 3+3+30s interrupted-attempt age.
        s.Clock.Advance(TimeSpan.FromSeconds(40));
        await using var recovered = await s.ParentAsync(attachSession: sessionId, attachAgent: agentId);
        await recovered.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        await s.AssertReceiptAsync(note, recovered, floor, "recovered-parent-receipt");
        await recovered.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        await s.TickAsync();
        await s.AssertReceiptAsync(note, recovered, floor, "no-replayed-prompt");
        (submittedBeforeRestart + recovered.Adapter.SubmittedBodies.Count).ShouldBe(1, "no-replayed-prompt");
        var restored = await s.AssertNoteAsync(taskId, sessionId);
        restored.Id.ShouldBe(note.Id);
        if (cut == "submit-before-verdict")
        {
            restored.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
            restored.DeliveryAttempts.ShouldBe(1);
            recovered.Adapter.SubmittedBodies.ShouldBeEmpty("no-replayed-prompt");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1021_explicit_exhausted_host_is_preserved(bool remote)
    {
        await using var s = await Scenario.CreateAsync(false);
        await s.DefaultsAsync(remote ? "desktop" : "server2");
        var created = await s.CreateAsync(s.Request with
        {
            RunnerId = remote ? "server2" : "local",
            RequiredPlatform = remote ? RequiredPlatform.Linux : RequiredPlatform.Windows,
        });
        var task = await s.AssertBlockedAsync(created.Id);
        task.RunnerId.ShouldBe(remote ? "server2" : null, "explicit-host");
        task.RunnerSelectionSource.ShouldBe(RunnerSelectionSource.Explicit);
        created.Routing!.Candidates.ShouldNotContain(x => x.Outcome == "chosen");
    }

    [Test]
    [Arguments("existing-process")]
    [Arguments("non-worktree")]
    [Arguments("unsupported-kind")]
    [Arguments("codex-source-landing")]
    [Arguments("non-claude-orchestrator")]
    [Arguments("specialist")]
    [Arguments("source-landing-wrong-role")]
    [Arguments("source-landing-non-worker")]
    public void C1021_other_shape_exclusions_stay_independent(string variant)
    {
        var shape = new DefaultRunnerShape(WorkspaceMode.Worktree, AgentKind.ClaudeCode,
            AgentTaskKind.Worker, AgentTaskRole.Code, false, false);
        var expected = DefaultRunnerRoutingPolicy.ReasonKindNotSupported;
        shape = variant switch
        {
            "existing-process" => shape with { ExistingProcess = true },
            "non-worktree" => shape with { Workspace = WorkspaceMode.Shared },
            "unsupported-kind" => shape with { Kind = AgentKind.Raw },
            "codex-source-landing" => shape with { Kind = AgentKind.Codex, SourceLanding = true, Role = AgentTaskRole.Mutation },
            "non-claude-orchestrator" => shape with { Kind = AgentKind.Grok, TaskKind = AgentTaskKind.Orchestrator },
            "specialist" => shape with { Role = AgentTaskRole.Check },
            "source-landing-wrong-role" => shape with { SourceLanding = true },
            "source-landing-non-worker" => shape with { SourceLanding = true, TaskKind = AgentTaskKind.Orchestrator, Role = AgentTaskRole.Mutation },
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        if (variant == "existing-process") expected = DefaultRunnerRoutingPolicy.ReasonExistingProcess;
        if (variant == "non-worktree") expected = DefaultRunnerRoutingPolicy.ReasonWorkspaceNotWorktree;
        if (variant.StartsWith("source-landing")) expected = DefaultRunnerRoutingPolicy.ReasonSourceLandingNotSupported;
        DefaultRunnerRoutingPolicy.ExclusionFor(shape).ShouldBe(expected, "shape-exclusion");
    }

    private sealed record Ids(Guid[] Tasks, Guid[] Events, Guid[] Notes);

    private sealed class Scenario(IsolatedTestSchema database, bool chain, string local, AgentKind head) : IAsyncDisposable
    {
        public const string ListReason = "CARD-1021 isolated Human list";
        public DefaultRunnerKit Kit { get; } = DefaultRunnerKit.Create(database.ConnectionString, "server2");
        public PlacementDirectory Directory { get; } = new(local);
        public SaveObserver Observer { get; } = new();
        public RecordingSink Sink { get; } = new();
        public RecordingWorktrees Worktrees { get; } = new();
        public ScaledTimeProvider Clock { get; } = new(1);
        private readonly List<string> _parentRoots = [];
        private PhoneHomeTestHost? _dispatchHost;
        private PhoneHomeScriptedPeer? _peer;
        public Guid ListId { get; } = Guid.NewGuid();
        public Guid CardId { get; } = Guid.NewGuid();
        public Guid BoardId { get; } = Guid.NewGuid();
        public Guid ColumnId { get; } = Guid.NewGuid();
        public string ListJson { get; private set; } = "";
        public long Revision { get; private set; }
        public CreateAgentTaskRequest Request => new("CARD-1021 held routing", Title: "CARD-1021 held routing",
            Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree, RequiredPlatform: RequiredPlatform.Linux,
            Complexity: chain ? TaskComplexity.Hard : null);

        public static async Task<Scenario> CreateAsync(bool chain, string local = "windows", AgentKind head = AgentKind.Grok)
        {
            var s = new Scenario(await TestDbFixture.CreateIsolatedSchemaAsync(), chain, local, head);
            s.Kit.RealDirectory = s.Directory;
            s.Kit.Settings.MaxConcurrentTasks = 512;
            await using var db = s.Context();
            var second = head == AgentKind.ClaudeCode ? AgentKind.Grok : AgentKind.ClaudeCode;
            s.ListJson = chain
                ? ComplexityChain.SerializeCandidates([new(head, AgentModelLevel.High), new(second, AgentModelLevel.High)])
                : RoutingCandidate.Serialize([new(head, AgentModelLevel.High), new(second, AgentModelLevel.High)]);
            if (chain)
                db.ComplexityChains.Add(new ComplexityChain
                {
                    Id = s.ListId, Role = AgentTaskRole.Code, Complexity = TaskComplexity.Hard,
                    CandidatesJson = s.ListJson, Provenance = RoutingPinProvenance.Human, Reason = ListReason,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
            else
                db.RoutingPins.Add(new RoutingPin
                {
                    Id = s.ListId, Role = AgentTaskRole.Code, CandidatesJson = s.ListJson,
                    Provenance = RoutingPinProvenance.Human, Strength = RoutingPinStrength.Required,
                    Reason = ListReason, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
            foreach (var kind in new[] { head, second })
                db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
                {
                    Id = Guid.NewGuid(), Kind = kind, ModelAlias = ModelLevelAliases.For(kind, AgentModelLevel.High),
                    Source = ModelAvailabilitySource.Manual, DisabledUntil = null, HitAt = DateTime.UtcNow,
                    Reason = "CARD-1021 open-ended test hold",
                });
            // The unlisted Codex (or Claude/Grok for their head cases) remains available.
            await db.SaveChangesAsync();
            await s.DefaultsAsync("server2");
            return s;
        }

        public AppDbContext Context(IInterceptor? fault = null)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(database.ConnectionString));
            options.AddInterceptors(Observer);
            if (fault is not null) options.AddInterceptors(fault);
            return new AppDbContext(options.Options);
        }

        public async Task DefaultsAsync(string? global, string? kind = null)
        {
            await using var db = Context();
            var defaults = new RunnerDefaultSettingsService(db, Options.Create(Kit.Settings), Clock, runners: Directory);
            var snapshot = await defaults.EnsureInitializedAsync(CancellationToken.None);
            Revision = (await defaults.PutAsync(new(snapshot.Revision, global,
                kind is null ? [] : [new(head, kind)], "CARD-1021 isolated defaults", "Human"), null, CancellationToken.None)).Revision;
        }

        public async Task<AgentTaskCreatedDto> CreateAsync(CreateAgentTaskRequest? request = null,
            Guid? parentSession = null, CutFault? fault = null, AgentRegistrySettings? registry = null)
        {
            await using var db = Context(fault);
            var defaults = new RunnerDefaultSettingsService(db, Options.Create(Kit.Settings), Clock, runners: Directory);
            return await Kit.Service(db, defaults, withRouting: true, eventBus: fault, registry: registry).CreateAsync(
                request ?? Request, new AgentTaskService.Caller(null, parentSession, Kit.RepoRoot), CancellationToken.None);
        }

        public async Task<AgentTask> AssertBlockedAsync(Guid id)
        {
            await using var db = Context();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(x => x.Id == id);
            task.Status.ShouldBe(AgentTaskStatus.Blocked, "durable-blocked");
            task.FailureReason.ShouldNotBeNull().ShouldStartWith(ComplexityRoutingService.RoutingExhaustedPrefix, customMessage: "exhaustion-prefix");
            task.AgentSessionId.ShouldBeNull("no-session");
            task.DispatchedAt.ShouldBeNull();
            (await db.AgentTaskEvents.CountAsync(x => x.AgentTaskId == id && x.Type == AgentTaskEventType.Created)).ShouldBe(1, "created-event");
            (await db.AgentTaskEvents.CountAsync(x => x.AgentTaskId == id && x.Type == AgentTaskEventType.Blocked)).ShouldBe(1, "blocked-event");
            return task;
        }

        public async Task<Ids> IdsAsync()
        {
            await using var db = Context();
            return new(await db.AgentTasks.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(),
                await db.AgentTaskEvents.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(),
                await db.SessionQueuedMessages.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        }
        public async Task AssertIdsAsync(Ids before, string label)
        {
            var after = await IdsAsync();
            after.Tasks.ShouldBe(before.Tasks, label);
            after.Events.ShouldBe(before.Events, label);
            after.Notes.ShouldBe(before.Notes, label);
        }
        public async Task<Guid[]> SessionIdsAsync()
        {
            await using var db = Context();
            return await db.AgentSessions.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        }

        public async Task<SessionQueuedMessage> AssertNoteAsync(Guid taskId, Guid parent)
        {
            await using var db = Context();
            var notes = await db.SessionQueuedMessages.AsNoTracking().Where(x => x.SourceTaskId == taskId).ToListAsync();
            var note = notes.ShouldHaveSingleItem("source-linked-parent-note");
            note.SourceTaskId.ShouldBe(taskId);
            note.AgentSessionId.ShouldBe(parent);
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(x => x.Id == taskId);
            note.ContentDigest.ShouldBe(DelegationNoteDigest.Compute(task.FailureReason!));
            note.Body.ShouldContain(DelegationReportFormatter.Short(taskId));
            note.Body.ShouldContain(task.FailureReason!);
            return note;
        }
        public async Task<BridgeQueueHarness> ParentAsync(CutFault? fault = null, Guid? attachSession = null, Guid? attachAgent = null)
        {
            var parent = await BridgeQueueHarness.CreateAsync(new()
            {
                AlwaysOn = true, PreserveDatabaseOnDispose = true, ConnectionString = database.ConnectionString,
                TimeProvider = Clock, AttachSessionId = attachSession, AttachAgentId = attachAgent,
                ConfigureDbContext = options => { if (fault is not null) options.AddInterceptors(fault); },
            });
            _parentRoots.Add(parent.TempRoot);
            return parent;
        }
        public async Task<long> TranscriptFloorAsync(Guid session)
        {
            await using var db = Context();
            return await db.TranscriptEntries.Where(x => x.AgentSessionId == session).MaxAsync(x => (long?)x.Sequence) ?? 0;
        }
        public async Task<List<TranscriptEntry>> PromptsAsync(Guid session)
        {
            await using var db = Context();
            return await db.TranscriptEntries.AsNoTracking()
                .Where(x => x.AgentSessionId == session && x.Kind == TranscriptKinds.UserPrompt).ToListAsync();
        }
        public async Task AssertReceiptAsync(SessionQueuedMessage note, BridgeQueueHarness parent, long floor, string label)
        {
            var prompts = await PromptsAsync(parent.SessionId);
            var receipt = prompts.Where(x => x.Sequence > floor && x.Text is not null
                && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text)).ShouldHaveSingleItem(label);
            PromptSubmissionMatch.Normalize(receipt.Text!).ShouldBe(PromptSubmissionMatch.Normalize(note.Body), label);
            receipt.AgentSessionId.ShouldBe(note.AgentSessionId, label);
            prompts.Count.ShouldBe(1, label);
            await using var db = Context();
            var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(x => x.Id == note.Id);
            queued.SourceTaskId.ShouldBe(note.SourceTaskId);
            queued.DeliveryVerdict.ShouldBeOneOf(DeliveryVerdict.Delivered, DeliveryVerdict.LateConfirmed);
        }
        public async Task SeedCardAsync()
        {
            await using var db = Context();
            var projectId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            db.Projects.Add(new Project { Id = projectId, Name = "C1021", GitRepositoryUrl = "https://example.invalid/test.git",
                BaseBranch = "master", CreatedAt = now, UpdatedAt = now });
            db.Boards.Add(new Board { Id = BoardId, ProjectId = projectId, Name = "C1021", CreatedAt = now, UpdatedAt = now });
            db.BoardColumns.Add(new BoardColumn { Id = ColumnId, BoardId = BoardId, StateKey = "backlog", Name = "Backlog",
                CardStatus = CardStatus.Backlog, IsActive = true, CreatedAt = now, UpdatedAt = now });
            db.Cards.Add(new Card { Id = CardId, BoardId = BoardId, BoardColumnId = ColumnId, Identifier = "CARD-1021",
                Title = "routing exhaustion", Description = "isolated", Status = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        public async Task<AgentTaskDispatcher.TickResult> TickAsync()
        {
            if (_dispatchHost is null)
            {
                _dispatchHost = await PhoneHomeTestHost.StartAsync(connectionString: database.ConnectionString,
                    configureRunnerSettings: settings => settings.AllowedRunnerId = "server2");
                _peer = await _dispatchHost.ConnectPeerAsync(runnerId: "server2", platform: Directory.RemotePlatform,
                    capabilities: new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                        Features: [RunnerPlatformWire.Feature, RunnerCapabilityFeatures.WorkspaceRepositoryV1],
                        Platform: Directory.RemotePlatform));
                _peer.Reply = frame => frame.Operation == PhoneHomeOperation.WorkspaceMirror
                    ? new PhoneHomeFrame(PhoneHomeFrameKind.Result, frame.Epoch, frame.RequestId, frame.Operation,
                        System.Text.Json.JsonSerializer.SerializeToElement(new PhoneHomeWorkspaceMirrorResponse("/work/c1021-fixture"), PhoneHomeFraming.Json))
                    : null;
                _dispatchHost.Directory.MarkRecovered(await _dispatchHost.WaitLiveAsync(runnerId: "server2"));
                Directory.RemoteClient = _dispatchHost.Directory.Resolve("server2");
            }
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(database.ConnectionString).AddInterceptors(Observer));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddSingleton(Options.Create(Kit.Settings));
            services.AddSingleton(Options.Create(Kit.PhoneHome));
            services.AddOptions<AgentRegistrySettings>().Configure(r =>
            {
                r.DefaultDefinition = "claude";
                r.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
                r.Definitions["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok" };
                r.Definitions["codex"] = new AgentDefinition { Kind = "Codex", Exe = "codex" };
            });
            services.AddSingleton<ISessionRunnerDirectory>(Directory);
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph();
            services.AddSingleton<IWorktreeManager>(Worktrees);
            services.AddSingleton<ILandingGit>(Directory.Git);
            services.AddSingleton<PhoneHomeLaunchPolicy>();
            services.AddSingleton<RemoteWorkspaceService>();
            services.AddSingleton<RemoteWorkspacePreparer>();
            services.AddSingleton<IAgentTaskLaunchSink>(Sink);
            services.AddScoped<AgentTaskService>();
            services.AddScoped<RoutingPinService>();
            services.AddScoped<ModelAvailability>();
            services.AddScoped<ComplexityRoutingService>();
            services.AddScoped<AgentTaskDispatcher>();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            // Dispose awaits any preparer children; source and database remain owned until then.
            return result;
        }
        public void AssertNoLaunch()
        {
            Sink.Specs.ShouldBeEmpty("no-launch");
            Directory.Client.Starts.ShouldBeEmpty("no-launch");
            Directory.Client.Inputs.ShouldBeEmpty("no-input");
            Directory.Git.Commands.ShouldBeEmpty("no-mirror-preparation");
            Worktrees.Creates.ShouldBeEmpty("no-worktree-preparation");
            if (_peer is not null)
            {
                _peer.RequestCount(PhoneHomeOperation.WorkspaceMirror).ShouldBe(0, "no-mirror-preparation");
                _peer.Launches.ShouldBeEmpty("no-launch");
                _peer.RequestCount(PhoneHomeOperation.Input).ShouldBe(0, "no-input");
            }
        }
        public async ValueTask DisposeAsync()
        {
            if (_peer is not null) await _peer.DisposeAsync();
            if (_dispatchHost is not null) await _dispatchHost.DisposeAsync();
            await database.DisposeAsync();
            foreach (var root in _parentRoots)
                if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SaveObserver : SaveChangesInterceptor
    {
        public List<(Guid Id, AgentTaskStatus Status, string? Runner, RequiredPlatform Platform)> Transitions { get; } = [];
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            foreach (var e in data.Context!.ChangeTracker.Entries<AgentTask>().Where(x => x.State is EntityState.Added or EntityState.Modified))
                Transitions.Add((e.Entity.Id, e.Entity.Status, e.Entity.RunnerId, e.Entity.RequiredPlatform));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CutException(string cut) : Exception(cut);

    /// <summary>Faults at the actual producer/queue commit boundaries; every cut must fire.</summary>
    private sealed class CutFault(string cut) : DbCommandInterceptor, ISaveChangesInterceptor, IEventBus
    {
        public bool Fired { get; private set; }
        private bool _attemptSaving;
        private void Fire() { Fired = true; throw new CutException(cut); }
        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fired) return ValueTask.FromResult(result);
            if (cut == "before-save" && data.Context!.ChangeTracker.Entries<AgentTaskEvent>()
                .Any(e => e.State == EntityState.Added && e.Entity.Type == AgentTaskEventType.Blocked)) Fire();
            var notes = data.Context!.ChangeTracker.Entries<SessionQueuedMessage>()
                .Where(e => e.State == EntityState.Modified && e.Entity.SourceTaskId != null).Select(e => e.Entity).ToList();
            if (cut == "submit-before-verdict" && notes.Any(m => m.DeliveryVerdict == DeliveryVerdict.Delivered)) Fire();
            _attemptSaving = cut == "attempt-before-input" && notes.Any(m => m.Status == QueuedMessageStatus.Sent
                && m.DeliveryAttempts == 1 && m.DeliveryVerdict == null);
            return ValueTask.FromResult(result);
        }
        public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result,
            CancellationToken cancellationToken = default)
        {
            if (_attemptSaving && !Fired) Fire();
            return ValueTask.FromResult(result);
        }
        private void FailEnqueue(DbCommand command)
        {
            if (!Fired && cut == "enqueue-fails"
                && command.CommandText.Contains("\"SessionQueuedMessages\"", StringComparison.Ordinal)
                && command.CommandText.Contains("max(", StringComparison.OrdinalIgnoreCase)) Fire();
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { FailEnqueue(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { FailEnqueue(command); return ValueTask.FromResult(result); }
        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
        {
            if (!Fired && cut == "after-commit-before-wake" && eventName == "AgentTaskChanged") Fire();
            return Task.CompletedTask;
        }
        public Task PublishToGroupAsync(string group, string eventName, object payload, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class PlacementDirectory(string local) : ISessionRunnerDirectory
    {
        public ProbeClient Client { get; } = new();
        public ISessionRunnerClient? RemoteClient { get; set; }
        public PreparationGit Git { get; } = new();
        public ISessionRunnerClient Local => Client;
        public string? RemotePlatform { get; set; } = local == "windows" ? "linux" : "windows";
        public bool Eligible { get; set; } = true;
        public bool Feature { get; set; } = true;
        public IReadOnlyList<string> KnownRunnerIds => ["desktop", "server2"];
        public Guid? GetLiveStoreId(string? runnerId) => string.IsNullOrWhiteSpace(runnerId) ? null : Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
        {
            var desktop = string.IsNullOrWhiteSpace(runnerId) || RunnerRequestIntent.IsDesktopAlias(runnerId);
            var platform = desktop ? local : RemotePlatform;
            return Task.FromResult<RunnerDescriptor?>(new RunnerDescriptor(desktop ? "desktop" : "server2", "fixture", platform,
                DateTimeOffset.UtcNow, desktop || Eligible, desktop || Eligible, false, 10,
                new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                    Features: desktop || Feature ? [RunnerPlatformWire.Feature, RunnerCapabilityFeatures.WorkspaceRepositoryV1] : [RunnerCapabilityFeatures.WorkspaceRepositoryV1], Platform: platform)));
        }
        public ISessionRunnerClient Resolve(string? runnerId) => Eligible || string.IsNullOrWhiteSpace(runnerId)
            ? (string.IsNullOrWhiteSpace(runnerId) ? Client : RemoteClient ?? Client) : throw new ServiceUnavailableException("fixture runner unavailable", PhoneHomeProblemTypes.Unavailable);
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("fixture has no child sessions"));
    }

    private sealed class ProbeClient : ISessionRunnerClient
    {
        public List<string> Providers { get; } = [];
        public List<Guid> Starts { get; } = [];
        public List<string> Inputs { get; } = [];
        public Task<RunnerProviderAuthDto?> GetProviderAuthAsync(string provider, CancellationToken ct)
        {
            Providers.Add(provider);
            return Task.FromResult<RunnerProviderAuthDto?>(new(provider, false, null, null, DateTimeOffset.UtcNow, null));
        }
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct)
        { Starts.Add(id); throw new InvalidOperationException("unexpected launch"); }
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) { Inputs.Add(input); return Task.CompletedTask; }
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        { await Task.CompletedTask; yield break; }
    }
    private sealed class RecordingSink : IAgentTaskLaunchSink
    {
        public List<AgentLaunchSpec> Specs { get; } = [];
        public void Enqueue(Guid session, Guid agent, DateTime generation, AgentLaunchSpec spec) => Specs.Add(spec);
    }
    private sealed class RecordingWorktrees : IWorktreeManager
    {
        public List<string> Creates { get; } = [];
        public Task<WorktreeInfo> CreateAsync(string repo, string id, string baseRef, CancellationToken ct)
        { Creates.Add(id); return Task.FromResult(new WorktreeInfo(id, repo, repo, "feat/test", baseRef, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)); }
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repo, CancellationToken ct) => Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);
        public Task RemoveAsync(string repo, string path, CancellationToken ct) => Task.CompletedTask;
        public Task TouchAsync(string path, CancellationToken ct) => Task.CompletedTask;
        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }
    private sealed class PreparationGit : ILandingGit
    {
        public List<string> Commands { get; } = [];
        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
            Record(arguments, new LandingGitResult(0,
                arguments.Count == 3 && arguments[0] == "remote" && arguments[1] == "get-url" && arguments[2] == "origin"
                    ? "https://github.com/example/antiphon.git"
                    : new string('1', 40), ""));
        private Task<LandingGitResult> Record(IReadOnlyList<string> arguments, LandingGitResult result)
        { Commands.Add(string.Join(" ", arguments)); return Task.FromResult(result); }
        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => Task.FromResult(path);
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => Task.FromResult(repository);
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => Task.FromResult<IReadOnlyList<LandingRegistration>>([]);
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) => throw new NotSupportedException();
    }
}
