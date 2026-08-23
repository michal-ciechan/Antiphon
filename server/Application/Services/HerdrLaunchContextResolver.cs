using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Resolves <see cref="HerdrLaunchOptions"/> for a herdr-lane launch (CARD-0160). The runner has
/// no DB access, so workspace key/label/cwd are decided here from the session's project scope.
/// </summary>
public sealed class HerdrLaunchContextResolver
{
    private readonly AppDbContext _db;

    public HerdrLaunchContextResolver(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Card session → Board.ProjectId; interactive standing agent → Agent.BoardId → Project;
    /// delegate → task project / PoolProjectId; nothing resolvable → catch-all
    /// (<c>WorkspaceKey = "none"</c>, label "Antiphon", cwd null).
    /// </summary>
    public async Task<HerdrLaunchOptions> ResolveAsync(
        AgentSession session,
        Agent? agent,
        string paneTitle,
        CancellationToken ct)
    {
        // Card-owned session: the card's board names the project.
        if (session.CardId is Guid cardId)
        {
            var project = await _db.Cards.AsNoTracking()
                .Where(c => c.Id == cardId)
                .Select(c => c.Board.Project)
                .FirstOrDefaultAsync(ct);
            if (project is not null)
                return FromProject(project, paneTitle);
        }

        // Standing / pool agent: board → project, else pool project scope.
        if (agent is not null)
        {
            if (agent.BoardId is Guid boardId)
            {
                var project = await _db.Boards.AsNoTracking()
                    .Where(b => b.Id == boardId)
                    .Select(b => b.Project)
                    .FirstOrDefaultAsync(ct);
                if (project is not null)
                    return FromProject(project, paneTitle);
            }

            if (agent.PoolProjectId is Guid poolProjectId)
            {
                var project = await _db.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == poolProjectId, ct);
                if (project is not null)
                    return FromProject(project, paneTitle);
            }
        }

        return CatchAll(paneTitle);
    }

    private static HerdrLaunchOptions FromProject(Project project, string paneTitle) =>
        new(
            WorkspaceKey: $"project:{project.Id:D}",
            WorkspaceLabel: string.IsNullOrWhiteSpace(project.Name) ? "Antiphon" : project.Name,
            WorkspaceCwd: string.IsNullOrWhiteSpace(project.LocalRepositoryPath)
                ? null
                : project.LocalRepositoryPath,
            PaneTitle: paneTitle);

    private static HerdrLaunchOptions CatchAll(string paneTitle) =>
        new(
            WorkspaceKey: "none",
            WorkspaceLabel: "Antiphon",
            WorkspaceCwd: null,
            PaneTitle: paneTitle);
}
