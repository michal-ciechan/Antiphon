using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0710 V-2. Platform columns survive migration, retry and a fresh context.</summary>
[Category("Integration")]
public sealed class TaskPlatformPersistenceTests
{
    [Test]
    public async Task Legacy_rows_migrate_to_any()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        var platform = Array.FindIndex(migrations, name => name.EndsWith("_AddTaskPlatformPlacement", StringComparison.Ordinal));
        platform.ShouldBeGreaterThan(0);
        await db.GetService<IMigrator>().MigrateAsync(migrations[platform - 1]);

        var taskId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AgentTasks" ("Id", "RootTaskId", "Depth", "Title", "Goal", "Kind", "Role",
                "ModelLevel", "Attempt", "MaxAttempts", "WorkingDirectory", "Ephemeral", "Status", "ReplyTo",
                "ConcurrencyToken", "CreatedAt", "TokensIn", "TokensOut", "CostUsd", "Workspace", "WorktreeBranch", "WorktreePath")
            VALUES ({taskId}, {taskId}, 0, 'legacy', 'legacy fixture', 0, 0, 0, 0, 1, 'fixture', false,
                {(int)AgentTaskStatus.Succeeded}, 0, {Guid.NewGuid()}, {now}, 0, 0, 0,
                {(int)WorkspaceMode.Worktree}, 'feat/legacy', 'legacy-fixture')
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Projects" ("Id", "Name", "GitRepositoryUrl", "GitHubIntegrationEnabled",
                "NotificationsEnabled", "CreatedAt", "UpdatedAt")
            VALUES ({projectId}, {"c710-legacy"}, {"https://example.test/c710.git"}, false, false, {now}, {now})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Boards" ("Id", "ProjectId", "Name", "Description", "TrackerKind",
                "MaxConcurrentSessions", "CreatedAt", "UpdatedAt")
            VALUES ({boardId}, {projectId}, {"c710"}, {""}, 0, 1, {now}, {now})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "BoardColumns" ("Id", "BoardId", "StateKey", "Name", "ColumnOrder", "CardStatus",
                "IsActive", "IsTerminal", "CreatedAt", "UpdatedAt")
            VALUES ({columnId}, {boardId}, {"ready"}, {"Ready"}, 0, 0, false, false, {now}, {now})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Cards" ("Id", "BoardId", "BoardColumnId", "Identifier", "Title", "Description",
                "PrivateNotes", "CardFileVisibility", "Importance", "LabelsJson", "Status", "ConcurrencyToken",
                "CreatedAt", "UpdatedAt", "RevisionCount")
            VALUES ({cardId}, {boardId}, {columnId}, {"CARD-0710"}, {"legacy"}, {""}, {""}, {"Inherit"},
                0, {"[]"}::jsonb, 0, {Guid.NewGuid()}, {now}, {now}, 0)
            """);

        await db.GetService<IMigrator>().MigrateAsync();
        await using var read = new AppDbContext(options);
        var task = await read.AgentTasks.SingleAsync(row => row.Id == taskId);
        task.RequiredPlatform.ShouldBe(RequiredPlatform.Any);
        task.RequirementSource.ShouldBe(RequirementSource.Default);
        task.ObservedPlatform.ShouldBeNull();
        (await read.Cards.SingleAsync(row => row.Id == cardId)).RequiredPlatform.ShouldBe(RequiredPlatform.Any);
    }

    [Test]
    public async Task Specific_requirement_and_observation_round_trip()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var directory = new PlacementDirectory("linux");
        kit.RealDirectory = directory;
        await using var db = kit.Context();
        var created = await kit.Service(db).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 round trip",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.Grok,
                Workspace: WorkspaceMode.Worktree,
                RunnerId: "server2",
                RequiredPlatform: RequiredPlatform.Linux),
            kit.Caller, CancellationToken.None);
        directory.Platform = "windows";

        await using var read = kit.Context();
        var task = await read.AgentTasks.SingleAsync(row => row.Id == created.Id);
        task.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        task.RequirementSource.ShouldBe(RequirementSource.Request);
        task.ObservedPlatform.ShouldBe("linux");
        task.RunnerId.ShouldBe("server2");
    }

    [Test]
    public async Task Retry_and_continuations_keep_frozen_requirement()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = new PlacementDirectory("windows");
        await using var db = kit.Context();
        var created = await kit.Service(db).CreateAsync(
            new CreateAgentTaskRequest(
                "c710 retry keeps windows",
                Role: AgentTaskRole.Code,
                AgentKind: AgentKind.ClaudeCode,
                Workspace: WorkspaceMode.Shared,
                RunnerId: "desktop",
                RequiredPlatform: RequiredPlatform.Windows),
            kit.Caller, CancellationToken.None);
        await using (var fail = kit.Context())
        {
            await fail.AgentTasks.Where(row => row.Id == created.Id).ExecuteUpdateAsync(update => update
                .SetProperty(row => row.Status, AgentTaskStatus.Failed)
                .SetProperty(row => row.FailureReason, "retry me"));
        }

        await using var retryDb = kit.Context();
        var retried = await kit.Service(retryDb).RetryAsync(created.Id, CancellationToken.None);
        retried.RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        await using var afterRetry = kit.Context();
        var stored = await afterRetry.AgentTasks.SingleAsync(row => row.Id == created.Id);
        stored.RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        stored.RequirementSource.ShouldBe(RequirementSource.Request);
        stored.ObservedPlatform.ShouldBe("windows");
        stored.Status.ShouldBe(AgentTaskStatus.Queued);

        await afterRetry.AgentTasks.Where(row => row.Id == created.Id).ExecuteUpdateAsync(update => update
            .SetProperty(row => row.Status, AgentTaskStatus.Succeeded));
        await using var followDb = kit.Context();
        var follow = await kit.Service(followDb).CreateAsync(
            new CreateAgentTaskRequest("c710 continuation", FollowUpOnTask: created.Id.ToString("D")),
            kit.Caller, CancellationToken.None);
        await using var followRead = kit.Context();
        var continued = await followRead.AgentTasks.SingleAsync(row => row.Id == follow.Id);
        continued.RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        continued.RequirementSource.ShouldBe(RequirementSource.FollowUp);
        (await followRead.AgentTasks.SingleAsync(row => row.Id == created.Id)).RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
    }
}

/// <summary>CARD-0710 V-2. Card default, history and task binding stay distinct.</summary>
[Category("Integration")]
public sealed class CardPlatformTests
{
    [Test]
    public async Task Create_edit_reset_and_history()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var boardId = await SeedBoardAsync(kit);
        await using var db = kit.Context();
        var cards = Cards(db);
        var created = await cards.CreateAsync(boardId, new CreateCardRequest(null, "platform card", RequiredPlatform: RequiredPlatform.Windows), CancellationToken.None);
        created.RequiredPlatform.ShouldBe(RequiredPlatform.Windows);

        var edited = await cards.UpdateContentAsync(created.Id, new UpdateCardContentRequest(
            created.ConcurrencyToken, "Linux evidence", RequiredPlatform: RequiredPlatform.Linux), CancellationToken.None);
        edited.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        var reset = await cards.UpdateContentAsync(edited.Id, new UpdateCardContentRequest(
            edited.ConcurrencyToken, "Reset to any host", RequiredPlatform: RequiredPlatform.Any), CancellationToken.None);
        reset.RequiredPlatform.ShouldBe(RequiredPlatform.Any);

        var history = await db.CardRevisions.AsNoTracking()
            .Where(row => row.CardId == created.Id && row.Kind == CardRevisionKind.ContentEdit)
            .OrderBy(row => row.RevisionNumber)
            .ToListAsync();
        history.Count.ShouldBe(2);
        history[0].RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        history[1].RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
    }

    [Test]
    public async Task Task_binding_precedence_and_snapshot()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        kit.RealDirectory = new PlacementDirectory("linux");
        var boardId = await SeedBoardAsync(kit);
        await using var cardDb = kit.Context();
        var card = await Cards(cardDb).CreateAsync(
            boardId, new CreateCardRequest(null, "binding card", RequiredPlatform: RequiredPlatform.Windows), CancellationToken.None);

        await using var createDb = kit.Context();
        var service = kit.Service(createDb);
        var inherited = await service.CreateAsync(new CreateAgentTaskRequest(
            "inherit the card", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok,
            Workspace: WorkspaceMode.Worktree, Card: card.Identifier), kit.Caller, CancellationToken.None);
        inherited.RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        inherited.RequirementSource.ShouldBe(RequirementSource.Card);
        var explicitAny = await service.CreateAsync(new CreateAgentTaskRequest(
            "explicit any", Role: AgentTaskRole.Code, AgentKind: AgentKind.Grok,
            Workspace: WorkspaceMode.Worktree, Card: card.Identifier, RequiredPlatform: RequiredPlatform.Any),
            kit.Caller, CancellationToken.None);
        explicitAny.RequiredPlatform.ShouldBe(RequiredPlatform.Any);
        explicitAny.RequirementSource.ShouldBe(RequirementSource.Request);

        await using var doneDb = kit.Context();
        await doneDb.AgentTasks.Where(row => row.Id == inherited.Id).ExecuteUpdateAsync(update => update
            .SetProperty(row => row.Status, AgentTaskStatus.Succeeded));
        await using var followDb = kit.Context();
        var follow = await kit.Service(followDb).CreateAsync(
            new CreateAgentTaskRequest("follow the frozen windows task", FollowUpOnTask: inherited.Id.ToString("D")),
            kit.Caller, CancellationToken.None);
        follow.RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        follow.RequirementSource.ShouldBe(RequirementSource.FollowUp);

        await using var editDb = kit.Context();
        await Cards(editDb).UpdateContentAsync(card.Id, new UpdateCardContentRequest(
            card.ConcurrencyToken, "Later card default must not move a queued task", RequiredPlatform: RequiredPlatform.Linux),
            CancellationToken.None);
        await using var read = kit.Context();
        (await read.AgentTasks.SingleAsync(row => row.Id == inherited.Id)).RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        (await read.AgentTasks.SingleAsync(row => row.Id == explicitAny.Id)).RequiredPlatform.ShouldBe(RequiredPlatform.Any);
        (await read.AgentTasks.SingleAsync(row => row.Id == follow.Id)).RequiredPlatform.ShouldBe(RequiredPlatform.Windows);
        (await read.Cards.SingleAsync(row => row.Id == card.Id)).RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
    }

    [Test]
    public async Task Invalid_and_stale_edits_leave_content_unchanged()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var boardId = await SeedBoardAsync(kit);
        await using var db = kit.Context();
        var cards = Cards(db);
        var created = await cards.CreateAsync(
            boardId, new CreateCardRequest(null, "stable card", RequiredPlatform: RequiredPlatform.Linux), CancellationToken.None);

        await Should.ThrowAsync<ValidationException>(() => cards.UpdateContentAsync(created.Id, new UpdateCardContentRequest(
            created.ConcurrencyToken, "not a platform", RequiredPlatform: (RequiredPlatform)99), CancellationToken.None));
        await Should.ThrowAsync<ConflictException>(() => cards.UpdateContentAsync(created.Id, new UpdateCardContentRequest(
            Guid.NewGuid(), "stale token", RequiredPlatform: RequiredPlatform.Windows), CancellationToken.None));

        await using var read = kit.Context();
        var card = await read.Cards.SingleAsync(row => row.Id == created.Id);
        card.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        card.Title.ShouldBe("stable card");
        card.ConcurrencyToken.ShouldBe(created.ConcurrencyToken);
        (await read.CardRevisions.CountAsync(row => row.CardId == created.Id)).ShouldBe(0);
    }

    private static CardService Cards(AppDbContext db) =>
        new(db, null!, null!, null!, new MockEventBus(), TimeProvider.System, null!);

    private static async Task<Guid> SeedBoardAsync(DefaultRunnerKit kit)
    {
        await using var db = kit.Context();
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "c710 " + Guid.NewGuid().ToString("N")[..8],
            GitRepositoryUrl = "https://example.test/c710.git",
            LocalRepositoryPath = kit.RepoRoot,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "c710",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Name = "Ready",
            StateKey = "ready",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        db.Boards.Add(board);
        db.BoardColumns.Add(column);
        await db.SaveChangesAsync();
        return board.Id;
    }
}

file sealed class PlacementDirectory : ISessionRunnerDirectory
{
    public PlacementDirectory(string platform) => Platform = platform;
    public string Platform { get; set; }
    public ISessionRunnerClient Local { get; } = new PhoneHomeTestHost.RecordingLocalClient();
    public IReadOnlyList<string> KnownRunnerIds => ["server2"];
    public Guid? GetLiveStoreId(string? runnerId) => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
    {
        var id = string.IsNullOrWhiteSpace(runnerId) || RunnerRequestIntent.IsDesktopAlias(runnerId) ? "desktop" : runnerId.Trim();
        return Task.FromResult<RunnerDescriptor?>(new RunnerDescriptor(
            id, id, Platform, DateTimeOffset.UtcNow, true, true, false, 4,
            new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                Features: [RunnerPlatformWire.Feature], Platform: Platform)));
    }
    public ISessionRunnerClient Resolve(string? runnerId) => Local;
    public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult<SessionRunnerOwner?>(null);
    public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
    public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
        Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("not asked"));
}
