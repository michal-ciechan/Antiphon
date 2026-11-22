using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Review 7a972462 round 1 witnesses on PostgreSQL: path-matched readiness agents and setup's
/// reverse project lookup (CARD-0558) share one host-independent path identity
/// (<c>WorkingDirectoryIdentity</c>). Windows-spelled paths (drive or UNC) compare
/// case-insensitively with either separator; every other path is POSIX and compares exactly;
/// trailing separators never matter above the root. Every path carries a fresh token so rows
/// left in the shared test database by other tests cannot match.
/// </summary>
public partial class ProjectReadinessTests
{
    public static IEnumerable<Func<(string Project, string Agent)>> SameDirectorySpellings()
    {
        // Identical stored paths with a trailing separator (review blocker 1).
        yield return () => (@"C:\work\review-{u}\", @"C:\work\review-{u}\");
        yield return () => (@"\\filehost\share\review-{u}\", @"\\filehost\share\review-{u}\");
        yield return () => ("/srv/review-{u}/", "/srv/review-{u}/");
        // One-sided trailing separators, both directions, both kinds.
        yield return () => ("/srv/repo-{u}/", "/srv/repo-{u}");
        yield return () => ("/srv/repo-{u}", "/srv/repo-{u}/");
        yield return () => (@"C:\work\repo-{u}\", @"C:\work\repo-{u}");
        yield return () => (@"C:\work\repo-{u}", @"C:\work\repo-{u}\");
        yield return () => (@"\\filehost\share\repo-{u}", @"\\filehost\share\repo-{u}\");
        // A Windows path spelled with mixed separators and case still matches.
        yield return () => (@"C:\Work\Mixed-{u}\Repo", "c:/work/MIXED-{u}/repo/");
        yield return () => (@"\\FileHost\Share\Mixed-{u}", @"\/filehost/share/mixed-{u}/");
    }

    public static IEnumerable<Func<(string Project, string Agent)>> DistinctDirectorySpellings()
    {
        // POSIX identity (review blocker 2): a backslash is a file-name character on Linux and
        // case is significant.
        yield return () => (@"/srv/c-{u}/alpha\repo", "/srv/c-{u}/alpha/repo");
        yield return () => ("/srv/c-{u}/alpha/repo", @"/srv/c-{u}/alpha\repo");
        yield return () => ("/srv/c-{u}/Team/repo", "/srv/c-{u}/team/repo");
        yield return () => (@"alpha-{u}\repo", "alpha-{u}/repo");
        // A POSIX path never equals a Windows-spelled one.
        yield return () => ("/c-{u}/repo", @"C:\c-{u}\repo");
        yield return () => ("//filehost/share/c-{u}", @"\\filehost\share\c-{u}");
        // Plain cross-board negatives: a sibling and a child directory.
        yield return () => (@"C:\work\c-{u}", @"C:\work\c-{u}-other");
        yield return () => ("/srv/c-{u}", "/srv/c-{u}/child");
    }

    [Test]
    [MethodDataSource(nameof(SameDirectorySpellings))]
    public async Task readiness_counts_another_boards_agent_for_the_same_directory_spelling(
        string projectSpelling, string otherSpelling)
    {
        var (projectPath, agentPath) = Tokenise(projectSpelling, otherSpelling);
        var project = await SeedProjectAsync(localPath: projectPath);
        var otherBoard = await SeedBoardAsync((await SeedProjectAsync()).Id);
        var agent = await SeedAgentAsync(otherBoard.Id, workingDirectory: agentPath);

        var check = await CheckAsync(project.Id, ReadinessKeys.Agent);

        check.Status.ShouldBe(ReadinessStatus.Ok, $"project '{projectPath}' agent '{agentPath}'");
        check.Summary.ShouldBe($"Agent '{agent.Name}' is linked to this project.");
    }

    [Test]
    [MethodDataSource(nameof(DistinctDirectorySpellings))]
    public async Task readiness_ignores_another_boards_agent_in_a_distinct_directory(
        string projectSpelling, string otherSpelling)
    {
        var (projectPath, agentPath) = Tokenise(projectSpelling, otherSpelling);
        var project = await SeedProjectAsync(localPath: projectPath);
        var otherBoard = await SeedBoardAsync((await SeedProjectAsync()).Id);
        await SeedAgentAsync(otherBoard.Id, workingDirectory: agentPath);

        var check = await CheckAsync(project.Id, ReadinessKeys.Agent);

        check.Status.ShouldBe(ReadinessStatus.Missing, $"project '{projectPath}' agent '{agentPath}'");
    }

    [Test]
    public async Task readiness_names_only_the_agent_in_the_exact_posix_directory()
    {
        var u = Guid.NewGuid().ToString("N");
        var projectPath = $@"/srv/c-{u}/alpha\repo";
        var project = await SeedProjectAsync(localPath: projectPath);
        var otherBoard = await SeedBoardAsync((await SeedProjectAsync()).Id);
        var exact = await SeedAgentAsync(otherBoard.Id, workingDirectory: projectPath);
        await SeedAgentAsync(otherBoard.Id, workingDirectory: $"/srv/c-{u}/alpha/repo");
        await SeedAgentAsync(otherBoard.Id, workingDirectory: $@"/srv/c-{u}/Alpha\Repo");

        var check = await CheckAsync(project.Id, ReadinessKeys.Agent);

        check.Status.ShouldBe(ReadinessStatus.Ok);
        check.Summary.ShouldBe($"Agent '{exact.Name}' is linked to this project.");
    }

    [Test]
    [MethodDataSource(nameof(SameDirectorySpellings))]
    public async Task reverse_lookup_finds_the_project_for_the_same_directory_spelling(
        string projectSpelling, string otherSpelling)
    {
        var (projectPath, workingDirectory) = Tokenise(projectSpelling, otherSpelling);
        var project = await SeedProjectAsync(localPath: projectPath);

        await using var db = CreateContext();
        var found = await CreateAgentService(db).FindProjectForWorkingDirectoryAsync(
            workingDirectory, CancellationToken.None);

        found.ShouldNotBeNull($"project '{projectPath}' lookup '{workingDirectory}'");
        found.Id.ShouldBe(project.Id);
    }

    [Test]
    [MethodDataSource(nameof(DistinctDirectorySpellings))]
    public async Task reverse_lookup_ignores_a_project_in_a_distinct_directory(
        string projectSpelling, string otherSpelling)
    {
        var (projectPath, workingDirectory) = Tokenise(projectSpelling, otherSpelling);
        await SeedProjectAsync(localPath: projectPath);

        await using var db = CreateContext();
        var found = await CreateAgentService(db).FindProjectForWorkingDirectoryAsync(
            workingDirectory, CancellationToken.None);

        found.ShouldBeNull($"project '{projectPath}' lookup '{workingDirectory}'");
    }

    [Test]
    public async Task reverse_lookup_returns_the_case_exact_posix_project_among_loose_twins()
    {
        var u = Guid.NewGuid().ToString("N");
        var upper = await SeedProjectAsync(localPath: $"/srv/c-{u}/Team/repo");
        var lower = await SeedProjectAsync(localPath: $"/srv/c-{u}/team/repo/");
        var slashed = await SeedProjectAsync(localPath: $"/srv/c-{u}/team\\repo");

        await using var db = CreateContext();
        var service = CreateAgentService(db);
        (await LookupAsync(service, $"/srv/c-{u}/Team/repo/")).ShouldBe(upper.Id);
        (await LookupAsync(service, $"/srv/c-{u}/team/repo")).ShouldBe(lower.Id);
        (await LookupAsync(service, $"/srv/c-{u}/team\\repo")).ShouldBe(slashed.Id);
        (await LookupAsync(service, $"/srv/c-{u}/TEAM/repo")).ShouldBeNull();
    }

    private static (string Project, string Other) Tokenise(string projectSpelling, string otherSpelling)
    {
        var u = Guid.NewGuid().ToString("N");
        return (projectSpelling.Replace("{u}", u, StringComparison.Ordinal),
            otherSpelling.Replace("{u}", u, StringComparison.Ordinal));
    }

    private static async Task<Guid?> LookupAsync(AgentService service, string workingDirectory) =>
        (await service.FindProjectForWorkingDirectoryAsync(workingDirectory, CancellationToken.None))?.Id;
}
