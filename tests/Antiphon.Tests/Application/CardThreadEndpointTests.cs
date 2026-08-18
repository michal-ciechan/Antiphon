using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The two new routes over real HTTP: <c>GET /api/cards/{id}/thread</c> and <c>GET /api/plans</c>.
///
/// <para>Service-level tests cannot see any of what is pinned here — that the services are
/// registered at all, that the thread route takes the identifier a human writes the way CARD-0051
/// made every other card route take it, and that the plan reader's refusal arrives as a 422 rather
/// than a 500 or a leaked file.</para>
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class CardThreadEndpointTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _projectId;

    public CardThreadEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

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
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Id == _projectId).ExecuteDeleteAsync();
        _projectId = Guid.Empty;
    }

    [Test]
    public async Task The_thread_route_answers_to_every_form_of_the_identifier()
    {
        // The id a caller SEES must be the id the API TAKES (CARD-0051). A thread is the surface a
        // human reaches for by name — "#67" — so a route that only took a guid would put back
        // exactly the lookup round trip that card landed to remove.
        var (cardId, identifier) = await SeedCardAsync();
        var number = int.Parse(identifier["CARD-".Length..]);
        using var client = _factory.CreateClient();

        // The "#67" form is escaped, not because the route is fussy but because a bare '#' in a URL
        // is a fragment delimiter — it never reaches the server at all.
        foreach (var form in new[] { identifier, identifier.ToLowerInvariant(), $"#{number}", $"{number}", $"{cardId}" })
        {
            var thread = await client.GetFromJsonAsync<CardThreadDto>(
                $"/api/cards/{Uri.EscapeDataString(form)}/thread", Json);

            thread.ShouldNotBeNull($"'{form}' names {identifier}");
            thread!.Card.Id.ShouldBe(cardId);
            thread.Identifier.ShouldBe(identifier);
        }
    }

    [Test]
    public async Task A_thread_for_a_card_with_no_checkout_says_nobody_asked()
    {
        // The seeded project names a repository path that does not exist on this machine, which is
        // the ordinary state of a card whose worktree has been cleaned up.
        var (_, identifier) = await SeedCardAsync();
        using var client = _factory.CreateClient();

        var thread = await client.GetFromJsonAsync<CardThreadDto>($"/api/cards/{identifier}/thread", Json);

        thread!.ReposConsulted.ShouldBeFalse();
        thread.RepoRoot.ShouldBeNull();
        thread.Commits.ShouldBeEmpty();
    }

    [Test]
    public async Task A_task_row_carries_its_agent_kind_on_the_wire()
    {
        // Read as RAW json, not through the DTO: the client mirrors the wire shape by hand, and a
        // round trip through the same record it is being compared against cannot notice a missing
        // property. The tier badge on this surface is rendered from it — without the kind, a Grok
        // task reads `opus`, naming a model nobody was paying for (CARD-0084 S4).
        var (_, identifier) = await SeedCardAsync();
        var taskId = await AddGrokTaskAsync(identifier);
        try
        {
            using var client = _factory.CreateClient();

            var json = await client.GetStringAsync($"/api/cards/{identifier}/thread");

            using var document = JsonDocument.Parse(json);
            var row = document.RootElement.GetProperty("tasks").EnumerateArray()
                .Single(t => Guid.Parse(t.GetProperty("id").GetString()!) == taskId);
            row.GetProperty("agentKind").GetString().ShouldBe("Grok");
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AgentTasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Garbage_in_the_id_segment_is_a_422_on_the_thread_route_too()
    {
        await SeedCardAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/cards/not-a-card/thread");

        ((int)response.StatusCode).ShouldBe(422);
    }

    [Test]
    public async Task The_plan_catalog_route_serves_the_repo_it_is_running_in()
    {
        using var client = _factory.CreateClient();

        var catalog = await client.GetFromJsonAsync<PlanCatalogDto>("/api/plans", Json);

        catalog.ShouldNotBeNull();
        catalog!.RootResolved.ShouldBeTrue("the server is running inside a checkout that holds plans");
        catalog.Plans.ShouldNotBeEmpty();
        var plan = catalog.Plans.First();

        var content = await client.GetFromJsonAsync<PlanContentDto>(
            $"/api/plans/content?file={Uri.EscapeDataString(plan.RelativePath)}", Json);

        content!.Content.Length.ShouldBeGreaterThan(0);
        content.Plan.RelativePath.ShouldBe(plan.RelativePath);
    }

    [Test]
    [Arguments("../../../../secrets.md")]
    [Arguments("docs/superpowers/specs/../../../CLAUDE.md")]
    [Arguments("CLAUDE.md")]
    [Arguments("server/Program.cs")]
    public async Task The_plan_reader_refuses_anything_outside_its_roots(string escape)
    {
        // Over HTTP, because the status code is the contract: 422 says "that name is not allowed",
        // where a 404 would invite the caller to keep guessing at names that might be.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/plans/content?file={Uri.EscapeDataString(escape)}");

        ((int)response.StatusCode).ShouldBe(422, $"'{escape}' is not a plan");
    }

    private async Task<Guid> AddGrokTaskAsync(string identifier)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Depth = 0,
            Title = $"{identifier} - the Grok half",
            Goal = "work",
            Kind = Server.Domain.Enums.AgentTaskKind.Worker,
            Role = Server.Domain.Enums.AgentTaskRole.Code,
            AgentKind = Server.Domain.Enums.AgentKind.Grok,
            ModelLevel = Server.Domain.Enums.AgentModelLevel.High,
            Workspace = Server.Domain.Enums.WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = Server.Domain.Enums.AgentTaskStatus.Dispatched,
            ReplyTo = Server.Domain.Enums.AgentTaskReplyTo.Session,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<(Guid CardId, string Identifier)> SeedCardAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boards = scope.ServiceProvider.GetRequiredService<BoardService>();
        var cards = scope.ServiceProvider.GetRequiredService<CardService>();

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Card Thread Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"antiphon-thread-{Guid.NewGuid():N}"),
            BaseBranch = "master",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        _projectId = project.Id;

        var board = await boards.CreateAsync(
            new CreateBoardRequest(_projectId, $"Thread board {Guid.NewGuid():N}"), CancellationToken.None);
        var created = await cards.CreateAsync(
            board.Id, new CreateCardRequest(null, "The card the thread is about"), CancellationToken.None);

        // A CARD-nnnn no row in this shared database holds: the identifier is unique per BOARD, so
        // every board's first card is CARD-0001 and addressing that by identifier is a 409.
        var identifier = await NextUnusedIdentifierAsync(db);
        await db.Cards.Where(c => c.Id == created.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Identifier, identifier));
        return (created.Id, identifier);
    }

    private static async Task<string> NextUnusedIdentifierAsync(AppDbContext db)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = $"CARD-{Random.Shared.Next(4_000, 9_999):0000}";
            if (!await db.Cards.AnyAsync(c => c.Identifier == candidate))
                return candidate;
        }
        throw new InvalidOperationException("No unused CARD-nnnn identifier available.");
    }
}
