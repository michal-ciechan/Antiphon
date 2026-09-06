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
