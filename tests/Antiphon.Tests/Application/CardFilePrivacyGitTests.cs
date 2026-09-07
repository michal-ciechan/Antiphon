using Microsoft.EntityFrameworkCore;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFilePrivacyGitTests
{
    private static CardFileRepository Repository() => new(new GitProcessGate(),
        Options.Create(new GitSettings()), NullLogger<CardFileRepository>.Instance);

    [Test]
    public async Task Managed_file_symlink_refuses_sync_and_repository_IO_before_mutation()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(); await world.AddCardAsync();
        Directory.CreateDirectory(world.DirectoryPath);
        var outside = Path.Combine(world.Repo.WorktreeRoot, "outside.md");
        var link = Path.Combine(world.DirectoryPath, "legacy.md");
        await File.WriteAllTextAsync(outside, "C408_OUTSIDE_UNCHANGED");
        var head = await world.Repo.GitReadAsync("rev-parse", "HEAD");
        try
        {
            // Requires Windows file-symlink privilege. Failure to create the fixture is
            // an explicit environmental failure, never a passing junction substitute.
            File.CreateSymbolicLink(link, outside);
            (await Should.ThrowAsync<ConflictException>(() => world.SyncAsync())).Code.ShouldBe("unsafe_card_file_path");
            await using var db = world.Db();
            (await db.Boards.AsNoTracking().SingleAsync(b => b.Id == world.BoardId)).CardFilesDirectorySlug.ShouldBeNull();
            var repository = Repository();
            (await Should.ThrowAsync<ConflictException>(() => repository.ReadAsync(world.Repo.Path, link, default))).Code.ShouldBe("unsafe_card_file_path");
            (await Should.ThrowAsync<ConflictException>(() => repository.WriteAsync(world.Repo.Path, link, "public", world.BoardId, default))).Code.ShouldBe("unsafe_card_file_path");
            Should.Throw<ConflictException>(() => repository.Delete(world.Repo.Path, link)).Code.ShouldBe("unsafe_card_file_path");
            (await File.ReadAllTextAsync(outside)).ShouldBe("C408_OUTSIDE_UNCHANGED");
            (await world.Repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        }
        finally { if (File.Exists(link)) File.Delete(link); }
    }

    [Test]
    public async Task Unmerged_managed_path_has_named_guard_and_preserves_conflict_index()
    {
        using var repo = new ScratchGitRepo("c408");
        const string path = "docs/cards/board/card.md";
        Directory.CreateDirectory(Path.Combine(repo.Path, "docs/cards/board"));
        await repo.CommitFileAsync(path, "base\n");
        var branch = (await repo.GitReadAsync("symbolic-ref", "--short", "HEAD")).Trim();
        await repo.GitAsync("checkout", "-b", "c408-side");
        await repo.CommitFileAsync(path, "side\n");
        await repo.GitAsync("checkout", branch);
        await repo.CommitFileAsync(path, "main\n");
        (await ScratchGitRepo.GitInAsync(repo.Path, "merge", "c408-side")).Ok.ShouldBeFalse();
        var mergeHead = (await repo.GitReadAsync("rev-parse", "--git-path", "MERGE_HEAD")).Trim();
        File.Delete(Path.IsPathRooted(mergeHead) ? mergeHead : Path.Combine(repo.Path, mergeHead));
        var index = await repo.GitReadAsync("ls-files", "--stage", "-z");
        var body = await File.ReadAllTextAsync(Path.Combine(repo.Path, path));
        var result = await Repository().CommitAsync(repo.Path, Path.Combine(repo.Path, "docs/cards/board"),
            new Dictionary<string, string?> { [path] = body }, "sync", default);
        result.SkipReason.ShouldBe("conflicted_paths"); result.Sha.ShouldBeNull();
        (await repo.GitReadAsync("ls-files", "--stage", "-z")).ShouldBe(index);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, path), "C408_RESOLVED\n");
        await repo.GitAsync("add", "--", path);
        (await Repository().CommitAsync(repo.Path, Path.Combine(repo.Path, "docs/cards/board"),
            new Dictionary<string, string?> { [path] = "C408_RESOLVED\n" }, "sync", default)).Sha.ShouldNotBeNull();
    }

    [Test]
    [Arguments("root")]
    [Arguments("cards")]
    [Arguments("board")]
    public async Task Junction_ancestors_refuse_root_and_managed_file_aliases(string placement)
    {
        using var repo = new ScratchGitRepo("c408");
        var outside = Directory.CreateDirectory(Path.Combine(repo.Path, "outside")).FullName;
        var sentinel = Path.Combine(outside, "sentinel.md");
        await File.WriteAllTextAsync(sentinel, "C408_OUTSIDE_UNCHANGED");
        var link = Path.Combine(repo.Path, placement switch { "root" => "alias", "cards" => "docs/cards", _ => "docs/cards/board" });
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var start = new System.Diagnostics.ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("New-Item -ItemType Junction -Path $env:C408_LINK -Target $env:C408_TARGET -ErrorAction Stop | Out-Null");
        start.Environment["C408_LINK"] = link; start.Environment["C408_TARGET"] = outside;
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await stdout + await stderr);
        try
        {
            var root = placement == "root" ? link : repo.Path;
            Should.Throw<ConflictException>(() => Repository().ValidatePath(root, Path.Combine(link, "sentinel.md")))
                .Code.ShouldBe("unsafe_card_file_path");
            (await File.ReadAllTextAsync(sentinel)).ShouldBe("C408_OUTSIDE_UNCHANGED");
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    public async Task Subdirectory_project_commits_and_removes_its_relative_paths()
    {
        using var repo = new ScratchGitRepo("c408");
        await repo.CommitFileAsync("seed", "seed\n");
        var root = Directory.CreateDirectory(Path.Combine(repo.Path, "component")).FullName;
        var directory = Directory.CreateDirectory(Path.Combine(root, "docs/cards/board")).FullName;
        const string path = "docs/cards/board/card.md";
        await File.WriteAllTextAsync(Path.Combine(root, path), "C408_PUBLIC\n");
        var repository = Repository();
        var created = await repository.CommitAsync(root, directory, new Dictionary<string, string?> { [path] = "C408_PUBLIC\n" }, "sync", default);
        created.Sha.ShouldNotBeNull();
        (await repo.GitReadAsync("show", "HEAD:component/" + path)).ShouldBe("C408_PUBLIC\n");
        File.Delete(Path.Combine(root, path));
        var removed = await repository.CommitAsync(root, directory, new Dictionary<string, string?> { [path] = null }, "antiphon: remove unpublished card files", default);
        removed.Sha.ShouldNotBeNull();
        (await repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).ShouldNotContain(path);
    }

    [Test]
    [Arguments("rebase-merge", "rebase_in_progress")]
    [Arguments("rebase-apply", "rebase_in_progress")]
    [Arguments("MERGE_HEAD", "merge_in_progress")]
    [Arguments("CHERRY_PICK_HEAD", "cherry_pick_in_progress")]
    [Arguments("detached", "detached_head")]
    public async Task Git_operation_guards_keep_index_and_HEAD_unchanged(string marker, string reason)
    {
        using var repo = new ScratchGitRepo("c408");
        await repo.CommitFileAsync("seed", "seed\n");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var branch = (await repo.GitReadAsync("symbolic-ref", "--short", "HEAD")).Trim();
        var directory = Path.Combine(repo.Path, "docs/cards/board");
        Directory.CreateDirectory(directory);
        const string path = "docs/cards/board/card.md";
        await File.WriteAllTextAsync(Path.Combine(repo.Path, path), "C408_PUBLIC\n");
        if (marker == "detached") await repo.GitAsync("checkout", "--detach", "HEAD");
        else
        {
            var gitPath = (await repo.GitReadAsync("rev-parse", "--git-path", marker)).Trim();
            gitPath = Path.IsPathRooted(gitPath) ? gitPath : Path.Combine(repo.Path, gitPath);
            if (marker.StartsWith("rebase")) Directory.CreateDirectory(gitPath);
            else await File.WriteAllTextAsync(gitPath, head.Trim() + "\n");
        }
        var result = await Repository().CommitAsync(repo.Path, directory,
            new Dictionary<string, string?> { [path] = "C408_PUBLIC\n" }, "sync", default);
        result.SkipReason.ShouldBe(reason);
        result.Sha.ShouldBeNull();
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
        if (marker == "detached") await repo.GitAsync("checkout", branch);
        else {
            var owned = (await repo.GitReadAsync("rev-parse", "--git-path", marker)).Trim();
            owned = Path.IsPathRooted(owned) ? owned : Path.Combine(repo.Path, owned);
            if (marker.StartsWith("rebase")) Directory.Delete(owned); else File.Delete(owned);
        }
        var retry = await Repository().CommitAsync(repo.Path, directory,
            new Dictionary<string, string?> { [path] = "C408_PUBLIC\n" }, "sync", default);
        retry.Error.ShouldBeNull(); retry.Sha.ShouldNotBeNull();
    }

    [Test]
    public async Task Literal_NUL_pathspec_batch_exceeds_Windows_argument_limit_without_overmatching()
    {
        using var repo = new ScratchGitRepo("c408");
        await repo.CommitFileAsync("seed", "seed\n");
        var directory = Path.Combine(repo.Path, "docs/cards/board");
        Directory.CreateDirectory(directory);
        var expected = new Dictionary<string, string?>();
        for (var i = 0; i < 700; i++)
        {
            var path = $"docs/cards/board/CARD-{i:0000}-legacy[1]-sufficiently-long-path.md";
            expected[path] = "C408_PUBLIC\n";
            await File.WriteAllTextAsync(Path.Combine(repo.Path, path), expected[path]);
        }
        const string neighbor = "docs/cards/board/CARD-0000-legacy1-sufficiently-long-path.md";
        await File.WriteAllTextAsync(Path.Combine(repo.Path, neighbor), "C408_NEIGHBOR");
        expected.Keys.Sum(p => p.Length + 1).ShouldBeGreaterThan(32768);
        var result = await Repository().CommitAsync(repo.Path, directory, expected, "sync", default);
        result.Error.ShouldBeNull();
        result.Sha.ShouldNotBeNull();
        var paths = (await repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).Split('\n');
        paths.ShouldNotContain(neighbor);
        foreach (var path in expected.Keys) paths.ShouldContain(path);
    }

    [Test]
    public async Task AutoCommit_stages_and_commits_only_exact_generated_paths_never_directory()
    {
        using var repo = new ScratchGitRepo("c408");
        await repo.CommitFileAsync("seed", "seed\n");
        var excluded = new[] { "source.txt", ".gitignore", "docs/cards/board/notes.txt",
            "docs/cards/board/nested/keep.md", "docs/cards/other/keep.md", "docs/cards/board-other/keep.md" };
        foreach (var path in excluded)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Path, path))!);
            await File.WriteAllTextAsync(Path.Combine(repo.Path, path), "C408_EXCLUDED\n");
            await repo.GitAsync("add", "--", path);
        }
        var indexBefore = await repo.GitReadAsync("ls-files", "--stage", "-z");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "docs/cards/board/untracked.txt"), "C408_UNTRACKED");
        const string card = "docs/cards/board/CARD-0001-public.md";
        await File.WriteAllTextAsync(Path.Combine(repo.Path, card), "C408_PUBLIC_BODY\n");
        var result = await Repository().CommitAsync(repo.Path, Path.Combine(repo.Path, "docs/cards/board"),
            new Dictionary<string, string?> { [card] = "C408_PUBLIC_BODY\n" }, "antiphon: sync card files (board)", default);
        result.Error.ShouldBeNull();
        result.Sha.ShouldNotBeNull();
        (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim().ShouldBe(card);
        (await repo.GitReadAsync("diff", "--cached", "--name-only", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Order().ShouldBe(excluded.Order());
        var indexAfter = await repo.GitReadAsync("ls-files", "--stage", "-z");
        foreach (var entry in indexBefore.Split('\0', StringSplitOptions.RemoveEmptyEntries)) indexAfter.ShouldContain(entry);
        indexAfter.ShouldNotContain("untracked.txt");
        (await repo.GitReadAsync("show", "HEAD:" + card)).ShouldBe("C408_PUBLIC_BODY\n");
    }

    [Test]
    public async Task Index_only_private_addition_is_unstaged_without_nonexistent_commit_path()
    {
        using var repo = new ScratchGitRepo("c408");
        await repo.CommitFileAsync("seed", "seed\n");
        const string path = "docs/cards/board/legacy[1].md";
        var directory = Path.Combine(repo.Path, "docs/cards/board");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, path), "C408_PRIVATE_CARD_STAGED");
        await repo.GitAsync("--literal-pathspecs", "add", "--", path);
        File.Delete(Path.Combine(repo.Path, path));
        Directory.Delete(directory);
        var repository = Repository();
        (await repository.GetManagedGitPathsAsync(repo.Path, directory, default)).ShouldContain(path);
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var result = await repository.CommitAsync(repo.Path, directory,
            new Dictionary<string, string?> { [path] = null }, "antiphon: remove unpublished card files", default);
        result.SkipReason.ShouldBe("nothing_to_commit");
        result.Error.ShouldBeNull();
        (await repo.GitReadAsync("ls-files", "-z")).ShouldNotContain(path);
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
    }

    [Test]
    public async Task Changed_generated_bytes_before_commit_refuse_with_generated_files_changed()
    {
        using var repo = new ScratchGitRepo("c408");
        await repo.CommitFileAsync("seed", "seed\n");
        var directory = Path.Combine(repo.Path, "docs/cards/board");
        Directory.CreateDirectory(directory);
        const string path = "docs/cards/board/card.md";
        await File.WriteAllTextAsync(Path.Combine(repo.Path, path), "C408_CHANGED");
        var result = await Repository().CommitAsync(repo.Path, directory,
            new Dictionary<string, string?> { [path] = "C408_PUBLIC" }, "sync", default);
        result.SkipReason.ShouldBe("generated_files_changed");
        result.Sha.ShouldBeNull();
        (await repo.GitReadAsync("ls-files", "-z")).ShouldNotContain(path);
    }

    [Test]
    public async Task Tracked_stale_file_is_deleted_when_entire_working_directory_is_absent()
    {
        using var repo = new ScratchGitRepo("c408");
        await repo.CommitFileAsync("seed", "seed\n");
        var directory = Path.Combine(repo.Path, "docs/cards/board");
        Directory.CreateDirectory(directory);
        const string path = "docs/cards/board/card.md";
        await File.WriteAllTextAsync(Path.Combine(repo.Path, path), "C408_OLD_PUBLIC\n");
        var repository = Repository();
        var prior = await repository.CommitAsync(repo.Path, directory,
            new Dictionary<string, string?> { [path] = "C408_OLD_PUBLIC\n" }, "antiphon: sync card files (board)", default);
        prior.Sha.ShouldNotBeNull();
        File.Delete(Path.Combine(repo.Path, path));
        Directory.Delete(directory);
        var result = await repository.CommitAsync(repo.Path, directory,
            new Dictionary<string, string?> { [path] = null }, "antiphon: remove unpublished card files", default);
        result.Sha.ShouldNotBeNull();
        (await repo.GitReadAsync("ls-tree", "-r", "--name-only", "HEAD")).ShouldNotContain(path);
        (await repo.GitReadAsync("show", prior.Sha + ":" + path)).ShouldBe("C408_OLD_PUBLIC\n");
        (await repo.GitReadAsync("log", "-1", "--format=%s")).Trim().ShouldBe("antiphon: remove unpublished card files");
    }

    [Test]
    public void Containment_rejects_parent_and_prefix_sibling()
    {
        var root = Path.Combine(Path.GetTempPath(), "c408-root");
        Should.Throw<ConflictException>(() => Repository().ValidatePath(root, root + "-other/card.md"))
            .Code.ShouldBe("unsafe_card_file_path");
        Should.Throw<ConflictException>(() => Repository().ValidatePath(root, Path.Combine(root, "../card.md")))
            .Code.ShouldBe("unsafe_card_file_path");
    }
}
