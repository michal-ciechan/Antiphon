using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Historical baselines stop at <see cref="MigrationTestDatabase.LastReversibleMigration"/>, which
/// is before <c>20261120091515_AddRequestedModelAlias</c>. The current <c>AgentTask</c> shape still
/// writes <c>RequestedModelAlias</c>, so a seed insert hits 42703 unless the nullable column is
/// present. This interceptor adds that empty column on the test connection and rewrites the later
/// Up to <c>IF NOT EXISTS</c>. It does not insert a history row and it does not change the shipped
/// migration. <see cref="MigrationTestDatabase"/> drops the empty column again before a tip rewind.
/// A later command that already sees the column does not ALTER again: <c>ADD COLUMN IF NOT EXISTS</c>
/// still takes <c>ACCESS EXCLUSIVE</c> until the surrounding transaction commits.
/// </summary>
internal static class RequestedModelAliasSchema
{
    public const string Column = "RequestedModelAlias";
    public const string MigrationId = "20261120091515_AddRequestedModelAlias";

    private static readonly ConcurrentDictionary<string, byte> ReadyDatabases = new(StringComparer.Ordinal);

    private const string ColumnVisibleSql = """
        SELECT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = 'AgentTasks'
              AND column_name = 'RequestedModelAlias')
        """;

    public static IInterceptor Interceptor { get; } = new AliasInterceptor();

    private static bool Mentions(string text) => text.Contains(Column, StringComparison.Ordinal);

    private static bool LeaveAlone(string text) =>
        text.Contains("information_schema", StringComparison.OrdinalIgnoreCase)
        || text.Contains("DROP COLUMN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase);

    private static void ForgetIfSchemaDropped(string text, DbConnection? connection)
    {
        if (connection?.Database is not { Length: > 0 } database)
            return;
        var dropsColumn = text.Contains("DROP COLUMN", StringComparison.OrdinalIgnoreCase)
            && text.Contains(Column, StringComparison.Ordinal);
        var dropsTable = text.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase)
            && text.Contains("AgentTasks", StringComparison.Ordinal);
        if (dropsColumn || dropsTable)
            ReadyDatabases.TryRemove(database, out _);
    }

    private static bool KnownReady(DbConnection connection) =>
        connection.Database is { Length: > 0 } database && ReadyDatabases.ContainsKey(database);

    // An open transaction can see a column this transaction just added. Remembering that
    // would let another session skip the add before the commit, and then fail with 42703.
    private static void RememberIfCommitted(DbCommand command)
    {
        if (command.Transaction is not null)
            return;
        if (command.Connection?.Database is { Length: > 0 } database)
            ReadyDatabases[database] = 1;
    }

    private static string RewriteAdd(string text)
    {
        if (text.Contains("IF NOT EXISTS \"" + Column + "\"", StringComparison.Ordinal))
            return text;
        text = text.Replace(
            "ADD COLUMN \"" + Column + "\"",
            "ADD COLUMN IF NOT EXISTS \"" + Column + "\"",
            StringComparison.Ordinal);
        return text.Replace(
            "ADD \"" + Column + "\"",
            "ADD COLUMN IF NOT EXISTS \"" + Column + "\"",
            StringComparison.Ordinal);
    }

    private static bool IsColumnAdd(string text) =>
        text.Contains("ADD COLUMN IF NOT EXISTS \"" + Column + "\"", StringComparison.Ordinal)
        && text.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase)
        && !text.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
        && !text.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
        && !text.Contains("SELECT", StringComparison.OrdinalIgnoreCase);

    private static void Prepare(DbCommand command)
    {
        var text = command.CommandText;
        if (string.IsNullOrEmpty(text))
            return;
        ForgetIfSchemaDropped(text, command.Connection);
        if (!Mentions(text) || LeaveAlone(text))
            return;
        text = RewriteAdd(text);
        command.CommandText = text;
        if (IsColumnAdd(text) || command.Connection is null || KnownReady(command.Connection))
            return;
        using var visible = command.Connection.CreateCommand();
        visible.Transaction = command.Transaction;
        visible.CommandTimeout = 30;
        visible.CommandText = ColumnVisibleSql;
        if (visible.ExecuteScalar() is true)
        {
            RememberIfCommitted(command);
            return;
        }
        using var check = command.Connection.CreateCommand();
        check.Transaction = command.Transaction;
        check.CommandTimeout = 30;
        check.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = current_schema() AND table_name = 'AgentTasks')
            """;
        if (check.ExecuteScalar() is not true)
            return;
        using var add = command.Connection.CreateCommand();
        add.Transaction = command.Transaction;
        add.CommandTimeout = 30;
        add.CommandText = """ALTER TABLE "AgentTasks" ADD COLUMN IF NOT EXISTS "RequestedModelAlias" character varying(80)""";
        add.ExecuteNonQuery();
        RememberIfCommitted(command);
    }

    private static async Task PrepareAsync(DbCommand command, CancellationToken cancellationToken)
    {
        var text = command.CommandText;
        if (string.IsNullOrEmpty(text))
            return;
        ForgetIfSchemaDropped(text, command.Connection);
        if (!Mentions(text) || LeaveAlone(text))
            return;
        text = RewriteAdd(text);
        command.CommandText = text;
        if (IsColumnAdd(text) || command.Connection is null || KnownReady(command.Connection))
            return;
        await using var visible = command.Connection.CreateCommand();
        visible.Transaction = command.Transaction;
        visible.CommandTimeout = 30;
        visible.CommandText = ColumnVisibleSql;
        if (await visible.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true)
        {
            RememberIfCommitted(command);
            return;
        }
        await using var check = command.Connection.CreateCommand();
        check.Transaction = command.Transaction;
        check.CommandTimeout = 30;
        check.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = current_schema() AND table_name = 'AgentTasks')
            """;
        if (await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not true)
            return;
        await using var add = command.Connection.CreateCommand();
        add.Transaction = command.Transaction;
        add.CommandTimeout = 30;
        add.CommandText = """ALTER TABLE "AgentTasks" ADD COLUMN IF NOT EXISTS "RequestedModelAlias" character varying(80)""";
        await add.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        RememberIfCommitted(command);
    }

    private sealed class AliasInterceptor : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Prepare(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await PrepareAsync(command, cancellationToken).ConfigureAwait(false);
            return result;
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Prepare(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await PrepareAsync(command, cancellationToken).ConfigureAwait(false);
            return result;
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Prepare(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            await PrepareAsync(command, cancellationToken).ConfigureAwait(false);
            return result;
        }
    }
}
