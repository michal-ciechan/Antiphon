using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>Production side-effects for session health repair (see ISessionHealthActions).</summary>
public sealed class SessionHealthActions : ISessionHealthActions
{
    private readonly SessionMessageQueueService _queue;
    private readonly AgentSessionService _sessions;
    private readonly ISessionRunnerClient _runner;

    public SessionHealthActions(
        SessionMessageQueueService queue,
        AgentSessionService sessions,
        ISessionRunnerClient runner)
    {
        _queue = queue;
        _sessions = sessions;
        _runner = runner;
    }

    public async Task EnqueueWhenIdleAsync(Guid sessionId, string text, CancellationToken ct) =>
        await _queue.EnqueueAsync(sessionId, text, MessageSendMode.WhenIdle, ct);

    public Task KillSessionAsync(Guid sessionId, CancellationToken ct) =>
        _sessions.KillAsync(sessionId, ct);

    public async Task<string> SnapshotScreenAsync(Guid sessionId, CancellationToken ct)
    {
        var snapshot = await _runner.GetSnapshotAsync(sessionId, ct);
        return snapshot.RenderedScreen;
    }

    public Task SendRawInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        _runner.SendInputAsync(sessionId, input, ct);
}
