using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Infrastructure.Supervision;

/// <summary>Production side-effects for session health repair (see ISessionHealthActions).</summary>
public sealed class SessionHealthActions : ISessionHealthActions
{
    private readonly SessionMessageQueueService _queue;
    private readonly AgentSessionService _sessions;
    private readonly ISessionRunnerClient _runner;
    private readonly AgentSessionRuntime _runtime;
    private readonly IServiceScopeFactory _scopeFactory;

    public SessionHealthActions(
        SessionMessageQueueService queue,
        AgentSessionService sessions,
        ISessionRunnerClient runner,
        AgentSessionRuntime runtime,
        IServiceScopeFactory scopeFactory)
    {
        _queue = queue;
        _sessions = sessions;
        _runner = runner;
        _runtime = runtime;
        _scopeFactory = scopeFactory;
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

    /// <summary>
    /// CARD-0292 S5: the exact plumbing <c>SessionMessageQueueService.TryDismissOverlayAsync</c>
    /// uses — fetch the rendered screen, match <see cref="RemoteControlMenuScreen"/>, and only if
    /// the menu is present AND a fresh transcript pull says the session is idle, send one Esc via
    /// <see cref="AgentSessionRuntime.SendInputAsync"/> with <c>trackManualTurn: false</c>. A
    /// wrong premise (an armed-but-dead bridge that reconnected cleanly) sees no menu and costs
    /// one snapshot.
    /// </summary>
    public async Task<bool> TryDismissRemoteControlMenuAsync(Guid sessionId, CancellationToken ct)
    {
        string screen;
        try
        {
            screen = (await _runner.GetSnapshotAsync(sessionId, ct)).RenderedScreen;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }

        if (!RemoteControlMenuScreen.IsPresent(screen))
            return false;

        // Idle-after-pull guard (CARD-0137's discipline): the menu evidence is a screen read, and
        // a working session must never receive a speculative keystroke.
        await _runtime.CatchUpTranscriptAsync(sessionId, ct);
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct))
                return false;
        }

        await _runtime.SendInputAsync(sessionId, "\u001b", ct, trackManualTurn: false);
        return true;
    }
}
