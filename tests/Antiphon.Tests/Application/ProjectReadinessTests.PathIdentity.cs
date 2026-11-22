using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
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

    [Test]
    public async Task candidate_prefilter_runs_in_postgres_as_one_filtered_scan()
    {
        await using var db = CreateContext();
        var probe = $"/srv/plan-{Guid.NewGuid():N}/";
        var query = db.Agents.AsNoTracking()
            .Where(a => !a.IsPoolDelegate)
            .Where(WorkingDirectoryIdentity.CandidateFilter<Agent>(a => a.WorkingDirectory, probe));

        var sql = query.ToQueryString();
        Console.WriteLine(sql);
        sql.ShouldContain("lower(rtrim(replace(");
        sql.ShouldContain("COLLATE \"C\")");
        sql.ShouldContain(" LIKE @");
        sql.ShouldContain("ESCAPE '!'");
        sql.ShouldNotContain("ILIKE");

        await using var command = query.CreateDbCommand();
        command.CommandText = "EXPLAIN " + command.CommandText;
        await db.Database.OpenConnectionAsync();
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(0));
        }

        Console.WriteLine(string.Join(Environment.NewLine, plan));
        plan[0].ShouldContain("on \"Agents\"");
        plan.ShouldContain(line => line.Contains("Filter:") && line.Contains("rtrim"));
        plan.ShouldNotContain(line => line.Contains("Join") || line.Contains("SubPlan"));
    }

    [Test]
    public async Task candidate_prefilter_is_case_insensitive_and_treats_like_wildcards_literally()
    {
        var u = Guid.NewGuid().ToString("N");
        var board = await SeedBoardAsync((await SeedProjectAsync()).Id);
        var exact = await SeedAgentAsync(board.Id, workingDirectory: $@"/SRV/c-{u}/a_b%!\");
        var cased = await SeedAgentAsync(board.Id, workingDirectory: $"/srv/c-{u}/A_B%!");
        await SeedAgentAsync(board.Id, workingDirectory: $"/srv/c-{u}/axbyz");
        await SeedAgentAsync(board.Id, workingDirectory: $"/srv/c-{u}/a_b%");
        await SeedAgentAsync(board.Id, workingDirectory: $"/srv/c-{u}/a_b%!x");

        await using var db = CreateContext();
        var ids = await db.Agents.AsNoTracking()
            .Where(WorkingDirectoryIdentity.CandidateFilter<Agent>(a => a.WorkingDirectory, $"/srv/c-{u}/a_b%!/"))
            .Select(a => a.Id)
            .ToListAsync();

        ids.ShouldBe([exact.Id, cased.Id], ignoreOrder: true);
    }

    /// <summary>
    /// Review 0d322ed5 round 2: case variants beyond ASCII. Windows spellings fold case with
    /// <c>ToLowerInvariant</c>, so the corpus covers é/É, ü/Ü, Greek (including final sigma),
    /// Turkish dotted and dotless i, ß/ẞ, the Kelvin and Angstrom signs (non-ASCII letters whose
    /// lower case is ASCII or Latin-1) and a supplementary-plane pair. POSIX copies stay exact.
    /// </summary>
    internal static readonly string[] CaseFoldWords =
    [
        "café", "CAFÉ", "Café", "cafe", "CAFE",
        "über", "ÜBER", "Über", "uber",
        "σοφια", "ΣΟΦΙΑ", "Σοφια",
        "οδος", "ΟΔΟΣ", "οδοσ",
        "\u0130zmir", "izmir", "Izmir", "\u0131zmir", "IZMIR",
        "straße", "STRAẞE", "STRASSE", "strasse",
        "\u212Aelvin", "kelvin", "KELVIN",
        "\u212Bngstrom", "ångstrom", "ÅNGSTROM",
        "\U00010400x", "\U00010428x",
    ];

    internal static IReadOnlyList<string> CaseFoldCorpus(string u)
    {
        var corpus = new List<string>();
        for (var i = 0; i < CaseFoldWords.Length; i++)
        {
            var word = CaseFoldWords[i];
            corpus.Add(i % 2 == 0 ? $@"C:\fold-{u}\{word}" : $"c:/FOLD-{u}/{word}/");
            corpus.Add($"/srv/fold-{u}/{word}");
        }

        return corpus;
    }

    /// <summary>
    /// Property witness: for every pair of corpus spellings that <see cref="WorkingDirectoryIdentity.Same"/>
    /// accepts, the SQL prefilter returns the stored row for the probe. The prefilter may return more
    /// (callers refine with Same); it must never return less.
    /// </summary>
    internal static async Task AssertPrefilterAdmitsEverySamePairAsync(
        Func<AppDbContext> createContext, Guid boardId, Func<Guid, string, Task<Agent>> seedAgent)
    {
        var corpus = CaseFoldCorpus(Guid.NewGuid().ToString("N"));
        var stored = new Dictionary<Guid, string>();
        foreach (var path in corpus)
            stored.Add((await seedAgent(boardId, path)).Id, path);

        var missed = new List<string>();
        var accepted = 0;
        await using var db = createContext();
        foreach (var probe in corpus)
        {
            var admitted = (await db.Agents.AsNoTracking()
                .Where(a => a.BoardId == boardId)
                .Where(WorkingDirectoryIdentity.CandidateFilter<Agent>(a => a.WorkingDirectory, probe))
                .Select(a => a.Id)
                .ToListAsync()).ToHashSet();
            var pattern = WorkingDirectoryIdentity.CandidatePattern(probe);
            stored.Where(row => admitted.Contains(row.Key)).Select(row => row.Value).Order(StringComparer.Ordinal)
                .ShouldBe(stored.Values.Where(path => CLocalePrefilterModel.Admits(path, pattern)).Order(StringComparer.Ordinal),
                    $"the database must evaluate the prefilter exactly as the C-collation model for probe '{probe}'");
            foreach (var (id, path) in stored)
            {
                if (!WorkingDirectoryIdentity.Same(path, probe))
                    continue;
                accepted++;
                if (!admitted.Contains(id))
                    missed.Add($"stored {path} probe {probe}");
            }
        }

        missed.ShouldBeEmpty();
        accepted.ShouldBeGreaterThan(corpus.Count, "the corpus must contain case-variant pairs, not only self-pairs");
    }

    [Test]
    public async Task candidate_prefilter_admits_every_pair_same_accepts()
    {
        var board = await SeedBoardAsync((await SeedProjectAsync()).Id);
        await AssertPrefilterAdmitsEverySamePairAsync(
            CreateContext, board.Id, (boardId, path) => SeedAgentAsync(boardId, workingDirectory: path));
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
