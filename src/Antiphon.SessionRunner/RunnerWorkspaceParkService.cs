using System.Security.Cryptography;
using System.Text;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1065 S2. Strict publication for ordinary task-owned mirrors. Deliberately has no
/// wire registration or release caller. Legacy workspacePublishV1 keeps its old semantics.
/// </summary>
public sealed class RunnerWorkspaceParkService(RunnerWorkspaceService workspace)
{
    // I/O barriers only: tests may change the real repository or cancel an actual operation.
    internal Func<WorkspaceParkBoundary, CancellationToken, Task>? BoundaryAsync { get; init; }

    public Task<WorkspaceParkResult> PrepareAsync(WorkspaceParkRequest request, CancellationToken ct) =>
        ExecuteAsync(request, null, ct);

    /// <summary>Read-only fresh proof. A stale receipt cannot trigger another push.</summary>
    public Task<WorkspaceParkResult> VerifyAsync(WorkspaceParkReceipt receipt, CancellationToken ct) =>
        ExecuteAsync(receipt.Request, receipt, ct);

    private async Task<WorkspaceParkResult> ExecuteAsync(
        WorkspaceParkRequest request, WorkspaceParkReceipt? receipt, CancellationToken ct)
    {
        var pushing = false;
        try
        {
            if (ValidateLexicalTarget(request) is { } lexical) return Held(lexical);
            if (!ValidBinding(request.Binding)) return Held("park_invalid_binding");
            if (ResolveTarget(request.Path) is not { } path) return Held("park_root_changed");

            var ownership = await InspectRepositoryAsync(path, request.Binding.RepositoryIdentity, ct);
            if (ownership.Failure is { } ownershipFailure) return ownershipFailure;
            var common = ownership.Common!;

            // Same common-directory file lease convention as RepositoryMutationLease. A busy
            // repository is a retryable hold, never an invitation to push outside the lease.
            var leaseDirectory = Path.Combine(common, "antiphon");
            Directory.CreateDirectory(leaseDirectory);
            FileStream lease;
            try
            {
                lease = new FileStream(Path.Combine(leaseDirectory, "landing.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { return Held("park_repository_lease_busy"); }
            await using var ownedLease = lease;
            ct.ThrowIfCancellationRequested();

            var before = await InspectSourceAsync(path, request.Binding, ct);
            if (before.Failure is { } sourceFailure) return sourceFailure;
            var tip = before.Sha!;
            if (receipt is not null && (receipt.ReceiptId == Guid.Empty || !receipt.Clean
                || !receipt.DescendsFromBaseline || receipt.SourceSha != tip || receipt.RemoteSha != tip))
                return Held("park_source_changed");
            var ancestry = await workspace.GitAsync(path, ct, "merge-base", "--is-ancestor",
                request.Binding.BaselineSha, tip);
            if (AncestryDecision(ancestry.ExitCode, "park_baseline_diverged") is { } baselineFailure)
                return baselineFailure;

            var endpoint = await ReadEndpointAsync(path, request.Binding.EndpointFingerprint, ct);
            if (endpoint.Failure is { } endpointFailure) return endpointFailure;
            var remoteBefore = await ReadExactRefAsync(path, endpoint.Url!, request.Binding.FullRef, ct);
            if (remoteBefore.Failure is { } readFailure) return readFailure;
            if (remoteBefore.Sha is { } remote && remote != tip)
            {
                var forward = await workspace.GitAsync(path, ct, "merge-base", "--is-ancestor", remote, tip);
                if (AncestryDecision(forward.ExitCode, "park_remote_not_ancestor") is { } remoteFailure)
                    return remoteFailure;
            }

            if (remoteBefore.Sha != tip)
            {
                if (receipt is not null) return Held("park_publish_unconfirmed");
                await AtBoundaryAsync(WorkspaceParkBoundary.BeforePush, ct);
                // A captured SHA, one owned ref, and no force: branch drift cannot publish some
                // other local tip. A concurrent remote writer still wins Git's FF check.
                pushing = true;
                using var pushBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pushBudget.CancelAfter(TimeSpan.FromSeconds(60));
                var push = await workspace.GitAsync(path, pushBudget.Token, "push", "--no-follow-tags",
                    "--recurse-submodules=no", "--", endpoint.Url!, tip + ":" + request.Binding.FullRef);
                if (push.ExitCode != 0) return Held("park_push_rejected");
                pushing = false;
                await AtBoundaryAsync(WorkspaceParkBoundary.AfterPush, ct);
            }

            // Even equal and previously absent refs require a second, fresh exact observation.
            await AtBoundaryAsync(WorkspaceParkBoundary.BeforeFinalObservation, ct);
            var finalEndpoint = await ReadEndpointAsync(path, request.Binding.EndpointFingerprint, ct);
            if (finalEndpoint.Failure is { } finalEndpointFailure) return finalEndpointFailure;
            var remoteAfter = await ReadExactRefAsync(path, finalEndpoint.Url!, request.Binding.FullRef, ct);
            if (remoteAfter.Failure is { } finalReadFailure) return finalReadFailure;
            if (remoteAfter.Sha != tip) return Held("park_publish_unconfirmed");
            await AtBoundaryAsync(WorkspaceParkBoundary.AfterFinalObservation, ct);

            if (ResolveTarget(request.Path) != path) return Held("park_root_changed");
            var finalOwnership = await InspectRepositoryAsync(path, request.Binding.RepositoryIdentity, ct);
            if (finalOwnership.Failure is { } finalOwnershipFailure) return finalOwnershipFailure;
            if (!RunnerWorkspaceService.PathsEqual(common, finalOwnership.Common!)) return Held("park_repository_changed");
            var after = await InspectSourceAsync(path, request.Binding, ct);
            if (after.Failure is { } afterFailure) return afterFailure;
            if (after.Sha != tip) return Held("park_source_changed");
            var lastEndpoint = await ReadEndpointAsync(path, request.Binding.EndpointFingerprint, ct);
            if (lastEndpoint.Failure is { } lastEndpointFailure) return lastEndpointFailure;

            return new(WorkspaceParkOutcome.Published, "park_published", receipt ?? new(
                Guid.NewGuid(), request, tip, remoteAfter.Sha!, remoteBefore.Sha,
                Clean: true, DescendsFromBaseline: true, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException)
        {
            // Cancellation is uncertain publication, including a push whose ACK was lost.
            return pushing ? Held("park_push_rejected") : Unknown();
        }
        catch (Exception ex) when (ex is PhoneHomeAdmissionException or IOException
            or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Do not copy Git stderr, remote URLs or filesystem paths into a receipt/refusal.
            return pushing ? Held("park_push_rejected") : Unknown();
        }
    }

    internal string? ValidateLexicalTarget(WorkspaceParkRequest request)
    {
        if (!workspace.IsUnderRoot(request.Path)) return "park_path_unowned";
        var name = "task-" + request.Binding.TaskId.ToString("N")[..8];
        if (Path.GetFileName(request.Path.TrimEnd('/', '\\')) != name
            || request.Binding.FullRef != "refs/heads/feat/card-" + name
            || !IsObjectId(request.Binding.BaselineSha)) return "park_invalid_target";
        return null;
    }

    private string? ResolveTarget(string path)
    {
        var resolved = RunnerWorkspaceService.TryResolveFinal(path);
        var root = RunnerWorkspaceService.TryResolveFinal(workspace.WorktreeRoot);
        return resolved is not null && root is not null && Directory.Exists(resolved)
            && RunnerWorkspaceService.IsInside(resolved, root)
            && RunnerWorkspaceService.PathsEqual(Path.GetDirectoryName(resolved)!, root)
            ? resolved : null;
    }

    private async Task<(string? Common, WorkspaceParkResult? Failure)> InspectRepositoryAsync(
        string path, string expectedIdentity, CancellationToken ct)
    {
        var read = await workspace.GitAsync(path, ct, "rev-parse", "--path-format=absolute", "--git-common-dir");
        if (read.ExitCode != 0) return (null, Unknown());
        var common = RunnerWorkspaceService.TryResolveFinal(read.Stdout.Trim());
        if (common is null) return (null, Unknown());
        if (!workspace.IsOwnedCommonDirectory(common)) return (null, Held("park_repository_unowned"));
        if (RepositoryIdentity(common) != expectedIdentity) return (null, Held("park_repository_changed"));
        return (common, null);
    }

    internal async Task<(string? Sha, WorkspaceParkResult? Failure)> InspectSourceAsync(
        string path, WorkspaceParkBinding binding, CancellationToken ct)
    {
        var symbolic = await workspace.GitAsync(path, ct, "symbolic-ref", "-q", "HEAD");
        if (symbolic.ExitCode == 1) return (null, Held("park_not_on_branch"));
        if (symbolic.ExitCode != 0) return (null, Unknown());
        if (symbolic.Stdout.Trim() != binding.FullRef) return (null, Held("park_not_on_branch"));
        var top = await workspace.GitAsync(path, ct, "rev-parse", "--show-toplevel");
        if (top.ExitCode != 0) return (null, Unknown());
        if (!RunnerWorkspaceService.PathsEqual(top.Stdout.Trim(), path))
            return (null, Held("park_path_unowned"));
        var gitDir = await workspace.GitAsync(path, ct, "rev-parse", "--path-format=absolute", "--git-dir");
        if (gitDir.ExitCode != 0) return (null, Unknown());
        if (HasActiveSequencer(gitDir.Stdout.Trim())) return (null, Held("park_sequencer_active"));
        var head = await workspace.GitAsync(path, ct, "rev-parse", "--verify", "HEAD^{commit}");
        var status = await workspace.GitAsync(path, ct, "status", "--porcelain=v1", "-z",
            "--untracked-files=all", "--ignore-submodules=none");
        if (head.ExitCode != 0 || !IsObjectId(head.Stdout.Trim()) || status.ExitCode != 0)
            return (null, Unknown());
        if (status.Stdout.Length != 0) return (null, Held("park_dirty"));
        // Sparse/assume-unchanged index flags can hide modified tracked source from status.
        var flags = await workspace.GitAsync(path, ct, "ls-files", "-v", "-z");
        if (flags.ExitCode != 0) return (null, Unknown());
        if (flags.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line[0] == 'S' || char.IsLower(line[0])))
            return (null, Held("park_index_unverifiable"));
        return (head.Stdout.Trim(), null);
    }

    private static bool HasActiveSequencer(string directory) =>
        new[] { "rebase-merge", "rebase-apply", "sequencer", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD" }
            .Any(name => Directory.Exists(Path.Combine(directory, name)) || File.Exists(Path.Combine(directory, name)));

    internal static WorkspaceParkResult? AncestryDecision(int exitCode, string refusal) =>
        exitCode switch { 0 => null, 1 => Held(refusal), _ => Unknown() };

    private async Task<(string? Url, WorkspaceParkResult? Failure)> ReadEndpointAsync(
        string path, string expectedFingerprint, CancellationToken ct)
    {
        // Observe the publication endpoint, including pushurl and URL rewrites, just as the
        // server's TaskProgressGit does. Never substitute the fetch/tracking endpoint.
        var result = await workspace.GitAsync(path, ct, "remote", "get-url", "--push", "--all", "origin");
        if (result.ExitCode != 0) return (null, Unknown());
        var urls = result.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (urls.Length != 1 || string.IsNullOrWhiteSpace(urls[0]) || urls[0].StartsWith('-'))
            return (null, Unknown());
        if (Fingerprint(urls[0]) != expectedFingerprint) return (null, Held("park_endpoint_changed"));
        return (urls[0], null);
    }

    private async Task<(string? Sha, WorkspaceParkResult? Failure)> ReadExactRefAsync(
        string path, string endpoint, string fullRef, CancellationToken ct)
    {
        var result = await workspace.GitAsync(path, ct, "ls-remote", "--refs", "--exit-code", "--", endpoint, fullRef);
        if (result.ExitCode == 2 && string.IsNullOrWhiteSpace(result.Stdout)) return (null, null);
        if (result.ExitCode != 0) return (null, Unknown());
        var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var fields = lines.Length == 1 ? lines[0].TrimEnd('\r').Split('\t') : [];
        if (fields.Length != 2 || fields[1] != fullRef || !IsObjectId(fields[0])) return (null, Unknown());
        return (fields[0], null);
    }

    // Canonical common-directory identity is shared by all mirrors of this repository.
    internal static string RepositoryIdentity(string commonDirectory) => Fingerprint(
        OperatingSystem.IsWindows() ? Path.GetFullPath(commonDirectory).ToUpperInvariant() : Path.GetFullPath(commonDirectory));
    internal static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool IsObjectId(string? value) => value is { Length: 40 or 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool ValidBinding(WorkspaceParkBinding b) => b.ParkId != Guid.Empty
        && b.ActionId != Guid.Empty && b.TaskId != Guid.Empty && b.Attempt > 0
        && b.BlockEventId != Guid.Empty && b.AgentId != Guid.Empty && !string.IsNullOrWhiteSpace(b.RunnerId)
        && b.RunnerStoreId != Guid.Empty && b.SessionId != Guid.Empty && b.AcceptedStartedAt != default
        && b.TaskConcurrencyToken != Guid.Empty && !string.IsNullOrWhiteSpace(b.ReportDigest)
        && !string.IsNullOrWhiteSpace(b.RepositoryIdentity) && !string.IsNullOrWhiteSpace(b.EndpointFingerprint);
    private Task AtBoundaryAsync(WorkspaceParkBoundary boundary, CancellationToken ct) =>
        BoundaryAsync?.Invoke(boundary, ct) ?? Task.CompletedTask;
    private static WorkspaceParkResult Held(string reason) => new(WorkspaceParkOutcome.Held, reason);
    private static WorkspaceParkResult Unknown() => new(WorkspaceParkOutcome.Unknown, "park_inspection_unavailable");
}

internal enum WorkspaceParkBoundary { BeforePush, AfterPush, BeforeFinalObservation, AfterFinalObservation }
