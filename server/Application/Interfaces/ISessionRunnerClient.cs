using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

public interface ISessionRunnerClient
{
    Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct);
    Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct);
    Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct);
    Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct);
    Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct);
    Task SendInputAsync(Guid sessionId, string input, CancellationToken ct);
    Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct);
    Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct);
    Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct);
    IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct);
}
