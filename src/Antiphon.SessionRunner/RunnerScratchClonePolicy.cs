using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.SessionRunner;

/// <summary>
/// A scratch name is not deletion authority. The producer records its owner worktree;
/// that owner and its retiring copies must be absent. Standalone clones must be clean
/// and every local/ref-log commit must be contained by the runner's live origin.
/// Linked worktrees are left for their owning repository's worktree-removal path.
/// </summary>
internal sealed class RunnerScratchClonePolicy(RunnerWorkspaceService workspace, string retiringRoot)
{
    internal const string OwnerFile = ".antiphon-scratch-owner";

    internal async Task<string?> CheckAsync(string root, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            if (!OperatingSystem.IsLinux()) return "scratch_audit_unknown";
            string owner;
            using (var file = new AgentPinPosixReader().Open(Path.Combine(root, OwnerFile)))
            {
                if (file.Length is < 1 or > 4096) return "scratch_registration_invalid";
                var bytes = new byte[checked((int)file.Length)];
                file.ReadExactly(bytes);
                if (file.ReadByte() != -1) return "scratch_registration_invalid";
                owner = new UTF8Encoding(false, true).GetString(bytes);
            }
            if (owner.Any(char.IsControl) || !Path.IsPathFullyQualified(owner)
                || Path.GetFullPath(owner) != owner || Path.GetDirectoryName(owner) != workspace.WorktreeRoot)
                return "scratch_registration_invalid";
            var leaf = Path.GetFileName(owner);
            if (!Regex.IsMatch(leaf, @"^(?:task-[0-9a-f]{8}|ws-[A-Za-z0-9-]+)\z", RegexOptions.CultureInvariant))
                return "scratch_registration_invalid";
            if (leaf.StartsWith("task-", StringComparison.Ordinal)
                && !Path.GetFileName(root).Contains(leaf[5..], StringComparison.Ordinal))
                return "scratch_registration_invalid";
            if (RunnerTempSweep.DefaultMirrorPresent(owner)
                || RunnerTempSweep.DefaultMirrorPresent(retiringRoot.TrimEnd('/') + "/" + leaf + ".*"))
                return Antiphon.SessionRunner.Contracts.TempSweepCodes.MirrorPresent;

            // The only non-repository file at a container root is its ownership record.
            // Refuse links and unknown content rather than recursively guessing repo roots.
            var repos = new List<string>();
            if (File.Exists(Path.Combine(root, ".git"))) return "scratch_linked_worktree";
            if (Directory.Exists(Path.Combine(root, ".git"))) repos.Add(root);
            else
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(root))
                {
                    if (Path.GetFileName(entry) == OwnerFile) continue;
                    if (!Directory.Exists(entry) || new DirectoryInfo(entry).LinkTarget is not null)
                        return "scratch_content_unknown";
                    if (File.Exists(Path.Combine(entry, ".git"))) return "scratch_linked_worktree";
                    if (!Directory.Exists(Path.Combine(entry, ".git"))) return "scratch_content_unknown";
                    repos.Add(entry);
                }
            }
            if (repos.Count is < 1 or > 16) return "scratch_content_unknown";
            var endpoint = await workspace.LiveOriginEndpointAsync(workspace.RunnerRepositories()[0], budget.Token);
            if (endpoint is null) return "scratch_origin_unknown";
            var remote = await workspace.InspectionGitAsync(root, budget.Token, "ls-remote", "--heads", "--", endpoint);
            if (remote.ExitCode != 0) return "scratch_origin_unknown";
            var tips = new List<string>();
            foreach (var line in remote.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.TrimEnd('\r').Split('\t');
                if (parts.Length != 2 || !Regex.IsMatch(parts[0], @"^[0-9a-f]{40}\z")
                    || !parts[1].StartsWith("refs/heads/", StringComparison.Ordinal))
                    return "scratch_origin_unknown";
                tips.Add(parts[0]);
            }
            if (tips.Count is < 1 or > 10000) return "scratch_origin_unknown";
            foreach (var repo in repos)
            {
                // Never let core.worktree or a linked common directory redirect the audit.
                var top = await workspace.InspectionGitAsync(repo, budget.Token, "rev-parse", "--show-toplevel");
                var common = await workspace.InspectionGitAsync(repo, budget.Token, "rev-parse", "--path-format=absolute", "--git-common-dir");
                if (top.ExitCode != 0 || top.Stdout.Trim() != repo || common.ExitCode != 0
                    || common.Stdout.Trim() != Path.Combine(repo, ".git")
                    || !RunnerCompletedCardWorkspaceRemoval.UnlinkedPath(Path.Combine(repo, ".git")))
                    return "scratch_repository_unknown";
                var origin = await workspace.InspectionGitAsync(repo, budget.Token, "remote", "get-url", "origin");
                if (origin.ExitCode != 0 || origin.Stdout.Trim() != endpoint) return "scratch_origin_unknown";
                // Status intentionally hides assume-unchanged and skip-worktree edits.
                // Their presence makes cleanliness unprovable, even with a clean status.
                var index = await workspace.InspectionGitAsync(repo, budget.Token, "ls-files", "-v", "-z");
                if (index.ExitCode != 0) return "scratch_audit_unknown";
                if (index.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                    .Any(entry => entry[0] == 'S' || char.IsLower(entry[0]))) return "scratch_index_hidden";
                string[] statusArgs = ["status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none", "--", "."];
                if (repo == root) statusArgs = [.. statusArgs, ":(exclude)" + OwnerFile];
                var status = await workspace.InspectionGitAsync(repo, budget.Token, statusArgs);
                if (status.ExitCode != 0) return "scratch_audit_unknown";
                if (status.Stdout.Length > 0) return "scratch_dirty";
                var unpublished = await workspace.InspectionGitAsync(repo, budget.Token,
                    ["rev-list", "--count", "--all", "--reflog", "HEAD", "--not", .. tips.Distinct(StringComparer.Ordinal)]);
                if (unpublished.ExitCode != 0 || !long.TryParse(unpublished.Stdout.Trim(), out var count))
                    return "scratch_publication_unknown";
                if (count != 0) return "scratch_unpublished";
            }
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "scratch_audit_unknown";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PhoneHomeAdmissionException)
        {
            return "scratch_audit_unknown";
        }
    }
}
