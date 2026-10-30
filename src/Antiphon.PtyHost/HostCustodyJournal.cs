using System.Diagnostics;
using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.PtyHost;

/// <summary>Host-owned durable launch boundaries. One instance is consumed by exactly one attempt.</summary>
public sealed class HostCustodyJournal : IPtyCustodyJournal
{
    private readonly VerificationCustodyStore _store;
    private CustodyStamp? _seal;
    private CustodyRoot? _root;
    private bool _nativeIntent;
    private string? _failure;
    private bool _unsupported;

    public VerificationExecutionBinding Binding { get; }
    public VerificationHostIdentity Identity { get; }
    public Guid ContainerId => Identity.ContainerId;

    /// <summary>
    /// Test seam. When set, the first linux-namespace.json write uses this id.
    /// Production leaves it unset and reads /run/antiphon-custody/namespace.
    /// </summary>
    internal static readonly AsyncLocal<Guid?> LinuxNamespaceForTests = new();

    public HostCustodyJournal(VerificationCustodyStore store, VerificationExecutionBinding binding,
        Guid hostInstanceId)
    {
        _store = store;
        Binding = store.RequireBinding(binding);
        if (store.ReadRecord<CustodyStamp>(binding, "runner-start-intent.json") is null
            || store.ReadRecord<CustodyStamp>(binding, "runner-seal.json") is not null)
            throw new VerificationCustodyException("verification_custody_sealed_or_missing_intent");
        using var host = Process.GetCurrentProcess();
        Identity = new(store.StoreId, hostInstanceId, Guid.NewGuid(), host.Id, host.StartTime.ToUniversalTime());
        // A second host cannot replace a former host/job, even if its root or manifest disappeared.
        store.WriteRecord(binding, "host-identity.json", Identity);
        store.WriteRecord(binding, "linux-namespace.json",
            new LinuxNamespaceRecord(1, ReadLinuxNamespace(), Identity.ContainerId));
    }

    private static Guid ReadLinuxNamespace()
    {
        if (LinuxNamespaceForTests.Value is Guid injected)
            return injected;
        const string marker = "/run/antiphon-custody/namespace";
        try
        {
            var text = File.ReadAllText(marker).Trim();
            return Guid.TryParseExact(text, "D", out var id) ? id : Guid.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Guid.Empty;
        }
    }

