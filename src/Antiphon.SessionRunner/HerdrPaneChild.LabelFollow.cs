using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

internal sealed partial class HerdrPaneChild
{
    private readonly SemaphoreSlim _labelAdmission = new(1, 1);
    private readonly object _labelSnapshotGate = new();
    private readonly CancellationTokenSource _labelLifetime = new();
    private bool _labelDisposed;
    private long _freshLabelSequence;
    internal bool LabelObservationInFlight => _labelAdmission.CurrentCount == 0;
    // Awaited boundary seam. Production leaves it null; tests abort/join before reconstruction.
    internal Func<string, CancellationToken, Task>? LabelFollowBoundary { get; set; }
    internal Action<string>? BeforeLabelFileReplace { get; set; }
    internal Action<string>? BeforeLastPaneFileReplace { get; set; }

    internal void RetireLabelSnapshot(string reason)
    {
        lock (_labelSnapshotGate)
        {
            HerdrPaneSidecar.Retire(_settings.SessionLogPath, _sessionId, reason);
            _exited = true;
        }
    }

    private void DeleteLabelSnapshot()
    {
        lock (_labelSnapshotGate)
        {
            HerdrPaneSidecar.TryDelete(_settings.SessionLogPath, _sessionId);
            _freshLabelSequence = 0;
        }
    }

    private Task LabelBoundaryAsync(string name, CancellationToken ct) =>
        LabelFollowBoundary?.Invoke(name, ct) ?? Task.CompletedTask;

