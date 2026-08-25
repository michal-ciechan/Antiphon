using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// <summary>
    /// This host's own migrated schema. It must NOT be the shared public one, because booting the
    /// real app is not a read-only act: Program.cs runs AgentTuiProfileImporter.ImportAsync at
    /// startup, which materialises a profile per configured agent definition and marks one the
    /// INSTALLATION DEFAULT. AgentService.CreateAsync then hands that default to every agent any
    /// other suite creates — its lookup is an unscoped SingleOrDefaultAsync(p => p.IsDefault) —
    /// and a harness that wires no AgentTuiLaunchResolver (AgentSupervisionTests, among others)
    /// then dies on ConflictException "The selected runner profile cannot be resolved by this
    /// installation." Whether that happened depended purely on whether this host had booted yet,
    /// so it moved with scheduling order rather than with any test's own behaviour.
    ///
    /// Lazy and blocking because ConfigureWebHost is synchronous and runs inside
    /// WebApplicationFactory.EnsureServer(); one schema per factory instance, one migration run.
    /// Subclasses that already own a schema override <see cref="ConnectionString"/> and this is
    /// never created.
    /// </summary>
    private readonly Lazy<IsolatedTestSchema> _schema = new(() =>
        TestDbFixture.CreateIsolatedSchemaAsync().GetAwaiter().GetResult());

    /// <summary>The database this host runs against. Override to supply your own schema.</summary>
    protected virtual string ConnectionString => _schema.Value.ConnectionString;

    private readonly string _workspacePath =
        Path.Combine(Path.GetTempPath(), "antiphon-waf", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// CARD-0204: the session-runner this host talks to. It refuses every launch and records the
    /// attempt, so a test can assert that booting the host started nothing anywhere. Replaces the
    /// HTTP client that used to reach the always-on production runner on 17204 — through which
    /// every boot of this factory launched a <c>cmd.exe</c> check interpreter that nothing ever
    /// stopped (187 of them on 2026-08-25). See <see cref="ProductionRunnerGuard"/>.
    /// </summary>
    public RefusingSessionRunnerClient SessionRunner { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_workspacePath);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Git:WorkspacePath"] = _workspacePath,
                ["Git:WorktreeBasePath"] = Path.Combine(_workspacePath, "worktrees"),
                ["AgentTui:KeyRingPath"] = Path.Combine(_workspacePath, "data-protection-keys"),
                // This host shares the assembly's schema with every service-level test. The
                // startup import's BackfillAgentsAsync stamps TuiProfileId onto EVERY agent row
                // with none, including rows a test that builds its own harness (no launch
                // resolver) is about to launch - which then fails with
                // "The selected runner profile cannot be resolved by this installation."
                // A factory on its own schema (AgentTuiApiWebAppFactory) turns it back on.
                ["AgentTui:ImportProfilesOnStartup"] = "false",
                ["GitHub:Enabled"] = "false",
                ["Agents:DefaultDefinition"] = "test-raw",
                ["Agents:Definitions:test-raw:Kind"] = "Raw",
                ["Agents:Definitions:test-raw:Exe"] = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                // CARD-0204: this host must not be able to reach the production session-runner,
                // and must not want to. The ProductionRunnerGuard env vars already say both for
                // every Program boot in the assembly; these are the same facts stated where the
                // factory's own definition lives, so removing the guard cannot silently re-arm
                // the leak. The event pump is off because the runner it would stream from does
                // not exist; the check interpreter is off because starting it is the launch that
                // leaked; its directory is under this host's own scratch so a test that turns it
                // back on still cannot land in C:\logs\antiphon\check-interpreter.
                ["SessionRunner:BaseUrl"] = ProductionRunnerGuard.DeadRunnerBaseUrl,
                ["SessionRunner:Enabled"] = "false",
                ["Delegation:CheckInterpreterEnabled"] = "false",
                ["Delegation:CheckInterpreterWorkingDirectory"] = Path.Combine(_workspacePath, "check-interpreter"),
            });
        });

        builder.ConfigureServices(services =>
        {
            // Health checks may probe external resources; not needed for API tests.
            services.Configure<HealthCheckServiceOptions>(o => o.Registrations.Clear());

            // CARD-0204: structural, not just configured — no code path in this host can start a
            // process on any runner, whatever the configuration says.
            services.RemoveAll<ISessionRunnerClient>();
            services.AddSingleton<ISessionRunnerClient>(SessionRunner);

            // Point EF at the shared testcontainer regardless of how the app wired it.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(ConnectionString, npgsql =>
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

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_schema.IsValueCreated)
            await _schema.Value.DisposeAsync();
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
