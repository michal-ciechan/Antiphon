using System.Collections;
using System.Data.Common;
using System.Reflection;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("AgentQueue")]
public sealed class TaskInputAttentionCostTests
{
    [Test]
    public async Task No_input_reads_no_transcripts_despite_large_history()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        await SeedHistoryAsync(f);
        // Ordinary delegate queue traffic must not make the session an input candidate.
        await using (var db = f.Db())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = 1,
                Body = "An ordinary delegation brief without a caller input.",
                Origin = QueuedMessageOrigin.Delegation, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var probe = new TranscriptReadProbe();
        (await ReadBuilderAsync(f, probe)).ShouldBeEmpty();
        probe.ReadOperations.ShouldBe(0, "no-input transcript materialization cost");
        probe.Commands.ShouldBe(0, "no-input transcript query cost");
    }

    [Test]
    public async Task Input_reads_exclude_unrelated_history_before_first_delivery()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        await SeedHistoryAsync(f);
        var row = await SeedInputAsync(f, 3000);
        await AddEntryAsync(f, 3001, TranscriptKinds.AssistantText,
            $"I cannot read {row.RemoteSpillRelativePath}.");
        // A retried row has a newer baseline. The first complaint still belongs to it.
        await using (var db = f.Db())
            await db.SessionQueuedMessages.Where(r => r.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastDeliveryBaselineSequence, 3001L)
                    .SetProperty(r => r.DeliveryAttempts, 2));
        var probe = new TranscriptReadProbe();
        (await ReadBuilderAsync(f, probe)).Single().MessageId.ShouldBe(row.Id);
        probe.ReadOperations.ShouldBeLessThanOrEqualTo(8,
            "input transcript reads must be bounded independently of 2,000 historical rows");
        probe.MaxColumns.ShouldBeLessThanOrEqualTo(4, "transcript projection only");
    }

    [Test]
    [Arguments("none")]
    [Arguments("ok")]
    [Arguments("unreadable")]
    [Arguments("legacy")]
    [Arguments("multiple")]
    [Arguments("multiple-implicit")]
    [Arguments("large")]
    [Arguments("retry")]
    [Arguments("superseded")]
    [Arguments("foreign-key")]
    [Arguments("malformed-key")]
    [Arguments("ansi")]
    [Arguments("space-free")]
    [Arguments("framed")]
    [Arguments("wildcards")]
    [Arguments("short")]
    [Arguments("shared-session")]
    public async Task Results_are_byte_identical_to_original_builder(string scenario)
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        if (scenario is "none" or "large")
            await SeedHistoryAsync(f);
        if (scenario != "none")
        {
            SessionQueuedMessage row;
            if (scenario == "legacy")
            {
                var marker = DelegationReportFormatter.TaskMarker(f.TaskId);
                var path = $@"C:\src\Antiphon\.antiphon\task-{DelegationReportFormatter.Short(f.TaskId)}-refinement-20261001.md";
                row = new SessionQueuedMessage
                {
                    Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = 1,
                    Body = $"{marker} REFINEMENT Read '{path}' {marker}",
                    Origin = QueuedMessageOrigin.Delegation, Status = QueuedMessageStatus.Sent,
                    CreatedAt = DateTime.UtcNow,
                };
                await using var db = f.Db();
                db.SessionQueuedMessages.Add(row);
                await db.SaveChangesAsync();
                await AddEntryAsync(f, 3000, TranscriptKinds.UserPrompt, row.Body);
                await AddEntryAsync(f, 3001, TranscriptKinds.AssistantText,
                    $"I cannot read {path}; it is not mounted.", timestamp: false);
            }
            else
            {
                row = await SeedInputAsync(f, 3000);
                if (scenario is "ansi" or "space-free" or "framed" or "wildcards" or "short")
                {
                    var body = scenario switch
                    {
                        "wildcards" => "%_! 😀 " + row.Body,
                        "short" => "go",
                        _ => row.Body,
                    };
                    var prompt = scenario switch
                    {
                        "ansi" => string.Join("\u001b[0m", body.Select(c => c.ToString())),
                        "space-free" => PromptSubmissionMatch.Normalize(body).Replace(" ", "", StringComparison.Ordinal),
                        "framed" => new string('z', 8000) + body + " end framing",
                        "short" => string.Empty,
                        _ => body,
                    };
                    await using var db = f.Db();
                    await db.SessionQueuedMessages.Where(r => r.Id == row.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Body, body));
                    await db.TranscriptEntries.Where(t => t.Sequence == 3000)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.Text, prompt));
                }
                if (scenario == "foreign-key")
                {
                    await using var db = f.Db();
                    await db.AgentTaskEvents.ExecuteUpdateAsync(s => s.SetProperty(e => e.AgentSessionId, (Guid?)null));
                }
                if (scenario == "malformed-key")
                {
                    await using var db = f.Db();
                    await db.SessionQueuedMessages.ExecuteUpdateAsync(s => s.SetProperty(r => r.ConversationKey, "task-input:invalid"));
                }
                if (scenario == "retry")
                {
                    await using var db = f.Db();
                    await db.SessionQueuedMessages.ExecuteUpdateAsync(s => s.SetProperty(r => r.DeliveryAttempts, 3)
                        .SetProperty(r => r.LastDeliveryBaselineSequence, 3010L));
                }
                await AddEntryAsync(f, 3001, TranscriptKinds.AssistantText,
                    scenario == "ok" ? "Read the input successfully and continuing."
                        : new string('z', 8000) + $" I cannot read {row.RemoteSpillRelativePath}.", timestamp: false);
                // Null prompts and tool quotations are not turn boundaries or assistant complaints.
                await AddEntryAsync(f, 3002, TranscriptKinds.UserPrompt, null);
                await AddEntryAsync(f, 3003, TranscriptKinds.ToolResult,
                    $"I cannot read {row.RemoteSpillRelativePath}.");
                if (scenario is "multiple" or "multiple-implicit")
                {
                    var second = await SeedInputAsync(f, 3010);
                    await AddEntryAsync(f, 3011, TranscriptKinds.AssistantText,
                        scenario == "multiple" ? $"I cannot read {second.RemoteSpillRelativePath}."
                            : "The refinement file is not mounted.");
                }
                if (scenario == "superseded")
                {
                    var body = "An ordinary delegation brief that begins a later complete turn.";
                    await using var db = f.Db();
                    db.SessionQueuedMessages.Add(new SessionQueuedMessage
                    {
                        Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = 2,
                        Body = body, Origin = QueuedMessageOrigin.Delegation,
                        Status = QueuedMessageStatus.Canceled, CreatedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync();
                    await AddEntryAsync(f, 3010, TranscriptKinds.UserPrompt, body);
                }
                if (scenario == "shared-session")
                {
                    await using var db = f.Db();
                    var secondTaskId = Guid.NewGuid();
                    var eventId = Guid.NewGuid();
                    var body = "A separate caller input owned by the blocked task on this session.";
                    db.AgentTasks.Add(new AgentTask
                    {
                        Id = secondTaskId, RootTaskId = secondTaskId, AgentSessionId = f.SessionId,
                        Title = "second owner", Goal = "Check", Kind = AgentTaskKind.Worker,
                        Role = AgentTaskRole.Code, AgentKind = AgentKind.Codex, ModelLevel = AgentModelLevel.High,
                        Workspace = WorkspaceMode.Worktree, WorkingDirectory = f.ServerRoot,
                        Status = AgentTaskStatus.Blocked, CreatedAt = DateTime.UtcNow,
                    });
                    db.AgentTaskEvents.Add(new AgentTaskEvent
                    {
                        Id = eventId, AgentTaskId = secondTaskId, AgentSessionId = f.SessionId,
                        Type = AgentTaskEventType.Replied, InputBody = body, At = DateTime.UtcNow,
                    });
                    db.SessionQueuedMessages.Add(new SessionQueuedMessage
                    {
                        Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = 2,
                        Body = body, Origin = QueuedMessageOrigin.Delegation, CreatedAt = DateTime.UtcNow,
                        ConversationKey = AgentTaskInputService.ConversationKey(secondTaskId, eventId),
                    });
                    await db.SaveChangesAsync();
                    await AddEntryAsync(f, 3010, TranscriptKinds.UserPrompt, body);
                    await AddEntryAsync(f, 3011, TranscriptKinds.AssistantText,
                        $"I cannot read {AgentTaskInputService.Route(secondTaskId, eventId)}.");
                }
            }
        }
        await using var oracleDb = f.Db();
        var tasks = await oracleDb.AgentTasks.AsNoTracking().OrderBy(t => t.CreatedAt).ToListAsync();
        var expected = await OriginalBuilderAsync(oracleDb, tasks, CancellationToken.None);
        var actual = await ReadBuilderAsync(f);
        expected.Count.ShouldBe(scenario is "none" or "ok" or "multiple-implicit" or "superseded"
            or "foreign-key" or "malformed-key" ? 0 : 1, "oracle fixture must exercise its declared behavior");
        System.Text.Json.JsonSerializer.Serialize(actual)
            .ShouldBe(System.Text.Json.JsonSerializer.Serialize(expected), scenario);
    }

    // Hand-written outcomes. The numbers are the bound, not a value copied from production.
    [Test]
    [Arguments("closed-by-later-prompt", 0, 3, 256)]
    [Arguments("complaint-on-last-bounded-row", 1, 16, 8192)]
    [Arguments("complaint-just-outside-bound", 1, 16, 8192)]
    public async Task Weak_arm_reads_a_bounded_window(string scenario, int items, int maxRows, int maxTextBytes)
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        var row = await SeedWeakArmCaseAsync(f, scenario);
        var probe = new TranscriptReadProbe { CountTextBytes = true };
        var actual = await ReadBuilderAsync(f, probe);
        var detail = $"{scenario} rows={probe.ReadOperations} textBytes={probe.TextBytes}";
        actual.Count.ShouldBe(items, detail);
        if (items == 1)
            actual.Single().MessageId.ShouldBe(row.Id, detail);
        probe.ReadOperations.ShouldBeLessThanOrEqualTo(maxRows, detail);
        probe.TextBytes.ShouldBeLessThanOrEqualTo(maxTextBytes, detail);
    }

    private static async Task<SessionQueuedMessage> SeedWeakArmCaseAsync(TaskInputSpillFixture f, string scenario)
    {
        if (scenario == "closed-by-later-prompt")
        {
            await SeedHistoryAsync(f);
            var row = await SeedInputAsync(f, 3000);
            await SetShortBodyAsync(f, row.Id, 3000);
            await AddEntryAsync(f, 3001, TranscriptKinds.AssistantText,
                $"I cannot read {row.RemoteSpillRelativePath}.");
            return row;
        }

        var owned = await SeedInputAsync(f, 10);
        await SetShortBodyAsync(f, owned.Id, 10);
        var complaintOn = scenario == "complaint-on-last-bounded-row" ? 8 : 9;
        if (complaintOn != 8 && complaintOn != 9)
            throw new InvalidOperationException(scenario);
        for (var offset = 1; offset <= 8; offset++)
        {
            var text = offset == complaintOn
                ? $"I cannot read {owned.RemoteSpillRelativePath}."
                : "still working on the task";
            await AddEntryAsync(f, 10 + offset, TranscriptKinds.AssistantText, text);
        }

        var trailingAt = 19L;
        if (complaintOn == 9)
        {
            await AddEntryAsync(f, 19, TranscriptKinds.AssistantText,
                $"I cannot read {owned.RemoteSpillRelativePath}.");
            trailingAt = 20;
        }

        await SeedTrailingAssistantsAsync(f, trailingAt);
        return owned;
    }

    private static async Task SetShortBodyAsync(TaskInputSpillFixture f, Guid rowId, long promptSequence)
    {
        await using var db = f.Db();
        await db.SessionQueuedMessages.Where(r => r.Id == rowId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Body, "go"));
        await db.TranscriptEntries.Where(t => t.Sequence == promptSequence)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Text, ""));
    }

    private static async Task SeedTrailingAssistantsAsync(TaskInputSpillFixture f, long start)
    {
        await using var db = f.Db();
        var now = DateTime.UtcNow;
        for (var sequence = start; sequence < start + 2000; sequence++)
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = sequence,
                Kind = TranscriptKinds.AssistantText,
                Text = "unrelated trailing transcript " + new string('x', 1000),
                Timestamp = now, CreatedAt = now,
            });
        await db.SaveChangesAsync();
    }

    private static async Task SeedHistoryAsync(TaskInputSpillFixture f)
    {
        await using var db = f.Db();
        var now = DateTime.UtcNow.AddDays(-1);
        for (var sequence = 1; sequence <= 2000; sequence++)
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = sequence,
                Kind = sequence % 2 == 0 ? TranscriptKinds.UserPrompt : TranscriptKinds.AssistantText,
                Text = "unrelated old transcript " + new string('x', 1000),
                Timestamp = now, CreatedAt = now,
            });
        await db.SaveChangesAsync();
    }

    private static async Task<SessionQueuedMessage> SeedInputAsync(TaskInputSpillFixture f,
        long sequence)
    {
        await f.Replies.RefineAsync(f.TaskId, new string('q', 1800), CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.AsNoTracking().OrderByDescending(r => r.Sequence).FirstAsync();
        await AddEntryAsync(f, sequence, TranscriptKinds.UserPrompt, row.Body);
        return row;
    }

    private static async Task AddEntryAsync(TaskInputSpillFixture f, long sequence,
        string kind, string? text, bool timestamp = true)
    {
        await using var db = f.Db();
        var now = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc).AddSeconds(sequence);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = sequence,
            Kind = kind, Text = text, Timestamp = timestamp ? now : null, CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<AttentionItemDto>> ReadBuilderAsync(TaskInputSpillFixture f,
        TranscriptReadProbe? probe = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>(
            TestDbFixture.CreateDbContextOptions(f.ConnectionString));
        if (probe is not null)
            options.AddInterceptors(probe);
        await using var db = new AppDbContext(options.Options);
        var tasks = await db.AgentTasks.AsNoTracking().OrderBy(t => t.CreatedAt).ToListAsync();
        var service = new AttentionService(db, new BridgeQueueHarness.EmptyRunnerClient(),
            Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
            TimeProvider.System, NullLogger<AttentionService>.Instance);
        var method = typeof(AttentionService).GetMethod("BuildTaskInputUnreadableItemsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return await (Task<List<AttentionItemDto>>)method.Invoke(service, [tasks, CancellationToken.None])!;
    }

    private sealed class TranscriptReadProbe : DbCommandInterceptor
    {
        public int Commands { get; private set; }
        public int ReadOperations { get; private set; }
        public int MaxColumns { get; private set; }
        public int TextBytes { get; private set; }
        public bool CountTextBytes { get; init; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"TranscriptEntries\"", StringComparison.Ordinal))
            {
                MaxColumns = Math.Max(MaxColumns, result.FieldCount);
                if (CountTextBytes)
                    result = new TextByteReader(result, this);
            }
            return ValueTask.FromResult(result);
        }

        private sealed class TextByteReader : DbDataReader
        {
            private readonly DbDataReader _inner;
            private readonly TranscriptReadProbe _owner;
            private readonly HashSet<int> _seen = [];

            public TextByteReader(DbDataReader inner, TranscriptReadProbe owner)
            {
                _inner = inner;
                _owner = owner;
            }

            private void Note(int ordinal, object? value)
            {
                if (value is not string text || !_seen.Add(ordinal))
                    return;
                _owner.TextBytes += Encoding.UTF8.GetByteCount(text);
            }

            public override bool Read()
            {
                _seen.Clear();
                return _inner.Read();
            }

            public override object GetValue(int ordinal)
            {
                var value = _inner.GetValue(ordinal);
                Note(ordinal, value);
                return value;
            }

            public override string GetString(int ordinal)
            {
                var value = _inner.GetString(ordinal);
                Note(ordinal, value);
                return value;
            }

            public override int Depth => _inner.Depth;
            public override int FieldCount => _inner.FieldCount;
            public override bool HasRows => _inner.HasRows;
            public override bool IsClosed => _inner.IsClosed;
            public override int RecordsAffected => _inner.RecordsAffected;
            public override object this[int ordinal] => _inner[ordinal];
            public override object this[string name] => _inner[name];
            public override bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);
            public override byte GetByte(int ordinal) => _inner.GetByte(ordinal);
            public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
                _inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
            public override char GetChar(int ordinal) => _inner.GetChar(ordinal);
            public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
                _inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
            public override string GetDataTypeName(int ordinal) => _inner.GetDataTypeName(ordinal);
            public override DateTime GetDateTime(int ordinal) => _inner.GetDateTime(ordinal);
            public override decimal GetDecimal(int ordinal) => _inner.GetDecimal(ordinal);
            public override double GetDouble(int ordinal) => _inner.GetDouble(ordinal);
            public override IEnumerator GetEnumerator() => _inner.GetEnumerator();
            public override Type GetFieldType(int ordinal) => _inner.GetFieldType(ordinal);
            public override float GetFloat(int ordinal) => _inner.GetFloat(ordinal);
            public override Guid GetGuid(int ordinal) => _inner.GetGuid(ordinal);
            public override short GetInt16(int ordinal) => _inner.GetInt16(ordinal);
            public override int GetInt32(int ordinal) => _inner.GetInt32(ordinal);
            public override long GetInt64(int ordinal) => _inner.GetInt64(ordinal);
            public override string GetName(int ordinal) => _inner.GetName(ordinal);
            public override int GetOrdinal(string name) => _inner.GetOrdinal(name);
            public override int GetValues(object[] values) => _inner.GetValues(values);
            public override bool IsDBNull(int ordinal) => _inner.IsDBNull(ordinal);
            public override bool NextResult() => _inner.NextResult();

            public override Task<bool> ReadAsync(CancellationToken cancellationToken)
            {
                _seen.Clear();
                return _inner.ReadAsync(cancellationToken);
            }

            public override ValueTask DisposeAsync() => _inner.DisposeAsync();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }

        public override InterceptionResult DataReaderDisposing(DbCommand command,
            DataReaderDisposingEventData eventData, InterceptionResult result)
        {
            if (command.CommandText.Contains("\"TranscriptEntries\"", StringComparison.Ordinal))
            {
                Commands++;
                ReadOperations += eventData.ReadCount;
            }
            return result;
        }
    }
    // Frozen pre-CARD-0974 algorithm, copied verbatim except for the explicit DbContext argument.
    private static async Task<List<AttentionItemDto>> OriginalBuilderAsync(
        AppDbContext db, IReadOnlyList<AgentTask> tasks, CancellationToken ct)
    {
        var bound = tasks.Where(t => t.AgentSessionId is not null).ToList();
        if (bound.Count == 0)
            return [];
        var sessionIds = bound.Select(t => t.AgentSessionId!.Value).Distinct().ToArray();
        var taskIds = bound.Select(t => t.Id).ToArray();
        var rows = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => sessionIds.Contains(m.AgentSessionId)
                && m.Origin == QueuedMessageOrigin.Delegation)
            .OrderBy(m => m.Sequence).ToListAsync(ct);
        var inputEvents = await db.AgentTaskEvents.AsNoTracking()
            .Where(e => taskIds.Contains(e.AgentTaskId) && e.InputBody != null
                && (e.Type == AgentTaskEventType.Refined || e.Type == AgentTaskEventType.Replied))
            .Select(e => new { e.Id, e.AgentTaskId, e.AgentSessionId, e.Type })
            .ToDictionaryAsync(e => e.Id, ct);
        var transcripts = await db.TranscriptEntries.AsNoTracking()
            .Where(t => sessionIds.Contains(t.AgentSessionId)
                && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.AssistantText))
            .OrderBy(t => t.Sequence).ToListAsync(ct);
        var items = new List<AttentionItemDto>();

        foreach (var task in bound)
        {
            var sessionId = task.AgentSessionId!.Value;
            var marker = DelegationReportFormatter.TaskMarker(task.Id);
            var taskRows = rows.Where(r => r.AgentSessionId == sessionId).Select(r =>
            {
                var owned = AgentTaskInputService.TryParseConversationKey(
                    r.ConversationKey, out var keyedTaskId, out var eventId)
                    && keyedTaskId == task.Id
                    && inputEvents.TryGetValue(eventId, out var e)
                    && e.AgentTaskId == task.Id && e.AgentSessionId == sessionId;
                var legacy = !owned && r.ConversationKey is null
                    && r.Body.Contains(marker, StringComparison.Ordinal)
                    && r.Body.Contains("REFINEMENT", StringComparison.Ordinal)
                    && TaskInputReadFailure.LegacyLocation(r.Body) is not null;
                var location = owned
                    ? r.RemoteSpillRelativePath ?? AgentTaskInputService.Route(task.Id, eventId)
                    : legacy ? TaskInputReadFailure.LegacyLocation(r.Body) : null;
                return new { Row = r, Location = location,
                    IsReply = owned && inputEvents[eventId].Type == AgentTaskEventType.Replied };
            }).Where(x => x.Location is not null).ToList();
            if (taskRows.Count == 0)
                continue;
            var sessionEntries = transcripts.Where(t => t.AgentSessionId == sessionId).ToList();
            var prompts = sessionEntries.Where(t => t.Kind == TranscriptKinds.UserPrompt
                && t.Text is not null).ToList();

            foreach (var input in taskRows)
            {
                var prompt = prompts.FirstOrDefault(p =>
                    PromptSubmissionMatch.IsCompleteIn(input.Row.Body, p.Text));
                if (prompt is null)
                    continue;
                var nextPrompt = prompts.FirstOrDefault(p => p.Sequence > prompt.Sequence);
                var end = nextPrompt?.Sequence ?? long.MaxValue;
                var soleInput = taskRows.Count(x => x.Row.Sequence <= input.Row.Sequence) == 1;
                var complaint = sessionEntries.FirstOrDefault(t => t.Kind == TranscriptKinds.AssistantText
                    && t.Sequence > prompt.Sequence && t.Sequence < end
                    && TaskInputReadFailure.Matches(t.Text, input.Location!, input.IsReply, soleInput));
                if (complaint is null)
                    continue;

                // A later complete caller input begins a new turn and supersedes this episode.
                if (prompts.Any(p => p.Sequence > complaint.Sequence && rows.Any(r =>
                        r.AgentSessionId == sessionId
                        && PromptSubmissionMatch.IsCompleteIn(r.Body, p.Text))))
                    continue;

                items.Add(new AttentionItemDto(
                    AttentionKind.TaskInputUnreadable, AlertSeverity.Warning,
                    task.Id, sessionId, task.AgentId, input.Row.Id,
                    $"Input unreadable: {DelegationReportFormatter.Short(task.Id)}",
                    "Delegate says it cannot read a caller input",
                    $"Assistant transcript #{complaint.Sequence}: {(complaint.Text ?? string.Empty)[..Math.Min(240, complaint.Text?.Length ?? 0)]}",
                    complaint.Timestamp ?? complaint.CreatedAt, null,
                    [AttentionAction.OpenDrawer], task.CardId,
                    ConditionKey: $"task-input-unreadable:{task.Id:D}:{input.Row.Id:D}"));
            }
        }
        return items;
    }

}
