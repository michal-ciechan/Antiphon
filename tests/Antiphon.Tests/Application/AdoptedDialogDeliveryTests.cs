using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Adopted Claude sessions whose guarded observation stays Unknown. A real dialog, a mid-turn
/// transcript, an unreadable screen, uncertain input, and a missing snapshot stay withheld.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class AdoptedDialogDeliveryTests
{
    private const string Modal = "Which option?\n❯ 1. First\n2. Second\nEnter to select · Esc to cancel";
    private static readonly DateTimeOffset AttentionNow = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AdoptedIdleClearScreenDelivers()
    {
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync();
        harness.Adapter.DialogAdopted = true;
        await harness.InsertStoreVisibleTurnAsync("prior", "done");
        await harness.Queue.EnqueueAsync(harness.SessionId, "idle adopted notice", MessageSendMode.WhenIdle, CancellationToken.None);

        harness.Adapter.Inputs.ShouldContain("idle adopted notice");
        await using var db = harness.CreateObserverContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == harness.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryAttempts.ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task AdoptedBlockedScreenStaysWithheldWithoutAnAttempt()
    {
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync();
        harness.Adapter.DialogAdopted = true;
        harness.Adapter.RenderedScreenOverride = Modal;
        await harness.InsertStoreVisibleTurnAsync("prior", "done");
        await harness.Queue.EnqueueAsync(harness.SessionId, "into dialog", MessageSendMode.WhenIdle, CancellationToken.None);

        harness.Adapter.Inputs.ShouldBeEmpty();
        var queue = await harness.Queue.GetQueueAsync(harness.SessionId, CancellationToken.None);
        queue.ModalBlockedReason.ShouldBe("Claude dialog blocks input");
        var held = queue.Messages.ShouldHaveSingleItem();
        held.DeliveryAttempts.ShouldBe(0);
        held.WithholdReason.ShouldBe("Claude dialog blocks input");
    }

    [Test]
    public async Task AdoptedMidTurnIsNotTypedAndCountsOneAttempt()
    {
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync();
        harness.Adapter.DialogAdopted = true;
        await harness.InsertStoreVisibleTurnAsync("prior", "done");
        await harness.MarkWorkingAsync();
        await harness.Queue.EnqueueAsync(harness.SessionId, "mid-turn notice", MessageSendMode.WhenIdle, CancellationToken.None);
        await harness.Queue.OnTurnEndAsync(harness.SessionId, CancellationToken.None);

        harness.Adapter.Inputs.ShouldBeEmpty();
        var queue = await harness.Queue.GetQueueAsync(harness.SessionId, CancellationToken.None);
        queue.ModalBlockedReason.ShouldBe("Claude dialog observation unavailable: adopted session");
        queue.Messages.ShouldHaveSingleItem().DeliveryAttempts.ShouldBe(1);
    }

    [Test]
    public async Task UnknownScreenAndBusyTranscriptStayWithheld()
    {
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync();
        harness.Adapter.DialogAdopted = true;
        harness.Adapter.RenderedScreenOverride = "partial";
        await harness.MarkWorkingAsync();
        await harness.Queue.EnqueueAsync(harness.SessionId, "unreadable notice", MessageSendMode.WhenIdle, CancellationToken.None);
        await harness.Queue.OnTurnEndAsync(harness.SessionId, CancellationToken.None);

        harness.Adapter.Inputs.ShouldBeEmpty();
        var queue = await harness.Queue.GetQueueAsync(harness.SessionId, CancellationToken.None);
        queue.ModalBlockedReason.ShouldBe("Claude dialog screen unknown");
        queue.Messages.ShouldHaveSingleItem().DeliveryAttempts.ShouldBe(1);
    }

    [Test]
    public async Task UncertainInputStaysWithheldWhenTheTranscriptIsIdle()
    {
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync();
        harness.Adapter.DialogAdopted = true;
        harness.Adapter.DialogInputUncertain = true;
        await harness.InsertStoreVisibleTurnAsync("prior", "done");
        await harness.Queue.EnqueueAsync(harness.SessionId, "uncertain notice", MessageSendMode.WhenIdle, CancellationToken.None);

        harness.Adapter.Inputs.ShouldBeEmpty();
        var queue = await harness.Queue.GetQueueAsync(harness.SessionId, CancellationToken.None);
        queue.ModalBlockedReason.ShouldBe("Claude dialog input uncertain");
        queue.Messages.ShouldHaveSingleItem().DeliveryAttempts.ShouldBe(1);
    }

    [Test]
    public async Task MissingSnapshotStaysAPreAttemptWithhold()
    {
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync();
        harness.Adapter.DialogGuardEnabled = false;
        await harness.InsertStoreVisibleTurnAsync("prior", "done");
        await harness.Queue.EnqueueAsync(harness.SessionId, "no snapshot", MessageSendMode.WhenIdle, CancellationToken.None);

        harness.Adapter.Inputs.ShouldBeEmpty();
        var queue = await harness.Queue.GetQueueAsync(harness.SessionId, CancellationToken.None);
        queue.ModalBlockedReason.ShouldBe("Claude dialog snapshot unavailable");
        queue.Messages.ShouldHaveSingleItem().DeliveryAttempts.ShouldBe(0);
    }

    [Test]
    public async Task AcceptedGenerationMismatchIsNamedAndCountedOnce()
    {
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync();
        harness.Adapter.AcceptedStartedAt = harness.Adapter.AcceptedStartedAt!.Value.AddMinutes(-30);
        await harness.InsertStoreVisibleTurnAsync("prior", "done");
        await harness.Queue.EnqueueAsync(harness.SessionId, "stale generation", MessageSendMode.WhenIdle, CancellationToken.None);
        await harness.Queue.OnTurnEndAsync(harness.SessionId, CancellationToken.None);

        harness.Adapter.Inputs.ShouldBeEmpty();
        var queue = await harness.Queue.GetQueueAsync(harness.SessionId, CancellationToken.None);
        queue.ModalBlockedReason.ShouldBe("Claude dialog accepted generation mismatch");
        queue.Messages.ShouldHaveSingleItem().DeliveryAttempts.ShouldBe(1);
    }

    [Test]
    public async Task AgedPendingNotesRaiseOneWarningAndIgnoreYoungOrParkedRows()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await ClaudeDialogFixture.CreateBridgeAsync(
            new BridgeQueueHarness.HarnessOptions { ConnectionString = schema.ConnectionString });
        var aged = AttentionNow.UtcDateTime.AddMinutes(-20);
        await harness.SeedPendingMessageAsync("aged notice", createdAtUtc: aged);
        var youngSession = await AddSessionAsync(harness);
        await harness.SeedPendingMessageAsync("young notice", sessionId: youngSession,
            createdAtUtc: AttentionNow.UtcDateTime.AddMinutes(-5));
        var parkedSession = await AddSessionAsync(harness);
        await harness.SeedPendingMessageAsync("parked notice", sessionId: parkedSession,
            deliveryAttempts: 3, createdAtUtc: aged);

        await using var db = harness.CreateObserverContext();
        var attention = await ReadAttentionAsync(db, pendingNoteAlertAgeSeconds: 900);
        var row = attention.Items.Where(i => i.Kind == AttentionKind.PendingNotesAged && i.SessionId == harness.SessionId)
            .ShouldHaveSingleItem();
        row.Severity.ShouldBe(AlertSeverity.Warning);
        row.Headline.ShouldContain("1");
        attention.Items.ShouldNotContain(i => i.Kind == AttentionKind.PendingNotesAged && i.SessionId == youngSession);
        attention.Items.ShouldNotContain(i => i.Kind == AttentionKind.PendingNotesAged && i.SessionId == parkedSession);

        var disabled = await ReadAttentionAsync(db, pendingNoteAlertAgeSeconds: 0);
        disabled.Items.ShouldNotContain(i => i.Kind == AttentionKind.PendingNotesAged);
    }

    private static async Task<Guid> AddSessionAsync(BridgeQueueHarness harness)
    {
        var id = Guid.NewGuid();
        var now = AttentionNow.UtcDateTime;
        await using var db = harness.CreateObserverContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = "/tmp",
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static Task<AttentionDto> ReadAttentionAsync(AppDbContext db, int pendingNoteAlertAgeSeconds)
    {
        var supervision = new SupervisionSettings
        {
            DeliveryVerification = new DeliveryVerificationSettings
            {
                PendingNoteAlertAgeSeconds = pendingNoteAlertAgeSeconds,
            },
        };
        return new AttentionService(
            db, new AttentionServiceTests.FakeRunnerClient(),
            Options.Create(supervision), Options.Create(new DelegationSettings()),
            new FakeTimeProvider(AttentionNow), NullLogger<AttentionService>.Instance)
            .GetAsync(CancellationToken.None, includeProgressProbe: false);
    }
}
