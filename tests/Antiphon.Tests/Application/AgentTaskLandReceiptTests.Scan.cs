using System.Collections;
using System.Data.Common;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class AgentTaskLandReceiptTests
{
    [Test]
    [Timeout(180_000)]
    [Arguments("none")]
    [Arguments("below-floor")]
    [Arguments("different-text")]
    [Arguments("exact")]
    [Arguments("queued-only-two-kind")]
    [Arguments("user-only-one-kind")]
    [Arguments("queued-only-one-kind")]
    [Arguments("null-text")]
    [Arguments("duplicate")]
    [Arguments("match-last")]
    [Arguments("match-first")]
    public async Task C1073_ReceiptDecisionMatchesTheInMemoryScan(string shape)
    {
        var (legacy, expected, rows) = Scenario(shape);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var note = await SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.QueueMessageId.ShouldNotBeNull();
        if (legacy)
        {
            note.IsLegacy = true;
            await db.SaveChangesAsync();
        }

        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        row.Body.ShouldBe(note.Body);
        row.Status = QueuedMessageStatus.Sent;
        row.DeliveryAttempts = 1;
        row.LastDeliveryBaselineSequence = Floor;
        row.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        row.DeliveryVerdict = null;
        foreach (var prompt in rows)
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Sequence = prompt.Sequence,
                Kind = prompt.Kind,
                Text = prompt.Text == BodyMarker ? note.Body : prompt.Text,
                Timestamp = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        var oracle = InMemoryFirstReceipt(rows, Floor, legacy, note.Body);
        oracle.ShouldBe(expected);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.ConfirmingPromptSequence.ShouldBe(expected);
        note.ConfirmedAt.HasValue.ShouldBe(expected is not null);
        note.State.ShouldBe(expected is null ? LandNotificationState.AwaitingReceipt : LandNotificationState.Confirmed);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    [Timeout(180_000)]
    public async Task C1073_LowFloorScanProjectsSequenceAndTextAndStopsAtTheFirstMatch()
    {
        const long floor = 5;
        const long matchSequence = 7;
        const int candidates = 48;
        var fat = new string('x', 2000);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        var probe = new ReceiptScanProbe();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(TestDbFixture.CreateDbContextOptions(schema.ConnectionString))
            .AddInterceptors(probe)
            .Options);
        var note = await SeedAsync(db, h.SessionId);
        var service = new AgentTaskLandNotificationService(db, h.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == note.QueueMessageId);
        row.Body.ShouldBe(note.Body);
        row.Status = QueuedMessageStatus.Sent;
        row.DeliveryAttempts = 1;
        row.LastDeliveryBaselineSequence = floor;
        row.LastDeliveryStartedAt = DateTime.UtcNow.AddSeconds(-1);
        row.DeliveryVerdict = null;
        db.TranscriptEntries.Add(Prompt(h.SessionId, floor, note.Body, fat));
        db.TranscriptEntries.Add(Prompt(h.SessionId, matchSequence - 1, null, fat));
        db.TranscriptEntries.Add(Prompt(h.SessionId, matchSequence, note.Body, fat));
        for (var i = 0; i < candidates - 1; i++)
            db.TranscriptEntries.Add(Prompt(h.SessionId, matchSequence + 1 + i, "unrelated filler " + i, fat));
        await db.SaveChangesAsync();

        probe.ReceiptSql.Clear();
        probe.Readers.Clear();
        await service.ReconcileAsync(note.Id, CancellationToken.None);
        await db.Entry(note).ReloadAsync();
        note.State.ShouldBe(LandNotificationState.Confirmed);
        note.ConfirmingPromptSequence.ShouldBe(matchSequence);

        var sql = probe.ReceiptSql.ShouldHaveSingleItem();
        var select = sql.Split("FROM", 2, StringSplitOptions.None)[0];
        select.ShouldContain("\"Sequence\"");
        select.ShouldContain("\"Text\"");
        select.ShouldNotContain("\"ToolInput\"", sql);
        select.ShouldNotContain("\"ApiErrorTimeZoneId\"", sql);
        select.ShouldNotContain("\"ModelCalls\"", sql);
        var reader = probe.Readers.ShouldHaveSingleItem();
        reader.Rows.ShouldBe(1, $"the first candidate is the receipt; the scan read past it toward {candidates} rows");
    }

    private const long Floor = 10;
    private const string BodyMarker = "\u0001body";

    private readonly record struct PromptRow(long Sequence, string? Text, string Kind);

    private static (bool Legacy, long? Expected, PromptRow[] Rows) Scenario(string shape)
    {
        // BodyMarker stands in for the seeded note body. The caller substitutes the real text
        // when it inserts, so every exact arm carries that same immutable payload.
        PromptRow[] rows;
        bool legacy;
        long? expected;
        switch (shape)
        {
            case "none":
                legacy = false; expected = null; rows = [];
                break;
            case "below-floor":
                legacy = false; expected = null; rows = [new(Floor, BodyMarker, TranscriptKinds.UserPrompt)];
                break;
            case "different-text":
                legacy = false; expected = null; rows = [new(11, "unrelated later caller prompt", TranscriptKinds.UserPrompt)];
                break;
            case "exact":
                legacy = false; expected = 11; rows = [new(11, BodyMarker, TranscriptKinds.UserPrompt)];
                break;
            case "queued-only-two-kind":
                legacy = false; expected = 12; rows =
                [
                    new(11, "unrelated user prompt", TranscriptKinds.UserPrompt),
                    new(12, BodyMarker, TranscriptKinds.QueuedUserPrompt),
                ];
                break;
            case "user-only-one-kind":
                legacy = true; expected = 12; rows =
                [
                    new(11, "unrelated queued prompt", TranscriptKinds.QueuedUserPrompt),
                    new(12, BodyMarker, TranscriptKinds.UserPrompt),
                ];
                break;
            case "queued-only-one-kind":
                legacy = true; expected = null; rows =
                [
                    new(11, BodyMarker, TranscriptKinds.QueuedUserPrompt),
                    new(12, "unrelated user prompt", TranscriptKinds.UserPrompt),
                ];
                break;
            case "null-text":
                legacy = false; expected = 12; rows =
                [
                    new(11, null, TranscriptKinds.UserPrompt),
                    new(12, BodyMarker, TranscriptKinds.UserPrompt),
                ];
                break;
            case "duplicate":
                legacy = false; expected = 11; rows =
                [
                    new(11, BodyMarker, TranscriptKinds.UserPrompt),
                    new(12, "unrelated between duplicates", TranscriptKinds.UserPrompt),
                    new(15, BodyMarker, TranscriptKinds.UserPrompt),
                ];
                break;
            case "match-last":
                legacy = false; expected = 41;
                rows = Enumerable.Range(11, 30)
                    .Select(seq => new PromptRow(seq, "unrelated filler " + seq, TranscriptKinds.UserPrompt))
                    .Append(new PromptRow(41, BodyMarker, TranscriptKinds.UserPrompt))
                    .ToArray();
                break;
            case "match-first":
                legacy = false; expected = 11;
                rows = new[] { new PromptRow(11, BodyMarker, TranscriptKinds.UserPrompt) }
                    .Concat(Enumerable.Range(12, 39).Select(seq => new PromptRow(seq, "unrelated filler " + seq, TranscriptKinds.UserPrompt)))
                    .ToArray();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "unknown receipt shape");
        }

        return (legacy, expected, rows);
    }

    private static long? InMemoryFirstReceipt(IReadOnlyList<PromptRow> rows, long floor, bool legacy, string expected)
    {
        var acceptsQueued = LandNoteReceipt.AcceptsQueuedPrompt(legacy, LandNotificationKind.Outcome);
        foreach (var row in rows.OrderBy(r => r.Sequence))
        {
            if (row.Sequence <= floor || row.Text is null)
                continue;
            if (acceptsQueued)
            {
                if (row.Kind != TranscriptKinds.UserPrompt && row.Kind != TranscriptKinds.QueuedUserPrompt)
                    continue;
            }
            else if (row.Kind != TranscriptKinds.UserPrompt)
                continue;
            var text = row.Text == BodyMarker ? expected : row.Text;
            if (LandNoteReceipt.IsReceipt(expected, text))
                return row.Sequence;
        }

        return null;
    }

    private static TranscriptEntry Prompt(Guid session, long sequence, string? text, string toolInput) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = session,
        Sequence = sequence,
        Kind = TranscriptKinds.UserPrompt,
        Text = text,
        ToolInput = toolInput,
        Timestamp = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
    };

    private sealed class ReceiptScanProbe : DbCommandInterceptor
    {
        public List<string> ReceiptSql { get; } = new();
        public List<CountingReader> Readers { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsReceiptScan(command.CommandText))
                ReceiptSql.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!IsReceiptScan(command.CommandText))
                return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
            var counting = new CountingReader(result);
            Readers.Add(counting);
            return new(counting);
        }

        private static bool IsReceiptScan(string sql) =>
            sql.Contains("\"TranscriptEntries\"", StringComparison.Ordinal)
            && sql.Contains("ORDER BY", StringComparison.Ordinal)
            && sql.Contains("\"Sequence\"", StringComparison.Ordinal)
            && !sql.Contains("INSERT", StringComparison.Ordinal);
    }

    private sealed class CountingReader(DbDataReader inner) : DbDataReader
    {
        public int Rows { get; private set; }

        public override bool Read()
        {
            var ok = inner.Read();
            if (ok) Rows++;
            return ok;
        }

        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            var ok = await inner.ReadAsync(cancellationToken);
            if (ok) Rows++;
            return ok;
        }

        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override IEnumerator GetEnumerator() => inner.GetEnumerator();
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
            inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
            inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override bool NextResult() => inner.NextResult();
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override ValueTask<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken = default) =>
            inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken = default) =>
            inner.IsDBNullAsync(ordinal, cancellationToken);
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) =>
            inner.NextResultAsync(cancellationToken);
        public override void Close() => inner.Close();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
        }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
