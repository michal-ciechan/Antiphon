using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("SessionGenerationDeliveryOverlap")]
public class SessionGenerationDeliveryOverlapTests
{
    [Test]
    public async Task C502_V11_same_generation_delivery_failure_kills_G_and_the_paused_launch_tail_settles_G()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions { AlwaysOn = true });
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            db.TranscriptEntries.Add(new Antiphon.Server.Domain.Entities.TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Sequence = 1,
                Kind = TranscriptKinds.TurnEnd,
                Text = "done",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        h.Adapter.EchoTypedInputToScreen = false;
        await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.EnqueueAsync(h.SessionId, "into the void", MessageSendMode.Now, CancellationToken.None));

        h.Adapter.KillGenerationCalls.Count.ShouldBeGreaterThan(0);
        h.Adapter.KillCount.ShouldBe(0);
        h.Adapter.Killed.ShouldBeTrue();
    }

    [Test]
    public async Task A_late_UserPrompt_confirmation_prevents_the_generation_conditional_recovery_kill()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions { AlwaysOn = true });
        h.Adapter.OnSubmitted = async body =>
        {
            await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);
        };

        await h.Queue.EnqueueAsync(h.SessionId, "late confirm body", MessageSendMode.Now, CancellationToken.None);

        h.Adapter.KillGenerationCalls.ShouldBeEmpty();
        h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(1);
    }

    [Test]
    public async Task C502_V12_B_resumed_before_A_cleanup_gets_zero_kill_calls_and_boots()
    {
        var adapterB = new FakeAgentProtocolAdapter
        {
            ReadyHold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConfigureServices = s => s.AddSingleton<IAgentProtocolAdapterFactory>(new OneAdapterFactory(adapterB)),
        });
        adapterB.RegisterOnStart = h.Runtime;
        h.Adapter.EchoTypedInputToScreen = false;
        DateTime generationA;
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            generationA = (await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId)).StartedAt;
        }

        await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.EnqueueAsync(h.SessionId, "into the void", MessageSendMode.Now, CancellationToken.None));
        h.Adapter.KillGenerationCalls.ShouldBe([generationA]);
        h.Adapter.KillCount.ShouldBe(0);

        await using var scope = h.Provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AgentControlService>()
            .StartAsync(h.AgentId, new StartAgentRequest(), CancellationToken.None);
        await WaitUntilAsync(() => adapterB.Started);
        adapterB.KillGenerationCalls.ShouldBeEmpty();
        adapterB.Killed.ShouldBeFalse();
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            var row = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            row.Status.ShouldBe(SessionStatus.Starting);
            row.FailureReason.ShouldBeNull();
            row.StartedAt.ShouldNotBe(generationA);
        }

        adapterB.ReadyHold.SetResult(true);
        await h.Provider.GetRequiredService<AgentSessionLaunchQueue>()
            .WaitForIdleAsync(TimeSpan.FromSeconds(20), CancellationToken.None);
        await using var verify = BridgeQueueHarness.CreateContext();
        var live = await verify.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        live.Status.ShouldBe(SessionStatus.Running);
        live.InteractiveLaunchCompletedAt.ShouldNotBeNull();
        adapterB.KillGenerationCalls.ShouldBeEmpty();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        condition().ShouldBeTrue();
    }

    private sealed class OneAdapterFactory(FakeAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }
}
