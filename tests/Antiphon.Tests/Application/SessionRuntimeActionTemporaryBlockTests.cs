using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class SessionRuntimeActionTemporaryBlockTests
{
    [Test]
    [Arguments("transport-unavailable")]
    [Arguments("herdr-pending")]
    [Arguments("herdr-send-unreachable")]
    [Arguments("modal")]
    public async Task C1308_temporary_block_retains_action_then_delivers(string variant)
    {
        await using var modal = variant == "modal"
            ? await RemoteControlRecoveryHarness.CreateAsync(isolated: true) : null;
        await using var schema = modal is null ? await TestDbFixture.CreateIsolatedSchemaAsync() : null;
        await using var owned = modal is null ? await ClaudeDialogFixture.CreateBridgeAsync(new()
        {
            ConnectionString = schema!.ConnectionString,
            ConfigureServices = services =>
            {
                if (variant == "transport-unavailable")
                    services.AddSingleton<ISessionRunnerDirectory>(sp =>
                        new TemporaryRunnerDirectory(sp.GetRequiredService<ISessionRunnerClient>()));
            }
        }) : null;
        var h = modal?.Inner ?? owned!;
        await h.InsertTurnAsync("previous complete prompt", "previous complete answer");
        const string body = "C1308 first complete line\nC1308 second complete line";
        var queued = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, default, deliverIfIdle: false);
        var messageId = queued.Messages.Single().Id;
        var actions = h.Provider.GetRequiredService<SessionRuntimeActionCoordinator>();
        await actions.RequestAsync(h.SessionId, "status", default);
        Guid actionId;
        await using (var db = h.CreateObserverContext())
            actionId = (await db.SessionRuntimeActions.SingleAsync()).Id;

        TemporaryRunnerDirectory? directory = null;
        Guid? episodeId = null;
        var rejectedWrites = 0;
        if (variant == "transport-unavailable")
        {
            directory = (TemporaryRunnerDirectory)h.Provider.GetRequiredService<ISessionRunnerDirectory>();
            directory.SessionId = h.SessionId;
            await using var db = h.CreateObserverContext();
            await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u =>
                u.SetProperty(s => s.RunnerId, directory.Owner.RunnerId)
                    .SetProperty(s => s.RunnerStoreId, directory.Owner.RunnerStoreId)
                    .SetProperty(s => s.RunnerCwd, directory.Owner.RunnerCwd));
            directory.Unavailable = true;
            h.Runtime.ListLiveOrUnknownSessions().ShouldContain(h.SessionId);
        }
        if (variant == "herdr-pending") h.Runtime.SetTestPending(h.SessionId, HerdrPendingReasons.Unreachable);
        if (variant == "herdr-send-unreachable")
        {
            h.Adapter.BeforeInput = async (_, _) =>
            {
                // Observe the real charged attempt before rejecting all input. The assertions
                // below then prove the BackendUnreachable path refunded and cleared it.
                await using var db = h.CreateObserverContext();
                var attempt = await db.SessionQueuedMessages.SingleAsync(m => m.Id == messageId);
                attempt.Status.ShouldBe(QueuedMessageStatus.Sent);
                attempt.DeliveryAttempts.ShouldBe(1);
                attempt.LastDeliveryStartedAt.ShouldNotBeNull();
                rejectedWrites++;
                throw new ServiceUnavailableException("fixture Herdr unavailable", HerdrProblemTypes.Unreachable);
            };
        }
        if (modal is not null)
        {
            modal.Adapter.RemoteControlMenuOpen = true;
            var observation = await modal.Recovery.ObserveAsync(h.SessionId, modal.Generation, default);
            observation.Menu.IsPresent.ShouldBeTrue();
            var episode = await modal.Recovery.DetectAsync(h.SessionId, modal.Generation, observation, null, default);
            episode.ShouldNotBeNull();
            episodeId = episode.Id;
        }

        (await actions.DrainPageAsync(h.SessionId, default)).ShouldBeFalse();
        h.Adapter.Inputs.ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        h.Adapter.Killed.ShouldBeFalse();
        Finished(h).ShouldBe(0);
        if (directory is not null) directory.Refusals.ShouldBeGreaterThan(0);
        if (variant == "herdr-send-unreachable") rejectedWrites.ShouldBe(1);
        await using (var db = h.CreateObserverContext())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == messageId);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.DeliveryAttempts.ShouldBe(0);
            row.DeliveryVerdict.ShouldBeNull();
            row.DeliveryVerdictAt.ShouldBeNull();
            row.SentAt.ShouldBeNull();
            row.LastDeliveryStartedAt.ShouldBeNull();
            row.LastDeliveryGeneration.ShouldBeNull();
            row.LastDeliveryBaselineSequence.ShouldBeNull();
            var retained = await db.SessionRuntimeActions.SingleAsync(a => a.Id == actionId);
            retained.Phase.ShouldBe(SessionRuntimeActionCoordinator.QueuePhase);
            retained.FailureReason.ShouldBe("Deferred");
            retained.Attempts.ShouldBe(1);
            (await db.TranscriptEntries.AnyAsync(t => t.Kind == TranscriptKinds.UserPrompt && t.Text == body)).ShouldBeFalse();
        }

        if (directory is not null) directory.Unavailable = false;
        if (variant == "herdr-pending") h.Runtime.SetTestPending(h.SessionId, null);
        if (variant == "herdr-send-unreachable") h.Adapter.BeforeInput = null;
        if (modal is not null)
        {
            var sem = h.Queue.GetLock(h.SessionId);
            await sem.WaitAsync();
            try
            {
                (await modal.Recovery.TryDismissIdleUnderLockAsync(h.SessionId, episodeId!.Value, default))
                    .ShouldBe(RemoteControlDismissalResult.DismissedVerified);
            }
            finally { sem.Release(); }
            h.Adapter.ConditionalInputs.ShouldBe(["\u001b"]);
            (await h.Queue.IsModalBlockedAsync(h.SessionId, default)).ShouldBeFalse();
            await using var db = h.CreateObserverContext();
            var episode = await db.RemoteControlModalEpisodes.SingleAsync(e => e.Id == episodeId);
            episode.ResolvedAt.ShouldNotBeNull();
            episode.DismissalVerifiedAt.ShouldNotBeNull();
        }

        var receipt = new SessionRunnerTranscriptEvent(h.SessionId, 10, TranscriptKinds.UserPrompt,
            Guid.NewGuid().ToString(), null, h.Clock.GetUtcNow(), "user", body, null, null, null, null, null);
        h.Adapter.OnSubmitted = sent =>
        {
            sent.ShouldBe(body);
            return h.Runtime.ObserveTranscriptAsync(receipt, default);
        };
        // Make only the retained debt due; do not admit a replacement wakeup or bypass
        // the coordinator with a direct queue flush.
        await using (var db = h.CreateObserverContext())
            await db.SessionRuntimeActions.Where(a => a.Id == actionId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.DueAt, h.Now));
        await actions.DrainPageAsync(h.SessionId, default);
        await using (var db = h.CreateObserverContext())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == messageId);
            row.Body.ShouldBe(body);
            row.Status.ShouldBe(QueuedMessageStatus.Sent);
            row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            row.DeliveryAttempts.ShouldBe(1);
            (await db.TranscriptEntries.SingleAsync(t => t.Uuid == receipt.Uuid)).Text.ShouldBe(body);
            (await db.SessionRuntimeActions.AnyAsync(a => a.Id == actionId)).ShouldBeFalse();
        }
        var paste = "\u001b[200~" + body + "\u001b[201~";
        h.Adapter.Inputs.ShouldBe(modal is null ? [paste, "\r"] : ["\u001b", paste, "\r"]);
        h.Adapter.SubmittedBodies.ShouldBe([body]);
        await actions.DrainPageAsync(h.SessionId, default);
        h.Adapter.SubmittedBodies.ShouldBe([body], "acknowledged debt cannot send again");
        h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(1);
        h.Adapter.Killed.ShouldBeFalse();
        Finished(h).ShouldBe(0, "a status wakeup never publishes a turn boundary");
    }

    private static int Finished(BridgeQueueHarness h) =>
        h.EventBus.PublishedEvents.Count(e => e.EventName == "SessionFinished");

    private sealed class TemporaryRunnerDirectory(ISessionRunnerClient client) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => client;
        public SessionRunnerOwner Owner { get; } = new("c1308-fixture", Guid.NewGuid(), "/fixture");
        public Guid SessionId { get; set; }
        public bool Unavailable { get; set; }
        public int Refusals { get; private set; }
        public IReadOnlyList<string> KnownRunnerIds => [Owner.RunnerId];
        public ISessionRunnerClient Resolve(string? runnerId)
        {
            if (!Unavailable) return client;
            Refusals++;
            throw new ServiceUnavailableException("fixture transport unavailable", PhoneHomeProblemTypes.Unavailable);
        }
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(Owner);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(new SessionRunnerBinding.Remote(Owner));
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("fixture inventory unknown"));
        public IReadOnlyCollection<Guid> LiveRemoteSessionIds() => [SessionId];
        public Guid? GetLiveStoreId(string? runnerId) => Owner.RunnerStoreId;
    }
}
