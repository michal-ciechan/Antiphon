using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
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
[ParallelLimiter<ProcessSpawnLimit>]
public class SessionMessageQueuePtyIntegrationTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    /// <summary>
    /// CARD-0045: the backend these host-mediated sessions run on, declared rather than inherited.
    /// fakeclaude models the INBOX conhost's typed-input path (CARD-0028) — including the CR-vs-LF
    /// fragmentation this file exists to pin — so that is the pty these tests mean. It travels
    /// <c>DirectSessionRunnerClient</c> → <c>SessionRunnerSettings.PtyBackend</c> →
    /// <c>--pty-backend</c> → the detached host's <c>HostSession</c> (slice 3): before that seam the
    /// per-instance override could not reach a pty three processes down, and these tests ran on
    /// whatever the launching shell had exported.
    /// </summary>
    private const string PinnedBackend = "inbox";

    [Test]
    public async Task Queued_message_submits_through_the_real_runtime_runner_pty_path()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-fake-pty-{Guid.NewGuid():N}");
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

    /// <summary>
    /// CARD-0137: leftover overlay makes the next real body <c>NoComposerEvidence</c> without S5/S6.
    /// fakeclaude's overlay discards typed bytes until Esc. Grok is the Supported kind.
    /// </summary>
    [Test]
    public async Task A_body_typed_while_an_overlay_is_up_recovers_via_Esc_and_submits()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-fake-pty-{Guid.NewGuid():N}");
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
                Enabled = true,
                OverlayRecoveryEnabled = true,
                OverlaySettleMs = 50,
                EvidenceTimeoutSeconds = 8,
                PollIntervalMs = 50,
                PostSubmitAdvanceTimeoutSeconds = 5,
            },
        }));
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
            Kind: AgentKind.Grok,
            Exe: FakeClaudeExe,
            Args: Array.Empty<string>(),
            Env: new Dictionary<string, string> { ["ANTIPHON_FAKE_OVERLAY_ON_COMMAND"] = "/usage" },
            Cwd: cwd,
            Cols: 120,
            Rows: 30);

        try
        {
            await client.StartAsync(sessionId, spec, CancellationToken.None);
            (await WaitForRawAsync(client, sessionId, s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(15)))
                .ShouldBeTrue();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, CardId = null, DefinitionName = "fakeclaude",
                    AgentKind = AgentKind.Grok, Status = SessionStatus.Running,
                    Cwd = cwd, Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now,
                });
                await db.SaveChangesAsync();
            }

            var queue = provider.GetRequiredService<SessionMessageQueueService>();
            await queue.EnqueueAsync(sessionId, "/usage", MessageSendMode.Now, CancellationToken.None);
            (await WaitForRawAsync(client, sessionId, s => s.Contains("OVERLAY:open"), TimeSpan.FromSeconds(8)))
                .ShouldBeTrue("the first /usage must leave the overlay standing");

            await queue.EnqueueAsync(sessionId, "hello after overlay", MessageSendMode.Now, CancellationToken.None);

            var submitted = await WaitForRawAsync(
                client, sessionId, s => s.Contains("SUBMITTED:hello after overlay"), TimeSpan.FromSeconds(15));
            submitted.ShouldBeTrue("S5/S6 must Esc the leftover overlay and deliver the real body");
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

    // PR 9 at the PTY tier: a batched multi-line body must pass composer delivery verification and
    // submit as ONE turn through the real runtime -> runner -> ConPTY path.
    [Test]
    public async Task Batched_multiline_body_passes_composer_delivery_verification_and_submits_once()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-fake-pty-{Guid.NewGuid():N}");
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
        services.AddSingleton(Options.Create(
            new Antiphon.Server.Application.Settings.ChannelBridgeSettings { BatchingEnabled = true }));
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
            (await WaitForRawAsync(client, sessionId, s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(15)))
                .ShouldBeTrue();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, CardId = null, DefinitionName = "fakeclaude",
                    AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running,
                    Cwd = cwd, Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now,
                });
                // Working state so both messages stay pending until the turn-end flush batches them.
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 1,
                    Kind = Antiphon.SessionRunner.Contracts.TranscriptKinds.AssistantText,
                    Text = "working", CreatedAt = now,
                });
                await db.SaveChangesAsync();
            }

            var queue = provider.GetRequiredService<SessionMessageQueueService>();
            await queue.EnqueueAsync(sessionId, "[T] batched alpha", MessageSendMode.WhenIdle,
                CancellationToken.None, QueuedMessageOrigin.Channel, "telegram:batch");
            await queue.EnqueueAsync(sessionId, "[T] batched omega", MessageSendMode.WhenIdle,
                CancellationToken.None, QueuedMessageOrigin.Channel, "telegram:batch");

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 2,
                    Kind = Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd,
                    StopReason = "end_turn", CreatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            await queue.OnTurnEndAsync(sessionId, CancellationToken.None);

            // ONE submit whose escaped marker carries both batch markers and both messages.
            var submitted = await WaitForRawAsync(
                client, sessionId,
                s => s.Contains("SUBMITTED:" + ChannelPromptFormat.BatchContextMarker)
                     && s.Contains("[T] batched alpha")
                     && s.Contains("[T] batched omega"),
                TimeSpan.FromSeconds(10));
            submitted.ShouldBeTrue("the batched body must pass verification and submit as one turn");
        }
        finally
        {
            try { await client.KillAsync(sessionId, CancellationToken.None); } catch { /* best effort */ }
            await client.DisposeAsync();
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId).ExecuteDeleteAsync();
                await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            }
            try { Directory.Delete(cwd, recursive: true); } catch { /* best effort */ }
        }
    }

    // Live miss 2026-07-29 ("Add these to my calendar", Antiphon-Family): a 2.4 KB multi-line body
    // was delivered raw; ConPTY chunked it, the TUI's paste heuristic fragmented it at line breaks,
    // and the agent's prompt was ONLY the final fragment (no envelope, no content). The body must
    // reach the agent as ONE intact turn — DeliverAsync wraps multi-line bodies in bracketed paste.
    [Test]
    public async Task Large_multiline_channel_body_submits_as_one_intact_turn()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-fake-pty-{Guid.NewGuid():N}");
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
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-fake-cwd-{sessionId:N}");
        Directory.CreateDirectory(cwd);

        var spec = new AgentLaunchSpec(
            DefinitionName: "fakeclaude", Kind: AgentKind.ClaudeCode, Exe: FakeClaudeExe,
            Args: Array.Empty<string>(), Env: new Dictionary<string, string>(),
            Cwd: cwd, Cols: 120, Rows: 30);

        try
        {
            await client.StartAsync(sessionId, spec, CancellationToken.None);
            (await WaitForRawAsync(client, sessionId, s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(15)))
                .ShouldBeTrue();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId, CardId = null, DefinitionName = "fakeclaude",
                    AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running,
                    Cwd = cwd, Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now,
                });
                await db.SaveChangesAsync();
            }

            // The live-miss shape: envelope header, blank-line padding, content lines, long tail —
            // deliberately with CRLF line endings, the actual fragmentation hazard (measured against
            // real Claude 2026-07-31: mid-body \r submits; \n is literal). DeliverAsync must
            // normalize the endings to LF or every line break submits a partial turn.
            //
            // The line count is bounded, not arbitrary. This session runs on the INBOX conhost
            // (PinnedBackend), and CARD-0037's tripwire is a hard gate there: a body over
            // DelegationSettings.PtySingleChunkBytes - 1 024 bytes, the measured chunk cut - is
            // never typed at all. SessionMessageQueueService spills it to .antiphon/inbox/ and
            // types a pointer instead. At 12 lines this body was 1 286 bytes, so it had been
            // taking the spill path - and failing - ever since that gate reached the queue,
            // proving nothing about the thing it exists to prove. What it exists to prove is the
            // CRLF normalization, which needs many line BREAKS, not a large body: it keeps every
            // break and loses the bulk. CARD-0025 (00ad946) is what made this spill; that path is
            // pinned separately by SessionMessageQueueSpillTests.
            var body = "[Telegram \"Family\" — Mike 01:55] HEAD-MARKER add these to my calendar:\r\n\r\n"
                + string.Join("\r\n", Enumerable.Range(1, 8).Select(i => $"booking line {i} " + new string('x', 80)))
                + "\r\nTAIL-MARKER also check my outlook calendar?";

            // Fail loudly on the size, not mysteriously on the assertion below: grow this body past
            // the inbox tripwire and the queue spills it to a file, and the assertion below would
            // then be pinning the POINTER, not the CRLF normalization.
            System.Text.Encoding.UTF8.GetByteCount(body).ShouldBeLessThan(
                new DelegationSettings().PtySingleChunkBytes,
                "the body must be TYPED, not spilled - assert it rather than trusting the "
                + "arithmetic to survive the next edit to these lines");

            var queue = provider.GetRequiredService<SessionMessageQueueService>();
            await queue.EnqueueAsync(sessionId, body, MessageSendMode.Now, CancellationToken.None);

            // ONE submit carrying the WHOLE body: head and tail inside the SAME SUBMITTED marker —
            // i.e. no turn boundary ("FAKE response" echo) between them. ConPTY soft-wraps the long
            // marker line, so unwrap CR/LF before matching.
            var oneIntactTurn = new System.Text.RegularExpressions.Regex(
                @"SUBMITTED:(?:(?!FAKE response).)*HEAD-MARKER(?:(?!FAKE response).)*TAIL-MARKER");
            var submitted = await WaitForRawAsync(
                client, sessionId,
                s => oneIntactTurn.IsMatch(s.Replace("\r", "").Replace("\n", "")),
                TimeSpan.FromSeconds(10));

            var snapshot = await client.GetSnapshotAsync(sessionId, CancellationToken.None);
            submitted.ShouldBeTrue(
                "the multi-line body must submit as ONE intact turn (head AND tail in one SUBMITTED marker). Raw:\n"
                + snapshot.RawOutput);
        }
        finally
        {
            try { await client.KillAsync(sessionId, CancellationToken.None); } catch { /* best effort */ }
            await client.DisposeAsync();
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            }
            try { Directory.Delete(cwd, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Risk row 4 at the PTY tier (PR 5): a multi-line <c>--append-system-prompt</c> value must
    /// survive the runner → pty-host → CreateProcess quoting chain byte-for-byte.
    ///
    /// <para>CARD-0101 / coverage plan P0-2 changed what this asserts and where it runs, because
    /// what it USED to assert was not enough on either axis:</para>
    /// <list type="bullet">
    /// <item><b>The line it reads.</b> It read <c>ARGS:</c>, which is the argv <b>.NET</b> parsed.
    /// .NET accepts a doubled <c>""</c> inside a quoted argument as one escaped quote;
    /// <c>CommandLineToArgvW</c> — <c>claude.exe</c>'s parser — splits there. This test passed for
    /// months on a command line that was delivering NINE arguments where three were intended
    /// (<c>LaunchArgvGuardTests</c> measures it on this test's own literal). It now asserts on
    /// <c>ARGVSTRICT:</c>, which is that same line re-parsed by the real Win32 parser — and keeps
    /// the <c>ARGS:</c> assertion beside it, so a future divergence between the two is visible
    /// rather than silently replaced.</item>
    /// <item><b>The backend it ran on.</b> Inbox only, while production runs <c>modern</c> — and
    /// CARD-0101's defect was in <c>ModernConPtyConnection.BuildCommandLine</c>. Both are now
    /// declared arms of the same test.</item>
    /// </list>
    /// </summary>
    [Test]
    [Arguments("inbox")]
    [Arguments("modern")]
    public async Task Launch_args_reach_the_child_process(string backend)
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");
        if (backend == "modern" && !ConPtyRedistributable.TryLocate(out _, out var why))
            throw new SkipTestException("no shipped conpty.dll: " + why);

        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-fake-pty-{Guid.NewGuid():N}");
        var client = new DirectSessionRunnerClient(sessionLogPath, ptyBackend: backend);
        var sessionId = Guid.NewGuid();
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-fake-cwd-{sessionId:N}");
        Directory.CreateDirectory(cwd);

        // The literal LaunchArgvGuardTests measures the shred on: quotes and braces inside a
        // multi-line value. Deliberately unchanged — a test whose hostile content was softened to
        // make it pass would be the exact regression this class exists to catch.
        var appendText = "line one of the preamble\nline two with \"quotes\" and {braces}\nline three — final.";
        var spec = new AgentLaunchSpec(
            DefinitionName: "fakeclaude",
            Kind: AgentKind.ClaudeCode,
            Exe: FakeClaudeExe,
            Args: new[] { "--echo-args", "--echo-argv-strict", "--append-system-prompt", appendText },
            Env: new Dictionary<string, string>(),
            Cwd: cwd,
            Cols: 200,
            Rows: 30);

        try
        {
            await client.StartAsync(sessionId, spec, CancellationToken.None);

            var expected = "--echo-args␟--echo-argv-strict␟--append-system-prompt␟"
                + appendText.Replace("\n", "\\n");
            var echoed = await WaitForRawAsync(
                client, sessionId, s => s.Contains("ARGVSTRICT:"), TimeSpan.FromSeconds(20));
            echoed.ShouldBeTrue($"fakeclaude must print its ARGVSTRICT banner line on {backend}");

            // ConPTY may soft-wrap the long banner line at the terminal width; stripping CR/LF
            // rejoins it before the exact-match check.
            var snapshot = await client.GetSnapshotAsync(sessionId, CancellationToken.None);
            var unwrapped = (snapshot.RawOutput ?? string.Empty).Replace("\r", "").Replace("\n", "");

            unwrapped.ShouldNotContain(
                "ARGVSTRICTWARN:",
                customMessage: $"on {backend} argv[0] was not the fake's own exe, so the strict vector is "
                    + "offset by one and the assertion below would be meaningless. Raw:\n" + snapshot.RawOutput);

            // The assertion that matters: what a NATIVE child's CRT builds from this command line.
            unwrapped.ShouldContain(
                "ARGVSTRICT:" + expected,
                customMessage: $"on {backend}, the multi-line append-system-prompt value must survive "
                    + "runner→pty quoting intact AS A NATIVE CHILD PARSES IT. A green ARGS: line beside a "
                    + "red one here is CARD-0101 exactly: correct for a .NET child, shredded for claude.exe. "
                    + "Raw:\n" + snapshot.RawOutput);

            // Kept, not replaced: on a correct command line the two parsers agree, and the day they
            // stop agreeing is the day something composed a line only .NET can read.
            unwrapped.ShouldContain(
                "ARGS:" + expected,
                customMessage: $"on {backend}, the .NET-parsed argv must agree with the native one. Raw:\n"
                    + snapshot.RawOutput);
        }
        finally
        {
            try { await client.KillAsync(sessionId, CancellationToken.None); } catch { /* best effort */ }
            await client.DisposeAsync();
            try { Directory.Delete(cwd, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// CARD-0055 end to end, on the failure the card was written for: the first submitting Enter is
    /// EATEN while the screen redraws (<c>ANTIPHON_FAKE_SWALLOW_ENTER=1</c> — the measured
    /// <c>ea2feb92</c> state), and the delivery must still end with the body in Claude's transcript
    /// EXACTLY ONCE.
    ///
    /// <para>All three counts are the point. <b>Zero</b> is the shipped-before behaviour: the redraw
    /// satisfied "output advanced", the message was marked Sent and the body sat in the composer for
    /// 104 minutes. <b>Two</b> is the failure mode the fix could have introduced: the retry is an
    /// Enter re-press and never a re-type, so a body that did go in cannot go in again — for a
    /// channel reply, two would be a duplicate message to a human. <b>One</b> is the contract.</para>
    ///
    /// <para>Real queue, real ConPTY, real fakeclaude, and a transcript pump standing in for the
    /// runner→server ingestion the queue polls (<c>TranscriptEntries</c> rows are the ground truth
    /// the confirm loop reads; the fake's own JSONL is where they come from here).</para>
    /// </summary>
    [Test]
    public async Task A_swallowed_enter_is_re_pressed_and_the_body_lands_in_the_transcript_exactly_once()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

        const string body = "CARD-0055 swallowed-enter delivery: reply with exactly OK.";
        var transcriptPath = Path.Combine(Path.GetTempPath(), $"antiphon-c55-swallow-{Guid.NewGuid():N}.jsonl");
        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-fake-pty-{Guid.NewGuid():N}");
        var client = new DirectSessionRunnerClient(sessionLogPath, ptyBackend: PinnedBackend);

        // ReEnterIntervalSeconds is 7 in production so a slow-but-successful submit's record usually
        // lands before any re-press; here the re-press IS the mechanism under test, so it is short.
        var provider = BuildProviderWithVerification(client, new DeliveryVerificationSettings
        {
            PollIntervalMs = 200,
            ReEnterIntervalSeconds = 3,
            TranscriptConfirmTimeoutSeconds = 25,
        });
        await using var _ = provider;

        var sessionId = Guid.NewGuid();
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-fake-cwd-{sessionId:N}");
        Directory.CreateDirectory(cwd);
        using var pump = new CancellationTokenSource();

        try
        {
            await StartSwallowingFakeAsync(client, sessionId, cwd, transcriptPath, swallowEnters: 1);
            await SeedIdleClaudeSessionAsync(sessionId, cwd);
            var pumping = PumpTranscriptAsync(transcriptPath, sessionId, pump.Token);

            var queue = provider.GetRequiredService<SessionMessageQueueService>();
            await queue.EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

            // The swallow really happened — otherwise this test passes for the boring reason.
            var snapshot = await client.GetSnapshotAsync(sessionId, CancellationToken.None);
            (snapshot.RawOutput ?? string.Empty).ShouldContain("SWALLOWED-ENTER:",
                customMessage: "the fake must have eaten the first Enter; without that this asserts nothing");

            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                var message = await db.SessionQueuedMessages
                    .SingleAsync(m => m.AgentSessionId == sessionId);
                message.Status.ShouldBe(QueuedMessageStatus.Sent,
                    "the re-pressed Enter submitted the body the composer still held, and the "
                    + "UserPrompt record confirmed it. Screen:\n" + snapshot.RawOutput);
                message.DeliveryAttempts.ShouldBe(1, "one delivery, however many Enters it took");
            }

            pump.Cancel();
            await pumping;

            // Ground truth: what Claude actually received. Not zero (stranded), not two (re-typed).
            var prompts = UserPromptsIn(transcriptPath);
            prompts.Count.ShouldBe(1,
                $"the body must reach the transcript exactly once — got {prompts.Count}: "
                + string.Join(" | ", prompts));
            prompts[0].ShouldBe(body);
        }
        finally
        {
            pump.Cancel();
            await CleanupAsync(client, sessionId, cwd, transcriptPath);
        }
    }

    /// <summary>
    /// The other end of the same mechanism: every Enter is eaten, so the body NEVER goes in. The
    /// delivery must fail loudly rather than report Sent — the message returns to Pending with its
    /// attempt recorded, and the body appears in the transcript ZERO times. A queue that typed it
    /// again on the way out (instead of re-pressing) would show two.
    /// </summary>
    [Test]
    public async Task A_delivery_whose_every_enter_is_swallowed_reverts_and_never_reaches_the_transcript()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

        const string body = "CARD-0055 never-submitted delivery: reply with exactly OK.";
        var transcriptPath = Path.Combine(Path.GetTempPath(), $"antiphon-c55-wedged-{Guid.NewGuid():N}.jsonl");
        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-fake-pty-{Guid.NewGuid():N}");
        var client = new DirectSessionRunnerClient(sessionLogPath, ptyBackend: PinnedBackend);

        var provider = BuildProviderWithVerification(client, new DeliveryVerificationSettings
        {
            PollIntervalMs = 200,
            ReEnterIntervalSeconds = 2,
            TranscriptConfirmTimeoutSeconds = 8, // 3 Enters at 0/2/4s, then give up
        });
        await using var _ = provider;

        var sessionId = Guid.NewGuid();
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-fake-cwd-{sessionId:N}");
        Directory.CreateDirectory(cwd);
        using var pump = new CancellationTokenSource();

        try
        {
            // More swallows than SubmitAttempts: nothing this delivery does can submit.
            await StartSwallowingFakeAsync(client, sessionId, cwd, transcriptPath, swallowEnters: 9);
            await SeedIdleClaudeSessionAsync(sessionId, cwd);
            var pumping = PumpTranscriptAsync(transcriptPath, sessionId, pump.Token);

            var queue = provider.GetRequiredService<SessionMessageQueueService>();
            await queue.EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

            pump.Cancel();
            await pumping;

            var snapshot = await client.GetSnapshotAsync(sessionId, CancellationToken.None);
            var raw = snapshot.RawOutput ?? string.Empty;
            // SubmitAttempts (3) Enters, all eaten — the re-press loop ran and gave up.
            raw.Split("SWALLOWED-ENTER:").Length.ShouldBe(4,
                "the confirm loop must press Enter SubmitAttempts times and no more. Screen:\n" + raw);

            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                var message = await db.SessionQueuedMessages
                    .SingleAsync(m => m.AgentSessionId == sessionId);
                message.Status.ShouldBe(QueuedMessageStatus.Pending,
                    "an unconfirmed delivery must go back in the queue, never be reported Sent");
                message.SentAt.ShouldBeNull();
                message.DeliveryAttempts.ShouldBe(1, "the attempt survives the revert — it is the retry brake");
                message.LastDeliveryBaselineSequence.ShouldNotBeNull(
                    "the stored baseline is what the next attempt's late-confirm reads");
            }

            UserPromptsIn(transcriptPath).ShouldBeEmpty(
                "nothing was submitted, so nothing may appear in Claude's transcript");
        }
        finally
        {
            pump.Cancel();
            await CleanupAsync(client, sessionId, cwd, transcriptPath);
        }
    }

    private static ServiceProvider BuildProviderWithVerification(
        DirectSessionRunnerClient client, DeliveryVerificationSettings verification)
    {
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
        services.AddSingleton<IOptions<SupervisionSettings>>(
            Options.Create(new SupervisionSettings { DeliveryVerification = verification }));
        services.AddSingleton<ISessionRunnerClient>(client);
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static async Task StartSwallowingFakeAsync(
        DirectSessionRunnerClient client, Guid sessionId, string cwd, string transcriptPath, int swallowEnters)
    {
        var spec = new AgentLaunchSpec(
            DefinitionName: "fakeclaude",
            Kind: AgentKind.ClaudeCode,
            Exe: FakeClaudeExe,
            Args: Array.Empty<string>(),
            Env: new Dictionary<string, string>
            {
                ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = transcriptPath,
                ["ANTIPHON_FAKE_SWALLOW_ENTER"] = swallowEnters.ToString(),
            },
            Cwd: cwd,
            Cols: 120,
            Rows: 30);

        await client.StartAsync(sessionId, spec, CancellationToken.None);
        // Both banners: the fake announces every armed model, and a test that believed it armed the
        // swallow model and did not would otherwise pass for entirely the wrong reason.
        var ready = await WaitForRawAsync(
            client, sessionId,
            s => s.Contains("Fake Claude ready") && s.Contains($"SWALLOWENTER:count={swallowEnters}"),
            TimeSpan.FromSeconds(15));
        ready.ShouldBeTrue("the fake must reach readiness AND announce the armed swallow model");
    }

    /// <summary>
    /// A Claude-kind session that reads IDLE and whose transcript is OBSERVABLE — the seeded TurnEnd
    /// does both jobs. Without a row the CARD-0055 observability gate degrades the delivery to the
    /// legacy screen-only verdict, which is precisely the behaviour these tests exist to replace.
    /// </summary>
    private static async Task SeedIdleClaudeSessionAsync(Guid sessionId, string cwd)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId, CardId = null, DefinitionName = "fakeclaude",
            AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running,
            Cwd = cwd, Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now,
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = SeedSequence,
            Kind = Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd,
            StopReason = "end_turn", CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private const long SeedSequence = 1;

    /// <summary>
    /// Stands in for the runner→server transcript ingestion: tail the fake's JSONL, normalize each
    /// line with the PRODUCTION normalizer, and persist the parts as <c>TranscriptEntry</c> rows —
    /// which is what <c>WaitForTranscriptConfirmAsync</c> polls. Only whole lines are consumed; a
    /// half-written last line is left for the next pass.
    /// </summary>
    private static async Task PumpTranscriptAsync(string path, Guid sessionId, CancellationToken ct)
    {
        var sequence = SeedSequence;
        var consumed = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(path))
                {
                    var text = await File.ReadAllTextAsync(path, CancellationToken.None);
                    var lines = text.Split('\n');
                    // The last element is either the empty tail after a final '\n' or a line still
                    // being appended — either way it is not ours to consume yet.
                    var complete = lines.Length - 1;
                    for (var i = consumed; i < complete; i++)
                    {
                        foreach (var part in Antiphon.SessionRunner.TranscriptNormalizer.Normalize(lines[i]))
                        {
                            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
                            db.TranscriptEntries.Add(new TranscriptEntry
                            {
                                Id = Guid.NewGuid(),
                                AgentSessionId = sessionId,
                                Sequence = ++sequence,
                                Kind = part.Kind,
                                Uuid = part.Uuid,
                                Role = part.Role,
                                Text = part.Text,
                                StopReason = part.StopReason,
                                Timestamp = part.Timestamp?.UtcDateTime,
                                CreatedAt = DateTime.UtcNow,
                            });
                            await db.SaveChangesAsync(CancellationToken.None);
                        }
                    }
                    consumed = complete;
                }
            }
            catch (IOException)
            {
                // Mid-append; try again.
            }

            try { await Task.Delay(100, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>The user-prompt texts the fake actually recorded — ground truth, not screen matching.</summary>
    private static List<string> UserPromptsIn(string transcriptPath)
    {
        if (!File.Exists(transcriptPath))
            return [];

        var prompts = new List<string>();
        foreach (var line in File.ReadAllLines(transcriptPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            foreach (var part in Antiphon.SessionRunner.TranscriptNormalizer.Normalize(line))
            {
                if (part.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt)
                    prompts.Add(part.Text ?? string.Empty);
            }
        }
        return prompts;
    }

    private static async Task CleanupAsync(
        DirectSessionRunnerClient client, Guid sessionId, string cwd, string transcriptPath)
    {
        try { await client.KillAsync(sessionId, CancellationToken.None); } catch { /* best effort */ }
        await client.DisposeAsync();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
        }
        try { Directory.Delete(cwd, recursive: true); } catch { /* best effort */ }
        try { File.Delete(transcriptPath); } catch { /* best effort */ }
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
