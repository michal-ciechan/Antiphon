using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// Explicit best-effort disposal: under Antiphon's pane lease persist intent, freshly inspect,
/// classify and compare, then call protocol-20 pane.close. External activity AFTER that final
/// observation can still be closed. This is the operator-accepted non-atomic limitation.
/// </summary>
public sealed class HerdrPaneDisposalService
{
    private readonly IHerdrDisposalBackend _backend;
    private readonly SessionRunnerRuntime _runtime;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly HerdrDisposalLocatorStore _locators;
    private readonly IHerdrDisposalReceiptStore _receipts;
    private readonly HerdrDisposalIdentity _identity = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly object _previewGate = new();
    private readonly Dictionary<Guid, Review> _previews = [];
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _operations = new();

    public HerdrPaneDisposalService(HerdrClient client, SessionRunnerRuntime runtime,
        IOptions<SessionRunnerSettings> settings, TimeProvider time, ILogger<HerdrPaneDisposalService>? logger = null)
        : this(runtime, settings.Value.SessionLogPath, time, null, null, null, logger)
    { _backend = new HerdrDisposalBackend(client, runtime, _locators, new HerdrDisposalProcessInspector()); }

    internal HerdrPaneDisposalService(SessionRunnerRuntime runtime, string root, TimeProvider time,
        IHerdrDisposalBackend? backend, IHerdrDisposalReceiptStore? receipts = null,
        HerdrDisposalLocatorStore? locators = null, ILogger? logger = null)
    {
        _runtime = runtime; _time = time; _logger = logger ?? NullLogger.Instance;
        _locators = locators ?? new(root); _receipts = receipts ?? new HerdrDisposalReceiptStore(root);
        _backend = backend!;
    }

    public async Task<HerdrPaneDisposalPreview> PreviewAsync(HerdrPaneDisposalPreviewRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        HerdrDisposalObservation o;
        try { o = await _backend.InspectAsync(request.PaneId, true, cancellationToken); }
        catch (HerdrApiException ex) when (IsMissing(ex))
        { throw new HerdrLaunchException("Selected pane not found.", HerdrProblemTypes.PaneNotFound); }
        var refusal = _identity.Refusal(request, o);
        var now = _time.GetUtcNow();
        var p = o.Pane;
        var preview = new HerdrPaneDisposalPreview(Guid.NewGuid(), now.AddMinutes(2), p.PaneId,
            request.ExpectedSessionId, request.ExpectedNativeSessionId, p.WorkspaceId, p.TabId, p.TerminalId,
            o.WorkspaceLabel, o.TabLabel, p.Label, o.Backend.Version, o.Backend.Protocol,
            o.Shell?.Pid, o.Foreground, o.Claims, o.EmptyTab, refusal is null, true, o.Complete,
            refusal is null ? [] : [refusal], o.Backend.InstanceId, o.Shell, o.Affected,
            PlannedTerminationPids: refusal is null ? o.Affected.Select(f => f.Pid).ToArray() : []);
        lock (_previewGate)
        {
            foreach (var id in _previews.Where(p => p.Value.Preview.ExpiresAtUtc <= now).Select(p => p.Key).ToArray()) _previews.Remove(id);
            if (_previews.Count >= 256) _previews.Remove(_previews.MinBy(p => p.Value.Preview.ExpiresAtUtc).Key);
            _previews.Add(preview.PreviewId, new(preview, Stamp(o), o.Files));
        }
        // No file writes or retention pruning during preview.
        return Clone(preview);
    }

    public HerdrPaneDisposalPreview GetPreview(Guid previewId)
    { lock (_previewGate) return Clone(RequirePreview(previewId).Preview); }

