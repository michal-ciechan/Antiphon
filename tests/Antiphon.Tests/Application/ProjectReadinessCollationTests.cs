using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.FileSystem;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using TUnit.Core;
using System.IO.Abstractions;

namespace Antiphon.Tests.Application;

/// <summary>
/// Review 0d322ed5 round 2 blocker: with a UTF-8 database whose locale is C, PostgreSQL folds only
/// ASCII case, so a prefilter that relies on the database's case folding dropped Windows-spelled
/// pairs that <see cref="WorkingDirectoryIdentity.Same"/> accepts (stored <c>c:\café</c>, project
/// <c>C:/CAFÉ</c>). These witnesses run both services and the prefilter itself against an isolated
/// database created with <c>LC_COLLATE</c> and <c>LC_CTYPE</c> C, migrated to the tip once per class
/// and dropped afterwards. Every path carries a fresh token.
/// </summary>
[Category("Integration")]
public class ProjectReadinessCollationTests
{
    private static IsolatedTestSchema? s_schema;

    [Before(Class)]
    public static async Task CreateCLocaleDatabaseAsync()
    {
        await TestDbFixture.Lifecycle.EnsureReadyAsync();
        using var setup = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var database = $"test_{Guid.NewGuid():N}";
        await using (var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString))
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.DbCreate, "C-locale readiness database");
            await maintenance.OpenAsync(setup.Token);
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE {database} TEMPLATE template0 ENCODING 'UTF8' LC_COLLATE 'C' LC_CTYPE 'C'",
                maintenance) { CommandTimeout = 30 };
            await create.ExecuteNonQueryAsync(setup.Token);
        }

        var connection = new NpgsqlConnectionStringBuilder(TestDbFixture.MaintenanceConnectionString) { Database = database };
        s_schema = new IsolatedTestSchema(database, connection.ConnectionString);
        using (TestTiming.BeginPhase(TestPhaseKind.DbMigrate, "C-locale readiness database tip"))
        {
            await using var db = CreateContext();
            await db.Database.MigrateAsync(setup.Token);
        }
    }

    [After(Class)]
    public static async Task DropCLocaleDatabaseAsync()
    {
        if (s_schema is not null)
            await s_schema.DisposeAsync();
        s_schema = null;
    }

    /// <summary>Windows-spelled case variants that differ only in non-ASCII letters.</summary>
    public static IEnumerable<Func<(string Stored, string Probe)>> NonAsciiCaseVariants()
    {
        yield return () => (@"c:\work\c-{u}\café", "C:/WORK/C-{u}/CAFÉ");
        yield return () => ("C:/WORK/C-{u}/CAFÉ/", @"c:\work\c-{u}\café");
        yield return () => (@"C:\work\c-{u}\Über", @"c:\work\c-{u}\über\");
        yield return () => (@"C:\work\c-{u}\ΣΟΦΙΑ", "c:/work/c-{u}/σοφια");
        yield return () => (@"c:\work\c-{u}\straße", @"C:\WORK\C-{u}\STRAẞE");
        yield return () => (@"\\filehost\share\c-{u}\ångström", @"\\FILEHOST\SHARE\C-{u}\ÅNGSTRÖM");
    }

    [Test]
    public async Task database_folds_only_ascii_case()
    {
        await using var db = CreateContext();
        var locale = await db.Database.SqlQueryRaw<string>(
            "SELECT datcollate || '|' || datctype AS \"Value\" FROM pg_database WHERE datname = current_database()")
            .SingleAsync();
        locale.ShouldBe("C|C");
        var folded = await db.Database.SqlQueryRaw<string>("SELECT lower('CAFÉ') AS \"Value\"").SingleAsync();
        folded.ShouldBe("cafÉ", "the C locale must leave non-ASCII case alone, or this fixture witnesses nothing");
    }

    [Test]
    [MethodDataSource(nameof(NonAsciiCaseVariants))]
    public async Task readiness_counts_a_non_ascii_case_variant_agent_in_c_locale(string storedSpelling, string probeSpelling)
    {
        var (agentPath, projectPath) = Tokenise(storedSpelling, probeSpelling);
        WorkingDirectoryIdentity.Same(agentPath, projectPath).ShouldBeTrue("fixture pair must be one directory");
        var project = await SeedProjectAsync(projectPath);
        var otherBoard = await SeedBoardAsync((await SeedProjectAsync(null)).Id);
        var agent = await SeedAgentAsync(otherBoard.Id, agentPath);

        await using var db = CreateContext();
        var dto = await CreateSetupService(db).GetReadinessAsync(project.Id, CancellationToken.None);
        var check = dto.Checks.Single(c => c.Key == ReadinessKeys.Agent);

        check.Status.ShouldBe(ReadinessStatus.Ok, $"project '{projectPath}' agent '{agentPath}'");
        check.Summary.ShouldBe($"Agent '{agent.Name}' is linked to this project.");
    }

    [Test]
    [MethodDataSource(nameof(NonAsciiCaseVariants))]
    public async Task reverse_lookup_finds_a_non_ascii_case_variant_project_in_c_locale(string storedSpelling, string probeSpelling)
    {
        var (projectPath, workingDirectory) = Tokenise(storedSpelling, probeSpelling);
        var project = await SeedProjectAsync(projectPath);

        await using var db = CreateContext();
        var found = await CreateAgentService(db).FindProjectForWorkingDirectoryAsync(workingDirectory, CancellationToken.None);

        found.ShouldNotBeNull($"project '{projectPath}' lookup '{workingDirectory}'");
        found.Id.ShouldBe(project.Id);
    }

    [Test]
    public async Task prefilter_admits_every_pair_same_accepts_in_c_locale()
    {
        var board = await SeedBoardAsync((await SeedProjectAsync(null)).Id);
        await ProjectReadinessTests.AssertPrefilterAdmitsEverySamePairAsync(CreateContext, board.Id, SeedAgentAsync);
    }

    private static (string Stored, string Probe) Tokenise(string stored, string probe)
    {
        var u = Guid.NewGuid().ToString("N");
        return (stored.Replace("{u}", u, StringComparison.Ordinal), probe.Replace("{u}", u, StringComparison.Ordinal));
    }

    private static AppDbContext CreateContext() =>
        new(TestDbFixture.CreateDbContextOptions(
            (s_schema ?? throw new InvalidOperationException("C-locale database not created")).ConnectionString));

    private static ProjectSetupService CreateSetupService(AppDbContext db) =>
        new(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings()),
            NullLogger<ProjectSetupService>.Instance);

    private static AgentService CreateAgentService(AppDbContext db) =>
        new(
            db,
            new CardWorkflowRunFactory(db, TimeProvider.System),
            new MockEventBus(),
            TimeProvider.System,
            new FileSystemDirectoryWriter(new FileSystem()),
            NullLogger<AgentService>.Instance);

    private static async Task<Project> SeedProjectAsync(string? localPath)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Collation {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = localPath,
            BaseBranch = "master",
            CreatedAt = now,
            UpdatedAt = now,
        };
        await using var db = CreateContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private static async Task<Board> SeedBoardAsync(Guid projectId)
    {
        var now = DateTime.UtcNow;
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = $"Board {Guid.NewGuid():N}",
            Description = string.Empty,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var column in BoardService.CreateDefaultColumns(board, now))
            board.Columns.Add(column);
        await using var db = CreateContext();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        return board;
    }

    private static async Task<Agent> SeedAgentAsync(Guid boardId, string workingDirectory)
    {
        var now = DateTime.UtcNow;
        var slug = Guid.NewGuid().ToString("N");
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"Agent {slug}",
            Slug = $"agent-{slug}",
            WorkingDirectory = workingDirectory,
            BoardId = boardId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }
}
