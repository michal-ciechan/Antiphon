using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

public sealed class PhoneHomeRuntimeAdapter : IPhoneHomeRuntimeSurface
{
    private readonly SessionRunnerRuntime _runtime;
    private readonly RunnerBuildDto _build;

    public PhoneHomeRuntimeAdapter(SessionRunnerRuntime runtime, RunnerBuildDto build)
    {
        _runtime = runtime;
        _build = build;
    }

    public int OwnedSessionCount => _runtime.LiveSessionCount;

    public RunnerCapabilitiesDto Capabilities()
    {
        var decision = PtyBackendPolicy.Resolve();
        IReadOnlyList<string> backends = [SessionBackends.PtyHost];
        IReadOnlyList<string> features =
        [
            GrokRulesTransport.Capability,
            RunnerCapabilityFeatures.SessionGenerationV1,
            RunnerCapabilityFeatures.ConditionalMaintenanceInputV1,
        ];
        // Phone-home never advertises Windows custody, even if the host binary could.
        return new RunnerCapabilitiesDto(
            decision.Backend.ToString(), decision.Requested, decision.Reason, decision.FellBack,
            SessionRunnerRuntime.SupportedTranscriptFormats, _build, backends,
            Version: _build.CommitSha ?? "unknown",
            Features: features, VerificationCustodyBackend: null,
            RunnerStoreId: _runtime.RunnerStoreId);
    }

    public string Health() => "Healthy";
    public IReadOnlyList<RunnerSessionDto> List() => _runtime.List();
    public Task<RunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => _runtime.GetAsync(sessionId, ct);
    public Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct) =>
        _runtime.StartAsync(request, ct);
    public RunnerBufferDto GetBuffer(Guid sessionId) => _runtime.GetBuffer(sessionId);
    public RunnerSnapshotDto GetSnapshot(Guid sessionId) => _runtime.GetSnapshot(sessionId);
    public RunnerTranscriptDto GetTranscript(Guid sessionId) => _runtime.GetTranscript(sessionId);
    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        _runtime.SendInputAsync(sessionId, input, ct);
    public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
        _runtime.SendConditionalInputAsync(sessionId, request, ct);
    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
        _runtime.ClearLiveBufferAsync(sessionId, ct);
    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        _runtime.ResizeAsync(sessionId, cols, rows, ct);
    public Task<RunnerKillGenerationResult> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
        _runtime.KillGenerationAsync(sessionId, expectedAcceptedStartedAt, TimeSpan.FromSeconds(15), ct);
}
