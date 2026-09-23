using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0384: harness runner-client fake with a PlacementCheck script hook, recorded check
/// calls, and a StartRefusal hook that throws <see cref="ConflictException"/> from StartAsync.
/// </summary>
internal sealed class FakeSessionRunnerClient : ISessionRunnerClient
{
    private readonly List<HerdrPlacementCheckRequest> _checkCalls = [];
    private readonly object _gate = new();

    public Func<HerdrPlacementCheckRequest, Task<HerdrPlacementCheckResult>>? PlacementCheck { get; set; }

    public Func<ConflictException>? StartRefusal { get; set; }
    public Func<CancellationToken, Task<IReadOnlyList<SessionRunnerSessionDto>>>? ListOverride { get; set; }
    public Func<Guid, CancellationToken, Task<SessionRunnerSessionDto>>? GetOverride { get; set; }

    public bool AdvertiseHerdr { get; set; } = true;
    public bool AdvertiseGrokRules { get; set; }
    public Guid? VerificationStoreId { get; set; }
    public Func<Guid, GrokRulesReceipt?>? RulesReceipt { get; set; }

    public bool AdvertiseHerdrAttach { get; set; } = true;

    public bool AdvertiseHerdrNamedTabPlacement { get; set; } = true;

    public bool AdvertiseSessionGeneration { get; set; } = true;
    public bool AdvertiseConditionalInput { get; set; } = true;
    public bool AdvertiseCompactionStop { get; set; }
    public CompactionTailObservation? CompactionObservation { get; set; }
    public List<CompactionContinuationStopRequest> CompactionStops { get; } = [];
    public CompactionContinuationStopResult? CompactionStopResult { get; set; }

    /// <summary>
    /// CARD-0606: a stop result computed FROM the request, so an ordinary success fake echoes the
    /// attempt the coordinator actually asked about instead of inventing a fresh id. An explicitly
    /// supplied <see cref="CompactionStopResult"/> still wins, which is how a crossed-response test
    /// hands back a prior attempt's success on purpose.
    /// </summary>
    public Func<CompactionContinuationStopRequest, CompactionContinuationStopResult>? CompactionStopResultFor { get; set; }
    public int KillCalls { get; private set; }
    public List<(Guid SessionId, DateTime Expected)> KillGenerationCalls { get; } = [];
    public List<(Guid SessionId, RunnerConditionalInputRequest Request)> ConditionalInputCalls { get; } = [];
    public RunnerConditionalInputResult? ConditionalInputResult { get; set; }

    public IReadOnlyList<HerdrPlacementCheckRequest> CheckCalls
    {
        get { lock (_gate) return _checkCalls.ToList(); }
    }

    public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<string> backends = AdvertiseHerdr
            ? [SessionBackends.PtyHost, SessionBackends.Herdr]
            : [SessionBackends.PtyHost];
        var features = new List<string>();
        if (VerificationStoreId is not null) features.Add(RunnerCapabilityFeatures.VerificationCustodyV1);
        if (AdvertiseGrokRules) features.Add(GrokRulesTransport.Capability);
        if (AdvertiseHerdr && AdvertiseHerdrAttach)
            features.Add(RunnerCapabilityFeatures.HerdrAttach);
        if (AdvertiseHerdr && AdvertiseHerdrNamedTabPlacement)
            features.Add(RunnerCapabilityFeatures.HerdrNamedTabPlacement);
        if (AdvertiseSessionGeneration)
            features.Add(RunnerCapabilityFeatures.SessionGenerationV1);
        if (AdvertiseConditionalInput)
            features.Add(RunnerCapabilityFeatures.ConditionalMaintenanceInputV1);
        if (AdvertiseCompactionStop)
            features.Add(RunnerCapabilityFeatures.CompactionContinuationStopV1);
        return Task.FromResult<RunnerCapabilitiesDto?>(new(
            "ModernConPty",
            "modern",
            "harness fake runner",
            false,
            SessionRunnerRuntime.SupportedTranscriptFormats,
            SessionBackends: backends,
            Features: features.Count == 0 ? null : features,
            VerificationCustodyBackend: VerificationStoreId is null ? null : "windows-job-v1",
            RunnerStoreId: VerificationStoreId));
    }

    public Task<string?> GetSessionBackendCapabilityMismatchAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (AdvertiseHerdr)
            return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(
            "The harness fake runner does not advertise herdr.");
    }

    public async Task<HerdrPlacementCheckResult> CheckHerdrPlacementAsync(
        HerdrPlacementCheckRequest request, CancellationToken ct)
    {
        lock (_gate)
            _checkCalls.Add(request);
        if (PlacementCheck is { } hook)
            return await hook(request);
        return new HerdrPlacementCheckResult("create");
    }

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
    {
        if (StartRefusal is { } refuse)
            throw refuse();
        throw new NotSupportedException("Harness launches go through IAgentProtocolAdapter, not StartAsync.");
    }

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
        ListOverride?.Invoke(ct) ?? Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        GetOverride?.Invoke(sessionId, ct) ?? Task.FromResult(new SessionRunnerSessionDto(
            sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0, GrokRulesReceipt: RulesReceipt?.Invoke(sessionId)));

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<CompactionTailObservation> ObserveCompactionAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(CompactionObservation ?? CompactionTailObservation.Unsupported());
    }

    public Task<CompactionContinuationStopResult> StopCompactionContinuationAsync(
        Guid sessionId, CompactionContinuationStopRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CompactionStops.Add(request);
        return Task.FromResult(CompactionStopResult
            ?? CompactionStopResultFor?.Invoke(request)
            ?? new CompactionContinuationStopResult(
                sessionId, request.AttemptId, false, CompactionStopOutcomes.Unsupported, null));
    }

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
    {
        KillCalls++;
        return Task.FromResult(new SessionRunnerSessionDto(
            sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));
    }

    public Task<RunnerKillGenerationResult> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct)
    {
        KillGenerationCalls.Add((sessionId, expectedAcceptedStartedAt));
        return Task.FromResult(new RunnerKillGenerationResult(
            sessionId, true, KillGenerationOutcomes.Killed, expectedAcceptedStartedAt));
    }

    public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct)
    {
        if (!AdvertiseConditionalInput)
            return Task.FromResult(new RunnerConditionalInputResult(
                sessionId, ConditionalInputOutcomes.Unsupported, null, null));
        ConditionalInputCalls.Add((sessionId, request));
        return Task.FromResult(ConditionalInputResult
            ?? new RunnerConditionalInputResult(
                sessionId, ConditionalInputOutcomes.Written, request.ExpectedAcceptedStartedAt, request.ExpectedLastSequence));
    }

    public Func<VerificationExecutionBinding, bool, CancellationToken, Task<VerificationCustodyStatus>>? VerificationCustody { get; set; }

    public Task<VerificationCustodyStatus> ReadVerificationCustodyAsync(
        VerificationExecutionBinding binding, bool seal, CancellationToken ct) =>
        VerificationCustody?.Invoke(binding, seal, ct)
        ?? Task.FromResult(new VerificationCustodyStatus(binding, VerificationCustodyState.UnsupportedBackend,
            "verification_custody_unsupported_backend"));

    public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
        EmptyEvents(ct);

    private static async IAsyncEnumerable<SessionRunnerEvent> EmptyEvents(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }
}
