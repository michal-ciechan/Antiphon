using System.Runtime.InteropServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0514 V-8 / R-14: the REAL producer-to-recipient lane —
/// <see cref="SessionMessageQueueService"/> -> <see cref="AgentSessionRuntime"/> ->
/// <see cref="DirectSessionRunnerClient"/> (a real in-process <c>SessionRunnerRuntime</c>) -> a real
/// ConPTY -> a real <c>fakeclaude.exe</c> child process.
///
/// <para><b>Why this exists.</b> The scripted sibling (<see cref="RemoteControlRecoveryHarness"/>)
/// runs a <c>FakeAgentProtocolAdapter</c> whose <c>OnSubmitted</c> callback WRITES the
/// <c>UserPrompt</c> transcript row the test then asserts — a receipt the test itself produced, which
/// proves the queue called the adapter and nothing about delivery. Nothing in THIS harness ever
/// inserts a <c>UserPrompt</c> row: the only ones that exist are what
/// <see cref="SessionQueueTranscriptPump"/> ingests from the child's own JSONL file, and
/// <see cref="SessionQueueTranscriptPump.FileUserPrompts"/> reads that file directly. The plan's
/// ingestion note stands (the pump substitutes for the provider tailer, which is off for
/// ClaudeCode in this client) — but the record it ingests is the child's, not the test's.</para>
/// </summary>
internal sealed class RemoteControlPtyLane : IAsyncDisposable
{
    /// <summary>CARD-0045: fakeclaude models the INBOX conhost's typed-input path; say so rather
    /// than inheriting whatever the launching shell exported.</summary>
    public const string PinnedBackend = "inbox";

    public required DirectSessionRunnerClient Client { get; init; }
    public required ServiceProvider Provider { get; init; }
    public required Guid SessionId { get; init; }
    public required string TranscriptPath { get; init; }
    public required string Cwd { get; init; }
    public required DateTime Generation { get; init; }
    public required ChildOutputRcProbe Probe { get; init; }
    public required CancellationTokenSource PumpCts { get; init; }
    public required Task Pumping { get; init; }
    public required long Baseline { get; init; }

    public SessionMessageQueueService Queue => Provider.GetRequiredService<SessionMessageQueueService>();
    public AgentSessionRuntime Runtime => Provider.GetRequiredService<AgentSessionRuntime>();
    public RemoteControlRecoveryService Recovery => Provider.GetRequiredService<RemoteControlRecoveryService>();

