using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Read-only inspector with explicit S5 removal in the companion partial.
/// C1032_InspectionReadOnlyBound checks repository/tree/store bytes.
/// </summary>
public sealed partial class RunnerCompletedCardWorkspaceRemoval(RunnerWorkspaceService workspace, string? landingMapKey)
{
    public RunnerCompletedCardWorkspaceRemoval(RunnerWorkspaceService workspace) : this(workspace, null) { }
    public async Task<RunnerCompletedCardWorkspaceInspection> InspectAsync(
        CompletedCardWorkspaceInspectRequest request, CancellationToken ct)
    {
        if (request.SchemaVersion != CompletedCardWorkspaceProtocol.SchemaVersion) return Held("schema_version");
        try
        {
            if (workspace.InspectionBindingReason(request.Binding) is { } bindingReason) return Held(bindingReason);
            var binding = request.Binding;
            if (!Directory.Exists(binding.WorktreePath)) return Held("stale_mirror_binding");
            if (!UnlinkedPath(binding.WorktreePath) || !UnlinkedPath(binding.GitDirectory)
                || !UnlinkedPath(binding.CommonDirectory)) return Held("linked_content");
            if (await workspace.InspectionPhysicalIdentityReasonAsync(binding, ct) is { } identityReason)
                return Held(identityReason);
            var head = await ReadAsync(binding.WorktreePath, ct, "rev-parse", "HEAD");
            var symbolic = await workspace.InspectionGitAsync(binding.WorktreePath, ct, "symbolic-ref", "-q", "HEAD");
            if (symbolic.ExitCode == 1) return Held("source_ref_mismatch", head);
            if (symbolic.ExitCode != 0) return Held("inspection_unreadable", head);
            var sourceRef = symbolic.Stdout.Trim();
            if (sourceRef != binding.SourceFullRef) return Held("source_ref_mismatch", head);
            var registrations = await ReadAsync(binding.WorktreePath, ct, "worktree", "list", "--porcelain", "-z");
            var blocks = registrations.Split("\0\0", StringSplitOptions.RemoveEmptyEntries);
            var exact = blocks.Where(block => block.Split('\0').Any(line =>
                line.StartsWith("worktree ", StringComparison.Ordinal)
                && RunnerWorkspaceService.InspectionPathsEqual(line[9..], binding.WorktreePath))).ToArray();
            if (exact.Length != 1) return Held("stale_mirror_binding", head);
            if (exact[0].Split('\0').Any(line => line == "locked" || line.StartsWith("locked ", StringComparison.Ordinal)))
                return Held("locked_registration", head);
            if (exact[0].Split('\0').Any(line => line == "prunable" || line.StartsWith("prunable ", StringComparison.Ordinal)))
                return Held("stale_mirror_binding", head);
            foreach (var gitDirectory in new[] { binding.GitDirectory, binding.CommonDirectory }.Distinct(StringComparer.Ordinal))
                if (RepositoryStateReason(gitDirectory, ct) is { } stateReason) return Held(stateReason, head);
            var sparse = await workspace.InspectionGitAsync(binding.WorktreePath, ct, "config", "--bool", "--get", "core.sparseCheckout");
            if (sparse.ExitCode == 0 && sparse.Stdout.Trim() == "true") return Held("sparse_checkout", head);
            if (sparse.ExitCode != 1 && (sparse.ExitCode != 0 || sparse.Stdout.Trim() != "false"))
                return Held("inspection_unreadable", head);
            var shallow = await ReadAsync(binding.WorktreePath, ct, "rev-parse", "--is-shallow-repository");
            if (shallow == "true") return Held("shallow_repository", head);
            if (shallow != "false") return Held("inspection_unreadable", head);
            var tracked = await ReadAsync(binding.WorktreePath, ct, "ls-files", "-v", "-z");
            var flaggedPaths = new List<string>();
            foreach (var entry in tracked.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.Length < 3 || entry[1] != ' ') return Held("inspection_unreadable", head);
                if (char.IsLower(entry[0]) || entry[0] == 'S') flaggedPaths.Add(entry[2..]);
            }
            if (flaggedPaths.Count != 0) return Held("tracked_index_flags", head) with
            { HeldPaths = flaggedPaths.Order(StringComparer.Ordinal).ToArray() };
            var staged = await ReadAsync(binding.WorktreePath, ct, "ls-files", "--stage", "-z");
            if (staged.Split('\0').Any(entry => entry.StartsWith("160000 ", StringComparison.Ordinal)))
                return Held("submodule_content", head);
            var structural = StructuralReason(binding.WorktreePath, ct);
            if (structural is not null) return Held(structural, head);
            // status/diff-files can run clean filters and can hide mode changes through
            // core.fileMode. Compare staged source to HEAD, then raw physical blobs/modes.
            var stagedChanges = await ReadAsync(binding.WorktreePath, ct, "diff-index", "--cached",
                "--name-only", "-z", "--no-ext-diff", "--no-textconv", "HEAD", "--");
            if (stagedChanges.Length != 0 || !RawTrackedFilesMatch(binding.WorktreePath, staged, ct))
                return Held("dirty_source", head);
            var untracked = await ReadAsync(binding.WorktreePath, ct, "ls-files", "--others", "--exclude-standard", "-z");
            if (untracked.Length != 0) return Held("dirty_source", head);
            var ignored = await ReadAsync(binding.WorktreePath, ct, "ls-files", "--others", "--ignored", "--exclude-standard", "-z");
            var classifier = new RunnerWorkspaceIgnoredContent();
            var inventory = ignored.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(path => new RunnerIgnoredContentEntry(path, classifier.Classify(path))).OrderBy(i => i.Path, StringComparer.Ordinal).ToArray();
            var reason = inventory.Any(i => i.Kind == RunnerIgnoredContentKind.Protected) ? "protected_content"
                : inventory.Any(i => i.Kind == RunnerIgnoredContentKind.Evidence) ? "evidence_requires_admission" : null;
            return new(head, sourceRef, inventory, reason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PhoneHomeAdmissionException
            or ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return Held("inspection_unreadable");
        }
    }

