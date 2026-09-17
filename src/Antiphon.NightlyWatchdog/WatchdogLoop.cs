using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.NightlyWatchdog;

/// <summary>
/// CARD-0545 tick orchestration: probe Windmill, evaluate D-3 outages into the ledger (intent committed
/// before any network call), read the recipient's view first, import receipts, then send or retry per
/// D-7, and record the heartbeat the snapshot serves. Nothing here reads Windows-side state.
/// </summary>
public sealed class WatchdogLoop
{
    private const string HeldSinceCounter = "readerHeldSinceUnix";
    private const string LastReadCounter = "readerLastReadUnix";

    private readonly WatchdogOptions _options;
    private readonly Ledger _ledger;
    private readonly IWindmillApi _windmill;
    private readonly INotificationTransport _transport;
    private readonly IRecipientReader _reader;
    private readonly TimeProvider _clock;
    private readonly Action<CrashPoint>? _crashHook;
    private readonly ILogger _logger;
    private readonly ReceiptImporter _importer;

    public WatchdogLoop(WatchdogOptions options, Ledger ledger, IWindmillApi windmill, INotificationTransport transport,
        IRecipientReader reader, TimeProvider clock, Action<CrashPoint>? crashHook = null, ILogger? logger = null)
    {
        _options = options;
        _ledger = ledger;
        _windmill = windmill;
        _transport = transport;
        _reader = reader;
        _clock = clock;
        _crashHook = crashHook;
        _logger = logger ?? NullLogger.Instance;
        _importer = new ReceiptImporter(ledger, options);
    }

