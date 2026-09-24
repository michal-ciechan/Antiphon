using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-0679 D-6: the client a remote adapter holds. It resolves the runner's CURRENT connection
/// on every call, exactly as <see cref="RoutingSessionRunnerClient"/> does by session binding, so
/// an adapter created before a reconnect reaches the replacement connection afterwards. It used to
/// hold the one <see cref="PhoneHomeRunnerClient"/> Resolve returned at creation, and every later
/// call (the clean-up kill included) went to a disposed connection and failed its dispatch gate.
/// Resolve refuses while the runner has no usable connection, which is a typed
/// <c>phone_home_unavailable</c>, never a request on a dead socket.
/// </summary>
public sealed class RunnerScopedSessionRunnerClient : ISessionRunnerClient, IVerificationWorkspaceTransport
{
    private readonly ISessionRunnerDirectory _directory;

    public RunnerScopedSessionRunnerClient(ISessionRunnerDirectory directory, string runnerId)
    {
        _directory = directory;
        RunnerId = runnerId;
    }

    public string RunnerId { get; }

    private ISessionRunnerClient Current => _directory.Resolve(RunnerId);

    private IVerificationWorkspaceTransport CurrentTransport =>
        Current as IVerificationWorkspaceTransport
        ?? throw new ServiceUnavailableException(
            $"Runner '{RunnerId}' has no verification workspace transport.", PhoneHomeProblemTypes.Unavailable);

    public Task<VerificationCustodyStatus> ReadVerificationCustodyAsync(
        VerificationExecutionBinding binding, bool seal, CancellationToken ct) =>
        Current.ReadVerificationCustodyAsync(binding, seal, ct);

    public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => Current.GetCapabilitiesAsync(ct);

    public Task<RunnerProviderAuthDto?> GetProviderAuthAsync(string provider, CancellationToken ct) =>
        Current.GetProviderAuthAsync(provider, ct);

    public Task<string?> GetHealthAsync(CancellationToken ct) => Current.GetHealthAsync(ct);

    public Task<RunnerCapabilityMismatch?> GetTranscriptCapabilityMismatchAsync(AgentKind kind, CancellationToken ct) =>
        Current.GetTranscriptCapabilityMismatchAsync(kind, ct);

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
        Current.StartAsync(sessionId, spec, ct);

    public Task<HerdrPaneInspectDto> InspectHerdrPaneAsync(string paneId, CancellationToken ct) =>
        Current.InspectHerdrPaneAsync(paneId, ct);

    public Task<HerdrPaneDisposalPreview> PreviewHerdrPaneDisposalAsync(
        HerdrPaneDisposalPreviewRequest request, CancellationToken ct) =>
        Current.PreviewHerdrPaneDisposalAsync(request, ct);

    public Task<HerdrPaneDisposalReceipt> DisposeHerdrPaneAsync(HerdrPaneDisposalRequest request, CancellationToken ct) =>
        Current.DisposeHerdrPaneAsync(request, ct);

    public Task<HerdrPaneDisposalPreview> GetHerdrPaneDisposalPreviewAsync(Guid previewId, CancellationToken ct) =>
        Current.GetHerdrPaneDisposalPreviewAsync(previewId, ct);

    public Task<HerdrPaneDisposalReceipt> GetHerdrPaneDisposalAsync(Guid operationId, CancellationToken ct) =>
        Current.GetHerdrPaneDisposalAsync(operationId, ct);

    public Task<SessionRunnerSessionDto> AttachHerdrAsync(HerdrAttachRequest request, CancellationToken ct) =>
        Current.AttachHerdrAsync(request, ct);

    public Task<HerdrPlacementCheckResult> CheckHerdrPlacementAsync(
        HerdrPlacementCheckRequest request, CancellationToken ct) =>
        Current.CheckHerdrPlacementAsync(request, ct);

    public Task<string?> GetSessionBackendCapabilityMismatchAsync(CancellationToken ct) =>
        Current.GetSessionBackendCapabilityMismatchAsync(ct);

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Current.ListAsync(ct);

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => Current.GetAsync(sessionId, ct);

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        Current.GetBufferAsync(sessionId, ct);

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
        Current.GetSnapshotAsync(sessionId, ct);

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
        Current.GetTranscriptAsync(sessionId, ct);

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        Current.SendInputAsync(sessionId, input, ct);

    public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
        Current.SendConditionalInputAsync(sessionId, request, ct);

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Current.ClearLiveBufferAsync(sessionId, ct);

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        Current.ResizeAsync(sessionId, cols, rows, ct);

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) => Current.KillAsync(sessionId, ct);

    public Task<SessionRunnerSessionDto> ReleaseSlotAsync(Guid sessionId, string reason, CancellationToken ct) =>
        Current.ReleaseSlotAsync(sessionId, reason, ct);

    public Task<RunnerKillGenerationResult> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
        Current.KillGenerationAsync(sessionId, expectedAcceptedStartedAt, ct);

    public Task<CompactionContinuationStopResult> StopCompactionContinuationAsync(
        Guid sessionId, CompactionContinuationStopRequest request, CancellationToken ct) =>
        Current.StopCompactionContinuationAsync(sessionId, request, ct);

    public Task<CompactionTailObservation> ObserveCompactionAsync(Guid sessionId, CancellationToken ct) =>
        Current.ObserveCompactionAsync(sessionId, ct);

    public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Current.StreamEventsAsync(ct);

    public Task<PhoneHomeVerificationCreateResponse> CreateAsync(
        PhoneHomeVerificationCreateRequest request, CancellationToken ct) =>
        CurrentTransport.CreateAsync(request, ct);

    public Task<PhoneHomeVerificationValidateResponse> ValidateAsync(
        PhoneHomeVerificationValidateRequest request, CancellationToken ct) =>
        CurrentTransport.ValidateAsync(request, ct);

    public Task<PhoneHomeVerificationInspectResponse> InspectAsync(
        PhoneHomeVerificationInspectRequest request, CancellationToken ct) =>
        CurrentTransport.InspectAsync(request, ct);

    public Task<PhoneHomeVerificationReadRestorationResponse> ReadRestorationAsync(
        PhoneHomeVerificationReadRestorationRequest request, CancellationToken ct) =>
        CurrentTransport.ReadRestorationAsync(request, ct);

    public Task<PhoneHomeVerificationRemoveResponse> RemoveAsync(
        PhoneHomeVerificationRemoveRequest request, CancellationToken ct) =>
        CurrentTransport.RemoveAsync(request, ct);
}
