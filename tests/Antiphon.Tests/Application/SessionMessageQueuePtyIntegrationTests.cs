using System.Runtime.InteropServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// The capstone end-to-end test: a queued message must actually <em>submit</em> when driven through the
/// REAL production transport — <see cref="SessionMessageQueueService"/> → <see cref="AgentSessionRuntime"/>
/// → <see cref="DirectSessionRunnerClient"/> (a real in-process <c>SessionRunnerRuntime</c>) → a real
/// ConPTY → the fake Claude. The other layers prove the pieces (the service emits two writes; two writes
/// submit at the PTY level); this proves they compose — that the runtime + runner faithfully forward the
/// body and the submitting CR as two distinct PTY writes, preserving the 20ms gap, so the fake submits.
///
/// This is the test shape that would have caught the original bug outright. Needs Windows ConPTY, the
/// staged <c>fakeclaude.exe</c>, and the test Postgres (same as the other queue integration tests).
/// </summary>
[Category("Integration")]
[NotInParallel("Headed")]
public class SessionMessageQueuePtyIntegrationTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    [Test]
    public async Task Queued_message_submits_through_the_real_runtime_runner_pty_path()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-fake-pty-{Guid.NewGuid():N}");
        var client = new DirectSessionRunnerClient(sessionLogPath);

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
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-fake-cwd-{sessionId:N}");
        Directory.CreateDirectory(cwd);

        var spec = new AgentLaunchSpec(
            DefinitionName: "fakeclaude",
            Kind: AgentKind.ClaudeCode,
            Exe: FakeClaudeExe,
            Args: Array.Empty<string>(),
            Env: new Dictionary<string, string>(),
            Cwd: cwd,
            Cols: 120,
            Rows: 30);

        try
        {
            await client.StartAsync(sessionId, spec, CancellationToken.None);

            var ready = await WaitForRawAsync(client, sessionId, s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(15));
            ready.ShouldBeTrue("fake Claude should reach readiness");

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId,
                    CardId = null,
                    DefinitionName = "fakeclaude",
                    AgentKind = AgentKind.ClaudeCode,
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
        }
    }

    private static async Task<bool> WaitForRawAsync(
        DirectSessionRunnerClient client, Guid sessionId, Func<string, bool> predicate, TimeSpan timeout)
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