    /// <summary>
    /// D-11: the fault hook exists only when validation allows it (never in the production namespace).
    /// Production passes <see cref="Environment.FailFast(string)"/> as <paramref name="crash"/>.
    /// </summary>
    public static Action<CrashPoint>? CreateFaultHook(WatchdogOptions options, Action<CrashPoint> crash)
    {
        if (options.Validate().Contains("fault-injection-forbidden")) return null;
        if (!options.AllowFaultInjection || string.IsNullOrWhiteSpace(options.CrashAfter)) return null;
        CrashPoint? target = options.CrashAfter.Trim().ToLowerInvariant() switch
        {
            "intent" => CrashPoint.Intent,
            "send-before-response" => CrashPoint.SendBeforeResponse,
            "transport-accepted" => CrashPoint.TransportAccepted,
            "reader-observation" => CrashPoint.ReaderObservation,
            _ => null,
        };
        return target is null ? null : point => { if (point == target) crash(point); };
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync(ct);
            }
            catch (WatchdogCrashException) { throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError("tick failed: {Error}", Redact(ex.Message));
            }
            try { await Task.Delay(TimeSpan.FromSeconds(_options.TickSeconds), _clock, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task<TickReport> TickAsync(CancellationToken ct = default)
    {
        EnsureDestinationHash();
        var probes = await ProbeAsync(ct);
        var evaluation = OutageEvaluator.Evaluate(probes, _ledger.Outages(), _ledger.Counters(), _options);
        var transitions = new List<OutageTransition>();

        foreach (var decision in evaluation.Decisions)
        {
            switch (decision.Change)
            {
                case OutageChange.Opened:
                {
                    var (outage, notification, _) = _ledger.OpenOutageWithIntent(decision.Kind, decision.DueDay, decision.JobId,
                        decision.RunId, decision.Evidence, oid => FailureContent(decision, oid));
                    transitions.Add(new OutageTransition(decision.Kind, outage.OutageId, OutageChange.Opened, notification.Nid));
                    _logger.LogWarning("outage opened {OutageId} nid {Nid}", outage.OutageId, notification.Nid);
                    _crashHook?.Invoke(CrashPoint.Intent);
                    break;
                }
                case OutageChange.Closed:
                {
                    var recovery = _ledger.CloseOutageWithRecovery(decision.OutageId!, decision.Evidence,
                        (outage, failureReceived) => RecoveryContent(outage, decision, failureReceived));
                    transitions.Add(new OutageTransition(decision.Kind, decision.OutageId!, OutageChange.Closed, recovery.Nid));
                    _logger.LogInformation("outage closed {OutageId} recovery nid {Nid}", decision.OutageId, recovery.Nid);
                    break;
                }
                case OutageChange.Amended:
                    _ledger.AmendOutage(decision.OutageId!, evidence =>
                    {
                        foreach (var (key, value) in decision.Evidence)
                        {
                            if (key == "addJobId")
                            {
                                var ids = evidence["jobIds"] as JsonArray ?? new JsonArray();
                                evidence["jobIds"] = ids;
                                var id = value!.GetValue<string>();
                                if (!ids.Any(n => n?.GetValue<string>() == id)) ids.Add(id);
                            }
                            else
                            {
                                evidence[key] = value?.DeepClone();
                            }
                        }
                    });
                    transitions.Add(new OutageTransition(decision.Kind, decision.OutageId!, OutageChange.Amended, null));
                    break;
            }
        }
        foreach (var (key, value) in evaluation.Counters)
            _ledger.SetCounter(key, value);
        if (evaluation.LastDueDay is not null)
            _ledger.SetLastDueDay(evaluation.LastDueDay);
        _ledger.SaveProbe(evaluation.ProbeRecord);

        var (imports, readerState) = await ReadBackAsync(ct);
        var sends = new List<SendRecord>();
        foreach (var notification in _ledger.PendingNotifications())
        {
            var record = await DeliverAsync(notification, readerState == ReaderState.Held, ct);
            if (record is not null) sends.Add(record);
        }

        _ledger.UpdateHeartbeat(HeartbeatState(probes, readerState), null);
        return new TickReport(transitions, sends, imports, readerState);
    }

    /// <summary>DL-545-C: the ledger-backed qualification notice, delivered through the same path.</summary>
    public async Task<NotificationRow> SendQualificationNoticeAsync(CancellationToken ct = default)
    {
        EnsureDestinationHash();
        var now = _clock.GetUtcNow().UtcDateTime;
        var notice = _ledger.OpenNoticeWithIntent(new NotificationContent("qualification", null,
            LondonClock.Format(LondonClock.DueDay(now)), _options.WindmillWorkspace, _options.SchedulePath, null,
            $"qualification notice for namespace {_options.Namespace}", null, null, null, null));
        await DeliverAsync(notice, readerHeld: false, ct);
        return _ledger.Notification(notice.Nid)!;
    }

    private async Task<(List<ImportOutcome> Imports, ReaderState State)> ReadBackAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var pending = _ledger.PendingNotifications();
        var unreadReceipts = _ledger.Receipts().Where(r => r.ReadObservedAt is null && now - r.ImportedAt < TimeSpan.FromHours(24)).ToList();
        if (pending.Count == 0 && unreadReceipts.Count == 0)
            return ([], ReaderState.Eligible);

        var floor = pending.SelectMany(n => _ledger.Attempts(n.Nid)).Select(a => a.StartedAt)
            .Concat(unreadReceipts.Select(r => r.DateUtc)).DefaultIfEmpty(now).Min();

        ReaderReadResult read;
        if (_options.Namespace != WatchdogOptions.ProductionNamespace && _options.HoldControlPath is { Length: > 0 } hold && File.Exists(hold))
        {
            read = ReaderReadResult.Held("hold-control");
        }
        else
        {
            try { read = await _reader.ReadAsync(floor, ct); }
            catch (WatchdogCrashException) { throw; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("reader failed: {Error}", Redact(ex.Message));
                read = ReaderReadResult.Held("reader-error");
            }
        }

        if (read.IsHeld)
        {
            if (_ledger.Counter(HeldSinceCounter) == 0)
                _ledger.SetCounter(HeldSinceCounter, (int)new DateTimeOffset(now).ToUnixTimeSeconds());
            return ([], ReaderState.Held);
        }
        _ledger.SetCounter(HeldSinceCounter, 0);
        _ledger.SetCounter(LastReadCounter, (int)new DateTimeOffset(now).ToUnixTimeSeconds());
        var outcomes = _importer.Import(read.Observations, () => _crashHook?.Invoke(CrashPoint.ReaderObservation));
        return (outcomes.Where(o => o.Reason != "duplicate").ToList(), ReaderState.Eligible);
    }

    private async Task<SendRecord?> DeliverAsync(NotificationRow notification, bool readerHeld, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var attempts = _ledger.Attempts(notification.Nid);
        var heldSince = _ledger.Counter(HeldSinceCounter) is > 0 and var s ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : (DateTime?)null;
        var decision = RetryPolicy.Decide(attempts, now, readerHeld, heldSince, _options);
        if (decision.Action == RetryAction.None) return null;

        var attempt = decision.Action == RetryAction.SendNew
            ? _ledger.CreateAttempt(notification.Nid, decision.Attempt)
            : attempts[^1];
        TransportResult result;
        try
        {
            result = await _transport.SendAsync(new OutboundMessage(notification.Nid, attempt.Attempt, attempt.Body), ct);
        }
        catch (WatchdogCrashException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            result = TransportResult.Fail("transport", Redact(ex.Message));
        }
        _crashHook?.Invoke(CrashPoint.SendBeforeResponse);
        _ledger.RecordAttempt(notification.Nid, attempt.Attempt, result);
        if (result.Accepted)
            _crashHook?.Invoke(CrashPoint.TransportAccepted);
        return new SendRecord(notification.Nid, attempt.Attempt, result.Accepted, result.MessageId, result.ErrorClass, decision.Note);
    }

    private async Task<ProbeSet> ProbeAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        try
        {
            var version = await _windmill.GetVersionAsync(ct);
            if (!version.IsOk)
                return new ProbeSet(now, false, null, null, null, null, false, null, null);
            var who = await _windmill.WhoAmIAsync(ct);
            bool? authOk = who.Status switch
            {
                WindmillCallStatus.Ok => true,
                WindmillCallStatus.Unauthorized => false,
                _ => null,
            };
            if (authOk != true)
                return new ProbeSet(now, true, version.Value, authOk, null, null, false, null, null);

            var script = await _windmill.GetScriptAsync(ct);
            var schedule = await _windmill.GetScheduleAsync(ct);
            var workers = await _windmill.ListWorkersAsync(ct);
            DateTime? ping = workers.IsOk
                ? workers.Value!.Where(w => string.Equals(w.WorkerGroup, _options.DesktopWorkerGroup, StringComparison.Ordinal))
                    .Select(w => (DateTime?)w.LastPingUtc).DefaultIfEmpty(null).Max()
                : null;
            var listed = await _windmill.ListJobsAsync(ct);
            IReadOnlyList<WindmillJob>? jobs = null;
            if (listed.IsOk)
            {
                var enriched = new List<WindmillJob>();
                var fetched = 0;
                foreach (var job in listed.Value!.OrderByDescending(j => j.CreatedAtUtc))
                {
                    var current = job;
                    if (job.Kind == JobKind.Completed && !string.IsNullOrWhiteSpace(job.SchedulePath) && job.Result is null && fetched < 5)
                    {
                        fetched++;
                        var result = await _windmill.GetCompletedResultAsync(job.Id, ct);
                        if (result.IsOk) current = current with { Result = result.Value };
                    }
                    if (job.Kind == JobKind.Completed && job.Success == false && string.IsNullOrEmpty(job.Logs))
                    {
                        var logs = await _windmill.GetLogsAsync(job.Id, ct);
                        if (logs.IsOk) current = current with { Logs = logs.Value ?? "" };
                    }
                    enriched.Add(current);
                }
                jobs = enriched;
            }
            return new ProbeSet(now, true, version.Value, true, script, schedule, workers.IsOk, ping, jobs);
        }
        catch (WatchdogCrashException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("windmill probe failed: {Error}", Redact(ex.Message));
            return new ProbeSet(now, false, null, null, null, null, false, null, null);
        }
    }

