using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.PtyHost.Protocol;

public sealed record CustodyStamp(long Revision, DateTime AtUtc);
public sealed record CustodyRoot(long Revision, int Pid, DateTime StartTimeUtc, Guid ContainerId);
public sealed record CustodyFailure(string Reason, DateTime AtUtc);

/// <summary>External I/O seam for durable custody writes, including the pre-rename fault boundary.</summary>
public interface IVerificationCustodyFiles
{
    byte[]? Read(string path);
    void CommitImmutable(string path, byte[] bytes);
}

public interface IVerificationCustodyWriteOperations
{
    FileStream CreateTemporary(string path);
    void Publish(string temporary, string final);
}

public sealed class VerificationCustodyWriteOperations : IVerificationCustodyWriteOperations
{
    public FileStream CreateTemporary(string path) => new(path, FileMode.CreateNew, FileAccess.Write,
        FileShare.None, 4096, FileOptions.WriteThrough);
    public void Publish(string temporary, string final) => File.Move(temporary, final, overwrite: false);
}

public sealed class VerificationCustodyFiles(IVerificationCustodyWriteOperations? writes = null) : IVerificationCustodyFiles
{
    private readonly IVerificationCustodyWriteOperations _writes = writes ?? new VerificationCustodyWriteOperations();
    public byte[]? Read(string path)
    {
        try { return File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void CommitImmutable(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = _writes.CreateTemporary(temporary))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            try { _writes.Publish(temporary, path); }
            catch (IOException) when (File.Exists(path))
            {
                // A concurrent identical replay is harmless. Never replace terminal bytes.
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                    throw new VerificationCustodyException("verification_custody_integrity_error");
            }
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// Execution-keyed append-only runtime ledger. The runner initializes the store and reservation;
/// the host opens that exact identity. Temporary files are never evidence. No TTL or delete API.
/// </summary>
public sealed class VerificationCustodyStore
{
    private readonly IVerificationCustodyFiles _files;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    public string Root { get; }
    public Guid StoreId { get; }

    public VerificationCustodyStore(string root, Guid? expectedStoreId = null,
        IVerificationCustodyFiles? files = null)
    {
        Root = Path.GetFullPath(root);
        _files = files ?? new VerificationCustodyFiles();
        Directory.CreateDirectory(Root);
        var identityPath = Path.Combine(Root, "store.json");
        // Serialize store initialization across runner processes. Losing an existing identity
        // cannot turn an old ledger into a freshly empty runner.
        var identity = Read<StoreIdentity>(identityPath);
        if (identity is null)
        {
            using var identityLock = new FileStream(Path.Combine(Root, "store.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            identity = Read<StoreIdentity>(identityPath);
            if (identity is null)
            {
                if (expectedStoreId is not null || Directory.EnumerateDirectories(Root).Any()
                    || Directory.EnumerateFiles(Root).Any(p => Path.GetFileName(p) != "store.lock"))
                    throw new VerificationCustodyException("verification_custody_store_identity_missing");
                identity = new(1, Guid.NewGuid());
                Write(identityPath, identity);
            }
        }
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty
            || (expectedStoreId is { } expected && expected != identity.StoreId))
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
        StoreId = identity.StoreId;
    }

    public void Reserve(VerificationExecutionBinding binding)
    {
        ValidateBinding(binding);
        Write(PathFor(binding.ExecutionId, "binding.json"), binding);
    }

    public VerificationExecutionBinding RequireBinding(VerificationExecutionBinding expected)
    {
        ValidateBinding(expected);
        var actual = Read<VerificationExecutionBinding>(PathFor(expected.ExecutionId, "binding.json"));
        if (actual != expected)
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
        return actual;
    }

    public IEnumerable<VerificationExecutionBinding> ReadReservations()
    {
        RequireStoreIdentity();
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id))
                throw new VerificationCustodyException("verification_custody_corrupt_store");
            var binding = Read<VerificationExecutionBinding>(PathFor(id, "binding.json"))
                ?? throw new VerificationCustodyException("verification_custody_corrupt_store");
            ValidateBinding(binding);
            if (binding.ExecutionId != id)
                throw new VerificationCustodyException("verification_custody_identity_mismatch");
            yield return binding;
        }
    }

    public T? ReadRecord<T>(VerificationExecutionBinding binding, string name) where T : class
    {
        RequireBinding(binding);
        return Read<T>(PathFor(binding.ExecutionId, name));
    }

    public void WriteRecord<T>(VerificationExecutionBinding binding, string name, T value)
    {
        RequireBinding(binding);
        Write(PathFor(binding.ExecutionId, name), value);
    }

    public byte[]? ReadReceipt(VerificationExecutionBinding binding, VerificationHostIdentity host, bool accepted)
    {
        RequireBinding(binding);
        var bytes = _files.Read(PathFor(binding.ExecutionId,
            accepted ? "accepted-receipt.json" : "producer-receipt.json"));
        if (bytes is not null) ValidateReceipt(bytes, binding, host);
        return bytes;
    }

    public byte[] SaveProducer(VerificationCustodyReceipt receipt)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, _json);
        ValidateReceipt(bytes, receipt.Binding, receipt.Host);
        RequireBinding(receipt.Binding);
        _files.CommitImmutable(PathFor(receipt.Binding.ExecutionId, "producer-receipt.json"), bytes);
        return bytes;
    }

    public void AcceptReceipt(byte[] bytes, VerificationExecutionBinding binding, VerificationHostIdentity host)
    {
        RequireBinding(binding);
        ValidateReceipt(bytes, binding, host);
        _files.CommitImmutable(PathFor(binding.ExecutionId, "accepted-receipt.json"), bytes);
    }

    public VerificationCustodyReceipt ValidateReceipt(byte[] bytes,
        VerificationExecutionBinding binding, VerificationHostIdentity host)
    {
        VerificationCustodyReceipt receipt;
        try
        {
            // Require every structural field even when its CLR default could look valid.
            using var document = JsonDocument.Parse(bytes);
            foreach (var name in new[] { "schemaVersion", "binding", "host", "stateRevision",
                         "sealedAtUtc", "observedAtUtc", "observationMethod", "activeProcesses",
                         "outputDrained", "disposition", "rootPid", "rootStartTimeUtc" })
                if (!document.RootElement.TryGetProperty(name, out _))
                    throw new JsonException("Incomplete custody receipt");
            receipt = JsonSerializer.Deserialize<VerificationCustodyReceipt>(bytes, _json)
                ?? throw new JsonException("Empty custody receipt");
        }
        catch (JsonException ex) { throw new VerificationCustodyException("verification_custody_invalid_receipt", ex); }
        ValidateBinding(binding);
        if (receipt.SchemaVersion != 1 || receipt.StateRevision < 3
            || receipt.SealedAtUtc.Kind != DateTimeKind.Utc || receipt.SealedAtUtc == default
            || receipt.ObservedAtUtc.Kind != DateTimeKind.Utc || receipt.ObservedAtUtc < receipt.SealedAtUtc
            || !receipt.OutputDrained)
            throw new VerificationCustodyException("verification_custody_invalid_receipt");
        if (receipt.Binding != binding || receipt.Host != host || host.RunnerStoreId != StoreId
            || host.HostInstanceId == Guid.Empty || host.ContainerId == Guid.Empty
            || host.HostPid <= 0 || host.HostStartTimeUtc.Kind != DateTimeKind.Utc
            || host.HostStartTimeUtc == default)
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
        var exited = receipt.Disposition == VerificationCustodyState.Exited
            && receipt.ObservationMethod == "JobObjectBasicAccountingInformation"
            && receipt.ActiveProcesses == 0 && receipt.RootPid > 0
            && receipt.RootStartTimeUtc is { Kind: DateTimeKind.Utc };
        var neverStarted = receipt.Disposition == VerificationCustodyState.NeverStarted
            && receipt.ObservationMethod == "sealed-before-native-start-intent"
            && receipt.ActiveProcesses is null && receipt.RootPid is null && receipt.RootStartTimeUtc is null;
        if (!exited && !neverStarted)
            throw new VerificationCustodyException("verification_custody_invalid_receipt");
        return receipt;
    }

    public string PathFor(Guid executionId, string name)
    {
        if (executionId == Guid.Empty || Path.GetFileName(name) != name || !name.EndsWith(".json", StringComparison.Ordinal))
            throw new ArgumentException("Invalid custody ledger coordinate");
        return Path.Combine(Root, executionId.ToString("N"), name);
    }

    public void ValidateBinding(VerificationExecutionBinding binding)
    {
        RequireStoreIdentity();
        if (binding.ExecutionId == Guid.Empty || binding.Source is null || binding.Generation is null
            || binding.Creation is null || binding.Source.TaskId == Guid.Empty || binding.Source.SourceOperationId == Guid.Empty
            || binding.Source.LandedSha is not { Length: 40 } sha || !sha.All(Uri.IsHexDigit)
            || binding.Generation.SessionId == Guid.Empty || binding.Generation.AcceptedStartedAt.Kind != DateTimeKind.Utc
            || binding.Generation.AcceptedStartedAt == default || binding.Generation.AcceptedStartedAt.Ticks % 10 != 0
            || binding.CustodyContractVersion != 1 || binding.Backend != "windows-job-v1"
            || binding.Creation.CreationId == Guid.Empty || string.IsNullOrWhiteSpace(binding.Creation.Branch))
            throw new VerificationCustodyException("verification_custody_invalid_binding");
        foreach (var path in new[] { binding.Creation.RepositoryPath, binding.Creation.CommonGitDirectory,
                     binding.Creation.WorktreePath, binding.Creation.WorktreeGitDirectory })
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
                || !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), path, StringComparison.Ordinal))
                throw new VerificationCustodyException("verification_custody_invalid_binding");
        RequireOutsideSnapshot(Root, binding);
    }

    public void RequireOutsideSnapshot(string path, VerificationExecutionBinding binding)
    {
        var relative = Path.GetRelativePath(binding.Creation.WorktreePath, Path.GetFullPath(path));
        if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new VerificationCustodyException("verification_custody_storage_inside_snapshot");
    }

    private T? Read<T>(string path) where T : class
    {
        try
        {
            var bytes = _files.Read(path);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, _json)
                ?? throw new JsonException("Empty ledger record");
        }
        catch (JsonException ex) { throw new VerificationCustodyException("verification_custody_corrupt_store", ex); }
    }

    private void Write<T>(string path, T value) =>
        _files.CommitImmutable(path, JsonSerializer.SerializeToUtf8Bytes(value, _json));

    private void RequireStoreIdentity()
    {
        if (Read<StoreIdentity>(Path.Combine(Root, "store.json")) != new StoreIdentity(1, StoreId))
            throw new VerificationCustodyException("verification_custody_identity_mismatch");
    }

    private sealed record StoreIdentity(int SchemaVersion, Guid StoreId);
}
