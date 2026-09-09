using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingRestartAccountingTests
{
    [Test]
    [Arguments(0)] [Arguments(1)] [Arguments(2)] [Arguments(int.MaxValue)]
    public async Task Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(int threshold)
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var interceptor = new CompositionFailure();
        var boot = new FakeAgentProtocolAdapter(); var resumed = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root, [boot, resumed],
                new SupervisionSettings { FreshAfterResumeFailures = threshold }, definitionKind: "ClaudeCode",
                configureDb: b => b.AddInterceptors(interceptor));
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var started = await h.Control.StartAsync(agent.Id, new(), default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            var id = Guid.Parse(started.PersistentSessionId!);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var session = (await db.AgentSessions.FindAsync(id))!;
                session.Status = SessionStatus.Failed;
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ConsecutiveFailures = 7;
                state.LastObservedRestartSessionId = id; state.LastObservedRestartStartedAt = session.StartedAt;
                state.NextRestartAt = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
                await db.SaveChangesAsync();
            }
            interceptor.AgentId = agent.Id; interceptor.Remaining = 3;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await h.Supervisor().TickAsync(default);
                await using var db = AgentSupervisionTests.CreateContext();
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ConsecutiveFailures.ShouldBe(7);
                state.RestartBackoffFailures.ShouldBe(attempt);
                state.ContinuityHeldAt.ShouldBeNull();
                (await db.Agents.FindAsync(agent.Id))!.PersistentSessionId.ShouldBe(id.ToString("D"));
                var before = interceptor.Hits;
                await h.Supervisor().TickAsync(default);
                interceptor.Hits.ShouldBe(before);
                h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, attempt) + 1));
            }
            interceptor.Hits.ShouldBe(3);
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            resumed.Started.ShouldBeTrue(); resumed.StartedArgs.ShouldContain("--resume");
            resumed.StartedSessionId.ShouldBe(id); resumed.StartedArgs.ShouldNotContain("--session-id");
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task Async_infrastructure_failure_is_consumed_once_across_recreation()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root,
                [new FakeAgentProtocolAdapter { ThrowOnStart = new PostgresException("starting", "FATAL", "FATAL", "57P03") }],
                definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var detail = await h.Control.StartAsync(agent.Id, new(), default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            var id = Guid.Parse(detail.PersistentSessionId!);
            for (var i = 0; i < 3; i++)
            {
                await h.Supervisor().TickAsync(default);
                await using var db = AgentSupervisionTests.CreateContext();
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ConsecutiveFailures.ShouldBe(0); state.RestartBackoffFailures.ShouldBe(1);
                state.LastObservedRestartSessionId.ShouldBe(id);
                var session = (await db.AgentSessions.FindAsync(id))!;
                session.RestartFailureKind.ShouldBe(RestartFailureKind.Infrastructure);
                session.InteractiveLaunchCompletedAt.ShouldBeNull();
                // Re-enter the dead-row observation branch, as a fresh scheduler scope would.
                state.NextRestartAt = null;
                await db.SaveChangesAsync();
            }
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    private sealed class CompositionFailure : DbCommandInterceptor
    {
        public Guid AgentId { get; set; }
        public int Remaining { get; set; }
        public int Hits { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Remaining > 0 && command.CommandText.Contains("FROM \"AgentBundleAttachments\"")
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is IEnumerable<Guid> ids && ids.Contains(AgentId)))
            {
                Remaining--; Hits++;
                throw new DbUpdateException("composition storage", new PostgresException("starting", "FATAL", "FATAL", "57P03"));
            }
            return ValueTask.FromResult(result);
        }
    }
}
