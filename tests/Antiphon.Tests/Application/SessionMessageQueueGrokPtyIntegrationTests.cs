using System.Runtime.InteropServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// Same capstone as <see cref="SessionMessageQueuePtyIntegrationTests"/>, against FakeGrok:
/// a queued message must submit through the real runtime → runner → ConPTY path.
/// </summary>
[Category("Integration")]
[NotInParallel("Headed")]
public class SessionMessageQueueGrokPtyIntegrationTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeGrokExe =>
        Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.exe");

    private const string PinnedBackend = "inbox";

    [Test]
    public async Task Queued_message_submits_through_the_real_runtime_runner_pty_path()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeGrokExe))
            throw new SkipTestException($"fakegrok.exe not staged at {FakeGrokExe} — build the solution first");

        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-fake-grok-pty-{Guid.NewGuid():N}");
        var client = new DirectSessionRunnerClient(sessionLogPath, ptyBackend: PinnedBackend);

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings()));
        services.AddSingleton<ISessionRunnerClient>(client);
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        var sessionId = Guid.NewGuid();
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-fake-grok-cwd-{sessionId:N}");
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-fake-grok-home-{sessionId:N}");
        Directory.CreateDirectory(cwd);

        var spec = new AgentLaunchSpec(
            DefinitionName: "fakegrok",
            Kind: AgentKind.Grok,
            Exe: FakeGrokExe,
            Args: ["--session-id", sessionId.ToString("D"), "--cwd", cwd],
            Env: new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
            Cwd: cwd,
            Cols: 120,
            Rows: 30);

        try
        {
            await client.StartAsync(sessionId, spec, CancellationToken.None);

            var ready = await WaitForRawAsync(client, sessionId, s => s.Contains("Fake Grok ready"), TimeSpan.FromSeconds(15));
            ready.ShouldBeTrue("fake Grok should reach readiness");

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId,
                    CardId = null,
                    DefinitionName = "fakegrok",
                    AgentKind = AgentKind.Grok,
                    Status = SessionStatus.Running,
                    Cwd = cwd,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = now,
                    StartedAt = now,
                    LastSeenAt = now,
                });
                await db.SaveChangesAsync();
            }

            var queue = provider.GetRequiredService<SessionMessageQueueService>();
            await queue.EnqueueAsync(sessionId, "queued hello", MessageSendMode.Now, CancellationToken.None);

            var submitted = await WaitForRawAsync(
                client, sessionId, s => s.Contains("SUBMITTED:queued hello"), TimeSpan.FromSeconds(10));
            submitted.ShouldBeTrue("a queued message must submit through the real runtime -> runner -> PTY path");
        }
        finally
        {
            try { await client.KillAsync(sessionId, CancellationToken.None); } catch { /* best effort */ }
            await client.DisposeAsync();
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            try { Directory.Delete(cwd, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(grokHome, recursive: true); } catch { /* best effort */ }
        }
    }

    private static async Task<bool> WaitForRawAsync(
        ISessionRunnerClient client,
        Guid sessionId,
        Func<string, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = await client.GetSnapshotAsync(sessionId, CancellationToken.None);
            if (predicate(snapshot.RawOutput ?? string.Empty))
                return true;
            await Task.Delay(150);
        }

        return false;
    }
}
