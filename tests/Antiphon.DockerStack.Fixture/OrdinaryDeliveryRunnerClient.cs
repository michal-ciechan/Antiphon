using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.DockerStack.Fixture;

public sealed class RecordingRunnerClient : ISessionRunnerClient
{
    public List<(Guid SessionId, string Input)> Inputs { get; } = new();
    public List<SessionRunnerEvent> Events { get; } = new();
    public SessionRunnerTranscriptDto Transcript { get; set; } =
        new(Guid.Empty, Array.Empty<SessionRunnerTranscriptEvent>(), 0);

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
        Task.FromResult(Session(sessionId));

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(Array.Empty<SessionRunnerSessionDto>());

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(Session(sessionId));

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerBufferDto(sessionId, "", 0));

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerSnapshotDto(sessionId, "", "", 0, DateTime.UnixEpoch));

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(Transcript);

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        Inputs.Add((sessionId, input));
        return Task.CompletedTask;
    }

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(Session(sessionId));

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var ev in Events)
        {
            ct.ThrowIfCancellationRequested();
            yield return ev;
            await Task.Yield();
        }
    }

    private static SessionRunnerSessionDto Session(Guid sessionId) =>
        new(sessionId, null, DateTime.UnixEpoch, "Running", null, AgentExitReason.Unknown, 0);
}

public sealed class OrdinaryDeliveryRunnerClient : ISessionRunnerClient
{
    private readonly ISessionRunnerClient _inner;
    private readonly DeliveryGate _gate;

    public OrdinaryDeliveryRunnerClient(ISessionRunnerClient inner, DeliveryGate gate)
    {
        _inner = inner;
        _gate = gate;
    }

    public ISessionRunnerClient Inner => _inner;

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        var carriage = input == "\r";
        if (_gate.IsTarget(sessionId) && carriage && _gate.IsArmed("body-before-enter"))
            return WaitThenSend(sessionId, input, ct);
        return _inner.SendInputAsync(sessionId, input, ct);
    }

    private async Task WaitThenSend(Guid sessionId, string input, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        await _inner.SendInputAsync(sessionId, input, ct);
    }

    public async Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
    {
        if (_gate.IsTarget(sessionId) && _gate.IsArmed("recipient-before-ingestion"))
            await _gate.WaitAsync(ct);
        return await _inner.GetTranscriptAsync(sessionId, ct);
    }

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var ev in _inner.StreamEventsAsync(ct))
        {
            if (ev.Transcript is { Kind: "UserPrompt" } && _gate.IsArmed("recipient-before-ingestion") && _gate.IsTarget(ev.SessionId))
                await _gate.WaitAsync(ct);
            yield return ev;
        }
    }

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
        _inner.StartAsync(sessionId, spec, ct);

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => _inner.ListAsync(ct);

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => _inner.GetAsync(sessionId, ct);

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        _inner.GetBufferAsync(sessionId, ct);

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
        _inner.GetSnapshotAsync(sessionId, ct);

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => _inner.ClearLiveBufferAsync(sessionId, ct);

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        _inner.ResizeAsync(sessionId, cols, rows, ct);

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) => _inner.KillAsync(sessionId, ct);
}
