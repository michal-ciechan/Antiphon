using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class GatedCommitServiceTests
{
    [Test]
    [Arguments(":")]
    [Arguments("=")]
    [Arguments("%")]
    public async Task Recovery_uses_Git_trailer_separators(string separator)
    {
        using var repo = await SeedAsync();
        await repo.GitAsync("config", "trailer.separators", separator);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "selected.md"), "selected");
        var (gate, spy) = Gate();
        var trailers = Trailers();
        var taskId = Guid.Parse(trailers.Single(t => t.Key == "antiphon-task").Value);
        const string settlement = "separator-settlement";
        var committed = await gate.CommitAsync(repo.Path, ["selected.md"], Message(),
            [.. trailers, ("antiphon-settlement", settlement)], CancellationToken.None);
        committed.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        committed.Sha.ShouldBe(head);
        committed.Files.ShouldBe(new[] { "selected.md" });
        (await repo.GitReadAsync("log", "-1", "--format=%B")).ShouldContain($"antiphon-task{separator} {taskId:D}");
        var operation = Guid.Parse((await repo.GitReadAsync("log", "-1",
            "--format=%(trailers:key=antiphon-operation,valueonly)")).Trim());
        var recovered = await gate.RecoverAsync(repo.Path, taskId, operation, CancellationToken.None);
        recovered.Sha.ShouldBe(head);
        recovered.Files.ShouldBe(committed.Files);
        var found = await spy.FindSettlementCommitsAsync(repo.Path, taskId, settlement, CancellationToken.None);
        found.Succeeded.ShouldBeTrue();
        found.Items.ShouldBe(new[] { head });
        var absent = await spy.FindSettlementCommitsAsync(repo.Path, Guid.NewGuid(), settlement, CancellationToken.None);
        absent.Succeeded.ShouldBeTrue();
        absent.Items.ShouldBeEmpty();
        // A second, differently cased identity key is still a duplicate, not an exact identity.
        await repo.GitAsync("commit", "--allow-empty", "-m", $"duplicate\n\nantiphon{separator} true\n"
            + $"antiphon-task{separator} {taskId:D}\nAntiphon-Task{separator} {Guid.NewGuid():D}\n"
            + $"antiphon-commit{separator} gated\nantiphon-settlement{separator} {settlement}");
        (await spy.FindSettlementCommitsAsync(repo.Path, taskId, settlement, CancellationToken.None)).Items.ShouldBe(new[] { head });
        spy.OverrideRun = args => args.Contains("--all") ? (128, "", "history unavailable") : null;
        (await spy.FindSettlementCommitsAsync(repo.Path, taskId, settlement, CancellationToken.None)).Succeeded.ShouldBeFalse();
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        spy.Verbs.ShouldNotContain("push");
    }
}
