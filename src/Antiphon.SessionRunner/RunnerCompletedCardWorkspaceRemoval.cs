using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// S4 read-only inspector. C1032_InspectionReadOnlyBound checks repository/tree/store bytes.
/// No removal method or production composition is supplied by this slice.
/// </summary>
public sealed class RunnerCompletedCardWorkspaceRemoval(RunnerWorkspaceService workspace)
{
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
            var sourceRef = await ReadAsync(binding.WorktreePath, ct, "symbolic-ref", "-q", "HEAD");
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
            var structural = StructuralReason(binding.WorktreePath, ct);
            if (structural is not null) return Held(structural, head);
            var status = await ReadAsync(binding.WorktreePath, ct, "status", "--porcelain=v1", "-z", "--untracked-files=all");
            if (status.Length != 0) return Held("dirty_source", head);
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
    /// Rebased proof admission is held here until a bound mapping validator is supplied in S5.
    /// </summary>
    public async Task<string?> CheckProofAsync(CompletedCardWorkspaceBinding binding,
        CompletedCardWorkspaceProof proof, CancellationToken ct)
    {
        var inspected = await InspectAsync(new(1, binding), ct);
        if (inspected.Reason is not null) return inspected.Reason;
        if (inspected.HeadSha != proof.ExpectedHeadSha) return "head_mismatch";
        if (proof.DestinationFingerprint != binding.RepositoryIdentity
            || proof.DestinationRef != workspace.InspectionPublishedRef) return "preservation_destination_mismatch";
        if (proof.ProofKind != CompletedCardWorkspaceProtocol.ProofContained
            || proof.RebasedSourceSha is not null || string.IsNullOrWhiteSpace(proof.ProofIdentity))
            return "preservation_proof_unsupported";
        if (!IsSha(proof.ObservedTargetSha) || !IsSha(proof.PreservedSourceSha)) return "preservation_proof_invalid";
        try
        {
            var remote = await workspace.GitAsync(binding.WorktreePath, ct, "ls-remote", "--refs", "origin", proof.DestinationRef);
            if (remote.ExitCode != 0) return "preservation_unreadable";
            var lines = remote.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != 1 || lines[0].TrimEnd('\r') != proof.ObservedTargetSha + "\t" + proof.DestinationRef)
                return "stale_preservation_proof";
            // No fetch: read-only inspection may hold if the fresh remote objects are not local.
            // Both H and the supplied preserved P must be contained in that exact fresh target.
            foreach (var source in new[] { proof.ExpectedHeadSha, proof.PreservedSourceSha })
            {
                var ancestor = await workspace.GitAsync(binding.WorktreePath, ct, "merge-base", "--is-ancestor", source, proof.ObservedTargetSha);
                if (ancestor.ExitCode != 0) return "source_not_preserved";
            }
            return null;
        }
        catch (PhoneHomeAdmissionException) { return "preservation_unreadable"; }
    }

    private static bool IsSha(string sha) => sha.Length == 40 && sha.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private async Task<string> ReadAsync(string path, CancellationToken ct, params string[] args)
    {
        var result = await workspace.GitAsync(path, ct, args);
        if (result.ExitCode != 0) throw new IOException("Inspection Git read failed.");
        return args.Contains("-z") ? result.Stdout : result.Stdout.Trim();
    }
    private static RunnerCompletedCardWorkspaceInspection Held(string reason, string head = "") => new(head, "", [], reason);

    internal static bool UnlinkedPath(string path) => RunnerWorkspaceService.TryResolveFinal(path) is { } resolved
        && RunnerWorkspaceService.InspectionPathsEqual(Path.GetFullPath(path), resolved);

    private static string? StructuralReason(string root, CancellationToken ct)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(entry);
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
}
