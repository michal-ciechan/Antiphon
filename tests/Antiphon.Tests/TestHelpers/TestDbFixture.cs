using Microsoft.EntityFrameworkCore;
using Xunit;
using Antiphon.Server.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Shared PostgreSQL testcontainer fixture. One container per test collection.
/// Each test gets transaction rollback isolation via <see cref="TransactionalTestBase"/>.
/// </summary>
public class TestDbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("antiphon_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Apply EF Core migrations to the test database
        var options = CreateDbContextOptions();
        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public DbContextOptions<AppDbContext> CreateDbContextOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            })
            .Options;
    }

    public AppDbContext CreateDbContext()
    {
        return new AppDbContext(CreateDbContextOptions());
    }
}

/// <summary>
/// Base class for tests that need database access with transaction rollback isolation.
/// Each test runs inside a transaction that is rolled back on dispose.
/// </summary>
public abstract class TransactionalTestBase : IClassFixture<TestDbFixture>, IAsyncLifetime
{
    private readonly TestDbFixture _fixture;
    protected AppDbContext DbContext { get; private set; } = null!;

    protected TransactionalTestBase(TestDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        DbContext = _fixture.CreateDbContext();
        // Begin a transaction that will be rolled back after each test
        await DbContext.Database.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        if (DbContext.Database.CurrentTransaction is not null)
        {
            await DbContext.Database.RollbackTransactionAsync();
        }
        await DbContext.DisposeAsync();
    }
}
