using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.Tests.TestHelpers;

/// <summary>CARD-0515. Isolated store, deterministic fleet, and a successful-command counter.</summary>
public static class ScopedAgentTaskListFixture
{
    public static readonly DateTime T = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    public static readonly Guid ProjectX = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    public static readonly Guid ProjectY = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    public static readonly Guid ProjectY2 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3");
    public static readonly Guid BoardB1 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    public static readonly Guid BoardB2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
    public static readonly Guid BoardC = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3");
    public static readonly Guid BoardArchived = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4");
    public static readonly Guid CardB1 = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1");
    public static readonly Guid CardB2 = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc2");
    public static readonly Guid CardC = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc3");
    public static readonly Guid CardArchived = Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc4");

    public static readonly Guid X1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid X2 = Guid.Parse("11111111-1111-1111-1111-111111111112");
    public static readonly Guid X3 = Guid.Parse("11111111-1111-1111-1111-111111111113");
    public static readonly Guid X4 = Guid.Parse("11111111-1111-1111-1111-111111111114");
    public static readonly Guid Y1 = Guid.Parse("22222222-2222-2222-2222-222222222221");
    public static readonly Guid Y2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Y3 = Guid.Parse("22222222-2222-2222-2222-222222222223");
    public static readonly Guid N1 = Guid.Parse("33333333-3333-3333-3333-333333333331");
    public static readonly Guid N2 = Guid.Parse("33333333-3333-3333-3333-333333333332");

    public static readonly Guid RootShared = Guid.Parse("44444444-4444-4444-4444-444444444441");
    public static readonly Guid RootOther = Guid.Parse("44444444-4444-4444-4444-444444444442");

    public const string AntiphonName = "Antiphon";
    public const string GymStatName = "gym-stat";
    public const string BoardB1Name = "Antiphon board";
    public const string BoardB2Name = "Antiphon other";
    public const string BoardCName = "gym-stat board";
    public const string XPath = @"C:\src\Antiphon";
    public const string SiblingPath = @"C:\wt\card-x1";

    public static readonly Guid[] FleetOrder = [X1, X2, X3, X4, Y1, Y2, Y3, N1, N2];

