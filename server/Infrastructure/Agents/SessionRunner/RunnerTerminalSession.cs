using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

internal sealed class RunnerTerminalSession
{
    private readonly ISessionRunnerClient _client;
    private Guid _sessionId;
    private bool _started;

    public RunnerTerminalSession(ISessionRunnerClient client)
    {
        _client = client;
    }

    public Guid SessionId => _sessionId;
    public DateTime StartedAt { get; private set; }
    public int? Pid { get; private set; }
    public AgentExitReason ExitReason { get; private set; } = AgentExitReason.Unknown;
    public Task<int> Exited { get; private set; } = Task.FromResult(0);

    public async Task StartAsync(AgentLaunchSpec spec, CancellationToken ct)
    {
        if (_started)
            throw new InvalidOperationException("Runner terminal session already started.");
        if (spec.SessionId is not Guid sessionId || sessionId == Guid.Empty)
            throw new InvalidOperationException("AgentLaunchSpec.SessionId is required for session runner launches.");

        _sessionId = sessionId;
        var session = await _client.StartAsync(sessionId, spec, ct);
        StartedAt = session.StartedAt;
        Pid = session.Pid;
        ExitReason = session.ExitReason;
        Exited = WaitForExitAsync(sessionId, CancellationToken.None);
        _started = true;
    }

    /// <summary>
    /// Adopt a runner session this process did not start (CARD-0340). Requires the runner to
    /// report Running with no Pending herdr wait. <see cref="StartedAt"/> is copied from the
    /// runner so Claude/Grok <c>MinTotalWait</c> floors are already satisfied on a minutes-old
    /// process.
    /// </summary>
    public async Task AttachAsync(Guid sessionId, CancellationToken ct)
    {
        if (_started)
            throw new InvalidOperationException("Runner terminal session already started.");
        if (sessionId == Guid.Empty)
            throw new InvalidOperationException("sessionId is required to attach a runner session.");

        SessionRunnerSessionDto session;
        try
        {
            session = await _client.GetAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Cannot attach: session runner does not know session {sessionId}.", ex);
        }

        if (!string.Equals(session.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot attach: runner session {sessionId} is {session.Status}.");
        }

        if (!string.IsNullOrEmpty(session.Pending))
        {
            throw new InvalidOperationException(
                $"Cannot attach: runner session {sessionId} is pending ({session.Pending}).");
        }

        _sessionId = sessionId;
        StartedAt = session.StartedAt;
        Pid = session.Pid;
        ExitReason = session.ExitReason;
        Exited = WaitForExitAsync(sessionId, CancellationToken.None);
        _started = true;
    }

    public async Task WriteAsync(string input, CancellationToken ct)
    {
        EnsureStarted();
        await _client.SendInputAsync(_sessionId, input, ct);
    }

    public async Task SendLineAsync(string line, CancellationToken ct)
    {
        EnsureStarted();
        // LF-normalized and bracket-wrapped when multi-line (PtyInputEncoding REQUIREMENT). This
        // path delivered the interactive/card boot prompts RAW: a CRLF prompt's trailing CR fell
        // inside the TUI's paste window, folded to a newline, and the whole prompt sat stranded
        // unsubmitted in the composer (live miss 2026-08-08 — "agent never came back" after a
        // supervised restart). The submitting CR stays a separate, delayed, unbracketed write.
        await _client.SendInputAsync(_sessionId, PtyInputEncoding.EncodeBody(line), ct);
        await Task.Delay(20, ct);
        await _client.SendInputAsync(_sessionId, "\r", ct);
    }

    public async Task ClearLiveBufferAsync(CancellationToken ct)
    {
        EnsureStarted();
        await _client.ClearLiveBufferAsync(_sessionId, ct);
    }

    public async Task<string> SnapshotTextAsync(CancellationToken ct)
    {
        EnsureStarted();
        return (await _client.GetSnapshotAsync(_sessionId, ct)).RawOutput;
    }

    public async Task<string> SnapshotScreenAsync(CancellationToken ct)
    {
        EnsureStarted();
        return (await _client.GetSnapshotAsync(_sessionId, ct)).RenderedScreen;
    }

    public async Task<long> GetLastSequenceAsync(CancellationToken ct)
    {
        EnsureStarted();
        return (await _client.GetBufferAsync(_sessionId, ct)).LastSequence;
    }

    public async Task<bool> WaitForOutputAsync(
        Func<string, bool> predicate,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(await SnapshotTextAsync(ct)))
                return true;

            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }
        }

        return false;
    }

    public async Task<bool> WaitForQuietAsync(
        TimeSpan quietPeriod,
        TimeSpan maxWait,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + maxWait;
        var lastSequence = await GetLastSequenceAsync(ct);
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }

            var currentSequence = await GetLastSequenceAsync(ct);
            if (currentSequence != lastSequence)
            {
                lastSequence = currentSequence;
                lastChange = DateTime.UtcNow;
                continue;
            }

            if (DateTime.UtcNow - lastChange >= quietPeriod)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Quiet cannot count until the snapshot has visible child output (CARD-0052).
    /// Lockstep with <c>PtyAgentRunner.WaitForQuietAfterVisibleAsync</c>: poll until
    /// visible, then wait <paramref name="quietPeriod"/> of no further sequence
    /// change. Returns false if <paramref name="maxWait"/> expires at either stage.
    /// </summary>
    public async Task<bool> WaitForQuietAfterVisibleAsync(
        TimeSpan quietPeriod,
        TimeSpan maxWait,
        CancellationToken ct,
        Func<CancellationToken, Task>? observeAsync = null)
    {
        var deadline = DateTime.UtcNow + maxWait;

        while (DateTime.UtcNow < deadline)
        {
            if (observeAsync is not null)
                await observeAsync(ct);

            if (VisiblePtyOutput.HasVisibleOutput(await SnapshotTextAsync(ct)))
                break;

            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }
        }

        if (!VisiblePtyOutput.HasVisibleOutput(await SnapshotTextAsync(ct)))
            return false;

        var lastSequence = await GetLastSequenceAsync(ct);
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            if (observeAsync is not null)
                await observeAsync(ct);

            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { return false; }

            var currentSequence = await GetLastSequenceAsync(ct);
            if (currentSequence != lastSequence)
            {
                lastSequence = currentSequence;
                lastChange = DateTime.UtcNow;
                continue;
            }

            if (DateTime.UtcNow - lastChange >= quietPeriod)
                return true;
        }

        return false;
    }

    public async Task ResizeAsync(int cols, int rows, CancellationToken ct)
    {
        EnsureStarted();
        await _client.ResizeAsync(_sessionId, cols, rows, ct);
    }

    public async Task<bool> KillAsync(CancellationToken ct)
    {
        EnsureStarted();
        var session = await _client.KillAsync(_sessionId, ct);
        ExitReason = session.ExitReason;
        return session.Status == "Exited" || session.ExitCode is not null;
    }

    private async Task<int> WaitForExitAsync(Guid sessionId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var session = await _client.GetAsync(sessionId, ct);
                if (session.Status == "Exited")
                {
                    ExitReason = session.ExitReason;
                    return session.ExitCode ?? 0;
                }
            }
            catch
            {
                return -1;
            }

            await Task.Delay(250, ct);
        }

        return -1;
    }

    private void EnsureStarted()
    {
        if (!_started)
            throw new InvalidOperationException("Runner terminal session not started.");
    }
}
