using Microsoft.Extensions.Configuration;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class PostgresPoolConfigurationTests
{
    [Test]
    public void Every_shipped_server_connection_disables_pool_reset()
    {
        foreach (var relative in new[] { "server/appsettings.json", "Antiphon.AppHost/appsettings.json" })
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(DockerStackDocuments.RepoRoot, relative))
                .Build();
            var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
            connection.NoResetOnClose.ShouldBeTrue($"{relative} must configure the server pool");
        }

        var composeServer = DockerStackDocuments.Service(DockerStackDocuments.Read("docker-compose.yml"), "antiphon");
        var composeConnection = new NpgsqlConnectionStringBuilder(
            DockerStackDocuments.Env(composeServer, "ConnectionStrings__DefaultConnection"));
        composeConnection.NoResetOnClose.ShouldBeTrue("the standalone server pool must be configured");
    }
}
