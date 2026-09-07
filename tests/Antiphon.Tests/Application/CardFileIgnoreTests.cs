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
public class CardFileIgnoreTests
{
    private static CardFileRepository Repository() => new(new GitProcessGate(), Options.Create(new GitSettings()), NullLogger<CardFileRepository>.Instance);

    [Test]
    public async Task Fresh_project_create_installs_deny_default_without_publication()
    {
        await using var world = new CardFilePrivacyWorld();
        await world.InitializeAsync(false);
        using var repo = new ScratchGitRepo("c408-fresh");
        await repo.CommitFileAsync("seed", "seed\n");
        await using var db = world.Db();
        var service = new ProjectService(db, null!, Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, cardFiles: world.Service(db));
        var created = await service.CreateAsync(new("C408 fresh", "", null, false, false, repo.Path, "master"), default);
        try
        {
            created.RepositoryVisibility.ShouldBe(Antiphon.Server.Domain.Enums.RepositoryVisibility.Unknown);
            (await File.ReadAllTextAsync(Path.Combine(repo.Path, ".gitignore"))).Replace("\r", "").ShouldBe("# BEGIN ANTIPHON CARD FILES\n/docs/cards/\n# END ANTIPHON CARD FILES\n");
            created.CardFileWarnings.ShouldBeEmpty();
            Directory.Exists(Path.Combine(repo.Path, "docs/cards")).ShouldBeFalse();
            (await repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
        }
        finally { db.Projects.Remove((await db.Projects.FindAsync(created.Id))!); await db.SaveChangesAsync(); }
    }

    [Test]
    [Arguments("\n")]
    [Arguments("\r\n")]
    public async Task Existing_user_ignore_bytes_are_preserved_and_install_is_idempotent(string newline)
    {
        using var repo = new ScratchGitRepo("c408-ignore");
        await repo.CommitFileAsync("seed", "seed");
        var path = Path.Combine(repo.Path, ".gitignore");
        var original = new byte[] { 239, 187, 191 }.Concat(System.Text.Encoding.UTF8.GetBytes("# custom" + newline + "unrelated/")).ToArray();
        await File.WriteAllBytesAsync(path, original);
        await Repository().InstallIgnoreAsync(repo.Path, default);
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[..original.Length].ShouldBe(original);
        (await Repository().HasIgnoreProtectionAsync(repo.Path, [], default)).ShouldBeTrue();
        await Repository().InstallIgnoreAsync(repo.Path, default);
        (await File.ReadAllBytesAsync(path)).ShouldBe(bytes);
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
    }

    [Test]
    [Arguments("# BEGIN ANTIPHON CARD FILES\n/docs/cards/\n")]
    [Arguments("# BEGIN ANTIPHON CARD FILES\n/docs/cards/\n!oops\n# END ANTIPHON CARD FILES\n")]
    [Arguments("/docs/cards/*\n!/docs/cards/*/\n")]
    [Arguments("/docs/cards/*\n!/docs/cards/off/\n")]
    public async Task Malformed_or_broad_exceptions_refuse_protection(string ignore)
    {
        using var repo = new ScratchGitRepo("c408-ignore");
        await repo.CommitFileAsync("seed", "seed");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitignore"), ignore);
        (await Repository().HasIgnoreProtectionAsync(repo.Path, ["board"], default)).ShouldBeFalse();
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, ".gitignore"))).ShouldBe(ignore);
    }

    [Test]
    public async Task Effective_ignore_includes_tracked_files_and_named_exceptions_preserve_off_boards()
    {
        using var repo = new ScratchGitRepo("c408-ignore");
        await repo.CommitFileAsync("seed", "seed");
        Directory.CreateDirectory(Path.Combine(repo.Path, "docs/cards/board"));
        await repo.CommitFileAsync("docs/cards/board/card.md", "C408_PUBLIC");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitignore"), "/docs/cards/\n");
        (await Repository().IsIgnoredAsync(repo.Path, ["docs/cards/board/card.md"], default)).ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitignore"), "/docs/cards/*\n!/docs/cards/board/\n");
        (await Repository().HasIgnoreProtectionAsync(repo.Path, ["board"], default)).ShouldBeTrue();
        (await Repository().IsIgnoredAsync(repo.Path, ["docs/cards/board/card.md"], default)).ShouldBeFalse();
        (await Repository().IsIgnoredAsync(repo.Path, ["docs/cards/off/card.md"], default)).ShouldBeTrue();
    }
}
