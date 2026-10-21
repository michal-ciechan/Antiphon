using System.Collections.Concurrent;
using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class DispatchBaseInspectionDiagnosticsTests
{
    private static readonly string[] Codes = ["inspection_timeout", "candidate_limit", "git_command_limit", "git_inspection_error"];
    private const string Sensitive = "DO-NOT-LOG-c1349-sensitive-goal-subject-stderr";

    [Test]
    [Arguments("standalone commit")]
    [Arguments("outer commit")]
    [Arguments("outer rollback")]
    [Arguments("repeated read")]
    [Arguments("sensitive fields")]
    public async Task C1349_BlockedLogMatchesCommittedReason(string scenario)
    {
        foreach (var reason in Codes)
        {
            var log = new StructuredLog();
            var rollback = new RejectBlockCommit { Armed = scenario == "outer rollback" };
            var leased = false;
            await using var f = await SiblingInspectionFixture.CreateAsync(_ =>
            {
                leased = true; return Task.CompletedTask;
            }, services =>
            {
                services.AddSingleton<ILoggerProvider>(log);
                services.AddDbContext<AppDbContext>(options => options.AddInterceptors(rollback));
            });
            var options = f.Provider.GetRequiredService<IOptions<GitSettings>>().Value;
            options.WorktreeBaseMaxCandidates = reason == "candidate_limit" ? 1 : 16;
            options.WorktreeBaseMaxGitCommands = reason == "git_command_limit" ? 2 : 128;
            await using (var edit = f.Read())
            {
                var task = await edit.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
                task.Role = AgentTaskRole.Review; task.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Auto;
                task.Goal = Sensitive;
                (await edit.AgentTasks.SingleAsync(t => t.Id == f.Source.Id)).Status = AgentTaskStatus.Succeeded;
                if (reason == "candidate_limit")
                    await DispatchSiblingInspectionTests.SourceAsync(edit, f.Repo, f.Card.Id, "second", "master");
                await edit.SaveChangesAsync();
            }
            var faultReached = false;
            f.Git.Before = (args, ct) =>
            {
                if (leased && args[0] == "for-each-ref")
                {
                    if (reason == "inspection_timeout") { faultReached = true; f.Clock.Advance(TimeSpan.FromSeconds(5)); }
                    if (reason == "git_inspection_error")
                    {
                        faultReached = true;
                        return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(new(128, Sensitive, Sensitive));
                    }
                }
                return Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(null);
            };
            var committedDuringLog = false;
            log.OnBlocked = () =>
            {
                using var observer = f.Read();
                committedDuringLog = observer.AgentTasks.AsNoTracking().Single(t => t.Id == f.Task.Id).Status == AgentTaskStatus.Blocked;
            };
            await f.ObserveAsync();
            await f.TickAsync();
            leased.ShouldBeTrue();
            if (reason is "inspection_timeout" or "git_inspection_error") faultReached.ShouldBeTrue();
            await using var read = f.Read();
            var taskAfter = await read.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
            var records = log.Rows.Where(r => r.Category.EndsWith("AgentTaskDispatcher", StringComparison.Ordinal) && r.Fields.ContainsKey("ReasonCode") && r.Fields.ContainsKey("Attempt")).ToList();
            if (scenario == "outer rollback")
            {
                rollback.Fired.ShouldBeTrue(); records.ShouldBeEmpty(); committedDuringLog.ShouldBeFalse();
                taskAfter.Status.ShouldNotBe(AgentTaskStatus.Blocked);
                (await read.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskAfter.Id && e.Type == AgentTaskEventType.Blocked)).ShouldBe(0);
            }
            else
            {
                taskAfter.Status.ShouldBe(AgentTaskStatus.Blocked, taskAfter.FailureReason);
                taskAfter.FailureReason.ShouldStartWith(reason + ":");
                var record = records.ShouldHaveSingleItem();
                committedDuringLog.ShouldBeTrue(); record.Level.ShouldBe(LogLevel.Information);
                record.Fields["TaskId"].ShouldBe(taskAfter.Id); record.Fields["CardId"].ShouldBe(f.Card.Id);
                record.Fields["Attempt"].ShouldBe(taskAfter.Attempt); record.Fields["ReasonCode"].ShouldBe(reason);
                Convert.ToInt32(record.Fields["Candidates"]).ShouldBeInRange(0, 257);
                Convert.ToInt32(record.Fields["Inspected"]).ShouldBeInRange(0, 16);
                Convert.ToInt32(record.Fields["GitCommands"]).ShouldBeInRange(0, 128);
                if (scenario == "repeated read")
                {
                    await ReadAttentionAsync(f.Schema.ConnectionString, DateTime.UtcNow);
                    await ReadAttentionAsync(f.Schema.ConnectionString, DateTime.UtcNow);
                    await f.TickAsync();
                    log.Rows.Count(r => r.Category.EndsWith("AgentTaskDispatcher", StringComparison.Ordinal) && r.Fields.ContainsKey("ReasonCode") && r.Fields.ContainsKey("Attempt")).ShouldBe(1);
                }
            }
            foreach (var entry in log.Rows.Where(r => r.Category.EndsWith("CardSiblingInspectionService", StringComparison.Ordinal)
                || r.Fields.ContainsKey("ReasonCode")))
            {
                entry.Message.ShouldNotContain(Sensitive);
                entry.Exception?.ToString().ShouldNotContain(Sensitive);
                foreach (var value in entry.Fields.Values) value?.ToString().ShouldNotContain(Sensitive);
            }
        }
        if (scenario == "standalone commit")
        {
            var log = new StructuredLog();
            await using var f = await SiblingInspectionFixture.CreateAsync(configure: s => s.AddSingleton<ILoggerProvider>(log));
            await using (var db = f.Read())
                await db.AgentTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.CostUsd, 50m));
            await f.TickAsync();
            var record = log.Rows.Where(r => r.Fields.GetValueOrDefault("ReasonCode") as string == "cost_ceiling").ShouldHaveSingleItem();
            record.Level.ShouldBe(LogLevel.Information);
            await using var read = f.Read();
            (await read.AgentTasks.SingleAsync(t => t.Id == f.Task.Id)).Status.ShouldBe(AgentTaskStatus.Blocked);
        }
    }

    [Test]
    [Arguments("299s")]
    [Arguments("301s")]
    [Arguments("901s")]
    [Arguments("prelaunch block")]
    [Arguments("ordinary question words")]
    [Arguments("session-bound block")]
    [Arguments("retried/terminal")]
    public async Task C1349_InspectionAttentionExplainsRecovery(string scenario)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = DateTime.UtcNow;
        foreach (var code in Codes)
        {
            var id = Guid.NewGuid();
            Guid? session = null;
            await using (var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                if (scenario == "session-bound block")
                {
                    session = Guid.NewGuid();
                    seed.AgentSessions.Add(new AgentSession { Id = session.Value, DefinitionName = "fixture",
                        Status = SessionStatus.Running, Cwd = Path.GetTempPath(), CreatedAt = now,
                        StartedAt = now, LastSeenAt = now });
                }
                var held = scenario.EndsWith('s') && char.IsDigit(scenario[0]);
                var seconds = held ? int.Parse(scenario[..^1]) : 1;
                var task = new AgentTask { Id = id, RootTaskId = id, Title = "inspection attention", Goal = Sensitive,
                    Status = held ? AgentTaskStatus.Queued : AgentTaskStatus.Blocked, CreatedAt = now.AddMinutes(-20),
                    AgentSessionId = session, DispatchedAt = session is null ? null : now.AddMinutes(-1),
                    FailureReason = held ? null : code + ": candidates=17 inspected=16 commands=128; " + Sensitive,
                    Result = scenario == "ordinary question words" ? "Which candidate_limit should I choose?" : null };
                if (scenario == "ordinary question words") task.FailureReason = null;
                seed.AgentTasks.Add(task);
                seed.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = id,
                    Type = held ? AgentTaskEventType.Held : AgentTaskEventType.Blocked,
                    Detail = held ? "Held: same-card base inspection is pending." : task.FailureReason ?? task.Result!,
                    At = now.AddSeconds(-seconds) });
                await seed.SaveChangesAsync();
            }
            var items = await ReadAttentionAsync(schema.ConnectionString, now);
            if (scenario == "299s") { items.ShouldNotContain(i => i.TaskId == id); continue; }
            var item = items.Single(i => i.TaskId == id);
            if (scenario is "301s" or "901s")
            {
                item.Kind.ShouldBe(AttentionKind.DispatchHeld);
                item.Severity.ShouldBe(scenario == "301s" ? AlertSeverity.Warning : AlertSeverity.Error);
                item.ConditionKey.ShouldBe($"dispatch-held:{id:N}");
                item.Actions.ShouldBe([AttentionAction.OpenDrawer]);
            }
            else if (scenario is "ordinary question words" or "session-bound block")
            {
                item.Headline.ShouldContain("human answer");
                item.Actions.ShouldContain(AttentionAction.Reply);
            }
            else
            {
                item.Headline.ShouldContain("base inspection needs attention");
                item.ConditionKey.ShouldBe($"dispatch-base-inspection:{id:N}");
                item.Actions.ShouldBe([AttentionAction.OpenDrawer, AttentionAction.Cancel]);
                item.Evidence.ShouldContain(code); item.Evidence.ShouldContain("retry");
                item.Evidence.ShouldContain("candidates=17 inspected=16 commands=128");
                item.Evidence.ShouldNotContain(Sensitive);
                if (scenario == "retried/terminal")
                {
                    await using var change = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                    await change.AgentTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s =>
                        s.SetProperty(t => t.Status, AgentTaskStatus.Queued).SetProperty(t => t.Attempt, t => t.Attempt + 1)
                            .SetProperty(t => t.FailureReason, (string?)null));
                    (await ReadAttentionAsync(schema.ConnectionString, now)).ShouldNotContain(i => i.TaskId == id);
                    await change.AgentTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled));
                    (await ReadAttentionAsync(schema.ConnectionString, now)).ShouldNotContain(i => i.TaskId == id);
                }
            }
        }
    }

    private static async Task<IReadOnlyList<Antiphon.Server.Application.Dtos.AttentionItemDto>> ReadAttentionAsync(string connection, DateTime now)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        return (await new AttentionService(db, new AttentionServiceTests.FakeRunnerClient(),
            Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
            new FakeTimeProvider(now), NullLogger<AttentionService>.Instance)
            .GetAsync(default, includeProgressProbe: false)).Items;
    }
    private sealed class RejectBlockCommit : DbTransactionInterceptor
    {
        public bool Armed { get; init; }
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken ct = default)
        {
            if (Armed && !Fired && eventData.Context is AppDbContext db
                && db.ChangeTracker.Entries<AgentTaskEvent>().Any(e => e.Entity.Type == AgentTaskEventType.Blocked))
            { Fired = true; throw new IOException("owned block commit rejected"); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed record LogRow(string Category, LogLevel Level, string Message, Exception? Exception,
        IReadOnlyDictionary<string, object?> Fields);
    private sealed class StructuredLog : ILoggerProvider
    {
        public ConcurrentQueue<LogRow> Rows { get; } = new();
        public Action? OnBlocked { get; set; }
        public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);
        public void Dispose() { }
        private sealed class Sink(StructuredLog owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.ToDictionary(p => p.Key, p => p.Value) : new Dictionary<string, object?>();
                owner.Rows.Enqueue(new(category, level, formatter(state, exception), exception, fields));
                if (category.EndsWith("AgentTaskDispatcher", StringComparison.Ordinal) && fields.ContainsKey("ReasonCode") && fields.ContainsKey("Attempt")) owner.OnBlocked?.Invoke();
            }
        }
    }
}
