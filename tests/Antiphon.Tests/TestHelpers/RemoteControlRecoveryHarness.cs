using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0514: production queue/runtime/recovery/watch graph with a scripted runner and probe.
/// No auto-created UserPrompt. Isolated-schema optional.
/// </summary>
internal sealed class RemoteControlRecoveryHarness : IAsyncDisposable
{
    public const string MenuScreen = FakeAgentProtocolAdapter.RemoteControlMenuScreenText;
    public const int ChildPid = 4242;

    public required BridgeQueueHarness Inner { get; init; }
    public required ScriptedRcRunner Runner { get; init; }
    public required ScriptedRcProbe Probe { get; init; }
    public required string? ConnectionString { get; init; }
    public IsolatedTestSchema? Isolated { get; init; }

    public Guid SessionId => Inner.SessionId;
    public Guid AgentId => Inner.AgentId;
    public FakeAgentProtocolAdapter Adapter => Inner.Adapter;
    public AgentSessionRuntime Runtime => Inner.Runtime;
    public SessionMessageQueueService Queue => Inner.Queue;
    public RemoteControlRecoveryService Recovery =>
        Inner.Provider.GetRequiredService<RemoteControlRecoveryService>();
    public AgentSessionLaunchQueue LaunchQueue =>
        Inner.Provider.GetRequiredService<AgentSessionLaunchQueue>();

