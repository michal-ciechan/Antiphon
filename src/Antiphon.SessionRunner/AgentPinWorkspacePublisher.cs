using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Dormant, Linux x64 host publication primitive. The runner must supply one stable,
/// private journal root per store, outside every workspace. No HTTP/DI registration.
/// Git exclusion and import discovery admission are still required before activation.
/// A journal is custody; an observed marker or caller-supplied hash is not.
/// </summary>
public sealed class AgentPinWorkspacePublisher
{
    private readonly Guid _storeId;
    private readonly string _journalRoot;
    private readonly Func<string, Task>? _boundary;

    public AgentPinWorkspacePublisher(Guid runnerStoreId, string journalRoot)
        : this(runnerStoreId, journalRoot, null) { }

    internal AgentPinWorkspacePublisher(Guid runnerStoreId, string journalRoot, Func<string, Task>? boundary)
    {
        if (runnerStoreId == Guid.Empty) throw new ArgumentException("Runner store identity is required.");
        _storeId = runnerStoreId;
        _journalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(journalRoot));
        _boundary = boundary;
    }

    public async Task<AgentPinPublicationResult> ApplyAsync(AgentPinPublicationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.SchemaVersion != AgentPinWorkspaceStore.PathSchemaVersion) return Refuse("pin_schema_unsupported");
        if (request.AgentId == Guid.Empty) return Refuse("pin_owner_invalid");
        if (request.AgentId != request.TargetOwnerId) return Refuse("pin_owner_mismatch");
        if (request.RunnerStoreId != _storeId) return Refuse("pin_store_mismatch");
        if (!Enum.IsDefined(request.Action)) return Refuse("pin_action_unsupported");
        if (request.OperationId == Guid.Empty || request.LocationGeneration == Guid.Empty
            || request.Fence <= 0 || request.Revision <= 0) return Refuse("pin_operation_invalid");
        if (request.ExpectedSha256 is not null && !IsHash(request.ExpectedSha256)) return Refuse("pin_digest_invalid");
        // Copy caller-owned bytes before hashing/awaits: the request cannot change under the intent.
        var bytes = request.Content?.ToArray();
        if (request.Action == AgentPinFileAction.Publish)
        {
            if (bytes is null || bytes.Length > AgentPinWorkspaceStore.MaxInspectionBytes)
                return Refuse("pin_content_invalid");
            if (request.Sha256 != Hash(bytes)) return Refuse("pin_digest_mismatch");
        }
        else if (bytes is not null || request.Sha256 is not null) return Refuse("pin_content_invalid");

        var inspector = new AgentPinWorkspaceStore(_storeId);
        var inspectRequest = new AgentPinInspectRequest(request.SchemaVersion, request.AgentId, _storeId, request.Cwd);
        try
        {
            var observed = await inspector.InspectAsync(inspectRequest, ct);
            if (ObservationFailure(observed) is { } failure) return failure;
            var cwd = observed.Cwd!;
            var target = observed.Path!;
            if (_journalRoot == cwd || _journalRoot.StartsWith(cwd + "/", StringComparison.Ordinal))
                return Refuse("pin_journal_in_workspace");
            if (HasGitAncestor(cwd)) return Refuse("pin_git_not_qualified");

            using var journal = AgentPinPosixDirectory.Open(_journalRoot);
            var key = Hash(Encoding.UTF8.GetBytes(target));
            using var pathLock = journal.Acquire(key + ".lock");
            var journalName = key + ".json";
            var recordBytes = journal.Read(journalName, 64 * 1024);
            var prior = recordBytes is null ? null : JsonSerializer.Deserialize<Intent>(recordBytes)
                ?? throw new IOException("Invalid pin journal.");
            var desired = new Intent(request.SchemaVersion, request.AgentId, _storeId, target,
                request.LocationGeneration, request.OperationId, request.Fence, request.Revision,
                request.Action, request.Sha256, request.ExpectedSha256, bytes?.Length ?? 0, false);

            // Re-observe under the cross-instance/process lock, never use pre-lock evidence.
            observed = await inspector.InspectAsync(inspectRequest, ct);
            if (ObservationFailure(observed) is { } lockedFailure) return lockedFailure;
            if (prior is not null)
            {
                if (prior.SchemaVersion != request.SchemaVersion || prior.AgentId != request.AgentId
                    || prior.RunnerStoreId != _storeId || prior.Path != target)
                    return Refuse("pin_journal_identity_mismatch");
                if (request.Fence < prior.Fence) return Refuse("pin_fence_stale");
                if (request.Fence == prior.Fence)
                {
                    if (desired != prior with { Completed = false }) return Refuse("pin_operation_conflict");
                    if (observed.Sha256 == desired.Sha256)
                    {
                        Save(journal, journalName, desired with { Completed = true });
                        return Applied(desired);
                    }
                    if (prior.Completed || observed.Sha256 != desired.ExpectedSha256)
                        return Refuse("pin_bytes_conflict");
                }
                else
                {
                    // An interrupted intent proves custody of either its old or new bytes.
                    if (observed.Sha256 != prior.Sha256
                        && (prior.Completed || observed.Sha256 != prior.ExpectedSha256))
                        return Refuse("pin_bytes_conflict");
                    if (observed.Sha256 != desired.ExpectedSha256) return Refuse("pin_bytes_conflict");
                }
            }
            else
            {
                if (request.Action == AgentPinFileAction.Cleanup || request.ExpectedSha256 is not null)
                    return Refuse("pin_custody_missing");
                if (observed.Status != AgentPinInspectionStatus.MissingFile) return Refuse("pin_custody_missing");
            }

            Save(journal, journalName, desired); // Durable fence BEFORE any workspace mutation.
            await BoundaryAsync("intent", ct);
            using var cwdDirectory = AgentPinPosixDirectory.Open(cwd);
            using var antiphon = cwdDirectory.Child(".antiphon", true);
            using var pins = antiphon.Child("pins", true);
            var ownerName = request.AgentId.ToString("N");
            using var owner = pins.Child(ownerName, true);
            var temp = $"antiphon.{request.OperationId:N}.{Guid.NewGuid():N}.tmp";
            try
            {
                if (bytes is not null) owner.WriteNew(temp, bytes);
                await BoundaryAsync("before-compare", ct);
                observed = await inspector.InspectAsync(inspectRequest, ct);
                if (ObservationFailure(observed) is { } finalFailure) return finalFailure;
                if (HasGitAncestor(cwd)) return Refuse("pin_git_not_qualified");
                if (observed.Sha256 != desired.ExpectedSha256) return Refuse("pin_bytes_conflict");
                ct.ThrowIfCancellationRequested();
                if (bytes is null) owner.Delete("antiphon.md");
                else owner.Replace(temp, "antiphon.md");
                await BoundaryAsync("published", ct);
                observed = await inspector.InspectAsync(inspectRequest, ct);
                if (ObservationFailure(observed) is { } receiptFailure) return receiptFailure;
                if (observed.Sha256 != desired.Sha256) return Refuse("pin_bytes_conflict");
                Save(journal, journalName, desired with { Completed = true });
            }
            finally { owner.Delete(temp); }
            if (bytes is null) pins.RemoveEmptyDirectory(ownerName);
            return Applied(desired);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(AgentPinPublicationStatus.Unavailable, "pin_io_unavailable");
        }
    }

    private async Task BoundaryAsync(string phase, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_boundary is not null) await _boundary(phase);
        ct.ThrowIfCancellationRequested();
    }

    private static void Save(AgentPinPosixDirectory journal, string name, Intent intent)
    {
        var temp = name + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            journal.WriteNew(temp, JsonSerializer.SerializeToUtf8Bytes(intent));
            journal.Replace(temp, name);
        }
        finally { journal.Delete(temp); }
    }

    private static AgentPinPublicationResult? ObservationFailure(AgentPinInspection observed) => observed.Status switch
    {
        AgentPinInspectionStatus.Refused => Refuse(observed.Reason!),
        AgentPinInspectionStatus.Unavailable => new(AgentPinPublicationStatus.Unavailable, observed.Reason),
        AgentPinInspectionStatus.MissingCwd => Refuse("pin_cwd_missing"),
        _ => null
    };

    private static bool HasGitAncestor(string cwd)
    {
        for (var path = cwd; path is not null; path = Path.GetDirectoryName(path))
        {
            try { _ = File.GetAttributes(Path.Combine(path, ".git")); return true; }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return false;
    }

    private static bool IsHash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static AgentPinPublicationResult Refuse(string reason) => new(AgentPinPublicationStatus.Refused, reason);
    private static AgentPinPublicationResult Applied(Intent intent) => new(AgentPinPublicationStatus.Applied, null,
        new(intent.SchemaVersion, intent.AgentId, intent.RunnerStoreId, intent.Path,
            intent.LocationGeneration, intent.OperationId, intent.Fence, intent.Revision,
            intent.Action, intent.Sha256, intent.ByteCount));

    private sealed record Intent(int SchemaVersion, Guid AgentId, Guid RunnerStoreId, string Path,
        Guid LocationGeneration, Guid OperationId, long Fence, long Revision, AgentPinFileAction Action,
        string? Sha256, string? ExpectedSha256, long ByteCount, bool Completed);
}
