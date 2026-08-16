using Microsoft.EntityFrameworkCore;
using Npgsql;
using TUnit.Core;
using Antiphon.Server.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Shared PostgreSQL testcontainer fixture. One container per test session.
/// Each test gets transaction rollback isolation via <see cref="TransactionalTestBase"/>.
/// </summary>
public class TestDbFixture
{
	private static readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
		.WithImage("postgres:16-alpine")
		.WithDatabase("antiphon_test")
		.WithUsername("test")
		.WithPassword("test")
		.Build();

	public static string ConnectionString => _container.GetConnectionString();

	[Before(Assembly)]
	public static async Task InitializeAsync()
	{
		await _container.StartAsync();

		// Apply EF Core migrations to the test database
		var options = CreateDbContextOptions();
		await using var context = new AppDbContext(options);
		await context.Database.MigrateAsync();
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
	/// Creates an independently migrated schema in the shared test database. Use this when a test
	/// needs durable data and must not observe or alter data produced by another test class.
	/// </summary>
	public static async Task<IsolatedTestSchema> CreateIsolatedSchemaAsync()
	{
		var schemaName = $"test_{Guid.NewGuid():N}";
		await using (var connection = new NpgsqlConnection(ConnectionString))
		{
			await connection.OpenAsync();
			await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schemaName}\"", connection);
			await command.ExecuteNonQueryAsync();
		}

		var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
		{
			SearchPath = schemaName
		}.ConnectionString;

		try
		{
			await using var context = new AppDbContext(CreateDbContextOptions(connectionString));
			await context.Database.MigrateAsync();
			return new IsolatedTestSchema(schemaName, connectionString);
		}
		catch
		{
			await DropSchemaAsync(schemaName);
			throw;
		}
	}

	private static async Task DropSchemaAsync(string schemaName)
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand(
			$"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE", connection);
		await command.ExecuteNonQueryAsync();
	}

	public AppDbContext CreateDbContext()
	{
		return new AppDbContext(CreateDbContextOptions());
	}
}

/// <summary>
/// A migrated, disposable PostgreSQL schema scoped to one test.
/// </summary>
public sealed class IsolatedTestSchema : IAsyncDisposable
{
	private readonly string _schemaName;

	internal IsolatedTestSchema(string schemaName, string connectionString)
	{
		_schemaName = schemaName;
		ConnectionString = connectionString;
	}

	public string ConnectionString { get; }

	public async ValueTask DisposeAsync()
	{
		await using var connection = new NpgsqlConnection(TestDbFixture.ConnectionString);
		await connection.OpenAsync();
		await using var command = new NpgsqlCommand(
			$"DROP SCHEMA IF EXISTS \"{_schemaName}\" CASCADE", connection);
		await command.ExecuteNonQueryAsync();
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
