using Antiphon.DockerStack.Fixture;
using Antiphon.Tests.TestHelpers;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
public sealed class DockerDeliveryDatabaseTests
{
    [Test]
    public async Task Observer_finds_sent_row_after_empty_post_response() =>
        await WithSchema(async connection =>
        {
            var session = Guid.NewGuid();
            await InsertAsync(connection, session, "Sent", "marked-body", attempts: 1, floor: 3);
            var reader = new QueueObservationReader();
            var found = await SelectAsync(connection, reader, session, 0, "marked-body");
            found.ShouldBe(1);
        });

    [Test]
    public async Task Observer_role_refuses_writes() =>
        await WithSchema(async connection =>
        {
            await ExecuteAsync(connection, "DO $$ BEGIN CREATE ROLE c590_observer; EXCEPTION WHEN duplicate_object THEN NULL; END $$;");
            await ExecuteAsync(connection, "GRANT SELECT ON \"SessionQueuedMessages\" TO c590_observer");
            await using var restricted = new NpgsqlConnection(connection.ConnectionString);
            await restricted.OpenAsync();
            await ExecuteAsync(restricted, "SET ROLE c590_observer");
            var failed = false;
            try
            {
                await ExecuteAsync(restricted, "INSERT INTO \"SessionQueuedMessages\" (\"Id\") VALUES ('00000000-0000-0000-0000-000000000099')");
            }
            catch (PostgresException)
            {
                failed = true;
            }
            failed.ShouldBeTrue();
        });

    [Test]
    public async Task Observer_snapshot_is_coherent() =>
        await WithSchema(async connection =>
        {
            var session = Guid.NewGuid();
            await InsertAsync(connection, session, "Sent", "body", 1, 4);
            var count = await ScalarAsync(connection, "SELECT COUNT(*) FROM \"SessionQueuedMessages\" WHERE \"AgentSessionId\" = @s", session);
            count.ShouldBe(1);
        });

    [Test]
    public async Task Observer_lookup_rejects_duplicate_rows() =>
        await WithSchema(async connection =>
        {
            var session = Guid.NewGuid();
            await InsertAsync(connection, session, "Sent", "same", 1, 1);
            await InsertAsync(connection, session, "Pending", "same", 0, null);
            var reader = new QueueObservationReader();
            (await SelectAsync(connection, reader, session, 0, "same")).ShouldBe(2);
            var rows = new[] { Row(session, "same"), Row(session, "same") };
            DeliveryEvidenceValidator.SelectSingle(rows, null).Code.ShouldBe("AmbiguousQueueRows");
        });

    [Test]
    public async Task Insert_reached_is_visible_from_second_connection() =>
        await WithSchema(async connection =>
        {
            var session = Guid.NewGuid();
            await InsertAsync(connection, session, "Pending", "body", 0, null);
            await using var second = new NpgsqlConnection(connection.ConnectionString);
            await second.OpenAsync();
            (await ScalarAsync(second, "SELECT COUNT(*) FROM \"SessionQueuedMessages\" WHERE \"Body\" = 'body' AND \"AgentSessionId\" = @s", session)).ShouldBe(1);
        });

    [Test]
    public async Task Attempt_reached_is_visible_from_second_connection() =>
        await WithSchema(async connection =>
        {
            var session = Guid.NewGuid();
            await InsertAsync(connection, session, "Sent", "body", 1, 8);
            await using var second = new NpgsqlConnection(connection.ConnectionString);
            await second.OpenAsync();
            (await ScalarAsync(second, "SELECT \"DeliveryAttempts\" FROM \"SessionQueuedMessages\" WHERE \"AgentSessionId\" = @s", session)).ShouldBe(1);
        });

    [Test]
    public async Task Open_outer_transaction_never_reaches_committed_cut() =>
        await WithSchema(async connection =>
        {
            await using var tx = await connection.BeginTransactionAsync();
            var session = Guid.NewGuid();
            await InsertAsync(connection, session, "Pending", "hidden", 0, null, tx);
            await using var second = new NpgsqlConnection(connection.ConnectionString);
            await second.OpenAsync();
            (await ScalarAsync(second, "SELECT COUNT(*) FROM \"SessionQueuedMessages\" WHERE \"Body\" = 'hidden' AND \"AgentSessionId\" = @s", session)).ShouldBe(0);
            await tx.RollbackAsync();
        });

    [Test]
    public async Task Failed_save_emits_no_reached_record()
    {
        var interceptor = new OrdinaryDeliverySaveInterceptor();
        interceptor.OnSaveFailed(null);
        interceptor.Reached.ShouldBeEmpty();
    }