    public async Task<HerdrPaneDisposalReceipt> ExecuteAsync(HerdrPaneDisposalRequest request, CancellationToken cancellationToken)
    {
        ValidateExecution(request);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, _json)));
        var semaphore = _operations.GetOrAdd(request.OperationId, _ => new(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var previous = _receipts.Read(request.OperationId);
            if (previous is not null)
            {
                if (previous.Fingerprint != fingerprint)
                    throw new HerdrLaunchException("Operation ID already belongs to another request.", HerdrPaneDisposalCodes.OperationConflict);
                return (await ReconcileAsync(previous, cancellationToken)).Receipt;
            }
            Review review;
            lock (_previewGate)
            {
                review = RequirePreview(request.PreviewId);
                if (review.Consumer is not null)
                    throw new HerdrLaunchException("Preview already consumed.", HerdrPaneDisposalCodes.PreviewConsumed);
                review.Consumer = request.OperationId;
            }
            var p = review.Preview;
            var operation = new HerdrDisposalStoredOperation(fingerprint,
                new(request.OperationId, request.PreviewId, p.PaneId, "Unknown", HerdrPaneDisposalCodes.Unknown,
                    _time.GetUtcNow(), null, false, TerminalId: p.TerminalId,
                    ExpectedSessionId: p.ExpectedSessionId, ExpectedNativeSessionId: p.ExpectedNativeSessionId), p, review.Files);
            if (!p.Eligible) return SaveResult(operation, "Refused", p.Blockers.First(), null).Receipt;
            // Prevent the old preview-only server (which lacks standing-owner execution gates)
            // from accidentally enabling this newer runner's destructive implementation.
            if (request.GuardMode != "antiphon-best-effort")
                return SaveResult(operation, "Refused", HerdrPaneDisposalCodes.GuardUnavailable, null).Receipt;
            await using var lease = await _runtime.Placement.LockPaneAsync(p.PaneId, cancellationToken);
            var sent = false;
            var confirmed = false;
            try
            {
                var current = await _backend.InspectAsync(p.PaneId, false, cancellationToken);
                if (Refusal(review, current) is { } initialRefusal)
                    return SaveResult(operation, "Refused", initialRefusal, true).Receipt;
                _receipts.Prune(_time.GetUtcNow().AddDays(-7));
                _receipts.Save(operation); // intent BEFORE final inspection and destructive RPC
                current = await _backend.InspectAsync(p.PaneId, false, cancellationToken);
                if (Refusal(review, current) is { } finalRefusal)
                    return SaveResult(operation, "Refused", finalRefusal, true).Receipt;
                if (p.ExpiresAtUtc <= _time.GetUtcNow())
                    return SaveResult(operation, "Refused", HerdrPaneDisposalCodes.PreviewExpired, true).Receipt;
                cancellationToken.ThrowIfCancellationRequested();
                sent = true;
                await _backend.CloseAsync(p.PaneId, cancellationToken);
                confirmed = true;
                operation = SaveResult(operation, "Closed", "herdr_pane_closed", false);
                return Cleanup(operation).Receipt;
            }
            catch (HerdrApiException ex) when (IsMissing(ex))
            {
                var presence = await TryPresenceAsync(p, CancellationToken.None);
                if (presence.OriginalAbsent)
                    return Cleanup(SaveResult(operation, "AlreadyAbsent", "herdr_pane_already_absent", false, presence.ReplacementPresent)).Receipt;
                return SaveResult(operation, sent ? "Unknown" : "Refused",
                    sent ? HerdrPaneDisposalCodes.Unknown : HerdrProblemTypes.PaneChanged, null, presence.ReplacementPresent).Receipt;
            }
            catch (Exception ex) when (ex is HerdrBackendUnavailableException or HerdrProtocolException or HerdrApiException
                or HerdrLaunchException or OperationCanceledException or IOException or UnauthorizedAccessException)
            {
                // Never replay after send. The durable intent also survives a second write failure.
                return SaveResult(operation, confirmed ? "Closed" : sent ? "Unknown" : "Refused",
                    confirmed ? "herdr_pane_closed" : sent ? HerdrPaneDisposalCodes.Unknown : ex is HerdrLaunchException launch ? launch.Code ?? HerdrProblemTypes.Refused : HerdrProblemTypes.Unreachable,
                    confirmed ? false : null).Receipt;
            }
        }
        finally { semaphore.Release(); }
    }

    public async Task<HerdrPaneDisposalReceipt?> GetAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("A full operation ID is required.");
        var stored = _receipts.Read(operationId);
        if (stored is null) return null;
        var gate = _operations.GetOrAdd(operationId, _ => new(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken)) return stored.Receipt;
        try { return (await ReconcileAsync(_receipts.Read(operationId) ?? stored, cancellationToken)).Receipt; }
        finally { gate.Release(); }
    }

    private async Task<HerdrDisposalStoredOperation> ReconcileAsync(HerdrDisposalStoredOperation operation, CancellationToken ct)
    {
        if (operation.Reviewed is not { } reviewed) return operation;
        if (operation.Receipt.Outcome == "Unknown")
        {
            await using var lease = await _runtime.Placement.LockPaneAsync(reviewed.PaneId, ct);
            var presence = await TryPresenceAsync(reviewed, ct);
            if (presence.OriginalAbsent)
                return Cleanup(SaveResult(operation, "AlreadyAbsent", "herdr_pane_already_absent", false, presence.ReplacementPresent));
            return SaveResult(operation, "Unknown", HerdrPaneDisposalCodes.Unknown, null, presence.ReplacementPresent);
        }
        if (operation.Receipt.CleanupPending && operation.Receipt.Outcome is "Closed" or "AlreadyAbsent")
        {
            await using var lease = await _runtime.Placement.LockPaneAsync(reviewed.PaneId, ct);
            return Cleanup(operation); // file cleanup only; never re-close
        }
        return operation;
    }

    private async Task<HerdrDisposalPresence> TryPresenceAsync(HerdrPaneDisposalPreview p, CancellationToken ct)
    {
        try { return await _backend.PresenceAsync(p, ct); }
        catch (Exception ex) when (ex is HerdrBackendUnavailableException or HerdrApiException or HerdrProtocolException or IOException)
        { return new(false, null); }
    }

    private HerdrDisposalStoredOperation Cleanup(HerdrDisposalStoredOperation operation)
    {
        var pending = false;
        var changed = false;
        foreach (var file in operation.Files ?? [])
        {
            try { changed |= _locators.DeleteCaptured(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { pending = true; }
        }
        var notify = changed || !operation.CensusNotified;
        operation = operation with { Receipt = operation.Receipt with { CleanupPending = pending }, CensusNotified = true };
        _receipts.Save(operation);
        if (notify) _runtime.NotifyPaneSetChanged();
        return operation;
    }

    private HerdrDisposalStoredOperation SaveResult(HerdrDisposalStoredOperation operation, string outcome, string code, bool? leftOpen, bool? replacement = null)
    {
        operation = operation with { Receipt = operation.Receipt with { Outcome = outcome, Code = code,
            PaneLeftOpen = leftOpen, ReplacementPresent = replacement, RecordedAtUtc = _time.GetUtcNow(),
            CleanupPending = outcome is "Closed" or "AlreadyAbsent" } };
        _receipts.Save(operation);
        _logger.LogInformation("HerdrPaneDisposal {OperationId} {PreviewId} {PaneId} {Outcome} {Code}",
            operation.Receipt.OperationId, operation.Receipt.PreviewId, operation.Receipt.PaneId, outcome, code);
        return operation;
    }

    private string? Refusal(Review review, HerdrDisposalObservation current)
    {
        var p = review.Preview;
        var reason = _identity.Refusal(new(p.PaneId, p.ExpectedSessionId, p.ExpectedNativeSessionId), current);
        if (reason == HerdrProblemTypes.PaneBound) return reason;
        if (Stamp(current) != review.Stamp) return HerdrProblemTypes.PaneChanged;
        return reason;
    }

    private Review RequirePreview(Guid id)
    {
        if (!_previews.TryGetValue(id, out var r)) throw new HerdrLaunchException("Preview unknown, evicted or restarted.", HerdrPaneDisposalCodes.PreviewInvalid);
        if (r.Preview.ExpiresAtUtc <= _time.GetUtcNow()) throw new HerdrLaunchException("Preview expired; inspect again.", HerdrPaneDisposalCodes.PreviewExpired);
        return r;
    }
    private string Stamp(HerdrDisposalObservation o) => JsonSerializer.Serialize(new
    {
        o.Pane.PaneId, o.Pane.TerminalId, o.Pane.TabId, o.Pane.WorkspaceId, o.Pane.Agent,
        o.Backend.InstanceId, o.Backend.Protocol, o.Shell,
        Affected = o.Affected.OrderBy(p => p.Pid), Foreground = o.Foreground?.OrderBy(p => p.Pid),
        NativeMetadata = o.Pane.AgentSession is { } m && m.Source != HerdrSources.Antiphon
            && Guid.TryParse(m.Value, out var id) ? id : (Guid?)null,
    }, _json);
    private HerdrPaneDisposalPreview Clone(HerdrPaneDisposalPreview p) =>
        JsonSerializer.Deserialize<HerdrPaneDisposalPreview>(JsonSerializer.Serialize(p, _json), _json)!;
    private static bool IsMissing(HerdrApiException ex) => ex.Code is "pane_not_found" or "not_found";
    internal static void Validate(HerdrPaneDisposalPreviewRequest request)
    {
        if (request.PaneId is null || request.PaneId.Length > 128 || !Regex.IsMatch(request.PaneId, @"\Aw[0-9A-Za-z]+:p[0-9A-Za-z]+\z"))
            throw new ArgumentException("paneId requires one exact workspace-qualified pane ID.");
        if ((request.ExpectedSessionId is null && request.ExpectedNativeSessionId is null)
            || request.ExpectedSessionId == Guid.Empty || request.ExpectedNativeSessionId == Guid.Empty)
            throw new ArgumentException("At least one full nonempty expected UUID is required.");
    }
    internal static void ValidateExecution(HerdrPaneDisposalRequest r)
    {
        if (r.OperationId == Guid.Empty || r.PreviewId == Guid.Empty || string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Length > 4096)
            throw new ArgumentException("Nonempty operationId/previewId and a 1-4096-character reason are required.");
    }
    private sealed class Review(HerdrPaneDisposalPreview preview, string stamp, IReadOnlyList<HerdrDisposalFile> files)
    {
        public HerdrPaneDisposalPreview Preview { get; } = preview;
        public string Stamp { get; } = stamp;
        public IReadOnlyList<HerdrDisposalFile> Files { get; } = files;
        public Guid? Consumer { get; set; }
    }
}
