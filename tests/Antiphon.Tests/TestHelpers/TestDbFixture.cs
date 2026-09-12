using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Shared PostgreSQL testcontainer fixture. One container per test session, started lazily
/// on the first default-store consumer (CARD-0476 S1). Shared-store tests use
/// <see cref="TransactionalTestBase"/> against <c>antiphon_test</c>. Isolated consumers get a
/// cloned database via <see cref="CreateIsolatedSchemaAsync"/>.
/// </summary>
public class TestDbFixture
{
	internal const string TemplateDatabaseName = "antiphon_tmpl";
	internal const string SharedDatabaseName = "antiphon_test";
	internal const string ProbeMarker = "ANTIPHON_C476_PROBE";

	internal static TestDbFixtureLifecycle Lifecycle { get; } = new();

	public static string ConnectionString => Lifecycle.ConnectionString;

	internal static string MaintenanceConnectionString => Lifecycle.MaintenanceConnectionString;

	[After(Assembly)]
	public static async Task DisposeAsync()
	{
		await Lifecycle.DisposeAsync();
		WriteProbeEvidence();
	}

	public static DbContextOptions<AppDbContext> CreateDbContextOptions(string? connectionString = null)
	{
		if (connectionString is not null)
			return TestDbFixtureLifecycle.BuildOptions(connectionString);
		return Lifecycle.CreateDbContextOptions();
	}

	/// <summary>
	/// Returns a connection string to an empty, fully-migrated store. Isolation is a cloned
	/// database (<c>Database=test_…</c>), not a <c>SearchPath=</c> schema on the shared database.
	/// </summary>
	public static Task<IsolatedTestSchema> CreateIsolatedSchemaAsync() =>
		Lifecycle.CreateIsolatedSchemaAsync();

	internal static Task DropClonedDatabaseAsync(string databaseName) =>
		Lifecycle.DropClonedDatabaseAsync(databaseName);

	public AppDbContext CreateDbContext() => Lifecycle.CreateDbContext();

	private static void WriteProbeEvidence()
	{
		var raw = Environment.GetEnvironmentVariable(ProbeMarker);
		if (string.IsNullOrEmpty(raw))
			return;

		string? root = null;
		try
		{
			using var doc = JsonDocument.Parse(raw);
			if (doc.RootElement.TryGetProperty("root", out var rootEl))
				root = rootEl.GetString();
		}
		catch (JsonException)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(root))
			return;

		Directory.CreateDirectory(root);
		var payload = new Dictionary<string, object?>
		{
			["state"] = Lifecycle.State,
			["create"] = Lifecycle.Create,
			["start"] = Lifecycle.Start,
			["migrate"] = Lifecycle.Migrate,
			["protect"] = Lifecycle.Protect,
			["disposeOwned"] = Lifecycle.DisposeOwned,
			["teardownDispose"] = Lifecycle.TeardownDispose,
			["containerId"] = Lifecycle.ContainerId,
			["runnerBaseUrl"] = Environment.GetEnvironmentVariable(ProductionRunnerGuard.BaseUrlEnvVar),
			["ptyBackend"] = Environment.GetEnvironmentVariable(Antiphon.Agents.Pty.PtyBackendPolicy.EnvVar),
			["mvid"] = typeof(TestDbFixture).Assembly.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture)
		};
		File.WriteAllText(
			Path.Combine(root, "lifecycle.json"),
			JsonSerializer.Serialize(payload));
	}
}

/// <summary>
/// A migrated, disposable PostgreSQL database scoped to one consumer (a clone of the assembly
/// template). The type name is the historical contract; isolation is per-database, not per-schema.
/// </summary>
public sealed class IsolatedTestSchema : IAsyncDisposable
{
	private readonly string _databaseName;
	private readonly TestDbFixtureLifecycle _lifecycle;

	internal IsolatedTestSchema(string databaseName, string connectionString)
		: this(databaseName, connectionString, TestDbFixture.Lifecycle)
	{
	}

	internal IsolatedTestSchema(string databaseName, string connectionString, TestDbFixtureLifecycle lifecycle)
	{
		_databaseName = databaseName;
		ConnectionString = connectionString;
		_lifecycle = lifecycle;
	}

	public string ConnectionString { get; }

	public async ValueTask DisposeAsync()
	{
		await _lifecycle.DropClonedDatabaseAsync(_databaseName);
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
