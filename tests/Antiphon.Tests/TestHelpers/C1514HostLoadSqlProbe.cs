using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace Antiphon.Tests.TestHelpers;
internal sealed class C1514HostLoadSqlProbe : DbCommandInterceptor
{
    public Func<string, bool>? Hold { get; set; }
    public Func<string, bool>? Fail { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<string> Commands { get; } = [];
    private int _held;
    private async Task BeforeAsync(DbCommand command, CancellationToken ct)
    {
        lock (Commands) Commands.Add(command.CommandText);
        if (Fail?.Invoke(command.CommandText) == true) throw new IOException("owned_host_load_write_failure");
        if (Hold?.Invoke(command.CommandText) == true && Interlocked.Exchange(ref _held, 1) == 0)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
    }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { await BeforeAsync(command, cancellationToken); return result; }
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { await BeforeAsync(command, cancellationToken); return result; }
    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    { await BeforeAsync(command, cancellationToken); return result; }
}
