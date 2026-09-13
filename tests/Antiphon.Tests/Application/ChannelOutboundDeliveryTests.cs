using System.Text;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0418 V-6 / V-7 / V-8 / V-14 / V-16 / V-17 / V-23: the admission and publication half of
/// outbound conversion.
///
/// <para>The load-bearing distinction throughout is between a row that exists and a message that
/// was sent. <c>Deferred</c> means the server owes a reply and has durably recorded that it owes it;
/// only a producer that accepted the bytes may stamp anything as published. Every test here reads
/// its verdict back through a FRESH connection, because a conclusion drawn from the same tracked
/// entity the service just wrote proves nothing about what survived the transaction.</para>
/// </summary>
[Category("Integration")]
public class ChannelOutboundDeliveryTests
{
    // ---- V-6: trigger decision across every send shape ---------------------------------------

    /// <summary>
    /// Main, trailing and machine sends go through ONE policy. A shape that matches yields exactly
    /// one intent whichever dispatcher path produced it; a shape that does not match is published
    /// unchanged with no intent at all.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments(ChannelOutboundSendKind.Main)]
    [Arguments(ChannelOutboundSendKind.Trailing)]
    [Arguments(ChannelOutboundSendKind.Machine)]
    public async Task Main_trailing_and_machine_use_the_same_policy(ChannelOutboundSendKind kind, CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();

        var matching = ChannelOutboundWorld.MarkdownReply(markdownNames: "01-requirements.md");
        ChannelOutboundPublishResult result;
        await using (var db = world.NewContext())
            result = await world.NewService(db).SendAsync(world.Request(matching, sendKind: kind), ct);

        result.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
        result.DeliveryId.ShouldNotBeNull();
        world.Producer.MethodEntries.ShouldBe(0);
        (await world.DeliveryCountAsync()).ShouldBe(1);
        (await world.ReadDeliveryAsync(result.DeliveryId!.Value))!.SendKind.ShouldBe(kind);
    }

