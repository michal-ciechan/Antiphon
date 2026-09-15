using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class GatedCommitServiceTests
{
    [Test]
    [Arguments("stage")]
    [Arguments("commit")]
    [Arguments("unstage")]
    public async Task Explicit_git_mutations_treat_bracketed_filenames_literally(string operation)
    {
        using var repo = await SeedAsync();
        const string selected = "item[ab].md";
        const string neighbor = "itema.md";
        await repo.CommitFileAsync(selected, "selected base");
        await repo.CommitFileAsync(neighbor, "neighbor base");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, selected), "selected work");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, neighbor), "neighbor staged");
        await repo.GitAsync("add", "--", neighbor);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, neighbor), "neighbor work");
        var (_, git) = Gate();
        if (operation == "stage")
            (await git.StageAsync(repo.Path, [selected], CancellationToken.None)).Code.ShouldBe(0);
        else if (operation == "commit")
        {
            (await git.CommitOnlyAsync(repo.Path, [selected], Message(), Trailers(), CancellationToken.None)).Code.ShouldBe(0);
            (await git.TryDiffTreePathsAsync(repo.Path, "HEAD", CancellationToken.None)).Items.ShouldBe(new[] { selected });
            (await repo.GitReadAsync("show", $"HEAD:{neighbor}")).ShouldBe("neighbor base");
        }
        else
        {
            await repo.GitAsync("add", "--", ":(literal)" + selected);
            (await git.UnstageAsync(repo.Path, [selected], CancellationToken.None)).Code.ShouldBe(0);
        }
        (await repo.GitReadAsync("show", $":{neighbor}")).ShouldBe("neighbor staged");
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, neighbor))).ShouldBe("neighbor work");
        (await repo.GitReadAsync("show", $":{selected}")).ShouldBe(operation == "unstage" ? "selected base" : "selected work");
    }

    [Test]
    public async Task Scoped_staging_detects_and_restores_foreign_index_changes()
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "selected.md"), "selected");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign staged");
        await repo.GitAsync("add", "foreign.md");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foreign.md"), "foreign work");
        var index = await repo.GitReadAsync("write-tree");
        var head = await repo.GitReadAsync("rev-parse", "HEAD");
        var (gate, spy) = Gate();
        spy.BeforeRun = async args =>
        {
            if (args[0] == "add") await repo.GitAsync("add", "foreign.md");
        };
        var result = await gate.CommitAsync(repo.Path, ["selected.md"], Message(), Trailers(), CancellationToken.None);
        result.Outcome.ShouldBe(GatedCommitOutcome.CommitFailed);
        (await repo.GitReadAsync("write-tree")).ShouldBe(index);
        (await repo.GitReadAsync("rev-parse", "HEAD")).ShouldBe(head);
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "foreign.md"))).ShouldBe("foreign work");
        spy.Verbs.ShouldNotContain("commit");
    }

    [Test]
    [Arguments("extra")]
    [Arguments("missing")]
    public async Task Commit_footprint_mismatch_stays_pending_during_operation_recovery(string mismatch)
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "selected.md"), "selected");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "second.md"), "second");
        var (gate, spy) = Gate();
        var trailers = Trailers();
        spy.OverrideRun = args => args[0] == "diff-tree"
            ? (0, mismatch == "extra" ? "selected.md\0second.md\0foreign.md\0" : "selected.md\0", "") : null;
        var error = await Should.ThrowAsync<CommitInspectionPendingException>(() => gate.CommitAsync(
            repo.Path, ["selected.md", "second.md"], Message(), trailers, CancellationToken.None));
        var operationId = (Guid)error.Extensions!["operationId"]!;
        await Should.ThrowAsync<CommitInspectionPendingException>(() => gate.RecoverAsync(
            repo.Path, Guid.Parse(trailers.Single(t => t.Key == "antiphon-task").Value), operationId, CancellationToken.None));
        spy.OverrideRun = null;
        var result = await gate.RecoverAsync(repo.Path,
            Guid.Parse(trailers.Single(t => t.Key == "antiphon-task").Value), operationId, CancellationToken.None);
        result.Files.ShouldBe(new[] { "selected.md", "second.md" }, ignoreOrder: true);
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
    }
}
