using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0162: long-lived herdr <c>events.subscribe</c> pump. Every event is a verification
/// TRIGGER, never evidence — herdr REPLAYS historical <c>pane_closed</c> to every new subscriber
/// (measured E5). Registered always; inert unless <see cref="HerdrSettings.Enabled"/>.
/// </summary>
public sealed class HerdrEventPumpService : BackgroundService
{
    private readonly SessionRunnerRuntime _runtime;
    private readonly HerdrClient _client;
    private readonly HerdrSettings _settings;
    private readonly ILogger<HerdrEventPumpService> _logger;

    public HerdrEventPumpService(
        SessionRunnerRuntime runtime,
        HerdrClient client,
        IOptions<HerdrSettings> settings,
        ILogger<HerdrEventPumpService> logger)
    {
        _runtime = runtime;
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Herdr event pump idle — SessionRunner:Herdr:Enabled is false");
            return;
        }

        var paneChanged = NewTcs();
        void OnPaneSetChanged() =>
            Interlocked.Exchange(ref paneChanged, NewTcs()).TrySetResult();

        _runtime.PaneSetChanged += OnPaneSetChanged;
        try
        {
            var backoffSeconds = Math.Max(1, _settings.EventsReconnectMinSeconds);
            var maxBackoff = Math.Max(backoffSeconds, _settings.EventsReconnectMaxSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_runtime.LiveHerdrPanes().Count == 0)
                {
                    _logger.LogDebug("Herdr event pump waiting — no live herdr sessions");
                    await WaitForPaneChangeOrCancelAsync(paneChanged.Task, stoppingToken);
                    paneChanged = NewTcs();
                    continue;
                }

                using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                void RecycleOnPaneChange() => streamCts.Cancel();
                _runtime.PaneSetChanged += RecycleOnPaneChange;
                try
                {
                    await RunOneStreamAsync(streamCts.Token);
                    backoffSeconds = Math.Max(1, _settings.EventsReconnectMinSeconds);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Herdr event pump recycling subscription after pane-set change");
                    backoffSeconds = Math.Max(1, _settings.EventsReconnectMinSeconds);
                }
                catch (HerdrApiException ex) when (string.Equals(ex.Code, "pane_not_found", StringComparison.Ordinal))
                {
                    _logger.LogWarning(ex, "Herdr subscribe pane_not_found — verifying set and retrying");
                    await BaselineSweepAsync(stoppingToken);
                    backoffSeconds = Math.Max(1, _settings.EventsReconnectMinSeconds);
                }
                catch (Exception ex) when (ex is HerdrBackendUnavailableException
                                           or HerdrProtocolException
                                           or IOException)
                {
                    _logger.LogWarning(ex,
                        "Herdr event stream dropped; reconnecting in {Backoff}s", backoffSeconds);
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    backoffSeconds = Math.Min(backoffSeconds * 2, maxBackoff);
                }
                finally
                {
                    _runtime.PaneSetChanged -= RecycleOnPaneChange;
                }
            }
        }
        finally
        {
            _runtime.PaneSetChanged -= OnPaneSetChanged;
        }
    }

    private async Task RunOneStreamAsync(CancellationToken ct)
    {
        var live = _runtime.LiveHerdrPanes();
        if (live.Count == 0)
            return;

        // Baseline before the stream: herdr is change-only (E3) so no snapshot event arrives on
        // subscribe. Reconnect verification (§6B) is this same sweep.
        await BaselineSweepAsync(ct);
        live = _runtime.LiveHerdrPanes();
        if (live.Count == 0)
            return;

        var subscriptions = BuildSubscriptions(live);
        var byPane = live.ToDictionary(p => p.PaneId, p => p, StringComparer.Ordinal);

        await foreach (var evt in _client.SubscribeEventsAsync(subscriptions, ct))
            await HandleEventAsync(evt, byPane, ct);

        throw new HerdrBackendUnavailableException("Herdr event stream ended without a closing event.");
    }

    private static List<HerdrSubscription> BuildSubscriptions(
        IReadOnlyList<SessionRunnerRuntime.LiveHerdrPane> live)
    {
        var subscriptions = new List<HerdrSubscription>
        {
            new(HerdrEventTypes.PaneClosedSubscribe),
            new(HerdrEventTypes.PaneExitedSubscribe),
        };
        foreach (var pane in live)
            subscriptions.Add(new HerdrSubscription(HerdrEventTypes.PaneAgentStatusChangedSubscribe, pane.PaneId));
        return subscriptions;
    }

    private async Task HandleEventAsync(
        HerdrEvent evt,
        Dictionary<string, SessionRunnerRuntime.LiveHerdrPane> byPane,
        CancellationToken ct)
    {
        if (string.Equals(evt.Name, HerdrEventTypes.PaneClosedWire, StringComparison.Ordinal)
            || string.Equals(evt.Name, HerdrEventTypes.PaneExitedWire, StringComparison.Ordinal))
        {
            var closed = JsonSerializer.Deserialize<HerdrPaneClosedEventData>(evt.Data.GetRawText());
            if (closed is null || !byPane.TryGetValue(closed.PaneId, out var tracked))
            {
                _logger.LogDebug("Ignoring herdr {Event} for untracked pane", evt.Name);
                return;
            }

            await tracked.Session.VerifyHerdrLivenessAsync(_client, ct);
            return;
        }

        if (string.Equals(evt.Name, HerdrEventTypes.PaneAgentStatusChangedWire, StringComparison.Ordinal))
        {
            var status = JsonSerializer.Deserialize<HerdrPaneStatusEventData>(evt.Data.GetRawText());
            if (status is null || !byPane.TryGetValue(status.PaneId, out var tracked))
            {
                _logger.LogDebug("Ignoring herdr status event for untracked pane");
                return;
            }

            tracked.Session.ApplyHerdrAgentStatus(status.AgentStatus, DateTime.UtcNow);
            return;
        }

        _logger.LogDebug("Ignoring unhandled herdr event {Event}", evt.Name);
    }

    private async Task BaselineSweepAsync(CancellationToken ct)
    {
        foreach (var pane in _runtime.LiveHerdrPanes())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = await _client.PaneGetAsync(pane.PaneId, ct);
                if (info.AgentStatus is { } status)
                    pane.Session.ApplyHerdrAgentStatus(status, DateTime.UtcNow);
                await pane.Session.VerifyHerdrLivenessAsync(_client, ct);
            }
            catch (HerdrBackendUnavailableException)
            {
                return;
            }
            catch (HerdrApiException ex)
            {
                _logger.LogDebug(ex, "Baseline pane.get failed for {PaneId}", pane.PaneId);
                await pane.Session.VerifyHerdrLivenessAsync(_client, ct);
            }
        }
    }

    private static TaskCompletionSource NewTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitForPaneChangeOrCancelAsync(Task paneChanged, CancellationToken ct)
    {
        var cancelTcs = NewTcs();
        await using var reg = ct.Register(() => cancelTcs.TrySetCanceled(ct));
        await Task.WhenAny(paneChanged, cancelTcs.Task);
        ct.ThrowIfCancellationRequested();
    }
}