    /// <summary>
    /// MarkdownSources means what it says. A Markdown attachment, a settlement source zip and a
    /// bundle whose source-manifest.json exists all trigger; an arbitrary zip, a plain text reply
    /// and an unrelated binary do not — not even when the reply carries a source task id, because a
    /// task id is provenance, not content.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("markdown-attachment", true)]
    [Arguments("sources-zip", true)]
    [Arguments("source-manifest-on-task", true)]
    [Arguments("arbitrary-zip", false)]
    [Arguments("arbitrary-zip-with-source-task-id", false)]
    [Arguments("text-only", false)]
    [Arguments("binary-only", false)]
    public async Task MarkdownSources_triggers_on_content_not_on_provenance(string shape, bool expectConversion, CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var sourceTaskIds = new List<Guid>();
        var reply = shape switch
        {
            "markdown-attachment" => ChannelOutboundWorld.MarkdownReply(markdownNames: "01-requirements.md"),
            "sources-zip" => WithAttachment(BareReply(), "deliverable-sources.zip", "application/zip", [0x50, 0x4b, 3, 4]),
            "source-manifest-on-task" => BareReply(),
            "arbitrary-zip" => WithAttachment(BareReply(), "logs.zip", "application/zip", [0x50, 0x4b, 3, 4]),
            "arbitrary-zip-with-source-task-id" => WithAttachment(BareReply(), "logs.zip", "application/zip", [0x50, 0x4b, 3, 4]),
            "text-only" => BareReply() with { Text = "Done. No files." },
            "binary-only" => WithAttachment(BareReply(), "capture.png", "image/png", [0x89, 0x50]),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        if (shape is "source-manifest-on-task")
            sourceTaskIds.Add(await SeedTaskWithSourceManifestAsync(world, withManifest: true, ct));
        if (shape is "arbitrary-zip-with-source-task-id")
            sourceTaskIds.Add(await SeedTaskWithSourceManifestAsync(world, withManifest: false, ct));

        ChannelOutboundPublishResult result;
        await using (var db = world.NewContext())
        {
            result = await world.NewService(db).SendAsync(
                world.Request(reply, sourceTaskIds: sourceTaskIds), ct);
        }

        if (expectConversion)
        {
            result.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
            (await world.DeliveryCountAsync()).ShouldBe(1);
            world.Producer.MethodEntries.ShouldBe(0);
        }
        else
        {
            result.Status.ShouldBe(ChannelOutboundPublishStatus.Published);
            (await world.DeliveryCountAsync()).ShouldBe(0);
            world.Producer.AcceptedCount.ShouldBe(1);
            // A non-matching reply is passed through byte-identically, not annotated.
            world.Producer.Accepted[0].Json
                .ShouldNotContain(OutboundConversionManifestValidator.FallbackAnnotation);
        }
    }

    /// <summary>
    /// EveryAgentReply converts a Markdown-free text answer; MarkdownSources on the same reply does
    /// not. The policy is the only difference between the two runs.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments(ChannelOutboundTrigger.EveryAgentReply, true)]
    [Arguments(ChannelOutboundTrigger.MarkdownSources, false)]
    public async Task Trigger_policy_decides_a_text_only_reply(ChannelOutboundTrigger trigger, bool expectConversion, CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync(s =>
            s.Profiles[ChannelOutboundWorld.ProfileName].Trigger = trigger);

        ChannelOutboundPublishResult result;
        await using (var db = world.NewContext())
        {
            result = await world.NewService(db).SendAsync(
                world.Request(BareReply() with { Text = "A plain prose answer." }), ct);
        }

        result.Status.ShouldBe(expectConversion
            ? ChannelOutboundPublishStatus.Deferred
            : ChannelOutboundPublishStatus.Published);
        (await world.DeliveryCountAsync()).ShouldBe(expectConversion ? 1 : 0);
    }

    // ---- V-7: gates precede admission; controls bypass ----------------------------------------

    /// <summary>
    /// Control traffic — the server's own proactive notes, digests, incident and alert messages —
    /// keeps its existing behaviour exactly: it sends, it stamps, and it creates no worker. Even
    /// with Markdown attachments, even under X's EveryAgentReply policy, which is the configuration
    /// where a careless implementation would convert the server's own alerts.
    ///
    /// <para>The healthy agent reply beside it proves the route is armed, so the zero above is a
    /// bypass rather than a dead switch.</para>
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Gates_precede_admission_and_controls_bypass(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync(s =>
            s.Profiles[ChannelOutboundWorld.ProfileName].Trigger = ChannelOutboundTrigger.EveryAgentReply);

        var control = ChannelOutboundWorld.MarkdownReply(text: "Incident: runner unreachable.");
        ChannelOutboundPublishResult controlResult;
        await using (var db = world.NewContext())
        {
            controlResult = await world.NewService(db).SendAsync(
                world.Request(control, origin: ChannelOutboundOrigin.Control,
                    sendKind: ChannelOutboundSendKind.Control), ct);
        }

        controlResult.Status.ShouldBe(ChannelOutboundPublishStatus.Published);
        (await world.DeliveryCountAsync()).ShouldBe(0);
        world.Producer.AcceptedCount.ShouldBe(1);
        world.Producer.Accepted[0].Json.ShouldContain("Incident: runner unreachable.");

        // Same channel, same policy, ordinary agent turn: this one IS admitted.
        await using (var db = world.NewContext())
        {
            var healthy = await world.NewService(db).SendAsync(
                world.Request(ChannelOutboundWorld.MarkdownReply(), promptSequence: 2, windowStart: 3, windowEnd: 4), ct);
            healthy.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
        }

        (await world.DeliveryCountAsync()).ShouldBe(1);
        world.Producer.AcceptedCount.ShouldBe(1); // still just the control message
    }

    /// <summary>
    /// A channel with no profile and a channel in another project both pass straight through with
    /// their text, kind, raw overrides and attachments intact, and create no work anywhere.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Unconfigured_conversations_preserve_the_reply_exactly(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var raw = JsonDocument.Parse("{\"disable_notification\":true}").RootElement.Clone();

        foreach (var (channelId, conversation) in new[]
                 {
                     (world.ChannelY, "Y-conversation"),
                     (world.ChannelZ, "Z-conversation"),
                 })
        {
            var reply = ChannelOutboundWorld.MarkdownReply(conversationId: conversation) with
            {
                Kind = ChannelReplyKind.Progress,
                RawOverrides = raw,
                Text = "[[attach:already-resolved.md]] Marker text preserved.",
            };
            await using var db = world.NewContext();
            var result = await world.NewService(db).SendAsync(world.Request(reply, channelId), ct);
            result.Status.ShouldBe(ChannelOutboundPublishStatus.Published);
        }

        (await world.DeliveryCountAsync()).ShouldBe(0);
        world.Producer.AcceptedCount.ShouldBe(2);
        foreach (var accepted in world.Producer.Accepted)
        {
            accepted.Kind.ShouldBe(nameof(ChannelReplyKind.Progress));
            accepted.Json.ShouldContain("disable_notification");
            accepted.Json.ShouldContain("[[attach:already-resolved.md]]");
            accepted.Attachments.Count.ShouldBe(1);
        }
    }