    public static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    /// <summary>Real ConPTY plus a staged child; a lane test cannot be faked past either.</summary>
    public static void SkipIfUnavailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");
    }

    /// <param name="rcScenario">
    /// <c>ANTIPHON_FAKE_RC_SCENARIO</c> for the child. <c>"c514"</c> makes the first submitted
    /// <c>/remote-control</c> arm the bridge (<c>RCMENU:armed</c>) and a later one open the
    /// management menu, which is the shape D-1/D-2 are about.
    /// </param>
    public static async Task<RemoteControlPtyLane> CreateAsync(
        string? rcScenario = null,
        IReadOnlyDictionary<string, string>? extraEnv = null,
        Action<SupervisionSettings>? configureSupervision = null)
    {
        SkipIfUnavailable();

        var sessionId = Guid.NewGuid();
        var transcriptPath = Path.Combine(Path.GetTempPath(), $"antiphon-c514-lane-{sessionId:N}.jsonl");
        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-c514-pty-{sessionId:N}");
        var cwd = Path.Combine(Path.GetTempPath(), $"antiphon-c514-cwd-{sessionId:N}");
        Directory.CreateDirectory(cwd);
        var client = new DirectSessionRunnerClient(sessionLogPath, ptyBackend: PinnedBackend);
        var probe = new ChildOutputRcProbe(() => RawOutput(client, sessionId));

        var supervision = RemoteControlRecoveryHarness.DefaultSupervision();
        // A real child echoes at ConPTY speed, not at a fake's speed: give composer evidence and
        // transcript confirmation the room the PTY suites already use.
        supervision.DeliveryVerification.EvidenceTimeoutSeconds = 10;
        supervision.DeliveryVerification.TranscriptConfirmTimeoutSeconds = 15;
        supervision.DeliveryVerification.PostSubmitAdvanceTimeoutSeconds = 5;
        configureSupervision?.Invoke(supervision);

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
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            SessionLogPath = sessionLogPath,
        }));
        services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(supervision));
        services.AddSingleton<ISessionRunnerClient>(client);
        services.AddSingleton<IRcBridgeProbe>(probe);
        services.AddSingleton<ILaunchOwnership>(new UnownedLaunches());
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<RemoteControlRecoveryService>();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var spec = new AgentLaunchSpec(
            DefinitionName: "fakeclaude",
            Kind: AgentKind.ClaudeCode,
            Exe: FakeClaudeExe,
            Args: Array.Empty<string>(),
            Env: BuildEnv(transcriptPath, rcScenario, extraEnv),
            Cwd: cwd,
            Cols: 120,
            Rows: 30);

        var started = await client.StartAsync(sessionId, spec, CancellationToken.None);
        var ready = await WaitForRawAsync(
            client, sessionId, s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(20));
        if (!ready)
            throw new InvalidOperationException("fake Claude never reached readiness");

        // The runner's own accepted generation is the equality token every conditional write is
        // fenced on, so the session row must carry exactly it — not a fresh UtcNow.
        var generation = SessionGeneration.Normalize(
            started.AcceptedStartedAt ?? throw new InvalidOperationException("runner reported no generation"));
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
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
                StartedAt = generation,
                LastSeenAt = now,
            });
            await db.SaveChangesAsync();
        }

        var baseline = await SessionQueueTranscriptPump.MaxSequenceAsync(sessionId);
        var pumpCts = new CancellationTokenSource();
        var pumping = SessionQueueTranscriptPump.RunAsync(transcriptPath, sessionId, pumpCts.Token);

        return new RemoteControlPtyLane
        {
            Client = client,
            Provider = provider,
            SessionId = sessionId,
            TranscriptPath = transcriptPath,
            Cwd = cwd,
            Generation = generation,
            Probe = probe,
            PumpCts = pumpCts,
            Pumping = pumping,
            Baseline = baseline,
        };
    }

    private static Dictionary<string, string> BuildEnv(
        string transcriptPath, string? rcScenario, IReadOnlyDictionary<string, string>? extraEnv)
    {
        var env = new Dictionary<string, string> { ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = transcriptPath };
        if (!string.IsNullOrEmpty(rcScenario))
            env["ANTIPHON_FAKE_RC_SCENARIO"] = rcScenario;
        if (extraEnv is not null)
            foreach (var (key, value) in extraEnv)
                env[key] = value;
        return env;
    }

    /// <summary>The child's own JSONL, read from disk — never a row this process wrote.</summary>
    public List<string> ChildFileUserPrompts() => SessionQueueTranscriptPump.FileUserPrompts(TranscriptPath);

    /// <summary>The ingested copy of that same file, after the harness baseline.</summary>
    public Task<List<string>> PersistedUserPromptsAsync() =>
        SessionQueueTranscriptPump.DestinationUserPromptsAsync(SessionId, Baseline);

    public AppDbContext CreateDb() => new(TestDbFixture.CreateDbContextOptions());

    public string RawOutput() => RawOutput(Client, SessionId);

    private static string RawOutput(DirectSessionRunnerClient client, Guid sessionId)
    {
        try
        {
            return client.GetSnapshotAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult().RawOutput;
        }
        catch
        {
            return "";
        }
    }

    public async Task<bool> WaitForRawAsync(Func<string, bool> predicate, TimeSpan timeout) =>
        await WaitForRawAsync(Client, SessionId, predicate, timeout);

    private static async Task<bool> WaitForRawAsync(
        DirectSessionRunnerClient client, Guid sessionId, Func<string, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(RawOutput(client, sessionId)))
                return true;
            await Task.Delay(100);
        }

        return predicate(RawOutput(client, sessionId));
    }

    /// <summary>Waits until the pump has ingested a UserPrompt row for every body.</summary>
    public async Task<bool> WaitForPersistedPromptsAsync(IReadOnlyCollection<string> bodies, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var seen = await PersistedUserPromptsAsync();
            if (bodies.All(b => seen.Contains(b)))
                return true;
            await Task.Delay(150);
        }

        return false;
    }

    /// <summary>Reserve then execute one automatic arm under the production per-session queue lock.</summary>
    public async Task<RemoteControlArmResult> ReserveAndExecuteAsync(
        QueuedMessageOrigin origin = QueuedMessageOrigin.Supervision,
        CancellationToken ct = default)
    {
        var sem = Queue.GetLock(SessionId);
        await sem.WaitAsync(ct);
        try
        {
            var id = await Recovery.ReserveAutomaticArmUnderLockAsync(SessionId, Generation, origin, ct)
                ?? throw new InvalidOperationException("automatic arm was not reserved");
            return await Recovery.ExecuteAutomaticArmUnderLockAsync(SessionId, id, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<RemoteControlArmResult> ExecuteAsync(Guid requestId, CancellationToken ct = default)
    {
        var sem = Queue.GetLock(SessionId);
        await sem.WaitAsync(ct);
        try
        {
            return await Recovery.ExecuteAutomaticArmUnderLockAsync(SessionId, requestId, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Ends the child's hung turn from the server side. The BUSY state this releases was the
    /// child's own (its JSONL user record with no reply under ANTIPHON_FAKE_NO_REPLY); only the
    /// release is scripted, because that child never answers.
    /// </summary>
    public async Task MarkIdleAsync()
    {
        await using var db = CreateDb();
        var seq = ((await db.TranscriptEntries
            .Where(t => t.AgentSessionId == SessionId)
            .MaxAsync(t => (long?)t.Sequence)) ?? 0) + 1;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = SessionId,
            Sequence = seq,
            Kind = TranscriptKinds.TurnEnd,
            StopReason = "end_turn",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await PumpCts.CancelAsync();
        try { await Pumping; } catch (OperationCanceledException) { }
        PumpCts.Dispose();
        try { await Client.KillAsync(SessionId, CancellationToken.None); } catch { /* best effort */ }
        await Client.DisposeAsync();
        await Provider.DisposeAsync();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            await db.RemoteControlModalEpisodes.Where(e => e.SessionId == SessionId).ExecuteDeleteAsync();
            await db.SessionQueuedMessages.Where(m => m.AgentSessionId == SessionId).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
        }
        try { Directory.Delete(Cwd, recursive: true); } catch { /* best effort */ }
        try { File.Delete(TranscriptPath); } catch { /* best effort */ }
        try { File.Delete(TranscriptPath + ".timing"); } catch { /* best effort */ }
    }
}

/// <summary>
/// The bridge probe reads Claude's per-process state file, which fakeclaude does not write. This
/// stand-in answers from the CHILD'S OWN OUTPUT instead: Armed only once the child has actually
/// printed its arm marker. It cannot report Armed for a write that never reached the child.
/// </summary>
internal sealed class ChildOutputRcProbe : IRcBridgeProbe
{
    private readonly Func<string> _rawOutput;

    public ChildOutputRcProbe(Func<string> rawOutput) => _rawOutput = rawOutput;

    /// <summary>The marker fakeclaude prints when a submitted /remote-control armed the bridge.</summary>
    public string ArmedMarker { get; set; } = "RCMENU:armed";

    public bool StateFileFound { get; set; } = true;
    public int ProbeCount { get; private set; }

    public RcProbeResult Probe(int pid)
    {
        ProbeCount++;
        var armed = _rawOutput().Contains(ArmedMarker, StringComparison.Ordinal);
        return new RcProbeResult(armed, armed ? 2 : 0, StateFileFound);
    }
}

/// <summary>No launch in this process owns anything: the lane starts its child directly.</summary>
internal sealed class UnownedLaunches : ILaunchOwnership
{
    public bool Owns(Guid sessionId) => false;
    public void ResumeInterrupted(Guid sessionId, Guid agentId) { }
    public bool TryRegister(Guid sessionId) => true;
    public void Unregister(Guid sessionId) { }
}
