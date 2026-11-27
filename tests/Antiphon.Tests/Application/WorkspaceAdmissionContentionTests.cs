using System.Text.Json;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class WorkspaceAdmissionContentionTests
{
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
        var journal = new WorkspaceReservationJournal(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
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
            await admission.RequireConsumerAsync(command, context.RequestAborted);
        }, logger);
        var request = new DefaultHttpContext();
        using var body = new MemoryStream();
        request.Response.Body = body;
        await middleware.InvokeAsync(request);

        request.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable,
            logger.Entries.SingleOrDefault()?.Exception?.ToString());
        request.Response.Headers.RetryAfter.ToString().ShouldBe("2");
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
