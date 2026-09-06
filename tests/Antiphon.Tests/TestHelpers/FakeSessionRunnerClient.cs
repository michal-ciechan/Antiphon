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

    public bool AdvertiseHerdr { get; set; } = true;
    public bool AdvertiseGrokRules { get; set; }
    public Func<Guid, GrokRulesReceipt?>? RulesReceipt { get; set; }

    public bool AdvertiseHerdrAttach { get; set; } = true;

    public bool AdvertiseHerdrNamedTabPlacement { get; set; } = true;

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
        if (AdvertiseGrokRules) features.Add(GrokRulesTransport.Capability);
        if (AdvertiseHerdr && AdvertiseHerdrAttach)
            features.Add(RunnerCapabilityFeatures.HerdrAttach);
        if (AdvertiseHerdr && AdvertiseHerdrNamedTabPlacement)
            features.Add(RunnerCapabilityFeatures.HerdrNamedTabPlacement);
        return Task.FromResult<RunnerCapabilitiesDto?>(new(
            "ModernConPty",
            "modern",
            "harness fake runner",
            false,
            SessionRunnerRuntime.SupportedTranscriptFormats,
            SessionBackends: backends,
            Features: features.Count == 0 ? null : features));
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
        Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerSessionDto(
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

    public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
        EmptyEvents(ct);

    private static async IAsyncEnumerable<SessionRunnerEvent> EmptyEvents(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }
}
