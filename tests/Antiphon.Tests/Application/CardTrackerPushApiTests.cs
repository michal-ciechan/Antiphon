using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0347 S2: PATCH /api/cards/{id} carries trackerPush only when the card is linked.</summary>
[NotInParallel]
[ClassDataSource<CardTrackerPushWebAppFactory>(Shared = SharedType.PerClass)]
[Category("Integration")]
public class CardTrackerPushApiTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CardTrackerPushWebAppFactory _factory;
    private Guid _projectId;

    public CardTrackerPushApiTests(CardTrackerPushWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [After(Test)]
    public async Task CleanupAsync()
    {
        if (_projectId == Guid.Empty)
            return;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boardIds = await db.Boards.Where(b => b.ProjectId == _projectId).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task Unlinked_card_patch_json_has_no_trackerPush_property()
    {
        var seeded = await SeedAsync(linked: false);
        using var client = _factory.CreateClient();

        var patch = await client.PatchAsJsonAsync(
            $"/api/cards/{seeded.Card.Id}",
            new MoveCardRequest(seeded.DoneColumnId, seeded.Card.ConcurrencyToken, "Closed."));
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await patch.Content.ReadAsStringAsync();
        json.ShouldNotContain("trackerPush");
    }

    [Test]
    public async Task Linked_card_patch_json_has_outcome_Closed()
    {
        var seeded = await SeedAsync(linked: true);
        _factory.Tracker.Candidates =
        [
            new TrackedIssue(
                seeded.ExternalId, seeded.ExternalKey, "Title", "Body", "open", 0,
                ["status:backlog"], [], seeded.Url, "{}")
        ];
        using var client = _factory.CreateClient();

        var patch = await client.PatchAsJsonAsync(
            $"/api/cards/{seeded.Card.Id}",
            new MoveCardRequest(seeded.DoneColumnId, seeded.Card.ConcurrencyToken, "Shipped."));
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await patch.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("trackerPush").GetProperty("outcome").GetString()
            .ShouldBe("Closed");
    }

    private async Task<Seeded> SeedAsync(bool linked)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var n = Random.Shared.Next(10_000, 99_999);
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"PushApi Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-push-api-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"PushApi Board {n}",
            TrackerKind = linked ? TrackerKind.GitHubIssues : TrackerKind.Internal,
            TrackerActivatedAt = linked ? now : null,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        var backlog = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            IsActive = false,
            IsTerminal = false,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        var done = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "done",
            Name = "Done",
            ColumnOrder = 1,
            CardStatus = CardStatus.Done,
            IsActive = false,
            IsTerminal = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.Columns.Add(backlog);
        board.Columns.Add(done);

        if (linked)
        {
            board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                Version = 1,
                Name = "Tracked",
                Content = string.Join('\n',
                    "---",
                    "tracker:",
                    "  kind: github_issues",
                    "  repository: acme/app",
                    "  active_states: [open]",
                    "---",
                    "Work."),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                Board = board
            });
        }

        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = backlog.Id,
            Identifier = $"CARD-{n}",
            Title = $"Api push {n}",
            Description = "desc",
            Importance = CardImportance.Normal,
            LabelsJson = "[]",
            Status = CardStatus.Backlog,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
            BoardColumn = backlog
        };
        db.Projects.Add(project);
        db.Cards.Add(card);

        var externalId = $"acme/app#{n}";
        var externalKey = $"#{n}";
        var url = $"https://github.test/acme/app/issues/{n}";
        if (linked)
        {
            db.ExternalIssueRefs.Add(new ExternalIssueRef
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                TrackerKind = TrackerKind.GitHubIssues,
                ExternalId = externalId,
                ExternalKey = externalKey,
                Url = url,
                RawPayloadJson = "{}",
                LastSyncedAt = now,
                Origin = ExternalIssueOrigin.ExternalImport,
                LastKnownExternalState = "open",
                LastRevisionSynced = 0,
                Card = card
            });
        }

        await db.SaveChangesAsync();
        _projectId = project.Id;
        return new Seeded(card, done.Id, externalId, externalKey, url);
    }

    private sealed record Seeded(Card Card, Guid DoneColumnId, string ExternalId, string ExternalKey, string Url);
}

public sealed class CardTrackerPushWebAppFactory : AntiphonWebAppFactory
{
    internal FakeBidirectionalTracker Tracker { get; } = new(TrackerKind.GitHubIssues);

    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        var existing = services.Where(d => d.ServiceType == typeof(IIssueTracker)).ToList();
        foreach (var d in existing)
            services.Remove(d);

        services.AddSingleton(Tracker);
        services.AddScoped<IIssueTracker>(_ => Tracker);
    }
}
