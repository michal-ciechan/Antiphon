using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The boot-stall path on a real <c>TickAsync</c>, production boot deadline 8 minutes. CARD-1151
/// (option B): an unanswered boot prompt on a Working session is detection only — CARD-0079 is the
/// only automatic stop of a Working session. The world is the shared
/// <see cref="BootStallWorld"/> (A-11), with a fake clock pinned to the seeding instant.
/// </summary>
[Category("Integration")]
public class BootStallWorkingTickCharacterizationTests
{
    /// <summary>
    /// V-1. CARD-1151 deliberately reverses the CARD-1149/1150 characterization: boot silence is
    /// detection; the original stop/requeue assertions are replaced by positive
    /// same-attempt/same-session assertions. The fixture is unchanged in substance (nine-minute
    /// prompt, quiet available workspace, listed matching generation, boot wait 8, cap 0).
    /// </summary>
    [Test]
    public async Task Aged_prompt_only_Working_tick_detects_without_stopping_or_requeueing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, new BootStallWorldOptions { MinutesAgo = 9, MaxConcurrentTasks = 0 });
        var before = await world.TaskAsync();

        var tick = await world.TickAsync();

        tick.SweepFailures.ShouldBe(0, world.Warnings());
        // Was 1 while the boot tail requeued the task: no Queued retry exists now, so the claim
        // loop sees no row and the zero cap is never consulted.
        tick.SkippedConcurrency.ShouldBe(0, world.Warnings());
        tick.Dispatched.ShouldBe(0);
        AssertNothingDestructive(world);

        await AssertSameAttemptAsync(world, before);
        var warnings = await world.BootWarningsAsync();
        warnings.Count.ShouldBe(1, world.Warnings());
        warnings[0].ShouldStartWith(BootStallPolicy.DetectedToken + " ");
        warnings[0].ShouldNotContain(BootStallWorld.PromptCanary);
        await AssertNoFailureTraceAsync(world);

        // Repeat on the same provider, then from a freshly built provider: still one event.
        var second = await world.TickAsync();
        second.SweepFailures.ShouldBe(0, world.Warnings());
        await using (var fresh = world.Recreate())
        {
            var third = await fresh.TickAsync();
            third.SweepFailures.ShouldBe(0, fresh.Warnings());
        }

        (await world.BootWarningsAsync()).Count.ShouldBe(1, world.Warnings());
        AssertNothingDestructive(world);
        await AssertSameAttemptAsync(world, before);
        await AssertNoFailureTraceAsync(world);
        (await world.PromptCountAsync()).ShouldBe(1, "detection sends nothing to the delegate");
    }

    [Test]
    public async Task Aged_Working_tick_with_an_assistant_row_is_not_stopped()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString,
            new BootStallWorldOptions { MinutesAgo = 9, AssistantAfterPrompt = true, MaxConcurrentTasks = 2 });
        var before = await world.TaskAsync();

        var tick = await world.TickAsync();

        tick.SweepFailures.ShouldBe(0, world.Warnings());
        tick.Dispatched.ShouldBe(0);
        tick.SkippedConcurrency.ShouldBe(0);
        AssertNothingDestructive(world);
        await AssertSameAttemptAsync(world, before);
        (await world.BootWarningsAsync()).ShouldBeEmpty("a model row ended the boot turn");
        (await world.PromptCountAsync()).ShouldBe(1);
    }

    [Test]
    public async Task Young_prompt_only_Working_tick_is_not_stopped()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var world = await BootStallWorld.CreateAsync(
            schema.ConnectionString, new BootStallWorldOptions { MinutesAgo = 1, MaxConcurrentTasks = 2 });
        var before = await world.TaskAsync();

        var tick = await world.TickAsync();

        tick.SweepFailures.ShouldBe(0, world.Warnings());
        tick.Dispatched.ShouldBe(0);
        tick.SkippedConcurrency.ShouldBe(0);
        AssertNothingDestructive(world);
        await AssertSameAttemptAsync(world, before);
        (await world.BootWarningsAsync()).ShouldBeEmpty("one minute is short of every stage");
        (await world.PromptCountAsync()).ShouldBe(1);
    }

    internal static void AssertNothingDestructive(BootStallWorld world)
    {
        world.Stopper.Killed.ShouldBeEmpty(world.Warnings());
        world.Runner.Kills.ShouldBe(0);
        world.Runner.Starts.ShouldBe(0);
        world.Runner.Releases.ShouldBe(0);
        world.Runner.CompactionStops.ShouldBe(0);
        world.Runner.Inputs.ShouldBe(0);
    }

    internal static async Task AssertSameAttemptAsync(
        BootStallWorld world, AgentTask before, SessionStatus session = SessionStatus.Running)
    {
        var task = await world.TaskAsync();
        task.Status.ShouldBe(before.Status, world.Warnings());
        task.Attempt.ShouldBe(before.Attempt);
        task.ConcurrencyToken.ShouldBe(before.ConcurrencyToken);
        task.AgentSessionId.ShouldBe(world.SessionId);
        task.AgentId.ShouldBe(before.AgentId);
        task.DispatchedAt.ShouldBe(before.DispatchedAt);
        task.CompletedAt.ShouldBeNull();
        task.FailureCode.ShouldBeNull();
        task.FailureReason.ShouldBeNull();
        (await world.SessionAsync()).Status.ShouldBe(session);
    }

    internal static async Task AssertNoFailureTraceAsync(BootStallWorld world)
    {
        var events = await world.EventsAsync();
        events.ShouldNotContain(e => e.Type == AgentTaskEventType.Failed, world.Warnings());
        events.ShouldNotContain(e => e.Type == AgentTaskEventType.Retried);
        await using var db = world.Read();
        (await db.AgentIncidents.AnyAsync(i => i.SessionId == world.SessionId
            && i.Kind == AgentIncidentKind.ProviderUnresponsive)).ShouldBeFalse("detection writes no incident");
        (await db.ModelAvailabilityHolds.AnyAsync(h => h.ClearedAt == null))
            .ShouldBeFalse("detection holds no alias");
    }
}