    private NotificationContent FailureContent(OutageDecision decision, string outageId) => new(
        "failure", decision.Kind, decision.DueDay, _options.WindmillWorkspace, _options.SchedulePath, decision.JobId,
        decision.Evidence["detail"]?.GetValue<string>() ?? decision.Kind, outageId,
        decision.Evidence["sha"]?.GetValue<string>(), decision.RunId, decision.Evidence["policyHash"]?.GetValue<string>());

    private NotificationContent RecoveryContent(OutageRow outage, OutageDecision decision, bool failureReceived)
    {
        var detail = decision.JobId is null
            ? "probes clean"
            : $"closed by job {decision.JobId} run {decision.RunId ?? "none"}";
        return new NotificationContent("recovery", outage.Kind, outage.DueDay, _options.WindmillWorkspace, _options.SchedulePath,
            decision.JobId, detail, outage.OutageId, null, outage.RunId, null, outage.FailureNid, failureReceived);
    }

    private void EnsureDestinationHash()
    {
        if (!long.TryParse(_options.DestinationChatId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)) return;
        if (_ledger.Heartbeat().DestinationHash is null)
            _ledger.SetDestinationHash(WatchdogOptions.DestinationHash(_options.DestinationChatId)!);
    }

    private JsonObject HeartbeatState(ProbeSet probes, ReaderState readerState)
    {
        var previous = _ledger.Heartbeat().StateJson is { } json ? JsonNode.Parse(json) as JsonObject : null;
        var lastOk = probes.Reachable ? Ledger.Iso(probes.NowUtc) : previous?["windmill"]?["lastOkAt"]?.GetValue<string>();
        var lastRead = _ledger.Counter(LastReadCounter) is > 0 and var r
            ? Ledger.Iso(DateTimeOffset.FromUnixTimeSeconds(r).UtcDateTime) : null;
        return new JsonObject
        {
            ["windmill"] = new JsonObject { ["reachable"] = probes.Reachable, ["lastOkAt"] = lastOk, ["version"] = probes.WindmillVersion },
            ["desktopWorker"] = new JsonObject
            {
                ["seen"] = probes.WorkersKnown && probes.WorkerLastPingUtc is not null,
                ["lastPingAt"] = probes.WorkerLastPingUtc is { } ping ? Ledger.Iso(ping) : null,
            },
            ["schedule"] = new JsonObject
            {
                ["present"] = probes.Schedule?.Status == WindmillCallStatus.Ok,
                ["enabled"] = probes.Schedule?.Value?.Enabled,
                ["scriptHash"] = probes.Script?.Value?.Hash,
            },
            ["reader"] = new JsonObject
            {
                ["available"] = readerState == ReaderState.Eligible,
                ["lastReadAt"] = lastRead,
                ["held"] = readerState == ReaderState.Held,
            },
        };
    }

    private string Redact(string message)
    {
        foreach (var secret in new[] { _options.TelegramBotToken, _options.WindmillToken, _options.ReaderApiHash })
        {
            if (!string.IsNullOrEmpty(secret)) message = message.Replace(secret, "***", StringComparison.Ordinal);
        }
        return message;
    }
}
