using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0225: pane title is the agent's name, never the shared TUI profile id. CARD-0384 V-6 standing overrides.</summary>
[Category("Unit")]
public class HerdrLaunchContextTitleTests
{
    [Test]
    [Category("Unit")]
    public void PaneTitleFor_prefers_agent_Name_over_DefinitionName()
    {
        var agent = new Agent { Name = "PM-Orchestrator-Grok", Slug = "pm-orchestrator-grok" };
        var session = new AgentSession { DefinitionName = "grok-gkp-project" };

        HerdrLaunchContextResolver.PaneTitleFor(agent, session).ShouldBe("PM-Orchestrator-Grok");
    }

    [Test]
    [Category("Unit")]
    public void PaneTitleFor_falls_back_to_Slug_when_Name_is_blank()
    {
        var agent = new Agent { Name = "  ", Slug = "pm-orchestrator-grok" };
        var session = new AgentSession { DefinitionName = "grok-gkp-project" };

        HerdrLaunchContextResolver.PaneTitleFor(agent, session).ShouldBe("pm-orchestrator-grok");
    }

    [Test]
    [Category("Unit")]
    public void PaneTitleFor_falls_back_to_DefinitionName_when_agent_is_null()
    {
        var session = new AgentSession { DefinitionName = "grok-gkp-project" };

        HerdrLaunchContextResolver.PaneTitleFor(null, session).ShouldBe("grok-gkp-project");
    }

    [Test]
    [Category("Unit")]
    public void PaneTitleFor_falls_back_to_agent_when_nothing_is_set()
    {
        HerdrLaunchContextResolver.PaneTitleFor(null, new AgentSession()).ShouldBe("agent");
        HerdrLaunchContextResolver.PaneTitleFor(
            new Agent { Name = "", Slug = "" },
            new AgentSession { DefinitionName = "  " }).ShouldBe("agent");
    }

}

[Category("Integration")]
public class HerdrLaunchContextResolverTests
{
    [Test]
    public async Task Standing_cardless_agent_overrides_workspace_label_and_carries_tab_label()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var (project, agent) = await SeedStandingAsync(db, "PredictionMarkets", "Orch");
        var session = new AgentSession { CardId = null };
        var opts = await new HerdrLaunchContextResolver(db).ResolveAsync(session, agent, "title", CancellationToken.None);
        opts.WorkspaceKey.ShouldBe($"project:{project.Id:D}");
        opts.WorkspaceLabel.ShouldBe("PredictionMarkets");
        opts.TabLabel.ShouldBe("Orch");
    }

    [Test]
    [Category("Integration")]
    public async Task Card_session_ignores_owner_labels()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var (project, agent) = await SeedStandingAsync(db, "PredictionMarkets", "Orch");
        var board = await db.Boards.SingleAsync(b => b.Id == agent.BoardId);
        var now = DateTime.UtcNow;
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Title = "c",
            Identifier = $"C-{Guid.NewGuid():N}"[..12],
            Status = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.BoardColumns.Add(column);
        db.Cards.Add(card);
        await db.SaveChangesAsync();
        var session = new AgentSession { CardId = card.Id };
        var opts = await new HerdrLaunchContextResolver(db).ResolveAsync(session, agent, "title", CancellationToken.None);
        opts.WorkspaceKey.ShouldBe($"project:{project.Id:D}");
        opts.WorkspaceLabel.ShouldBe(project.Name);
        opts.TabLabel.ShouldBeNull();
    }

    [Test]
    [Category("Integration")]
    public async Task Pool_delegate_ignores_labels()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var (project, agent) = await SeedStandingAsync(db, "PredictionMarkets", "Orch", pool: true);
        var session = new AgentSession { CardId = null };
        var opts = await new HerdrLaunchContextResolver(db).ResolveAsync(session, agent, "title", CancellationToken.None);
        opts.WorkspaceLabel.ShouldBe(project.Name);
        opts.TabLabel.ShouldBeNull();
    }

    [Test]
    [Category("Integration")]
    public async Task Blank_labels_leave_defaults()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var (project, agent) = await SeedStandingAsync(db, "  ", "  ");
        var session = new AgentSession { CardId = null };
        var opts = await new HerdrLaunchContextResolver(db).ResolveAsync(session, agent, "title", CancellationToken.None);
        opts.WorkspaceLabel.ShouldBe(project.Name);
        opts.TabLabel.ShouldBeNull();
    }

    [Test]
    [Category("Integration")]
    public async Task Workspace_override_changes_label_not_key()
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var (project, agent) = await SeedStandingAsync(db, "Override", null);
        var session = new AgentSession { CardId = null };
        var opts = await new HerdrLaunchContextResolver(db).ResolveAsync(session, agent, "title", CancellationToken.None);
        opts.WorkspaceKey.ShouldBe($"project:{project.Id:D}");
        opts.WorkspaceLabel.ShouldBe("Override");
        opts.TabLabel.ShouldBeNull();
    }

    private static async Task<(Project Project, Agent Agent)> SeedStandingAsync(
        AppDbContext db, string? workspaceLabel, string? tabLabel, bool pool = false)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Proj {Guid.NewGuid():N}"[..20],
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = "D:/src/app",
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "Board",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Standing",
            Slug = $"st-{Guid.NewGuid():N}"[..16],
            WorkingDirectory = "D:/src/app",
            Details = "",
            Status = AgentStatus.Idle,
            BoardId = board.Id,
            IsPoolDelegate = pool,
            HerdrWorkspaceLabel = workspaceLabel,
            HerdrTabLabel = tabLabel,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        db.Boards.Add(board);
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return (project, agent);
    }
}
