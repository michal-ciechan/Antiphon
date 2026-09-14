using System.Diagnostics;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0418 V-15 / F-3: what survives a process that DIES, at each boundary where dying could lose
/// or duplicate a user's reply.
///
/// <para>These cuts cannot be simulated in-process. Throwing an exception runs finally blocks,
/// flushes buffers and disposes the DbContext cleanly — all the things a killed process does not do.
/// So each case launches a real child (<c>Antiphon.ChannelOutbound.Probe</c>) against a database,
/// outbound store and producer evidence sink that the PARENT owns and keeps; the child announces the
/// barrier it reached and parks there; the parent kills the PID it recorded, then starts a fresh
/// child against the same data.</para>
///
/// <para>The producer's acceptances are appended and flushed to disk before its call can return, so
/// "did the broker take the bytes?" is answerable even for the child that never returned.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("OutboundRecovery")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ChannelOutboundRecoveryTests
{
    /// <summary>
    /// C-1. Killed while the input is still staged under its temporary name, before the atomic
    /// rename and before any intent is committed. Nothing may be visible, and nothing orphaned may
    /// ever be published: a retry must produce ONE complete snapshot and ONE intent.
    /// </summary>
    [Test]
    [Timeout(300_000)]
    public async Task Process_death_before_input_finalization_leaves_no_intent(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        await using var probe = new ProbeHarness(world);

        var first = await probe.RunAsync(ct, action: "admit", dieAt: "InputStaging");
        first.Killed.ShouldBeTrue("the child must have reached the staging barrier and been killed there");

        (await world.DeliveryCountAsync()).ShouldBe(0, "an uncommitted intent is not an intent");
        probe.AcceptedRecords().ShouldBeEmpty();

        // Retry in a fresh process against the same store.
        var second = await probe.RunAsync(ct, action: "admit");
        second.Killed.ShouldBeFalse();
        second.ExitCode.ShouldBe(0);

        (await world.DeliveryCountAsync()).ShouldBe(1);
        var delivery = await probe.SingleDeliveryAsync();
        delivery.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
        delivery.InputHash.ShouldNotBeNullOrWhiteSpace();

        // The snapshot the survivor owns is complete, and the orphaned temporary directory the
        // killed child left is not part of it.
        var inputDir = world.Store.InputDirectory(delivery.Id);
        File.Exists(Path.Combine(inputDir, "reply.json")).ShouldBeTrue();
        File.Exists(Path.Combine(inputDir, "request.json")).ShouldBeTrue();
        (await File.ReadAllTextAsync(Path.Combine(inputDir, "hash.txt"), ct)).Trim()
            .ShouldBe(delivery.InputHash);
        probe.AcceptedRecords().ShouldBeEmpty();
    }

    /// <summary>
    /// C-2. Killed after the intent and its frozen input are committed, before any conversion task
    /// exists. The recovery must REUSE that intent — its id, its input hash, its frozen routing —
    /// rather than admitting the reply a second time.
    /// </summary>
    [Test]
    [Timeout(300_000)]
    public async Task Process_death_after_intent_commit_reuses_the_same_intent(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        await using var probe = new ProbeHarness(world);

        var first = await probe.RunAsync(ct, action: "admit", dieAt: "IntentCommit");
        first.Killed.ShouldBeTrue();

        var afterCrash = await probe.SingleDeliveryAsync();
        afterCrash.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
        var intentId = afterCrash.Id;
        var inputHash = afterCrash.InputHash;
        var frozen = afterCrash.FrozenReplyJson;
        frozen.ShouldNotBeNull();
        afterCrash.ReplyHandle.ShouldBe("handle-T1");

        var second = await probe.RunAsync(ct, action: "admit");
        second.ExitCode.ShouldBe(0);
        second.StandardOutput.ShouldContain("Deferred");

        (await world.DeliveryCountAsync()).ShouldBe(1, "the reply is owed once, not twice");
        var recovered = await probe.SingleDeliveryAsync();
        recovered.Id.ShouldBe(intentId);
        recovered.InputHash.ShouldBe(inputHash);
        recovered.FrozenReplyJson.ShouldBe(frozen);
        recovered.ReplyHandle.ShouldBe("handle-T1");
        probe.AcceptedRecords().ShouldBeEmpty();
    }

    /// <summary>
    /// C-5. Killed after the conversion output is sealed on disk but before the Ready transition is
    /// committed. A fresh process must finish the job exactly once — one publication, carrying the
    /// sealed bytes.
    /// </summary>
    [Test]
    [Timeout(300_000)]
    public async Task Process_death_before_ready_commit_publishes_exactly_once(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        await using var probe = new ProbeHarness(world);

        (await probe.RunAsync(ct, action: "admit")).ExitCode.ShouldBe(0);
        var deliveryId = (await probe.SingleDeliveryAsync()).Id;
        await probe.MoveToConvertedAsync(deliveryId, ct);

        var crashed = await probe.RunAsync(ct, action: "pump", dieAt: "Ready");
        crashed.Killed.ShouldBeTrue();

        // The sealed payload is on disk; the state transition that would have announced it is not.
        File.Exists(Path.Combine(world.StoreRoot, deliveryId.ToString("D"), "sealed.json")).ShouldBeTrue();
        (await probe.SingleDeliveryAsync()).State.ShouldBe(ChannelOutboundDeliveryState.Converting);
        probe.AcceptedRecords().ShouldBeEmpty();

        // Fresh processes finish it. Two ticks, so a second one cannot publish again.
        world.AdvancePastLease();
        (await probe.RunAsync(ct, action: "pump")).ExitCode.ShouldBe(0);
        (await probe.RunAsync(ct, action: "pump")).ExitCode.ShouldBe(0);
        (await probe.RunAsync(ct, action: "pump")).ExitCode.ShouldBe(0);

        var delivery = await probe.SingleDeliveryAsync();
        delivery.State.ShouldBe(ChannelOutboundDeliveryState.Published);
        var accepted = probe.AcceptedRecords();
        accepted.Count.ShouldBe(1, "a recovered publication happens once");
        accepted[0].GetProperty("conversationId").GetString().ShouldBe("X-conversation");
        accepted[0].GetProperty("replyHandle").GetString().ShouldBe("handle-T1");
    }

    /// <summary>
    /// C-8. Killed after everything is committed. Repeat ticks and restarts must add nothing.
    /// </summary>
    [Test]
    [Timeout(300_000)]
    public async Task Restart_after_a_committed_publication_adds_nothing(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        await using var probe = new ProbeHarness(world);

        (await probe.RunAsync(ct, action: "admit")).ExitCode.ShouldBe(0);
        var deliveryId = (await probe.SingleDeliveryAsync()).Id;
        await probe.MoveToConvertedAsync(deliveryId, ct);

        (await probe.RunAsync(ct, action: "pump")).ExitCode.ShouldBe(0);
        world.AdvancePastLease();
        (await probe.RunAsync(ct, action: "pump")).ExitCode.ShouldBe(0);
        (await probe.SingleDeliveryAsync()).State.ShouldBe(ChannelOutboundDeliveryState.Published);
        probe.AcceptedRecords().Count.ShouldBe(1);

        for (var i = 0; i < 3; i++)
        {
            world.AdvancePastLease();
            (await probe.RunAsync(ct, action: "pump")).ExitCode.ShouldBe(0);
        }

        probe.AcceptedRecords().Count.ShouldBe(1, "a settled delivery is not re-sent by later ticks");
        (await world.DeliveryCountAsync()).ShouldBe(1);
        (await probe.SingleDeliveryAsync()).PublishedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// C-6 and C-7: FINDING, not a pass.
    ///
    /// <para>C-6 kills the process after the Publishing transition is committed but before the first
    /// possible producer call; C-7 kills it after the broker has demonstrably accepted the bytes.
    /// The plan requires both to recover as <c>PublishUncertain</c>, surfaced through attention for
    /// an explicit operator decision, and C-7 to produce no second accepted record.</para>
    ///
    /// <para>What actually happens: the row stays in <c>Publishing</c> for ever.
    /// <c>PumpOnceAsync</c> selects only Pending, Converting and Ready, so no later tick and no
    /// restart ever looks at it again; and <c>AttentionService.BuildOutboundDeliveryItemsAsync</c>
    /// projects only Held, Failed and PublishUncertain, so <c>Publishing</c> appears in no operator
    /// view either. A reply that was being published when the server died is therefore never sent
    /// (C-6) or never confirmed (C-7), and nothing anywhere says so — which is the CARD-0067 failure
    /// class this feature is otherwise careful about.</para>
    ///
    /// <para>The no-duplicate half does hold, but only as a side effect of the row being
    /// unreachable. This test asserts exactly that state, so the day the recovery is implemented it
    /// fails and has to be updated deliberately.</para>
    /// </summary>
    [Test]
    [Timeout(300_000)]
    [Arguments("C-6", "Publishing", "accept", 0)]
    [Arguments("C-7", null, "accept-then-park", 1)]
    public async Task Process_death_during_publication_is_stranded_in_publishing(
        string cut, string? dieAt, string producerMode, int expectedAcceptedRecords, CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        await using var probe = new ProbeHarness(world);

        (await probe.RunAsync(ct, action: "admit")).ExitCode.ShouldBe(0);
        var deliveryId = (await probe.SingleDeliveryAsync()).Id;
        await probe.MoveToConvertedAsync(deliveryId, ct);

        // Seal the payload in a clean process so the crash below is about publication alone.
        (await probe.RunAsync(ct, action: "pump", producerMode: "refuse")).ExitCode.ShouldBe(0);
        world.AdvancePastLease();
        await probe.ResetToReadyAsync(deliveryId, ct);

        var crashed = await probe.RunAsync(ct, action: "pump", dieAt: dieAt, producerMode: producerMode);
        crashed.Killed.ShouldBeTrue(cut + ": the child must have been killed mid-publication");

        var afterCrash = await probe.SingleDeliveryAsync();
        afterCrash.State.ShouldBe(ChannelOutboundDeliveryState.Publishing,
            cut + ": Publishing is persisted before the first possible producer call");
        afterCrash.PublishedAt.ShouldBeNull();
        probe.AcceptedRecords().Count.ShouldBe(expectedAcceptedRecords, cut + ": broker acceptance");

        // Three fresh processes. None of them touches the row.
        for (var i = 0; i < 3; i++)
        {
            world.AdvancePastLease();
            (await probe.RunAsync(ct, action: "pump")).ExitCode.ShouldBe(0);
        }

        var recovered = await probe.SingleDeliveryAsync();
        probe.AcceptedRecords().Count.ShouldBe(expectedAcceptedRecords,
            cut + ": no duplicate send (though only because the row is unreachable)");
        recovered.PublishedAt.ShouldBeNull();

        // GAP: the plan requires PublishUncertain here, and an attention item for an operator.
        recovered.State.ShouldBe(ChannelOutboundDeliveryState.Publishing,
            cut + ": recovery leaves the row stranded in Publishing");
        recovered.State.ShouldNotBe(ChannelOutboundDeliveryState.PublishUncertain);
        recovered.State.ShouldNotBe(ChannelOutboundDeliveryState.Published);
    }
}