    private bool OwnsLabelBinding(HerdrPaneSidecar expected, Func<bool> stillOwned)
    {
        var current = _sidecar;
        if (_exited || _labelDisposed || !stillOwned() || current is null
            || current.SessionId != expected.SessionId || current.AcceptedStartedAt != expected.AcceptedStartedAt
            || current.WorkspaceId != expected.WorkspaceId || current.TabId != expected.TabId || current.PaneId != expected.PaneId)
            return false;
        var saved = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(_settings.SessionLogPath, expected.SessionId));
        return saved is not null && saved.SessionId == expected.SessionId && saved.AcceptedStartedAt == expected.AcceptedStartedAt
            && saved.WorkspaceId == expected.WorkspaceId && saved.TabId == expected.TabId && saved.PaneId == expected.PaneId
            && saved.Origin == expected.Origin && saved.LabelFollow?.Sequence == expected.LabelFollow?.Sequence;
    }

    private void SaveLabelState(HerdrPaneSidecar state)
    {
        state.SaveAtomic(HerdrPaneSidecar.PathFor(_settings.SessionLogPath, state.SessionId), BeforeLabelFileReplace);
        _sidecar = state;
    }

    /// <summary>One nonqueueing durable admission shared by GET, baseline and timer.</summary>
    internal async Task FollowLabelsAsync(TimeProvider clock, Func<bool> stillOwned, CancellationToken ct)
    {
        var settings = _client.Settings;
        settings.ValidateLabelFollow();
        if (!settings.Enabled || _sidecar is not { } initial || !HerdrLabelObserver.Eligible(initial)
            || !_labelAdmission.Wait(0)) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(settings.LabelFollowObservationTimeoutSeconds), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token, _labelLifetime.Token);
        var token = linked.Token;
        HerdrPaneSidecar? claimed = null;
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var due = initial.LabelFollow!.NextDueAtUtc is not { } next || now >= next;
            if (!due && !initial.LabelFollow.LastPaneRepairPending) return;
            if (due)
            {
                await LabelBoundaryAsync("before-claim", token);
                // The short metadata commit precedes lease wait as well as RPC I/O, so even
                // lease timeouts consume the hour. Retirement uses this same metadata gate.
                // It is never the runtime's synchronous gate and is released before any await.
                lock (_labelSnapshotGate)
                {
                    if (!OwnsLabelBinding(initial, stillOwned)) return;
                    claimed = initial with { LabelFollow = initial.LabelFollow with {
                        Sequence = checked(initial.LabelFollow.Sequence + 1), LastAttemptAtUtc = now,
                        NextDueAtUtc = now.AddMinutes(settings.LabelFollowCooldownMinutes), Observation = null } };
                    _freshLabelSequence = 0;
                    SaveLabelState(claimed);
                }
                await LabelBoundaryAsync("after-claim", token);
            }

            await using var keyLease = _coordinator is null ? null : await _coordinator.LockWorkspaceKeyAsync(initial.WorkspaceKey, token);
            await using var workspaceLease = _coordinator is null ? null : await _coordinator.LockWorkspaceIdAsync(initial.WorkspaceId, token);
            await using var paneLease = _coordinator is null ? null : await _coordinator.LockPaneAsync(initial.PaneId, token);
            var current = _sidecar!;
            lock (_labelSnapshotGate) { if (!OwnsLabelBinding(current, stillOwned)) return; }
            await RepairLastPaneAsync(current, stillOwned, token);
            if (!due) return;
            current = _sidecar!;
            var candidate = await new HerdrLabelObserver(_client, HerdrNamedTabResolver.HostLabelComparer).CollectAsync(current, token);
            await LabelBoundaryAsync("before-result", token);
            var completion = clock.GetUtcNow().UtcDateTime;
            var observation = new HerdrLabelObservation(1, current.SessionId, current.AcceptedStartedAt!.Value,
                current.WorkspaceId, current.TabId, current.PaneId, current.Origin!, current.LabelFollow!.Intent,
                current.LabelFollow.Sequence, completion, current.LabelFollow.NextDueAtUtc!.Value,
                candidate.ResultCode, candidate.TabLabel, candidate.WorkspaceLabel);
            var result = current with {
                TabLabel = candidate.TabLabel ?? current.TabLabel,
                WorkspaceLabel = candidate.WorkspaceLabel ?? current.WorkspaceLabel,
                LabelFollow = current.LabelFollow with { Observation = observation, LastPaneRepairPending = candidate.ResultCode == "validated" } };
            lock (_labelSnapshotGate)
            {
                if (!OwnsLabelBinding(current, stillOwned)) return;
                result.SaveAtomic(HerdrPaneSidecar.PathFor(_settings.SessionLogPath, result.SessionId), BeforeLabelFileReplace);
            }
            await LabelBoundaryAsync("after-result-file", token);
            lock (_labelSnapshotGate)
            {
                if (!OwnsLabelBinding(result, stillOwned)) return;
                _sidecar = result;
                _freshLabelSequence = result.LabelFollow.Sequence;
            }
            await RepairLastPaneAsync(result, stillOwned, token);
            _logger.LogDebug("Herdr label observation {SessionId} {Generation} {Sequence}: {Code}",
                result.SessionId, result.AcceptedStartedAt, result.LabelFollow.Sequence, candidate.ResultCode);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException or GrokRulesTransportException)
        {
            // No exit authority. The durable claim has already invalidated the previous candidate.
            lock (_labelSnapshotGate)
            {
                var current = _sidecar;
                var saved = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(_settings.SessionLogPath, _sessionId));
                if (claimed is not null && current?.LabelFollow?.Sequence == claimed.LabelFollow!.Sequence
                    && saved?.LabelFollow?.Observation is null && OwnsLabelBinding(current, stillOwned))
                {
                    try
                    {
                        var failure = new HerdrLabelObservation(1, current.SessionId, current.AcceptedStartedAt!.Value,
                            current.WorkspaceId, current.TabId, current.PaneId, current.Origin!, current.LabelFollow.Intent,
                            current.LabelFollow.Sequence, clock.GetUtcNow().UtcDateTime, current.LabelFollow.NextDueAtUtc!.Value,
                            deadline.IsCancellationRequested ? "timeout" : "persistence_failed");
                        SaveLabelState(current with { LabelFollow = current.LabelFollow with { Observation = failure } });
                        _freshLabelSequence = current.LabelFollow.Sequence;
                    }
                    catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException or GrokRulesTransportException) { }
                }
            }
            _logger.LogDebug("Herdr label attempt did not complete for {SessionId}: {Failure}", _sessionId, ex.GetType().Name);
        }
        finally { _labelAdmission.Release(); }
    }

    private async Task RepairLastPaneAsync(HerdrPaneSidecar current, Func<bool> stillOwned, CancellationToken ct)
    {
        if (current.LabelFollow?.LastPaneRepairPending != true) return;
        await LabelBoundaryAsync("before-repair", ct);
        lock (_labelSnapshotGate)
        {
            if (!OwnsLabelBinding(current, stillOwned)) return;
            var path = HerdrLastPane.PathFor(_settings.SessionLogPath, current.SessionId);
            var last = HerdrLastPane.TryLoad(path);
            if (last is null && File.Exists(path)) return; // Unreadable is not proof of replacement.
            if (last is not null && LastPaneMatches(current, last))
                (last with { TabLabel = current.TabLabel, WorkspaceLabel = current.WorkspaceLabel }).SaveAtomic(path, BeforeLastPaneFileReplace);
        }
        await LabelBoundaryAsync("after-repair-file", ct);
        lock (_labelSnapshotGate)
        {
            if (OwnsLabelBinding(current, stillOwned))
                SaveLabelState(current with { LabelFollow = current.LabelFollow with { LastPaneRepairPending = false } });
        }
    }

    internal static bool LastPaneMatches(HerdrPaneSidecar current, HerdrLastPane last) =>
        last.SessionId == current.SessionId && last.AcceptedStartedAt is not null
        && last.AcceptedStartedAt == current.AcceptedStartedAt && last.WorkspaceId == current.WorkspaceId
        && last.TabId == current.TabId && last.PaneId == current.PaneId && last.Origin == HerdrPaneOrigins.Launched;

    internal async Task<HerdrLabelObservation?> ReadLabelObservationAsync(TimeProvider clock, Func<bool> stillOwned, CancellationToken ct)
    {
        if (!_labelAdmission.Wait(0)) return null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(_client.Settings.LabelFollowObservationTimeoutSeconds), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token, _labelLifetime.Token);
        try
        {
            var current = _sidecar;
            if (current is null || !HerdrLabelObserver.Eligible(current) || _freshLabelSequence == 0
                || current.LabelFollow!.Observation is not { } observation || observation.Sequence != _freshLabelSequence
                || clock.GetUtcNow().UtcDateTime >= observation.ExpiresAtUtc) return null;
            await using var lease = _coordinator is null ? null : await _coordinator.LockPaneAsync(current.PaneId, linked.Token);
            var pane = await _client.PaneGetAsync(current.PaneId, linked.Token);
            var process = await _client.PaneProcessInfoAsync(current.PaneId, linked.Token);
            lock (_labelSnapshotGate)
            {
                if (!OwnsLabelBinding(current, stillOwned) || !HerdrLabelObserver.SamePane(current, pane)
                    || current.ChildPid is not > 0 || process.PaneId != current.PaneId
                    || process.ForegroundProcesses?.Any(p => p.Pid == current.ChildPid) != true) return null;
                return observation with { PositivelyVerified = true };
            }
        }
        catch (Exception ex) when (ex is HerdrApiException or HerdrBackendUnavailableException or HerdrProtocolException or OperationCanceledException or IOException)
        { return null; }
        finally { _labelAdmission.Release(); }
    }
}
