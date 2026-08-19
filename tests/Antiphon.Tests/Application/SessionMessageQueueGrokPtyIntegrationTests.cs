using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
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
[ParallelLimiter<ProcessSpawnLimit>]
public class SessionMessageQueueGrokPtyIntegrationTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeGrokExe =>
        Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.exe");

    private const string PinnedBackend = "inbox";

    /// <summary>
    /// The backend this deployment actually runs (<c>SessionRunner:PtyBackend</c>), and the only one
    /// on which a delegate brief is large enough to be typed inline at all — see
    /// <see cref="A_grok_brief_spills_and_its_pointer_survives_the_join_and_confirms"/>.
    /// </summary>
    private const string ModernBackend = "modern";

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
    /// CARD-0084 S1's capstone: a delegate BRIEF for a Grok session takes the spill path, and the
    /// pointer that replaces it survives the composer's join — through the real queue → runtime →
    /// runner → ConPTY path into fakegrok.
    ///
    /// <para>Three things are proved together, because separately none of them is the property that
    /// matters. (1) The gate SPILLS: the same brief on the same backend is typed inline for a Claude
    /// delegate — 43 200 bytes of headroom — so before this slice every Grok brief was typed whole
    /// and arrived run-on. (2) The pointer that IS typed is already one line, so Grok's newline-drop
    /// is a no-op on it and the spill path keeps a delimiter between itself and the sentence after
    /// it: the fusion this prevents is <c>…task-xxxx-brief.mdEverything you need is there</c>, a
    /// path a delegate cannot open. (3) The delivery still ends <c>Sent</c> — a join-safe rendering
    /// that broke transcript confirmation would trade a readable brief for a killed session.</para>
    ///
    /// <para><b>Why this one pins <see cref="ModernBackend"/> while its neighbours pin the inbox
    /// conhost.</b> Two reasons, and the first is the point of the test: the exposure S1 closes only
    /// exists on the modern backend, whose 43 200-byte inline ceiling is what let a whole brief be
    /// typed — on the inbox conhost every brief already spills at 900 bytes, so an "it spilled"
    /// assertion there would pass with the fix reverted. The second is a measured property of the
    /// inbox binary (2026-08-18): it narrows non-ASCII TYPED input to one byte per char before a
    /// .NET peer, and an em-dash — which the pointer's own prose carries — arrives as <c>U+0000</c>.
    /// That breaks the head-window text match, so the delivery could never confirm, and Postgres
    /// rejects the NUL outright. On the modern backend the same em-dash arrives as <c>U+2014</c>.
    /// It is an artefact of a .NET peer reading the inbox pty, not something a real Grok sees — and
    /// the deployment this ships to runs <c>modern</c> anyway.</para>
    /// </summary>
    [Test]
    public async Task A_grok_brief_spills_and_its_pointer_survives_the_join_and_confirms()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeGrokExe))
            throw new SkipTestException($"fakegrok.exe not staged at {FakeGrokExe} — build the solution first");

        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-fake-grok-pty-{Guid.NewGuid():N}");
        var client = new DirectSessionRunnerClient(sessionLogPath, ptyBackend: ModernBackend);

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

        // A brief with the structure a Code delegate's brief actually has: a command and a path that
        // mean what they say only while the line they sit on still ends where it was written to end.
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            Title = "CARD-0084 grok brief capstone",
            Goal = "HEAD-MARKER\n"
                + "run: dotnet run --project tests/Antiphon.Tests\n"
                + string.Join("\n", Enumerable.Range(0, 40).Select(i => $"step {i:D4}: do the thing"))
                + "\nTAIL-MARKER",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = cwd,
            Status = AgentTaskStatus.Dispatched,
        };
        var settings = new DelegationSettings();
        var ceilings = settings.CeilingsFor(PtyBackend.ModernConPty, "card-0084 capstone");
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var spill = Path.Combine(cwd, ".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md");

        try
        {
            await client.StartAsync(sessionId, new AgentLaunchSpec(
                DefinitionName: "fakegrok",
                Kind: AgentKind.Grok,
                Exe: FakeGrokExe,
                Args: ["--session-id", sessionId.ToString("D"), "--cwd", cwd],
                Env: new Dictionary<string, string> { ["GROK_HOME"] = grokHome },
                Cwd: cwd,
                Cols: 120,
                Rows: 30), CancellationToken.None);
            (await WaitForRawAsync(client, sessionId, s => s.Contains("Fake Grok ready"), TimeSpan.FromSeconds(15)))
                .ShouldBeTrue("fake Grok should reach readiness");

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

            // (1) The REAL gate, twice on the SAME backend — the only difference is whose composer is
            // on the other end.
            var forClaude = AgentTaskDispatcher.FitBriefForTyping(task, settings, ceilings, null, AgentKind.ClaudeCode);
            var typed = AgentTaskDispatcher.FitBriefForTyping(task, settings, ceilings, null, AgentKind.Grok);

            forClaude.ShouldContain("TAIL-MARKER",
                customMessage: "the backend alone would type this brief inline — which is the exposure");
            typed.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE",
                customMessage: "and for Grok the same brief must spill instead");
            File.Exists(spill).ShouldBeTrue($"the spilled brief must exist at {spill}");
            var spilled = await File.ReadAllTextAsync(spill);
            spilled.ShouldContain("HEAD-MARKER\nrun: dotnet run --project tests/Antiphon.Tests\n",
                customMessage: "the file keeps the structure the composer would have destroyed");
            spilled.ShouldContain("TAIL-MARKER");

            // (2) The pointer is already joined, so the composer has nothing left to take.
            typed.ShouldNotContain("\n");
            typed.ShouldStartWith(marker);
            typed.ShouldEndWith(marker);
            typed.ShouldContain($"'{spill}' Everything you need is there");

            pumping = PumpRunnerTranscriptAsync(client, sessionId, seedSequence: 1, pump.Token);

            var queue = provider.GetRequiredService<SessionMessageQueueService>();
            await queue.EnqueueAsync(
                sessionId, typed, MessageSendMode.WhenIdle, CancellationToken.None, QueuedMessageOrigin.Delegation);

            var snapshot = await client.GetSnapshotAsync(sessionId, CancellationToken.None);
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                // (3) Confirmed, not assumed: the pointer's own UserPrompt row landed.
                var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == sessionId);
                message.Status.ShouldBe(QueuedMessageStatus.Sent,
                    "a join-safe brief pointer must still be transcript-confirmed. Screen:\n" + snapshot.RawOutput);

                var prompt = await db.TranscriptEntries.SingleAsync(t =>
                    t.AgentSessionId == sessionId
                    && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt);
                var recorded = prompt.Text ?? string.Empty;

                // What Grok actually recorded is still parseable: the path is delimited from the
                // sentence after it, and correlation survives at both ends.
                recorded.ShouldContain(
                    $"task-{DelegationReportFormatter.Short(task.Id)}-brief.md' Everything you need is there",
                    customMessage: "the joined pointer must not fuse the spill path with the next line");
                recorded.ShouldContain(marker, customMessage: "settlement correlates on this marker");
                recorded.TrimEnd().ShouldEndWith(marker,
                    customMessage: "and the tail marker is the fragment that survives every measured loss");
                recorded.ShouldNotContain("TAIL-MARKER", customMessage: "the body itself was never typed");
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
