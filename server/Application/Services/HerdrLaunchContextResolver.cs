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
    /// CARD-0225: the title the operator sees on the tab/pane. The agent's name, never the
    /// shared TUI profile id — one profile serves many agents, so its DisplayName cannot label
    /// any one of them. DefinitionName survives only for a session that has no agent at all.
    /// </summary>
    public static string PaneTitleFor(Agent? agent, AgentSession session)
    {
        if (!string.IsNullOrWhiteSpace(agent?.Name)) return agent.Name.Trim();
        if (!string.IsNullOrWhiteSpace(agent?.Slug)) return agent.Slug;
        if (!string.IsNullOrWhiteSpace(session.DefinitionName)) return session.DefinitionName;
        return "agent";
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
        HerdrLaunchOptions options;
        // Card-owned session: the card's board names the project.
        if (session.CardId is Guid cardId)
        {
            var project = await _db.Cards.AsNoTracking()
                .Where(c => c.Id == cardId)
                .Select(c => c.Board.Project)
                .FirstOrDefaultAsync(ct);
            options = project is not null ? FromProject(project, paneTitle) : CatchAll(paneTitle);
            return ApplyStandingOverrides(session, agent, options);
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
                    return ApplyStandingOverrides(session, agent, FromProject(project, paneTitle));
            }

            if (agent.PoolProjectId is Guid poolProjectId)
            {
                var project = await _db.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == poolProjectId, ct);
                if (project is not null)
                    return ApplyStandingOverrides(session, agent, FromProject(project, paneTitle));
            }
        }

        return ApplyStandingOverrides(session, agent, CatchAll(paneTitle));
    }

    /// <summary>
    /// CARD-0384: standing cardless non-pool agents may override the workspace fallback/create
    /// label and carry a tab pin. The workspace key is never changed. Card-spawn and pool
    /// delegates keep project/allocator placement.
    /// </summary>
    internal static HerdrLaunchOptions ApplyStandingOverrides(
        AgentSession session, Agent? agent, HerdrLaunchOptions options)
    {
        if (session.CardId is not null || agent is null || agent.IsPoolDelegate)
            return options;

        var workspaceLabel = string.IsNullOrWhiteSpace(agent.HerdrWorkspaceLabel)
            ? options.WorkspaceLabel
            : agent.HerdrWorkspaceLabel.Trim();
        var tabLabel = string.IsNullOrWhiteSpace(agent.HerdrTabLabel)
            ? null
            : agent.HerdrTabLabel.Trim();
        return options with { WorkspaceLabel = workspaceLabel, TabLabel = tabLabel };
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
