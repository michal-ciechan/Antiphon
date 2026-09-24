using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class ExpectationPipelineTests
{
    private const string DirectiveId = "tonight";
    private const string Repo = "repo:C:\\src\\Antiphon";
    private static readonly Guid BoardId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    private static readonly Guid OtherBoardId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
    private static readonly Guid ProjectId = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1");
    private static readonly Guid AgentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    private static readonly Guid LocalTaskId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RemoteTaskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Now = new(2026, 9, 24, 2, 6, 23, DateTimeKind.Utc);

    [Test]
    public async Task C650_Queue_age_and_dispatch_progress_use_exact_boundaries()
    {
        var started = new DateTime(2026, 9, 24, 2, 0, 0, DateTimeKind.Utc);
        var almost = started.AddMinutes(10).AddSeconds(-1);
        var dueAt = started.AddMinutes(10);
        var scope = new ExpectationScopeContext(BoardId, ProjectId, AgentId);

        ExpectationWatchdogPolicy.Evaluate(Snap(almost, [Queue(LocalTaskId, started)]), Directive())
            .StalledPipelines.ShouldBeEmpty();

        var due = ExpectationWatchdogPolicy.Evaluate(Snap(dueAt, [Queue(LocalTaskId, started)]), Directive())
            .StalledPipelines.ShouldHaveSingleItem();
        due.IsDue.ShouldBeTrue();
        due.Immediate.ShouldBeFalse();
        due.Kind.ShouldBe(ExpectationEpisodeKind.StalledPipeline);
        due.ExampleTaskIds.ShouldBe([LocalTaskId]);
        due.SubjectKey.ShouldBe(ExpectationSubjects.Pipeline(DirectiveId, Repo));

        var recentOwn = new ExpectationDispatchMark(
            started.AddMinutes(9), BoardId, ProjectId, ProjectId, null, false);
        var foreign = new ExpectationDispatchMark(
            started.AddMinutes(9), OtherBoardId, ProjectId, ProjectId, null, false);
        var recentAt = ExpectationDispatchProgress.Latest([recentOwn, foreign], scope, dueAt);
        recentAt.ShouldBe(started.AddMinutes(9));
        ExpectationWatchdogPolicy.Evaluate(
                Snap(dueAt, [Queue(LocalTaskId, started)], recentAt), Directive())
            .StalledPipelines.ShouldBeEmpty();

        ExpectationDispatchProgress.Latest([foreign], scope, dueAt).ShouldBeNull();
        ExpectationWatchdogPolicy.Evaluate(Snap(dueAt, [Queue(LocalTaskId, started)]), Directive())
            .StalledPipelines.Count.ShouldBe(1);

        var exact = ExpectationDispatchProgress.Latest(
            [new ExpectationDispatchMark(started, BoardId, ProjectId, ProjectId, null, false)],
            scope,
            dueAt);
        exact.ShouldBe(started);
        ExpectationWatchdogPolicy.Evaluate(
                Snap(dueAt, [Queue(LocalTaskId, started)], started), Directive())
            .StalledPipelines.Count.ShouldBe(1);

        var workingOnly = Snap(dueAt, []) with
        {
            Lanes = [new ExpectationLaneSnapshot { RunnerId = null, Target = 3, Running = 2 }],
        };
        ExpectationWatchdogPolicy.Evaluate(workingOnly, Directive()).StalledPipelines.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Requeue_resets_age_but_Held_does_not()
    {
        var created = new DateTime(2026, 9, 24, 1, 0, 0, DateTimeKind.Utc);
        var retried = created.AddMinutes(30);
        var events = new[]
        {
            new ExpectationStintEvent(AgentTaskEventType.Created, created),
            new ExpectationStintEvent(AgentTaskEventType.Dispatched, created.AddMinutes(1)),
            new ExpectationStintEvent(AgentTaskEventType.Held, created.AddMinutes(4)),
            new ExpectationStintEvent(AgentTaskEventType.HeldAged, created.AddMinutes(9)),
            new ExpectationStintEvent(AgentTaskEventType.Retried, retried),
            new ExpectationStintEvent(AgentTaskEventType.Held, retried.AddMinutes(1)),
        };

        ExpectationQueueStint.Start(created, events).ShouldBe(retried);
        ExpectationQueueStint.Start(
                created,
                events.Where(row => row.Type is AgentTaskEventType.Held
                    or AgentTaskEventType.HeldAged
                    or AgentTaskEventType.Dispatched
                    or AgentTaskEventType.Created))
            .ShouldBe(created);
        ExpectationQueueStint.Start(
                created,
                [new ExpectationStintEvent(AgentTaskEventType.Escalated, created.AddMinutes(12))])
            .ShouldBe(created.AddMinutes(12));
        ExpectationQueueStint.Start(
                created,
                [new ExpectationStintEvent(AgentTaskEventType.Rerouted, created.AddMinutes(8))])
            .ShouldBe(created.AddMinutes(8));

        var almost = retried.AddMinutes(10).AddSeconds(-1);
        ExpectationWatchdogPolicy.Evaluate(Snap(almost, [Queue(LocalTaskId, retried)]), Directive())
            .StalledPipelines.ShouldBeEmpty();
        ExpectationWatchdogPolicy.Evaluate(
                Snap(retried.AddMinutes(10), [Queue(LocalTaskId, retried)]), Directive())
            .StalledPipelines.ShouldHaveSingleItem()
            .IsDue.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_All_paths_fenced_is_immediate()
    {
        var stint = Now.AddMinutes(-1);
        (Now - stint).ShouldBeLessThan(TimeSpan.FromMinutes(10));
        var snapshot = Snap(Now,
        [
            Fenced(LocalTaskId, null, stint),
            Fenced(RemoteTaskId, "server2", stint),
        ]);

        var evaluation = ExpectationWatchdogPolicy.Evaluate(snapshot, Directive());
        evaluation.StalledPipelines.ShouldBeEmpty();
        var fence = evaluation.DispatchFence;
        fence.ShouldNotBeNull("a repository fence on every queued path is due on the first observation");
        fence.IsDue.ShouldBeTrue();
        fence.Immediate.ShouldBeTrue();
        fence.Kind.ShouldBe(ExpectationEpisodeKind.DispatchFence);
        fence.ReasonCode.ShouldBe("repository-fenced");
        fence.Scope.ShouldBe(Repo);
        fence.SubjectKey.ShouldBe(ExpectationSubjects.Fence(DirectiveId, Repo));
        fence.ExampleTaskIds.ShouldBe(new[] { LocalTaskId, RemoteTaskId }.OrderBy(id => id).ToArray());
        fence.Evidence.ShouldContain("dead journal");
        fence.Evidence.ShouldContain("Inspect the recorded fence read-only");
        evaluation.ScopedFences.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Partial_or_unknown_path_is_not_global_fence()
    {
        var stint = Now.AddMinutes(-1);
        var oneOpen = ExpectationWatchdogPolicy.Evaluate(
            Snap(Now, [Fenced(LocalTaskId, null, stint), Open(RemoteTaskId, "server2", stint)]),
            Directive());
        oneOpen.DispatchFence.ShouldBeNull();
        oneOpen.ScopedFences.ShouldBeEmpty();

        var oneUnknown = ExpectationWatchdogPolicy.Evaluate(
            Snap(Now,
            [
                Fenced(LocalTaskId, null, stint),
                Open(RemoteTaskId, "server2", stint) with { HoldClass = ExpectationHoldClass.Unknown },
            ]),
            Directive());
        oneUnknown.DispatchFence.ShouldBeNull();

        var server2Only = ExpectationWatchdogPolicy.Evaluate(
            Snap(Now,
            [
                Open(LocalTaskId, null, stint),
                Open(RemoteTaskId, "server2", stint) with { HoldClass = ExpectationHoldClass.RunnerUnavailable },
            ]),
            Directive());
        server2Only.DispatchFence.ShouldBeNull();
        var scoped = server2Only.ScopedFences.ShouldHaveSingleItem();
        scoped.Scope.ShouldBe("runner:server2");
        scoped.IsDue.ShouldBeTrue();
        scoped.Immediate.ShouldBeTrue();
        scoped.Evidence.ShouldContain("not an all-board fence");
        scoped.ExampleTaskIds.ShouldBe([RemoteTaskId]);

        var shared = ExpectationWatchdogPolicy.Evaluate(
            Snap(Now, [Fenced(LocalTaskId, null, stint), Fenced(RemoteTaskId, "server2", stint)]),
            Directive());
        shared.DispatchFence.ShouldNotBeNull();
        shared.DispatchFence.Scope.ShouldBe(Repo);
        shared.DispatchFence.Immediate.ShouldBeTrue();
        shared.DispatchFence.ExampleTaskIds.ShouldBe(new[] { LocalTaskId, RemoteTaskId }.OrderBy(id => id).ToArray());
        shared.ScopedFences.ShouldBeEmpty();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Quota_fences_new_admission_only()
    {
        var settings = new SubscriptionQuotaGateSettings();
        ExpectationAdmission.Classify(Sample(4, TimeSpan.FromMinutes(1)), true, false, settings, Now)
            .ShouldBe(ExpectationAdmissionState.QuotaRefused);
        ExpectationAdmission.Classify(Sample(1, TimeSpan.FromMinutes(181)), true, false, settings, Now)
            .ShouldBe(ExpectationAdmissionState.Unknown);
        ExpectationAdmission.Classify(null, true, false, settings, Now)
            .ShouldBe(ExpectationAdmissionState.Unknown);
        ExpectationAdmission.Classify(Sample(4, TimeSpan.FromMinutes(1)), false, false, settings, Now)
            .ShouldBe(ExpectationAdmissionState.Unknown);
        ExpectationAdmission.Classify(Sample(80, TimeSpan.FromMinutes(1)), true, false, settings, Now)
            .ShouldBe(ExpectationAdmissionState.Open);
        ExpectationAdmission.Classify(Sample(80, TimeSpan.FromMinutes(1)), true, null, settings, Now)
            .ShouldBe(ExpectationAdmissionState.Unknown);

        var queued = Open(LocalTaskId, null, Now.AddMinutes(-1));
        var refused = ExpectationWatchdogPolicy.Evaluate(
            Snap(Now, [queued]) with
            {
                EligibleBacklog = 2,
                Admission =
                [
                    Candidate(ExpectationAdmissionState.QuotaRefused, null),
                    Candidate(ExpectationAdmissionState.QuotaRefused, "server2"),
                ],
            },
            Directive());
        var fence = refused.DispatchFence;
        fence.ShouldNotBeNull();
        fence.IsDue.ShouldBeTrue();
        fence.Immediate.ShouldBeTrue();
        fence.ReasonCode.ShouldBe("quota-new-admission");
        fence.Scope.ShouldBe("admission");
        fence.ExampleTaskIds.ShouldBeEmpty();
        fence.ExampleTaskIds.ShouldNotContain(LocalTaskId);
        fence.Evidence.ShouldContain("already queued work is not blocked by quota");
        refused.StalledPipelines.ShouldBeEmpty();

        ExpectationWatchdogPolicy.Evaluate(
                Snap(Now, [queued]) with
                {
                    EligibleBacklog = 2,
                    Admission =
                    [
                        Candidate(ExpectationAdmissionState.QuotaRefused, null),
                        Candidate(ExpectationAdmissionState.Unknown, "server2"),
                    ],
                },
                Directive())
            .DispatchFence.ShouldBeNull();

        ExpectationWatchdogPolicy.Evaluate(
                Snap(Now, [queued]) with
                {
                    EligibleBacklog = 0,
                    Admission = [Candidate(ExpectationAdmissionState.QuotaRefused, null)],
                },
                Directive())
            .DispatchFence.ShouldBeNull();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Capacity_deficit_requires_continuous_ready_work()
    {
        var first = ExpectationWatchdogPolicy.Evaluate(
            CapacitySnap(running: 1, queued: 0, backlog: 2, since: null, changed: false),
            Directive());
        var started = first.Capacity.ShouldHaveSingleItem();
        started.InDeficit.ShouldBeTrue();
        started.IsDue.ShouldBeFalse();
        started.ClockStartedAt.ShouldBe(Now);
        started.SubjectKey.ShouldBe(ExpectationSubjects.Capacity(DirectiveId, null));

        ExpectationWatchdogPolicy.Evaluate(
                CapacitySnap(1, 0, 2, Now.AddMinutes(-10).AddSeconds(1), false),
                Directive())
            .Capacity.ShouldHaveSingleItem().IsDue.ShouldBeFalse();

        var due = ExpectationWatchdogPolicy.Evaluate(
                CapacitySnap(1, 0, 2, Now.AddMinutes(-10), false),
                Directive())
            .Capacity.ShouldHaveSingleItem();
        due.IsDue.ShouldBeTrue();
        due.InDeficit.ShouldBeTrue();
        due.ExplainsHeldQueue.ShouldBeFalse();
        due.Evidence.ShouldContain("select an admissible");

        var explained = ExpectationWatchdogPolicy.Evaluate(
                CapacitySnap(1, 4, 2, Now.AddMinutes(-10), false),
                Directive())
            .Capacity.ShouldHaveSingleItem();
        explained.IsDue.ShouldBeTrue();
        explained.ExplainsHeldQueue.ShouldBeTrue();
        explained.Evidence.ShouldContain("do not duplicate");

        var restarted = ExpectationWatchdogPolicy.Evaluate(
                CapacitySnap(1, 0, 2, Now.AddMinutes(-30), changed: true),
                Directive())
            .Capacity.ShouldHaveSingleItem();
        restarted.IsDue.ShouldBeFalse();
        restarted.ClockStartedAt.ShouldBe(Now);

        ExpectationWatchdogPolicy.Evaluate(CapacitySnap(1, 0, 0, Now.AddHours(-1), false), Directive())
            .Capacity.ShouldHaveSingleItem().InDeficit.ShouldBeFalse();
        ExpectationWatchdogPolicy.Evaluate(CapacitySnap(3, 0, 2, Now.AddHours(-1), false), Directive())
            .Capacity.ShouldHaveSingleItem().InDeficit.ShouldBeFalse();

        var paused = ExpectationWatchdogPolicy.Evaluate(
            CapacitySnap(0, 0, 4, Now.AddHours(-1), false) with { DirectiveActive = false },
            Directive());
        paused.Capacity.ShouldBeEmpty();
        paused.PreservesOpenEpisodes.ShouldBeTrue();
        paused.ObservationUnknown.ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Ordinary_waits_are_not_global_fences()
    {
        var now = Now;
        string[] details =
        [
            DispatchHoldDetails.ConcurrencyCap(4),
            DispatchHoldDetails.LeaseHeldByLand("abcd1234", "land the card", "req1", now),
            DispatchHoldDetails.RemotePrepBackoff("server2", 2, now.AddMinutes(5)),
            DispatchHoldDetails.RemoteMirrorRequested("server2", now),
            "routing pin not before 2099-01-01T00:00:00Z; dispatch paused (wait).",
            "Held: running task abcd1234 \"title\" is already writing in this shared checkout (Shared/Shared; no intersecting scope - two shared writers share one working tree).",
            "Held: 'server/' intersects running task abcd1234 \"title\" (Shared/Shared).",
            "held: CARD-0647's kept branch feat/x (task abcd1234) is landing and is not yet in origin/master",
            "abcd1234 is landing",
            DispatchHoldDetails.PinnedAgentParkedOn("claude", "abcd1234", AgentTaskStatus.Blocked),
            DispatchHoldDetails.StandingAgentBusy("orch", "abcd1234", AgentTaskStatus.Working),
            DispatchHoldDetails.StandingAgentNoSession("orch"),
            DispatchHoldDetails.Escalation(
                "Warning:", 300, now.AddMinutes(-5), now.AddMinutes(-20),
                DispatchHoldDetails.ConcurrencyCap(4), 4, 4, ["abcd1234"]),
        ];

        var queued = details.Select((detail, index) => new ExpectationQueuedTask
        {
            TaskId = Guid.Parse("10000000-0000-0000-0000-000000000000") ,
            RunnerId = index % 2 == 0 ? null : "server2",
            RepositoryScope = Repo,
            StintStartedAt = now.AddMinutes(-30),
            HoldClass = DispatchHoldDetails.Classify(detail).Class,
            HoldDetail = detail,
        }).ToList();
        // Distinct ids; the placeholder above is replaced per row.
        for (var i = 0; i < queued.Count; i++)
        {
            queued[i] = queued[i] with
            {
                TaskId = Guid.Parse($"10000000-0000-0000-0000-{i + 1:000000000000}"),
            };
        }

        queued.ShouldAllBe(task => task.HoldClass == ExpectationHoldClass.OrdinaryWait);
        var evaluation = ExpectationWatchdogPolicy.Evaluate(Snap(now, queued), Directive());
        evaluation.DispatchFence.ShouldBeNull();
        evaluation.ScopedFences.ShouldBeEmpty();

        var landDetail = DispatchHoldDetails.LeaseHeldByLand("abcd1234", "land the card", "req1", now);
        var capDetail = DispatchHoldDetails.ConcurrencyCap(4);
        var wrappedCap = DispatchHoldDetails.Escalation(
            "Warning:", 900, now.AddMinutes(-20), now.AddMinutes(-40), capDetail, 4, 4, ["abcd1234"]);
        var branchLanding = "held: CARD-0647's kept branch feat/x (task abcd1234) is landing and is not yet in origin/master";
        foreach (var detail in new[] { landDetail, capDetail, wrappedCap, branchLanding })
        {
            var occupied = AgedHold(detail, running: 1);
            occupied.StalledPipelines.Count.ShouldBe(
                0,
                "a land or a full cap with running tasks is not a stalled-pipeline episode");
            occupied.DispatchFence.ShouldBeNull();
            occupied.ScopedFences.ShouldBeEmpty();
            PagesStalledPipeline(occupied).ShouldBe(
                false,
                "occupied capacity must not page a stalled pipeline");
        }

        var openLand = new ExpectationOpenEpisode
        {
            Kind = ExpectationEpisodeKind.StalledPipeline,
            SubjectKey = ExpectationSubjects.Pipeline(DirectiveId, Repo),
            FirstObservedAt = now.AddHours(-2),
            Evidence = "queued behind a land",
        };
        var cleared = AgedHold(landDetail, running: 1, open: [openLand]);
        cleared.StalledPipelines.ShouldBeEmpty();
        cleared.ObservedClear.ShouldBeTrue();
        PagesStalledPipeline(cleared).ShouldBeFalse();

        var idleLand = AgedHold(landDetail, running: 0);
        var idleCap = AgedHold(capDetail, running: 0);
        var pinWhileBusy = AgedHold(
            "routing pin not before 2099-01-01T00:00:00Z; dispatch paused (wait).",
            running: 4);
        foreach (var paging in new[] { idleLand, idleCap, pinWhileBusy })
        {
            var episode = paging.StalledPipelines.ShouldHaveSingleItem();
            episode.Kind.ShouldBe(ExpectationEpisodeKind.StalledPipeline);
            episode.IsDue.ShouldBeTrue();
            episode.Immediate.ShouldBeFalse();
            PagesStalledPipeline(paging).ShouldBeTrue();
            paging.DispatchFence.ShouldBeNull();
            paging.ObservedClear.ShouldBeFalse();
        }

        var deadDetail = DispatchHoldDetails.LeaseFenced("dead child journal");
        var dead = AgedHold(deadDetail, running: 1);
        var deadEpisode = dead.StalledPipelines.ShouldHaveSingleItem();
        deadEpisode.Kind.ShouldBe(ExpectationEpisodeKind.StalledPipeline);
        deadEpisode.IsDue.ShouldBeTrue();
        deadEpisode.Immediate.ShouldBeFalse();
        deadEpisode.Evidence.ShouldContain("dead child journal");
        PagesStalledPipeline(dead).ShouldBeTrue();
        dead.DispatchFence.ShouldNotBeNull();
        dead.DispatchFence!.IsDue.ShouldBeTrue();
        dead.DispatchFence.Immediate.ShouldBeTrue();
        dead.ObservedClear.ShouldBeFalse();

        var unknownOwner = AgedHold(DispatchHoldDetails.LeaseOccupiedUnknown, running: 1);
        var unknownEpisode = unknownOwner.StalledPipelines.ShouldHaveSingleItem();
        unknownEpisode.Kind.ShouldBe(ExpectationEpisodeKind.StalledPipeline);
        unknownEpisode.IsDue.ShouldBeTrue();
        PagesStalledPipeline(unknownOwner).ShouldBeTrue();
        unknownOwner.DispatchFence.ShouldNotBeNull();
        unknownOwner.DispatchFence!.ReasonCode.ShouldBe("repository-owner-unknown");
        unknownOwner.DispatchFence.IsDue.ShouldBeTrue();

        var ineligible = AgedHold(
            DispatchHoldDetails.RunnerUnavailable("server2", "lease expired"),
            running: 1,
            runnerId: "server2");
        var ineligibleEpisode = ineligible.StalledPipelines.ShouldHaveSingleItem();
        ineligibleEpisode.Kind.ShouldBe(ExpectationEpisodeKind.StalledPipeline);
        ineligibleEpisode.IsDue.ShouldBeTrue();
        ineligibleEpisode.Immediate.ShouldBeFalse();
        PagesStalledPipeline(ineligible).ShouldBeTrue();
        ineligible.DispatchFence.ShouldBeNull();
        var runnerFence = ineligible.ScopedFences.ShouldHaveSingleItem();
        runnerFence.Scope.ShouldBe("runner:server2");
        runnerFence.IsDue.ShouldBeTrue();
        runnerFence.Immediate.ShouldBeTrue();

        var youngFence = DispatchHoldDetails.LeaseFenced("dead child journal");
        var landBesideYoungFence = ExpectationWatchdogPolicy.Evaluate(
            Snap(now,
            [
                Hold(LocalTaskId, landDetail, now.AddMinutes(-40)),
                Hold(RemoteTaskId, youngFence, now.AddMinutes(-1)),
            ]) with
            {
                Lanes = [new ExpectationLaneSnapshot { RunnerId = null, Target = 3, Running = 1 }],
            },
            Directive());
        landBesideYoungFence.StalledPipelines.ShouldBeEmpty();
        landBesideYoungFence.DispatchFence.ShouldBeNull();
        PagesStalledPipeline(landBesideYoungFence).ShouldBeFalse();

        var landBesideOldFence = ExpectationWatchdogPolicy.Evaluate(
            Snap(now,
            [
                Hold(LocalTaskId, landDetail, now.AddMinutes(-40)),
                Hold(RemoteTaskId, youngFence, now.AddMinutes(-30)),
            ]) with
            {
                Lanes = [new ExpectationLaneSnapshot { RunnerId = null, Target = 3, Running = 1 }],
            },
            Directive());
        landBesideOldFence.DispatchFence.ShouldBeNull();
        var fenceEpisode = landBesideOldFence.StalledPipelines.ShouldHaveSingleItem();
        fenceEpisode.Kind.ShouldBe(ExpectationEpisodeKind.StalledPipeline);
        fenceEpisode.IsDue.ShouldBeTrue();
        fenceEpisode.ExampleTaskIds.ShouldBe([RemoteTaskId]);
        PagesStalledPipeline(landBesideOldFence).ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task C650_Unknown_observation_does_not_resolve()
    {
        var open = new ExpectationOpenEpisode
        {
            Kind = ExpectationEpisodeKind.DispatchFence,
            SubjectKey = ExpectationSubjects.Fence(DirectiveId, Repo),
            FirstObservedAt = Now.AddHours(-5),
            Evidence = "journal",
        };
        var unknown = ExpectationWatchdogPolicy.Evaluate(
            new ExpectationSnapshot
            {
                AsOf = Now,
                ProbeUnknown = true,
                ProbeError = "runner probe failed",
                OpenEpisodes = [open],
            },
            Directive());
        unknown.ObservationUnknown.ShouldBeTrue();
        unknown.PreservesOpenEpisodes.ShouldBeTrue();
        unknown.ObservedClear.ShouldBeFalse();
        unknown.DispatchFence.ShouldBeNull();
        unknown.ProbeError.ShouldBe("runner probe failed");

        var cleared = ExpectationWatchdogPolicy.Evaluate(
            new ExpectationSnapshot { AsOf = Now, OpenEpisodes = [open] },
            Directive());
        cleared.ObservationUnknown.ShouldBeFalse();
        cleared.PreservesOpenEpisodes.ShouldBeFalse();
        cleared.ObservedClear.ShouldBeTrue();
        cleared.DispatchFence.ShouldBeNull();

        var still = ExpectationWatchdogPolicy.Evaluate(
            Snap(Now, [Fenced(LocalTaskId, null, Now.AddMinutes(-1)), Fenced(RemoteTaskId, "server2", Now.AddMinutes(-1))]) with
            {
                OpenEpisodes = [open],
            },
            Directive());
        still.ObservedClear.ShouldBeFalse();
        still.DispatchFence.ShouldNotBeNull();
        still.PreservesOpenEpisodes.ShouldBeFalse();
        await Task.CompletedTask;
    }

    private static ExpectationDirectiveSettings Directive() => new()
    {
        Id = DirectiveId,
        AgentId = AgentId,
        BoardId = BoardId,
        AuditCardId = Guid.Parse("dddddddd-dddd-dddd-dddd-ddddddddddd1"),
        OperatorChannelId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee1"),
        Enabled = true,
        Targets =
        [
            new ExpectationTargetSettings
            {
                InFlightTarget = 3,
                Candidates =
                [
                    new ExpectationCandidateSettings
                    {
                        AgentKind = AgentKind.Grok,
                        ModelLevel = AgentModelLevel.High,
                        SubscriptionKey = "local-key",
                    },
                ],
            },
            new ExpectationTargetSettings
            {
                RunnerId = "server2",
                InFlightTarget = 3,
                Candidates =
                [
                    new ExpectationCandidateSettings
                    {
                        AgentKind = AgentKind.ClaudeCode,
                        ModelLevel = AgentModelLevel.High,
                        SubscriptionKey = "remote-key",
                    },
                ],
            },
        ],
    };

    private static ExpectationSnapshot Snap(
        DateTime asOf,
        IReadOnlyList<ExpectationQueuedTask> queued,
        DateTime? lastDispatch = null) => new()
    {
        AsOf = asOf,
        DirectiveActive = true,
        Queued = queued,
        LastScopedDispatchAt = lastDispatch,
        RepositoryScope = Repo,
    };

    private static bool PagesStalledPipeline(ExpectationEvaluation evaluation) =>
        evaluation.StalledPipelines.Any(condition =>
            condition.IsDue && condition.Kind == ExpectationEpisodeKind.StalledPipeline);

    private static ExpectationQueuedTask Hold(Guid id, string detail, DateTime stint) => new()
    {
        TaskId = id,
        RepositoryScope = Repo,
        StintStartedAt = stint,
        HoldClass = DispatchHoldDetails.Classify(detail).Class,
        HoldDetail = detail,
    };

    private static ExpectationEvaluation AgedHold(
        string detail,
        int running,
        string? runnerId = null,
        Guid? taskId = null,
        IReadOnlyList<ExpectationOpenEpisode>? open = null)
    {
        var id = taskId ?? LocalTaskId;
        return ExpectationWatchdogPolicy.Evaluate(
            Snap(Now,
            [
                Hold(id, detail, Now.AddMinutes(-30)) with { RunnerId = runnerId },
            ]) with
            {
                Lanes =
                [
                    new ExpectationLaneSnapshot
                    {
                        RunnerId = null,
                        Target = 3,
                        Running = running,
                    },
                ],
                OpenEpisodes = open ?? [],
            },
            Directive());
    }

    private static ExpectationQueuedTask Queue(Guid id, DateTime stint) => new()
    {
        TaskId = id,
        RepositoryScope = Repo,
        StintStartedAt = stint,
        HoldClass = ExpectationHoldClass.OrdinaryWait,
    };

    private static ExpectationQueuedTask Fenced(Guid id, string? runnerId, DateTime stint) => new()
    {
        TaskId = id,
        RunnerId = runnerId,
        RepositoryScope = Repo,
        StintStartedAt = stint,
        HoldClass = ExpectationHoldClass.RepositoryFenced,
        HoldDetail = DispatchHoldDetails.LeaseFenced("dead journal"),
        HoldObservedAt = stint,
    };

    private static ExpectationQueuedTask Open(Guid id, string? runnerId, DateTime stint) => new()
    {
        TaskId = id,
        RunnerId = runnerId,
        RepositoryScope = Repo,
        StintStartedAt = stint,
    };

    private static ExpectationAdmissionCandidate Candidate(ExpectationAdmissionState state, string? runnerId) => new()
    {
        RunnerId = runnerId,
        AgentKind = runnerId is null ? AgentKind.Grok : AgentKind.ClaudeCode,
        ModelLevel = AgentModelLevel.High,
        SubscriptionKey = runnerId is null ? "local-key" : "remote-key",
        State = state,
    };

    private static ExpectationSnapshot CapacitySnap(
        int running, int queued, int backlog, DateTime? since, bool changed) => new()
    {
        AsOf = Now,
        DirectiveActive = true,
        EligibleBacklog = backlog,
        ConfigChanged = changed,
        Lanes =
        [
            new ExpectationLaneSnapshot
            {
                RunnerId = null,
                Target = 3,
                Running = running,
                Queued = queued,
                DeficitSince = since,
            },
        ],
    };

    private static SubscriptionUsageSnapshot Sample(double remaining, TimeSpan age) => new(
        AgentKind.Grok,
        "local-key",
        "pro",
        remaining,
        Now.AddDays(7),
        Now - age,
        age);
}
