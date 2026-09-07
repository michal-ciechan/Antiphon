using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
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
public class CardFileIgnoreAcceptanceTests
{
    private static CardFileRepository Repository() => new(new GitProcessGate(), Options.Create(new GitSettings()), NullLogger<CardFileRepository>.Instance);

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Failed_ignore_install_preserves_created_project_and_original_ignore_bytes(bool denied)
    {
        await using var world = new CardFilePrivacyWorld(); await world.Repo.CommitFileAsync("seed", "seed");
        var file = Path.Combine(world.Repo.Path, ".gitignore");
        var original = denied ? "# original\ncustom/\n" : "# BEGIN ANTIPHON CARD FILES\n# malformed\n";
        await File.WriteAllTextAsync(file, original);
        var repository = new CardFileTestRepository { BeforeInstall = denied ? () => throw new UnauthorizedAccessException("synthetic denial") : null };
        await using var db = world.Db();
        var service = new ProjectService(db, null!, Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, cardFiles: world.Service(db, repository: repository));
        var result = await service.CreateAsync(new("c408-install", "", null, false, false, world.Repo.Path, "master"), default);
        result.CardFileWarnings.ShouldContain("card_files_ignore_missing");
        (await db.Projects.AnyAsync(p => p.Id == result.Id)).ShouldBeTrue();
        (await File.ReadAllTextAsync(file)).ShouldBe(original); Directory.Exists(world.DirectoryPath).ShouldBeFalse();
        await db.Projects.Where(p => p.Id == result.Id).ExecuteDeleteAsync();
    }

    [Test]
    public async Task Same_target_other_project_opt_in_preserves_exceptions_and_warnings()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync(); await world.AddCardAsync();
        await using var db = world.Db();
        var other = new Project { Id = Guid.NewGuid(), Name = "c408-other", LocalRepositoryPath = world.Repo.Path, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Projects.Add(other); await db.SaveChangesAsync();
        try {
            var before = await File.ReadAllBytesAsync(Path.Combine(world.Repo.Path, ".gitignore"));
            (await world.Service(db).ProjectWarningsAsync(other.Id, true, default)).ShouldNotContain("card_files_ignore_missing");
            (await File.ReadAllBytesAsync(Path.Combine(world.Repo.Path, ".gitignore"))).ShouldBe(before);
            (await world.SyncAsync()).Written.ShouldBe(2);
        }
        finally { await db.Projects.Where(p => p.Id == other.Id).ExecuteDeleteAsync(); }
    }

    [Test]
    public async Task Path_assignment_with_existing_opt_in_does_not_append_blanket_ignore()
    {
        await using var world = new CardFilePrivacyWorld(); await world.InitializeAsync();
        using var target = new ScratchGitRepo("c408-target"); await target.CommitFileAsync("seed", "seed");
        await using var db = world.Db();
        var service = new ProjectService(db, null!, Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, cardFiles: world.Service(db));
        var result = await service.UpdateAsync(world.ProjectId, new("c408-target", "", null, false, false, target.Path, "master"), default);
        result.RepositoryVisibility.ShouldBe(RepositoryVisibility.Unknown);
        result.CardFileWarnings.ShouldContain("card_files_ignore_missing"); File.Exists(Path.Combine(target.Path, ".gitignore")).ShouldBeFalse();
        (await db.Boards.AsNoTracking().SingleAsync(b => b.Id == world.BoardId)).SyncCardFiles.ShouldBeTrue();
    }

    [Test]
    [Arguments("global", false)] [Arguments("global", true)]
    [Arguments("info", false)] [Arguments("info", true)]
    public async Task Effective_external_ignore_protects_root_and_subdirectory_without_rewriting_user_config(string source, bool nested)
    {
        using var repo = new ScratchGitRepo("c408-ignore-\u96ea [root]"); await repo.CommitFileAsync("seed", "seed");
        var root = nested ? Directory.CreateDirectory(Path.Combine(repo.Path, "component")).FullName : repo.Path;
        var file = source == "info" ? Path.Combine(repo.Path, ".git/info/exclude") : Path.Combine(repo.Path, "synthetic-global-ignore");
        var original = "# user owned\n**/docs/cards/\n";
        await File.WriteAllTextAsync(file, original);
        if (source == "global") await repo.GitAsync("config", "core.excludesFile", file);
        (await Repository().HasIgnoreProtectionAsync(root, [], default)).ShouldBeTrue();
        (await Repository().IsIgnoredAsync(root, ["docs/cards/board/card.md"], default)).ShouldBeTrue();
        await Repository().InstallIgnoreAsync(root, default);
        File.Exists(Path.Combine(root, ".gitignore")).ShouldBeFalse();
        (await File.ReadAllTextAsync(file)).ShouldBe(original);
        Directory.Exists(Path.Combine(root, "docs/cards")).ShouldBeFalse();
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Local_ignore_install_preserves_no_BOM_and_trailing_newline_variants(bool trailingNewline)
    {
        using var repo = new ScratchGitRepo("c408-ignore"); await repo.CommitFileAsync("seed", "seed");
        var file = Path.Combine(repo.Path, ".gitignore");
        var original = System.Text.Encoding.UTF8.GetBytes("# original\ncustom/"+(trailingNewline ? "\n" : ""));
        await File.WriteAllBytesAsync(file, original); await Repository().InstallIgnoreAsync(repo.Path, default);
        var after = await File.ReadAllBytesAsync(file); after[..original.Length].ShouldBe(original);
        after.Take(3).ShouldNotBe(new byte[] { 239, 187, 191 });
        await Repository().InstallIgnoreAsync(repo.Path, default); (await File.ReadAllBytesAsync(file)).ShouldBe(after);
    }

    [Test]
    public async Task Subdirectory_install_is_project_local_and_does_not_change_repository_root_ignore()
    {
        using var repo = new ScratchGitRepo("c408-ignore-sub"); await repo.CommitFileAsync("seed", "seed");
        var root = Directory.CreateDirectory(Path.Combine(repo.Path, "component")).FullName;
        await Repository().InstallIgnoreAsync(root, default);
        File.Exists(Path.Combine(repo.Path, ".gitignore")).ShouldBeFalse();
        (await Repository().HasIgnoreProtectionAsync(root, [], default)).ShouldBeTrue();
        (await Repository().IsIgnoredAsync(root, ["docs/cards/board/card.md"], default)).ShouldBeTrue();
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
    }
}
