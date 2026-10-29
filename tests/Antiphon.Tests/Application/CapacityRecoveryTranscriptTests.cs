using System.Reflection;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class CapacityRecoveryTranscriptTests
{
    [Test]
    public async Task C1322_Cancelled_boundary_does_not_progress_recovery()
    {
        foreach (var stored in new[] { true, false })
        foreach (var completionReason in new string?[] { "end_turn", null, "future_reason" })
        {
            await using var f = await SessionStateTestFixture.CreateAsync(configure: services =>
            {
                services.AddSingleton(Options.Create(new SupervisionSettings
                {
                    CapacityRecovery = new CapacityRecoverySettings
                    {
                        Enabled = true, AdmissionIntervalSeconds = 60, JitterSeconds = 0,
                    },
                }));
                services.AddSingleton<CapacityRecoveryService>();
            });
            var recovery = f.Services.GetRequiredService<CapacityRecoveryService>();
            var wait = await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"session:{f.SessionId:N}", CapacityWaitConsumerKind.LiveSession,
                executionKind: AgentKind.Codex, alias: null,
                holdAlreadyCleared: true, sessionId: f.SessionId), default);
            await using (var demand = f.Db())
                await CapacityRecoveryTestSupport.SeedHeldChannelDemandAsync(demand, f.SessionId);
            (await recovery.GrantReadyAsync(default)).ShouldBe(1);
            (await recovery.RedeemAsync(wait.Id, wait.ActionKey, AgentKind.Codex,
                CapacityRedemptionPath.Queue, default)).Ok.ShouldBeTrue();

            await DeliverAsync(f, f.Event(1, TranscriptKinds.UserPrompt, text: "resume work"), stored);
            var confirmed = await ReloadAsync(f, wait.Id);
            confirmed.State.ShouldBe(CapacityRecoveryWaitState.PromptConfirmed);
            confirmed.ConfirmedPromptSequence.ShouldBe(1);

            var apiError = f.Event(2, TranscriptKinds.TurnEnd) with
            {
                StopReason = "end_turn", IsApiError = true,
            };
            await DeliverAsync(f, apiError, stored);
            var afterError = await ReloadAsync(f, wait.Id);
            afterError.State.ShouldBe(CapacityRecoveryWaitState.PromptConfirmed,
                "an API-error completion is still not recovery progress");
            afterError.Version.ShouldBe(confirmed.Version);

            var cancelled = f.Event(3, TranscriptKinds.TurnEnd) with
            {
                StopReason = TranscriptKinds.StopReasons.Cancelled, IsApiError = false,
            };
            await DeliverAsync(f, cancelled, stored);
            var afterCancel = await ReloadAsync(f, wait.Id);
            afterCancel.State.ShouldBe(CapacityRecoveryWaitState.PromptConfirmed,
                $"{(stored ? "stored action" : "live forwarding")}: cancellation cannot count as recovery progress");
            afterCancel.Outcome.ShouldBe(nameof(CapacityRecoveryWaitState.PromptConfirmed));
            afterCancel.Version.ShouldBe(confirmed.Version);
            afterCancel.ConfirmedPromptSequence.ShouldBe(1);

            await DeliverAsync(f, cancelled, stored);
            (await ReloadAsync(f, wait.Id)).Version.ShouldBe(confirmed.Version,
                "replayed cancellation must leave the confirmed wait unchanged");
            if (stored)
            {
                await using var db = f.Db();
                var boundary = await db.TranscriptEntries.SingleAsync(t => t.Uuid == cancelled.Uuid);
                boundary.StopReason.ShouldBe(TranscriptKinds.StopReasons.Cancelled);
                boundary.IsApiError.ShouldBe(false);
                (await db.SessionRuntimeActions.CountAsync()).ShouldBe(0,
                    "the durable action must finish, rather than hiding a failed capacity phase");
            }

            var completed = f.Event(4, TranscriptKinds.TurnEnd) with
            {
                StopReason = completionReason, IsApiError = false,
            };
            await DeliverAsync(f, completed, stored);
            var progressed = await ReloadAsync(f, wait.Id);
            progressed.State.ShouldBe(CapacityRecoveryWaitState.Progressed,
                "normal, legacy null and other non-cancelled reasons retain their completion semantics");
            progressed.Outcome.ShouldBe(nameof(CapacityRecoveryWaitState.Progressed));
            progressed.Version.ShouldBe(confirmed.Version + 1);

            await DeliverAsync(f, completed, stored);
            await DeliverAsync(f, completed with { Sequence = 5, Uuid = Guid.NewGuid().ToString() }, stored);
            (await ReloadAsync(f, wait.Id)).Version.ShouldBe(progressed.Version,
                "replay and later completion must not progress the same wait twice");
            await using var verify = f.Db();
            (await verify.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
        }
    }

    private static async Task<CapacityRecoveryWait> ReloadAsync(SessionStateTestFixture f, Guid waitId)
    {
        await using var db = f.Db();
        return await db.CapacityRecoveryWaits.SingleAsync(w => w.Id == waitId);
    }

    private static async Task DeliverAsync(SessionStateTestFixture f, SessionRunnerTranscriptEvent entry, bool stored)
    {
        if (stored)
        {
            await f.Runtime.ObserveTranscriptAsync(entry, default);
            // A rebuilt coordinator must read the persisted row, including StopReason.
            await using var restarted = f.NewContainer();
            await restarted.GetRequiredService<SessionRuntimeActionCoordinator>().DrainPageAsync(f.SessionId, default);
        }
        else
        {
            // The legacy live forwarding seam is private; exercise its actual argument
            // forwarding without adding a production API solely for the test.
            var dispatch = typeof(AgentSessionRuntime).GetMethod("DispatchCapacityRecoveryTranscriptAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)dispatch.Invoke(f.Runtime, [entry, entry.Sequence, CancellationToken.None])!;
        }
    }
}
