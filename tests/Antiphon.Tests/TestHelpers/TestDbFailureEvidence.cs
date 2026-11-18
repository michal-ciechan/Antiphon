using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Profile-413 (rc run f2de5b30): a transient Npgsql failure reaches a test only as EF's
/// "likely due to a transient failure" wrapper, and the TRX keeps no inner exception. Every
/// context built from <see cref="TestDbFixtureLifecycle.BuildOptions(string)"/> writes such a
/// failure's whole exception chain to the test's output, so the next occurrence is attributable.
/// </summary>
internal static class TestDbFailureEvidence
{
    public const string Tag = "[db-failure]";

    private static readonly AsyncLocal<TextWriter?> Override = new();

    /// <summary>The test's own output unless a witness redirects it for its async flow.</summary>
    public static TextWriter Out
    {
        get => Override.Value ?? Console.Out;
        set => Override.Value = value;
    }

    public static IInterceptor Interceptor { get; } = new FailureInterceptor();

    /// <summary>The exceptions EF's Npgsql strategy wraps as transient (NpgsqlTransientExceptionDetector).</summary>
    internal static bool IsTransient(Exception ex) => ex is NpgsqlException { IsTransient: true } or TimeoutException;

    internal static void Write(string phase, string? database, TimeSpan duration, Exception ex)
    {
        if (!IsTransient(ex))
            return;
        var chain = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
            chain.Add(e.GetType().FullName + (e is PostgresException pg ? " " + pg.SqlState : "") + ": " + e.Message);
        // One compact line first: a truncated stdout excerpt still names the cause.
        Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Tag} {phase} database={database} elapsedMs={(long)duration.TotalMilliseconds} chain={string.Join(" > ", chain)}"));
        Out.WriteLine(ex.ToString());
    }

    private sealed class FailureInterceptor : IDbConnectionInterceptor, IDbCommandInterceptor
    {
        public void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) =>
            Write("connection-open", connection.Database, eventData.Duration, eventData.Exception);

        public Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Write("connection-open", connection.Database, eventData.Duration, eventData.Exception);
            return Task.CompletedTask;
        }

        public void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
            Write("command", command.Connection?.Database, eventData.Duration, eventData.Exception);

        public Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Write("command", command.Connection?.Database, eventData.Duration, eventData.Exception);
            return Task.CompletedTask;
        }
    }
}
