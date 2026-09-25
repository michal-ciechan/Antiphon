using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-0688 D-3/D-5. One persistent land worktree per repository under the managed root, detached and
/// <c>worktree lock</c>ed. It carries no branch and no metadata record, so the delegate listing, the health
/// scan, the stale-prune janitor and <c>git worktree prune</c> all leave it alone. Callers hold the land lease.
/// </summary>
public sealed class LandWorkspace(ILandingGit git, IOptions<GitSettings> settings, TimeProvider clock) : ILandWorkspace
{
    public const string LockReason = "antiphon-land";
    public const string ForeignCode = "land_worktree_foreign";
    public const string SequencerStuckCode = "land_worktree_sequencer_stuck";
    public const string UnreadyCode = "land_worktree_unready";
    private static readonly string[] SequencerMarkers =
        ["rebase-merge", "rebase-apply", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "sequencer"];

    private readonly GitSettings _settings = settings.Value;

    public string PathFor(string commonDirectory)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(commonDirectory)).Replace('\\', '/');
        if (OperatingSystem.IsWindows()) canonical = canonical.ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..12].ToLowerInvariant();
        return Path.Combine(WorktreeRoots.Resolve(_settings), "land", hash);
    }

    public async Task<LandWorkspaceState> EnsureAsync(string repository, string path, string sha,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<LandingGitResult>>? mutate, CancellationToken ct)
    {
        mutate ??= (directory, arguments, token) => git.RunAsync(directory, arguments, token);
        LandWorkspaceState Refuse(string reason, string? detail = null) => new(path, null, false, reason, detail);
        if (!LandingGit.IsOid(sha)) return Refuse(UnreadyCode, "invalid commit");
        var common = await git.CommonDirectoryAsync(repository, ct);
        var created = false;
        if (!Exists(Path.Combine(path, ".git")))
        {
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
                return Refuse(ForeignCode, $"{path} exists without a worktree .git file");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Twice-forced: git's documented override for a registered-but-missing (or locked) path. It heals
            // a deleted directory without `worktree prune`, which would prune every missing registration.
            var add = await mutate(repository, ["worktree", "add", "--detach", "--force", "--force", path, sha], ct);
            if (!add.Succeeded) return Refuse("land_worktree_add_failed", add.Diagnostic);
            created = true;
        }
        else
        {
            if (!LandingGit.PathsEqual(await git.CommonDirectoryAsync(path, ct), common))
                return Refuse(ForeignCode, $"{path} belongs to another repository");
            var observation = await git.InspectIndexLockAsync(path, ct);
            if (GitIndexLock.Refusal(observation, GitIndexLock.StaleAfter(_settings.IndexLockStaleAfterSeconds),
                    clock.GetUtcNow().UtcDateTime) is { } held)
                return Refuse(held.Code, held.Detail);
        }

        var admin = AdminDirectory(path);
        if (admin is null) return Refuse(ForeignCode, $"{path}/.git does not name an admin directory");
        if (!created)
        {
            // Disposable by design: this invocation holds the land lease, so any sequencer here is an
            // interrupted land's. Abort it; --quit drops state an abort cannot read; the reset clears the rest.
            foreach (var (command, markers) in new[]
                     {
                         ("rebase", new[] { "rebase-merge", "rebase-apply" }),
                         ("cherry-pick", new[] { "CHERRY_PICK_HEAD", "sequencer" }),
                         ("revert", new[] { "REVERT_HEAD" }),
                         ("merge", new[] { "MERGE_HEAD" }),
                     })
            {
                if (!markers.Any(m => Exists(Path.Combine(admin, m)))) continue;
                await mutate(path, [command, "--abort"], ct);
                if (command != "merge" && markers.Any(m => Exists(Path.Combine(admin, m))))
                    await mutate(path, [command, "--quit"], ct);
            }
            // A branch someone checked out here must never move with the reset: detach first.
            var attached = await git.RunAsync(path, ["symbolic-ref", "-q", "HEAD"], ct);
            if (attached.ExitCode == 0)
            {
                var head = await git.RunAsync(path, ["rev-parse", "--verify", "HEAD^{commit}"], ct);
                if (!head.Succeeded) return Refuse(UnreadyCode, "attached HEAD is unreadable");
                var detach = await mutate(path, ["update-ref", "--no-deref", "HEAD", head.Output.Trim()], ct);
                if (!detach.Succeeded) return Refuse(UnreadyCode, "could not detach HEAD");
            }
            var reset = await mutate(path, ["reset", "--hard", sha], ct);
            if (!reset.Succeeded) return Refuse("land_worktree_reset_failed", reset.Diagnostic);
            var clean = await mutate(path, ["clean", "-fdx"], ct);
            if (!clean.Succeeded) return Refuse("land_worktree_clean_failed", clean.Diagnostic);
            if (HasSequencer(admin)) return Refuse(SequencerStuckCode, $"{admin} still holds sequencer state");
        }

        var confirmed = await git.RunAsync(path, ["rev-parse", "--verify", "HEAD^{commit}"], ct);
        if (!confirmed.Succeeded || confirmed.Output.Trim() != sha) return Refuse(UnreadyCode, "HEAD is not the requested commit");
        var symbolic = await git.RunAsync(path, ["symbolic-ref", "-q", "HEAD"], ct);
        if (symbolic.ExitCode != 1) return Refuse(UnreadyCode, "HEAD is not detached");
        var status = await git.RunAsync(path, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], ct);
        if (!status.Succeeded || status.Output.Length != 0) return Refuse(UnreadyCode, "the land worktree is not clean");
        if (!Exists(Path.Combine(admin, "locked")))
        {
            var locked = await git.RunAsync(repository, ["worktree", "lock", "--reason", LockReason, path], ct);
            if (!locked.Succeeded && !Exists(Path.Combine(admin, "locked"))) return Refuse("land_worktree_lock_failed", locked.Diagnostic);
        }
        return new(path, sha, created, null);
    }

    public IReadOnlyList<LandingHeadFile> ScanHeadFiles(string commonDirectory) => Scan(commonDirectory);

    /// <summary>The HEAD-file scan, usable without a configured worktree root.</summary>
    public static IReadOnlyList<LandingHeadFile> Scan(string commonDirectory)
    {
        var rows = new List<LandingHeadFile>();
        var main = MainCheckout(commonDirectory);
        var (mainRef, mainSha) = ReadHead(Path.Combine(commonDirectory, "HEAD"));
        rows.Add(new(commonDirectory, main, mainRef, mainSha, true));
        var linked = Path.Combine(commonDirectory, "worktrees");
        if (!Directory.Exists(linked)) return rows;
        foreach (var admin in Directory.EnumerateDirectories(linked))
        {
            var head = Path.Combine(admin, "HEAD");
            if (!File.Exists(head)) continue;
            var (symbolic, sha) = ReadHead(head);
            string? worktree = null;
            try
            {
                var gitdir = File.ReadAllText(Path.Combine(admin, "gitdir")).Trim();
                if (gitdir.Length > 0) worktree = Path.GetDirectoryName(Path.GetFullPath(gitdir));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            rows.Add(new(admin, worktree, symbolic, sha, false));
        }
        return rows;
    }

    /// <summary>The main checkout for a non-bare repository whose common directory is its <c>.git</c>.</summary>
    public static string? MainCheckout(string commonDirectory) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(commonDirectory)) == ".git"
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(commonDirectory)) : null;

    private static (string? Symbolic, string? Sha) ReadHead(string file)
    {
        string text;
        try { text = File.ReadAllText(file).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, null); }
        if (text.StartsWith("ref:", StringComparison.Ordinal)) return (text[4..].Trim(), null);
        return (null, LandingGit.IsOid(text) ? text : null);
    }

    private static string? AdminDirectory(string path)
    {
        var marker = Path.Combine(path, ".git");
        if (Directory.Exists(marker)) return marker;
        try
        {
            var text = File.ReadAllText(marker).Trim();
            if (!text.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
            var admin = text[7..].Trim();
            return Path.GetFullPath(Path.IsPathRooted(admin) ? admin : Path.Combine(path, admin));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool HasSequencer(string admin) => SequencerMarkers.Any(m => Exists(Path.Combine(admin, m)));

    private static bool Exists(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }
}
