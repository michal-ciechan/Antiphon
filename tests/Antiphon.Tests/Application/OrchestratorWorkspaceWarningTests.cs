using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0251 S3: launch-time incident is once per (agent, fingerprint), declared-only,
/// silenced by project acknowledgement, and re-raised after a cwd change.
/// </summary>
[Category("Integration")]
public class OrchestratorWorkspaceWarningTests
{
    [Test]
    public async Task raises_once_for_a_declared_orchestrator_in_the_checkout()
    {
        var temp = NewTemp();
        try
        {
            await GitInitAsync(temp);
            await File.WriteAllTextAsync(Path.Combine(temp, "AGENTS.md"), "repo");
            var project = await SeedProjectAsync(temp);
            var board = await SeedBoardAsync(project.Id);
            var agent = await SeedAgentAsync(board.Id, temp, alwaysOn: true);
            await AttachBundlesAsync(agent.Id, [InstructionBundles.Orchestrator, InstructionBundles.BoardApi]);

            var service = CreateService();
            await service.MaybeRaiseForStandingAgentAsync(agent, sessionId: Guid.NewGuid(), CancellationToken.None);
            await service.MaybeRaiseForStandingAgentAsync(agent, sessionId: Guid.NewGuid(), CancellationToken.None);

            await using var db = CreateContext();
            var rows = await db.AgentIncidents
                .Where(i => i.AgentId == agent.Id
                    && i.Kind == AgentIncidentKind.OrchestratorWorkspaceUnconfigured)
                .ToListAsync();
            rows.Count.ShouldBe(1);
            rows[0].Severity.ShouldBe(AlertSeverity.Warning);
            rows[0].FailureReason.ShouldStartWith(OrchestratorWorkspaceWarningService.FingerprintPrefix);
            rows[0].Message.ShouldContain("orchestrator-workspace.ps1 plan");
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task does_not_raise_for_a_worker()
    {
        var temp = NewTemp();
        try
        {
            await GitInitAsync(temp);
            await File.WriteAllTextAsync(Path.Combine(temp, "AGENTS.md"), "repo");
            var project = await SeedProjectAsync(temp);
            var board = await SeedBoardAsync(project.Id);
            var agent = await SeedAgentAsync(board.Id, temp, alwaysOn: true);

            await CreateService().MaybeRaiseForStandingAgentAsync(agent, null, CancellationToken.None);

            await using var db = CreateContext();
            (await db.AgentIncidents.CountAsync(i => i.AgentId == agent.Id)).ShouldBe(0);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task does_not_raise_when_the_project_has_acknowledged()
    {
        var temp = NewTemp();
        try
        {
            await GitInitAsync(temp);
            await File.WriteAllTextAsync(Path.Combine(temp, "AGENTS.md"), "repo");
            var project = await SeedProjectAsync(temp);
            await using (var db = CreateContext())
            {
                var row = await db.Projects.SingleAsync(p => p.Id == project.Id);
                row.OrchestratorWorkspaceAcknowledgedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            var board = await SeedBoardAsync(project.Id);
            var agent = await SeedAgentAsync(board.Id, temp, alwaysOn: true);
            await AttachBundlesAsync(agent.Id, [InstructionBundles.Orchestrator]);

            await CreateService().MaybeRaiseForStandingAgentAsync(agent, null, CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentIncidents.CountAsync(i => i.AgentId == agent.Id)).ShouldBe(0);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task raises_again_after_the_working_directory_changes()
    {
        var first = NewTemp();
        var second = NewTemp();
        try
        {
            await GitInitAsync(first);
            await File.WriteAllTextAsync(Path.Combine(first, "AGENTS.md"), "one");
            await GitInitAsync(second);
            await File.WriteAllTextAsync(Path.Combine(second, "AGENTS.md"), "two");
            var project = await SeedProjectAsync(first);
            var board = await SeedBoardAsync(project.Id);
            var agent = await SeedAgentAsync(board.Id, first, alwaysOn: true);
            await AttachBundlesAsync(agent.Id, [InstructionBundles.Orchestrator]);

            var service = CreateService();
            await service.MaybeRaiseForStandingAgentAsync(agent, null, CancellationToken.None);

            await using (var db = CreateContext())
            {
                var row = await db.Agents.SingleAsync(a => a.Id == agent.Id);
                row.WorkingDirectory = second;
                await db.SaveChangesAsync();
                agent = row;
            }

            await service.MaybeRaiseForStandingAgentAsync(agent, null, CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.AgentIncidents.CountAsync(
                i => i.AgentId == agent.Id
                    && i.Kind == AgentIncidentKind.OrchestratorWorkspaceUnconfigured))
                .ShouldBe(2);
        }
        finally
        {
            Cleanup(first);
            Cleanup(second);
        }
    }

    [Test]
    public async Task task_kind_orchestrator_raises_even_without_the_bundle()
    {
        var temp = NewTemp();
        try
        {
            await GitInitAsync(temp);
            await File.WriteAllTextAsync(Path.Combine(temp, "AGENTS.md"), "repo");
            var project = await SeedProjectAsync(temp);
            var board = await SeedBoardAsync(project.Id);
            var agent = await SeedAgentAsync(board.Id, temp, alwaysOn: false);

            await CreateService().MaybeRaiseForOrchestratorTaskAsync(agent, Guid.NewGuid(), CancellationToken.None);

            await using var db = CreateContext();
            (await db.AgentIncidents.CountAsync(
                i => i.AgentId == agent.Id
                    && i.Kind == AgentIncidentKind.OrchestratorWorkspaceUnconfigured))
                .ShouldBe(1);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    private static OrchestratorWorkspaceWarningService CreateService() =>
        new(
            CreateContext(),
            new OrchestratorWorkspaceFactGatherer(),
            TimeProvider.System,
            NullLogger<OrchestratorWorkspaceWarningService>.Instance);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string NewTemp() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-oww-{Guid.NewGuid():N}");

    private static void Cleanup(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
    }

    private static async Task GitInitAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("init");
        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("git failed to start");
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await process.StandardError.ReadToEndAsync());
    }

    private static async Task<Project> SeedProjectAsync(string localPath)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"OWW {Guid.NewGuid():N}",
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
        await using var db = CreateContext();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        return board;
    }

    private static async Task<Agent> SeedAgentAsync(Guid boardId, string workingDirectory, bool alwaysOn)
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
            AlwaysOn = alwaysOn,
            Kind = AgentKind.ClaudeCode,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }

    private static async Task AttachBundlesAsync(Guid agentId, IReadOnlyList<string> keys)
    {
        await using var db = CreateContext();
        var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
        await AgentBundleAttachments.SetAsync(db, agent, keys, DateTime.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();
    }
}
