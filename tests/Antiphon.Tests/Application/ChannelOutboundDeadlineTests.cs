using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0418 V-11: everything that can go wrong with the converter, and the one property that must
/// survive all of it — the user still gets the answer.
///
/// <para>A refusal, a crash, a timeout, a full queue and a converter that never existed all end the
/// same way: the ORIGINAL text and sources are published, annotated honestly, with the reason
/// recorded on the row. Never a silent drop, never a reroute to a different agent, never a bypass
/// flag that pretends the conversion happened.</para>
/// </summary>
[Category("Integration")]
public class ChannelOutboundDeadlineTests
{
    /// <summary>
    /// Every converter-side failure mode, each asserting its own recorded reason. The originals go
    /// out intact in all of them, and the annotation says so rather than claiming a conversion.
    /// </summary>
    [Test]
    [Timeout(240_000)]
    [Arguments("no-converter-configured", "converter-unavailable")]
    [Arguments("task-row-vanished", "missing-task")]
    [Arguments("task-failed", "task-Failed")]
    [Arguments("task-blocked", "task-Blocked")]
    [Arguments("task-canceled", "task-Canceled")]
    [Arguments("output-never-written", "missing-output")]
    public async Task Refusals_and_expiry_preserve_originals_and_owners(string shape, string expectedReason, CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var deliveryId = await AdmitAsync(world, ct);

        switch (shape)
        {
            case "no-converter-configured":
                // The pump runs with no OutboundConversionTaskRunner at all, which is what a server
                // with the feature configured but no reachable converter looks like.
                break;
            case "task-row-vanished":
                await SetStateAsync(world, deliveryId, ChannelOutboundDeliveryState.Converting, Guid.NewGuid(), ct);
                break;
            case "task-failed":
                await SetStateAsync(world, deliveryId, ChannelOutboundDeliveryState.Converting,
                    await SeedConversionTaskAsync(world, AgentTaskStatus.Failed, ct), ct);
                break;
            case "task-blocked":
                await SetStateAsync(world, deliveryId, ChannelOutboundDeliveryState.Converting,
                    await SeedConversionTaskAsync(world, AgentTaskStatus.Blocked, ct), ct);
                break;
            case "task-canceled":
                await SetStateAsync(world, deliveryId, ChannelOutboundDeliveryState.Converting,
                    await SeedConversionTaskAsync(world, AgentTaskStatus.Canceled, ct), ct);
                break;
            case "output-never-written":
                await SetStateAsync(world, deliveryId, ChannelOutboundDeliveryState.Converting,
                    await SeedConversionTaskAsync(world, AgentTaskStatus.Succeeded, ct), ct);
                break;
        }

        await DriveAsync(world, deliveryId, ct);

        var delivery = await world.ReadDeliveryAsync(deliveryId);
        delivery!.State.ShouldBe(ChannelOutboundDeliveryState.Published);
        delivery.ConversionSucceeded.ShouldBeFalse();
        delivery.FailureReason.ShouldBe(expectedReason);

        world.Producer.AcceptedCount.ShouldBe(1);
        var accepted = world.Producer.Accepted[0];
        accepted.Attachments.Select(a => a.Name).ShouldBe(["01-requirements.md"]);
        accepted.ConversationId.ShouldBe("X-conversation");
        accepted.ReplyHandle.ShouldBe("handle-T1");
        accepted.Json.ShouldContain(OutboundConversionManifestValidator.FallbackAnnotation);
        accepted.Json.ShouldContain("Here are the sources."); // the agent's own words survive

        // No reroute: the only converter named anywhere is the one the profile pins.
        await using var fresh = world.NewContext();
        var tasks = await fresh.AgentTasks.AsNoTracking().ToListAsync(ct);
        tasks.ShouldAllBe(t => t.AgentId == null || t.AgentId == world.AgentC);
    }