    /// <summary>
    /// Read-only direct-containment proof; C1032_InspectionSourceProof covers live ref drift.
    /// Direct proof inspection refuses Rebased. C1032_AdmissionBoundRebaseProof covers
    /// the S7 signed command path in explicitly composed transport.
    /// </summary>
    public async Task<string?> CheckProofAsync(CompletedCardWorkspaceBinding binding,
        CompletedCardWorkspaceProof proof, CancellationToken ct)
    {
        var inspected = await InspectAsync(new(1, binding), ct);
        if (inspected.Reason is not null) return inspected.Reason;
        if (inspected.HeadSha != proof.ExpectedHeadSha) return "head_mismatch";
        return await CheckRemoteProofAsync(binding, proof, binding.WorktreePath, null, ct);
    }

    private async Task<string?> CheckRemoteProofAsync(CompletedCardWorkspaceBinding binding,
        CompletedCardWorkspaceProof proof, string repositoryPath, CompletedCardWorkspaceCommand? command, CancellationToken ct)
    {
        if (proof.DestinationFingerprint != binding.RepositoryIdentity
            || proof.DestinationRef != workspace.InspectionPublishedRef) return "preservation_destination_mismatch";
        var rebased = proof.ProofKind == CompletedCardWorkspaceProtocol.ProofRebased;
        if (string.IsNullOrWhiteSpace(proof.ProofIdentity)
            || rebased && (command is null || !CompletedCardWorkspaceLandingAuthority.Verify(command, landingMapKey)
                || proof.RebasedSourceSha != proof.PreservedSourceSha || !IsSha(proof.RebasedSourceSha))
            || !rebased && (proof.ProofKind != CompletedCardWorkspaceProtocol.ProofContained || proof.RebasedSourceSha is not null))
            return "preservation_proof_unsupported";
        if (!IsSha(proof.ObservedTargetSha) || !IsSha(proof.PreservedSourceSha)) return "preservation_proof_invalid";
        try
        {
            var remote = await workspace.InspectionGitAsync(repositoryPath, ct, "ls-remote", "--refs", binding.RepositoryIdentity, proof.DestinationRef);
            if (remote.ExitCode != 0) return "preservation_unreadable";
            var lines = remote.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != 1 || lines[0].TrimEnd('\r') != proof.ObservedTargetSha + "\t" + proof.DestinationRef)
                return "stale_preservation_proof";
            // No fetch: read-only inspection may hold if the fresh remote objects are not local.
            // Direct proof checks H and preserved P ancestry in that exact fresh target.
            // Rebased proof authenticates H-to-P via the signed command above, then
            // checks P ancestry; it does not require H ancestry after the rebase.
            foreach (var source in rebased ? new[] { proof.RebasedSourceSha!, proof.PreservedSourceSha }
                : new[] { proof.ExpectedHeadSha, proof.PreservedSourceSha })
            {
                var ancestor = await workspace.InspectionGitAsync(repositoryPath, ct, "merge-base", "--is-ancestor", source, proof.ObservedTargetSha);
                if (ancestor.ExitCode != 0) return "source_not_preserved";
            }
            return null;
        }
        catch (PhoneHomeAdmissionException) { return "preservation_unreadable"; }
    }

    private static bool RawTrackedFilesMatch(string root, string staged, CancellationToken ct)
    {
        foreach (var entry in staged.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            var tab = entry.IndexOf('\t');
            if (tab < 0) throw new IOException("Malformed tracked entry.");
            var fields = entry[..tab].Split(' ');
            if (fields.Length != 3 || fields[2] != "0" || fields[0] is not ("100644" or "100755")
                || !IsSha(fields[1])) return false;
            var path = Path.GetFullPath(Path.Combine(root, entry[(tab + 1)..]));
            if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new IOException("Tracked path escaped workspace.");
            if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return false;
            if (!OperatingSystem.IsWindows()
                && File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute) != (fields[0] == "100755")) return false;
            using var stream = File.OpenRead(path);
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA1);
            hash.AppendData(System.Text.Encoding.ASCII.GetBytes("blob "
                + stream.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0"));
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer)) != 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer.AsSpan(0, read)); }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(fields[1], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static bool IsSha(string sha) => sha.Length == 40 && sha.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private async Task<string> ReadAsync(string path, CancellationToken ct, params string[] args)
    {
        var result = await workspace.InspectionGitAsync(path, ct, args);
        if (result.ExitCode != 0) throw new IOException("Inspection Git read failed.");
        return args.Contains("-z") ? result.Stdout : result.Stdout.Trim();
    }
    private static RunnerCompletedCardWorkspaceInspection Held(string reason, string head = "") => new(head, "", [], reason);

    internal static bool UnlinkedPath(string path) => RunnerWorkspaceService.TryResolveFinal(path) is { } resolved
        && RunnerWorkspaceService.InspectionPathsEqual(Path.GetFullPath(path), resolved);

    // C1032_InspectionHoldShapes supplies admin/common lock and operation markers.
    private static string? RepositoryStateReason(string root, CancellationToken ct)
    {
        foreach (var marker in new[] { "MERGE_HEAD", "rebase-merge", "rebase-apply", "CHERRY_PICK_HEAD",
            "REVERT_HEAD", "BISECT_LOG", "sequencer" })
            if (RepositoryDirectoryShape.Attributes(Path.Combine(root, marker)) is not null)
                return "unfinished_operation";
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) return "linked_content";
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(entry);
                if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("gc.pid", StringComparison.OrdinalIgnoreCase)) return "repository_locked";
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) return "linked_content";
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
            }
        }
        return null;
    }

    private static string? StructuralReason(string root, CancellationToken ct)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) return "linked_content";
            if (directory != root && RepositoryDirectoryShape.HasRepositoryMarker(directory)) return "nested_repository";
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(entry);
                if (!names.Add(name)) return "case_ambiguous_content";
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                {
                    if (directory != root) return "nested_repository";
                    if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint)) return "linked_content";
                    continue;
                }
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)) return "linked_content";
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
            }
        }
        return null;
    }
}

public sealed record RunnerIgnoredContentEntry(string Path, RunnerIgnoredContentKind Kind);

/// <summary>An inspection snapshot, not cleanup authority. S5 must validate again before mutation.</summary>
public sealed record RunnerCompletedCardWorkspaceInspection(string HeadSha, string SourceFullRef,
    IReadOnlyList<RunnerIgnoredContentEntry> IgnoredContent, string? Reason)
{
    public bool Eligible => Reason is null;
    public IReadOnlyList<string> HeldPaths { get; init; } = [];
}
