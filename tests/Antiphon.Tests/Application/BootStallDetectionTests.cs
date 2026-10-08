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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.BootStallWorkingTickCharacterizationTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1151 (decision Q-1 option B): the dispatcher's boot disposition on a real overdue sweep.
/// Design V-3..V-6, V-9, V-16 here; V-12, V-13 and V-15 are S5 and stay skipped until that slice.
/// V-7 and V-8 were struck with option B (there is no automatic boot failure to prove or revoke).
/// Fixture: the shared <see cref="BootStallWorld"/> over an isolated PostgreSQL database with a
/// fake clock. Shipped deadlines stay in force (boot 8, model wait 20, Code ceiling 240); age is
/// arranged by back-dating rows. Every method asserts the destructive counters and the task's
/// identity as well as its named outcome.
/// </summary>
[Category("Integration")]
public class BootStallDetectionTests
{
    /// <summary>
    /// V-3. No listing, inventory or task-status shape changes the outcome, and the boot branch
    /// reads no inventory at all (option B has no absence proof). Working shapes at nine minutes
    /// write one BootStallDetected. Idle shapes (an interrupt marker after the prompt makes Working
    /// false) are aged past the 240-minute Code ceiling: the ceiling no longer fails an unresolved
    /// boot, and the operator event is written instead. terminal-row-reconciler-owned (A-1) and
    /// dispatched-pending-brief (A-2) write nothing and fail nothing. dispatched-sent-brief-detects
    /// is the real-world shape: Dispatched, brief Sent, prompt transcript-confirmed.
    /// </summary>
    [Test]
    [Arguments("working-listed")]
    [Arguments("working-empty-list")]
    [Arguments("idle-listed-running")]
    [Arguments("idle-listed-exited")]
    [Arguments("idle-wrong-generation")]
    [Arguments("unavailable-remote")]
    [Arguments("missing-directory")]
    [Arguments("null-list")]
    [Arguments("terminal-row-reconciler-owned")]
    [Arguments("dispatched-pending-brief")]
    [Arguments("dispatched-sent-brief-detects")]
    public async Task C1151_Listed_or_unknown_session_is_untouched(string shape)
    {
        var working = new BootStallWorldOptions { MinutesAgo = 9, Directory = BootStallDirectoryMode.Available };
        var idle = working with { MinutesAgo = 241, InterruptAfterPrompt = true };
        var (options, expected, session) = shape switch
        {
            "working-listed" => (working, BootStallPolicy.DetectedToken, SessionStatus.Running),
            "working-empty-list" => (working with { Listing = BootStallListing.None }, BootStallPolicy.DetectedToken, SessionStatus.Running),
            "idle-listed-running" => (idle, BootStallPolicy.NeedsOperatorToken, SessionStatus.Running),
            "idle-listed-exited" => (idle with { Listing = BootStallListing.Exited }, BootStallPolicy.NeedsOperatorToken, SessionStatus.Running),
            "idle-wrong-generation" => (idle with { Listing = BootStallListing.WrongGeneration }, BootStallPolicy.NeedsOperatorToken, SessionStatus.Running),
            "unavailable-remote" => (working with { Directory = BootStallDirectoryMode.Unavailable }, BootStallPolicy.DetectedToken, SessionStatus.Running),
            "missing-directory" => (working with { Directory = BootStallDirectoryMode.Missing }, BootStallPolicy.DetectedToken, SessionStatus.Running),
            "null-list" => (working with { Directory = BootStallDirectoryMode.NullList }, BootStallPolicy.DetectedToken, SessionStatus.Running),
            "terminal-row-reconciler-owned" => (working with { SessionStatus = SessionStatus.Stopped, SessionEnded = true }, (string?)null, SessionStatus.Stopped),
            "dispatched-pending-brief" => (working with { TaskStatus = AgentTaskStatus.Dispatched, Brief = QueuedMessageStatus.Pending }, (string?)null, SessionStatus.Running),
            "dispatched-sent-brief-detects" => (working with { TaskStatus = AgentTaskStatus.Dispatched, Brief = QueuedMessageStatus.Sent }, BootStallPolicy.DetectedToken, SessionStatus.Running),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(schema.ConnectionString, options);
        var before = await world.TaskAsync();

        (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
        (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());

        AssertNothingDestructive(world);
        await AssertSameAttemptAsync(world, before, session);
        await AssertNoFailureTraceAsync(world);
        world.Directory!.Reads.ShouldBe(0, "the boot branch never consults runner inventory");
        world.Runner.Lists.ShouldBe(0);
        var warnings = await world.BootWarningsAsync();
        if (expected is null)
        {
            warnings.ShouldBeEmpty($"{shape}: owned by another sweep, nothing is written");
        }
        else
        {
            warnings.Count.ShouldBe(1, world.Warnings());
            warnings[0].ShouldStartWith(expected + " ");
        }
    }

    /// <summary>
    /// V-4. An unresolved prompt-only boot stays open under every other clock: general-20m,
    /// ceiling-240m, custom-ceiling-earlier (Code ceiling 5, swept at 6 minutes: nothing is even
    /// due yet), model-wait-shorter-than-boot (ModelWait 5, Boot 8), workspace-progress (the probe
    /// reports a file change: it neither withholds nor authorizes anything any more),
    /// boot-notification-disabled (Boot 0: no Detected event, the 20-minute operator event
    /// instead). model-reply-returns-to-ordinary-policy: an assistant row after the prompt makes
    /// the task NotBoot, and the ordinary non-killing general failure applies at 20 minutes.
    /// </summary>
    [Test]
    [Arguments("general-20m")]
    [Arguments("ceiling-240m")]
    [Arguments("custom-ceiling-earlier")]
    [Arguments("model-wait-shorter-than-boot")]
    [Arguments("workspace-progress")]
    [Arguments("boot-notification-disabled")]
    [Arguments("model-reply-returns-to-ordinary-policy")]
    public async Task C1151_Boot_protection_survives_all_deadlines(string clock)
    {
        var (options, expected) = clock switch
        {
            "general-20m" => (new BootStallWorldOptions { MinutesAgo = 21 }, new[] { BootStallPolicy.NeedsOperatorToken }),
            "ceiling-240m" => (new BootStallWorldOptions { MinutesAgo = 241 }, new[] { BootStallPolicy.NeedsOperatorToken }),
            "custom-ceiling-earlier" => (new BootStallWorldOptions
            {
                MinutesAgo = 6,
                Configure = s => s.RolePolicy["Code"].TimeoutMinutes = 5,
            }, Array.Empty<string>()),
            "model-wait-shorter-than-boot" => (new BootStallWorldOptions
            {
                MinutesAgo = 9,
                Configure = s => s.ModelWaitDeadlineMinutes = 5,
            }, new[] { BootStallPolicy.NeedsOperatorToken }),
            "workspace-progress" => (new BootStallWorldOptions { MinutesAgo = 21 }, new[] { BootStallPolicy.NeedsOperatorToken }),
            "boot-notification-disabled" => (new BootStallWorldOptions
            {
                MinutesAgo = 21,
                Configure = s => s.BootModelWaitDeadlineMinutes = 0,
            }, new[] { BootStallPolicy.NeedsOperatorToken }),
            "model-reply-returns-to-ordinary-policy" => (new BootStallWorldOptions { MinutesAgo = 21, AssistantAfterPrompt = true }, Array.Empty<string>()),
            _ => throw new ArgumentOutOfRangeException(nameof(clock), clock, null),
        };

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(schema.ConnectionString, options);
        if (clock == "workspace-progress")
        {
            world.WorkspaceProbe = new StubWorkspaceProgressProbe(new WorkspaceProgressArm(
                Available: true, LastFileChangeAt: world.Now0.AddMinutes(-1), LastCommitAt: null, SharedCheckout: false));
        }

        var before = await world.TaskAsync();
        var failed = await world.RunOverdueSweepAsync();

        AssertNothingDestructive(world);
        if (clock == "model-reply-returns-to-ordinary-policy")
        {
            failed.ShouldBe(1, world.Warnings());
            var task = await world.TaskAsync();
            task.Status.ShouldBe(AgentTaskStatus.Failed, "a model row ends the boot episode");
            task.FailureCode.ShouldBeNull("the general arm has no failure code and no retry");
            task.FailureReason.ShouldNotBeNull();
            task.FailureReason.ShouldContain("The session was NOT killed");
            task.Attempt.ShouldBe(1);
            (await world.BootWarningsAsync()).ShouldBeEmpty();
            return;
        }

        failed.ShouldBe(0, world.Warnings());
        (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
        await AssertSameAttemptAsync(world, before);
        await AssertNoFailureTraceAsync(world);
        AssertNothingDestructive(world);
        var warnings = await world.BootWarningsAsync();
        warnings.Select(w => w.Split(' ')[0]).ShouldBe(expected, world.Warnings());
    }

    /// <summary>
    /// V-5. Operator threshold <c>max(bootDueAt, promptAt + 20 min)</c> on the fake clock.
    /// before-threshold: one Detected, no NeedsOperator. at-threshold: one NeedsOperator at
    /// equality and nothing else (an episode first seen after escalation gets one operator event,
    /// not a burst). after-threshold: Detected, then NeedsOperator once the clock passes it, then
    /// nothing more. restart-after-threshold: the escalation is written by a new provider and a
    /// third provider writes nothing. clock-rewind-after-detection: a provider whose clock stepped
    /// back below the boot due time writes nothing, the forward step writes the single operator
    /// event, and a step back after it adds no Detected. Every argument: same attempt, token and
    /// binding, no input, stopper empty, Details carry the key and never the prompt canary.
    /// </summary>
    [Test]
    [Arguments("before-threshold")]
    [Arguments("at-threshold")]
    [Arguments("after-threshold")]
    [Arguments("restart-after-threshold")]
    [Arguments("clock-rewind-after-detection")]
    public async Task C1151_Operator_escalation_preserves_the_attempt(string moment)
    {
        var age = moment switch
        {
            "before-threshold" => TimeSpan.FromMinutes(20) - TimeSpan.FromSeconds(1),
            "at-threshold" => TimeSpan.FromMinutes(20),
            "clock-rewind-after-detection" => TimeSpan.FromMinutes(9),
            _ => TimeSpan.FromMinutes(19),
        };
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, new BootStallWorldOptions { MinutesAgo = 21, PromptAge = age });
        var before = await world.TaskAsync();
        var operatorDue = world.PromptAt.AddMinutes(20);

        await world.RunOverdueSweepAsync();
        string[] expected;
        switch (moment)
        {
            case "before-threshold":
                expected = [BootStallPolicy.DetectedToken];
                break;
            case "at-threshold":
                world.Clock.GetUtcNow().UtcDateTime.ShouldBe(operatorDue, "the sweep ran at equality");
                expected = [BootStallPolicy.NeedsOperatorToken];
                break;
            case "after-threshold":
                world.Clock.SetUtcNow(new DateTimeOffset(operatorDue.AddMinutes(1), TimeSpan.Zero));
                await world.RunOverdueSweepAsync();
                await world.RunOverdueSweepAsync();
                expected = [BootStallPolicy.DetectedToken, BootStallPolicy.NeedsOperatorToken];
                break;
            case "restart-after-threshold":
                world.Clock.SetUtcNow(new DateTimeOffset(operatorDue.AddMinutes(1), TimeSpan.Zero));
                await using (var restarted = world.Recreate())
                    await restarted.RunOverdueSweepAsync();
                await using (var again = world.Recreate())
                    await again.RunOverdueSweepAsync();
                expected = [BootStallPolicy.DetectedToken, BootStallPolicy.NeedsOperatorToken];
                break;
            case "clock-rewind-after-detection":
                (await world.BootWarningsAsync()).Count.ShouldBe(1, world.Warnings());
                await using (var rewound = world.Recreate(Fake(world.PromptAt.AddMinutes(7))))
                    await rewound.RunOverdueSweepAsync();
                (await world.BootWarningsAsync()).Count.ShouldBe(1, "a step back mints no key and no event");
                world.Clock.SetUtcNow(new DateTimeOffset(operatorDue, TimeSpan.Zero));
                await world.RunOverdueSweepAsync();
                await using (var rewoundAgain = world.Recreate(Fake(world.PromptAt.AddMinutes(10))))
                    await rewoundAgain.RunOverdueSweepAsync();
                expected = [BootStallPolicy.DetectedToken, BootStallPolicy.NeedsOperatorToken];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(moment), moment, null);
        }

        var warnings = await world.BootWarningsAsync();
        warnings.Select(w => w.Split(' ')[0]).ShouldBe(expected, world.Warnings());
        foreach (var detail in warnings)
        {
            detail.ShouldContain($"episode={world.TaskId:N}/1/{world.SessionId:N}/");
            detail.ShouldContain($"operatorDueAt={operatorDue:o}");
            detail.ShouldNotContain(BootStallWorld.PromptCanary);
            detail.ShouldNotContain("the brief");
        }

        AssertNothingDestructive(world);
        await AssertSameAttemptAsync(world, before);
        await AssertNoFailureTraceAsync(world);
        (await world.PromptCountAsync()).ShouldBe(1);
    }

    /// <summary>
    /// V-6. Faults injected into the telemetry writer's own context: event-read (the key read),
    /// insert, commit, publish (event bus), later-unrelated-save (the sweep's own scoped context
    /// saves an unrelated row after a failed telemetry insert). Decisive: the disposition is
    /// unchanged, the sweep fails nothing and logs no evaluation error, no Warning row leaks, and
    /// once the fault clears the next sweep records the event exactly once.
    /// </summary>
    [Test]
    [Arguments("event-read")]
    [Arguments("insert")]
    [Arguments("commit")]
    [Arguments("publish")]
    [Arguments("later-unrelated-save")]
    public async Task C1151_Telemetry_failure_never_changes_disposition(string fault)
    {
        var faults = new TelemetryFaults(fault);
        var bus = new MockEventBus { ThrowOnceOnEvent = fault == "publish" ? "AgentTaskChanged" : null };
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, new BootStallWorldOptions { MinutesAgo = 9, EventBus = bus });
        world.UseTelemetryFactory = true;
        world.TelemetryInterceptors = [faults, faults.Commit];
        var before = await world.TaskAsync();

        await using (var scope = world.CreateScope())
        {
            var dispatcher = world.Prepare(scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>());
            (await dispatcher.FailOverdueTasksAsync(CancellationToken.None)).ShouldBe(0, world.Warnings());
            if (fault == "later-unrelated-save")
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(),
                    AgentTaskId = world.TaskId,
                    Type = AgentTaskEventType.Warning,
                    Detail = "unrelated save after a failed telemetry write",
                    At = world.Now0,
                });
                await db.SaveChangesAsync();
            }
        }

        if (fault == "publish")
            world.Warnings().ShouldContain("Could not publish the boot-stall change");
        else
            faults.Fired.ShouldBeTrue($"the {fault} fault must actually fire");
        world.Warnings().ShouldNotContain("Overdue-deadline evaluation");
        AssertNothingDestructive(world);
        await AssertSameAttemptAsync(world, before);
        await AssertNoFailureTraceAsync(world);
        (await world.BootWarningsAsync()).Count.ShouldBe(fault == "publish" ? 1 : 0,
            "a failed telemetry transaction leaves nothing behind; a failed publish is after the commit");

        faults.Clear();
        (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
        (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
        var warnings = await world.BootWarningsAsync();
        warnings.Count.ShouldBe(1, world.Warnings());
        warnings[0].ShouldStartWith(BootStallPolicy.DetectedToken + " ");
        await AssertSameAttemptAsync(world, before);
        AssertNothingDestructive(world);
    }

    /// <summary>
    /// V-9. repeated-tick: three sweeps, one Detected. service-recreation: a second provider over
    /// the same database, still one. concurrent-contexts: two scoped dispatchers released together
    /// at the runner pull, still one (the task lock serializes the key read and insert).
    /// later-real-prompt: a second real prompt into the still-silent session is a new key with
    /// exactly one new Detected once it is due; the old key gets nothing further.
    /// </summary>
    [Test]
    [Arguments("repeated-tick")]
    [Arguments("service-recreation")]
    [Arguments("concurrent-contexts")]
    [Arguments("later-real-prompt")]
    public async Task C1151_Warnings_deduplicate_per_episode(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, new BootStallWorldOptions { MinutesAgo = 9 });
        var before = await world.TaskAsync();
        var expectedKeys = 1;

        switch (shape)
        {
            case "repeated-tick":
                for (var i = 0; i < 3; i++)
                    (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
                break;
            case "service-recreation":
                await world.RunOverdueSweepAsync();
                await using (var second = world.Recreate())
                    await second.RunOverdueSweepAsync();
                await world.RunOverdueSweepAsync();
                break;
            case "concurrent-contexts":
            {
                var barrier = new Barrier(2);
                world.CatchUp = (_, _) =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(20)).ShouldBeTrue("both dispatchers reach the pull");
                    return Task.CompletedTask;
                };
                await using var a = world.CreateScope();
                await using var b = world.CreateScope();
                var first = world.Prepare(a.ServiceProvider.GetRequiredService<AgentTaskDispatcher>());
                var second = world.Prepare(b.ServiceProvider.GetRequiredService<AgentTaskDispatcher>());
                var results = await Task.WhenAll(
                    Task.Run(() => first.FailOverdueTasksAsync(CancellationToken.None)),
                    Task.Run(() => second.FailOverdueTasksAsync(CancellationToken.None)));
                results.ShouldBe([0, 0], world.Warnings());
                break;
            }
            case "later-real-prompt":
            {
                await world.RunOverdueSweepAsync();
                var refined = world.Now0;
                await world.AddEntryAsync(TranscriptKinds.UserPrompt, "a refinement, still unanswered", refined);
                await world.RunOverdueSweepAsync();
                world.Clock.SetUtcNow(new DateTimeOffset(refined.AddMinutes(8), TimeSpan.Zero));
                await world.RunOverdueSweepAsync();
                await world.RunOverdueSweepAsync();
                expectedKeys = 2;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }

        var warnings = await world.BootWarningsAsync();
        warnings.Count.ShouldBe(expectedKeys, world.Warnings());
        warnings.ShouldAllBe(w => w.StartsWith(BootStallPolicy.DetectedToken + " "));
        warnings.Select(w => w.Split(';')[0]).Distinct().Count().ShouldBe(expectedKeys, "one event per key and stage");
        AssertNothingDestructive(world);
        await AssertSameAttemptAsync(world, before);
        await AssertNoFailureTraceAsync(world);
    }

    /// <summary>
    /// V-12 (S5). A detected Working task keeps its seat: no AgentTaskPark row, no
    /// RunnerSeatRelease row, runner Releases 0, stopper empty, task Working, with
    /// BlockedTaskParking Enabled false (parking-off) and true (parking-on). The session-scoped
    /// BootReplyWatchdogService sweep in the same fixture raises no LivenessProbeFailed incident
    /// and calls no stopper because the task stays open.
    /// </summary>
    [Test]
    [Arguments("parking-off")]
    [Arguments("parking-on")]
    public Task C1151_Detection_does_not_release_or_park(string parking) =>
        Card1151Pending.Skip("S5", nameof(C1151_Detection_does_not_release_or_park));

    /// <summary>
    /// V-13 (S5). FullCommandCounter over every context (the sweep's and the telemetry
    /// writer's) plus runner call counters, on the paths of the design's statement table under
    /// option B. Code pins the measured exact totals and each argument prints its roster.
    /// young-preview: 0 delta. working-first-detection. working-repeated-episode (A-7: no runner
    /// pull, no second evaluation). operator-escalation. identity-changed-before-event.
    /// event-save-fault. non-working-listed and absent-proof-refused: the idle boot past the
    /// ceiling, with zero inventory reads under option B. The two safe-absent arguments were
    /// struck with option B.
    /// </summary>
    [Test]
    [Arguments("young-preview")]
    [Arguments("working-first-detection")]
    [Arguments("working-repeated-episode")]
    [Arguments("operator-escalation")]
    [Arguments("identity-changed-before-event")]
    [Arguments("event-save-fault")]
    [Arguments("non-working-listed")]
    [Arguments("absent-proof-refused")]
    public Task C1151_Boot_branch_statement_counts(string path) =>
        Card1151Pending.Skip("S5", nameof(C1151_Boot_branch_statement_counts));

    /// <summary>
    /// V-15 (S5). The delivery watchdog's stopper stays conditional. real-idle-failure-cleans-up:
    /// a Dispatched task, Pending brief, idle session, ten minutes: Failed and the existing kill.
    /// working-withholds: the same with a Working transcript: Failed, no kill.
    /// stale-or-unsuccessful-failure-withholds: the Failed write is refused by a concurrency
    /// fault; no kill. In every argument a BootStallDetected Warning present on the task is never
    /// read as a delivery failure and never routes to the stopper.
    /// </summary>
    [Test]
    [Arguments("real-idle-failure-cleans-up")]
    [Arguments("working-withholds")]
    [Arguments("stale-or-unsuccessful-failure-withholds")]
    public Task C1151_Delivery_watchdog_stopper_requires_real_safe_failure(string shape) =>
        Card1151Pending.Skip("S5", nameof(C1151_Delivery_watchdog_stopper_requires_real_safe_failure));

    /// <summary>
    /// V-16. A detected Working task retried by the explicit <c>AgentTaskService.RetryAsync</c>
    /// still stops the delegate and requeues at the same kind and tier. Under option B there is no
    /// internal no-stop requeue to reach at all: no <c>AgentTaskService</c> or dispatcher member
    /// takes a stop-skipping switch.
    /// </summary>
    [Test]
    public async Task C1151_Explicit_retry_retains_operator_semantics()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, new BootStallWorldOptions { MinutesAgo = 9 });
        await world.RunOverdueSweepAsync();
        (await world.BootWarningsAsync()).Count.ShouldBe(1, world.Warnings());
        world.Stopper.Killed.ShouldBeEmpty("detection stopped nothing");

        await using (var scope = world.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(world.TaskId, CancellationToken.None);

        world.Stopper.Killed.ShouldBe([world.SessionId], "the operator's explicit Retry keeps its stop");
        var task = await world.TaskAsync();
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.Attempt.ShouldBe(2);
        task.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        task.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        (await world.EventsAsync()).Count(e => e.Type == AgentTaskEventType.Retried).ShouldBe(1);

        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;
        foreach (var type in new[] { typeof(AgentTaskService), typeof(AgentTaskDispatcher) })
        {
            type.GetMethods(all)
                .SelectMany(m => m.GetParameters().Select(p => $"{m.Name}({p.Name})"))
                .Where(p => p.Contains("skipStop", StringComparison.OrdinalIgnoreCase)
                    || p.Contains("withoutStop", StringComparison.OrdinalIgnoreCase)
                    || p.Contains("AbsentRequeue", StringComparison.OrdinalIgnoreCase)
                    || p.Contains("AbsentSession", StringComparison.OrdinalIgnoreCase))
                .ShouldBeEmpty($"{type.Name} exposes no stop-skipping requeue");
        }

        typeof(AgentTaskDispatcher).GetMethods(all).Select(m => m.Name)
            .ShouldNotContain("TryFailBootStallAsync", "the automatic boot tail is removed");
    }

    /// <summary>
    /// CARD-1151 R1 at the dispatcher. accepted-no-reply-detects: an accepted prompt with no reply
    /// is DetectOnly with one Detected and no failure. accepted-then-queued-refinement: past the
    /// ceiling, the accepted prompt's episode has its operator event; a queued refinement then
    /// lands and the clock moves eight minutes past it. The queued row neither opens a new episode
    /// nor restarts the clock: no Detected for a second key, still no failure.
    /// queued-only-past-ceiling: the one prompt was only queued, so there is no episode and the
    /// shipped 240-minute Code ceiling fails the task non-destructively, exactly as before
    /// CARD-1151.
    /// </summary>
    [Test]
    [Arguments("accepted-no-reply-detects")]
    [Arguments("accepted-then-queued-refinement")]
    [Arguments("queued-only-past-ceiling")]
    public async Task C1151_Only_an_accepted_prompt_is_protected(string shape)
    {
        var options = shape switch
        {
            "queued-only-past-ceiling" => new BootStallWorldOptions { MinutesAgo = 241, PromptKind = TranscriptKinds.QueuedUserPrompt },
            "accepted-then-queued-refinement" => new BootStallWorldOptions { MinutesAgo = 241 },
            _ => new BootStallWorldOptions { MinutesAgo = 9 },
        };
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(schema.ConnectionString, options);
        var before = await world.TaskAsync();

        if (shape == "queued-only-past-ceiling")
        {
            (await world.RunOverdueSweepAsync()).ShouldBe(1, world.Warnings());
            var task = await world.TaskAsync();
            task.Status.ShouldBe(AgentTaskStatus.Failed, "nothing was received, so nothing is protected");
            task.FailureCode.ShouldBeNull();
            task.FailureReason.ShouldNotBeNull();
            task.FailureReason.ShouldContain("240-minute ceiling for role Code");
            task.FailureReason.ShouldContain("Last transcript entry: QueuedUserPrompt");
            task.FailureReason.ShouldContain("The session was NOT killed");
            task.Attempt.ShouldBe(1);
            (await world.BootWarningsAsync()).ShouldBeEmpty("a queued-only prompt is no boot episode");
            AssertNothingDestructive(world);
            return;
        }

        (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
        var first = await world.BootWarningsAsync();
        first.Count.ShouldBe(1, world.Warnings());
        var expected = shape == "accepted-then-queued-refinement"
            ? BootStallPolicy.NeedsOperatorToken
            : BootStallPolicy.DetectedToken;
        first[0].ShouldStartWith(expected + " ");
        var key = first[0].Split(';')[0].Split(' ')[1];

        if (shape == "accepted-then-queued-refinement")
        {
            var refined = world.Now0;
            await world.AddEntryAsync(TranscriptKinds.QueuedUserPrompt, "a refinement, still queued", refined);
            (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
            world.Clock.SetUtcNow(new DateTimeOffset(refined.AddMinutes(8), TimeSpan.Zero));
            (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
            (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
        }

        var warnings = await world.BootWarningsAsync();
        warnings.Count.ShouldBe(1, $"{shape}: one episode, one event; a queued row opens none\n{world.Warnings()}");
        warnings[0].ShouldContain($" {key};", customMessage: "the event belongs to the accepted prompt's episode");
        warnings[0].ShouldContain($"promptAt={world.PromptAt:o};");
        AssertNothingDestructive(world);
        await AssertSameAttemptAsync(world, before);
        await AssertNoFailureTraceAsync(world);
    }

    private static FakeTimeProvider Fake(DateTime at) => new(new DateTimeOffset(at, TimeSpan.Zero));

    /// <summary>
    /// One fault at a time on the telemetry writer's context. Matches the writer's own statements
    /// only: the boot-warning key read, the event insert, and the transaction commit.
    /// </summary>
    private sealed class TelemetryFaults : DbCommandInterceptor
    {
        private readonly string _fault;
        private volatile bool _armed = true;

        public TelemetryFaults(string fault)
        {
            _fault = fault;
            Commit = new CommitFault(this);
        }

        public bool Fired { get; private set; }

        public DbTransactionInterceptor Commit { get; }

        public void Clear() => _armed = false;

        private void Maybe(DbCommand command)
        {
            if (!_armed)
                return;
            var sql = command.CommandText;
            var hit = _fault switch
            {
                "event-read" => sql.Contains("\"AgentTaskEvents\"") && sql.Contains("LIKE") && sql.TrimStart().StartsWith("SELECT"),
                "insert" or "later-unrelated-save" => sql.Contains("INSERT INTO \"AgentTaskEvents\""),
                _ => false,
            };
            if (!hit)
                return;
            Fired = true;
            throw new InvalidOperationException($"C1151 injected {_fault} fault");
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Maybe(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Maybe(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private sealed class CommitFault(TelemetryFaults owner) : DbTransactionInterceptor
        {
            public override ValueTask<InterceptionResult> TransactionCommittingAsync(
                DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
                CancellationToken cancellationToken = default)
            {
                if (owner._armed && owner._fault == "commit")
                {
                    owner.Fired = true;
                    throw new InvalidOperationException("C1151 injected commit fault");
                }

                return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
            }
        }
    }
}
