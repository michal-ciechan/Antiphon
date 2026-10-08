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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
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

    /// <summary>
    /// CARD-1151 R2. The warning writer revalidates the whole episode immediately before its
    /// insert. An interleaving runs on a separate connection just as the writer's first statement
    /// (the task-row lock) executes, after the sweep has decided. generation-change: the session
    /// row's accepted generation moves (launch clock unchanged). launch-clock-change: a resume
    /// moves the launch clock to between dispatch and the prompt (prompt still visible, generation
    /// unchanged). prompt-identity-change: a newer accepted prompt lands. Each writes nothing, and
    /// silently. same-episode: the interleaving changes nothing and the event is written once
    /// across two sweeps. concurrent-writers: two dispatchers meet at the writer's lock statement;
    /// exactly one row.
    /// </summary>
    [Test]
    [Arguments("generation-change")]
    [Arguments("launch-clock-change")]
    [Arguments("prompt-identity-change")]
    [Arguments("same-episode")]
    [Arguments("concurrent-writers")]
    public async Task C1151_Warning_writer_revalidates_the_episode_identity(string change)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString,
            new BootStallWorldOptions { MinutesAgo = 10, PromptAge = TimeSpan.FromMinutes(9) });
        var before = await world.TaskAsync();
        var dispatched = before.DispatchedAt!.Value;
        var interleave = new WriterInterleaving(change switch
        {
            "generation-change" => async () =>
            {
                await using var db = world.Read();
                await db.AgentSessions.Where(s => s.Id == world.SessionId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedAt, world.Now0));
            },
            "launch-clock-change" => async () =>
            {
                await using var db = world.Read();
                await db.AgentSessions.Where(s => s.Id == world.SessionId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.LaunchResumedAt, (DateTime?)dispatched.AddSeconds(30)));
            },
            "prompt-identity-change" => () => world.AddEntryAsync(
                TranscriptKinds.UserPrompt, "a newer accepted prompt", world.Now0.AddMinutes(-1)),
            "same-episode" => () => Task.CompletedTask,
            "concurrent-writers" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
        });
        world.UseTelemetryFactory = true;
        world.TelemetryInterceptors = [interleave];

        if (change == "concurrent-writers")
        {
            await using var a = world.CreateScope();
            await using var b = world.CreateScope();
            var first = world.Prepare(a.ServiceProvider.GetRequiredService<AgentTaskDispatcher>());
            var second = world.Prepare(b.ServiceProvider.GetRequiredService<AgentTaskDispatcher>());
            var results = await Task.WhenAll(
                Task.Run(() => first.FailOverdueTasksAsync(CancellationToken.None)),
                Task.Run(() => second.FailOverdueTasksAsync(CancellationToken.None)));
            results.ShouldBe([0, 0], world.Warnings());
            interleave.Arrivals.ShouldBe(2, "both writers reached the lock statement together");
        }
        else
        {
            (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
            interleave.Arrivals.ShouldBe(1, "the interleaving ran inside the writer, after the decision");
        }

        var warnings = await world.BootWarningsAsync();
        if (change is "same-episode" or "concurrent-writers")
        {
            if (change == "same-episode")
                (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
            warnings = await world.BootWarningsAsync();
            warnings.Count.ShouldBe(1, world.Warnings());
            warnings[0].ShouldStartWith(BootStallPolicy.DetectedToken + " ");
        }
        else
        {
            warnings.ShouldBeEmpty($"{change}: the decided episode is no longer current, so nothing is written");
            world.Warnings().ShouldNotContain("Could not record", customMessage: "a mismatch is silent, not a fault");
        }

        AssertNothingDestructive(world);
        await AssertNoFailureTraceAsync(world);
        var task = await world.TaskAsync();
        task.Status.ShouldBe(before.Status);
        task.Attempt.ShouldBe(before.Attempt);
        task.AgentSessionId.ShouldBe(world.SessionId);
        task.FailureReason.ShouldBeNull();
    }

    /// <summary>
    /// CARD-1151 H-7 (repair 2, Final Review 409623bd F1). Gate 2's runner pull is the only way a
    /// reply the database has not stored yet reaches the decision. Every argument starts from
    /// stored rows with an accepted prompt 21 minutes old and no model reply, so the stored-row
    /// pass alone would write BootStallNeedsOperator. fresh-reply-lands-in-the-pull: the pull
    /// lands a reply stamped now; the episode ends and the ordinary policy finds nothing due (no
    /// warning, task Working, and the next sweep has no reason to pull). stale-reply-lands-in-the-pull:
    /// the pull lands a reply the tailer missed 30 s after the prompt; the ordinary general clock
    /// fails the task non-destructively, with no boot warning. pull-times-out: the production pull
    /// reaches the runner and its transcript request times out (a TaskCanceledException on an
    /// uncancelled sweep); detection still records BootStallNeedsOperator once and the task stays
    /// Working. CP-4's pre-seeded model-reply-returns-to-ordinary-policy stays the stored-row control.
    /// </summary>
    [Test]
    [Arguments("fresh-reply-lands-in-the-pull")]
    [Arguments("stale-reply-lands-in-the-pull")]
    [Arguments("pull-times-out")]
    public async Task C1151_Reply_landing_in_the_pull_ends_the_episode(string pull)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, new BootStallWorldOptions { MinutesAgo = 21 });
        var before = await world.TaskAsync();
        await using (var db = world.Read())
        {
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == world.SessionId
                && t.Kind != TranscriptKinds.UserPrompt)).ShouldBe(0, "control: no model reply is stored before the pull");
        }

        var pulls = 0;
        DateTime? replyAt = pull switch
        {
            "fresh-reply-lands-in-the-pull" => world.Now0,
            "stale-reply-lands-in-the-pull" => world.PromptAt.AddSeconds(30),
            "pull-times-out" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(pull), pull, null),
        };
        if (replyAt is { } at)
        {
            world.CatchUp = async (sessionId, _) =>
            {
                sessionId.ShouldBe(world.SessionId);
                if (++pulls == 1)
                    await world.AddEntryAsync(TranscriptKinds.AssistantText, "the reply the tailer had not stored", at);
            };
        }
        else
        {
            world.Runner.TranscriptFault = new TaskCanceledException("C1151 runner transcript request timed out");
        }

        var failed = await world.RunOverdueSweepAsync();

        AssertNothingDestructive(world);
        world.Warnings().ShouldNotContain("Overdue-deadline evaluation");
        switch (pull)
        {
            case "fresh-reply-lands-in-the-pull":
                failed.ShouldBe(0, world.Warnings());
                pulls.ShouldBe(1, "the sweep pulled before deciding");
                (await world.BootWarningsAsync()).ShouldBeEmpty("the reply the pull landed ended the episode");
                (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
                pulls.ShouldBe(1, "with the reply stored nothing is due, so nothing is pulled");
                (await world.BootWarningsAsync()).ShouldBeEmpty();
                await AssertSameAttemptAsync(world, before);
                await AssertNoFailureTraceAsync(world);
                break;
            case "stale-reply-lands-in-the-pull":
            {
                failed.ShouldBe(1, world.Warnings());
                pulls.ShouldBe(1, "the sweep pulled before deciding");
                var task = await world.TaskAsync();
                task.Status.ShouldBe(AgentTaskStatus.Failed, "the landed reply returns the task to the ordinary general clock");
                task.FailureCode.ShouldBeNull("the general arm has no failure code and no retry");
                task.FailureReason.ShouldNotBeNull();
                task.FailureReason.ShouldContain("The session was NOT killed");
                task.Attempt.ShouldBe(1);
                (await world.BootWarningsAsync()).ShouldBeEmpty("a resolved boot writes no boot warning");
                break;
            }
            default:
            {
                failed.ShouldBe(0, world.Warnings());
                world.Runner.TranscriptPulls.ShouldBe(1, "the production pull reached the runner and failed there");
                var warnings = await world.BootWarningsAsync();
                warnings.Select(w => w.Split(' ')[0]).ShouldBe([BootStallPolicy.NeedsOperatorToken], world.Warnings());
                await AssertSameAttemptAsync(world, before);
                await AssertNoFailureTraceAsync(world);
                break;
            }
        }
    }

    /// <summary>
    /// CARD-1151 repair 2 (Final Review 409623bd F2). The warning writer's session revalidation
    /// under real two-connection contention, through the production logging graph: the scoped
    /// context and the writer's default context log through the world's factory, captured from
    /// Debug up, so EF Core's own command and query errors are visible. held-session-row: a second
    /// connection holds an uncommitted UPDATE of the session row (the lock any session write
    /// takes), and a control proves the row really refuses a share lock. The sweep returns
    /// promptly, writes no event and leaves the task Working; in that window nothing is logged
    /// above Debug except EF's routine executed-command records, nothing carries an exception, and
    /// the writer says at Debug that the session was mid-update. After the holder rolls back the
    /// next sweep records exactly one BootStallDetected. session-read-fault: a genuine, non-contention
    /// fault on the same session statement is still reported as before (the writer's Warning) and
    /// writes nothing; once it clears, one event.
    /// </summary>
    [Test]
    [Arguments("held-session-row")]
    [Arguments("session-read-fault")]
    public async Task C1151_Session_row_contention_is_quiet(string shape)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, new BootStallWorldOptions { MinutesAgo = 9, MinimumLogLevel = LogLevel.Debug });
        var before = await world.TaskAsync();

        if (shape == "session-read-fault")
        {
            var fault = new SessionStatementFault();
            world.UseTelemetryFactory = true;
            world.TelemetryInterceptors = [fault];
            (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
            fault.Fired.ShouldBeTrue("the fault must hit the writer's session statement");
            world.Warnings().ShouldContain(
                $"Could not record {BootStallPolicy.DetectedToken}", customMessage: "a genuine fault is still reported");
            (await world.BootWarningsAsync()).ShouldBeEmpty("a faulted statement writes nothing");
            await AssertSameAttemptAsync(world, before);
            fault.Armed = false;
        }
        else if (shape == "held-session-row")
        {
            // Warm the model so first-use model-building logs fall outside the measured window.
            await using (var warm = world.CreateScope())
                await warm.ServiceProvider.GetRequiredService<AppDbContext>().AgentSessions.AnyAsync();

            await using var holder = new NpgsqlConnection(schema.ConnectionString);
            await holder.OpenAsync();
            await using var hold = await holder.BeginTransactionAsync();
            await using (var update = new NpgsqlCommand(
                "UPDATE \"AgentSessions\" SET \"LastSeenAt\" = \"LastSeenAt\" WHERE \"Id\" = @id", holder, hold))
            {
                update.Parameters.AddWithValue("id", world.SessionId);
                (await update.ExecuteNonQueryAsync()).ShouldBe(1, "the holder writes the session row");
            }

            await using (var probe = new NpgsqlConnection(schema.ConnectionString))
            {
                await probe.OpenAsync();
                await using var check = new NpgsqlCommand(
                    "SELECT 1 FROM \"AgentSessions\" WHERE \"Id\" = @id FOR SHARE NOWAIT", probe);
                check.Parameters.AddWithValue("id", world.SessionId);
                (await Should.ThrowAsync<PostgresException>(() => check.ExecuteScalarAsync()))
                    .SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable, "control: the row is really held");
            }

            var mark = world.LogEntries().Count;
            try
            {
                (await world.RunOverdueSweepAsync().WaitAsync(TimeSpan.FromSeconds(30)))
                    .ShouldBe(0, world.Warnings());
            }
            finally
            {
                await hold.RollbackAsync();
            }

            var during = world.LogEntries().Skip(mark).ToList();
            var described = string.Join('\n', during.Select(e => $"{e.Level} {e.Category} [{e.EventId.Id}]: {e.Message}"));
            during.ShouldContain(
                e => e.Level == LogLevel.Debug && e.Message.Contains("is missing or mid-update"),
                $"the writer met the held row and said so at Debug\n{described}");
            during.Where(e => e.Level > LogLevel.Debug && e.EventId.Id != RelationalEventId.CommandExecuted.Id)
                .ShouldBeEmpty($"expected contention logs nothing above Debug\n{described}");
            during.Where(e => e.Exception is not null)
                .ShouldBeEmpty($"expected contention is a result, not an exception\n{described}");
            (await world.BootWarningsAsync()).ShouldBeEmpty("a held session row writes nothing");
            await AssertSameAttemptAsync(world, before);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }

        (await world.RunOverdueSweepAsync()).ShouldBe(0, world.Warnings());
        var warnings = await world.BootWarningsAsync();
        warnings.Count.ShouldBe(1, world.Warnings());
        warnings[0].ShouldStartWith(BootStallPolicy.DetectedToken + " ");
        AssertNothingDestructive(world);
        await AssertSameAttemptAsync(world, before);
        await AssertNoFailureTraceAsync(world);
    }

    /// <summary>A non-contention fault on the warning writer's session share-lock statement.</summary>
    private sealed class SessionStatementFault : DbCommandInterceptor
    {
        public volatile bool Armed = true;

        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed
                && command.CommandText.Contains("\"AgentSessions\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR SHARE", StringComparison.Ordinal))
            {
                Fired = true;
                throw new InvalidOperationException("C1151 injected session-statement fault");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Runs one interleaving (or, with none, meets a second writer) just as the warning writer's
    /// task-row lock statement executes: the sweep has already decided and only the writer's
    /// revalidation stands between that decision and the insert.
    /// </summary>
    private sealed class WriterInterleaving(Func<Task>? change) : DbCommandInterceptor
    {
        private readonly Barrier _writers = new(2);
        private int _arrivals;

        public int Arrivals => Volatile.Read(ref _arrivals);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"AgentTasks\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
            {
                var arrival = Interlocked.Increment(ref _arrivals);
                if (change is null)
                    _writers.SignalAndWait(TimeSpan.FromSeconds(20)).ShouldBeTrue("both writers reach the lock");
                else if (arrival == 1)
                    await change();
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
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
