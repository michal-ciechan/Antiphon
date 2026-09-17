using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>CARD-0547. Records every command text a context executes, for query-count guards.</summary>
public sealed class CountingCommandInterceptor : DbCommandInterceptor
{
    public ConcurrentQueue<string> Commands { get; } = new();

    public int Count(string fragment) => Commands.Count(c => c.Contains(fragment, StringComparison.Ordinal));

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Commands.Enqueue(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
