using Antiphon.Resilience;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Resilience;

public interface IDatabaseReadFault
{
    Exception? Consume();
}

public interface IDatabaseReadObserver
{
    void OnAttemptEnded(AppDbContext context);
}

public sealed class DatabaseResilienceName
{
    public DatabaseResilienceName(string? name) =>
        Name = string.IsNullOrWhiteSpace(name) ? "default" : name;

    public string Name { get; }
}

/// <summary>
/// Runs one admitted materialized read in a fresh scope per attempt. Not a global execution strategy.
/// </summary>
public sealed class DatabaseResilienceExecutor
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DatabaseAttemptExecutor _attempts;
    private readonly IOptionsMonitor<ResilienceSettings> _settings;
    private readonly TimeProvider _time;
    private readonly DatabaseResilienceName _database;
    private readonly IDatabaseReadFault? _fault;
    private readonly IDatabaseReadObserver? _observer;

    public DatabaseResilienceExecutor(
        IServiceScopeFactory scopes,
        DatabaseAttemptExecutor attempts,
        IOptionsMonitor<ResilienceSettings> settings,
        TimeProvider time,
        DatabaseResilienceName database,
        IDatabaseReadFault? fault = null,
        IDatabaseReadObserver? observer = null)
    {
        _scopes = scopes;
        _attempts = attempts;
        _settings = settings;
        _time = time;
        _database = database;
        _fault = fault;
        _observer = observer;
    }

    public async Task<T> ExecuteReadAsync<T>(
        string operation,
        Func<AppDbContext, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        if (System.Transactions.Transaction.Current is not null)
            throw new ResilienceAdmissionException("ambient-transaction");
        if (!ResilienceOperations.IsAdmittedDatabaseRead(operation))
            throw new ResilienceAdmissionException("not-admitted");

        var settings = _settings.CurrentValue;
        var budget = ResilienceBudget.Start(_time, settings, profile: null);
        if (!settings.Enabled)
            return await Once(read, cancellationToken).ConfigureAwait(false);

        return await _attempts.ExecuteAsync(
            _database.Name,
            operation,
            budget,
            exception => NpgsqlTransientFailureClassifier.IsRetryable(exception, _settings.CurrentValue, cancellationToken),
            exception => NpgsqlTransientFailureClassifier.IsAvailabilityFailure(exception, _settings.CurrentValue, cancellationToken),
            token => new ValueTask<T>(Once(read, token)),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> Once<T>(Func<AppDbContext, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
    {
        AppDbContext? context = null;
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fault = _fault?.Consume();
            if (fault is not null)
                throw fault;
            var result = await read(context, cancellationToken).ConfigureAwait(false);
            if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
                throw new ResilienceAdmissionException("non-read");
            return result;
        }
        finally
        {
            if (context is not null)
                _observer?.OnAttemptEnded(context);
        }
    }
}