    public void RecordStartIntent()
    {
        if (_seal is not null || _store.ReadRecord<CustodyStamp>(Binding, "runner-seal.json") is not null)
            throw new VerificationCustodyException("verification_custody_sealed");
        // Set uncertainty before I/O: a failed write can never justify NeverStarted.
        _nativeIntent = true;
        _store.WriteRecord(Binding, "native-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));
    }

    public void RecordTracking(int processId)
    {
        // Process.StartTime is available on both platforms; on Linux it is derived from
        // /proc/<pid>/stat's start field against btime, which is exactly the pair D-17 names as
        // the identity of the tracked root. Reading it here, inside the host that spawned the
        // pid, is what stops a later pid reuse from looking like the same root.
        using var root = Process.GetProcessById(processId);
        var tracking = new CustodyRoot(2, processId, root.StartTime.ToUniversalTime(), Identity.ContainerId);
        _store.WriteRecord(Binding, "tracking.json", tracking);
        _root = tracking;
    }

    public void RecordLinuxPlacement(bool containedRoot)
    {
        if (!containedRoot || _root is null || Binding.Backend != VerificationCustodyBackends.LinuxCgroup)
            return;
        var ns = _store.ReadRecord<LinuxNamespaceRecord>(Binding, "linux-namespace.json");
        if (ns is null || ns.NamespaceId == Guid.Empty || ns.ContainerId != Identity.ContainerId)
            return;
        _store.WriteRecord(Binding, "linux-placement.json",
            new LinuxPlacementRecord(1, _root.Pid, _root.StartTimeUtc, Identity.ContainerId, ns.NamespaceId));
    }

    public void RecordSeal()
    {
        _seal ??= new(3, DateTime.UtcNow);
        _store.WriteRecord(Binding, "producer-seal.json", _seal);
    }

    public void RecordManifest(PtyHostManifest manifest, bool launched) =>
        _store.WriteRecord(Binding, launched ? "running-manifest.json" : "host-manifest.json", manifest);

    public void RecordLaunchFailure(bool unsupported)
    {
        _unsupported = unsupported;
        _failure = unsupported ? "verification_custody_unsupported_backend" : "verification_custody_start_failed";
        _store.WriteRecord(Binding, "launch-failure.json", new CustodyFailure(_failure, DateTime.UtcNow));
        RecordSeal();
    }

    public async Task<VerificationCustodyStatus> ObserveAsync(PtyAgentRunner runner,
        Func<bool> hostOutputFailed, bool seal, CancellationToken ct)
    {
        _store.RequireBinding(Binding);
        if (_store.ReadRecord<VerificationHostIdentity>(Binding, "host-identity.json") != Identity)
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
        if (_store.ReadReceipt(Binding, Identity, accepted: false) is { } final)
            return FromReceipt(final);
        if (_unsupported)
            return new(Binding, VerificationCustodyState.UnsupportedBackend, _failure, Identity);
        if (_failure is not null)
        {
            if (NativeIntentRecorded())
            {
                if (!seal || Binding.Backend != VerificationCustodyBackends.LinuxCgroup)
                    return new(Binding, VerificationCustodyState.Unknown, _failure, Identity);
                return await ReleaseFailedLinuxAsync(runner, ct);
            }
            RecordSeal();
            return Persist(new(1, Binding, Identity, 4, _seal!.AtUtc, DateTime.UtcNow,
                VerificationCustodyBackends.NeverStartedMethod, null, true,
                VerificationCustodyState.NeverStarted, null, null));
        }
        if (Binding.Backend == VerificationCustodyBackends.LinuxCgroup)
            return seal
                ? await ObserveLinuxExplicitAsync(runner, hostOutputFailed, ct)
                : await ObserveLinuxOrdinaryAsync(runner, hostOutputFailed, ct);
        if (!seal && _seal is null)
            return new(Binding, VerificationCustodyState.Tracking, null, Identity);
        try
        {
            var observation = await runner.SealAndObserveCustodyAsync(ct);
            if (observation.ActiveProcesses != 0 || !observation.OutputDrained)
                return new(Binding, VerificationCustodyState.Draining, "descendants_running", Identity);
            if (hostOutputFailed())
                return new(Binding, VerificationCustodyState.Unknown, "host_output_drain_failed", Identity);
            if (!ExitedIdentityIntact())
                throw new VerificationCustodyException("verification_custody_corrupt_store");
            var termination = runner.CustodyTermination;
            // CARD-0604 D-19 / G-30: the method is derived from the binding's backend, so a
            // Linux producer can never emit a Windows method and vice versa.
            return PersistExited(observation.ActiveProcesses, termination?.Succeeded, termination?.ErrorCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The owned drain remains running. Cancellation never creates terminal evidence.
            return new(Binding, VerificationCustodyState.Unknown, "observation_canceled", Identity);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return new(Binding, VerificationCustodyState.Unknown, "observation_failed", Identity);
        }
    }

    internal bool ShouldWithholdLinger()
    {
        if (Binding.Backend != VerificationCustodyBackends.LinuxCgroup)
            return false;
        if (_store.ReadReceipt(Binding, Identity, accepted: true) is not null || HasReleaseDisposition())
            return false;
        return NativeIntentRecorded() || _root is not null
            || _store.ReadRecord<CustodyRoot>(Binding, "tracking.json") is not null;
    }

    internal bool HasReleaseDisposition()
    {
        var disposition = _store.ReadRecord<LinuxReleaseDisposition>(Binding, "linux-release-disposition.json");
        if (disposition is not { Revision: 1 }
            || disposition.StoreId != _store.StoreId
            || disposition.ExecutionId != Binding.ExecutionId
            || disposition.NamespaceId == Guid.Empty
            || disposition.HostInstanceId == Guid.Empty
            || disposition.ContainerId == Guid.Empty)
            return false;
        var ns = _store.ReadRecord<LinuxNamespaceRecord>(Binding, "linux-namespace.json");
        if (ns is null || ns.NamespaceId == Guid.Empty || disposition.NamespaceId != ns.NamespaceId)
            return false;
        if (disposition.HostInstanceId != Identity.HostInstanceId || disposition.ContainerId != Identity.ContainerId)
            return false;
        return _store.ReadRecord<CustodyStamp>(Binding, "runner-seal.json") is not null;
    }

    private bool NativeIntentRecorded() =>
        _nativeIntent || _store.ReadRecord<CustodyStamp>(Binding, "native-start-intent.json") is not null;

    private async Task<VerificationCustodyStatus> ObserveLinuxOrdinaryAsync(PtyAgentRunner runner,
        Func<bool> hostOutputFailed, CancellationToken ct)
    {
        if (_root is not null && RootAlive(_root.Pid))
            return new(Binding, VerificationCustodyState.Tracking, null, Identity);
        if (_root is null && _seal is null)
            return new(Binding, VerificationCustodyState.Tracking, null, Identity);
        try
        {
            var population = await runner.ObserveLinuxAccountingAsync(fence: false, ct);
            if (population.ActiveProcesses != 0)
                return new(Binding, VerificationCustodyState.Draining, "descendants_running", Identity);
            if (!CanNaturalFinalize())
                return new(Binding, VerificationCustodyState.Unknown, "observation_failed", Identity);
            var finalized = await runner.ObserveLinuxAccountingAsync(fence: true, ct);
            if (finalized.ActiveProcesses != 0)
                return new(Binding, VerificationCustodyState.Draining, "descendants_running", Identity);
            if (!finalized.OutputDrained || hostOutputFailed())
                return new(Binding, VerificationCustodyState.Unknown, "host_output_drain_failed", Identity);
            if (!ExitedIdentityIntact())
                throw new VerificationCustodyException("verification_custody_corrupt_store");
            // Natural zero is not a helper termination. The receipt must not claim one.
            return PersistExited(finalized.ActiveProcesses, terminationSucceeded: null, terminationError: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new(Binding, VerificationCustodyState.Unknown, "observation_canceled", Identity);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception
            or PlatformNotSupportedException)
        {
            return new(Binding, VerificationCustodyState.Unknown, "observation_failed", Identity);
        }
    }

    private async Task<VerificationCustodyStatus> ObserveLinuxExplicitAsync(PtyAgentRunner runner,
        Func<bool> hostOutputFailed, CancellationToken ct)
    {
        try
        {
            var observation = await runner.SealAndObserveCustodyAsync(ct);
            if (observation.ActiveProcesses != 0 || !observation.OutputDrained)
                return new(Binding, VerificationCustodyState.Draining, "descendants_running", Identity);
            if (hostOutputFailed() || !ExitedIdentityIntact())
            {
                TryWriteReleaseDisposition();
                return new(Binding, VerificationCustodyState.Unknown,
                    hostOutputFailed() ? "host_output_drain_failed" : "observation_failed", Identity);
            }
            var termination = runner.CustodyTermination;
            return PersistExited(observation.ActiveProcesses, termination?.Succeeded, termination?.ErrorCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new(Binding, VerificationCustodyState.Unknown, "observation_canceled", Identity);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception
            or PlatformNotSupportedException)
        {
            return new(Binding, VerificationCustodyState.Unknown, "observation_failed", Identity);
        }
    }

    private async Task<VerificationCustodyStatus> ReleaseFailedLinuxAsync(PtyAgentRunner runner, CancellationToken ct)
    {
        try
        {
            var observation = await runner.SealAndObserveCustodyAsync(ct);
            if (observation.ActiveProcesses == 0 && observation.OutputDrained)
                TryWriteReleaseDisposition();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new(Binding, VerificationCustodyState.Unknown, "observation_canceled", Identity);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception
            or PlatformNotSupportedException)
        {
            return new(Binding, VerificationCustodyState.Unknown, "observation_failed", Identity);
        }

        return new(Binding, VerificationCustodyState.Unknown, _failure, Identity);
    }

    private bool CanNaturalFinalize()
    {
        if (_root is null || RootAlive(_root.Pid))
            return false;
        var placement = _store.ReadRecord<LinuxPlacementRecord>(Binding, "linux-placement.json");
        var ns = _store.ReadRecord<LinuxNamespaceRecord>(Binding, "linux-namespace.json");
        return placement is { Revision: 1 }
            && placement.Pid == _root.Pid
            && placement.StartTimeUtc == _root.StartTimeUtc
            && placement.ContainerId == Identity.ContainerId
            && ns is not null
            && ns.NamespaceId != Guid.Empty
            && placement.NamespaceId == ns.NamespaceId
            && ns.ContainerId == Identity.ContainerId
            && _store.ReadRecord<CustodyRoot>(Binding, "tracking.json") == _root
            && _store.ReadRecord<CustodyStamp>(Binding, "native-start-intent.json") is { Revision: 1 };
    }

    private bool ExitedIdentityIntact() =>
        _root is not null && _seal is not null
        && _store.ReadRecord<CustodyRoot>(Binding, "tracking.json") == _root
        && _store.ReadRecord<CustodyStamp>(Binding, "producer-seal.json") == _seal
        && _store.ReadRecord<CustodyStamp>(Binding, "native-start-intent.json") is { Revision: 1 };

    private static bool RootAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        catch
        {
            return true;
        }
    }

    private void TryWriteReleaseDisposition()
    {
        if (_store.ReadRecord<LinuxReleaseDisposition>(Binding, "linux-release-disposition.json") is not null)
            return;
        if (_store.ReadRecord<CustodyStamp>(Binding, "runner-seal.json") is null)
            return;
        var ns = _store.ReadRecord<LinuxNamespaceRecord>(Binding, "linux-namespace.json");
        if (ns is null || ns.NamespaceId == Guid.Empty || ns.ContainerId != Identity.ContainerId)
            return;
        _store.WriteRecord(Binding, "linux-release-disposition.json", new LinuxReleaseDisposition(
            1, _store.StoreId, Binding.ExecutionId, ns.NamespaceId,
            Identity.HostInstanceId, Identity.ContainerId, DateTime.UtcNow));
    }

    private VerificationCustodyStatus PersistExited(uint activeProcesses, bool? terminationSucceeded, int? terminationError) =>
        Persist(new(1, Binding, Identity, 4, _seal!.AtUtc, DateTime.UtcNow,
            VerificationCustodyBackends.ObservationMethodFor(Binding.Backend),
            activeProcesses, true,
            VerificationCustodyState.Exited, _root!.Pid, _root.StartTimeUtc,
            terminationSucceeded, terminationError));

    public void RequireAcceptedReceipt()
    {
        var producer = _store.ReadReceipt(Binding, Identity, accepted: false);
        var accepted = _store.ReadReceipt(Binding, Identity, accepted: true);
        if (producer is null || accepted is null || !producer.AsSpan().SequenceEqual(accepted))
            throw new VerificationCustodyException("verification_custody_receipt_not_accepted");
    }

    private VerificationCustodyStatus Persist(VerificationCustodyReceipt receipt) =>
        FromReceipt(_store.SaveProducer(receipt));

    private VerificationCustodyStatus FromReceipt(byte[] bytes) =>
        new(Binding, _store.ValidateReceipt(bytes, Binding, Identity).Disposition, null, Identity, bytes);
}