    public static async Task<RemoteControlRecoveryHarness> CreateAsync(
        bool isolated = false,
        bool transcriptBound = true,
        bool advertiseConditional = true,
        Action<SupervisionSettings>? configureSupervision = null)
    {
        IsolatedTestSchema? isolatedSchema = null;
        string? cs = null;
        if (isolated)
        {
            isolatedSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
            cs = isolatedSchema.ConnectionString;
        }

        var runner = new ScriptedRcRunner { AdvertiseConditional = advertiseConditional };
        var probe = new ScriptedRcProbe();
        var supervision = DefaultSupervision();
        configureSupervision?.Invoke(supervision);

        var inner = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConnectionString = cs,
            Supervision = supervision,
            ConfigureServices = services =>
            {
                services.AddSingleton<ISessionRunnerClient>(runner);
                services.AddSingleton<IRcBridgeProbe>(probe);
                services.AddSingleton<RemoteControlRecoveryService>();
                services.AddScoped<RemoteControlModalWatchService>();
                services.AddScoped<AttentionService>();
                services.AddScoped<IAgentIncidentRecorder>(sp =>
                    sp.GetRequiredService<AgentSupervisorService>());
            },
        });

        inner.Adapter.OnSubmitted = null;
        inner.Adapter.Pid = ChildPid;
        DateTime generation;
        await using (var db = CreateDb(cs))
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == inner.SessionId);
            generation = SessionGeneration.Normalize(session.StartedAt);
            session.StartedAt = generation;
            await db.SaveChangesAsync();
        }

        inner.Adapter.AcceptedStartedAt = generation;
        inner.Adapter.SnapshotSequence = 0;
        if (transcriptBound)
            inner.Runtime.SetTestTranscriptBound(inner.SessionId, true);

        runner.Bind(inner.SessionId, inner.Adapter, generation);
        probe.Armed = false;
        probe.StateFileFound = true;
        probe.Connections = 0;

        return new RemoteControlRecoveryHarness
        {
            Inner = inner,
            Runner = runner,
            Probe = probe,
            ConnectionString = cs,
            Isolated = isolatedSchema,
        };
    }

    public AppDbContext CreateDb() => CreateDb(ConnectionString);

    public static AppDbContext CreateDb(string? cs) =>
        new(TestDbFixture.CreateDbContextOptions(cs));

    public DateTime Generation
    {
        get
        {
            using var db = CreateDb();
            return SessionGeneration.Normalize(
                db.AgentSessions.Single(s => s.Id == SessionId).StartedAt);
        }
    }

    public async Task<RemoteControlArmResult> ReserveAndExecuteAsync(
        QueuedMessageOrigin origin = QueuedMessageOrigin.Supervision,
        CancellationToken ct = default)
    {
        var sem = Queue.GetLock(SessionId);
        await sem.WaitAsync(ct);
        try
        {
            var id = await Recovery.ReserveAutomaticArmUnderLockAsync(
                SessionId, Generation, origin, ct)
                ?? throw new InvalidOperationException("automatic arm was not reserved");
            return await Recovery.ExecuteAutomaticArmUnderLockAsync(SessionId, id, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<Guid?> ReserveAsync(
        QueuedMessageOrigin origin = QueuedMessageOrigin.Supervision,
        CancellationToken ct = default) =>
        await Recovery.TryReserveAutomaticArmAsync(SessionId, Generation, origin, ct);

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

    public async Task ArmProbeAfterWriteAsync()
    {
        Probe.OnProbe = _ => Adapter.ConditionalInputs.Count >= 2
            ? new RcProbeResult(true, 2, true)
            : new RcProbeResult(false, 0, true);
        await Task.CompletedTask;
    }

    public async Task MarkWorkingAsync()
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
            Kind = TranscriptKinds.UserPrompt,
            Text = "working-prompt-body-c514",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

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

    public async Task<int> TickWatchAsync(CancellationToken ct = default)
    {
        await using var scope = Inner.Provider.CreateAsyncScope();
        var watch = scope.ServiceProvider.GetRequiredService<RemoteControlModalWatchService>();
        return await watch.TickAsync(ct);
    }

    public async Task<AttentionDto> AttentionAsync()
    {
        await using var scope = Inner.Provider.CreateAsyncScope();
        var attention = scope.ServiceProvider.GetRequiredService<AttentionService>();
        return await attention.GetAsync(CancellationToken.None);
    }

    public async Task BindChannelAsync()
    {
        await using var db = CreateDb();
        db.ChatChannels.Add(new ChatChannel
        {
            Id = Guid.NewGuid(),
            Provider = "telegram",
            ExternalId = "c514-" + Guid.NewGuid().ToString("N")[..8],
            Kind = ChatChannelKind.Direct,
            AgentId = AgentId,
            Enabled = true,
        });
        await db.SaveChangesAsync();
    }

    public async Task SetKindAsync(AgentKind kind)
    {
        await using var db = CreateDb();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == SessionId);
        session.AgentKind = kind;
        await db.SaveChangesAsync();
    }

    public async Task SetStatusAsync(SessionStatus status)
    {
        await using var db = CreateDb();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == SessionId);
        session.Status = status;
        await db.SaveChangesAsync();
    }

    public async Task ReplaceGenerationAsync()
    {
        await using var db = CreateDb();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == SessionId);
        var next = SessionGeneration.Next(session.StartedAt, DateTime.UtcNow.AddMinutes(1));
        session.StartedAt = next;
        await db.SaveChangesAsync();
        Adapter.AcceptedStartedAt = next;
        Runner.AcceptedStartedAt = next;
    }

    public static SupervisionSettings DefaultSupervision() => new()
    {
        Enabled = true,
        RcWatch = new RcWatchSettings { Enabled = false },
        RcModalWatch = new RcModalWatchSettings { Enabled = true, SnapshotTimeoutSeconds = 1 },
        DeliveryVerification = new DeliveryVerificationSettings
        {
            Enabled = true,
            EvidenceTimeoutSeconds = 1,
            PollIntervalMs = 50,
            TranscriptConfirmTimeoutSeconds = 3,
            ReEnterIntervalSeconds = 1,
            PostFailureConfirmGraceSeconds = 3,
            BootPromptRetryDelaySeconds = 0,
        },
    };

    public async ValueTask DisposeAsync()
    {
        await Inner.DisposeAsync();
        if (Isolated is not null)
            await Isolated.DisposeAsync();
    }
}

internal sealed class ScriptedRcProbe : IRcBridgeProbe
{
    public bool Armed { get; set; }
    public int Connections { get; set; }
    public bool StateFileFound { get; set; } = true;
    public bool Throw { get; set; }
    public Func<int, RcProbeResult>? OnProbe { get; set; }
    public int ProbeCount { get; private set; }

    public RcProbeResult Probe(int pid)
    {
        ProbeCount++;
        if (Throw)
            throw new InvalidOperationException("probe failed");
        return OnProbe?.Invoke(pid) ?? new RcProbeResult(Armed, Connections, StateFileFound);
    }
}

