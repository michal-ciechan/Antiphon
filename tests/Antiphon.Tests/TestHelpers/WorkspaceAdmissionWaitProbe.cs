using System.Data.Common;
using Antiphon.Server.Application.Exceptions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Observes a real advisory-lock command.</summary>
internal sealed class WorkspaceAdmissionWaitProbe : DbCommandInterceptor
{
    public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Refuse { get; set; }

    public void Reset() => Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("pg_advisory_xact_lock(1017, 459)", StringComparison.Ordinal))
        {
            var refuse = Refuse;
            Entered.TrySetResult();
            if (refuse)
                throw new WorkspaceAdmissionBusyException(new TimeoutException("Injected admission refusal."));
        }
        return ValueTask.FromResult(result);
    }

    // Fixtures set a 100 ms interactive limit. Keep the actual PostgreSQL lock held longer.
    public static async Task<bool> StillWaitingAsync(Task operation) =>
        await Task.WhenAny(operation, Task.Delay(TimeSpan.FromMilliseconds(500))) != operation;

    public static async Task<NpgsqlTransaction> HoldAsync(NpgsqlConnection connection)
    {
        var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(1017, 459)", connection, transaction);
        await command.ExecuteNonQueryAsync();
        return transaction;
    }
}