    /// <summary>
    /// The deadline is measured from admission and belongs to the reply, not to the worker. A
    /// delivery that reaches its deadline while still waiting for a converter publishes the
    /// originals — and a worker that finishes AFTERWARDS changes nothing, because the reply has
    /// already been sent and must not be sent twice.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task An_expired_deadline_publishes_originals_and_a_late_success_is_ignored(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync(s =>
            s.Profiles[ChannelOutboundWorld.ProfileName].TimeoutSeconds = 10);
        var deliveryId = await AdmitAsync(world, ct);
        (await world.ReadDeliveryAsync(deliveryId))!.DeadlineAt
            .ShouldBeLessThan(DateTime.UtcNow.AddSeconds(30));

        world.Clock.Advance(TimeSpan.FromSeconds(11));
        await using (var db = world.NewContext())
            await world.NewService(db).PumpOnceAsync(ct);

        var afterDeadline = await world.ReadDeliveryAsync(deliveryId);
        afterDeadline!.State.ShouldBe(ChannelOutboundDeliveryState.Published);
        afterDeadline.FailureReason.ShouldBe("deadline");
        afterDeadline.ConversionTaskId.ShouldBeNull("nothing was ever launched");
        world.Producer.AcceptedCount.ShouldBe(1);

        // The worker finishes late with a perfectly good result. It is too late to matter.
        await world.WriteWorkerOutputAsync(
            deliveryId, "converted", "Converted (late).",
            ("combined.pdf", "%PDF-1.7\nlate\n"u8.ToArray()));
        for (var i = 0; i < 3; i++)
        {
            world.AdvancePastLease();
            await using var db = world.NewContext();
            await world.NewService(db).PumpOnceAsync(ct);
        }

        world.Producer.AcceptedCount.ShouldBe(1, "the reply was already sent; a late success may not send it again");
        (await world.ReadDeliveryAsync(deliveryId))!.State.ShouldBe(ChannelOutboundDeliveryState.Published);
    }

    /// <summary>
    /// A full queue degrades instead of stalling. With MaxPending 1 the second matching reply is not
    /// admitted at all: it is published immediately with the annotation, no intent row, no task —
    /// and the first one is untouched.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Pending_overflow_falls_back_without_a_task(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync(s =>
            s.Profiles[ChannelOutboundWorld.ProfileName].MaxPending = 1);

        var first = await AdmitAsync(world, ct);
        (await world.DeliveryCountAsync()).ShouldBe(1);
        world.Producer.MethodEntries.ShouldBe(0);

        await using (var db = world.NewContext())
        {
            var overflow = await world.NewService(db).SendAsync(
                world.Request(ChannelOutboundWorld.MarkdownReply(), promptSequence: 7, windowStart: 7, windowEnd: 8), ct);
            overflow.Status.ShouldBe(ChannelOutboundPublishStatus.Published);
            overflow.DeliveryId.ShouldBeNull();
        }

        (await world.DeliveryCountAsync()).ShouldBe(1, "the overflow reply creates no second intent");
        world.Producer.AcceptedCount.ShouldBe(1);
        world.Producer.Accepted[0].Json.ShouldContain(OutboundConversionManifestValidator.FallbackAnnotation);
        (await world.ReadDeliveryAsync(first))!.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
    }

    /// <summary>
    /// Seat accounting, asserted on the guard rather than on a launched process.
    ///
    /// <para>The runner is constructed with a NULL task service on purpose: every refusal below must
    /// be decided before any task is created, so a guard that slipped past its check would fault
    /// here instead of quietly launching a second worker. The one row that is allowed through is
    /// covered by <c>OutboundConversionTaskTests</c>, which uses the real service.</para>
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("busy-seat-same-profile")]
    [Arguments("global-limit-reached")]
    [Arguments("deadline-already-passed")]
    [Arguments("unknown-profile")]
    public async Task A_busy_or_expired_seat_takes_no_new_launch(string shape, CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync(s => s.GlobalConversionLimit = 2);
        var deliveryId = await AdmitAsync(world, ct);

        switch (shape)
        {
            case "busy-seat-same-profile":
                await SeedConvertingAsync(world, ChannelOutboundWorld.ProfileName, ct);
                break;
            case "global-limit-reached":
                // Two other profiles are already converting, which fills the global limit even
                // though this delivery's own profile seat is free.
                await SeedConvertingAsync(world, "other-profile-1", ct);
                await SeedConvertingAsync(world, "other-profile-2", ct);
                break;
            case "deadline-already-passed":
                await using (var db = world.NewContext())
                {
                    await db.ChannelOutboundDeliveries.Where(d => d.Id == deliveryId)
                        .ExecuteUpdateAsync(u => u.SetProperty(
                            d => d.DeadlineAt, DateTime.UtcNow.AddSeconds(-1)), ct);
                }

                break;
            case "unknown-profile":
                await using (var db = world.NewContext())
                {
                    await db.ChannelOutboundDeliveries.Where(d => d.Id == deliveryId)
                        .ExecuteUpdateAsync(u => u.SetProperty(
                            d => d.ProfileName, "a-profile-that-is-not-configured"), ct);
                }

                break;
        }

        await using var runnerDb = world.NewContext();
        var runner = new OutboundConversionTaskRunner(
            runnerDb,
            tasks: null!,
            world.NewPolicy(runnerDb),
            world.Store,
            Options.Create(world.Settings),
            world.Clock,
            NullLogger<OutboundConversionTaskRunner>.Instance);

        var delivery = await runnerDb.ChannelOutboundDeliveries.FirstAsync(d => d.Id == deliveryId, ct);
        (await runner.TryDispatchAsync(delivery, ct)).ShouldBeNull();

        var after = await world.ReadDeliveryAsync(deliveryId);
        after!.ConversionTaskId.ShouldBeNull();
        after.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
        await using var fresh = world.NewContext();
        (await fresh.AgentTasks.CountAsync(ct)).ShouldBe(0);
    }

    /// <summary>
    /// A text-only reply under EveryAgentReply that fails conversion keeps its text and claims
    /// nothing it does not have: no attachments are invented to stand in for sources that never
    /// existed.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Text_only_failure_retains_text_without_claiming_sources(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync(s =>
        {
            s.Profiles[ChannelOutboundWorld.ProfileName].Trigger = ChannelOutboundTrigger.EveryAgentReply;
            s.Profiles[ChannelOutboundWorld.ProfileName].TimeoutSeconds = 10;
        });

        ChannelOutboundPublishResult admitted;
        await using (var db = world.NewContext())
        {
            admitted = await world.NewService(db).SendAsync(world.Request(new Antiphon.Messaging.ChannelReply
            {
                Channel = "slack",
                ConversationId = "X-conversation",
                ReplyHandle = "handle-T1",
                Text = "The migration finished; nothing to attach.",
            }), ct);
        }

        admitted.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
        world.Clock.Advance(TimeSpan.FromSeconds(11));
        await using (var db = world.NewContext())
            await world.NewService(db).PumpOnceAsync(ct);

        var delivery = await world.ReadDeliveryAsync(admitted.DeliveryId!.Value);
        delivery!.State.ShouldBe(ChannelOutboundDeliveryState.Published);
        delivery.SourceComplete.ShouldBeFalse();
        world.Producer.AcceptedCount.ShouldBe(1);
        var accepted = world.Producer.Accepted[0];
        accepted.Attachments.ShouldBeEmpty();
        accepted.Json.ShouldContain("The migration finished; nothing to attach.");
        accepted.Json.ShouldContain(OutboundConversionManifestValidator.FallbackAnnotation);
    }

    // ---- fixture helpers -----------------------------------------------------------------------

    private static async Task<Guid> AdmitAsync(ChannelOutboundWorld world, CancellationToken ct)
    {
        await using var db = world.NewContext();
        var result = await world.NewService(db).SendAsync(
            world.Request(ChannelOutboundWorld.MarkdownReply()), ct);
        result.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
        return result.DeliveryId!.Value;
    }

    private static async Task SetStateAsync(
        ChannelOutboundWorld world, Guid deliveryId, ChannelOutboundDeliveryState state, Guid? taskId, CancellationToken ct)
    {
        await using var db = world.NewContext();
        await db.ChannelOutboundDeliveries.Where(d => d.Id == deliveryId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(d => d.State, state)
                .SetProperty(d => d.ConversionTaskId, taskId), ct);
    }

    private static async Task<Guid> SeedConversionTaskAsync(
        ChannelOutboundWorld world, AgentTaskStatus status, CancellationToken ct)
    {
        var taskId = Guid.NewGuid();
        await using var db = world.NewContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "Outbound conversion",
            Goal = "Convert.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = world.ConverterWorkspace,
            RepoPath = world.ConverterWorkspace,
            AgentId = world.AgentC,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = status == AgentTaskStatus.Succeeded ? DateTime.UtcNow : null,
        });
        await db.SaveChangesAsync(ct);
        return taskId;
    }

    /// <summary>An occupied conversion seat: a Converting delivery with a task attached.</summary>
    private static async Task SeedConvertingAsync(ChannelOutboundWorld world, string profileName, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var db = world.NewContext();
        db.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
        {
            Id = Guid.NewGuid(),
            SourceKey = "seat|" + profileName + "|" + Guid.NewGuid().ToString("N"),
            SendKind = ChannelOutboundSendKind.Main,
            ChannelProvider = "slack",
            ConversationId = "seat-conversation",
            ProfileName = profileName,
            State = ChannelOutboundDeliveryState.Converting,
            ConversionTaskId = Guid.NewGuid(),
            InputHash = "seat",
            CreatedAt = now,
            UpdatedAt = now,
            DeadlineAt = now.AddMinutes(5),
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task DriveAsync(ChannelOutboundWorld world, Guid deliveryId, CancellationToken ct)
    {
        for (var i = 0; i < 8; i++)
        {
            await using (var db = world.NewContext())
                await world.NewService(db).PumpOnceAsync(ct);
            world.AdvancePastLease();
            var delivery = await world.ReadDeliveryAsync(deliveryId);
            if (delivery?.State is ChannelOutboundDeliveryState.Published
                or ChannelOutboundDeliveryState.Failed
                or ChannelOutboundDeliveryState.Held)
            {
                return;
            }
        }

        throw new System.TimeoutException($"delivery {deliveryId} never reached a terminal state");
    }
}
