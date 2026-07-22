using System.Collections.Concurrent;
using Antiphon.Messaging;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Same-sender inbound debounce (the Hermes rule): rapid-fire messages from ONE sender in ONE
/// conversation merge, newline-joined, into a single routed prompt. The debounce key is
/// (conversation, author) — deliberately NOT per-conversation, because the merged flush uses a
/// single envelope header and that header must stay truthful about who spoke. A sliding quiet
/// window (<see cref="ChannelBridgeSettings.DebounceWindowMs"/>) defers the flush while the sender
/// keeps typing; a hard cap from the FIRST buffered message
/// (<see cref="ChannelBridgeSettings.DebounceMaxMs"/>) bounds the deferral. Window 0 = passthrough.
///
/// Timers run on <see cref="TimeProvider"/> delays so FakeTimeProvider drives tests. Flush
/// callbacks run OUTSIDE the bridge's awaited consume loop — the callback wrapper supplied by the
/// bridge owns failure alerting (degradation is late-or-alerted, never silent loss).
/// </summary>
public sealed class ChannelInboundDebouncer
{
    /// <summary>One buffered inbound message (the newest carries routing metadata at flush).</summary>
    public sealed record Buffered(ChannelMessage Message, DateTimeOffset ArrivedAt);

    private sealed class Lane
    {
        public readonly List<Buffered> Items = [];
        public required Func<IReadOnlyList<Buffered>, Task> Flush;
        public DateTimeOffset FirstAt;
        public DateTimeOffset LastAt;
        public Task? Timer;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Lane> _lanes = new(StringComparer.Ordinal);
    private readonly ChannelBridgeSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChannelInboundDebouncer> _logger;

    public ChannelInboundDebouncer(
        IOptions<ChannelBridgeSettings> settings,
        TimeProvider timeProvider,
        ILogger<ChannelInboundDebouncer> logger)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Lanes currently holding buffered messages (test/diagnostic surface).</summary>
    public int PendingLanes
    {
        get { lock (_gate) return _lanes.Count; }
    }

    /// <summary>
    /// Buffer one message; <paramref name="flush"/> fires with the merged batch once the lane goes
    /// quiet (or immediately when the window is 0). Messages from different senders or different
    /// conversations never share a lane.
    /// </summary>
    public async Task AddAsync(ChannelMessage message, Func<IReadOnlyList<Buffered>, Task> flush, CancellationToken ct)
    {
        if (_settings.DebounceWindowMs <= 0)
        {
            await flush([new Buffered(message, _timeProvider.GetUtcNow())]);
            return;
        }

        var key = $"{message.Channel}:{message.Conversation.Id}:{message.Author.Id}";
        var now = _timeProvider.GetUtcNow();
        var startTimer = false;

        Lane lane;
        lock (_gate)
        {
            if (!_lanes.TryGetValue(key, out lane!))
            {
                // The lane keeps the callback from its FIRST message — same conversation and
                // sender, so the captured routing context is identical for every later message.
                lane = new Lane { FirstAt = now, Flush = flush };
                _lanes[key] = lane;
                startTimer = true;
            }
            lane.Items.Add(new Buffered(message, now));
            lane.LastAt = now;
        }

        if (startTimer)
            lane.Timer = RunLaneAsync(key, lane, ct);
    }

    /// <summary>Flush every lane immediately through its own stored callback (shutdown drain).</summary>
    public async Task FlushAllAsync()
    {
        List<Lane> drained;
        lock (_gate)
        {
            drained = _lanes.Values.ToList();
            _lanes.Clear();
        }
        foreach (var lane in drained.Where(l => l.Items.Count > 0))
        {
            try
            {
                await lane.Flush(lane.Items);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Shutdown drain flush threw ({Count} message(s) affected)", lane.Items.Count);
            }
        }
    }

    private async Task RunLaneAsync(string key, Lane lane, CancellationToken ct)
    {
        var window = TimeSpan.FromMilliseconds(_settings.DebounceWindowMs);
        var cap = TimeSpan.FromMilliseconds(Math.Max(_settings.DebounceMaxMs, _settings.DebounceWindowMs));

        List<Buffered> items;
        while (true)
        {
            try
            {
                await Task.Delay(window, _timeProvider, ct);
            }
            catch (OperationCanceledException)
            {
                // Shutdown: leave the lane for FlushAllAsync's drain.
                return;
            }

            var now = _timeProvider.GetUtcNow();
            lock (_gate)
            {
                var quiet = now - lane.LastAt >= window;
                var capped = now - lane.FirstAt >= cap;
                if (!quiet && !capped)
                    continue; // sender still typing and cap not hit — keep sliding

                items = new List<Buffered>(lane.Items);
                _lanes.Remove(key);
            }
            break;
        }

        if (items.Count == 0)
            return;

        // The flush callback owns its own failure handling (the bridge wraps it with alerting);
        // this catch is the last line so a rogue callback can never fault an unobserved task.
        try
        {
            await lane.Flush(items);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Debounce flush for lane {Key} threw ({Count} message(s) affected)", key, items.Count);
        }
    }
}
