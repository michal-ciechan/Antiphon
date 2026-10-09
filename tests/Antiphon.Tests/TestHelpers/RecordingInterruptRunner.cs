using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0491. Scripted runner for the mid-turn refine fixture. Records the conditional
/// key and raw input. Does not invent a cancelled turn.
/// </summary>
internal sealed class RecordingInterruptRunner : ISessionRunnerClient
{
    public Guid SessionId { get; set; }
    public string SessionStatusText { get; set; } = "Running";
    public DateTime? AcceptedStartedAt { get; set; }
    public long LastSequence { get; set; } = 41;
    public Exception? ThrowOnGet { get; set; }
    public bool ThrowOnSnapshot { get; set; }
    public string? RenderedScreen { get; set; }
    public DateTime? SnapshotAcceptedStartedAt { get; set; }
    public bool AdvertiseConditional { get; set; } = true;
    public string? ConditionalOutcomeOverride { get; set; }
    public Exception? ThrowOnConditional { get; set; }
    public Exception? ThrowOnInputOnce { get; set; }
    public Func<CancellationToken, Task>? OnConditional { get; set; }
    public Dictionary<Guid, IReadOnlyList<SessionRunnerTranscriptEvent>> NextEntries { get; } = new();
    public List<(Guid SessionId, RunnerConditionalInputRequest Request)> ConditionalCalls { get; } = [];
    public List<(Guid SessionId, string Input)> RawInputs { get; } = [];
    public int KillCalls { get; private set; }

    public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var features = new List<string> { RunnerCapabilityFeatures.SessionGenerationV1 };
        if (AdvertiseConditional)
            features.Add(RunnerCapabilityFeatures.ConditionalMaintenanceInputV1);
        return Task.FromResult<RunnerCapabilitiesDto?>(new(
            "ModernConPty", "modern", "c0491 harness", false, Features: features));
    }

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([SessionDto(SessionId)]);
    }

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ThrowOnGet is not null)
            throw ThrowOnGet;
        return Task.FromResult(SessionDto(sessionId));
    }

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ThrowOnSnapshot)
            throw new InvalidOperationException("snapshot unavailable");
        return Task.FromResult(new SessionRunnerSnapshotDto(
            sessionId,
            "",
            RenderedScreen ?? "",
            LastSequence,
            AcceptedStartedAt ?? default,
            SnapshotAcceptedStartedAt ?? AcceptedStartedAt));
    }

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entries = NextEntries.TryGetValue(sessionId, out var list)
            ? list
            : Array.Empty<SessionRunnerTranscriptEvent>();
        return Task.FromResult(new SessionRunnerTranscriptDto(sessionId, entries, LastSequence));
    }

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ThrowOnInputOnce is not null)
        {
            var error = ThrowOnInputOnce;
            ThrowOnInputOnce = null;
            throw error;
        }

        RawInputs.Add((sessionId, input));
        return Task.CompletedTask;
    }

    public async Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ConditionalCalls.Add((sessionId, request));
        if (OnConditional is not null)
            await OnConditional(ct);
        if (ThrowOnConditional is not null)
            throw ThrowOnConditional;
        var outcome = ConditionalOutcomeOverride ?? ConditionalInputOutcomes.Written;
        return new RunnerConditionalInputResult(
            sessionId, outcome, request.ExpectedAcceptedStartedAt, request.ExpectedLastSequence);
    }

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerBufferDto(sessionId, "", LastSequence));

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
    {
        KillCalls++;
        return Task.FromResult(SessionDto(sessionId) with { Status = "Exited", ExitCode = 0 });
    }

    public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty(ct);

    private static async IAsyncEnumerable<SessionRunnerEvent> Empty(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    private SessionRunnerSessionDto SessionDto(Guid sessionId) =>
        new(sessionId, null, AcceptedStartedAt ?? default, SessionStatusText, null, AgentExitReason.Unknown,
            LastSequence, AcceptedStartedAt: AcceptedStartedAt, TranscriptBound: true);
}
