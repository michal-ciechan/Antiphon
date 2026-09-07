using Antiphon.Server.Application.Interfaces;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class SpecialistTaskRunnerDeadlineTests
{
    [Test]
    public async Task Queued_hold_uses_the_task_kind_snapshot_like_dispatch()
    {
        using var h = new OutputDistillationHarness();
        var seat = await h.EnsureSpecialistAsync();
        var availability = new SwitchingAvailability();
        availability.OnCheck = async () =>
        {
            if (availability.Calls == 2)
            {
                await using var edit = OutputDistillationHarness.CreateContext();
                await edit.Agents.Where(a => a.Id == seat.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Kind, AgentKind.Grok));
            }
            if (availability.Calls == 3) availability.Held = true;
        };
        var db = h.Services.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        var runner = new SpecialistTaskRunner(db, h.Clock, NullLogger.Instance, modelAvailability: availability);
        var result = await runner.RunWithPolicyAsync(OutputDistillerProvisioner.Spec(h.Settings), "snapshot", "goal", 3,
            _ => Task.FromResult<Agent?>(seat), new(h.Clock.GetUtcNow().AddSeconds(45)), CancellationToken.None);
        result.Outcome.ShouldBe(SpecialistRunOutcome.Held);
        result.AvailabilityKind.ShouldBe(AgentKind.ClaudeCode);
        result.AvailabilityAlias.ShouldBe("haiku");
        (await h.ReloadTaskAsync(result.RunTaskId!.Value)).AgentKind.ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    public async Task Terminal_read_returning_after_deadline_is_late()
    {
        var clock = new ReadClock();
        var probe = new LateRead(clock);
        using var h = new OutputDistillationHarness(configureDb: o => o.AddInterceptors(probe));
        var seat = await h.EnsureSpecialistAsync();
        var db = h.Services.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        var runner = new SpecialistTaskRunner(db, clock, NullLogger.Instance, modelAvailability: new SwitchingAvailability());
        using var stop = new CancellationTokenSource();
        var pending = runner.RunWithPolicyAsync(OutputDistillerProvisioner.Spec(h.Settings), "late read", "goal", 3,
            _ => Task.FromResult<Agent?>(seat), new(clock.GetUtcNow().AddSeconds(30)), stop.Token);
        try
        {
            var row = await h.WaitForDistillAsync(seat.Id);
            await using var edit = OutputDistillationHarness.CreateContext();
            await edit.AgentTasks.Where(t => t.Id == row.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, AgentTaskStatus.Succeeded)
                .SetProperty(t => t.Result, "completed before the reader returned")
                .SetProperty(t => t.DispatchedAt, DateTime.UtcNow));
            probe.RunId = row.Id;
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            probe.Fired.ShouldBeTrue();
            result.Outcome.ShouldBe(SpecialistRunOutcome.Timeout);
        }
        finally { stop.Cancel(); try { await pending; } catch (OperationCanceledException) { } }
    }

    // UTC observation jumps as the completed DB read returns; real timers stay armed so the
    // post-read guard, rather than a previously canceled token, decides the terminal result.
    private sealed class ReadClock : TimeProvider
    {
        public TimeSpan Offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }

    private sealed class LateRead(ReadClock clock) : DbCommandInterceptor
    {
        public Guid RunId;
        public bool Fired;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (RunId != Guid.Empty && command.CommandText.Contains("FROM \"AgentTasks\"")
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid id && id == RunId))
            { clock.Offset = TimeSpan.FromSeconds(31); Fired = true; }
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    public async Task Hold_appearing_after_ensure_is_rechecked()
    {
        using var h = new OutputDistillationHarness();
        var availability = new SwitchingAvailability();
        var db = h.Services.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        var runner = new SpecialistTaskRunner(db, h.Clock, NullLogger.Instance, modelAvailability: availability);
        var result = await runner.RunWithPolicyAsync(OutputDistillerProvisioner.Spec(h.Settings), "title", "goal", 3,
            async ct => { var seat = await h.EnsureSpecialistAsync(); availability.Held = true; return seat; },
            new(h.Clock.GetUtcNow().AddSeconds(45)), CancellationToken.None);
        result.Outcome.ShouldBe(SpecialistRunOutcome.Held);
        result.RunTaskId.ShouldBeNull();
        availability.Calls.ShouldBe(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Hold_and_dispatch_race_cancel_only_queued(bool dispatchWins)
    {
        using var h = new OutputDistillationHarness();
        var seat = await h.EnsureSpecialistAsync();
        var availability = new SwitchingAvailability();
        var db = h.Services.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        var releaseCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        availability.OnCheck = async () =>
        {
            if (availability.Calls < 3) return;
            await releaseCheck.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await using var edit = OutputDistillationHarness.CreateContext();
            var row = await edit.AgentTasks.SingleAsync(t => t.AgentId == seat.Id && t.Role == AgentTaskRole.Distill);
            if (dispatchWins)
            {
                row.Status = AgentTaskStatus.Dispatched;
                row.DispatchedAt = h.Clock.GetUtcNow().UtcDateTime;
                await edit.SaveChangesAsync();
            }
            availability.Held = true;
        };
        var poll = new PollLogger();
        var runner = new SpecialistTaskRunner(db, h.Clock, poll, modelAvailability: availability);
        var pending = runner.RunWithPolicyAsync(OutputDistillerProvisioner.Spec(h.Settings), "title", "goal", 3,
            _ => Task.FromResult<Agent?>(seat), new(h.Clock.GetUtcNow().AddSeconds(45)), CancellationToken.None);
        var row = await h.WaitForDistillAsync(seat.Id);
        releaseCheck.SetResult();
        if (dispatchWins)
        {
            await UntilAsync(() => availability.Calls >= 3);
            await Task.WhenAny(pending, poll.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5));
            if (!pending.IsCompleted) h.Clock.Advance(TimeSpan.FromSeconds(45));
        }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        result.Outcome.ShouldBe(dispatchWins ? SpecialistRunOutcome.Timeout : SpecialistRunOutcome.Held);
        (await h.ReloadTaskAsync(row.Id)).Status.ShouldBe(dispatchWins ? AgentTaskStatus.Dispatched : AgentTaskStatus.Canceled);
    }

    [Test]
    public async Task Expired_ensure_return_does_not_create_work()
    {
        using var h = new OutputDistillationHarness();
        var seat = await h.EnsureSpecialistAsync();
        var db = h.Services.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        var runner = new SpecialistTaskRunner(db, h.Clock, NullLogger.Instance, modelAvailability: new SwitchingAvailability());
        var result = await runner.RunWithPolicyAsync(OutputDistillerProvisioner.Spec(h.Settings), "title", "goal", 3,
            _ => { h.Clock.Advance(TimeSpan.FromSeconds(45)); return Task.FromResult<Agent?>(seat); },
            new(h.Clock.GetUtcNow().AddSeconds(45)), CancellationToken.None);
        result.Outcome.ShouldBe(SpecialistRunOutcome.Expired);
        result.RunTaskId.ShouldBeNull();
        result.ExpiryPhase.ShouldBe("provision");
    }

    [Test]
    public async Task Host_shutdown_is_not_reported_as_timeout()
    {
        using var h = new OutputDistillationHarness();
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => h.Distiller.RequestAsync(Guid.NewGuid(), null, shutdown.Token));
    }

    internal static Task UntilAsync(Func<bool> condition) => UntilAsync(() => Task.FromResult(condition()));
    internal static async Task UntilAsync(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition()) await Task.Delay(10, deadline.Token);
    }

    private sealed class SwitchingAvailability : IModelAvailability
    {
        public bool Held;
        public int Calls;
        public Func<Task>? OnCheck;
        public async Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct)
        { Calls++; if (OnCheck is not null) await OnCheck(); return Held; }
    }

    private sealed class PollLogger : ILogger
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
        { if (format(state, error).Contains("specialist.poll-wait start")) Entered.TrySetResult(); }
    }
}
