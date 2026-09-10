using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingSessionSwitchConcurrencyTests
{
    [Test]
    [Arguments(SessionStatus.Running, false)]
    [Arguments(SessionStatus.Failed, false)]
    [Arguments(SessionStatus.Stopped, false)]
    [Arguments(SessionStatus.Running, true)]
    [Arguments(SessionStatus.Failed, true)]
    [Arguments(SessionStatus.Stopped, true)]
    public async Task Obsolete_queued_launch_cannot_overwrite_a_newer_outcome(SessionStatus outcome, bool alreadyStarted)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter
        {
            ReadyHold = ready,
            ThrowOnStartFactory = () => { entered.TrySetResult(); return null; },
            StartupOutput = "No conversation found with session ID: obsolete",
        };
        var factory = new GenerationAdapterFactory(adapter);
        await using var f = new StandingRecoveryFixture(
            s => s.AddSingleton<IAgentProtocolAdapterFactory>(factory), adapter);
        await f.SeedAsync();
        var target = alreadyStarted ? f.A.Id : f.B.Id;
        DateTime oldGeneration;
        AgentSession expected;
        try
        {
            if (alreadyStarted)
            {
                await f.StartAsync(new(ResumeSessionId: target));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            await using (var db = f.Db())
            {
                expected = (await db.AgentSessions.FindAsync(target))!;
                oldGeneration = expected.StartedAt;
                // Model a newer accepted outcome while an obsolete worker is delayed.
                // The commit-gap regression separately verifies normal Start serializes it.
                expected.StartedAt = oldGeneration.AddSeconds(1);
                expected.Status = outcome;
                expected.FailureReason = outcome == SessionStatus.Failed ? "newer failure evidence" : null;
                expected.RestartFailureKind = outcome == SessionStatus.Failed ? RestartFailureKind.Infrastructure : null;
                expected.TerminationSource = outcome == SessionStatus.Stopped
                    ? SessionTerminationSource.OperatorRequest : SessionTerminationSource.Unknown;
                expected.InteractiveLaunchCompletedAt = outcome == SessionStatus.Running ? expected.StartedAt : null;
                expected.EndedAt = outcome == SessionStatus.Running ? null : expected.StartedAt;
                expected.LastSeenAt = expected.StartedAt;
                var agent = (await db.Agents.FindAsync(f.Agent.Id))!;
                agent.Status = outcome == SessionStatus.Running ? AgentStatus.Running
                    : outcome == SessionStatus.Failed ? AgentStatus.Failed : AgentStatus.Stopped;
                await db.SaveChangesAsync();
            }
            if (!alreadyStarted)
                f.Harness.LaunchQueue.EnqueueInteractiveSession(target, f.Agent.Id, oldGeneration,
                    new AgentLaunchSpec("fake", AgentKind.ClaudeCode, "fake", [],
                        new Dictionary<string, string>(), f.Root, 120, 30), null,
                    initialPrompt: "obsolete prompt");
            ready.TrySetResult(false);
            await f.IdleAsync();

            factory.Creates.ShouldBe(alreadyStarted ? 1 : 0);
            adapter.Started.ShouldBe(alreadyStarted);
            adapter.Killed.ShouldBe(alreadyStarted);
            adapter.Disposed.ShouldBe(alreadyStarted);
            adapter.Inputs.ShouldBeEmpty();
            adapter.Prompts.ShouldBeEmpty();
            await using var verify = f.Db();
            var actual = (await verify.AgentSessions.FindAsync(target))!;
            actual.StartedAt.ShouldBe(expected.StartedAt);
            actual.Status.ShouldBe(expected.Status);
            actual.FailureReason.ShouldBe(expected.FailureReason);
            actual.RestartFailureKind.ShouldBe(expected.RestartFailureKind);
            actual.TerminationSource.ShouldBe(expected.TerminationSource);
            actual.InteractiveLaunchCompletedAt.ShouldBe(expected.InteractiveLaunchCompletedAt);
            actual.EndedAt.ShouldBe(expected.EndedAt);
            actual.LastSeenAt.ShouldBe(expected.LastSeenAt);
            var winner = (await verify.Agents.FindAsync(f.Agent.Id))!;
            winner.PersistentSessionId.ShouldBe(target.ToString("D"));
            winner.Status.ShouldBe(outcome == SessionStatus.Running ? AgentStatus.Running
                : outcome == SessionStatus.Failed ? AgentStatus.Failed : AgentStatus.Stopped);
            (await verify.AgentSupervisionStates.FindAsync(f.Agent.Id))?.ContinuityHeldAt.ShouldBeNull();
            (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == target)).ShouldBe(0);
        }
        finally { ready.TrySetResult(false); await f.IdleAsync(); }
    }

    private sealed class GenerationAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public int Creates { get; private set; }
        public IAgentProtocolAdapter Create(AgentKind kind) { Creates++; return adapter; }
    }
}