    // ---- V-8: durable admission that does not block the runtime -------------------------------

    /// <summary>
    /// Deferred is a claim about the DATABASE. This test pauses the service immediately before its
    /// commit and proves no caller can see Deferred yet, then lets the commit land and reads the
    /// intent, its input hash and its correlation FK from a connection that has never seen the
    /// service's tracked entities. Zero producer entries throughout: nothing was sent.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Deferred_is_durable_and_releases_runtime(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var correlationId = await SeedCorrelationAsync(world, ct);

        var atCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var db = world.NewContext();
        var service = world.NewService(db);
        service.TestPauseBeforeCommit = true;
        var barrierHits = 0;
        service.TestBarrier = async (name, _, token) =>
        {
            if (name != "IntentCommit" || Interlocked.Increment(ref barrierHits) != 1)
                return;
            atCommit.SetResult();
            await released.Task.WaitAsync(token);
        };

        var send = service.SendAsync(world.Request(
            ChannelOutboundWorld.MarkdownReply(), correlationIds: [correlationId]), ct);
        await atCommit.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);

        // Nothing is durable yet, so nothing may claim to be deferred yet.
        (await world.DeliveryCountAsync()).ShouldBe(0);
        send.IsCompleted.ShouldBeFalse();
        await using (var other = world.NewContext())
        {
            (await other.SessionQueuedMessages.AsNoTracking()
                .FirstAsync(m => m.Id == correlationId, ct)).OutboundDeliveryId.ShouldBeNull();
        }

        released.SetResult();
        var result = await send.WaitAsync(TimeSpan.FromSeconds(10), ct);
        result.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);

        var delivery = await world.ReadDeliveryAsync(result.DeliveryId!.Value);
        delivery.ShouldNotBeNull();
        delivery.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
        delivery.InputHash.ShouldNotBeNullOrWhiteSpace();
        delivery.FrozenReplyJson.ShouldNotBeNull();
        delivery.PublishedAt.ShouldBeNull();
        delivery.ProfileName.ShouldBe(ChannelOutboundWorld.ProfileName);
        delivery.ProjectId.ShouldBe(world.ProjectP);

        await using (var fresh = world.NewContext())
        {
            var row = await fresh.SessionQueuedMessages.AsNoTracking()
                .FirstAsync(m => m.Id == correlationId, ct);
            row.OutboundDeliveryId.ShouldBe(delivery.Id);
            row.ChannelReplySettledAt.ShouldBeNull();
        }

        world.Producer.MethodEntries.ShouldBe(0);

        // The staged input is on disk under the server-owned store, not in a task worktree.
        File.Exists(Path.Combine(world.Store.InputDirectory(delivery.Id), "reply.json")).ShouldBeTrue();
        (await File.ReadAllTextAsync(
            Path.Combine(world.Store.InputDirectory(delivery.Id), "hash.txt"), ct)).Trim()
            .ShouldBe(delivery.InputHash);
    }

    // ---- V-14: the destination is frozen at admission -----------------------------------------

    /// <summary>
    /// A later inbound message moves the channel's reply handle to a new thread. The already-admitted
    /// reply must still land in the thread it was answering — its handle, conversation and kind are
    /// frozen at admission and survive a restart of the service.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Later_prompt_thread_and_control_cannot_retarget_pending_reply(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        ChannelOutboundPublishResult admitted;
        await using (var db = world.NewContext())
        {
            admitted = await world.NewService(db).SendAsync(
                world.Request(ChannelOutboundWorld.MarkdownReply(replyHandle: "handle-T1")), ct);
        }

        admitted.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
        var deliveryId = admitted.DeliveryId!.Value;

        // T2 arrives: the channel's latest handle changes underneath the pending reply.
        await using (var db = world.NewContext())
        {
            await db.ChatChannels.Where(c => c.Id == world.ChannelX)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.ReplyHandle, "handle-T2"), ct);
        }

        // A control notice to the same conversation sends immediately while A is still pending.
        await using (var db = world.NewContext())
        {
            var control = await world.NewService(db).SendAsync(
                world.Request(BareReply() with { Text = "Digest." },
                    origin: ChannelOutboundOrigin.Control,
                    sendKind: ChannelOutboundSendKind.Control), ct);
            control.Status.ShouldBe(ChannelOutboundPublishStatus.Published);
        }

        // A fresh process finishes A.
        await DriveToPublishedAsync(world, deliveryId, ct);

        var forA = world.Producer.Accepted.Single(a => a.Attachments.Any(x => x.Name == "01-requirements.md"));
        forA.ReplyHandle.ShouldBe("handle-T1");
        forA.ConversationId.ShouldBe("X-conversation");
        forA.Kind.ShouldBe(nameof(ChannelReplyKind.Answer));

        var delivery = await world.ReadDeliveryAsync(deliveryId);
        delivery!.ReplyHandle.ShouldBe("handle-T1");
        delivery.State.ShouldBe(ChannelOutboundDeliveryState.Published);
    }

    // ---- V-16: only acceptance stamps ---------------------------------------------------------

    /// <summary>
    /// The stamp follows the broker, never the attempt.
    ///
    /// <list type="bullet">
    /// <item>Blocked inside the producer before acceptance: no Published state, no PublishedAt, no
    /// settled correlation — even though the state row already says Publishing.</item>
    /// <item>Accepted and then faulted: PublishUncertain, never Published, never replayed.</item>
    /// </list>
    ///
    /// <para>The bounded-retry half of V-16 is covered separately by
    /// <see cref="Nonacceptance_holds_uncertain_and_is_never_retried_automatically"/>, which records
    /// what this implementation actually does — see that test for the gap.</para>
    /// </summary>
    [Test]
    [Timeout(240_000)]
    public async Task Only_acceptance_stamps_complete_actual_payload(CancellationToken ct)
    {
        // (a) blocked before acceptance
        await using (var world = await ChannelOutboundWorld.CreateAsync())
        {
            var correlationId = await SeedCorrelationAsync(world, ct);
            var deliveryId = await AdmitAndSealAsync(world, ct, correlationId);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            world.Producer.BlockBeforeAcceptance = gate;

            await using var db = world.NewContext();
            var pump = world.NewService(db).PumpOnceAsync(ct);
            await WaitForAsync(() => world.Producer.MethodEntries == 1, TimeSpan.FromSeconds(10));

            var midflight = await world.ReadDeliveryAsync(deliveryId);
            midflight!.State.ShouldBe(ChannelOutboundDeliveryState.Publishing);
            midflight.PublishedAt.ShouldBeNull();
            world.Producer.AcceptedCount.ShouldBe(0);
            await using (var fresh = world.NewContext())
            {
                (await fresh.SessionQueuedMessages.AsNoTracking()
                    .FirstAsync(m => m.Id == correlationId, ct)).ChannelReplySettledAt.ShouldBeNull();
            }

            gate.SetResult();
            await pump.WaitAsync(TimeSpan.FromSeconds(15), ct);
            (await world.ReadDeliveryAsync(deliveryId))!.State.ShouldBe(ChannelOutboundDeliveryState.Published);
        }

        // (d) accepted, then the call faulted: uncertainty, never success
        await using (var world = await ChannelOutboundWorld.CreateAsync())
        {
            var deliveryId = await AdmitAndSealAsync(world, ct, correlationId: null);
            world.Producer.ThrowAfterAcceptance = true;

            await using (var db = world.NewContext())
                await world.NewService(db).PumpOnceAsync(ct);

            var delivery = await world.ReadDeliveryAsync(deliveryId);
            delivery!.State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
            delivery.PublishedAt.ShouldBeNull();
            world.Producer.AcceptedCount.ShouldBe(1); // the broker really did take it
        }
    }

    /// <summary>
    /// V-16 bounded retry: FINDING, not a pass.
    ///
    /// <para>The TestDesign asks for two demonstrable nonacceptances followed by success across
    /// three bounded attempts, then Failed at the cap. This implementation does neither. Every
    /// producer exception — a broker that demonstrably refused the bytes and one that took them and
    /// then faulted alike — lands in <c>PublishUncertain</c>, and <c>PumpOnceAsync</c> selects only
    /// Pending, Converting and Ready. So the row is never picked up again: it is never retried, and
    /// it never reaches Failed either, which makes the <c>MaxPublishAttempts</c> comparison in
    /// <c>PublishReadyAsync</c> unreachable and the configured cap inert.</para>
    ///
    /// <para>The conservative half of that is deliberate and right — C-6/C-7 want uncertainty over a
    /// replay that could duplicate a user-visible message, and the row does surface through
    /// <c>AttentionService</c> for an operator decision. The missing half is that a DEMONSTRABLE
    /// nonacceptance, which is safe to retry, is not distinguished and so is stranded with it.
    /// Closing it needs a producer-side contract for "the broker did not take this", which is a
    /// Code-stage design decision, not something a verification pass should invent.</para>
    ///
    /// <para>This test therefore asserts the CURRENT behaviour exactly, including the invariants
    /// that still hold: no false stamp, no duplicate send, and the sealed payload untouched.</para>
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Nonacceptance_holds_uncertain_and_is_never_retried_automatically(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var correlationId = await SeedCorrelationAsync(world, ct);
        var deliveryId = await AdmitAndSealAsync(world, ct, correlationId);
        var sealedHash = (await world.ReadDeliveryAsync(deliveryId))!.SealedPayloadHash;
        world.Producer.FailDefinitelyAlways = true;

        for (var i = 0; i < 4; i++)
        {
            await using var db = world.NewContext();
            await world.NewService(db).PumpOnceAsync(ct);
            world.AdvancePastLease();
        }

        var delivery = await world.ReadDeliveryAsync(deliveryId);
        delivery!.State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
        delivery.PublishedAt.ShouldBeNull();
        delivery.FailureReason.ShouldNotBeNullOrWhiteSpace();
        delivery.SealedPayloadHash.ShouldBe(sealedHash, "a refused send must not disturb the sealed bytes");

        // Exactly one attempt, ever: four pump ticks produced one producer entry and zero
        // acceptances. Whatever else is missing, the pump does not duplicate a user-visible message.
        delivery.PublishAttempts.ShouldBe(1);
        world.Producer.MethodEntries.ShouldBe(1);
        world.Producer.AcceptedCount.ShouldBe(0);

        // GAP: V-16 expects PublishAttempts to reach MaxPublishAttempts and the state to become
        // Failed. It cannot, because PumpOnceAsync never selects PublishUncertain.
        delivery.PublishAttempts.ShouldBeLessThan(world.Settings.MaxPublishAttempts);
        delivery.State.ShouldNotBe(ChannelOutboundDeliveryState.Failed);

        await using var fresh = world.NewContext();
        (await fresh.SessionQueuedMessages.AsNoTracking()
            .FirstAsync(m => m.Id == correlationId, ct)).ChannelReplySettledAt.ShouldBeNull();
    }

    /// <summary>
    /// Complete source delivery is its own fact. A payload carrying only one of four Markdown
    /// members must not mark the source task delivered as complete; all four must.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments(1, false)]
    [Arguments(4, true)]
    public async Task Partial_source_sets_are_never_recorded_as_complete(int included, bool expectComplete, CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var bundleDir = Path.Combine(world.Root, "bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundleDir);
        var names = new[] { "01-requirements.md", "02-design.md", "03-notes.md", "04-external-api.md" };
        foreach (var name in names)
            await File.WriteAllTextAsync(Path.Combine(bundleDir, name), "# " + name + "\n", ct);
        var members = new List<SourceBundleMember>();
        foreach (var name in names)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(bundleDir, name), ct);
            members.Add(new SourceBundleMember
            {
                RelativePath = "docs/" + name,
                StoredName = name,
                Kind = SourceBundleMemberKinds.Markdown,
                Length = bytes.LongLength,
                Sha256 = SourceBundleManifest.Sha256Hex(bytes),
            });
        }

        await SourceBundleManifest.WriteAsync(
            Path.Combine(bundleDir, SourceBundleManifest.FileName),
            new SourceBundleManifest { Members = members },
            ct);

        var taskId = await SeedTaskAsync(world, bundleDir, ct);
        var correlationId = await SeedCorrelationAsync(world, ct, taskId);
        var reply = ChannelOutboundWorld.MarkdownReply(markdownNames: names.Take(included).ToArray());

        ChannelOutboundPublishResult admitted;
        await using (var db = world.NewContext())
        {
            admitted = await world.NewService(db).SendAsync(
                world.Request(reply, correlationIds: [correlationId], sourceTaskIds: [taskId]), ct);
        }

        var deliveryId = admitted.DeliveryId!.Value;
        await DriveToPublishedAsync(world, deliveryId, ct);

        var delivery = await world.ReadDeliveryAsync(deliveryId);
        delivery!.State.ShouldBe(ChannelOutboundDeliveryState.Published);
        delivery.SourceComplete.ShouldBe(expectComplete);
    }

    // ---- V-17: a slow conversion is not an inbound loss ---------------------------------------

    /// <summary>
    /// The reply-loss sweep must not see an owed-but-converting correlation as a lost reply. The
    /// dispatcher's own pending query excludes rows that already carry an outbound delivery id;
    /// this proves the exclusion is real against the database, and that the row is neither settled
    /// early nor duplicated.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Pending_conversion_is_not_an_inbound_lost_reply(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var correlationId = await SeedCorrelationAsync(world, ct);

        ChannelOutboundPublishResult admitted;
        await using (var db = world.NewContext())
        {
            admitted = await world.NewService(db).SendAsync(
                world.Request(ChannelOutboundWorld.MarkdownReply(), correlationIds: [correlationId]), ct);
        }

        admitted.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);

        await using (var fresh = world.NewContext())
        {
            // The exact shape of the dispatcher's unsettled-correlation query.
            var stillOwed = await fresh.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.Origin == QueuedMessageOrigin.Channel
                    && m.Status == QueuedMessageStatus.Sent
                    && m.ConversationKey != null
                    && m.ChannelReplySettledAt == null
                    && m.OutboundDeliveryId == null)
                .CountAsync(ct);
            stillOwed.ShouldBe(0, "a correlation owned by a live conversion is not an unanswered one");
        }

        // Re-sending the same source window is idempotent: the same intent, never a second.
        await using (var db = world.NewContext())
        {
            var again = await world.NewService(db).SendAsync(
                world.Request(ChannelOutboundWorld.MarkdownReply(), correlationIds: [correlationId]), ct);
            again.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
            again.DeliveryId.ShouldBe(admitted.DeliveryId);
        }

        (await world.DeliveryCountAsync()).ShouldBe(1);
        world.Producer.MethodEntries.ShouldBe(0);

        await DriveToPublishedAsync(world, admitted.DeliveryId!.Value, ct);
        await using (var fresh = world.NewContext())
        {
            var row = await fresh.SessionQueuedMessages.AsNoTracking().FirstAsync(m => m.Id == correlationId, ct);
            row.ChannelReplySettledAt.ShouldNotBeNull();
        }

        world.Producer.AcceptedCount.ShouldBe(1);
    }

    // ---- V-23: the bus boundary is measured on the actual payload ------------------------------

    /// <summary>
    /// The cap is checked against the ACTUAL serialization — base64 expansion, escaped Unicode and
    /// metadata included — not against a raw byte estimate. A configured cap set just above and just
    /// below the real serialized size decides the two rows, which a size estimate could not do,
    /// since base64 inflates the attachment by a third before it reaches the wire.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Serialized_payload_budget_includes_all_fields(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var reply = BareReply() with
        {
            Text = "Zażółć gęślą jaźń 🙂 — every one of these escapes on the wire.",
            Attachments =
            [
                new OutboundAttachment
                {
                    Kind = AttachmentKind.File,
                    Name = "payload.bin",
                    Mime = "application/octet-stream",
                    Content = Enumerable.Range(0, 30_000).Select(i => (byte)(i % 251)).ToArray(),
                },
            ],
        };

        var serialized = Encoding.UTF8.GetByteCount(
            JsonSerializer.Serialize(reply, Antiphon.Messaging.MessagingJson.Options));
        serialized.ShouldBeGreaterThan(40_000, "base64 inflates 30 000 raw bytes well past the raw estimate");

        // Exactly at the cap: accepted.
        await using (var db = world.NewContext())
        {
            var exact = world.NewService(db, messaging: new AntiphonMessagingOptions { MaxMessageBytes = serialized });
            var result = await exact.SendAsync(
                world.Request(reply, world.ChannelY), ct);
            result.Status.ShouldBe(ChannelOutboundPublishStatus.Published);
        }

        // One byte under: refused, and nothing reaches the producer.
        var before = world.Producer.MethodEntries;
        await using (var db = world.NewContext())
        {
            var tight = world.NewService(db, messaging: new AntiphonMessagingOptions { MaxMessageBytes = serialized - 1 });
            var result = await tight.SendAsync(world.Request(reply, world.ChannelY), ct);
            result.Status.ShouldBe(ChannelOutboundPublishStatus.Failed);
            result.Failure.ShouldBe("serialized-cap");
        }

        world.Producer.MethodEntries.ShouldBe(before);

        // The raw attachment cap is a separate, earlier gate with its own reason.
        await using (var db = world.NewContext())
        {
            var rawCapped = world.NewService(db, bridge: new ChannelBridgeSettings { Enabled = true, MaxAttachmentBytes = 1024 });
            var result = await rawCapped.SendAsync(world.Request(reply, world.ChannelY), ct);
            result.Status.ShouldBe(ChannelOutboundPublishStatus.Failed);
            result.Failure.ShouldBe("raw-attachment-cap");
        }
    }

    /// <summary>
    /// An over-cap CONVERTED payload falls back to the originals rather than being sent or dropped,
    /// and the fallback is validated too: if even the originals cannot fit, the delivery fails
    /// visibly instead of claiming a send that never happened.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments(false, ChannelOutboundDeliveryState.Published)]
    [Arguments(true, ChannelOutboundDeliveryState.Failed)]
    public async Task Oversized_conversion_output_falls_back_without_a_false_stamp(
        bool originalsAlsoOverCap, ChannelOutboundDeliveryState expected, CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var deliveryId = await AdmitAndReachConvertingAsync(world, ct);
        await world.WriteWorkerOutputAsync(
            deliveryId, "converted", "Converted.",
            ("huge.pdf", Enumerable.Range(0, 120_000).Select(i => (byte)(i % 253)).ToArray()));

        // A cap that the originals fit under but the conversion output does not — unless the row
        // asks for a cap so tight that nothing fits, which is the terminal case.
        var cap = originalsAlsoOverCap ? 64 : 20_000;

        await using (var db = world.NewContext())
        {
            var service = world.NewService(db, messaging: new AntiphonMessagingOptions { MaxMessageBytes = cap });
            await service.PumpOnceAsync(ct);
        }

        var afterConversion = await world.ReadDeliveryAsync(deliveryId);
        afterConversion!.ConversionSucceeded.ShouldBeFalse();

        if (originalsAlsoOverCap)
        {
            afterConversion.State.ShouldBe(ChannelOutboundDeliveryState.Failed);
            afterConversion.FailureReason.ShouldBe("payload-over-cap");
            afterConversion.PublishedAt.ShouldBeNull();
            world.Producer.MethodEntries.ShouldBe(0);
            return;
        }

        afterConversion.State.ShouldBe(ChannelOutboundDeliveryState.Ready);
        afterConversion.FailureReason.ShouldBe("serialized-cap");

        world.AdvancePastLease();
        await using (var db = world.NewContext())
        {
            await world.NewService(db, messaging: new AntiphonMessagingOptions { MaxMessageBytes = cap })
                .PumpOnceAsync(ct);
        }

        (await world.ReadDeliveryAsync(deliveryId))!.State.ShouldBe(expected);
        world.Producer.AcceptedCount.ShouldBe(1);
        var accepted = world.Producer.Accepted[0];
        accepted.Attachments.Select(a => a.Name).ShouldBe(["01-requirements.md"]);
        accepted.Json.ShouldContain(OutboundConversionManifestValidator.FallbackAnnotation);
    }

    // ---- fixture helpers -----------------------------------------------------------------------

    private static ChannelReply BareReply(string conversationId = "X-conversation") => new()
    {
        Channel = "slack",
        ConversationId = conversationId,
        ReplyHandle = "handle-T1",
        Text = "Answer.",
    };

    private static ChannelReply WithAttachment(ChannelReply reply, string name, string mime, byte[] content) =>
        reply with
        {
            Attachments =
            [
                new OutboundAttachment
                {
                    Kind = AttachmentKind.File,
                    Name = name,
                    Mime = mime,
                    Content = content,
                },
            ],
        };

    private static async Task<Guid> SeedTaskAsync(ChannelOutboundWorld world, string? bundleDir, CancellationToken ct)
    {
        var taskId = Guid.NewGuid();
        await using var db = world.NewContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "Docs",
            Goal = "Write the docs.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = world.Root,
            RepoPath = world.Root,
            Status = AgentTaskStatus.Succeeded,
            DeliverableBundleDir = bundleDir,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return taskId;
    }

    private static async Task<Guid> SeedTaskWithSourceManifestAsync(
        ChannelOutboundWorld world, bool withManifest, CancellationToken ct)
    {
        var bundleDir = Path.Combine(world.Root, "bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundleDir);
        if (withManifest)
        {
            await SourceBundleManifest.WriteAsync(
                Path.Combine(bundleDir, SourceBundleManifest.FileName),
                new SourceBundleManifest(),
                ct);
        }

        return await SeedTaskAsync(world, bundleDir, ct);
    }

    private static async Task<Guid> SeedCorrelationAsync(
        ChannelOutboundWorld world, CancellationToken ct, Guid? sourceTaskId = null)
    {
        var sessionId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await using var db = world.NewContext();
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = world.Root,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = messageId,
            AgentSessionId = sessionId,
            Body = "Please write the docs.",
            Status = QueuedMessageStatus.Sent,
            Origin = QueuedMessageOrigin.Channel,
            ConversationKey = "slack:X-conversation",
            SourceTaskId = sourceTaskId,
            Sequence = 1,
            CreatedAt = now,
            SentAt = now,
        });
        await db.SaveChangesAsync(ct);
        return messageId;
    }

    /// <summary>Admits a matching reply and moves it to Converting behind a Succeeded worker row.</summary>
    private static async Task<Guid> AdmitAndReachConvertingAsync(ChannelOutboundWorld world, CancellationToken ct)
    {
        ChannelOutboundPublishResult admitted;
        await using (var db = world.NewContext())
            admitted = await world.NewService(db).SendAsync(world.Request(ChannelOutboundWorld.MarkdownReply()), ct);
        admitted.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
        var deliveryId = admitted.DeliveryId!.Value;
        var taskId = await SeedTaskAsync(world, bundleDir: null, ct);
        await using (var db = world.NewContext())
        {
            await db.ChannelOutboundDeliveries.Where(d => d.Id == deliveryId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(d => d.State, ChannelOutboundDeliveryState.Converting)
                    .SetProperty(d => d.ConversionTaskId, taskId), ct);
        }

        return deliveryId;
    }

    /// <summary>
    /// Admits a matching reply and seals an <c>unchanged</c> conversion result, leaving the delivery
    /// Ready with real sealed bytes — the state every publication test starts from.
    /// </summary>
    private static async Task<Guid> AdmitAndSealAsync(
        ChannelOutboundWorld world, CancellationToken ct, Guid? correlationId)
    {
        ChannelOutboundPublishResult admitted;
        await using (var db = world.NewContext())
        {
            admitted = await world.NewService(db).SendAsync(
                world.Request(ChannelOutboundWorld.MarkdownReply(),
                    correlationIds: correlationId is Guid id ? [id] : []), ct);
        }

        admitted.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
        var deliveryId = admitted.DeliveryId!.Value;
        var taskId = await SeedTaskAsync(world, bundleDir: null, ct);
        await using (var db = world.NewContext())
        {
            await db.ChannelOutboundDeliveries.Where(d => d.Id == deliveryId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(d => d.State, ChannelOutboundDeliveryState.Converting)
                    .SetProperty(d => d.ConversionTaskId, taskId), ct);
        }

        await world.WriteWorkerOutputAsync(deliveryId, "unchanged");
        await using (var db = world.NewContext())
            await world.NewService(db).PumpOnceAsync(ct);
        world.AdvancePastLease();
        (await world.ReadDeliveryAsync(deliveryId))!.State.ShouldBe(ChannelOutboundDeliveryState.Ready);
        return deliveryId;
    }

    private static async Task DriveToPublishedAsync(ChannelOutboundWorld world, Guid deliveryId, CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
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

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(25);
        }

        throw new System.TimeoutException("condition not reached within " + budget);
    }
}
