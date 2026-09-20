using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class RoutingSessionRunnerClient : ISessionRunnerClient
{
    private readonly ISessionRunnerDirectory _directory;

    public RoutingSessionRunnerClient(ISessionRunnerDirectory directory) => _directory = directory;

    public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
        _directory.Local.GetCapabilitiesAsync(ct);

    public Task<string?> GetHealthAsync(CancellationToken ct) => _directory.Local.GetHealthAsync(ct);

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
        _directory.Local.ListAsync(ct);

    public async Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
        await (await Route(sessionId, ct)).StartAsync(sessionId, spec, ct);

    public async Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        await (await Route(sessionId, ct)).GetAsync(sessionId, ct);

    public async Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        await (await Route(sessionId, ct)).GetBufferAsync(sessionId, ct);

    public async Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
        await (await Route(sessionId, ct)).GetSnapshotAsync(sessionId, ct);

    public async Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
        await (await Route(sessionId, ct)).GetTranscriptAsync(sessionId, ct);

    public async Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        await (await Route(sessionId, ct)).SendInputAsync(sessionId, input, ct);

    public async Task<RunnerConditionalInputResult> SendConditionalInputAsync(
        Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct) =>
        await (await Route(sessionId, ct)).SendConditionalInputAsync(sessionId, request, ct);

    public async Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
        await (await Route(sessionId, ct)).ClearLiveBufferAsync(sessionId, ct);

    public async Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        await (await Route(sessionId, ct)).ResizeAsync(sessionId, cols, rows, ct);

    public async Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        await (await Route(sessionId, ct)).KillAsync(sessionId, ct);

    public async Task<RunnerKillGenerationResult> KillGenerationAsync(
        Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
        await (await Route(sessionId, ct)).KillGenerationAsync(sessionId, expectedAcceptedStartedAt, ct);

    public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
        _directory.Local.StreamEventsAsync(ct);

    private async Task<ISessionRunnerClient> Route(Guid sessionId, CancellationToken ct)
    {
        var binding = await _directory.GetBindingAsync(sessionId, ct);
        return binding switch
        {
            SessionRunnerBinding.Missing => throw new NotFoundException("Session", sessionId),
            SessionRunnerBinding.Local => _directory.Local,
            SessionRunnerBinding.Remote remote => _directory.Resolve(remote.Owner.RunnerId),
            _ => _directory.Local,
        };
    }
}
