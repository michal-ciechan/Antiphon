using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed class AgentMentionRouter : IDisposable
{
    private const int MaxLineChars = 4096;
    private const int PendingMessageMinChars = 8;
    private static readonly TimeSpan PendingMentionDebounce = TimeSpan.FromMilliseconds(300);
    private readonly Channel<MentionRouteCommand> _commands = Channel.CreateBounded<MentionRouteCommand>(
        new BoundedChannelOptions(1024)
        {
            // A full queue refuses the new command. TryWrite fails immediately when the
            // channel is full; Wait applies only to WriteAsync. The drop is logged.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly ConcurrentDictionary<Guid, string> _pendingText = new();
    private readonly ConcurrentDictionary<Guid, PendingMentionState> _pendingStates = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MentionScanner _scanner;
    private readonly ILogger<AgentMentionRouter> _logger;
    private readonly MentionRouteDiagnostics? _diagnostics;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private int _disposed;
    private TimeProvider _clock = TimeProvider.System;

    // Test seam. Production leaves both null; a set gate pauses ProcessAsync before the route.
    internal TaskCompletionSource? BeforeRouteGate { get; set; }
    internal TaskCompletionSource? RouteGateEntered { get; set; }

    // Tests replace this so the 300 ms pending-line debounce can fire without a wall-clock sleep.
    internal TimeProvider Clock
    {
        get => _clock;
        set => _clock = value ?? TimeProvider.System;
    }

    public AgentMentionRouter(
        IServiceScopeFactory scopeFactory,
        MentionScanner scanner,
        ILogger<AgentMentionRouter> logger,
        MentionRouteDiagnostics? diagnostics = null)
    {
        _scopeFactory = scopeFactory;
        _scanner = scanner;
        _logger = logger;
        _diagnostics = diagnostics;
        _worker = Task.Run(ProcessAsync);
    }

    public void ObserveDelta(Guid sourceSessionId, string text) =>
        _ = ObserveCompletedAsync(sourceSessionId, text, capture: null, CancellationToken.None);

    // Returns once completed-line mentions are queued. It does not wait for the
    // serial worker, delivery, or a transcript confirmation. A null capture
    // (direct ObserveDelta, and every debounced remainder) leaves the route on
    // the live-status filter.
    internal async Task ObserveCompletedAsync(
        Guid sourceSessionId,
        string text,
        Func<CancellationToken, ValueTask<MentionSourceLiveness?>>? capture,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var stripped = MentionScanner.StripAnsi(text);
        Record(
            sourceSessionId,
            MentionRouteDiagnostics.DeltaObserved,
            $"chars={stripped.Length} newline={stripped.Contains('\n') || stripped.Contains('\r')}");
        var combined = _pendingText.AddOrUpdate(
            sourceSessionId,
            stripped,
            (_, existing) => existing + stripped);
        var lines = combined
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var completedLineCount = combined.EndsWith('\n') ? lines.Length : Math.Max(0, lines.Length - 1);
        var ready = new List<AgentMention>();
        for (var i = 0; i < completedLineCount; i++)
            ready.AddRange(MentionsToEnqueue(sourceSessionId, lines[i], skipPendingDuplicate: true));

        MentionSourceLiveness? observed = null;
        if (capture is not null && ready.Count > 0)
        {
            try
            {
                observed = await capture(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mention liveness for session {SessionId} was not recorded", sourceSessionId);
            }
        }

        foreach (var mention in ready)
            EnqueueCommand(sourceSessionId, mention, observed);
        for (var i = 0; i < completedLineCount; i++)
            _pendingStates.TryRemove(sourceSessionId, out _);

        var remainder = completedLineCount < lines.Length ? lines[^1] : string.Empty;
        if (remainder.Length > MaxLineChars)
            remainder = remainder[^MaxLineChars..];
        _pendingText[sourceSessionId] = remainder;
        SchedulePendingMentions(sourceSessionId, remainder);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _cts.Cancel();
        _commands.Writer.TryComplete();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort shutdown for background mention routing.
        }

        _cts.Dispose();
    }

    // Transcript confirmation waits out the delivery timeout. That wait must not
    // occupy the single reader, or the next completed line stays behind it.
    private void StartFlush(Guid sessionId)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        var token = _cts.Token;
        _ = FlushDetachedAsync(sessionId, token);
    }

    private async Task FlushDetachedAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();
            await queue.FlushIfIdleAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Accepted mention for session {SessionId} awaits queue recovery", sessionId);
        }
    }

    private List<AgentMention> MentionsToEnqueue(Guid sourceSessionId, string line, bool skipPendingDuplicate)
    {
        var ready = new List<AgentMention>();
        foreach (var mention in _scanner.Extract(line))
        {
            if (skipPendingDuplicate && PendingRouteExists(sourceSessionId, mention))
            {
                Record(sourceSessionId, MentionRouteDiagnostics.CommandSkippedDuplicate, $"target={mention.Target}");
                continue;
            }

            ready.Add(mention);
        }

        return ready;
    }

    private void SchedulePendingMentions(Guid sourceSessionId, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            _pendingStates.TryRemove(sourceSessionId, out _);
            Record(sourceSessionId, MentionRouteDiagnostics.PendingCleared);
            return;
        }

        var state = _pendingStates.GetOrAdd(sourceSessionId, _ => new PendingMentionState());
        long version;
        lock (state.Gate)
        {
            state.Text = line;
            version = ++state.Version;
        }

        Record(sourceSessionId, MentionRouteDiagnostics.PendingScheduled, $"version={version} remainderChars={line.Length}");
        _ = EnqueuePendingMentionsAfterDebounceAsync(sourceSessionId, version);
    }

    private async Task EnqueuePendingMentionsAfterDebounceAsync(Guid sourceSessionId, long version)
    {
        Record(sourceSessionId, MentionRouteDiagnostics.DebounceDelayStarted, $"version={version}");
        try
        {
            await Task.Delay(PendingMentionDebounce, _clock, _cts.Token);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            Record(sourceSessionId, MentionRouteDiagnostics.DebounceCancelled, $"version={version}");
            return;
        }

        if (!_pendingStates.TryGetValue(sourceSessionId, out var state))
        {
            Record(sourceSessionId, MentionRouteDiagnostics.DebounceStale, $"version={version} reason=state-gone");
            return;
        }

        string line;
        lock (state.Gate)
        {
            if (state.Version != version)
            {
                Record(sourceSessionId, MentionRouteDiagnostics.DebounceStale, $"version={version} current={state.Version}");
                return;
            }

            line = state.Text;
        }

        var mentions = _scanner.Extract(line);
        if (mentions.Count == 0)
        {
            Record(sourceSessionId, MentionRouteDiagnostics.DebounceNoMentions, $"lineChars={line.Length}");
            return;
        }

        lock (state.Gate)
        {
            if (state.Version != version)
            {
                Record(sourceSessionId, MentionRouteDiagnostics.DebounceStale, $"version={version} current={state.Version} after-extract");
                return;
            }

            Record(sourceSessionId, MentionRouteDiagnostics.DebounceFired, $"mentions={mentions.Count}");
            foreach (var mention in mentions)
            {
                if (!IsPendingMentionReady(mention))
                {
                    Record(sourceSessionId, MentionRouteDiagnostics.DebounceNotReady, $"target={mention.Target} messageChars={mention.Message.Length}");
                    continue;
                }

                var key = PendingRouteKey(mention);
                if (!state.RoutedKeys.Add(key))
                {
                    Record(sourceSessionId, MentionRouteDiagnostics.CommandSkippedDuplicate, $"target={mention.Target}");
                    continue;
                }

                EnqueueCommand(sourceSessionId, mention, observed: null);
            }
        }
    }

    private bool PendingRouteExists(Guid sourceSessionId, AgentMention mention)
    {
        if (!_pendingStates.TryGetValue(sourceSessionId, out var state))
            return false;

        lock (state.Gate)
        {
            return state.RoutedKeys.Contains(PendingRouteKey(mention));
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(_cts.Token))
            {
                Record(
                    command.SourceSessionId,
                    MentionRouteDiagnostics.CommandDequeued,
                    $"target={command.Mention.Target} messageChars={command.Mention.Message.Length}");
                try
                {
                    if (BeforeRouteGate is { } gate)
                    {
                        RouteGateEntered?.TrySetResult();
                        await gate.Task.WaitAsync(_cts.Token);
                    }

                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<AgentChannelService>();
                    var result = await service.RouteMentionAsync(
                        command.SourceSessionId,
                        command.Mention,
                        command.OccurrenceId,
                        _cts.Token,
                        command.ObservedLive,
                        deferFlush: true);
                    if (result.DeferredFlushSessionId is Guid flushSessionId)
                        StartFlush(flushSessionId);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Record(
                        command.SourceSessionId,
                        MentionRouteDiagnostics.RouteFailed,
                        $"{ex.GetType().Name}: {ex.Message}");
                    _logger.LogWarning(ex, "Failed to route mention from session {SessionId}", command.SourceSessionId);
                }
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    private sealed record MentionRouteCommand(
        Guid SourceSessionId, AgentMention Mention, Guid OccurrenceId, MentionSourceLiveness? ObservedLive);

    private sealed class PendingMentionState
    {
        public object Gate { get; } = new();
        public string Text { get; set; } = string.Empty;
        public long Version { get; set; }
        public HashSet<string> RoutedKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private void EnqueueCommand(Guid sourceSessionId, AgentMention mention, MentionSourceLiveness? observed)
    {
        var command = new MentionRouteCommand(sourceSessionId, mention, Guid.NewGuid(), observed);
        if (_commands.Writer.TryWrite(command))
        {
            Record(sourceSessionId, MentionRouteDiagnostics.CommandEnqueued, $"target={mention.Target}");
            return;
        }

        Record(sourceSessionId, MentionRouteDiagnostics.CommandDropped, $"target={mention.Target}");
        _logger.LogWarning("Dropped mention route command for source session {SessionId}", sourceSessionId);
    }

    private void Record(Guid sourceSessionId, string stage, string? detail = null)
    {
        if (_diagnostics is not null)
            _diagnostics.Record(sourceSessionId, stage, detail);
    }

    private static bool IsPendingMentionReady(AgentMention mention) =>
        string.IsNullOrWhiteSpace(mention.Message) || mention.Message.Length >= PendingMessageMinChars;

    private static string PendingRouteKey(AgentMention mention) => mention.Target;
}

/// <summary>
/// Source identity read when a completed mention line was queued.
/// <see cref="WasLive"/> is true only for Starting or Running at that read.
/// </summary>
internal readonly record struct MentionSourceLiveness(
    DateTime StartedAt,
    Guid? CardId,
    Guid? StandingAgentId,
    bool WasLive);

internal readonly record struct MentionRouteResult(bool Accepted, Guid? DeferredFlushSessionId);
