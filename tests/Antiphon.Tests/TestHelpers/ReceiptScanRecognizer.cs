using System.Collections;
using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1121 (promoted from CARD-1073's receipt-scan probe). Recognizes the reconciler's receipt
/// SELECT by the closed shape of the CARD-1073 projection, records each scan's floor parameter and
/// counts the rows the client actually read through a <see cref="CountingReader"/>. Any other
/// statement, including the session-state seed that also orders by sequence, is not a receipt scan.
/// </summary>
internal sealed class ReceiptScanRecognizer : DbCommandInterceptor
{
    private readonly ConcurrentQueue<Scan> _scans = new();

    internal sealed record Scan(string Sql, Guid? Session, long? Floor, CountingReader? Reader)
    {
        public int Rows => Reader?.Rows ?? 0;
    }

    public IReadOnlyList<Scan> Scans => _scans.ToArray();

    /// <summary>CARD-1121 S3 reader-fault seam: runs after each row a receipt scan reads (a throw faults the scan there).</summary>
    public Action<int>? AfterRow { get; set; }

    public void Reset()
    {
        while (_scans.TryDequeue(out _))
        {
        }
    }

    public static bool IsReceiptScan(string sql)
    {
        var text = sql.TrimStart();
        if (text.StartsWith("-- land-note.receipt-scan", StringComparison.Ordinal))
            text = text[(text.IndexOf('\n') + 1)..].TrimStart();
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.StartsWith("SELECT t.\"Sequence\", t.\"Text\" FROM \"TranscriptEntries\" AS t WHERE t.\"AgentSessionId\" = @", StringComparison.Ordinal)
            ? Shape(text)
            : false;
    }

    private static bool Shape(string text) =>
        text.Contains("t.\"Text\" IS NOT NULL", StringComparison.Ordinal)
        && (text.Contains("t.\"Kind\" = 'UserPrompt'", StringComparison.Ordinal)
            || text.Contains("t.\"Kind\" IN ('UserPrompt', 'QueuedUserPrompt')", StringComparison.Ordinal))
        && (text.Contains("t.\"Sequence\" > @", StringComparison.Ordinal)
            || text.Contains("t.\"Timestamp\" >= @", StringComparison.Ordinal))
        && text.EndsWith("ORDER BY t.\"Sequence\"", StringComparison.Ordinal)
        && !text.Contains("LIMIT", StringComparison.Ordinal)
        && !text.Contains("ToolInput", StringComparison.Ordinal)
        && !text.Contains("ApiErrorTimeZoneId", StringComparison.Ordinal)
        && !text.Contains("ModelCalls", StringComparison.Ordinal);

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (!IsReceiptScan(command.CommandText))
            return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        var counting = new CountingReader(result, AfterRow);
        _scans.Enqueue(new Scan(command.CommandText, Session(command), Floor(command), counting));
        return new(counting);
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        if (!IsReceiptScan(command.CommandText))
            return base.ReaderExecuted(command, eventData, result);
        var counting = new CountingReader(result, AfterRow);
        _scans.Enqueue(new Scan(command.CommandText, Session(command), Floor(command), counting));
        return counting;
    }

    private static Guid? Session(DbCommand command) => Parameter(command, "t.\"AgentSessionId\" = ") is Guid id ? id : null;

    // The sequence floor; null for the timestamp-floor shape.
    private static long? Floor(DbCommand command) =>
        Parameter(command, "t.\"Sequence\" > ") is { } value ? Convert.ToInt64(value) : null;

    private static object? Parameter(DbCommand command, string comparison)
    {
        var text = command.CommandText;
        var at = text.IndexOf(comparison + "@", StringComparison.Ordinal);
        if (at < 0) return null;
        var start = at + comparison.Length;
        var end = start + 1;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        var name = text[start..end];
        foreach (DbParameter parameter in command.Parameters)
            if (parameter.ParameterName == name || "@" + parameter.ParameterName == name)
                return parameter.Value is null or DBNull ? null : parameter.Value;
        return null;
    }
}

/// <summary>Counts the rows a client actually read from one reader. Promoted from CARD-1073's probe.</summary>
internal sealed class CountingReader(DbDataReader inner, Action<int>? afterRow = null) : DbDataReader
{
    public int Rows { get; private set; }

    public override bool Read()
    {
        var ok = inner.Read();
        if (ok) afterRow?.Invoke(++Rows);
        return ok;
    }

    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        var ok = await inner.ReadAsync(cancellationToken);
        if (ok) afterRow?.Invoke(++Rows);
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
    public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken = default) =>
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
