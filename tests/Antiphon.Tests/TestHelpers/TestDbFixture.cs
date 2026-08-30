using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TUnit.Core;
using Antiphon.Server.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Shared PostgreSQL testcontainer fixture. One container per test session.
/// Shared-store tests use <see cref="TransactionalTestBase"/> against <c>antiphon_test</c>.
/// Isolated consumers get a cloned database via <see cref="CreateIsolatedSchemaAsync"/> —
/// CARD-0110 S2 migrates a template once per assembly, then
/// <c>CREATE DATABASE … TEMPLATE</c> (~100–300 ms) instead of replaying every EF migration.
/// </summary>
public class TestDbFixture
{
	internal const string TemplateDatabaseName = "antiphon_tmpl";
	internal const string SharedDatabaseName = "antiphon_test";

	private static readonly Regex SafeDatabaseName = new("^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant);
	private static readonly SemaphoreSlim CloneLock = new(1, 1);

	private static readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
		.WithDatabase(SharedDatabaseName)
		.WithUsername("test")
		.WithPassword("test")
		.Build();

	public static string ConnectionString => _container.GetConnectionString();

	internal static string MaintenanceConnectionString =>
		new NpgsqlConnectionStringBuilder(ConnectionString)
		{
			Database = "postgres",
			Pooling = false
		}.ConnectionString;

	[Before(Assembly)]
	public static async Task InitializeAsync()
	{
		await _container.StartAsync();

		// Migrate the shared database once; isolated clones copy this state via TEMPLATE.
		var options = CreateDbContextOptions();
		await using (var context = new AppDbContext(options))
		{
			await context.Database.MigrateAsync();
		}

		// CREATE DATABASE … TEMPLATE requires the source to have no other sessions.
		NpgsqlConnection.ClearAllPools();
		await using var maintenance = new NpgsqlConnection(MaintenanceConnectionString);
		await maintenance.OpenAsync();
		await TerminateBackendsAsync(maintenance, SharedDatabaseName);

		await ExecuteNonQueryAsync(
			maintenance,
			$"CREATE DATABASE {TemplateDatabaseName} TEMPLATE {SharedDatabaseName}");
		// Nobody may connect to the template, including a stray pooled connection, or clones fail
		// with "source database is being accessed by other users".
		await ExecuteNonQueryAsync(
			maintenance,
			$"ALTER DATABASE {TemplateDatabaseName} IS_TEMPLATE true");
		await ExecuteNonQueryAsync(
			maintenance,
			$"ALTER DATABASE {TemplateDatabaseName} ALLOW_CONNECTIONS false");
	}

	[After(Assembly)]
	public static async Task DisposeAsync()
	{
		await _container.DisposeAsync();
	}

	public static DbContextOptions<AppDbContext> CreateDbContextOptions(string? connectionString = null)
	{
		return new DbContextOptionsBuilder<AppDbContext>()
			.UseNpgsql(connectionString ?? ConnectionString, npgsql =>
			{
				npgsql.MigrationsAssembly("Antiphon.Server");
				npgsql.SetPostgresVersion(16, 0);
			})
			.Options;
	}

	/// <summary>
	/// Returns a connection string to an empty, fully-migrated store. Isolation is a cloned
	/// database (<c>Database=test_…</c>), not a <c>SearchPath=</c> schema on the shared database.
	/// </summary>
	public static async Task<IsolatedTestSchema> CreateIsolatedSchemaAsync()
	{
		var databaseName = $"test_{Guid.NewGuid():N}";
		try
		{
			await CloneDatabaseAsync(databaseName);
			var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
			{
				Database = databaseName
			}.ConnectionString;
			return new IsolatedTestSchema(databaseName, connectionString);
		}
		catch
		{
			await DropClonedDatabaseAsync(databaseName);
			throw;
		}
	}

	internal static async Task DropClonedDatabaseAsync(string databaseName)
	{
		ValidateDatabaseName(databaseName);
		var clonedConnectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
		{
			Database = databaseName
		}.ConnectionString;
		NpgsqlConnection.ClearPool(new NpgsqlConnection(clonedConnectionString));

		await using var maintenance = new NpgsqlConnection(MaintenanceConnectionString);
		await maintenance.OpenAsync();
		await ExecuteNonQueryAsync(
			maintenance,
			$"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)");
	}

	private static async Task CloneDatabaseAsync(string databaseName)
	{
		ValidateDatabaseName(databaseName);
		await CloneLock.WaitAsync();
		try
		{
			const int maxAttempts = 5;
			for (var attempt = 1; attempt <= maxAttempts; attempt++)
			{
				try
				{
					await using var maintenance = new NpgsqlConnection(MaintenanceConnectionString);
					await maintenance.OpenAsync();
					await TerminateBackendsAsync(maintenance, TemplateDatabaseName);
					await ExecuteNonQueryAsync(
						maintenance,
						$"CREATE DATABASE {databaseName} TEMPLATE {TemplateDatabaseName}");
					return;
				}
				catch (PostgresException ex) when (
					ex.SqlState == PostgresErrorCodes.ObjectInUse && attempt < maxAttempts)
				{
					await Task.Delay(50 * attempt);
				}
			}

			throw new InvalidOperationException(
				$"Failed to clone '{databaseName}' from template '{TemplateDatabaseName}'.");
		}
		finally
		{
			CloneLock.Release();
		}
	}

	private static void ValidateDatabaseName(string databaseName)
	{
		if (!SafeDatabaseName.IsMatch(databaseName))
			throw new ArgumentException($"Unsafe database name '{databaseName}'.", nameof(databaseName));
	}

	private static async Task TerminateBackendsAsync(NpgsqlConnection connection, string databaseName)
	{
		await using var command = new NpgsqlCommand(
			"""
			SELECT pg_terminate_backend(pid)
			FROM pg_stat_activity
			WHERE datname = @db AND pid <> pg_backend_pid()
			""",
			connection);
		command.Parameters.AddWithValue("db", databaseName);
		await command.ExecuteNonQueryAsync();
	}

	private static async Task ExecuteNonQueryAsync(NpgsqlConnection connection, string sql)
	{
		await using var command = new NpgsqlCommand(sql, connection)
		{
			CommandTimeout = 60
		};
		await command.ExecuteNonQueryAsync();
	}

	public AppDbContext CreateDbContext()
	{
		return new AppDbContext(CreateDbContextOptions());
	}
}

/// <summary>
/// A migrated, disposable PostgreSQL database scoped to one consumer (a clone of the assembly
/// template). The type name is the historical contract; isolation is per-database, not per-schema.
/// </summary>
public sealed class IsolatedTestSchema : IAsyncDisposable
{
	private readonly string _databaseName;

	internal IsolatedTestSchema(string databaseName, string connectionString)
	{
		_databaseName = databaseName;
		ConnectionString = connectionString;
	}

	public string ConnectionString { get; }

	public async ValueTask DisposeAsync()
	{
		await TestDbFixture.DropClonedDatabaseAsync(_databaseName);
	}
}

/// <summary>
/// Base class for tests that need database access with transaction rollback isolation.
/// Each test runs inside a transaction that is rolled back on dispose.
/// </summary>
public abstract class TransactionalTestBase
{
	private readonly TestDbFixture _fixture;
	protected AppDbContext DbContext { get; private set; } = null!;

	protected TransactionalTestBase(TestDbFixture fixture)
	{
		_fixture = fixture;
	}

	[Before(Test)]
	public async Task SetupAsync()
	{
		DbContext = _fixture.CreateDbContext();
		// Begin a transaction that will be rolled back after each test
		await DbContext.Database.BeginTransactionAsync();
	}

	[After(Test)]
	public async Task TeardownAsync()
	{
		if (DbContext.Database.CurrentTransaction is not null)
		{
			await DbContext.Database.RollbackTransactionAsync();
		}
		await DbContext.DisposeAsync();
	}
}
