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

    /// <summary>
    /// CARD-0511: the runner identity the supervisor compares while an agent is held. Null keeps
    /// the pre-CARD-0511 shape (a runner that cannot say, i.e. identity "unknown").
    /// </summary>
    public RunnerBuildDto? Build { get; set; }

    /// <summary>
    /// CARD-0511: takes over <see cref="GetCapabilitiesAsync"/> entirely. Returning null models an
    /// unreachable runner, which must keep every runner-build hold.
    /// </summary>
    public Func<CancellationToken, Task<RunnerCapabilitiesDto?>>? CapabilitiesOverride { get; set; }

    /// <summary>CARD-0511: proves the identity probe is taken once per tick, and only while held.</summary>
    public int CapabilitiesCalls { get; private set; }
    public bool AdvertiseConditionalInput { get; set; } = true;
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
        CapabilitiesCalls++;
        if (CapabilitiesOverride is { } capabilities)
            return capabilities(ct);
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
        return Task.FromResult<RunnerCapabilitiesDto?>(new(
            "ModernConPty",
            "modern",
            "harness fake runner",
            false,
            SessionRunnerRuntime.SupportedTranscriptFormats,
            Build,
            backends,
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

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerSessionDto(
            sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));

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
