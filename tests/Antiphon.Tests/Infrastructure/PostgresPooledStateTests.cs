using Antiphon.Tests.TestHelpers;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
public sealed class PostgresPooledStateTests
{
    [Test]
    public async Task Transaction_local_setting_does_not_leak_across_a_reused_unreset_connection()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(TestDbFixture.ConnectionString)
        {
            NoResetOnClose = true,
            MaxPoolSize = 1
        }.ConnectionString;

        int firstBackend;
        string baseline;
        await using (var first = new NpgsqlConnection(connectionString))
        {
            await first.OpenAsync();
            firstBackend = first.ProcessID;
            baseline = await ReadSettingAsync(first);
            await using var transaction = await first.BeginTransactionAsync();
            await using var change = new NpgsqlCommand(
                "SELECT set_config('statement_timeout', '1234ms', true)", first, transaction);
            await change.ExecuteScalarAsync();
            (await ReadSettingAsync(first, transaction)).ShouldBe("1234ms");
            await transaction.CommitAsync();
        }

        await using var second = new NpgsqlConnection(connectionString);
        await second.OpenAsync();
        second.ProcessID.ShouldBe(firstBackend, "the test must observe reuse of the same physical connection");
        (await ReadSettingAsync(second)).ShouldBe(baseline);
    }

    private static async Task<string> ReadSettingAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand("SHOW statement_timeout", connection, transaction);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
