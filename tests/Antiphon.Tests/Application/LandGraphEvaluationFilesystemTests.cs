using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class LandGraphEvaluationTests
{
    private sealed record FilesystemFixture(string Id, string Group, int Expected, string Reason,
        string Owner, Dictionary<string, string> Files, string[]? Omit)
    {
        public Dictionary<string, string> Inputs() => Files
            .Where(pair => !(Omit ?? []).Contains(pair.Key)).ToDictionary();
    }

    private static FilesystemFixture[] FilesystemFixtures() =>
        JsonSerializer.Deserialize<FilesystemFixture[]>(File.ReadAllText(Path.Combine(RepoRoot(),
                "tests/Antiphon.Tests/Application/Fixtures/land-graph-c1528.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static async Task AssertFilesystemFixtureAsync(FilesystemFixture fixture)
    {
        await using var repo = await AuditRepo.CreateAsync(fixture.Inputs());
        if (fixture.Omit?.Contains("docs/flag.md") == true)
            Directory.Exists(repo.Full("docs")).ShouldBeFalse(fixture.Id);
        File.Exists(repo.Full("docs/missing.md")).ShouldBeFalse(fixture.Id);
        File.Exists(repo.Full("README.md")).ShouldBeFalse(fixture.Id);
        var run = await repo.RunToolAsync();
        var context = fixture.Id + ": " + run.Text;
        run.ExitCode.ShouldBe(fixture.Expected, context);
        run.Text.ShouldContain(fixture.Reason, Case.Sensitive, context);
        if (fixture.Expected == 0) run.Lines.ShouldContain("approved", context);
        else
        {
            run.Text.ShouldContain(fixture.Owner, Case.Sensitive, context);
            run.Lines.ShouldNotContain("approved", context);
            run.Text.ShouldNotContain("evaluation-failed:", Case.Sensitive, context);
        }
        repo.AssertUntouched();
    }

    [Test]
    [Arguments("present")]
    [Arguments("absent")]
    [Arguments("readme-absent")]
    [Arguments("backslash")]
    [Arguments("case")]
    [Timeout(300_000)]
    public Task C1528_StaticExistsRefuses(string variant) =>
        AssertFilesystemFixtureAsync(FilesystemFixtures().Single(f => f.Id == variant));

    [Test]
    [Timeout(300_000)]
    public Task C1528_ProjectedExistsRefuses() =>
        AssertFilesystemFixtureAsync(FilesystemFixtures().Single(f => f.Id == "projected"));

    [Test]
    [Arguments("metadata")]
    [Arguments("default-metadata")]
    [Timeout(300_000)]
    public Task C1528_MetadataReadRefuses(string variant) =>
        AssertFilesystemFixtureAsync(FilesystemFixtures().Single(f => f.Id == variant));

    [Test]
    [Timeout(300_000)]
    public Task C1528_EqualResultReadRefuses() =>
        AssertFilesystemFixtureAsync(FilesystemFixtures().Single(f => f.Id == "equal-result"));

    [Test]
    [Timeout(300_000)]
    public async Task C1528_FunctionSyntaxAndAbsence()
    {
        foreach (var fixture in FilesystemFixtures().Where(f => f.Group == "syntax"))
            await AssertFilesystemFixtureAsync(fixture);
    }

    [Test]
    [Timeout(300_000)]
    public async Task C1528_PureMetadataAndFunctionsApprove()
    {
        foreach (var fixture in FilesystemFixtures().Where(f => f.Group == "controls"))
            await AssertFilesystemFixtureAsync(fixture);
    }
}
