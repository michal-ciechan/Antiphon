using System.Globalization;
using System.Reflection;
using System.Text;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>
/// The shared <c>pg_stat_statements</c> snapshot. The script reads the same file from the checkout;
/// the server reads this embedded copy. Column order is the contract.
/// </summary>
public static class PgStatStatementsSql
{
    public const string ResourceName = "Antiphon.Server.PgStatStatementsSnapshot.sql";
    public const string SplitMarker = "-- @@split@@";

    public static string Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("pg_stat_statements snapshot SQL is not embedded.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static (string Counters, string Text) Split(string sql)
    {
        var at = sql.IndexOf(SplitMarker, StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidOperationException("pg_stat_statements snapshot SQL has no split marker.");
        return (sql[..at].Trim(), sql[(at + SplitMarker.Length)..].Trim());
    }

    /// <summary>Replace <c>/*IDS*/</c> with already-parsed query ids. Anything else is refused.</summary>
    public static string WithIds(string textSql, IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
            throw new InvalidOperationException("text snapshot needs at least one query id.");
        var builder = new StringBuilder(textSql.Length + ids.Count * 8);
        var cursor = 0;
        var at = textSql.IndexOf("/*IDS*/", StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidOperationException("text snapshot has no id list.");
        builder.Append(textSql, cursor, at);
        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(ids[i].ToString(CultureInfo.InvariantCulture));
        }
        builder.Append(textSql, at + "/*IDS*/".Length, textSql.Length - (at + "/*IDS*/".Length));
        return builder.ToString();
    }

    public static PgssSnapshot ToSnapshot(IReadOnlyList<PgssCounterRow> rows)
    {
        long dealloc = 0;
        var reset = "";
        var list = new List<PgssRow>(rows.Count);
        foreach (var row in rows)
        {
            if (long.TryParse(row.Dealloc, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                dealloc = parsed;
            reset = row.StatsReset ?? "";
            if (string.IsNullOrEmpty(row.QueryId)) continue;
            if (!long.TryParse(row.QueryId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                continue;
            long.TryParse(row.Calls, NumberStyles.Integer, CultureInfo.InvariantCulture, out var calls);
            double.TryParse(row.TotalExecMs, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms);
            long.TryParse(row.Rows, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count);
            list.Add(new PgssRow(id, calls, ms, count));
        }
        return new PgssSnapshot(dealloc, reset, list);
    }
}

public sealed class PgssCounterRow
{
    public string? VersionNum { get; set; }
    public string? Version { get; set; }
    public string? Dealloc { get; set; }
    public string? StatsReset { get; set; }
    public string? QueryId { get; set; }
    public string? Calls { get; set; }
    public string? TotalExecMs { get; set; }
    public string? Rows { get; set; }
}

public sealed class PgssTextRow
{
    public string? QueryId { get; set; }
    public string? QueryB64 { get; set; }
}