    public static AgentTaskService CreateService(AppDbContext db) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings { AllowedRoots = [] }),
        new MockEventBus(),
        new RecordingSessionStopper(),
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance);

    public static AppDbContext CreateContext(string connectionString, ScopeQueryCounter? counter = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>(
            TestDbFixture.CreateDbContextOptions(connectionString));
        if (counter is not null)
            builder.AddInterceptors(counter);
        return new AppDbContext(builder.Options);
    }

    public static async Task SeedFleetAsync(AppDbContext db, bool includeY2Twin = false)
    {
        var now = T;
        db.AddRange(
            Project(ProjectX, AntiphonName),
            Project(ProjectY, GymStatName));
        if (includeY2Twin)
            db.Add(Project(ProjectY2, GymStatName));
        db.AddRange(
            Board(BoardB1, ProjectX, BoardB1Name),
            Board(BoardB2, ProjectX, BoardB2Name),
            Board(BoardC, ProjectY, BoardCName),
            Board(BoardArchived, ProjectX, "archived Antiphon", archivedAt: now.AddDays(-2)));
        var colB1 = Column(BoardB1);
        var colB2 = Column(BoardB2);
        var colC = Column(BoardC);
        var colArchived = Column(BoardArchived);
        db.AddRange(colB1, colB2, colC, colArchived);
        db.AddRange(
            Card(CardB1, BoardB1, colB1.Id, "CARD-0039"),
            Card(CardB2, BoardB2, colB2.Id, "CARD-0040"),
            Card(CardC, BoardC, colC.Id, "CARD-0039"),
            Card(CardArchived, BoardArchived, colArchived.Id, "CARD-0515"));
        await db.SaveChangesAsync();

        db.AddRange(
            TaskRow(X1, ProjectX, CardB1, XPath, XPath, now.AddSeconds(1), RootShared),
            TaskRow(X2, null, CardB1, XPath, XPath, now.AddSeconds(2), RootShared),
            TaskRow(X3, ProjectX, null, XPath, XPath, now.AddSeconds(3), RootOther),
            TaskRow(X4, ProjectX, CardB2, XPath, XPath, now.AddSeconds(4), RootShared),
            TaskRow(Y1, ProjectY, CardC, @"C:\src\gym-stat", @"C:\src\gym-stat", now.AddSeconds(5), Y1),
            TaskRow(Y2, ProjectY, null, XPath, XPath, now.AddSeconds(6), Y2),
            TaskRow(Y3, ProjectY, CardB1, @"C:\src\gym-stat", @"C:\src\gym-stat", now.AddSeconds(7), Y3),
            TaskRow(N1, null, null, XPath, XPath, now.AddSeconds(8), N1),
            TaskRow(N2, null, null, SiblingPath, XPath, now.AddSeconds(9), N2));
        await db.SaveChangesAsync();
    }

    public static async Task SeedAllNoneAsync(AppDbContext db, int count = 2)
    {
        for (var i = 0; i < count; i++)
        {
            var id = Guid.Parse($"33333333-3333-3333-3333-3333333333{i:D2}");
            db.Add(TaskRow(id, null, null, XPath, XPath, T.AddSeconds(i + 1), id));
        }

        await db.SaveChangesAsync();
    }

    public static async Task SeedDistinctKeysAsync(AppDbContext db, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var projectId = Guid.Parse($"aa000000-0000-0000-0000-{i:D12}");
            var boardId = Guid.Parse($"bb000000-0000-0000-0000-{i:D12}");
            var cardId = Guid.Parse($"cc000000-0000-0000-0000-{i:D12}");
            var taskId = Guid.Parse($"dd000000-0000-0000-0000-{i:D12}");
            db.Add(Project(projectId, $"proj-{i}"));
            db.Add(Board(boardId, projectId, $"board-{i}"));
            var column = Column(boardId, $"col-{i}");
            db.Add(column);
            db.Add(Card(cardId, boardId, column.Id, $"CARD-{i:D4}"));
            db.Add(TaskRow(taskId, projectId, cardId, XPath, XPath, T.AddSeconds(i + 1), taskId));
        }

        await db.SaveChangesAsync();
    }

    public static Guid[] ExpectedItems(string scope, AgentTaskUnscopedMode mode) =>
        mode == AgentTaskUnscopedMode.Only
            ? [N1, N2]
            : Concat(ScopedMatches(scope), mode == AgentTaskUnscopedMode.Include ? [N1, N2] : []);

    public static Guid[] ScopedMatches(string scope) => scope switch
    {
        "projectX" => [X1, X2, X3, X4],
        "boardB1" => [X1, X2, Y3],
        "both" => [X1, X2],
        "projectY" => [Y1, Y2, Y3],
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };

    public static (int Total, int Unscoped, IReadOnlyDictionary<Guid, int> ByProject) ExpectedExcluded(
        string scope, AgentTaskUnscopedMode mode)
    {
        var items = new HashSet<Guid>(ExpectedItems(scope, mode));
        if (mode == AgentTaskUnscopedMode.Only)
            items = [N1, N2];
        var withheld = FleetOrder.Where(id => !items.Contains(id)).ToList();
        var unscoped = withheld.Count(id => id == N1 || id == N2);
        var byProject = new Dictionary<Guid, int>();
        foreach (var id in withheld)
        {
            if (id is var x && (x == X1 || x == X2 || x == X3 || x == X4))
                byProject[ProjectX] = byProject.GetValueOrDefault(ProjectX) + 1;
            else if (x == Y1 || x == Y2 || x == Y3)
                byProject[ProjectY] = byProject.GetValueOrDefault(ProjectY) + 1;
        }

        return (unscoped + byProject.Values.Sum(), unscoped, byProject);
    }

    public static AgentTaskScopeRequest ProjectXScope(AgentTaskUnscopedMode mode = AgentTaskUnscopedMode.Exclude) =>
        new(ProjectX, null, mode);

    public static AgentTaskScopeRequest BoardB1Scope(AgentTaskUnscopedMode mode = AgentTaskUnscopedMode.Exclude) =>
        new(null, BoardB1, mode);

    public static AgentTaskScopeRequest BothScope(AgentTaskUnscopedMode mode = AgentTaskUnscopedMode.Exclude) =>
        new(ProjectX, BoardB1, mode);

    public static Project Project(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        GitRepositoryUrl = "https://example.test/" + name,
        CreatedAt = T,
        UpdatedAt = T,
    };

    public static Board Board(Guid id, Guid projectId, string name, DateTime? archivedAt = null) => new()
    {
        Id = id,
        ProjectId = projectId,
        Name = name,
        MaxConcurrentSessions = 1,
        CreatedAt = T,
        UpdatedAt = T,
        ArchivedAt = archivedAt,
    };

    public static BoardColumn Column(Guid boardId, string? stateKey = null) => new()
    {
        Id = Guid.NewGuid(),
        BoardId = boardId,
        StateKey = stateKey ?? $"col-{boardId:N}"[..20],
        Name = "Backlog",
        ColumnOrder = 0,
        CardStatus = CardStatus.Backlog,
        CreatedAt = T,
        UpdatedAt = T,
    };

    public static Card Card(Guid id, Guid boardId, Guid columnId, string identifier) => new()
    {
        Id = id,
        BoardId = boardId,
        BoardColumnId = columnId,
        Identifier = identifier,
        Title = identifier,
        Description = identifier,
        CreatedAt = T,
        UpdatedAt = T,
    };

    public static AgentTask TaskRow(
        Guid id,
        Guid? projectId,
        Guid? cardId,
        string workingDirectory,
        string? repoPath,
        DateTime createdAt,
        Guid rootTaskId,
        AgentTaskStatus status = AgentTaskStatus.Working,
        AgentTaskRole role = AgentTaskRole.Code,
        DateTime? completedAt = null,
        decimal costUsd = 0.01m)
    {
        return new AgentTask
        {
            Id = id,
            RootTaskId = rootTaskId,
            Title = id.ToString("N")[..8],
            Goal = "scoped list fixture",
            Kind = AgentTaskKind.Worker,
            Role = role,
            ProjectId = projectId,
            CardId = cardId,
            Status = status,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            RepoPath = repoPath,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
            CostUsd = costUsd,
        };
    }

    private static Guid[] Concat(Guid[] scoped, Guid[] extra) =>
        extra.Length == 0 ? scoped : [..scoped, ..extra];
}

/// <summary>Successful reader/scalar commands on the measured context.</summary>
public sealed class ScopeQueryCounter : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];

    public void Reset() => Commands.Clear();

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Commands.Add(command.CommandText);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return new ValueTask<DbDataReader>(result);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Commands.Add(command.CommandText);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return new ValueTask<object?>(result);
    }

    public int LabelSelects()
    {
        return Commands.Count(IsLabelSelect);
    }

    public static bool IsLabelSelect(string sql)
    {
        var text = sql.Replace("\"", "", StringComparison.Ordinal);
        var fromCards = text.Contains("FROM Cards", StringComparison.OrdinalIgnoreCase)
            || text.Contains("FROM Cards AS", StringComparison.OrdinalIgnoreCase);
        var fromProjects = text.Contains("FROM Projects", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("FROM Cards", StringComparison.OrdinalIgnoreCase);
        return fromCards || fromProjects;
    }
}
