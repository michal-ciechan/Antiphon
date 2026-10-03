using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class TaskInputReadFailureTests
{
    [Test]
    public async Task Own_assistant_read_complaint_appears_while_working()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var row = await SeedPointerAsync(f);
        await AddEntryAsync(f, 101, TranscriptKinds.AssistantText,
            $"I cannot read {row.RemoteSpillRelativePath}; it is not mounted.");
        var items = await ReadItemsAsync(f);
        var note = items.Single(i => i.Kind == AttentionKind.TaskInputUnreadable);
        note.TaskId.ShouldBe(f.TaskId, "own-condition-task");
        note.SessionId.ShouldBe(f.SessionId);
        note.MessageId.ShouldBe(row.Id);
        note.Severity.ShouldBe(AlertSeverity.Warning);
        note.Evidence.ShouldContain("#101");
    }

    [Test]
    public async Task Legacy_windows_pointer_complaint_is_visible()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var path = $@"C:\src\Antiphon\.antiphon\task-{DelegationReportFormatter.Short(f.TaskId)}-refinement-20261001.md";
        var marker = DelegationReportFormatter.TaskMarker(f.TaskId);
        await using (var db = f.Db())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = 1,
                Body = $"{marker} REFINEMENT Read '{path}' {marker}",
                Origin = QueuedMessageOrigin.Delegation, Status = QueuedMessageStatus.Sent,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await using (var db = f.Db())
        {
            var row = await db.SessionQueuedMessages.SingleAsync();
            await AddEntryAsync(f, 100, TranscriptKinds.UserPrompt, row.Body);
        }
        await AddEntryAsync(f, 101, TranscriptKinds.AssistantText,
            @"The refinement file is in C:\src\Antiphon, which this Linux runner cannot access. Please copy its full contents into /work/worktrees/task-8d5aa4fc/.antiphon/inbox/");
        (await ReadItemsAsync(f)).Count(i => i.Kind == AttentionKind.TaskInputUnreadable)
            .ShouldBe(1, "legacy-windows-condition-count");
    }

    [Test]
    public async Task Other_task_path_or_turn_does_not_create_attention()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        await SeedPointerAsync(f);
        await AddEntryAsync(f, 101, TranscriptKinds.AssistantText,
            "I cannot read C:\\elsewhere\\other.md.");
        (await ReadItemsAsync(f)).Count(i => i.Kind == AttentionKind.TaskInputUnreadable)
            .ShouldBe(0, "foreign-path-condition-count=0");
        TaskInputReadFailure.Matches("I cannot read /other/file.md", ".antiphon/inbox/own.md",
            reply: false, soleInput: true).ShouldBeFalse("foreign-path-overrides-implicit");
        TaskInputReadFailure.Matches("The refinement file is not mounted", ".antiphon/inbox/own.md",
            reply: false, soleInput: false).ShouldBeFalse("ambiguous-input-condition-count=0");
    }

    [Test]
    public async Task Quoted_user_tool_text_and_provider_silence_do_not_create_attention()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var row = await SeedPointerAsync(f);
        await AddEntryAsync(f, 101, TranscriptKinds.ToolResult,
            $"quoted: cannot read {row.RemoteSpillRelativePath}");
        (await ReadItemsAsync(f)).Count(i => i.Kind == AttentionKind.TaskInputUnreadable)
            .ShouldBe(0, "tool-quote-condition-count=0");
    }

    [Test]
    public async Task Repeated_attention_reads_keep_one_stable_condition()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var row = await SeedPointerAsync(f);
        await AddEntryAsync(f, 101, TranscriptKinds.AssistantText,
            $"I cannot read {row.RemoteSpillRelativePath}.");
        var commands = new WriteProbe();
        var runner = DispatchProxy.Create<ISessionRunnerClient, InputCallProbe>();
        var inputProbe = (InputCallProbe)runner;
        var first = (await ReadItemsAsync(f, commands, runner)).Single(i => i.Kind == AttentionKind.TaskInputUnreadable);
        await using var db = f.Db();
        var eventsBefore = await db.AgentTaskEvents.CountAsync();
        var secondItems = await ReadItemsAsync(f, commands, runner);
        secondItems.Count(i => i.Kind == AttentionKind.TaskInputUnreadable).ShouldBe(1,
            "condition-count=1");
        var second = secondItems.Single(i => i.Kind == AttentionKind.TaskInputUnreadable);
        second.ConditionKey.ShouldBe(first.ConditionKey, "condition-key-stable");
        second.SinceUtc.ShouldBe(first.SinceUtc, "condition-time-stable");
        second.TaskId.ShouldBe(first.TaskId, "condition-task-stable");
        second.SessionId.ShouldBe(first.SessionId, "condition-session-stable");
        second.MessageId.ShouldBe(first.MessageId, "condition-message-stable");
        commands.Writes.ShouldBe(0, "attention-command-write-count=0");
        inputProbe.InputCalls.ShouldBe(0, "attention-input-call-count=0");
        (await db.AgentTaskEvents.CountAsync()).ShouldBe(eventsBefore, "attention-write-count=0");
    }

    [Test]
    public async Task Unrelated_assistant_progress_does_not_hide_the_complaint()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var row = await SeedPointerAsync(f);
        await AddEntryAsync(f, 101, TranscriptKinds.AssistantText,
            $"I cannot read {row.RemoteSpillRelativePath}.");
        await AddEntryAsync(f, 102, TranscriptKinds.AssistantText, "I am continuing with other files.");
        (await ReadItemsAsync(f)).Count(i => i.Kind == AttentionKind.TaskInputUnreadable)
            .ShouldBe(1, "progress-keeps-condition");
    }

    [Test]
    public async Task Later_delivered_input_supersedes_the_old_complaint()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var row = await SeedPointerAsync(f);
        await AddEntryAsync(f, 101, TranscriptKinds.AssistantText,
            $"I cannot read {row.RemoteSpillRelativePath}.");
        (await ReadItemsAsync(f)).Count(i => i.Kind == AttentionKind.TaskInputUnreadable).ShouldBe(1);
        await f.Replies.RefineAsync(f.TaskId, "a newer input", CancellationToken.None);
        await using (var db = f.Db())
        {
            var newer = await db.SessionQueuedMessages.AsNoTracking()
                .OrderByDescending(r => r.Sequence).FirstAsync();
            await AddEntryAsync(f, 103, TranscriptKinds.UserPrompt, newer.Body);
        }
        (await ReadItemsAsync(f)).Count(i => i.Kind == AttentionKind.TaskInputUnreadable)
            .ShouldBe(0, "later-delivered-input-clears-condition");
    }

    [Test]
    public async Task Terminal_task_removes_open_input_attention()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var row = await SeedPointerAsync(f);
        await AddEntryAsync(f, 101, TranscriptKinds.AssistantText,
            $"I cannot read {row.RemoteSpillRelativePath}.");
        Guid otherTaskId;
        await using (var db = f.Db())
        {
            otherTaskId = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = otherTaskId, RootTaskId = otherTaskId, Title = "independent blocked question",
                Goal = "decide", Status = AgentTaskStatus.Blocked, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var before = await ReadItemsAsync(f);
        before.Count(i => i.Kind == AttentionKind.TaskInputUnreadable).ShouldBe(1,
            "terminal-positive-input-condition-count=1");
        before.Count(i => i.Kind == AttentionKind.BlockedQuestion && i.TaskId == otherTaskId)
            .ShouldBe(1, "other-attention-positive-control");
        await using (var db = f.Db())
            await db.AgentTasks.Where(t => t.Id == f.TaskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Succeeded));
        var after = await ReadItemsAsync(f);
        after.Count(i => i.Kind == AttentionKind.BlockedQuestion && i.TaskId == otherTaskId)
            .ShouldBe(1, "terminal-preserves-other-attention");
        after.Count(i => i.Kind == AttentionKind.TaskInputUnreadable)
            .ShouldBe(0, "terminal-input-condition-count=0");
    }

    private static async Task<SessionQueuedMessage> SeedPointerAsync(TaskInputSpillFixture f)
    {
        await f.Replies.RefineAsync(f.TaskId, new string('q', 1800), CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync();
        await AddEntryAsync(f, 100, TranscriptKinds.UserPrompt, row.Body);
        return row;
    }

    private static async Task AddEntryAsync(TaskInputSpillFixture f, long sequence,
        string kind, string text)
    {
        await using var db = f.Db();
        var now = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc).AddSeconds(sequence);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = sequence,
            Kind = kind, Text = text, Timestamp = now, CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<IReadOnlyList<AttentionItemDto>> ReadItemsAsync(TaskInputSpillFixture f,
        WriteProbe? commands = null, ISessionRunnerClient? runner = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(f.ConnectionString);
        if (commands is not null) options.AddInterceptors(commands);
        await using var db = new AppDbContext(options.Options);
        var service = new AttentionService(db, runner ?? new BridgeQueueHarness.EmptyRunnerClient(),
            Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
            TimeProvider.System, NullLogger<AttentionService>.Instance);
        return (await service.GetAsync(CancellationToken.None, includeProgressProbe: false)).Items;
    }

    public class InputCallProbe : DispatchProxy
    {
        private readonly BridgeQueueHarness.EmptyRunnerClient _inner = new();
        public int InputCalls { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(ISessionRunnerClient.SendInputAsync)) InputCalls++;
            return method.Invoke(_inner, args);
        }
    }

    private sealed class WriteProbe : DbCommandInterceptor
    {
        public int Writes { get; private set; }
        private void Observe(DbCommand command)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(command.CommandText,
                    @"\b(INSERT|UPDATE|DELETE)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                Writes++;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result,
            CancellationToken ct = default)
        { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData data, InterceptionResult<int> result,
            CancellationToken ct = default)
        { Observe(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result)
        { Observe(command); return result; }
        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData data, InterceptionResult<int> result)
        { Observe(command); return result; }
    }
}
