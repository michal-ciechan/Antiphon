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

    /// <summary>
    /// CARD-0080 S2's capstone: a MULTI-LINE delivery to a Grok session ends <c>Sent</c> because
    /// its <c>UserPrompt</c> row appeared in the transcript — the row coming from the REAL
    /// <c>GrokTranscriptTailer</c> inside the real runtime, tailing the <c>updates.jsonl</c> that
    /// fakegrok (modelling measured grok 1.0.5) writes under GROK_HOME. This crosses every seam the
    /// slice touched: the launch request's TranscriptFormat, the deterministic-path tailer, the ACP
    /// normalizer, the queue's verify gate now including Grok, and the whitespace-free confirm arm
    /// — Grok drops every newline from pasted input, so the stored record is the JOINED body and
    /// the old spaced-only matcher would have failed this delivery.
    /// </summary>
    [Test]
    public async Task Multiline_delivery_is_transcript_confirmed_through_the_real_grok_tailer()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeGrokExe))
            throw new SkipTestException($"fakegrok.exe not staged at {FakeGrokExe} — build the solution first");

        const string body =
            "GROK-S2 HEAD confirm this multi-line channel reply\nsecond line of the reply body\nthird line TAIL";

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
        services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings
        {
            DeliveryVerification = new DeliveryVerificationSettings
            {
                PollIntervalMs = 200,
                TranscriptConfirmTimeoutSeconds = 25,
            },
        }));
        services.AddSingleton<ISessionRunnerClient>(client);
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        var sessionId = Guid.NewGuid();
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-fake-grok-cwd-{sessionId:N}");
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-fake-grok-home-{sessionId:N}");
        Directory.CreateDirectory(cwd);
        using var pump = new CancellationTokenSource();
        Task? pumping = null;

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
            (await WaitForRawAsync(client, sessionId, s => s.Contains("Fake Grok ready"), TimeSpan.FromSeconds(15)))
                .ShouldBeTrue("fake Grok should reach readiness");

            // An idle, transcript-OBSERVABLE Grok session: the seeded TurnEnd is both the idle
            // verdict and what keeps the CARD-0055 observability gate on the transcript path.
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, CardId = null, DefinitionName = "fakegrok",
                    AgentKind = AgentKind.Grok, Status = SessionStatus.Running,
                    Cwd = cwd, Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now,
                });
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 1,
                    Kind = Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd,
                    StopReason = "end_turn", CreatedAt = now,
                });
                await db.SaveChangesAsync();
            }

            // Stands in for SessionRunnerEventPump: mirror the REAL tailer's snapshot into
            // TranscriptEntries, which is what WaitForTranscriptConfirmAsync polls.
            pumping = PumpRunnerTranscriptAsync(client, sessionId, seedSequence: 1, pump.Token);

            var queue = provider.GetRequiredService<SessionMessageQueueService>();
            // WhenIdle rather than Now: the seeded TurnEnd reads idle so it delivers immediately,
            // and unlike Now-mode it persists the SessionQueuedMessages row whose Sent status IS
            // the transcript-confirmed verdict being asserted.
            await queue.EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

            var snapshot = await client.GetSnapshotAsync(sessionId, CancellationToken.None);
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == sessionId);
                message.Status.ShouldBe(QueuedMessageStatus.Sent,
                    "the delivery must be transcript-confirmed through the real Grok tailer. Screen:\n"
                    + snapshot.RawOutput);

                // Ground truth of the whole chain: the stored UserPrompt row is fakegrok's
                // user_message_chunk — the body with its newlines DROPPED, no separator (the
                // measured Grok composer contract) — normalized and persisted by the pump from the
                // real tailer's events.
                var prompt = await db.TranscriptEntries.SingleAsync(t =>
                    t.AgentSessionId == sessionId
                    && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt);
                prompt.Text.ShouldBe(body.Replace("\n", ""));
                prompt.Sequence.ShouldBeGreaterThan(1, "the confirming row sits past the delivery baseline");
            }
        }
        finally
        {
            pump.Cancel();
            if (pumping is not null)
                await pumping;
            try { await client.KillAsync(sessionId, CancellationToken.None); } catch { /* best effort */ }
            await client.DisposeAsync();
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId).ExecuteDeleteAsync();
                await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            }
            try { Directory.Delete(cwd, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(grokHome, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// The runner→server ingestion stand-in: poll the runtime's transcript snapshot (fed by the
    /// real <c>GrokTranscriptTailer</c>) and persist unseen entries as <c>TranscriptEntry</c> rows,
    /// offset past the seed row exactly the way arrival-ordered ingestion stacks new rows.
    /// </summary>
    private static async Task PumpRunnerTranscriptAsync(
        ISessionRunnerClient client, Guid sessionId, long seedSequence, CancellationToken ct)
    {
        long consumed = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var transcript = await client.GetTranscriptAsync(sessionId, CancellationToken.None);
                foreach (var entry in transcript.Entries.Where(e => e.Sequence > consumed).OrderBy(e => e.Sequence))
                {
                    await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
                    db.TranscriptEntries.Add(new TranscriptEntry
                    {
                        Id = Guid.NewGuid(),
                        AgentSessionId = sessionId,
                        Sequence = seedSequence + entry.Sequence,
                        Kind = entry.Kind,
                        Uuid = entry.Uuid,
                        Role = entry.Role,
                        Text = entry.Text,
                        StopReason = entry.StopReason,
                        Timestamp = entry.Timestamp?.UtcDateTime,
                        CreatedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync(CancellationToken.None);
                    consumed = entry.Sequence;
                }
            }
            catch (KeyNotFoundException)
            {
                // Session not registered yet / already gone — keep polling until cancelled.
            }

            try { await Task.Delay(100, ct); }
            catch (OperationCanceledException) { break; }
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
