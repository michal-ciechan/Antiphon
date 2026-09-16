using System.Text.Json;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class GatedCommitServiceTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Manifest_records_actual_staged_footprint_after_selected_revert(bool scoped)
    {
        using var repo = await SeedAsync();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "selected.md"), "selected");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "tracked.txt"), "staged change");
        await repo.GitAsync("add", "tracked.txt");
        var (gate, spy) = Gate();
        spy.BeforeRun = async args =>
        {
            if (args[0] == "add")
                await File.WriteAllTextAsync(Path.Combine(repo.Path, "tracked.txt"), "tracked\n");
        };
        var trailers = Trailers();
        var result = await gate.CommitAsync(repo.Path, scoped ? ["selected.md", "tracked.txt"] : null,
            Message(), trailers, CancellationToken.None);
        result.Outcome.ShouldBe(GatedCommitOutcome.Committed);
        result.Files.ShouldBe(new[] { "selected.md" });
        JsonSerializer.Deserialize<string[]>((await spy.ApprovedCommitPathsAsync(repo.Path, result.Sha!, CancellationToken.None)).Stdout)
            .ShouldBe(new[] { "selected.md" });
        (await repo.GitReadAsync("show", "HEAD:tracked.txt")).ShouldBe("tracked\n");
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
        var operationId = Guid.Parse((await repo.GitReadAsync("log", "-1",
            "--format=%(trailers:key=antiphon-operation,valueonly)")).Trim());
        var recovered = await gate.RecoverAsync(repo.Path, Guid.Parse(trailers.Single(t => t.Key == "antiphon-task").Value),
            operationId, CancellationToken.None);
        recovered.Sha.ShouldBe(result.Sha);
        recovered.Files.ShouldBe(result.Files);
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
    }
}
