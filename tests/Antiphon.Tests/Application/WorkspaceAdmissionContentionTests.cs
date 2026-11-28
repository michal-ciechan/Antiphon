using System.Text.Json;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class WorkspaceAdmissionContentionTests
{
    [Test]
    public async Task DefaultBackgroundAdmissionDoesNotTranslatePostgresLockTimeout()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var waiter = new NpgsqlConnectionStringBuilder(schema.ConnectionString)
        {
            Options = "-c lock_timeout=150ms",
        };
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(waiter.ConnectionString)));
        await using var provider = services.BuildServiceProvider();
        var journal = new WorkspaceReservationJournal(provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);
        var command = new WorkspaceReservationCommand(
            WorkspaceReservationKey.For(Path.GetTempPath(), "background-admission-test", null),
            WorkspaceReservationKind.Launch);

        await using var holder = new NpgsqlConnection(schema.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using var takeLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(1017, 459)", holder, transaction);
        await takeLock.ExecuteNonQueryAsync();

        var timeout = await Should.ThrowAsync<PostgresException>(() => journal.TryAdmitConsumerAsync(command, default));
        timeout.SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable);

        await transaction.RollbackAsync();
        (await journal.TryAdmitConsumerAsync(command, default)).Accepted.ShouldBeTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AdmissionUsesTheShorterTimeoutAndRecordsWaitAndHold(bool shorterSessionTimeout)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var waiter = new NpgsqlConnectionStringBuilder(schema.ConnectionString)
        {
            Options = shorterSessionTimeout
                ? "-c statement_timeout=1200ms -c lock_timeout=150ms"
                : "-c statement_timeout=1200ms",
        };
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(waiter.ConnectionString)));
        await using var provider = services.BuildServiceProvider();
        var options = Options.Create(new WorkspaceAdmissionSettings
        {
            LockTimeoutMilliseconds = shorterSessionTimeout ? 2_000 : 150,
        });
        var logger = new RecordingLogger<WorkspaceReservationJournal>();
        var journal = new WorkspaceReservationJournal(provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, admissionSettings: options, logger: logger);
        var command = new WorkspaceReservationCommand(
            WorkspaceReservationKey.For(Path.GetTempPath(), "workspace-timeout-test", null),
            WorkspaceReservationKind.Launch);

        await using var holder = new NpgsqlConnection(schema.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using var takeLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(1017, 459)", holder, transaction);
        await takeLock.ExecuteNonQueryAsync();
        var busy = await Should.ThrowAsync<WorkspaceAdmissionBusyException>(() =>
            journal.TryAdmitConsumerAsync(command, default, WorkspaceAdmissionWaitPolicy.Interactive));
        busy.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable,
            "the chosen lock timeout expires before the server's statement deadline");
        await transaction.RollbackAsync();

        (await journal.TryAdmitConsumerAsync(command, default)).Accepted.ShouldBeTrue();
        var measured = logger.Entries.Single(e => e.State.ContainsKey("HoldMilliseconds"));
        measured.Level.ShouldBe(LogLevel.Debug);
        measured.State["WaitMilliseconds"].ShouldBeOfType<double>().ShouldBeGreaterThanOrEqualTo(0);
        measured.State["HoldMilliseconds"].ShouldBeOfType<double>().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task IdleLockHolderReturnsServiceUnavailableAndAdmissionRecovers()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var waiterConnection = new NpgsqlConnectionStringBuilder(schema.ConnectionString) { CommandTimeout = 5 };
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(TestDbFixture.CreateDbContextOptions(waiterConnection.ConnectionString)));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var journalLogger = new RecordingLogger<WorkspaceReservationJournal>();
        var journal = new WorkspaceReservationJournal(provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, logger: journalLogger);
        var admission = new WorkspaceUseAdmission(journal, db);
        var command = new WorkspaceReservationCommand(
            WorkspaceReservationKey.For(Path.GetTempPath(), "workspace-admission-test", null),
            WorkspaceReservationKind.Launch);

        await using var holder = new NpgsqlConnection(schema.ConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using var takeLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(1017, 459)", holder, transaction);
        await takeLock.ExecuteNonQueryAsync();

        // Leave this backend idle in its transaction while a second connection attempts admission.
        var logger = new RecordingLogger<ExceptionMiddleware>();
        var middleware = new ExceptionMiddleware(async context =>
        {
            await admission.RequireConsumerAsync(command, context.RequestAborted, WorkspaceAdmissionWaitPolicy.Interactive);
        }, logger);
        var request = new DefaultHttpContext();
        using var body = new MemoryStream();
        request.Response.Body = body;
        await middleware.InvokeAsync(request);

        request.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable,
            logger.Entries.SingleOrDefault()?.Exception?.ToString());
        request.Response.Headers.RetryAfter.ToString().ShouldBe("2");
        journalLogger.Entries.Any(e => e.Level == LogLevel.Warning
            && e.State.GetValueOrDefault("Acquired") is false
            && e.State.GetValueOrDefault("WaitMilliseconds") is double wait && wait > 1_000)
            .ShouldBeTrue();
        body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("workspace_admission_busy");
        (await db.WorkspaceUseReservations.AnyAsync()).ShouldBeFalse();

        await transaction.RollbackAsync();
        var recovered = await admission.RequireConsumerAsync(command, default);
        var reservation = await db.WorkspaceUseReservations.SingleAsync();
        reservation.Id.ShouldBe(recovered.Id);
        reservation.Active.ShouldBeTrue();
    }
}
