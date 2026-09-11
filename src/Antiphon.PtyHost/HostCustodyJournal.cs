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
        using var root = Process.GetProcessById(processId);
        var tracking = new CustodyRoot(2, processId, root.StartTime.ToUniversalTime(), Identity.ContainerId);
        _store.WriteRecord(Binding, "tracking.json", tracking);
        _root = tracking;
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
        Func<bool> hostOutputFailed, CancellationToken ct) =>
        await ObserveAsync(runner, hostOutputFailed, true, ct);

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
            if (_nativeIntent || _store.ReadRecord<CustodyStamp>(Binding, "native-start-intent.json") is not null)
                return new(Binding, VerificationCustodyState.Unknown, _failure, Identity);
            RecordSeal();
            return Persist(new(1, Binding, Identity, 4, _seal!.AtUtc, DateTime.UtcNow,
                "sealed-before-native-start-intent", null, true, VerificationCustodyState.NeverStarted, null, null));
        }
        if (!seal && _seal is null)
            return new(Binding, VerificationCustodyState.Tracking, null, Identity);
        try
        {
            var observation = await runner.SealAndObserveCustodyAsync(ct);
            if (observation.ActiveProcesses != 0 || !observation.OutputDrained)
                return new(Binding, VerificationCustodyState.Draining, "descendants_running", Identity);
            if (hostOutputFailed())
                return new(Binding, VerificationCustodyState.Unknown, "host_output_drain_failed", Identity);
            if (_root is null || _seal is null
                || _store.ReadRecord<CustodyRoot>(Binding, "tracking.json") != _root
                || _store.ReadRecord<CustodyStamp>(Binding, "producer-seal.json") != _seal
                || _store.ReadRecord<CustodyStamp>(Binding, "native-start-intent.json") is not { Revision: 1 })
                throw new VerificationCustodyException("verification_custody_corrupt_store");
            var termination = runner.CustodyTermination;
            return Persist(new(1, Binding, Identity, 4, _seal.AtUtc, DateTime.UtcNow,
                "JobObjectBasicAccountingInformation", observation.ActiveProcesses, true,
                VerificationCustodyState.Exited, _root.Pid, _root.StartTimeUtc,
                termination?.Succeeded, termination?.ErrorCode));
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
