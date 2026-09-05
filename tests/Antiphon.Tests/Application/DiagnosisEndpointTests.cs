using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0352 S4 — POST /api/cards/{id}/diagnose and GET /api/diagnoses[/stats] over the real host.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class DiagnosisEndpointTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public DiagnosisEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [After(Test)]
    public async Task CleanupAsync()
    {
        _factory.Services.GetRequiredService<IOptions<DelegationSettings>>().Value.DiagnoseEnabled = false;
        DrainQueue();

        if (_projectId == Guid.Empty)
            return;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boardIds = await db.Boards.Where(b => b.ProjectId == _projectId).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.Diagnoses.Where(d => d.CardId != null && cardIds.Contains(d.CardId.Value)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => b.Id != Guid.Empty && boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task Post_diagnose_is_409_when_disabled()
    {
        var (_, card) = await SeedAsync("Diagnose disabled board", "Unlabelled", "Body.");
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/cards/{card.Id}/diagnose", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        DrainQueue();
        _factory.Services.GetRequiredService<DiagnoseQueue>().TryDequeue(out _).ShouldBeFalse();
    }

    [Test]
    public async Task Post_diagnose_is_404_for_an_unknown_card()
    {
        EnableDiagnose();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/cards/{Guid.NewGuid()}/diagnose", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        DrainQueue();
    }

    [Test]
    public async Task Post_diagnose_is_202_and_queues_a_forced_request()
    {
        EnableDiagnose();
        var (_, card) = await SeedAsync("Diagnose queue board", "Unlabelled", "Body.");
        DrainQueue();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/cards/{card.Id}/diagnose", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = (await response.Content.ReadFromJsonAsync<DiagnoseQueuedDto>(Json))!;
        body.Queued.ShouldBeTrue();

        var queue = _factory.Services.GetRequiredService<DiagnoseQueue>();
        queue.TryDequeue(out var request).ShouldBeTrue();
        request.Kind.ShouldBe(DiagnosisKind.Labels);
        request.CardId.ShouldBe(card.Id);
        request.Forced.ShouldBeTrue();
    }

    [Test]
    public async Task Get_diagnoses_by_cardId_is_newest_first()
    {
        var (_, card) = await SeedAsync("Diagnose list board", "Listed", "Body.");
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        var now = DateTime.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Diagnoses.AddRange(
                new DiagnosisRecord
                {
                    Id = older,
                    Kind = DiagnosisKind.Labels,
                    CardId = card.Id,
                    Outcome = DiagnosisOutcome.Unclear,
                    CreatedAt = now.AddMinutes(-5),
                },
                new DiagnosisRecord
                {
                    Id = newer,
                    Kind = DiagnosisKind.Labels,
                    CardId = card.Id,
                    Outcome = DiagnosisOutcome.Applied,
                    Applied = "complexity=easy ui=no",
                    CreatedAt = now.AddMinutes(-1),
                });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var rows = await client.GetFromJsonAsync<List<DiagnosisDto>>(
            $"/api/diagnoses?cardId={card.Id}", Json);

        rows.ShouldNotBeNull();
        rows.Count.ShouldBeGreaterThanOrEqualTo(2);
        rows[0].Id.ShouldBe(newer);
        rows[1].Id.ShouldBe(older);
        rows[0].CardIdentifier.ShouldBe(card.Identifier);
        rows[0].Outcome.ShouldBe(DiagnosisOutcome.Applied);
    }

    [Test]
    public async Task Get_stats_counts_by_kind_and_outcome_match_seeded_rows()
    {
        var (_, card) = await SeedAsync("Diagnose stats board", "Counted", "Body.");
        var now = DateTime.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Diagnoses.AddRange(
                Row(card.Id, DiagnosisKind.Labels, DiagnosisOutcome.Applied, now, "complexity=medium ui=no", 10),
                Row(card.Id, DiagnosisKind.Labels, DiagnosisOutcome.Applied, now, "complexity=hard ui=yes", 20),
                Row(card.Id, DiagnosisKind.Labels, DiagnosisOutcome.Unclear, now, null, 30),
                Row(card.Id, DiagnosisKind.Title, DiagnosisOutcome.Applied, now, "Plan the seat", 40));
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var stats = await client.GetFromJsonAsync<DiagnosisStatsDto>(
            $"/api/diagnoses/stats?since={Uri.EscapeDataString(now.AddMinutes(-1).ToString("O"))}", Json);

        stats.ShouldNotBeNull();
        stats.Total.ShouldBeGreaterThanOrEqualTo(4);
        stats.Counts.ShouldContain(c =>
            c.Kind == DiagnosisKind.Labels && c.Outcome == DiagnosisOutcome.Applied && c.Count >= 2);
        stats.Counts.ShouldContain(c =>
            c.Kind == DiagnosisKind.Labels && c.Outcome == DiagnosisOutcome.Unclear && c.Count >= 1);
        stats.Counts.ShouldContain(c =>
            c.Kind == DiagnosisKind.Title && c.Outcome == DiagnosisOutcome.Applied && c.Count >= 1);
        stats.LabelDistribution.ShouldContainKey("complexity:medium");
        stats.LabelDistribution.ShouldContainKey("ui:no");
    }

    private void EnableDiagnose() =>
        _factory.Services.GetRequiredService<IOptions<DelegationSettings>>().Value.DiagnoseEnabled = true;

    private void DrainQueue()
    {
        var queue = _factory.Services.GetRequiredService<DiagnoseQueue>();
        while (queue.TryDequeue(out _)) { }
    }

    private static DiagnosisRecord Row(
        Guid cardId,
        DiagnosisKind kind,
        DiagnosisOutcome outcome,
        DateTime createdAt,
        string? applied,
        int waitMs) =>
        new()
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            CardId = cardId,
            Outcome = outcome,
            Applied = applied,
            WaitMs = waitMs,
            CreatedAt = createdAt,
        };

    private async Task<(BoardDetailDto Board, CardDto Card)> SeedAsync(
        string boardName, string cardTitle, string cardDescription)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Diagnose Api Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-diag-api-{Guid.NewGuid():N}"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var board = await boards.CreateAsync(
            new CreateBoardRequest(project.Id, boardName), CancellationToken.None);
        var card = await cards.CreateAsync(
            board.Id, new CreateCardRequest(null, cardTitle, cardDescription), CancellationToken.None);
        return (board, card);
    }
}
