extern alias server;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using System.Diagnostics;
using System.Net;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Serilog.Core;
using Serilog.Events;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Observability.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveStartupIntegrationTests
{
    [Test]
    public async Task V9_Real_server_reports_startup_phases_after_listeners_are_ready(CancellationToken cancellationToken)
    {
        // Startup is the subject: one owned container/host per selected run, no shared live resources.
        var clock = Stopwatch.StartNew();
        await using var container = new PostgreSqlBuilder().WithImage("postgres:16-alpine")
            .WithDatabase("antiphon_c1564").WithUsername("test").WithPassword("test").Build();
        await container.StartAsync(cancellationToken);
        Console.WriteLine($"C1564 fixture-setup container-ready elapsedMs={clock.Elapsed.TotalMilliseconds}");
        clock.Restart();
        var root = Path.Combine(Path.GetTempPath(), "c1564-host-" + Guid.NewGuid().ToString("N"));
        using var sink = new DisposalWitnessSink(root);
        await using var factory = new StartupFactory(container.GetConnectionString(), root, sink);
        using var http = factory.CreateClient();
        using var response = await http.GetAsync("/api/diagnostics/startup", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Console.WriteLine("C1564 startup-observation " + body.RootElement.GetRawText());
        var rows = body.RootElement.GetProperty("phases").EnumerateArray().ToArray();
        var operations = rows.Select(x => x.GetProperty("operation").GetString()).ToArray();
        foreach (var operation in new[] { "config-load", "host-build", "migrate", "seed", "setup", "host-start", "listeners-ready" })
            operations.ShouldContain(operation);
        foreach (var row in rows)
        {
            row.GetProperty("elapsedMs").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
            row.GetProperty("outcome").GetString().ShouldBe("completed");
        }
        // The host keeps the established refusing runner; startup cannot launch a live session.
        body.RootElement.GetProperty("runId").GetString().ShouldNotBeNullOrWhiteSpace();
        factory.Runner.LaunchAttempts.ShouldBeEmpty();
        Console.WriteLine($"C1564 host-startup observed elapsedMs={clock.Elapsed.TotalMilliseconds}");
    }

    private sealed class RefusingProducer : IAntiphonMessagingProducer
    {
        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Startup timing fixture refuses outbound messaging");
    }

    // An explicit order assertion exposes the Windows sharing regression on Linux,
    // where deleting the directory alone would silently unlink an open stream.
    private sealed class DisposalWitnessSink : ILogEventSink, IDisposable
    {
        private readonly FileStream _stream;
        public bool Disposed { get; private set; }
        public int Events { get; private set; }

        public DisposalWitnessSink(string root)
        {
            Directory.CreateDirectory(root);
            _stream = new FileStream(Path.Combine(root, "disposal-witness.log"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }

        public void Emit(LogEvent logEvent) => Events++;
        public void Dispose()
        {
            _stream.Dispose();
            Disposed = true;
        }
    }

    private sealed class StartupFactory(string connection, string root, DisposalWitnessSink sink) : WebApplicationFactory<server::Program>
    {
        public RefusingSessionRunnerClient Runner { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(root);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["Git:WorkspacePath"] = root,
                ["Git:WorktreeBasePath"] = Path.Combine(root, "worktrees"),
                ["AgentTui:KeyRingPath"] = Path.Combine(root, "keys"),
                ["AgentTui:ImportProfilesOnStartup"] = "false",
                ["GitHub:Enabled"] = "false",
                ["ChannelBridge:Enabled"] = "false",
                ["Agents:DefaultDefinition"] = "test-raw",
                ["Agents:Definitions:test-raw:Kind"] = "Raw",
                ["Agents:Definitions:test-raw:Exe"] = "unused-test-executable",
                ["SessionRunner:BaseUrl"] = ProductionRunnerGuard.DeadRunnerBaseUrl,
                ["SessionRunner:Enabled"] = "false",
                ["Delegation:CheckInterpreterEnabled"] = "false",
                ["Delegation:DiagnoseEnabled"] = "false",
                ["Delegation:OutputDistillerEnabled"] = "false",
                ["Hangfire:ServerEnabled"] = "false",
                ["Serilog:LogPath"] = Path.Combine(root, "logs"),
                ["Serilog:ConsoleMinimumLevel"] = "Warning",
                [OperatorTokenFile.PathKey] = Path.Combine(root, "operator-token")
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILogEventSink>(sink);
                services.RemoveAll<IAntiphonMessagingProducer>();
                services.AddSingleton<IAntiphonMessagingProducer>(new RefusingProducer());
                services.RemoveAll<ISessionRunnerClient>();
                services.AddSingleton<ISessionRunnerClient>(Runner);
                services.RemoveAll<IZombieProcessCensus>();
                services.AddSingleton<IZombieProcessCensus>(new RefusingZombieProcessCensus());
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection));
            });
        }
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            sink.Events.ShouldBeGreaterThan(0, "the server logger must reach the held sink");
            sink.Disposed.ShouldBeTrue("the host logger must be disposed before directory deletion is attempted");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
