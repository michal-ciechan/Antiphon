using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0604 D-19 (Cut B). The runner half of the verification snapshot for a Mutation bound to
/// this runner: managed creation at the exact published sha, the same validation the desktop
/// WorktreeManager performs, an inspection, the restoration record, and the guarded removal.
///
/// This is deliberately NOT the mirror lane (D-15). A mirror is a disposable copy of a branch
/// that also exists on origin; a verification snapshot is a tracked execution's only workspace,
/// its evidence root is the common git dir here, and its removal is the thing a receipt is
/// exchanged for. So nothing here forces, recurses or prunes its way past a surprise: an unknown
/// file, a dirty tree or a moved HEAD is residue the operator sees, never something to delete.
///
/// Every git process is a direct child of the runner process, started through ArgumentList (no
/// shell, no quoting), as the runner's own uid.
/// </summary>
public sealed partial class RunnerWorkspaceService
{
    private static readonly Regex BranchForIdentifier = new(@"^feat/card-task-[0-9a-f]{8}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);

    /// <summary>Schema-2 creation metadata, mirrored field-for-field from the desktop manager.</summary>
    public sealed record VerificationCreationMetadata(
        int SchemaVersion, bool CreationComplete, Guid CreationId, string Identifier,
        string InitialSha, string Branch, string RepositoryPath, string WorktreePath, string GitDirectory,
        string CommonGitDirectory);

    private string VerificationMetadataPath(string identifier) =>
        _verificationMetadataRoot + "/" + identifier + ".json";

    public async Task<PhoneHomeVerificationCreateResponse> CreateVerificationAsync(
        PhoneHomeVerificationCreateRequest request, CancellationToken ct)
    {
        var identifier = request.Identifier ?? "";
        if (!NamePattern.IsMatch(identifier)) throw Refuse("Verification identifier must be task-<8 hex>.");
        if (!ShaPattern.IsMatch(request.Sha ?? "")) throw Refuse("Verification sha must be 40 lowercase hex characters.");
        if (!BranchForIdentifier.IsMatch(request.Branch ?? "") || request.Branch != "feat/card-" + identifier)
            throw Refuse("Verification branch must be feat/card-" + identifier + ".");

        var path = _worktreeRoot + "/" + identifier;
        var metadataPath = VerificationMetadataPath(identifier);
        if (File.Exists(metadataPath))
        {
            // Idempotent for a redispatch, and ONLY for the identical creation. A snapshot that
            // exists at a different commit is an identity mismatch, never something to adopt.
            var saved = JsonSerializer.Deserialize<VerificationCreationMetadata>(
                await File.ReadAllBytesAsync(metadataPath, ct), MetadataJson);
            if (saved is not { SchemaVersion: 2, CreationComplete: true } || saved.CreationId == Guid.Empty
                || saved.InitialSha != request.Sha || saved.Identifier != identifier
                || saved.Branch != request.Branch || saved.WorktreePath != path)
                throw Refuse("verification_creation_identity_mismatch");
            return new(Coordinates(saved), saved.InitialSha);
        }
        if (Directory.Exists(path))
            throw Refuse("A directory already exists at " + path + " with no creation metadata.");

        // The Mutation targets the sha that was actually PUBLISHED. Reachability from
        // origin/master at this moment is the check: a sha that only exists on some other branch,
        // or was force-pushed away, must not become a verification snapshot.
        var fetch = await GitAsync(_repository, ct, "fetch", "origin", _publishedBranch);
        if (fetch.ExitCode != 0) throw Refuse("Verification fetch failed: " + Tail(fetch.Stderr));
        var reachable = await GitAsync(_repository, ct, "merge-base", "--is-ancestor",
            request.Sha!, "origin/" + _publishedBranch);
        if (reachable.ExitCode != 0)
            throw Refuse($"{request.Sha} is not reachable from origin/{_publishedBranch} on this runner.");

        Directory.CreateDirectory(_worktreeRoot);
        Directory.CreateDirectory(_verificationMetadataRoot);
        var add = await GitAsync(_repository, ct, "worktree", "add", "-b", request.Branch!, path, request.Sha!);
        if (add.ExitCode != 0) throw Refuse("Verification creation failed: " + Tail(add.Stderr));

        var gitDirectory = await RequireOutputAsync(path, ct, "rev-parse", "--path-format=absolute", "--git-dir");
        var commonDirectory = await RequireOutputAsync(path, ct, "rev-parse", "--path-format=absolute", "--git-common-dir");
        var repositoryPath = Posix(_repository);
        var metadata = new VerificationCreationMetadata(2, true, Guid.NewGuid(), identifier,
            request.Sha!, request.Branch!, repositoryPath, path, Posix(gitDirectory), Posix(commonDirectory));

        // CreationComplete is written last and as one document: a torn creation leaves no
        // metadata at all, which reads as "never created" rather than as a snapshot to remove.
        await File.WriteAllBytesAsync(metadataPath,
            JsonSerializer.SerializeToUtf8Bytes(metadata, MetadataJson), ct);
        return new(Coordinates(metadata), metadata.InitialSha);
    }

    public async Task<PhoneHomeVerificationValidateResponse> ValidateVerificationAsync(
        PhoneHomeVerificationValidateRequest request, CancellationToken ct)
    {
        var coordinates = request.Coordinates;
        if (coordinates is null) return new(false, "verification_creation_identity_mismatch");
        var identifier = LastSegment(coordinates.WorktreePath);
        if (!NamePattern.IsMatch(identifier) || !IsUnderWorktreeRoot(coordinates.WorktreePath))
            return new(false, "verification_creation_outside_runner_repository");
        var metadataPath = VerificationMetadataPath(identifier);
        if (!File.Exists(metadataPath)) return new(false, "verification_creation_metadata_missing");
        var saved = JsonSerializer.Deserialize<VerificationCreationMetadata>(
            await File.ReadAllBytesAsync(metadataPath, ct), MetadataJson);
        if (saved is not { SchemaVersion: 2, CreationComplete: true } || Coordinates(saved) != coordinates
            || saved.InitialSha != request.Sha)
            return new(false, "verification_creation_identity_mismatch");

        var inspect = await InspectVerificationAsync(new(coordinates.WorktreePath), ct);
        if (!inspect.Registered) return new(false, "verification_creation_not_registered");
        if (inspect.Locked) return new(false, "verification_creation_locked_or_prunable");
        if (inspect.Head != request.Sha) return new(false, "verification_creation_head_moved");
        if (!inspect.Clean) return new(false, "verification_creation_dirty");
        if (inspect.Branch != coordinates.Branch) return new(false, "verification_creation_detached_or_wrong_branch");
        if (await HasSequencerAsync(coordinates.WorktreeGitDirectory))
            return new(false, "verification_creation_sequencer_in_progress");
        return new(true, null);
    }

    public async Task<PhoneHomeVerificationInspectResponse> InspectVerificationAsync(
        PhoneHomeVerificationInspectRequest request, CancellationToken ct)
    {
        var path = request.WorktreePath ?? "";
        if (!IsUnderWorktreeRoot(path)) throw Refuse("Only a workspace under the runner worktree root may be inspected.");
        var identifier = LastSegment(path);
        VerificationCreationMetadata? saved = null;
        var metadataPath = VerificationMetadataPath(identifier);
        if (File.Exists(metadataPath))
            saved = JsonSerializer.Deserialize<VerificationCreationMetadata>(
                await File.ReadAllBytesAsync(metadataPath, ct), MetadataJson);
        if (!Directory.Exists(path))
            return new(saved?.CreationId, saved?.InitialSha, saved?.Branch, saved?.RepositoryPath,
                saved?.WorktreePath, saved?.GitDirectory, null, false, false, false);

        var list = await GitAsync(_repository, ct, "worktree", "list", "--porcelain");
        var registrations = list.ExitCode != 0 ? 0 : list.Stdout.Replace("\r\n", "\n").Split('\n')
            .Count(line => line.StartsWith("worktree ", StringComparison.Ordinal)
                && Posix(line["worktree ".Length..].Trim()).TrimEnd('/') == path.TrimEnd('/'));
        // Exactly one registration. Zero is an unregistered directory; two is a repository whose
        // state nothing here should act on.
        var registered = registrations == 1;
        var locked = list.ExitCode == 0 && ListedFlagAsync(list.Stdout, path, "locked")
            || list.ExitCode == 0 && ListedFlagAsync(list.Stdout, path, "prunable");

        var head = await GitAsync(path, ct, "rev-parse", "HEAD");
        var symbolic = await GitAsync(path, ct, "symbolic-ref", "-q", "HEAD");
        // Tracked and index only: a build output the Mutation legitimately produced is not
        // "dirty", and it is checked separately as an exact expected-output list at removal.
        var status = await GitAsync(path, ct, "status", "--porcelain", "--untracked-files=no");
        var branch = symbolic.ExitCode == 0 ? symbolic.Stdout.Trim() : null;
        if (branch is not null && branch.StartsWith("refs/heads/", StringComparison.Ordinal))
            branch = branch["refs/heads/".Length..];

        return new(saved?.CreationId, saved?.InitialSha, saved?.Branch ?? branch, saved?.RepositoryPath ?? Posix(_repository),
            path, saved?.GitDirectory, head.ExitCode == 0 ? head.Stdout.Trim() : null,
            registered, status.ExitCode == 0 && status.Stdout.Trim().Length == 0, locked);
    }

    /// <summary>
    /// CARD-0604 G-38. The restoration record lives in the producer's own common git dir, which
    /// is on this runner. The desktop filesystem is never consulted for a remote task -- there is
    /// nothing there to consult.
    /// </summary>
    public async Task<PhoneHomeVerificationReadRestorationResponse> ReadVerificationRestorationAsync(
        PhoneHomeVerificationReadRestorationRequest request, CancellationToken ct)
    {
        var common = Posix(request.CommonGitDirectory ?? "").TrimEnd('/');
        if (common.Length == 0 || !common.StartsWith(Posix(_repository).TrimEnd('/') + "/", StringComparison.Ordinal))
            throw Refuse("The evidence root must live under this runner's repository.");
        if (request.SourceOperationId == Guid.Empty || request.TaskId == Guid.Empty)
            throw Refuse("Restoration read needs both identities.");
        var path = $"{common}/antiphon/verification/{request.SourceOperationId:N}/{request.TaskId:N}/restoration.json";
        if (!File.Exists(path)) return new((byte[]?)null);
        // A symlinked evidence file could point anywhere; the record must be what the producer
        // actually wrote, in the place it was required to write it.
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw Refuse("The restoration record is a link.");
        return new(await File.ReadAllBytesAsync(path, ct));
    }

    public async Task<PhoneHomeVerificationRemoveResponse> RemoveVerificationAsync(
        PhoneHomeVerificationRemoveRequest request, CancellationToken ct)
    {
        var coordinates = request.Coordinates;
        if (coordinates is null || !IsUnderWorktreeRoot(coordinates.WorktreePath))
            throw Refuse("Only a verification snapshot under the runner worktree root may be removed.");
        var identifier = LastSegment(coordinates.WorktreePath);
        if (!NamePattern.IsMatch(identifier)) throw Refuse("Verification identifier must be task-<8 hex>.");
        var path = coordinates.WorktreePath;
        if (!Directory.Exists(path))
        {
            await GitAsync(_repository, ct, "worktree", "prune");
            var pruned = await DeleteBranchAsync(coordinates.Branch, ct);
            File.Delete(VerificationMetadataPath(identifier));
            return new(true, true, pruned, null);
        }

        var head = await GitAsync(path, ct, "rev-parse", "HEAD");
        if (head.ExitCode != 0 || head.Stdout.Trim() != request.ExpectedSha)
            return new(false, false, false, path + " (HEAD is not the landed sha)");
        var tracked = await GitAsync(path, ct, "status", "--porcelain", "--untracked-files=no");
        if (tracked.ExitCode != 0 || tracked.Stdout.Trim().Length != 0)
            return new(false, false, false, path + " (tracked changes present)");

        // Exact expected outputs, nothing else. Not a glob, not a prefix: an unknown file in a
        // verification snapshot is evidence about what ran there, and deleting it destroys the
        // only copy of something nobody chose to keep.
        var untracked = await GitAsync(path, ct, "status", "--porcelain", "--untracked-files=all");
        var unknown = untracked.ExitCode != 0 ? ["status unavailable"] : untracked.Stdout.Replace("\r\n", "\n").Split('\n')
            .Where(line => line.StartsWith("?? ", StringComparison.Ordinal))
            .Select(line => line[3..].Trim().Trim('"'))
            .Where(name => !(request.Outputs ?? []).Contains(name, StringComparer.Ordinal))
            .ToArray();
        if (unknown.Length != 0)
            return new(false, false, false, path + " (unexpected files: " + string.Join(", ", unknown.Take(5)) + ")");

        // No --force. If git objects to the removal, that objection is the answer.
        var remove = await GitAsync(_repository, ct, "worktree", "remove", path);
        if (remove.ExitCode != 0 && Directory.Exists(path))
            return new(false, false, false, path + " (" + Tail(remove.Stderr) + ")");
        await GitAsync(_repository, ct, "worktree", "prune");
        var branchDeleted = await DeleteBranchAsync(coordinates.Branch, ct);
        var gone = !Directory.Exists(path);
        if (gone) File.Delete(VerificationMetadataPath(identifier));
        return new(gone, gone, branchDeleted, gone ? null : path);
    }

    private async Task<bool> DeleteBranchAsync(string branch, CancellationToken ct)
    {
        if (!BranchForIdentifier.IsMatch(branch ?? "")) return false;
        var exists = await GitAsync(_repository, ct, "rev-parse", "--verify", "--quiet", "refs/heads/" + branch);
        if (exists.ExitCode != 0) return true;
        var delete = await GitAsync(_repository, ct, "branch", "-D", branch!);
        return delete.ExitCode == 0;
    }

    private async Task<bool> HasSequencerAsync(string gitDirectory)
    {
        await Task.CompletedTask;
        var dir = Posix(gitDirectory ?? "").TrimEnd('/');
        if (dir.Length == 0) return false;
        foreach (var marker in new[] { "rebase-merge", "rebase-apply", "sequencer" })
            if (Directory.Exists(dir + "/" + marker)) return true;
        foreach (var marker in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "BISECT_LOG" })
            if (File.Exists(dir + "/" + marker)) return true;
        return false;
    }

