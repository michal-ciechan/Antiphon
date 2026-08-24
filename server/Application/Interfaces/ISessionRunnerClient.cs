using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Interfaces;

public interface ISessionRunnerClient
{
    /// <summary>
    /// What the runner's pty-hosts are actually served by — the evidence <c>PtyDeliveryProfile</c>
    /// needs before it will use the raised (paste-path) delivery ceilings.
    ///
    /// <para>Default implementation returns null, meaning "this client cannot say". Callers MUST
    /// treat null as no evidence either way and never as proof of the modern backend: the in-proc
    /// and fake clients used by tests all land here.</para>
    /// </summary>
    Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
        Task.FromResult<RunnerCapabilitiesDto?>(null);

    /// <summary>
    /// Body of the runner's <c>GET /health</c> (CARD-0179). Null means this client cannot say —
    /// the diagnostics bundle records that in errors.txt rather than failing the zip.
    /// </summary>
    Task<string?> GetHealthAsync(CancellationToken ct) =>
        Task.FromResult<string?>(null);

    /// <summary>
    /// Positive-evidence-only transcript compatibility verdict (CARD-0112). Null means either the
    /// runner cannot answer or it explicitly supports the requested format; neither absence nor a
    /// failed probe is evidence that a launch is unsafe.
    /// </summary>
    Task<RunnerCapabilityMismatch?> GetTranscriptCapabilityMismatchAsync(AgentKind kind, CancellationToken ct) =>
        Task.FromResult<RunnerCapabilityMismatch?>(null);

    Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct);
    Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct);
    Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct);
    Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct);
    Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct);
    Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct);
    Task SendInputAsync(Guid sessionId, string input, CancellationToken ct);
    Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct);
    Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct);
    Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct);
    IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct);
}
