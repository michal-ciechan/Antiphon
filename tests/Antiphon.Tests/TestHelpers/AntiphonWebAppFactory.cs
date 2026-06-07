using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Shared <see cref="WebApplicationFactory{TEntryPoint}"/> for HTTP-level integration tests.
/// Boots the real application over the in-memory TestServer, backed by the shared PostgreSQL
/// testcontainer from <see cref="TestDbFixture"/> (one container per assembly).
///
/// Inject via <c>[ClassDataSource&lt;AntiphonWebAppFactory&gt;(Shared = SharedType.PerTestSession)]</c>
/// so a single host is built once and reused across every test in the session — booting a
/// factory per test is expensive. Because the host is shared, singletons (notably the
/// <c>DirectoryBrowseService</c> cache) live across tests; call <see cref="ResetAsync"/> from
/// <c>[Before(Test)]</c> to clear all <see cref="IResettableCache"/> instances so no state
/// leaks from one test into the next.
///
/// Subclass and override <see cref="ApplyTestOverrides"/> to swap real dependencies for fakes
/// (see <see cref="MockedFileSystemWebAppFactory"/>). The database is always the real
/// testcontainer because the app runs EF migrations on startup, which the in-memory provider
/// cannot apply.
/// </summary>
public class AntiphonWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _workspacePath =
        Path.Combine(Path.GetTempPath(), "antiphon-waf", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_workspacePath);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDbFixture.ConnectionString,
                ["Git:WorkspacePath"] = _workspacePath,
                ["Git:WorktreeBasePath"] = Path.Combine(_workspacePath, "worktrees"),
                ["GitHub:Enabled"] = "false",
                ["Agents:DefaultDefinition"] = "test-raw",
                ["Agents:Definitions:test-raw:Kind"] = "Raw",
                ["Agents:Definitions:test-raw:Exe"] = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            });
        });

        builder.ConfigureServices(services =>
        {
            // Health checks may probe external resources; not needed for API tests.
            services.Configure<HealthCheckServiceOptions>(o => o.Registrations.Clear());

            // Point EF at the shared testcontainer regardless of how the app wired it.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                }));

            ApplyTestOverrides(services);
        });
    }

    /// <summary>Override to replace real dependencies with fakes. Runs after the base service config.</summary>
    protected virtual void ApplyTestOverrides(IServiceCollection services) { }

    /// <summary>
    /// Clears every in-memory cache (<see cref="IResettableCache"/>) so the shared host does not
    /// leak state between tests. Override to also reset subclass fakes (call <c>base.ResetAsync()</c>).
    /// Call from <c>[Before(Test)]</c>.
    /// </summary>
    public virtual Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        foreach (var cache in scope.ServiceProvider.GetServices<IResettableCache>())
            cache.Clear();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_workspacePath))
        {
            try { Directory.Delete(_workspacePath, recursive: true); }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }
}
