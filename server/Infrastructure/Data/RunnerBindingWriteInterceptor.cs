using System.Data.Common;
using Antiphon.Server.Domain.Agents;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>
/// CARD-1987. ExecuteUpdate and raw SQL skip the change tracker, so a non-query that writes
/// RunnerId, RunnerStoreId, or RunnerCwd on AgentSessions drops the binding cache.
/// A false positive only forces the next route to read again.
/// </summary>
internal sealed class RunnerBindingWriteInterceptor : DbCommandInterceptor
{
    internal static readonly RunnerBindingWriteInterceptor Instance = new();

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Consider(command);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Consider(command);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    private static void Consider(DbCommand command)
    {
        if (RunnerBindingSql.WritesBindingColumns(command.CommandText))
            RunnerBindingInvalidation.Notify(null);
    }
}

internal static class RunnerBindingSql
{
    internal static bool WritesBindingColumns(string? sql)
    {
        if (string.IsNullOrEmpty(sql))
            return false;
        if (DeletesAgentSessions(sql))
            return true;
        if (sql.IndexOf("\"AgentSessions\"", StringComparison.Ordinal) < 0)
            return false;
        if (IndexOfWord(sql, "UPDATE", 0) < 0)
            return false;

        var index = 0;
        while (IndexOfWord(sql, "SET", index) is var set && set >= 0)
        {
            var regionEnd = sql.Length;
            var where = IndexOfWord(sql, "WHERE", set + 3);
            if (where >= 0)
                regionEnd = where;
            var semi = sql.IndexOf(';', set + 3);
            if (semi >= 0 && semi < regionEnd)
                regionEnd = semi;
            if (RegionHasBindingColumn(sql.AsSpan(set, regionEnd - set)))
                return true;
            index = regionEnd;
        }

        return false;
    }

    private static bool DeletesAgentSessions(string sql)
    {
        var index = 0;
        while (IndexOfWord(sql, "DELETE", index) is var delete && delete >= 0)
        {
            var from = IndexOfWord(sql, "FROM", delete + 6);
            if (from < 0)
                return false;
            var table = sql.IndexOf("\"AgentSessions\"", from, StringComparison.Ordinal);
            if (table >= 0 && sql.AsSpan(from, table - from).IndexOf(';') < 0)
                return true;
            index = delete + 6;
        }

        return false;
    }

    private static bool RegionHasBindingColumn(ReadOnlySpan<char> region) =>
        region.Contains("\"RunnerId\"".AsSpan(), StringComparison.Ordinal)
        || region.Contains("\"RunnerStoreId\"".AsSpan(), StringComparison.Ordinal)
        || region.Contains("\"RunnerCwd\"".AsSpan(), StringComparison.Ordinal);

    private static int IndexOfWord(string sql, string word, int start)
    {
        var index = start;
        while (index < sql.Length)
        {
            var found = sql.IndexOf(word, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return -1;
            var before = found == 0 || !IsWord(sql[found - 1]);
            var after = found + word.Length >= sql.Length || !IsWord(sql[found + word.Length]);
            if (before && after)
                return found;
            index = found + 1;
        }

        return -1;
    }

    private static bool IsWord(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
