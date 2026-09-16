using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class GatedCommitServiceTests
{
    [Test]
    [Arguments("branch")]
    [Arguments("reflog")]
    [Arguments("unborn")]
    public async Task Recovery_finds_exact_attempt_across_refs_and_reflogs(string checkout)
    {
        using var repo = await SeedAsync();
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "selected.md"), "selected");
        var (gate, spy) = Gate();
        var trailers = Trailers();
        var taskId = Guid.Parse(trailers.Single(t => t.Key == "antiphon-task").Value);
        const string settlement = "durable-settlement";
        var committed = await gate.CommitAsync(repo.Path, ["selected.md"], Message(),
            [.. trailers, ("antiphon-settlement", settlement)], CancellationToken.None);
        committed.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        var operationId = Guid.Parse((await repo.GitReadAsync("log", "-1",
            "--format=%(trailers:key=antiphon-operation,valueonly)")).Trim());
        await repo.GitAsync("switch", "-c", "other", baseline);
        if (checkout == "reflog") await repo.GitAsync("branch", "-D", "master");
        if (checkout == "unborn") await repo.GitAsync("symbolic-ref", "HEAD", "refs/heads/unborn");
        if (checkout == "reflog")
            (await repo.GitReadAsync("log", "--all", "--format=%H")).ShouldNotContain(committed.Sha!);
        var branch = await repo.GitReadAsync("symbolic-ref", "HEAD");
        var index = await repo.GitReadAsync("write-tree");
        var recovered = await gate.RecoverAsync(repo.Path, taskId, operationId, CancellationToken.None);
        recovered.Sha.ShouldBe(committed.Sha);
        recovered.Files.ShouldBe(new[] { "selected.md" });
        var settlementResult = await spy.FindSettlementCommitsAsync(repo.Path, taskId, settlement, CancellationToken.None);
        settlementResult.Succeeded.ShouldBeTrue();
        settlementResult.Items.ShouldBe(new[] { committed.Sha! });
        (await spy.FindSettlementCommitsAsync(repo.Path, Guid.NewGuid(), settlement, CancellationToken.None)).Items.ShouldBeEmpty();
        spy.OverrideRun = args => args.Contains("--all") ? (128, "", "history unavailable") : null;
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ServiceUnavailableException>(() =>
            gate.RecoverAsync(repo.Path, taskId, operationId, CancellationToken.None));
        (await spy.FindSettlementCommitsAsync(repo.Path, taskId, settlement, CancellationToken.None)).Succeeded.ShouldBeFalse();
        (await repo.GitReadAsync("symbolic-ref", "HEAD")).ShouldBe(branch);
        (await repo.GitReadAsync("write-tree")).ShouldBe(index);
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
        spy.Verbs.ShouldNotContain("push");
    }
}
