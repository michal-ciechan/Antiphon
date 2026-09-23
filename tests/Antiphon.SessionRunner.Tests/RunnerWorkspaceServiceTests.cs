using System.Diagnostics;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// CARD-0604 D-15. Against a real scratch git repository, platform neutral: these are the runner's
// own boundaries on the mirror worktrees remote ordinary tasks run in. The DESKTOP worktree stays
// canonical, so everything here operates on a commit that already exists on the "origin" the
// scratch repo stands in for.
[Category("Integration")]
public sealed class RunnerWorkspaceServiceTests
{
    [Test]
    public async Task Mirror_creates_worktree_on_branch_at_sha()
    {
        using var scratch = Scratch.Create();
        var service = new RunnerWorkspaceService(scratch.Clone, scratch.Work);

        var response = await service.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, "task-deadbeef"),
            CancellationToken.None);

        response.Path.ShouldBe(scratch.Work.Replace('\\', '/') + "/worktrees/task-deadbeef");
        Directory.Exists(response.Path).ShouldBeTrue();
        Scratch.Git(response.Path, "rev-parse", "HEAD").Trim().ShouldBe(scratch.Sha);
        Scratch.Git(response.Path, "rev-parse", "--abbrev-ref", "HEAD").Trim().ShouldBe(Scratch.Branch);
    }

    [Test]
    public async Task Mirror_refuses_sha_mismatch()
    {
        using var scratch = Scratch.Create();
        var service = new RunnerWorkspaceService(scratch.Clone, scratch.Work);

        // G-27. The branch tip is whatever the desktop pushed; a mirror at some OTHER commit would
        // silently run the session against source the desktop never produced.
        var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, new string('b', 40), "task-deadbeef"),
            CancellationToken.None));
        refused.Code.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        Directory.Exists(Path.Combine(scratch.Work, "worktrees", "task-deadbeef")).ShouldBeFalse();
    }

    [Test]
    public async Task Mirror_refuses_a_name_or_sha_it_did_not_shape()
    {
        using var scratch = Scratch.Create();
        var service = new RunnerWorkspaceService(scratch.Clone, scratch.Work);

        foreach (var name in new[] { "../escape", "task-DEADBEEF", "task-dead", "", "task-deadbeef/x", "anything" })
        {
            var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.MirrorAsync(
                new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, name), CancellationToken.None));
            refused.Message.ShouldContain("task-");
        }

        foreach (var sha in new[] { "HEAD", "abc", scratch.Sha.ToUpperInvariant(), "" })
        {
            await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.MirrorAsync(
                new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, sha, "task-deadbeef"), CancellationToken.None));
        }
    }

    [Test]
    public async Task Remove_refuses_dirty_tree()
    {
        using var scratch = Scratch.Create();
        var service = new RunnerWorkspaceService(scratch.Clone, scratch.Work);
        var mirror = (await service.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, "task-deadbeef"),
            CancellationToken.None)).Path;

        // Work in the mirror that was never pushed is the ONLY copy of itself. Removing it is
        // destruction, so it is a refusal and the operator's residue, not a cleanup.
        await File.WriteAllTextAsync(Path.Combine(mirror, "unpushed.txt"), "not committed anywhere");
        var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(
            () => service.RemoveAsync(new PhoneHomeWorkspaceRemoveRequest(mirror), CancellationToken.None));
        refused.Message.ShouldContain("uncommitted");
        Directory.Exists(mirror).ShouldBeTrue();

        // A clean mirror holds nothing the branch does not, and goes.
        File.Delete(Path.Combine(mirror, "unpushed.txt"));
        var removed = await service.RemoveAsync(new PhoneHomeWorkspaceRemoveRequest(mirror), CancellationToken.None);
        removed.Removed.ShouldBeTrue();
        removed.Residue.ShouldBeNull();
        Directory.Exists(mirror).ShouldBeFalse();
    }

    [Test]
    public async Task Remove_refuses_a_path_outside_the_worktree_root()
    {
        using var scratch = Scratch.Create();
        var service = new RunnerWorkspaceService(scratch.Clone, scratch.Work);

        foreach (var path in new[] { scratch.Clone, scratch.Work, scratch.Work + "/worktrees", "/etc", "" })
        {
            await Should.ThrowAsync<PhoneHomeAdmissionException>(
                () => service.RemoveAsync(new PhoneHomeWorkspaceRemoveRequest(path), CancellationToken.None));
        }
    }

    [Test]
    public async Task Spill_write_stays_inside_runner_cwd()
    {
        using var scratch = Scratch.Create();
        var service = new RunnerWorkspaceService(scratch.Clone, scratch.Work);
        var mirror = (await service.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, "task-deadbeef"),
            CancellationToken.None)).Path;

        await service.WriteSpillAsync(
            mirror, new PhoneHomeInputSpill(".antiphon/inbox/brief.md", "the whole brief"), CancellationToken.None);
        (await File.ReadAllTextAsync(Path.Combine(mirror, ".antiphon", "inbox", "brief.md")))
            .ShouldBe("the whole brief");

        foreach (var relative in new[] { "../escape.md", "/etc/passwd", "a/../../b.md", "", "./", ".." })
        {
            await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.WriteSpillAsync(
                mirror, new PhoneHomeInputSpill(relative, "x"), CancellationToken.None));
        }

        // A cwd that is not this runner's workspace is refused outright.
        await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.WriteSpillAsync(
            "/tmp", new PhoneHomeInputSpill("brief.md", "x"), CancellationToken.None));
    }

    // ---- CARD-0604 D-19 (Cut B): the verification snapshot, not the mirror -------------------
    //
    // The distinction matters: a mirror is a disposable copy of a branch that also exists on
    // origin, so forcing its removal costs nothing. A verification snapshot is a tracked
    // execution's only workspace and its removal is what a custody receipt is exchanged for, so
    // every surprise here is residue the operator sees rather than something to delete.

    [Test]
    public async Task Verification_create_refuses_sha_not_on_origin_master()
    {
        using var scratch = Scratch.Create();
        var service = Verification(scratch);

        // The task branch's tip is a real commit in this repository -- and it is NOT published.
        // A Mutation must run on what was actually landed, never on a branch tip that merely
        // exists somewhere on the remote.
        var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.CreateVerificationAsync(
            new PhoneHomeVerificationCreateRequest("task-1234abcd", scratch.Sha, "feat/card-task-1234abcd"),
            CancellationToken.None));

        refused.Code.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        refused.Message.ShouldContain("not reachable");
        Directory.Exists(Path.Combine(scratch.Work, "worktrees", "task-1234abcd")).ShouldBeFalse();
    }

    [Test]
    public async Task Verification_create_writes_schema_2_metadata()
    {
        using var scratch = Scratch.Create();
        var service = Verification(scratch);

        var created = await service.CreateVerificationAsync(
            new PhoneHomeVerificationCreateRequest("task-1234abcd", scratch.PublishedSha, "feat/card-task-1234abcd"),
            CancellationToken.None);

        created.InitialSha.ShouldBe(scratch.PublishedSha);
        created.Coordinates.Branch.ShouldBe("feat/card-task-1234abcd");
        created.Coordinates.CreationId.ShouldNotBe(Guid.Empty);
        created.Coordinates.WorktreePath.ShouldBe(scratch.Work + "/worktrees/task-1234abcd");
        Scratch.Git(created.Coordinates.WorktreePath, "rev-parse", "HEAD").Trim().ShouldBe(scratch.PublishedSha);

        // Schema 2 with CreationComplete and a GitDirectory, exactly as the desktop manager
        // writes it -- the equality checks on both sides read the same fields.
        using var metadata = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(scratch.Work, "verification-creations", "task-1234abcd.json")));
        metadata.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(2);
        metadata.RootElement.GetProperty("creationComplete").GetBoolean().ShouldBeTrue();
        metadata.RootElement.GetProperty("gitDirectory").GetString().ShouldBe(created.Coordinates.WorktreeGitDirectory);

        // And it validates against itself.
        var validation = await service.ValidateVerificationAsync(
            new PhoneHomeVerificationValidateRequest(created.Coordinates, scratch.PublishedSha), CancellationToken.None);
        validation.Valid.ShouldBeTrue(validation.Reason ?? "");

        // An exact redispatch is idempotent; a different sha for the same identifier is not.
        (await service.CreateVerificationAsync(
                new PhoneHomeVerificationCreateRequest("task-1234abcd", scratch.PublishedSha, "feat/card-task-1234abcd"),
                CancellationToken.None))
            .Coordinates.ShouldBe(created.Coordinates);
        await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.CreateVerificationAsync(
            new PhoneHomeVerificationCreateRequest("task-1234abcd", scratch.Sha, "feat/card-task-1234abcd"),
            CancellationToken.None));
    }

    [Test]
    public async Task Verification_remove_refuses_unknown_files()
    {
        using var scratch = Scratch.Create();
        var service = Verification(scratch);
        var created = await service.CreateVerificationAsync(
            new PhoneHomeVerificationCreateRequest("task-1234abcd", scratch.PublishedSha, "feat/card-task-1234abcd"),
            CancellationToken.None);

        File.WriteAllText(created.Coordinates.WorktreePath + "/report.md", "expected output");
        File.WriteAllText(created.Coordinates.WorktreePath + "/surprise.bin", "nobody chose to keep this");

        var refused = await service.RemoveVerificationAsync(
            new PhoneHomeVerificationRemoveRequest(created.Coordinates, scratch.PublishedSha, ["report.md"]),
            CancellationToken.None);

        refused.DirectoryGone.ShouldBeFalse();
        refused.Residue.ShouldNotBeNull();
        refused.Residue.ShouldContain("surprise.bin");
        Directory.Exists(created.Coordinates.WorktreePath).ShouldBeTrue();

        // A tracked change refuses too: that work was never pushed anywhere.
        File.Delete(created.Coordinates.WorktreePath + "/surprise.bin");
        File.WriteAllText(created.Coordinates.WorktreePath + "/README.md", "edited");
        var dirty = await service.RemoveVerificationAsync(
            new PhoneHomeVerificationRemoveRequest(created.Coordinates, scratch.PublishedSha, ["report.md"]),
            CancellationToken.None);
        dirty.Residue.ShouldNotBeNull();
        dirty.Residue.ShouldContain("tracked changes present");

        // With only the exact expected outputs present, the removal proceeds and takes the
        // branch and the creation metadata with it.
        Scratch.Git(created.Coordinates.WorktreePath, "checkout", "--", "README.md");
        var removed = await service.RemoveVerificationAsync(
            new PhoneHomeVerificationRemoveRequest(created.Coordinates, scratch.PublishedSha, ["report.md"]),
            CancellationToken.None);
        removed.DirectoryGone.ShouldBeTrue();
        removed.Unregistered.ShouldBeTrue();
        removed.BranchDeleted.ShouldBeTrue();
        removed.Residue.ShouldBeNull();
        File.Exists(Path.Combine(scratch.Work, "verification-creations", "task-1234abcd.json")).ShouldBeFalse();
    }

    [Test]
    public async Task Verification_reads_the_restoration_only_under_the_runner_repository()
    {
        using var scratch = Scratch.Create();
        var service = Verification(scratch);
        var common = Path.Combine(scratch.Clone, ".git").Replace('\\', '/');
        var operationId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var root = Path.Combine(scratch.Clone, ".git", "antiphon", "verification",
            operationId.ToString("N"), taskId.ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "restoration.json"), "{\"schemaVersion\":1}");

        var response = await service.ReadVerificationRestorationAsync(
            new PhoneHomeVerificationReadRestorationRequest(common, operationId, taskId), CancellationToken.None);
        System.Text.Encoding.UTF8.GetString(response.Restoration!).ShouldContain("schemaVersion");

        // An evidence root outside this runner's repository is refused rather than read: the
        // record must be where the producer was required to write it.
        await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.ReadVerificationRestorationAsync(
            new PhoneHomeVerificationReadRestorationRequest("/etc/antiphon", operationId, taskId),
            CancellationToken.None));
    }

    private static RunnerWorkspaceService Verification(Scratch scratch) =>
        new(scratch.Clone, scratch.Work, publishedBranch: Scratch.Published);

    private sealed class Scratch : IDisposable
    {
        public const string Branch = "feat/card-task-deadbeef";

        /// <summary>The scratch repository's stand-in for master: what "published" means here.</summary>
        public const string Published = "main";

        public required string Root { get; init; }
        public required string Origin { get; init; }
        public required string Clone { get; init; }
        public required string Work { get; init; }
        public required string Sha { get; init; }

        /// <summary>Tip of the published branch. Reachable from origin/main; Sha is not.</summary>
        public required string PublishedSha { get; init; }

        public static Scratch Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "c604-ws-" + Guid.NewGuid().ToString("N"));
            var origin = Path.Combine(root, "origin");
            var clone = Path.Combine(root, "repo");
            var work = Path.Combine(root, "work");
            Directory.CreateDirectory(origin);
            Directory.CreateDirectory(work);

            Git(origin, "init", "--initial-branch=main");
            Git(origin, "config", "user.email", "c604@localhost");
            Git(origin, "config", "user.name", "c604");
            File.WriteAllText(Path.Combine(origin, "README.md"), "c604");
            Git(origin, "add", "README.md");
            Git(origin, "commit", "-m", "base");
            Git(origin, "checkout", "-b", Branch);
            File.WriteAllText(Path.Combine(origin, "task.txt"), "task work");
            Git(origin, "add", "task.txt");
            Git(origin, "commit", "-m", "task");
            var sha = Git(origin, "rev-parse", "HEAD").Trim();
            Git(origin, "checkout", Published);
            File.WriteAllText(Path.Combine(origin, "published.txt"), "landed");
            Git(origin, "add", "published.txt");
            Git(origin, "commit", "-m", "land");
            var publishedSha = Git(origin, "rev-parse", "HEAD").Trim();

            Git(root, "clone", origin, clone);
            Git(clone, "config", "user.email", "c604@localhost");
            Git(clone, "config", "user.name", "c604");

            return new Scratch
            {
                Root = root, Origin = origin, Clone = clone, Work = work.Replace('\\', '/'),
                Sha = sha, PublishedSha = publishedSha,
            };
        }

        public void Dispose()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A scratch directory left behind in TEMP is noise, never a failed assertion.
            }
        }

        public static string Git(string workingDirectory, params string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
            return stdout;
        }
    }
}
