using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Api;

/// <summary>
/// CARD-1320 S2. Open count is non-specialist Queued, Dispatched, and Working on the
/// agent's project. Shortfall is zero when that board has no dispatchable card.
/// </summary>
[Category("Integration")]
[NotInParallel("KeepSixShortfall")]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class KeepSixShortfallTests
{
    private static readonly DateTime T0 = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

    private readonly AntiphonWebAppFactory _factory;

    public KeepSixShortfallTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task legacy_open_counts_queued_dispatched_and_working_and_not_blocked()
    {
        var home = await BoardAsync("ks-open");
        await SetCeilingAsync(home.ProjectId, """{"mode":"LegacyOpen","maxParallel":6}""");
        var on = await AgentAsync("ks-open-on", home.BoardId);
        var off = await AgentAsync("ks-open-off", home.BoardId);
        await EnableAsync(on, 6);
        await HoldAsync(on, T0.AddDays(1));
        await CardAsync(home, "KS10-READY", CardImportance.Normal, CardUrgency.Normal);
        await TaskAsync(home.ProjectId, AgentTaskRole.Code, AgentTaskStatus.Queued, T0);
        await TaskAsync(home.ProjectId, AgentTaskRole.Code, AgentTaskStatus.Dispatched, T0.AddHours(1),
            dispatchedAt: T0.AddHours(10));
        await TaskAsync(home.ProjectId, AgentTaskRole.Code, AgentTaskStatus.Working, T0.AddHours(2));
        await TaskAsync(home.ProjectId, AgentTaskRole.Review, AgentTaskStatus.Blocked, T0.AddHours(3));
        await TaskAsync(home.ProjectId, AgentTaskRole.Plan, AgentTaskStatus.Succeeded, T0.AddHours(4),
            completedAt: T0.AddHours(5));
        await TaskAsync(home.ProjectId, AgentTaskRole.Check, AgentTaskStatus.Working, T0.AddHours(8));

        var enabled = await FindAsync(on);
        Count(enabled, "countedOpen").ShouldBe(3);
        Count(enabled, "effectiveTarget").ShouldBe(6);
        Count(enabled, "dispatchableBacklog").ShouldBe(1);
        Count(enabled, "shortfall").ShouldBe(3);
        enabled.GetProperty("badge").GetString().ShouldBe("under-target");
        enabled.GetProperty("holdReason").GetString().ShouldBe("model quota");
        Identifiers(enabled).ShouldBe(["KS10-READY"]);
        Instant(enabled, "lastDispatchAt").ShouldBe(T0.AddHours(4));
        Instant(enabled, "lastCompletionAt").ShouldBe(T0.AddHours(5));

        var disabled = await FindAsync(off);
        Count(disabled, "countedOpen").ShouldBe(3);
        Count(disabled, "shortfall").ShouldBe(3);
        disabled.GetProperty("badge").GetString().ShouldBe("off");
    }

    [Test]
    public async Task shortfall_is_zero_when_the_board_backlog_has_no_dispatchable_card()
    {
        var home = await BoardAsync("ks-empty");
        await SetCeilingAsync(home.ProjectId, """{"mode":"LegacyOpen","maxParallel":8}""");
        var agent = await AgentAsync("ks-empty-on", home.BoardId);
        await EnableAsync(agent, 6);
        await TaskAsync(home.ProjectId, AgentTaskRole.Code, AgentTaskStatus.Queued, T0);
        // A Done blocker is resolved (CARD-0100), so it would leave KS11-HELD dispatchable.
        // The open blocker is InProgress, and the Done card is only a non-candidate.
        await CardAsync(home, "KS11-DONE", CardImportance.Normal, CardUrgency.Normal,
            status: CardStatus.Done);
        var blocker = await CardAsync(home, "KS11-OPEN", CardImportance.Normal, CardUrgency.Normal,
            status: CardStatus.InProgress);
        var held = await CardAsync(home, "KS11-HELD", CardImportance.Critical, CardUrgency.Now);
        await BlockAsync(held, blocker);
        await CardAsync(home, "KS11-LABEL", CardImportance.Critical, CardUrgency.Now,
            labels: ["post-land-verification"]);
        var started = await CardAsync(home, "KS11-STARTED", CardImportance.Critical, CardUrgency.Now);
        await TaskAsync(home.ProjectId, AgentTaskRole.Code, AgentTaskStatus.Succeeded, T0,
            cardId: started);

        var row = await FindAsync(agent);
        Count(row, "countedOpen").ShouldBe(1);
        Count(row, "effectiveTarget").ShouldBe(6);
        Count(row, "dispatchableBacklog").ShouldBe(0);
        Count(row, "shortfall").ShouldBe(0);
        Identifiers(row).ShouldBe([]);
        row.GetProperty("badge").GetString().ShouldBe("healthy");
    }

    [Test]
    public async Task shortfall_uses_the_minimum_of_stored_target_and_the_absolute_cap()
    {
        var home = await BoardAsync("ks-cap");
        await SetCeilingAsync(home.ProjectId, """{"mode":"LegacyOpen","maxParallel":6}""");
        var high = await AgentAsync("ks-cap-high", home.BoardId);
        var low = await AgentAsync("ks-cap-low", home.BoardId);
        await EnableAsync(high, 10);
        await EnableAsync(low, 4);
        await TaskAsync(home.ProjectId, AgentTaskRole.Code, AgentTaskStatus.Queued, T0);
        await CardAsync(home, "KS12-READY", CardImportance.Normal, CardUrgency.Normal);

        var raised = await FindAsync(high);
        Count(raised, "countedOpen").ShouldBe(1);
        Count(raised, "effectiveTarget").ShouldBe(6);
        Count(raised, "shortfall").ShouldBe(5);

        var lowered = await FindAsync(low);
        Count(lowered, "countedOpen").ShouldBe(1);
        Count(lowered, "effectiveTarget").ShouldBe(4);
        Count(lowered, "shortfall").ShouldBe(3);
    }

    [Test]
    public async Task other_projects_and_other_boards_do_not_add_dispatchable_cards()
    {
        var home = await BoardAsync("ks-scope");
        var otherBoard = await BoardAsync("ks-scope-other", home.ProjectId);
        var foreign = await BoardAsync("ks-scope-foreign");
        await SetCeilingAsync(home.ProjectId, """{"mode":"LegacyOpen","maxParallel":6}""");
        await SetCeilingAsync(foreign.ProjectId, """{"mode":"LegacyOpen","maxParallel":6}""");
        var agent = await AgentAsync("ks-scope-on", home.BoardId);
        await EnableAsync(agent, 6);
        await TaskAsync(home.ProjectId, AgentTaskRole.Code, AgentTaskStatus.Queued, T0);
        await TaskAsync(home.ProjectId, AgentTaskRole.Plan, AgentTaskStatus.Working, T0.AddHours(1));
        await TaskAsync(foreign.ProjectId, AgentTaskRole.Code, AgentTaskStatus.Queued, T0);
        await TaskAsync(foreign.ProjectId, AgentTaskRole.Code, AgentTaskStatus.Dispatched, T0);
        await TaskAsync(foreign.ProjectId, AgentTaskRole.Code, AgentTaskStatus.Working, T0);
        await CardAsync(home, "KS13-HOME", CardImportance.Critical, CardUrgency.Now);
        await CardAsync(otherBoard, "KS13-OTHER", CardImportance.Critical, CardUrgency.Now);
        await CardAsync(foreign, "KS13-FOREIGN", CardImportance.Critical, CardUrgency.Now);

        var row = await FindAsync(agent);
        Count(row, "countedOpen").ShouldBe(2);
        Count(row, "dispatchableBacklog").ShouldBe(1);
        Identifiers(row).ShouldBe(["KS13-HOME"]);
    }

    [Test]
    public async Task separate_queues_caps_the_target_by_parallel_plus_queued_ceiling()
    {
        var summed = await BoardAsync("ks-sq-sum");
        var parallelOnly = await BoardAsync("ks-sq-parallel");
        await SetCeilingAsync(summed.ProjectId,
            """{"mode":"SeparateQueues","maxParallel":2,"maxQueued":4}""");
        await SetCeilingAsync(parallelOnly.ProjectId,
            """{"mode":"SeparateQueues","maxParallel":2,"maxQueued":null}""");
        var summedAgent = await AgentAsync("ks-sq-sum-on", summed.BoardId);
        var parallelAgent = await AgentAsync("ks-sq-parallel-on", parallelOnly.BoardId);
        await EnableAsync(summedAgent, 10);
        await EnableAsync(parallelAgent, 10);
        await CardAsync(summed, "KS14-SUM", CardImportance.Normal, CardUrgency.Normal);
        await CardAsync(parallelOnly, "KS14-PAR", CardImportance.Normal, CardUrgency.Normal);

        var withQueue = await FindAsync(summedAgent);
        Count(withQueue, "countedOpen").ShouldBe(0);
        Count(withQueue, "effectiveTarget").ShouldBe(6);
        Count(withQueue, "shortfall").ShouldBe(6);

        var withoutQueue = await FindAsync(parallelAgent);
        Count(withoutQueue, "countedOpen").ShouldBe(0);
        Count(withoutQueue, "effectiveTarget").ShouldBe(2);
        Count(withoutQueue, "shortfall").ShouldBe(2);
    }

    [Test]
    public async Task top_cards_are_the_five_lowest_rank_unblocked_backlog_cards_on_that_board()
    {
        var home = await BoardAsync("ks-rank");
        await SetCeilingAsync(home.ProjectId, """{"mode":"LegacyOpen","maxParallel":6}""");
        var agent = await AgentAsync("ks-rank-on", home.BoardId);
        await EnableAsync(agent, 6);
        await CardAsync(home, "KS15-A", CardImportance.Critical, CardUrgency.Now);
        await CardAsync(home, "KS15-B", CardImportance.Critical, CardUrgency.Soon);
        await CardAsync(home, "KS15-C", CardImportance.High, CardUrgency.Now);
        await CardAsync(home, "KS15-D", CardImportance.Critical, CardUrgency.Normal);
        await CardAsync(home, "KS15-E", CardImportance.High, CardUrgency.Soon);
        await CardAsync(home, "KS15-F", CardImportance.Normal, CardUrgency.Now);
        var blocker = await CardAsync(home, "KS15-BLOCKER", CardImportance.Normal, CardUrgency.Normal,
            status: CardStatus.InProgress);
        var held = await CardAsync(home, "KS15-HELD", CardImportance.Critical, CardUrgency.Now);
        await BlockAsync(held, blocker);
        var doneBlocker = await CardAsync(home, "KS15-DONE-BLOCKER", CardImportance.Normal, CardUrgency.Normal,
            status: CardStatus.Done);
        var released = await CardAsync(home, "KS15-RELEASED", CardImportance.High, CardUrgency.Normal);
        await BlockAsync(released, doneBlocker);
        await CardAsync(home, "KS15-LABEL", CardImportance.Critical, CardUrgency.Now,
            labels: ["post-land-verification"]);

        var row = await FindAsync(agent);
        Identifiers(row).ShouldBe(["KS15-A", "KS15-B", "KS15-C", "KS15-D", "KS15-E"]);
        Count(row, "dispatchableBacklog").ShouldBe(7);
        Identifiers(row).ShouldNotContain("KS15-F");
        Identifiers(row).ShouldNotContain("KS15-HELD");
        Identifiers(row).ShouldNotContain("KS15-LABEL");
    }

    private async Task<SeedBoard> BoardAsync(string name, Guid? projectId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var project = projectId ?? Guid.NewGuid();
        if (projectId is null)
        {
            db.Projects.Add(new Project
            {
                Id = project,
                Name = name + " " + project.ToString("N")[..8],
                GitRepositoryUrl = "https://example.test/" + project.ToString("N"),
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        db.Boards.Add(new Board
        {
            Id = boardId,
            ProjectId = project,
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.BoardColumns.Add(new BoardColumn
        {
            Id = columnId,
            BoardId = boardId,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return new SeedBoard(project, boardId, columnId);
    }

    private async Task<Guid> AgentAsync(string name, Guid boardId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Agents.Add(new Agent
        {
            Id = id,
            Name = name + "-" + id.ToString("N")[..8],
            Slug = "ks2-" + id.ToString("N"),
            WorkingDirectory = "/tmp/keep-six",
            BoardId = boardId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.AgentBundleAttachments.Add(new AgentBundleAttachment
        {
            AgentId = id,
            BundleKey = InstructionBundles.Orchestrator,
            Position = 0,
            CreatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> CardAsync(
        SeedBoard board,
        string identifier,
        CardImportance importance,
        CardUrgency urgency,
        CardStatus status = CardStatus.Backlog,
        string[]? labels = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        db.Cards.Add(new Card
        {
            Id = id,
            BoardId = board.BoardId,
            BoardColumnId = board.ColumnId,
            Identifier = identifier,
            Title = identifier,
            Description = "keep-six candidate",
            Status = status,
            Importance = importance,
            Urgency = urgency,
            LabelsJson = JsonSerializer.Serialize(labels ?? []),
            TerminalReason = status is CardStatus.Done or CardStatus.Canceled ? "keep-six fixture" : null,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task BlockAsync(Guid cardId, Guid blockerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CardRelationships.Add(new CardRelationship
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            RelatedCardId = blockerId,
            Kind = CardRelationshipKind.BlockedBy,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task TaskAsync(
        Guid projectId,
        AgentTaskRole role,
        AgentTaskStatus status,
        DateTime createdAt,
        DateTime? completedAt = null,
        DateTime? dispatchedAt = null,
        Guid? cardId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ProjectId = projectId,
            Role = role,
            Status = status,
            Title = "keep-six " + status,
            Goal = "keep-six shortfall",
            CardId = cardId,
            Workspace = WorkspaceMode.ReadOnly,
            WorkingDirectory = "/tmp/keep-six",
            CreatedAt = createdAt,
            DispatchedAt = dispatchedAt ?? (status == AgentTaskStatus.Queued ? null : createdAt),
            CompletedAt = completedAt,
        });
        await db.SaveChangesAsync();
    }

    private async Task EnableAsync(Guid agentId, int target)
    {
        using var client = _factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/orchestrators/{agentId}/keep-six", new
        {
            expectedRevision = 0,
            enabled = true,
            target,
            reason = "operator toggle",
            provenance = "Human",
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task HoldAsync(Guid agentId, DateTime until)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.KeepSixOrchestrators.SingleAsync(candidate => candidate.AgentId == agentId);
        row.HoldReason = "model quota";
        row.HoldUntil = until;
        await db.SaveChangesAsync();
    }

    private async Task SetCeilingAsync(Guid projectId, string overridesJson)
    {
        ConflictException? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<DispatchConcurrencySettingsService>();
            var current = await settings.GetProjectAsync(projectId, CancellationToken.None);
            using var document = JsonDocument.Parse(overridesJson);
            try
            {
                await settings.PutProjectAsync(
                    projectId,
                    new PutDispatchConcurrencyRequest(
                        current.Revision,
                        current.GlobalRevision,
                        document.RootElement.Clone(),
                        "keep-six shortfall fixture",
                        "Human"),
                    null,
                    CancellationToken.None);
                return;
            }
            catch (ConflictException ex)
            {
                last = ex;
            }
        }

        throw last!;
    }

    private async Task<JsonElement> FindAsync(Guid agentId)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/orchestrators/keep-six");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in body.EnumerateArray())
        {
            if (item.GetProperty("agentId").GetGuid() == agentId)
                return item;
        }

        throw new ShouldAssertException($"GET /api/orchestrators/keep-six did not list {agentId}.");
    }

    private static int Count(JsonElement row, string name)
    {
        var value = row.GetProperty(name);
        value.ValueKind.ShouldBe(JsonValueKind.Number, $"{name} is not filled");
        return value.GetInt32();
    }

    private static string[] Identifiers(JsonElement row)
    {
        var value = row.GetProperty("topCards");
        value.ValueKind.ShouldBe(JsonValueKind.Array, "topCards is not filled");
        return value.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static DateTime Instant(JsonElement row, string name)
    {
        var value = row.GetProperty(name);
        value.ValueKind.ShouldBe(JsonValueKind.String, $"{name} is not filled");
        return value.GetDateTimeOffset().UtcDateTime;
    }

    private readonly record struct SeedBoard(Guid ProjectId, Guid BoardId, Guid ColumnId);
}