    [Test]
    public async Task Concurrent_context_saves_do_not_cross_observe() =>
        await WithSchema(async connection =>
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            await InsertAsync(connection, a, "Sent", "alpha", 1, 1);
            await InsertAsync(connection, b, "Sent", "beta", 1, 1);
            var reader = new QueueObservationReader();
            (await SelectAsync(connection, reader, a, 0, "beta")).ShouldBe(0);
            (await SelectAsync(connection, reader, b, 0, "beta")).ShouldBe(1);
        });

    [Test]
    public async Task Verdict_hold_leaves_committed_verdict_null() =>
        await WithSchema(async connection =>
        {
            var session = Guid.NewGuid();
            await InsertAsync(connection, session, "Sent", "body", 1, 2);
            (await ScalarAsync(connection, "SELECT COUNT(*) FROM \"SessionQueuedMessages\" WHERE \"AgentSessionId\" = @s AND \"DeliveryVerdict\" IS NULL", session)).ShouldBe(1);
        });

    [Test]
    public async Task Runtime_batch_individual_and_stub_saves_remain_faulted()
    {
        var interceptor = new OrdinaryDeliverySaveInterceptor { FaultArmed = true, FaultUuid = "u" };
        Should.Throw<Microsoft.EntityFrameworkCore.DbUpdateException>(() => interceptor.FaultSnapshots(
            [new EntityStateSnapshot("TranscriptEntry", Microsoft.EntityFrameworkCore.EntityState.Added, "u", "UserPrompt")]));
        Should.Throw<Microsoft.EntityFrameworkCore.DbUpdateException>(() => interceptor.FaultSnapshots(
            [new EntityStateSnapshot("TranscriptEntry", Microsoft.EntityFrameworkCore.EntityState.Added, "u", "stub")]));
    }

    [Test]
    public async Task Sse_and_pull_join_same_receipt_gate()
    {
        var session = Guid.NewGuid();
        var gate = new DeliveryGate { TargetSession = session, ArmedCut = "recipient-before-ingestion" };
        var inner = new RecordingRunnerClient();
        var client = new OrdinaryDeliveryRunnerClient(inner, gate);
        var pull = client.GetTranscriptAsync(session, CancellationToken.None);
        await Task.Delay(20);
        pull.IsCompleted.ShouldBeFalse();
        gate.Release();
        await pull;
    }

    [Test]
    public async Task Buffered_response_waits_after_endpoint_completion()
    {
        var barrier = new OrdinaryDeliveryResponseBarrier { Armed = true };
        var response = new HoldingResponse();
        var write = barrier.WriteAsync(response, 200, "h", [1], CancellationToken.None);
        await Task.Delay(20);
        response.Body.ShouldBeEmpty();
        barrier.Release();
        await write;
    }

    [Test]
    public void Observe_mode_does_not_boot_Program()
    {
        var parsed = Antiphon.DockerStack.Fixture.Program.TryParseObserve(["observe", "--identity", "missing"], out var path, out _);
        parsed.ShouldBeTrue();
        path.ShouldBe("missing");
        Antiphon.DockerStack.Fixture.Program.Observe(["observe", "--identity", Path.Combine(Path.GetTempPath(), "c590-missing-identity.json")]).ShouldBe(2);
    }

    [Test]
    public async Task Serve_keeps_migrations_health_and_real_runner() =>
        await WithSchema(async connection =>
        {
            (await ScalarAsync(connection, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"", Guid.Empty)).ShouldBeGreaterThan(0);
        });

    [Test]
    public async Task Restarted_observer_uses_frozen_identity() =>
        await WithSchema(async connection =>
        {
            var session = Guid.NewGuid();
            var id = await InsertAsync(connection, session, "Sent", "frozen", 1, 5);
            var again = await ScalarAsync(connection, "SELECT COUNT(*) FROM \"SessionQueuedMessages\" WHERE \"Id\" = @s", id);
            again.ShouldBe(1);
        });

    private static async Task WithSchema(Func<NpgsqlConnection, Task> body)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var connection = new NpgsqlConnection(schema.ConnectionString);
        await connection.OpenAsync();
        await body(connection);
    }

    private static async Task<Guid> InsertAsync(
        NpgsqlConnection connection, Guid session, string status, string body, int attempts, long? floor, NpgsqlTransaction? tx = null)
    {
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO "SessionQueuedMessages"
                ("Id", "AgentSessionId", "Body", "Status", "Sequence", "Origin", "DeliveryAttempts",
                 "LastDeliveryBaselineSequence", "CreatedAt", "MaintenanceKind", "MaintenanceSlotActive", "RulesFollowOnCount")
            VALUES
                (@id, @session, @body, @status, 2, 0, @attempts, @floor, NOW(), 0, false, 0)
            """, connection, tx);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("session", session);
        command.Parameters.AddWithValue("body", body);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("attempts", attempts);
        command.Parameters.AddWithValue("floor", (object?)floor ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<int> SelectAsync(NpgsqlConnection connection, QueueObservationReader reader, Guid session, long highWater, string body)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM "SessionQueuedMessages"
            WHERE "AgentSessionId" = @session AND "Sequence" > @high AND "Body" = @body
            """, connection);
        command.Parameters.AddWithValue("session", session);
        command.Parameters.AddWithValue("high", highWater);
        command.Parameters.AddWithValue("body", body);
        _ = reader.Sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<int> ScalarAsync(NpgsqlConnection connection, string sql, Guid session)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        if (sql.Contains("@s", StringComparison.Ordinal))
            command.Parameters.AddWithValue("s", session);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static QueueCandidate Row(Guid session, string body) =>
        new(Guid.NewGuid(), session, 2, "Sent", body, "Ui", null, null, null, "None", 1, 1, DateTime.UnixEpoch, DateTime.UnixEpoch, null);
}
