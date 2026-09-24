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
[ParallelLimiter<ProcessSpawnLimit>]
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

        // G-27. The branch tip is whatever the desktop pushed; a mirror at some OTHER commit would
        // silently run the session against source the desktop never produced. CARD-0631 R-2: the
        // other commit is a REAL one the repository holds (the published tip), so only the
        // fetched-tip equality check stands between it and a worktree -- an unknown sha would be
        // refused later by worktree add and hide that check's absence. Run against both an
        // existing checkout and one the service has to clone first.
        var existing = new RunnerWorkspaceService(scratch.Clone, scratch.Work);
        var absent = scratch.Service(Path.Combine(scratch.Root, "absent-repo"));
        foreach (var service in new[] { existing, absent })
        {
            foreach (var sha in new[] { scratch.PublishedSha, new string('b', 40) })
            {
                var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.MirrorAsync(
                    new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, sha, "task-deadbeef"),
                    CancellationToken.None));
                refused.Code.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
                refused.Message.ShouldContain("Fetched tip");
                Directory.Exists(Path.Combine(scratch.Work, "worktrees", "task-deadbeef")).ShouldBeFalse();
            }
        }
    }

    // ---- CARD-0631 D-6/D-7/D-8: the runner provisions its own repository --------------------

    [Test]
    public async Task Mirror_clones_repository_when_absent()
    {
        using var scratch = Scratch.Create();
        var request = new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, "task-deadbeef");

        // A fresh volume: nothing at the runner repository path at all.
        var repository = Path.Combine(scratch.Root, "runner", "repos", "antiphon");
        var starts = new List<ProcessStartInfo>();
        var service = scratch.Service(repository, starts);

        var mirror = (await service.MirrorAsync(request, CancellationToken.None)).Path;

        Directory.Exists(Path.Combine(repository, ".git")).ShouldBeTrue();
        Scratch.Git(repository, "remote", "get-url", "origin").Trim().ShouldBe(scratch.Origin);
        Scratch.Git(mirror, "rev-parse", "HEAD").Trim().ShouldBe(scratch.Sha);
        Scratch.Git(mirror, "rev-parse", "--abbrev-ref", "HEAD").Trim().ShouldBe(Scratch.Branch);
        File.ReadAllText(Path.Combine(mirror, "task.txt")).ShouldBe("task work");

        // Pinned argv: a blobless, no-checkout clone of the runner-owned source into the repository
        // path, from its parent, with no interactive prompt possible.
        var clone = starts.Single(IsClone);
        clone.ArgumentList.ToArray().ShouldBe(
            new[] { "clone", "--filter=blob:none", "--no-checkout", scratch.Origin, repository });
        clone.WorkingDirectory.ShouldBe(Path.GetDirectoryName(repository));
        clone.Environment["GIT_TERMINAL_PROMPT"].ShouldBe("0");

        // A same-sha replay is the existing mirror; a second mirror reuses the repository. Neither
        // clones again. The second is another task branch, as every mirror is: git never checks one
        // branch out twice, and the clone's own HEAD still holds its default branch.
        (await service.MirrorAsync(request, CancellationToken.None)).Path.ShouldBe(mirror);
        Scratch.Git(scratch.Origin, "branch", "feat/card-task-cafef00d", scratch.PublishedSha);
        var second = (await service.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest("feat/card-task-cafef00d", scratch.PublishedSha, "task-cafef00d"),
            CancellationToken.None)).Path;
        Scratch.Git(second, "rev-parse", "HEAD").Trim().ShouldBe(scratch.PublishedSha);
        starts.Count(IsClone).ShouldBe(1);

        // An EMPTY destination directory is a valid clone target too.
        var empty = Path.Combine(scratch.Root, "empty-repo");
        Directory.CreateDirectory(empty);
        var emptyStarts = new List<ProcessStartInfo>();
        var fromEmpty = await scratch.Service(empty, emptyStarts, work: "work-empty")
            .MirrorAsync(request, CancellationToken.None);
        Scratch.Git(fromEmpty.Path, "rev-parse", "HEAD").Trim().ShouldBe(scratch.Sha);
        emptyStarts.Count(IsClone).ShouldBe(1);

        // A populated directory without .git is someone else's data: refused, never overwritten,
        // deleted or reset, and no git process runs in it.
        var populated = Path.Combine(scratch.Root, "populated-repo");
        Directory.CreateDirectory(populated);
        File.WriteAllText(Path.Combine(populated, "sentinel.txt"), "keep me");
        var populatedStarts = new List<ProcessStartInfo>();
        var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => scratch
            .Service(populated, populatedStarts, work: "work-populated").MirrorAsync(request, CancellationToken.None));
        refused.Code.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        refused.StatusCode.ShouldBe(409);
        File.ReadAllText(Path.Combine(populated, "sentinel.txt")).ShouldBe("keep me");
        Directory.EnumerateFileSystemEntries(populated).Count().ShouldBe(1);
        populatedStarts.ShouldBeEmpty();
        Directory.Exists(Path.Combine(scratch.Root, "work-populated", "worktrees", "task-deadbeef")).ShouldBeFalse();

        // An unreachable clone source is a 409 admission, never a successful mirror.
        var unavailableStarts = new List<ProcessStartInfo>();
        var unavailable = scratch.Service(Path.Combine(scratch.Root, "unavailable-repo"), unavailableStarts,
            work: "work-unavailable", cloneSource: Path.Combine(scratch.Root, "no-such-origin"));
        var failed = await Should.ThrowAsync<PhoneHomeAdmissionException>(
            () => unavailable.MirrorAsync(request, CancellationToken.None));
        failed.Code.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
        failed.StatusCode.ShouldBe(409);
        failed.Message.ShouldContain("clone");
        unavailableStarts.Count(IsClone).ShouldBe(1);
        Directory.Exists(Path.Combine(scratch.Root, "work-unavailable", "worktrees", "task-deadbeef")).ShouldBeFalse();
    }

    [Test]
    public async Task Mirror_failure_is_admission_error_not_crash()
    {
        using var scratch = Scratch.Create();
        var request = new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, "task-deadbeef");
        var mirrorPath = Path.Combine(scratch.Work, "worktrees", "task-deadbeef");

        // A git that cannot start -- missing binary, unreadable cwd, access denial, or no process
        // at all -- is a named workspace admission the server can answer, not an escaped fault.
        var faults = new Func<Exception?>[]
        {
            () => new System.ComponentModel.Win32Exception(2, "No such file or directory"),
            () => new IOException("disk gone"),
            () => new UnauthorizedAccessException("denied"),
            () => null,
        };
        foreach (var fault in faults)
        {
            foreach (var repository in new[] { scratch.Clone, Path.Combine(scratch.Root, "absent-repo") })
            {
                var service = new RunnerWorkspaceService(repository, scratch.Work, scratch.Origin,
                    _ =>
                    {
                        if (fault() is { } ex)
                            throw ex;
                        return null;
                    });
                var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(
                    () => service.MirrorAsync(request, CancellationToken.None));
                refused.Code.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget);
                refused.StatusCode.ShouldBe(409);
                refused.Message.ShouldContain(repository == scratch.Clone ? "git fetch" : "git clone");
                if (fault() is { } expected)
                    refused.Message.ShouldContain(expected.GetType().Name);
                Directory.Exists(mirrorPath).ShouldBeFalse();
            }
        }

        // Validation precedes every filesystem and process effect: an invalid request against an
        // absent repository starts nothing and creates nothing.
        var absent = Path.Combine(scratch.Root, "never-created", "repo");
        var invalidStarts = 0;
        var strict = new RunnerWorkspaceService(absent, scratch.Work, scratch.Origin,
            psi => { invalidStarts++; return Process.Start(psi); });
        foreach (var invalid in new[]
                 {
                     new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, "../escape"),
                     new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, "HEAD", "task-deadbeef"),
                     new PhoneHomeWorkspaceMirrorRequest("bad..branch", scratch.Sha, "task-deadbeef"),
                 })
        {
            await Should.ThrowAsync<PhoneHomeAdmissionException>(() => strict.MirrorAsync(invalid, CancellationToken.None));
        }
        invalidStarts.ShouldBe(0);
        Directory.Exists(Path.Combine(scratch.Root, "never-created")).ShouldBeFalse();

        // Caller cancellation stays cancellation, and the operation's own git child is killed and
        // reaped before MirrorAsync returns. The held child is a real git reading a stdin that is
        // never closed, so only the kill can end it. The fixture keeps its own handle and reaps
        // the child even when an assertion fails.
        Process? observer = null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new RunnerWorkspaceService(scratch.Clone, scratch.Work, scratch.Origin, psi =>
        {
            var hold = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = psi.WorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            hold.ArgumentList.Add("hash-object");
            hold.ArgumentList.Add("--stdin");
            var child = Process.Start(hold)!;
            observer = Process.GetProcessById(child.Id);
            started.TrySetResult();
            return child;
        });
        try
        {
            using var cts = new CancellationTokenSource();
            var mirror = held.MirrorAsync(request, cts.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            cts.Cancel();
            Exception? thrown = null;
            try { await mirror.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception ex) { thrown = ex; }
            thrown.ShouldBeAssignableTo<OperationCanceledException>();
            observer!.HasExited.ShouldBeTrue();
            Directory.Exists(mirrorPath).ShouldBeFalse();
        }
        finally
        {
            if (observer is not null)
            {
                try { if (!observer.HasExited) observer.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* already gone */ }
                observer.WaitForExit();
                observer.Dispose();
            }
        }
    }

    private static bool IsClone(ProcessStartInfo psi) => psi.ArgumentList.FirstOrDefault() == "clone";

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

    [Test]
    public async Task Spill_write_refuses_a_symlink_that_escapes_the_mirror()
    {
        using var scratch = Scratch.Create();
        var service = new RunnerWorkspaceService(scratch.Clone, scratch.Work);
        var mirror = (await service.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, "task-deadbeef"),
            CancellationToken.None)).Path;
        var outside = Path.Combine(Path.GetTempPath(), "c647-escape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var link = Path.Combine(mirror, "escape");
        try
        {
            CreateDirectoryLink(link, outside);
            var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.WriteSpillAsync(
                mirror, new PhoneHomeInputSpill("escape/inbox/note.md", "secret"), CancellationToken.None));
            refused.Message.ShouldContain("escapes the mirror");
            File.Exists(Path.Combine(outside, "inbox", "note.md")).ShouldBeFalse();
            Directory.Exists(Path.Combine(outside, "inbox")).ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
        }

        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = commandInterpreter,
            Arguments = $"/d /c mklink /J \"{linkPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start mklink for the junction test.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new IOException($"Could not create the test junction (exit code {process.ExitCode}).");
    }

    [Test]
    public async Task Spill_write_refuses_a_dangling_final_file_symlink()
    {
        using var scratch = Scratch.Create();
        var service = new RunnerWorkspaceService(scratch.Clone, scratch.Work);
        var mirror = (await service.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, "task-deadbeef"),
            CancellationToken.None)).Path;
        var outside = Path.Combine(Path.GetTempPath(), "c647-dangling-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var inbox = Path.Combine(mirror, ".antiphon", "inbox");
        Directory.CreateDirectory(inbox);
        var link = Path.Combine(inbox, "note.md");
        var target = Path.Combine(outside, "secret.md");
        try
        {
            if (!TryCreateDanglingSymlink(link, target, directory: false))
            {
                Skip.Test(
                    "Windows denied symlink creation. This dangling final-file refusal runs on Linux, where CreateSymbolicLink does not need that privilege.");
                return;
            }

            var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.WriteSpillAsync(
                mirror, new PhoneHomeInputSpill(".antiphon/inbox/note.md", "secret"), CancellationToken.None));
            refused.Message.ShouldContain("escapes the mirror");
            File.Exists(target).ShouldBeFalse();
        }
        finally
        {
            TryDeleteLink(link);
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    [Test]
    public async Task Spill_write_refuses_a_dangling_directory_symlink()
    {
        using var scratch = Scratch.Create();
        var service = new RunnerWorkspaceService(scratch.Clone, scratch.Work);
        var mirror = (await service.MirrorAsync(
            new PhoneHomeWorkspaceMirrorRequest(Scratch.Branch, scratch.Sha, "task-deadbeef"),
            CancellationToken.None)).Path;
        var outside = Path.Combine(Path.GetTempPath(), "c647-dangling-dir-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(mirror, "escape");
        try
        {
            if (!TryCreateDanglingSymlink(link, outside, directory: true))
            {
                Skip.Test(
                    "Windows denied symlink creation. This dangling directory refusal runs on Linux, where CreateSymbolicLink does not need that privilege.");
                return;
            }

            var refused = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => service.WriteSpillAsync(
                mirror, new PhoneHomeInputSpill("escape/inbox/note.md", "secret"), CancellationToken.None));
            refused.Message.ShouldContain("escapes the mirror");
            Directory.Exists(outside).ShouldBeFalse();
            File.Exists(Path.Combine(outside, "inbox", "note.md")).ShouldBeFalse();
        }
        finally
        {
            TryDeleteLink(link);
            if (Directory.Exists(outside))
                Directory.Delete(outside, recursive: true);
        }
    }

    /// <summary>
    /// A dangling link is the case <see cref="File.Exists"/> misses. Junctions cannot dangle, so
    /// there is no Windows fallback: without the privilege the test skips and Linux runs it.
    /// </summary>
    private static bool TryCreateDanglingSymlink(string linkPath, string targetPath, bool directory)
    {
        try
        {
            if (directory)
                Directory.CreateSymbolicLink(linkPath, targetPath);
            else
                File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (OperatingSystem.IsWindows()
            && ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDeleteLink(string linkPath)
    {
        try
        {
            if (new FileInfo(linkPath).LinkTarget is not null || new DirectoryInfo(linkPath).LinkTarget is not null)
                File.Delete(linkPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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

        /// <summary>
        /// CARD-0631: a service whose clone source is this scratch origin (or a named stand-in) and
        /// whose git processes see neither the user's nor the system's git configuration, so no
        /// credential helper or URL rewrite reaches outside the scratch root. Every start is recorded.
        /// </summary>
        public RunnerWorkspaceService Service(string repository, List<ProcessStartInfo>? starts = null,
            string work = "work", string? cloneSource = null)
        {
            var emptyConfig = Path.Combine(Root, "empty.gitconfig");
            if (!File.Exists(emptyConfig))
                File.WriteAllText(emptyConfig, "");
            var workPath = Path.Combine(Root, work).Replace('\\', '/');
            Directory.CreateDirectory(workPath);
            return new RunnerWorkspaceService(repository, workPath, cloneSource ?? Origin, psi =>
            {
                psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
                psi.Environment["GIT_CONFIG_GLOBAL"] = emptyConfig;
                starts?.Add(psi);
                return Process.Start(psi);
            });
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
