using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Supervision;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ClaudeDialogDeliveryTests
{
    private const string Modal = "Which option?\n❯ 1. First\n2. Second\nEnter to select · Esc to cancel";

    [Test]
    public async Task C680_QueueAndExpectationKeepDialogInputPending()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var adapter = f.Harness.Adapter;
        adapter.ClaudeComposerChrome = true;
        await f.Harness.MarkWorkingAsync();
        await f.Harness.Queue.EnqueueAsync(f.SessionId, "held ordinary message", MessageSendMode.WhenIdle, CancellationToken.None);
        adapter.RenderedScreenOverride = Modal;
        var nudge = await f.NudgeAsync();
        var result = await f.DeliverAsync(nudge.Id);
        result.Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        adapter.Inputs.ShouldBeEmpty("pre-attempt dialog veto writes no body, Enter or recovery key");
        var stored = await f.ReloadAsync(nudge.Id);
        stored.AttemptStartedAt.ShouldBeNull("refusal must precede attempt custody");
        var queue = await f.Harness.Queue.GetQueueAsync(f.SessionId, CancellationToken.None);
        queue.ModalBlocked.ShouldBeTrue();
        queue.Messages.ShouldHaveSingleItem();
        queue.Messages[0].DeliveryAttempts.ShouldBe(0);
        await f.Harness.Queue.SendNowAsync(f.SessionId, queue.Messages[0].Id, CancellationToken.None);
        adapter.Inputs.ShouldBeEmpty();

        adapter.RenderedScreenOverride = null;
        var control = await f.NudgeAsync("A distinct complete clear-screen control prompt for CARD-0680");
        (await f.DeliverAsync(control.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        adapter.SubmittedBodies.ShouldBe([control.Body]);
        (await f.ReloadAsync(control.Id)).ReceiptAt.ShouldNotBeNull();
        await using var db = f.Db();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task C680_AllAutomatedWritersUseTheGuard()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var h = f.Harness;
        h.Adapter.RenderedScreenOverride = Modal;
        await Should.ThrowAsync<ConflictException>(() => h.Runtime.SendInputAsync(f.SessionId, "direct", CancellationToken.None));
        await using var db = f.Db();
        var channel = new AgentChannelService(db, h.Runtime, h.Scope.ServiceProvider.GetRequiredService<CardService>(),
            h.EventBus, NullLogger<AgentChannelService>.Instance);
        await Should.ThrowAsync<ConflictException>(() => channel.SendToSessionAsync(null, f.SessionId, "channel", CancellationToken.None));
        var health = new SessionHealthActions(h.Queue, h.Provider.GetRequiredService<AgentSessionService>(), h.Runner,
            h.Runtime, h.Provider.GetRequiredService<IServiceScopeFactory>());
        await Should.ThrowAsync<ConflictException>(() => health.SendRawInputAsync(f.SessionId, "maintenance", CancellationToken.None));
        var watchdog = new WatchdogService(db, h.Runtime, new WatchdogMatcher(), new WatchdogCooldownStore(), h.EventBus,
            Options.Create(new WatchdogSettings { Enabled = true, Rules = [new() { Name = "fixture", Pattern = "Which option?", Response = "\r" }] }),
            TimeProvider.System, NullLogger<WatchdogService>.Instance);
        (await watchdog.ScanAsync(CancellationToken.None)).ShouldBe(0);
        h.Adapter.Inputs.ShouldBeEmpty();
        h.EventBus.PublishedEvents.ShouldNotContain(e => e.EventName is "WatchdogAutoResponded" or "AgentChannelMessage");
    }
}