internal sealed class ScriptedRcRunner : ISessionRunnerClient
{
    public bool AdvertiseConditional { get; set; } = true;
    public Guid SessionId { get; private set; }
    public FakeAgentProtocolAdapter? Adapter { get; set; }
    public DateTime AcceptedStartedAt { get; set; }
    public long LastSequence { get; set; }
    public string? RenderedScreenOverride { get; set; }
    public string? RawOutputOverride { get; set; }
    public DateTime? SnapshotAcceptedStartedAt { get; set; }
    public bool HangSnapshot { get; set; }
    public HashSet<Guid> HangSessions { get; } = [];
    public TimeSpan HangDelay { get; set; } = Timeout.InfiniteTimeSpan;
    public int ListCalls { get; private set; }
    public int SnapshotCalls { get; private set; }
    public List<(Guid SessionId, RunnerConditionalInputRequest Request)> ConditionalCalls { get; } = [];
    public List<(Guid SessionId, string Input)> RawInputs { get; } = [];
    public int? Pid { get; set; } = RemoteControlRecoveryHarness.ChildPid;
    public Func<Guid, Task<SessionRunnerSnapshotDto>>? SnapshotOverride { get; set; }
    public Dictionary<Guid, FakeAgentProtocolAdapter> ExtraAdapters { get; } = [];
    public List<SessionRunnerSessionDto>? ExtraSessions { get; set; }

    public void Bind(Guid sessionId, FakeAgentProtocolAdapter adapter, DateTime generation)
    {
        SessionId = sessionId;
        Adapter = adapter;
        AcceptedStartedAt = generation;
        SnapshotAcceptedStartedAt = generation;
        LastSequence = adapter.SnapshotSequence;
    }

    public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var features = new List<string> { RunnerCapabilityFeatures.SessionGenerationV1 };
        if (AdvertiseConditional)
            features.Add(RunnerCapabilityFeatures.ConditionalMaintenanceInputV1);
        return Task.FromResult<RunnerCapabilitiesDto?>(new(
            "ModernConPty", "modern", "c514 harness", false, Features: features));
    }

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
    {
        ListCalls++;
        ct.ThrowIfCancellationRequested();
        var list = new List<SessionRunnerSessionDto> { SessionDto(SessionId) };
        if (ExtraSessions is { } extra)
            list.AddRange(extra);
        return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(list);
    }

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(SessionDto(sessionId));

    public async Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        SnapshotCalls++;
        if (HangSnapshot || HangSessions.Contains(sessionId))
            await Task.Delay(HangDelay == Timeout.InfiniteTimeSpan ? TimeSpan.FromSeconds(30) : HangDelay, ct);
        if (SnapshotOverride is not null)
            return await SnapshotOverride(sessionId);

        var adapter = ExtraAdapters.GetValueOrDefault(sessionId) ?? Adapter;
        var screen = RenderedScreenOverride
            ?? adapter?.SnapshotRenderedScreen()
            ?? "";
        var raw = RawOutputOverride ?? adapter?.SnapshotRawOutput() ?? "";
        var seq = adapter?.SnapshotSequence ?? LastSequence;
        var gen = SnapshotAcceptedStartedAt ?? adapter?.AcceptedStartedAt ?? AcceptedStartedAt;
        return new SessionRunnerSnapshotDto(
            sessionId, raw, screen, seq, AcceptedStartedAt, gen);
    }

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        RawInputs.Add((sessionId, input));
        if (Adapter is not null && sessionId == SessionId)
            return Adapter.SendInputAsync(input, ct);
        return Task.CompletedTask;
    }

    public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct)
    {
        ConditionalCalls.Add((sessionId, request));
        if (!AdvertiseConditional)
            return Task.FromResult(new RunnerConditionalInputResult(
                sessionId, ConditionalInputOutcomes.Unsupported, null, null));
        if (Adapter is not null && sessionId == SessionId)
            return Adapter.SendConditionalInputAsync(request, ct);
        return Task.FromResult(new RunnerConditionalInputResult(
            sessionId, ConditionalInputOutcomes.Written, request.ExpectedAcceptedStartedAt,
            request.ExpectedLastSequence));
    }

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerBufferDto(sessionId, "", 0));

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(SessionDto(sessionId) with { Status = "Exited", ExitCode = 0 });

    public Task<RunnerKillGenerationResult> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
        Task.FromResult(new RunnerKillGenerationResult(
            sessionId, true, KillGenerationOutcomes.Killed, expectedAcceptedStartedAt));

    public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty(ct);

    private static async IAsyncEnumerable<SessionRunnerEvent> Empty(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    private SessionRunnerSessionDto SessionDto(Guid sessionId) =>
        new(sessionId, Pid, AcceptedStartedAt, "Running", null, AgentExitReason.Unknown,
            LastSequence, AcceptedStartedAt: AcceptedStartedAt, TranscriptBound: true);
}
