using System.Collections.Concurrent;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0163: mirrors the file-ordered transcript judgement into Herdr's display-only metadata.
/// It never reports an agent state, sends input, or reads its own labels back as evidence.
/// </summary>
public sealed class HerdrStatusPushService : BackgroundService
{
    private static readonly string[] HerdrStates = ["idle", "working", "blocked", "done", "unknown"];
    private readonly SessionRunnerRuntime _runtime;
    private readonly HerdrClient _client;
    private readonly HerdrSettings _settings;
    private readonly ILogger<HerdrStatusPushService> _logger;
    private readonly ConcurrentDictionary<Guid, PushState> _states = new();

    public HerdrStatusPushService(SessionRunnerRuntime runtime, HerdrClient client,
        IOptions<HerdrSettings> settings, ILogger<HerdrStatusPushService> logger)
    {
        _runtime = runtime;
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || !_settings.StatusPush.Enabled)
            return;

        var events = _runtime.Subscribe(stoppingToken);
        var heartbeat = TimeSpan.FromSeconds(Math.Max(1, _settings.StatusPush.HeartbeatSeconds));
        var nextHeartbeat = Task.Delay(heartbeat, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = events.WaitToReadAsync(stoppingToken).AsTask();
            var winner = await Task.WhenAny(wait, nextHeartbeat);
            if (winner == nextHeartbeat)
            {
                foreach (var pane in _runtime.LiveHerdrPanes())
                    Trigger(pane.SessionId, pane.PaneId, immediate: true, stoppingToken);
                nextHeartbeat = Task.Delay(heartbeat, stoppingToken);
                continue;
            }

            if (!await wait)
                return;
            while (events.TryRead(out var evt))
                Handle(evt, stoppingToken);
        }
    }

    private void Handle(RunnerServerSentEvent evt, CancellationToken stoppingToken)
    {
        Guid sessionId;
        try
        {
            sessionId = evt.EventName switch
            {
                SessionRunnerEventNames.SessionTranscript => JsonSerializer.Deserialize<RunnerTranscriptEvent>(evt.Json)?.SessionId ?? Guid.Empty,
                SessionRunnerEventNames.SessionTranscriptBound => JsonSerializer.Deserialize<RunnerTranscriptBoundEvent>(evt.Json)?.SessionId ?? Guid.Empty,
                SessionRunnerEventNames.SessionTranscriptFault => JsonSerializer.Deserialize<RunnerTranscriptFaultEvent>(evt.Json)?.SessionId ?? Guid.Empty,
                SessionRunnerEventNames.SessionStarted => JsonSerializer.Deserialize<RunnerSessionStartedEvent>(evt.Json)?.SessionId ?? Guid.Empty,
                SessionRunnerEventNames.SessionAdopted => JsonSerializer.Deserialize<RunnerSessionAdoptedEvent>(evt.Json)?.SessionId ?? Guid.Empty,
                SessionRunnerEventNames.SessionExited => JsonSerializer.Deserialize<RunnerSessionExitedEvent>(evt.Json)?.SessionId ?? Guid.Empty,
                _ => Guid.Empty,
            };
        }
        catch (JsonException)
        {
            return;
        }
        if (sessionId == Guid.Empty)
            return;

        if (evt.EventName == SessionRunnerEventNames.SessionExited)
        {
            if (_states.TryRemove(sessionId, out var exited))
                _ = ClearAsync(exited, stoppingToken);
            return;
        }

        var pane = _runtime.LiveHerdrPanes().FirstOrDefault(p => p.SessionId == sessionId);
        if (pane.SessionId != sessionId)
            return;
        Trigger(sessionId, pane.PaneId, immediate: evt.EventName == SessionRunnerEventNames.SessionAdopted, stoppingToken);
    }

    private void Trigger(Guid sessionId, string paneId, bool immediate, CancellationToken stoppingToken)
    {
        var state = _states.GetOrAdd(sessionId, _ => new PushState(paneId));
        state.PaneId = paneId;
        state.Debounce?.Cancel();
        state.Debounce?.Dispose();
        var debounce = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        state.Debounce = debounce;
        _ = PushAfterDebounceAsync(sessionId, state, immediate ? 0 : Math.Max(0, _settings.StatusPush.DebounceMs), debounce.Token);
    }

    private async Task PushAfterDebounceAsync(Guid sessionId, PushState state, int milliseconds, CancellationToken ct)
    {
        try
        {
            if (milliseconds > 0)
                await Task.Delay(milliseconds, ct);
            await PushAsync(sessionId, state, force: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Herdr status metadata push failed for {SessionId}; it will retry", sessionId);
        }
    }

    private async Task PushAsync(Guid sessionId, PushState state, bool force, CancellationToken ct)
    {
        var dto = _runtime.Get(sessionId);
        if (dto.Pending is not null)
            return;
        var transcript = _runtime.GetTranscript(sessionId);
        var (verdict, reason) = Classify(dto, transcript);
        var asOf = transcript.Entries.Select(e => e.Timestamp).Where(t => t is not null).Max();
        var now = DateTime.UtcNow;
        if (!force && state.Verdict == verdict && state.Reason == reason
            && now - state.LastPushUtc < TimeSpan.FromSeconds(Math.Max(1, _settings.StatusPush.HeartbeatSeconds)))
            return;

        var labels = HerdrStates.ToDictionary(s => s, s => $"{s} · antiphon: {verdict}");
        var tokens = new Dictionary<string, string?>
        {
            ["antiphon-state"] = reason is null ? verdict : $"{verdict}:{reason}",
            ["antiphon-as-of"] = asOf?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };
        await _client.PaneReportMetadataAsync(new HerdrPaneReportMetadataParams(
            state.PaneId, HerdrSources.Antiphon, tokens, TtlMs: Math.Max(1, _settings.StatusPush.TtlSeconds) * 1000L,
            Seq: checked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), StateLabels: labels), ct);
        state.Verdict = verdict;
        state.Reason = reason;
        state.LastPushUtc = now;
    }

    private async Task ClearAsync(PushState state, CancellationToken stoppingToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(Math.Max(1, _settings.StatusPush.ExitClearTimeoutMs));
            await _client.PaneReportMetadataAsync(new HerdrPaneReportMetadataParams(state.PaneId, HerdrSources.Antiphon,
                new Dictionary<string, string?> { ["antiphon-state"] = null, ["antiphon-as-of"] = null },
                ClearStateLabels: true, Seq: checked((ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())), timeout.Token);
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not clear Herdr display metadata for exited session pane {PaneId}", state.PaneId);
        }
    }

    private static (string Verdict, string? Reason) Classify(RunnerSessionDto dto, RunnerTranscriptDto transcript)
    {
        if (dto.TranscriptBound is null)
            return ("unknown", "no-transcript");
        if (dto.TranscriptBound == false)
            return ("unknown", dto.TranscriptUnboundReason ?? "unbound");
        if (transcript.Entries.Count == 0)
            return ("unknown", "empty");
        return TranscriptWorkingState.Classify(transcript.Entries) switch
        {
            TranscriptWorkingState.WorkingVerdict.Working => ("working", null),
            TranscriptWorkingState.WorkingVerdict.Idle => ("idle", null),
            _ => ("unknown", "empty"),
        };
    }

    private sealed class PushState(string paneId)
    {
        public string PaneId { get; set; } = paneId;
        public string? Verdict { get; set; }
        public string? Reason { get; set; }
        public DateTime LastPushUtc { get; set; }
        public CancellationTokenSource? Debounce { get; set; }
    }
}
