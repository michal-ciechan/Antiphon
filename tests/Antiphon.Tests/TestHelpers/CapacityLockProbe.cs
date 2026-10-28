using Npgsql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Deterministic Postgres lock observations. Waits poll <c>pg_stat_activity</c> and
/// <c>pg_locks</c> until a deadline. Advisory keys are matched by <c>classid</c>/<c>objid</c>
/// from <c>hashtext</c> on the same server.
/// </summary>
internal static class CapacityLockProbe
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    public static async Task<NpgsqlConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public static async Task<int> BackendPidAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", connection);
        return (int)(await command.ExecuteScalarAsync(ct))!;
    }

    public static async Task HoldRowAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string table, Guid id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"""SELECT "Id" FROM "{table}" WHERE "Id" = @id FOR UPDATE""", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteScalarAsync(ct);
    }

    public static async Task HoldAdvisoryAsync(NpgsqlConnection connection, string key, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(hashtext(@key))", connection);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteScalarAsync(ct);
    }

    public static async Task UnlockAdvisoryAsync(NpgsqlConnection connection, string key, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtext(@key))", connection);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteScalarAsync(ct);
    }

    public static async Task<int?> BlockedByAsync(string connectionString, int holderPid, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT a.pid
            FROM pg_stat_activity a
            WHERE a.wait_event_type = 'Lock'
              AND @holder = ANY (pg_blocking_pids(a.pid))
            LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("holder", holderPid);
        var value = await command.ExecuteScalarAsync(ct);
        return value is int pid ? pid : null;
    }

    public static async Task<int?> PidHoldingAdvisoryAsync(
        string connectionString, string key, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT pid
            FROM pg_locks
            WHERE locktype = 'advisory'
              AND granted
              AND objsubid = 1
              AND classid = ((hashtext(@key)::bigint >> 32) & 4294967295)::oid
              AND objid = (hashtext(@key)::bigint & 4294967295)::oid
            LIMIT 1
            """, connection);
        command.Parameters.AddWithValue("key", key);
        var value = await command.ExecuteScalarAsync(ct);
        return value is int pid ? pid : null;
    }

    public static async Task<int?> AnyLockWaitPidAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT pid
            FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND wait_event_type = 'Lock'
            LIMIT 1
            """, connection);
        var value = await command.ExecuteScalarAsync(ct);
        return value is int pid ? pid : null;
    }

    public static async Task<string> DescribePidAsync(string connectionString, int pid, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT coalesce(wait_event_type, '') || '/' || coalesce(wait_event, '')
                || E'\n' || left(coalesce(query, ''), 400)
            FROM pg_stat_activity
            WHERE pid = @pid
            """, connection);
        command.Parameters.AddWithValue("pid", pid);
        var activity = (string?)await command.ExecuteScalarAsync(ct) ?? "(pid gone)";
        await using var locks = new NpgsqlCommand(
            """
            SELECT coalesce(string_agg(
                locktype || ' ' || mode || ' granted=' || granted::text
                    || ' class=' || coalesce(classid::text, '')
                    || ' obj=' || coalesce(objid::text, '')
                    || ' sub=' || coalesce(objsubid::text, ''),
                E'\n'), '')
            FROM pg_locks
            WHERE pid = @pid
            """, connection);
        locks.Parameters.AddWithValue("pid", pid);
        var held = (string?)await locks.ExecuteScalarAsync(ct) ?? "";
        return activity + "\n" + held;
    }

    public static async Task<bool> HoldsAdvisoryAsync(
        string connectionString, int pid, string key, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_locks
                WHERE pid = @pid
                  AND locktype = 'advisory'
                  AND granted
                  AND objsubid = 1
                  AND classid = ((hashtext(@key)::bigint >> 32) & 4294967295)::oid
                  AND objid = (hashtext(@key)::bigint & 4294967295)::oid)
            """, connection);
        command.Parameters.AddWithValue("pid", pid);
        command.Parameters.AddWithValue("key", key);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>True when the row lock is acquired. False when Postgres raises 55P03.</summary>
    public static async Task<bool> TryNowaitAsync(
        string connectionString, string table, Guid id, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = new NpgsqlCommand(
            $"""SELECT "Id" FROM "{table}" WHERE "Id" = @id FOR UPDATE NOWAIT""", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        try
        {
            await command.ExecuteScalarAsync(ct);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == "55P03")
        {
            return false;
        }
    }

    public static async Task<int> PollBlockedByAsync(
        string connectionString, int holderPid, Task path, TimeSpan deadline, CancellationToken ct)
    {
        var until = DateTime.UtcNow + deadline;
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            if (path.IsCompleted)
                throw new InvalidOperationException(
                    "path finished before it waited on the held row: " + await DescribeAsync(path));
            if (await BlockedByAsync(connectionString, holderPid, ct) is int pid)
                return pid;
            await Task.Delay(20, ct);
        }

        throw new TimeoutException(
            $"no backend blocked by pid {holderPid} within {deadline.TotalSeconds:0}s; path completed={path.IsCompleted}");
    }

    /// <summary>
    /// Rule F subject: a claim backend blocked on the holder that still holds its
    /// <c>AgentTasks</c> row lock. A waiter with only a post-commit session FK, or a path
    /// that finishes without that in-claim wait, fails immediately.
    /// </summary>
    public static async Task<int> PollInClaimSessionWaitAsync(
        string connectionString, int holderPid, Task path, TimeSpan deadline, CancellationToken ct)
    {
        var until = DateTime.UtcNow + deadline;
        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();
            if (await BlockedByAsync(connectionString, holderPid, ct) is int pid)
            {
                if (await HoldsAgentTaskRowLockAsync(connectionString, pid, ct))
                    return pid;
                throw new InvalidOperationException(
                    "claim reaches the session only after the parallel key; waiter "
                    + $"{pid} holds no AgentTasks row lock: "
                    + await DescribePidAsync(connectionString, pid, ct));
            }

            if (path.IsCompleted)
                throw new InvalidOperationException(
                    "claim reaches the session only after the parallel key: path finished before an in-claim session wait: "
                    + await DescribeAsync(path));
            await Task.Delay(20, ct);
        }

        throw new TimeoutException(
            $"no in-claim backend blocked by pid {holderPid} within {deadline.TotalSeconds:0}s; path completed={path.IsCompleted}");
    }

    public static async Task<bool> HoldsAgentTaskRowLockAsync(
        string connectionString, int pid, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_locks l
                JOIN pg_class c ON c.oid = l.relation
                WHERE l.pid = @pid
                  AND l.granted
                  AND l.locktype = 'relation'
                  AND c.relname = 'AgentTasks'
                  AND l.mode IN (
                      'RowShareLock', 'RowExclusiveLock', 'ShareUpdateExclusiveLock',
                      'ShareLock', 'ShareRowExclusiveLock', 'ExclusiveLock', 'AccessExclusiveLock'))
            """, connection);
        command.Parameters.AddWithValue("pid", pid);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    public static async Task<string> DescribeAsync(Task path)
    {
        try
        {
            await path;
            return "completed";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }
}