    private static bool ListedFlagAsync(string porcelain, string path, string flag)
    {
        var lines = porcelain.Replace("\r\n", "\n").Split('\n');
        var inEntry = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
                inEntry = Posix(line["worktree ".Length..].Trim()).TrimEnd('/') == path.TrimEnd('/');
            else if (inEntry && line.Trim().StartsWith(flag, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private async Task<string> RequireOutputAsync(string cwd, CancellationToken ct, params string[] args)
    {
        var result = await GitAsync(cwd, ct, args);
        if (result.ExitCode != 0) throw Refuse("git " + string.Join(' ', args) + " failed: " + Tail(result.Stderr));
        return result.Stdout.Trim();
    }

    private static VerificationCreationCoordinates Coordinates(VerificationCreationMetadata metadata) =>
        new(metadata.RepositoryPath, metadata.CommonGitDirectory, metadata.WorktreePath,
            metadata.GitDirectory, metadata.Branch, metadata.CreationId);

    private bool IsUnderWorktreeRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = Posix(path).TrimEnd('/');
        var prefix = _worktreeRoot + "/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = normalized[prefix.Length..];
        return rest.Length > 0 && !rest.Contains('/', StringComparison.Ordinal) && rest is not ("." or "..");
    }

    private static string LastSegment(string? path) =>
        Posix(path ?? "").TrimEnd('/').Split('/') is { Length: > 0 } parts ? parts[^1] : "";

    private static string Posix(string path) => path.Replace('\\', '/');

    private static PhoneHomeAdmissionException Refuse(string message) =>
        new(PhoneHomeProblemTypes.UnsupportedTarget, message, 409);
}
