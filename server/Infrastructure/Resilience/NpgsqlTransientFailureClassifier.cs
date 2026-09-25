using Antiphon.Resilience;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Antiphon.Server.Infrastructure.Resilience;

/// <summary>
/// Strict SQLSTATE allowlist. Provider <see cref="NpgsqlException.IsTransient"/> is not an OR for Postgres errors.
/// Only the EF wrapper chain is unwrapped: InvalidOperationException around the provider or DbUpdateException, and one DbUpdateException.
/// </summary>
public static class NpgsqlTransientFailureClassifier
{
    public static bool IsRetryable(Exception exception, ResilienceSettings settings, CancellationToken callerCancellation)
    {
        if (callerCancellation.IsCancellationRequested || exception is OperationCanceledException)
            return false;
        if (HttpTransientFailureClassifier.IsTimeoutRejection(exception))
            return false;
        var provider = Unwrap(exception);
        if (provider is null)
            return false;
        if (provider is TimeoutException)
            return true;
        if (provider is PostgresException postgres)
            return settings.Database.AllowedSqlStates.Contains(postgres.SqlState, StringComparer.Ordinal);
        if (provider is NpgsqlException npgsql)
            return npgsql.IsTransient;
        return false;
    }

    public static bool IsAvailabilityFailure(Exception exception, ResilienceSettings settings, CancellationToken callerCancellation)
    {
        if (!IsRetryable(exception, settings, callerCancellation))
            return false;
        var postgres = Unwrap(exception) as PostgresException;
        return postgres is not { SqlState: "40001" or "40P01" };
    }

    public static Exception? Unwrap(Exception exception)
    {
        if (exception is InvalidOperationException invalid)
        {
            if (invalid.InnerException is DbUpdateException update)
                return ProviderOrNull(update.InnerException);
            return ProviderOrNull(invalid.InnerException);
        }

        if (exception is DbUpdateException dbUpdate)
            return ProviderOrNull(dbUpdate.InnerException);
        return ProviderOrNull(exception);
    }

    private static Exception? ProviderOrNull(Exception? exception) =>
        exception is PostgresException or NpgsqlException or TimeoutException ? exception : null;
}
