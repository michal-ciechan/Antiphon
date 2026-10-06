using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>Strict local source proof using the normal owned Git children and repository lease.</summary>
public sealed class LocalTaskParkPublisher(ITaskProgressGit git, IRepositoryMutationLease leases)
{
    /// <summary>Admit the runner's credential-free repository identity against the captured
    /// desktop origin. This reads configuration only; neither URL nor stderr leaves this boundary.</summary>
    internal async Task<string?> AdmitRunnerEndpointAsync(ProgressSourceBaseline baseline, string runnerRepository, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(baseline.Remote.EndpointFingerprint)) return "park_endpoint_unknown";
            var endpoint = await EndpointAsync(baseline.RegisteredCheckout ?? baseline.CanonicalRepository,
                baseline.Remote.EndpointFingerprint, ct);
            if (endpoint.Failure is not null) return endpoint.Failure.Reason;
            if (!RepositoryCloneSource.TryNormalize(endpoint.Url, out var desktop)
                || !RepositoryCloneSource.TryNormalize(runnerRepository, out var runner))
                return "park_endpoint_unadmitted";
            // TryNormalize deliberately retains local URL spelling. A file URI and its absolute
            // path can name the same test/local repository while retaining different fingerprints.
            if (Uri.TryCreate(desktop, UriKind.Absolute, out var desktopUri) && desktopUri.IsFile
                && Uri.TryCreate(runner, UriKind.Absolute, out var runnerUri) && runnerUri.IsFile)
                return LandingGit.PathsEqual(desktopUri.LocalPath, runnerUri.LocalPath) ? null : "park_endpoint_unadmitted";
            return string.Equals(desktop, runner, StringComparison.Ordinal) ? null : "park_endpoint_unadmitted";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is IOException or UnauthorizedAccessException
            or ArgumentException or OperationCanceledException or InvalidOperationException)
        { return "park_inspection_unavailable"; }
    }

    public async Task<TaskParkPublicationResult> InspectAsync(WorkspaceParkRequest request,
        WorkspaceMode mode, string repository, TaskParkPublicationEvidence? previous, CancellationToken ct)
    {
        try
        {
            var b = request.Binding;
            if (!GitObjectId.IsFull(b.BaselineSha) || !b.FullRef.StartsWith("refs/heads/", StringComparison.Ordinal)
                || mode == WorkspaceMode.Worktree && b.FullRef != "refs/heads/" + RemoteWorkspaceService.OwnedBranch(b.TaskId))
                return Held("park_invalid_target");
            var path = await git.CanonicalDirectoryAsync(request.Path, ct);
            if (!LandingGit.PathsEqual(path, request.Path)) return Held("park_path_changed");
            var common = await git.CommonDirectoryAsync(repository, ct);
            if (Identity(common) != b.RepositoryIdentity
                || !LandingGit.PathsEqual(common, await git.CommonDirectoryAsync(path, ct)))
                return Held("park_repository_changed");
            await using var lease = await leases.TryAcquireAsync(repository,
                new RepositoryLeaseOwnerTag(b.TaskId, "blocked-task-publication"), ct);
            if (lease is null || !leases.Owns(lease, common)) return Held("park_repository_lease_busy");
            var registrations = await git.RegistrationsAsync(repository, ct);
            if (!registrations.Any(r => LandingGit.PathsEqual(r.Path, path) && r.Branch == b.FullRef))
                return Held("park_checkout_unowned");

            var before = await SourceAsync(request, ct);
            if (before.Failure is not null) return before.Failure;
            var sha = before.Sha!;
            if (previous is not null && (previous.Request != request || previous.SourceSha != sha
                || !previous.Clean || !previous.DescendsFromBaseline || previous.ReceiptId == Guid.Empty))
                return Held("park_source_changed");
            var ancestry = await git.RunAsync(path, ["merge-base", "--is-ancestor", b.BaselineSha, sha], ct);
            if (ancestry.ExitCode == 1) return Held("park_baseline_diverged");
            if (!ancestry.Succeeded) return Unknown();

            string? remote = null;
            var kind = TaskParkPublicationOutcome.Published;
            if (mode == WorkspaceMode.ReadOnly)
            {
                if (sha != b.BaselineSha) return Held("park_readonly_changed");
                kind = TaskParkPublicationOutcome.NoSourceChanges;
                if (previous is not null && (previous.Outcome != kind || previous.RemoteSha is not null))
                    return Held("park_source_changed");
            }
            else
            {
                if (previous is not null && (previous.Outcome != kind || previous.RemoteSha != sha))
                    return Held("park_source_changed");
                var endpointRead = await EndpointAsync(path, b.EndpointFingerprint, ct);
                if (endpointRead.Failure is not null) return endpointRead.Failure;
                var endpoint = endpointRead.Url!;
                var observed = await RemoteAsync(path, endpoint, b.FullRef, ct);
                if (!observed.Known) return Unknown();
                if (observed.Sha != sha)
                {
                    if (mode != WorkspaceMode.Worktree || previous is not null)
                        return Held("park_publish_unconfirmed");
                    if (observed.Sha is { } old)
                    {
                        var ff = await git.RunAsync(path, ["merge-base", "--is-ancestor", old, sha], ct);
                        if (ff.ExitCode == 1) return Held("park_remote_not_ancestor");
                        if (!ff.Succeeded) return Unknown();
                    }
                    using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    budget.CancelAfter(TimeSpan.FromSeconds(60));
                    var push = await git.RunAsync(path, ["push", "--no-follow-tags", "--recurse-submodules=no",
                        "--", endpoint, sha + ":" + b.FullRef], budget.Token);
                    if (!push.Succeeded) return Held("park_push_rejected");
                }
                var finalEndpoint = await EndpointAsync(path, b.EndpointFingerprint, ct);
                if (finalEndpoint.Failure is not null) return finalEndpoint.Failure;
                var final = await RemoteAsync(path, endpoint, b.FullRef, ct);
                if (!final.Known) return Unknown();
                if (final.Sha != sha) return Held("park_publish_unconfirmed");
                remote = final.Sha;
            }

            var after = await SourceAsync(request, ct);
            if (after.Failure is not null) return after.Failure;
            if (after.Sha != sha || !LandingGit.PathsEqual(path, await git.CanonicalDirectoryAsync(request.Path, ct))
                || !LandingGit.PathsEqual(common, await git.CommonDirectoryAsync(path, ct)))
                return Held("park_source_changed");
            if (mode != WorkspaceMode.ReadOnly)
            {
                var lastEndpoint = await EndpointAsync(path, b.EndpointFingerprint, ct);
                if (lastEndpoint.Failure is not null) return lastEndpoint.Failure;
            }
            return new(kind, kind == TaskParkPublicationOutcome.Published ? "park_published" : "park_no_source_changes",
                previous ?? new(Guid.NewGuid(), request, sha, remote, true, true, kind));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or OperationCanceledException or InvalidOperationException)
        { return Unknown(); }
    }

    private async Task<(string? Sha, TaskParkPublicationResult? Failure)> SourceAsync(
        WorkspaceParkRequest request, CancellationToken ct)
    {
        var path = request.Path;
        var branch = await git.RunAsync(path, ["symbolic-ref", "-q", "HEAD"], ct);
        if (branch.ExitCode == 1) return (null, Held("park_not_on_branch"));
        if (!branch.Succeeded) return (null, Unknown());
        if (branch.Output.Trim() != request.Binding.FullRef) return (null, Held("park_not_on_branch"));
        var top = await git.RunAsync(path, ["rev-parse", "--show-toplevel"], ct);
        var dir = await git.RunAsync(path, ["rev-parse", "--path-format=absolute", "--git-dir"], ct);
        if (!top.Succeeded || !dir.Succeeded) return (null, Unknown());
        if (!LandingGit.PathsEqual(top.Output.Trim(), path)) return (null, Held("park_path_changed"));
        if (new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-apply", "rebase-merge", "sequencer", "BISECT_LOG" }
            .Any(name => File.Exists(Path.Combine(dir.Output.Trim(), name)) || Directory.Exists(Path.Combine(dir.Output.Trim(), name))))
            return (null, Held("park_sequencer_active"));
        var head = await git.RunAsync(path, ["rev-parse", "--verify", "HEAD^{commit}"], ct);
        var status = await git.RunAsync(path, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], ct);
        var flags = await git.RunAsync(path, ["ls-files", "-v", "-z"], ct);
        if (!head.Succeeded || !GitObjectId.IsFull(head.Output.Trim()) || !status.Succeeded || !flags.Succeeded)
            return (null, Unknown());
        if (status.Output.Length != 0) return (null, Held("park_dirty"));
        if (flags.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(s => s[0] == 'S' || char.IsLower(s[0])))
            return (null, Held("park_index_unverifiable"));
        return (head.Output.Trim(), null);
    }

    private async Task<(string? Url, TaskParkPublicationResult? Failure)> EndpointAsync(string path, string fingerprint, CancellationToken ct)
    {
        var result = await git.RunAsync(path, ["remote", "get-url", "--push", "--all", "origin"], ct);
        var lines = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (!result.Succeeded || lines.Length != 1 || lines[0].StartsWith('-')) return (null, Unknown());
        return BlockedTaskParkingService.Digest(lines[0]) == fingerprint
            ? (lines[0], null) : (null, Held("park_endpoint_changed"));
    }

    private async Task<(bool Known, string? Sha)> RemoteAsync(string path, string endpoint, string fullRef, CancellationToken ct)
    {
        var result = await git.RunAsync(path, ["ls-remote", "--refs", "--exit-code", "--", endpoint, fullRef], ct);
        if (result.ExitCode == 2 && string.IsNullOrWhiteSpace(result.Output)) return (true, null);
        var fields = result.Output.TrimEnd('\r', '\n').Split('\t');
        return result.Succeeded && fields.Length == 2 && fields[1] == fullRef && GitObjectId.IsFull(fields[0])
            ? (true, fields[0]) : (false, null);
    }

    internal static string Identity(string common) => BlockedTaskParkingService.Digest(
        OperatingSystem.IsWindows() ? Path.GetFullPath(common).ToUpperInvariant() : Path.GetFullPath(common))!;
    private static TaskParkPublicationResult Held(string reason) => new(TaskParkPublicationOutcome.Held, reason);
    private static TaskParkPublicationResult Unknown() => new(TaskParkPublicationOutcome.Unknown, "park_inspection_unavailable");
}
