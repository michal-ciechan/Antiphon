using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;
using static Antiphon.Server.Application.Services.GitWorkspaceService;

namespace Antiphon.Tests.Application;

// CARD-0547 D-6: the shared per-SHA trailer reader used by recovery and the Commit-child audit.
public sealed partial class GatedCommitServiceTests
{
    [Test]
    [Arguments(":")]
    [Arguments("=")]
    [Arguments("%")]
    public async Task C547_ReadTrailersAsync_parses_each_configured_separator(string separator)
    {
        using var repo = await SeedAsync();
        await repo.GitAsync("config", "trailer.separators", separator);
        await repo.GitAsync("commit", "--allow-empty", "-m",
            $"subject\n\nantiphon{separator} true\nantiphon-commit{separator} gated");
        (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain($"antiphon-commit{separator} gated");
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var (_, spy) = Gate();

        var read = await spy.ReadTrailersAsync(repo.Path, head, CancellationToken.None);

        read.Succeeded.ShouldBeTrue();
        read.Items.ShouldBe(new[] { new GitTrailer("antiphon", "true"), new GitTrailer("antiphon-commit", "gated") });
        HasExactTrailer(read.Items, "antiphon-commit", "gated").ShouldBeTrue();
        HasExactTrailer(read.Items, "Antiphon-Commit", "gated").ShouldBeTrue("the key is case-insensitive");
        HasExactTrailer(read.Items, "antiphon-commit", "Gated").ShouldBeFalse("the value is ordinal");
    }

    [Test]
    public async Task C547_ReadTrailersAsync_empty_block_is_success_with_no_items()
    {
        using var repo = await SeedAsync();
        await repo.GitAsync("commit", "--allow-empty", "-m", "subject only");
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var (_, spy) = Gate();

        var read = await spy.ReadTrailersAsync(repo.Path, head, CancellationToken.None);

        read.Succeeded.ShouldBeTrue();
        read.Items.ShouldBeEmpty();
        read.ExitCode.ShouldBe(0);
        HasExactTrailer(read.Items, "antiphon-commit", "gated").ShouldBeFalse();
    }

    [Test]
    public async Task C547_ReadTrailersAsync_odd_field_count_is_malformed_not_partial()
    {
        using var repo = await SeedAsync();
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var (_, spy) = Gate();
        spy.OverrideRun = args => args[0] == "log" && args.Any(a => a.StartsWith("--format=%(trailers:", StringComparison.Ordinal))
            ? (0, "antiphon\0true\0antiphon-commit\n", "")
            : null;

        var read = await spy.ReadTrailersAsync(repo.Path, head, CancellationToken.None);

        read.Succeeded.ShouldBeFalse();
        read.ExitCode.ShouldBe(-1);
        read.Error.ShouldBe("Malformed parsed Git trailers.");
        read.Items.ShouldBeEmpty();
    }

    [Test]
    public async Task C547_ReadTrailersAsync_git_failure_is_reported_not_empty()
    {
        using var repo = await SeedAsync();
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var (_, spy) = Gate();
        spy.OverrideRun = args => args[0] == "log" && args.Any(a => a.StartsWith("--format=%(trailers:", StringComparison.Ordinal))
            ? (128, "", "trailers unavailable")
            : null;

        var read = await spy.ReadTrailersAsync(repo.Path, head, CancellationToken.None);

        read.Succeeded.ShouldBeFalse();
        read.ExitCode.ShouldBe(128);
        read.Error.ShouldNotBeNull().ShouldContain("trailers unavailable");
    }

    [Test]
    public async Task C547_HasExactTrailer_requires_exactly_one_key_occurrence()
    {
        HasExactTrailer([new("antiphon-commit", "gated")], "antiphon-commit", "gated")
            .ShouldBeTrue("single");
        HasExactTrailer([new("antiphon-commit", "gated"), new("Antiphon-Commit", "other")], "antiphon-commit", "gated")
            .ShouldBeFalse("duplicate key, differently cased");
        HasExactTrailer([new("antiphon-commit", "gated"), new("antiphon-commit", "gated")], "antiphon-commit", "gated")
            .ShouldBeFalse("duplicate key, same value twice");
        HasExactTrailer([new("antiphon", "true")], "antiphon-commit", "gated")
            .ShouldBeFalse("absent");
        HasExactTrailer([new("antiphon", "true"), new("antiphon-commit", "gated"), new("antiphon-task", "x")], "antiphon-commit", "gated")
            .ShouldBeTrue("other keys around it");
        await Task.CompletedTask;
    }
}
