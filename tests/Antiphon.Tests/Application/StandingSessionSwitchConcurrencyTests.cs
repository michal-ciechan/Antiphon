using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingSessionSwitchConcurrencyTests
{
    [Test]
    public async Task Stop_supersedes_launch_before_spawn_and_before_typing()
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { ReadyHold = ready };
        await using var f = new StandingRecoveryFixture(adapter);
        await f.SeedAsync();
        try
        {
            await f.StartAsync(new(ResumeSessionId: f.A.Id, Prompt: "Must never type"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!adapter.Started) await Task.Delay(10, timeout.Token);
            await using var scope = f.Harness.Provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(f.Agent.Id, default);
            ready.TrySetResult(true);
            await f.IdleAsync();
            adapter.Killed.ShouldBeTrue(); adapter.Disposed.ShouldBeTrue();
            adapter.SentInput.ShouldBeEmpty();
            await using var db = f.Db();
            (await db.Agents.FindAsync(f.Agent.Id))!.Status.ShouldBe(AgentStatus.Stopped);
            (await db.AgentSupervisionStates.FindAsync(f.Agent.Id))!.Suspended.ShouldBeTrue();
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.A.Id)).ShouldBe(0);
        }
        finally { ready.TrySetResult(true); await f.IdleAsync(); }
    }
}
