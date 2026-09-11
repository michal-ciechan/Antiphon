using Antiphon.Agents.Pty;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Runner-owned history, independent of the disposable session registry. All launch and seal
/// callers hold the session admission lock; files remain authoritative after process restart.
/// </summary>
public sealed class RunnerCustodyLedger
{
    public VerificationCustodyStore Store { get; }

    public RunnerCustodyLedger(string root, IVerificationCustodyFiles? files = null) => Store = new(root, files: files);

    public IDisposable AcquireSession(Guid sessionId)
    {
        try
        {
            return new FileStream(Path.Combine(Store.Root, $"{sessionId:N}.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) { throw new VerificationCustodyException("verification_custody_admission_busy", ex); }
    }

    public void RequireUntrackedSession(Guid sessionId)
    {
        if (Store.ReadReservations().Any(b => b.Generation.SessionId == sessionId))
            throw new VerificationCustodyException("verification_custody_binding_required");
    }

    public VerificationExecutionBinding Resolve(Guid sessionId, Guid executionId, DateTime acceptedStartedAt)
    {
        var binding = Store.ReadReservations().SingleOrDefault(b => b.ExecutionId == executionId)
            ?? throw new VerificationCustodyException("verification_custody_unknown_execution");
        if (binding.Generation != new VerificationSessionGeneration(sessionId, acceptedStartedAt))
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
        return binding;
    }

    public void ValidateRecovery()
    {
        foreach (var binding in Store.ReadReservations())
        {
            // Read even nonterminal metadata. Torn history must fence startup, never vanish.
            _ = ReadStamp(binding, "runner-start-intent.json");
            _ = ReadStamp(binding, "native-start-intent.json");
            _ = ReadStamp(binding, "runner-seal.json");
            _ = ReadStamp(binding, "producer-seal.json");
            _ = Store.ReadRecord<CustodyFailure>(binding, "unsupported.json");
            _ = Store.ReadRecord<CustodyFailure>(binding, "launch-failure.json");
            _ = Store.ReadRecord<CustodyRoot>(binding, "tracking.json");
            _ = Store.ReadRecord<RunnerHostIdentity>(binding, "runner-host.json");
            _ = Store.ReadRecord<PtyHostManifest>(binding, "host-manifest.json");
            _ = Store.ReadRecord<PtyHostManifest>(binding, "running-manifest.json");
            var host = Store.ReadRecord<VerificationHostIdentity>(binding, "host-identity.json");
            if (host is not null)
            {
                RequireHost(binding, host);
                _ = Store.ReadReceipt(binding, host, accepted: false);
                _ = ReadFinal(binding);
            }
            else if (File.Exists(Store.PathFor(binding.ExecutionId, "producer-receipt.json"))
                || File.Exists(Store.PathFor(binding.ExecutionId, "accepted-receipt.json")))
                throw new VerificationCustodyException("verification_custody_corrupt_store");
        }
    }

    public void RecoverManifests(string manifestDir, IProcessLivenessProbe probe)
    {
        foreach (var binding in Store.ReadReservations())
        {
            var host = Store.ReadRecord<VerificationHostIdentity>(binding, "host-identity.json");
            if (host is null) continue;
            RequireHost(binding, host);
            if (!probe.IsAlive(host.HostPid, host.HostStartTimeUtc)) continue;
            var path = PtyHostManifest.PathFor(manifestDir, binding.Generation.SessionId);
            if (File.Exists(path)) continue;
            var manifest = Store.ReadRecord<PtyHostManifest>(binding, "running-manifest.json")
                ?? Store.ReadRecord<PtyHostManifest>(binding, "host-manifest.json");
            if (manifest is null) continue; // interrupted before any provider launch; preserve Unknown
            if (manifest.SchemaVersion != 1 || manifest.VerificationBinding != binding
                || manifest.VerificationHost != host || manifest.HostPid != host.HostPid
                || manifest.HostStartTimeUtc != host.HostStartTimeUtc || manifest.Cwd != binding.Creation.WorktreePath
                || manifest.SessionId != binding.Generation.SessionId
                || manifest.PipeName != PtyHostProtocol.PipeNameFor(binding.Generation.SessionId))
                throw new VerificationCustodyException("verification_custody_identity_mismatch");
            if (manifest.AnsiLogPath is { } ansi) Store.RequireOutsideSnapshot(ansi, binding);
            if (Store.ReadRecord<CustodyRoot>(binding, "tracking.json") is { } root)
                manifest = manifest with { ChildPid = root.Pid, ChildStartTimeUtc = root.StartTimeUtc };
            // This restores only discovery metadata. It neither creates a job nor fabricates a receipt.
            manifest.SaveAtomic(path);
        }
    }

    /// <returns>False for an exact replay. An accepted intent is never a request to create again.</returns>
    public bool PrepareStart(RunnerLaunchRequest request, string? ptyBackend)
    {
        var reservations = Store.ReadReservations().ToArray();
        var sameSession = reservations.Where(b => b.Generation.SessionId == request.SessionId).ToArray();
        if (request.VerificationBinding is not { } binding)
        {
            if (sameSession.Length != 0)
                throw new VerificationCustodyException("verification_custody_binding_required");
            return true;
        }
        Store.ValidateBinding(binding);
        if (binding.Generation.SessionId != request.SessionId || binding.Creation.WorktreePath != request.Cwd)
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
        var existing = reservations.SingleOrDefault(b => b.ExecutionId == binding.ExecutionId);
        if (existing is not null)
        {
            Store.RequireBinding(binding);
            if (ReadStamp(binding, "runner-seal.json") is not null)
                throw new VerificationCustodyException("verification_custody_sealed");
            return false;
        }
        foreach (var prior in sameSession)
        {
            if (prior.Generation == binding.Generation || ReadFinal(prior) is null)
                throw new VerificationCustodyException("verification_custody_session_fenced");
        }
        Store.Reserve(binding);
        if (request.Backend is not (null or SessionBackends.PtyHost)
            || !OperatingSystem.IsWindows() || PtyBackendPolicy.Resolve(ptyBackend).Backend != PtyBackend.ModernConPty)
        {
            Store.WriteRecord(binding, "unsupported.json",
                new CustodyFailure("verification_custody_unsupported_backend", DateTime.UtcNow));
            throw new VerificationCustodyException("verification_custody_unsupported_backend");
        }
        Store.WriteRecord(binding, "runner-start-intent.json", new CustodyStamp(1, DateTime.UtcNow));
        return true;
    }

    public void RecordConnectedHost(VerificationExecutionBinding binding, RunnerHostIdentity host)
    {
        if (host.HostInstanceId == Guid.Empty || host.HostPid <= 0 || host.HostStartTimeUtc.Kind != DateTimeKind.Utc)
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
        Store.WriteRecord(binding, "runner-host.json", host);
    }

    public void RecordUnsupported(VerificationExecutionBinding binding)
    {
        var previous = Store.ReadRecord<CustodyFailure>(binding, "unsupported.json");
        Store.WriteRecord(binding, "unsupported.json",
            previous ?? new CustodyFailure("verification_custody_unsupported_backend", DateTime.UtcNow));
    }

    public void RequireHost(VerificationExecutionBinding binding, VerificationHostIdentity host)
    {
        var acceptedHost = Store.ReadRecord<RunnerHostIdentity>(binding, "runner-host.json");
        if (host.RunnerStoreId != Store.StoreId || host.ContainerId == Guid.Empty
            || acceptedHost != new RunnerHostIdentity(host.HostInstanceId, host.HostPid, host.HostStartTimeUtc)
            || Store.ReadRecord<VerificationHostIdentity>(binding, "host-identity.json") != host)
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
    }

    public void Seal(VerificationExecutionBinding binding)
    {
        // A seal may beat an enqueued start. Reserve B so that later start/replay must refuse it.
        Store.Reserve(binding);
        var prior = ReadStamp(binding, "runner-seal.json");
        Store.WriteRecord(binding, "runner-seal.json", prior ?? new CustodyStamp(3, DateTime.UtcNow));
    }

    public VerificationCustodyStatus? ReadFinal(VerificationExecutionBinding binding)
    {
        var host = Store.ReadRecord<VerificationHostIdentity>(binding, "host-identity.json");
        if (host is null) return null;
        RequireHost(binding, host);
        if (Store.ReadReceipt(binding, host, accepted: true) is not { } bytes) return null;
        var receipt = Store.ValidateReceipt(bytes, binding, host);
        ValidateReceiptHistory(receipt);
        return new(binding, receipt.Disposition, null, host, bytes);
    }

    public VerificationCustodyStatus? ReadProducer(VerificationExecutionBinding binding)
    {
        var host = Store.ReadRecord<VerificationHostIdentity>(binding, "host-identity.json");
        if (host is null) return null;
        RequireHost(binding, host);
        if (Store.ReadReceipt(binding, host, accepted: false) is not { } bytes) return null;
        return new(binding, Store.ValidateReceipt(bytes, binding, host).Disposition, null, host, bytes);
    }

    public VerificationCustodyStatus ReadUnavailable(VerificationExecutionBinding binding)
    {
        if (ReadFinal(binding) is { } final) return final;
        if (Store.ReadRecord<CustodyFailure>(binding, "unsupported.json") is not null)
            return new(binding, VerificationCustodyState.UnsupportedBackend, "verification_custody_unsupported_backend");
        return new(binding, VerificationCustodyState.Unknown, "custody_observer_unavailable");
    }

    public VerificationCustodyStatus Accept(VerificationCustodyStatus status)
    {
        Store.RequireBinding(status.Binding);
        if (status.Host is not { } host)
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
        RequireHost(status.Binding, host);
        if (status.Receipt is null)
        {
            if (status.State is VerificationCustodyState.Exited or VerificationCustodyState.NeverStarted)
                throw new VerificationCustodyException("verification_custody_invalid_receipt");
            return status;
        }
        var receipt = Store.ValidateReceipt(status.Receipt, status.Binding, host);
        if (receipt.Disposition != status.State)
            throw new VerificationCustodyException("verification_custody_invalid_receipt");
        ValidateReceiptHistory(receipt);
        var producer = Store.ReadReceipt(status.Binding, host, accepted: false);
        if (producer is null || !producer.AsSpan().SequenceEqual(status.Receipt))
            throw new VerificationCustodyException("verification_custody_integrity_error");
        Store.AcceptReceipt(status.Receipt, status.Binding, host);
        return ReadFinal(status.Binding)!;
    }

    private void ValidateReceiptHistory(VerificationCustodyReceipt receipt)
    {
        var binding = receipt.Binding;
        var seal = ReadStamp(binding, "producer-seal.json");
        if (seal is not { Revision: 3 } || seal.AtUtc != receipt.SealedAtUtc
            || ReadStamp(binding, "runner-start-intent.json") is not { Revision: 1 }
            || Store.ReadRecord<CustodyFailure>(binding, "unsupported.json") is not null)
            throw new VerificationCustodyException("verification_custody_invalid_receipt");
        var nativeIntent = ReadStamp(binding, "native-start-intent.json");
        var root = Store.ReadRecord<CustodyRoot>(binding, "tracking.json");
        if (receipt.Disposition == VerificationCustodyState.Exited)
        {
            if (nativeIntent is not { Revision: 1 } || root is not { Revision: 2 }
                || root.ContainerId != receipt.Host.ContainerId || root.Pid != receipt.RootPid
                || root.StartTimeUtc != receipt.RootStartTimeUtc
                || Store.ReadRecord<CustodyFailure>(binding, "launch-failure.json") is not null)
                throw new VerificationCustodyException("verification_custody_invalid_receipt");
        }
        else if (nativeIntent is not null || root is not null
            || Store.ReadRecord<CustodyFailure>(binding, "launch-failure.json") is not { Reason: "verification_custody_start_failed" })
            throw new VerificationCustodyException("verification_custody_invalid_receipt");
    }

    private CustodyStamp? ReadStamp(VerificationExecutionBinding binding, string name)
    {
        var stamp = Store.ReadRecord<CustodyStamp>(binding, name);
        if (stamp is not null && (stamp.Revision <= 0 || stamp.AtUtc == default || stamp.AtUtc.Kind != DateTimeKind.Utc))
            throw new VerificationCustodyException("verification_custody_corrupt_store");
        return stamp;
    }
}

public sealed record RunnerHostIdentity(Guid HostInstanceId, int HostPid, DateTime HostStartTimeUtc);
