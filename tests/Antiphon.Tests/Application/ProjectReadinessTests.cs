using System.Diagnostics;
using System.IO.Abstractions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.FileSystem;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0032 slice 1 — <c>GET /api/projects/{id}/readiness</c> projection.</summary>
[Category("Integration")]
public class ProjectReadinessTests
{
    [Test]
    public async Task unknown_project_is_not_found()
    {
        await using var db = CreateContext();
        await Should.ThrowAsync<NotFoundException>(() =>
            CreateService(db).GetReadinessAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Test]
    public async Task directory_missing_when_path_unset()
    {
        var project = await SeedProjectAsync(localPath: null);
        var check = await CheckAsync(project.Id, ReadinessKeys.Directory);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Level.ShouldBe(ReadinessLevel.Required);
        check.Summary.ShouldContain("No local directory");
    }

    [Test]
    public async Task directory_missing_when_path_does_not_exist()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"antiphon-ready-miss-{Guid.NewGuid():N}");
        var project = await SeedProjectAsync(localPath: missing);
        var check = await CheckAsync(project.Id, ReadinessKeys.Directory);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Summary.ShouldContain(missing);
    }

    [Test]
    public async Task directory_ok_when_path_exists()
    {
        var temp = NewTemp();
        try
        {
            Directory.CreateDirectory(temp);
            var project = await SeedProjectAsync(localPath: temp);
            var check = await CheckAsync(project.Id, ReadinessKeys.Directory);
            check.Status.ShouldBe(ReadinessStatus.Ok);
            check.Summary.ShouldContain(temp);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task git_repository_not_applicable_without_directory()
    {
        var project = await SeedProjectAsync(localPath: null);
        var check = await CheckAsync(project.Id, ReadinessKeys.GitRepository);
        check.Status.ShouldBe(ReadinessStatus.NotApplicable);
        check.Level.ShouldBe(ReadinessLevel.Recommended);
    }

    [Test]
    public async Task git_repository_warning_when_plain_directory()
    {
        var temp = NewTemp();
        try
        {
            Directory.CreateDirectory(temp);
            var project = await SeedProjectAsync(localPath: temp);
            var check = await CheckAsync(project.Id, ReadinessKeys.GitRepository);
            check.Status.ShouldBe(ReadinessStatus.Warning);
            check.Summary.ShouldContain("not a git repository");
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task git_repository_ok_when_toplevel()
    {
        var temp = NewTemp();
        try
        {
            await GitInitAsync(temp);
            var project = await SeedProjectAsync(localPath: temp);
            var check = await CheckAsync(project.Id, ReadinessKeys.GitRepository);
            check.Status.ShouldBe(ReadinessStatus.Ok);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task git_repository_warning_when_subdirectory()
    {
        var temp = NewTemp();
        try
        {
            await GitInitAsync(temp);
            var nested = Path.Combine(temp, "src");
            Directory.CreateDirectory(nested);
            var project = await SeedProjectAsync(localPath: nested);
            var check = await CheckAsync(project.Id, ReadinessKeys.GitRepository);
            check.Status.ShouldBe(ReadinessStatus.Warning);
            check.Summary.ShouldContain("inside the repository");
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task board_missing_when_none()
    {
        var project = await SeedProjectAsync();
        var check = await CheckAsync(project.Id, ReadinessKeys.Board);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Level.ShouldBe(ReadinessLevel.Required);
        check.Summary.ShouldContain("no board");
    }

    [Test]
    public async Task board_missing_when_no_active_column()
    {
        var project = await SeedProjectAsync();
        await SeedBoardAsync(project.Id, withActiveColumn: false);
        var check = await CheckAsync(project.Id, ReadinessKeys.Board);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Summary.ShouldContain("active");
    }

    [Test]
    public async Task board_ok_when_active_column()
    {
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id, withActiveColumn: true);
        var check = await CheckAsync(project.Id, ReadinessKeys.Board);
        check.Status.ShouldBe(ReadinessStatus.Ok);
        check.Summary.ShouldContain(board.Name);
    }

    [Test]
    public async Task agent_missing_when_none()
    {
        var project = await SeedProjectAsync();
        await SeedBoardAsync(project.Id);
        var check = await CheckAsync(project.Id, ReadinessKeys.Agent);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Level.ShouldBe(ReadinessLevel.Required);
    }

    [Test]
    public async Task agent_ok_when_on_board()
    {
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id);
        var agent = await SeedAgentAsync(board.Id);
        var check = await CheckAsync(project.Id, ReadinessKeys.Agent);
        check.Status.ShouldBe(ReadinessStatus.Ok);
        check.Summary.ShouldContain(agent.Name);
    }

    [Test]
    public async Task agent_counts_a_path_matched_agent_on_another_board()
    {
        var temp = NewTemp();
        try
        {
            Directory.CreateDirectory(temp);
            var project = await SeedProjectAsync(localPath: temp);
            var other = await SeedProjectAsync();
            var otherBoard = await SeedBoardAsync(other.Id);
            var agent = await SeedAgentAsync(otherBoard.Id, workingDirectory: temp);
            var check = await CheckAsync(project.Id, ReadinessKeys.Agent);
            check.Status.ShouldBe(ReadinessStatus.Ok);
            check.Summary.ShouldContain(agent.Name);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task agent_ignores_pool_delegate_rows()
    {
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id);
        await SeedAgentAsync(board.Id, isPoolDelegate: true);
        var check = await CheckAsync(project.Id, ReadinessKeys.Agent);
        check.Status.ShouldBe(ReadinessStatus.Missing);
    }

    [Test]
    public async Task agent_runner_not_applicable_without_agent()
    {
        var project = await SeedProjectAsync();
        var check = await CheckAsync(project.Id, ReadinessKeys.AgentRunner);
        check.Status.ShouldBe(ReadinessStatus.NotApplicable);
        check.Level.ShouldBe(ReadinessLevel.Required);
    }

    [Test]
    public async Task agent_runner_missing_when_profile_disabled()
    {
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id);
        var profile = await SeedProfileAsync(enabled: false, withRevision: true);
        await SeedAgentAsync(board.Id, tuiProfileId: profile.Id);
        var check = await CheckAsync(project.Id, ReadinessKeys.AgentRunner);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Summary.ShouldContain("disabled");
    }

    [Test]
    public async Task agent_runner_missing_when_no_active_revision()
    {
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id);
        var profile = await SeedProfileAsync(enabled: true, withRevision: false);
        await SeedAgentAsync(board.Id, tuiProfileId: profile.Id);
        var check = await CheckAsync(project.Id, ReadinessKeys.AgentRunner);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Summary.ShouldContain("no active revision");
    }

    [Test]
    public async Task agent_runner_ok_when_enabled_with_revision()
    {
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id);
        var profile = await SeedProfileAsync(enabled: true, withRevision: true);
        await SeedAgentAsync(board.Id, tuiProfileId: profile.Id);
        var check = await CheckAsync(project.Id, ReadinessKeys.AgentRunner);
        check.Status.ShouldBe(ReadinessStatus.Ok);
        check.Summary.ShouldContain(profile.DisplayName);
    }

    [Test]
    public async Task agent_directory_not_applicable_without_agent()
    {
        var project = await SeedProjectAsync();
        var check = await CheckAsync(project.Id, ReadinessKeys.AgentDirectory);
        check.Status.ShouldBe(ReadinessStatus.NotApplicable);
    }

    [Test]
    public async Task agent_directory_missing_when_path_does_not_exist()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"antiphon-agent-miss-{Guid.NewGuid():N}");
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id);
        var agent = await SeedAgentAsync(board.Id, workingDirectory: missing);
        var check = await CheckAsync(project.Id, ReadinessKeys.AgentDirectory);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Summary.ShouldContain(agent.Name);
        check.Summary.ShouldContain(missing);
        check.Fix.ShouldNotBeNull();
        check.Fix!.Action.ShouldBe("create-directory");
        check.Fix.Route.ShouldNotBeNull();
        check.Fix.Route!.ShouldContain(agent.Id.ToString());
    }

    [Test]
    public async Task create_directory_fix_creates_the_agent_working_directory_and_readiness_becomes_ok()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"antiphon-agent-mkdir-{Guid.NewGuid():N}");
        try
        {
            Directory.Exists(missing).ShouldBeFalse();
            var project = await SeedProjectAsync();
            var board = await SeedBoardAsync(project.Id);
            var agent = await SeedAgentAsync(board.Id, workingDirectory: missing);

            var before = await CheckAsync(project.Id, ReadinessKeys.AgentDirectory);
            before.Status.ShouldBe(ReadinessStatus.Missing);
            before.Fix.ShouldNotBeNull();
            before.Fix!.Action.ShouldBe("create-directory");

            await using var db = CreateContext();
            var result = await CreateAgentService(db).EnsureWorkingDirectoryAsync(agent.Id, CancellationToken.None);
            result.AgentId.ShouldBe(agent.Id);
            result.WorkingDirectory.ShouldBe(missing);
            Directory.Exists(missing).ShouldBeTrue();

            var after = await CheckAsync(project.Id, ReadinessKeys.AgentDirectory);
            after.Status.ShouldBe(ReadinessStatus.Ok);
            after.Fix.ShouldBeNull();
        }
        finally
        {
            Cleanup(missing);
        }
    }

    [Test]
    public async Task agent_directory_ok_when_path_exists()
    {
        var temp = NewTemp();
        try
        {
            Directory.CreateDirectory(temp);
            var project = await SeedProjectAsync();
            var board = await SeedBoardAsync(project.Id);
            await SeedAgentAsync(board.Id, workingDirectory: temp);
            var check = await CheckAsync(project.Id, ReadinessKeys.AgentDirectory);
            check.Status.ShouldBe(ReadinessStatus.Ok);
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task delegation_root_empty_list_is_warning()
    {
        var temp = NewTemp();
        try
        {
            Directory.CreateDirectory(temp);
            var project = await SeedProjectAsync(localPath: temp);
            await using var db = CreateContext();
            var service = CreateService(db, new DelegationSettings { AllowedRoots = [] });
            var dto = await service.GetReadinessAsync(project.Id, CancellationToken.None);
            var check = dto.Checks.Single(c => c.Key == ReadinessKeys.DelegationRoot);
            check.Level.ShouldBe(ReadinessLevel.Recommended);
            check.Status.ShouldBe(ReadinessStatus.Warning);
            check.Summary.ShouldContain("AllowedRoots");
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task delegation_root_matching_root_is_ok()
    {
        var temp = NewTemp();
        try
        {
            Directory.CreateDirectory(temp);
            var project = await SeedProjectAsync(localPath: temp);
            await using var db = CreateContext();
            var service = CreateService(db, new DelegationSettings { AllowedRoots = [temp] });
            var check = (await service.GetReadinessAsync(project.Id, CancellationToken.None))
                .Checks.Single(c => c.Key == ReadinessKeys.DelegationRoot);
            check.Status.ShouldBe(ReadinessStatus.Ok);
            check.Summary.ShouldContain("under the allowed root");
        }
        finally
        {
            Cleanup(temp);
        }
    }

    [Test]
    public async Task delegation_root_non_matching_root_is_warning()
    {
        var temp = NewTemp();
        var other = NewTemp();
        try
        {
            Directory.CreateDirectory(temp);
            Directory.CreateDirectory(other);
            var project = await SeedProjectAsync(localPath: temp);
            await using var db = CreateContext();
            var service = CreateService(db, new DelegationSettings { AllowedRoots = [other] });
            var check = (await service.GetReadinessAsync(project.Id, CancellationToken.None))
                .Checks.Single(c => c.Key == ReadinessKeys.DelegationRoot);
            check.Status.ShouldBe(ReadinessStatus.Warning);
            check.Summary.ShouldContain("is not under one");
        }
        finally
        {
            Cleanup(temp);
            Cleanup(other);
        }
    }

    [Test]
    public async Task workflow_template_ok_when_any_exist()
    {
        await SeedTemplateAsync();
        var project = await SeedProjectAsync();
        var check = await CheckAsync(project.Id, ReadinessKeys.WorkflowTemplate);
        check.Status.ShouldBe(ReadinessStatus.Ok);
        check.Level.ShouldBe(ReadinessLevel.Required);
    }

    [Test]
    [NotInParallel]
    public async Task workflow_template_missing_when_none_exist()
    {
        await using var db = CreateContext();
        await db.WorkflowTemplates.ExecuteDeleteAsync();
        var project = await SeedProjectAsync();
        var check = await CheckAsync(project.Id, ReadinessKeys.WorkflowTemplate);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Summary.ShouldContain("No workflow template");
    }

    [Test]
    public async Task orchestrator_missing_without_always_on_bundles()
    {
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id);
        await SeedAgentAsync(board.Id, alwaysOn: true);
        var check = await CheckAsync(project.Id, ReadinessKeys.Orchestrator);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Level.ShouldBe(ReadinessLevel.Recommended);
    }

    [Test]
    public async Task orchestrator_ok_when_always_on_with_both_bundles()
    {
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id);
        var agent = await SeedAgentAsync(board.Id, alwaysOn: true);
        await AttachBundlesAsync(agent.Id, [InstructionBundles.Orchestrator, InstructionBundles.BoardApi]);
        var check = await CheckAsync(project.Id, ReadinessKeys.Orchestrator);
        check.Status.ShouldBe(ReadinessStatus.Ok);
        check.Summary.ShouldContain(agent.Name);
    }

    [Test]
    public async Task channel_missing_when_unbound()
    {
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id);
        await SeedAgentAsync(board.Id);
        var check = await CheckAsync(project.Id, ReadinessKeys.Channel);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Level.ShouldBe(ReadinessLevel.Optional);
    }

    [Test]
    public async Task channel_ok_when_bound_to_project_agent()
    {
        var project = await SeedProjectAsync();
        var board = await SeedBoardAsync(project.Id);
        var agent = await SeedAgentAsync(board.Id);
        await SeedChannelAsync(agent.Id);
        var check = await CheckAsync(project.Id, ReadinessKeys.Channel);
        check.Status.ShouldBe(ReadinessStatus.Ok);
    }

    [Test]
    public async Task github_missing_when_url_empty()
    {
        var project = await SeedProjectAsync(gitUrl: "");
        var check = await CheckAsync(project.Id, ReadinessKeys.GitHub);
        check.Status.ShouldBe(ReadinessStatus.Missing);
        check.Level.ShouldBe(ReadinessLevel.Optional);
    }

    [Test]
    public async Task github_ok_when_url_set()
    {
        var project = await SeedProjectAsync(gitUrl: "https://example.test/repo.git");
        var check = await CheckAsync(project.Id, ReadinessKeys.GitHub);
        check.Status.ShouldBe(ReadinessStatus.Ok);
    }

    [Test]
    public async Task github_warning_when_github_url_without_integration()
    {
        var project = await SeedProjectAsync(
            gitUrl: "https://github.com/org/repo.git",
            gitHubIntegration: false);
        var check = await CheckAsync(project.Id, ReadinessKeys.GitHub);
        check.Status.ShouldBe(ReadinessStatus.Warning);
        check.Summary.ShouldContain("GitHub integration is off");
    }

    [Test]
    public async Task can_dispatch_is_false_while_any_required_check_is_missing()
    {
        var project = await SeedProjectAsync(localPath: null, gitUrl: "");
        await using var db = CreateContext();
        var dto = await CreateService(db).GetReadinessAsync(project.Id, CancellationToken.None);
        dto.Checks.Any(c => c.Level == ReadinessLevel.Required && c.Status == ReadinessStatus.Missing)
            .ShouldBeTrue();
        dto.CanDispatch.ShouldBeFalse();
        dto.Checks.Select(c => c.Key).ShouldBe([
            ReadinessKeys.Directory,
            ReadinessKeys.GitRepository,
            ReadinessKeys.Board,
            ReadinessKeys.Agent,
            ReadinessKeys.AgentRunner,
            ReadinessKeys.AgentDirectory,
            ReadinessKeys.DelegationRoot,
            ReadinessKeys.WorkflowTemplate,
            ReadinessKeys.Orchestrator,
            ReadinessKeys.Channel,
            ReadinessKeys.GitHub,
        ]);
    }

    [Test]
    public async Task can_dispatch_ignores_delegation_root_and_optional_rows()
    {
        var temp = NewTemp();
        try
        {
            Directory.CreateDirectory(temp);
            await SeedTemplateAsync();
            var project = await SeedProjectAsync(localPath: temp, gitUrl: "");
            var board = await SeedBoardAsync(project.Id);
            var profile = await SeedProfileAsync(enabled: true, withRevision: true);
            await SeedAgentAsync(board.Id, workingDirectory: temp, tuiProfileId: profile.Id);

            await using var db = CreateContext();
            var dto = await CreateService(db, new DelegationSettings { AllowedRoots = [] })
                .GetReadinessAsync(project.Id, CancellationToken.None);

            dto.Checks.Single(c => c.Key == ReadinessKeys.DelegationRoot).Status
                .ShouldBe(ReadinessStatus.Warning);
            dto.Checks.Single(c => c.Key == ReadinessKeys.GitHub).Status
                .ShouldBe(ReadinessStatus.Missing);
            dto.CanDispatch.ShouldBeTrue();
        }
        finally
        {
            Cleanup(temp);
        }
    }

    private static async Task<ReadinessCheckDto> CheckAsync(Guid projectId, string key)
    {
        await using var db = CreateContext();
        var dto = await CreateService(db).GetReadinessAsync(projectId, CancellationToken.None);
        return dto.Checks.Single(c => c.Key == key);
    }

    private static ProjectSetupService CreateService(AppDbContext db, DelegationSettings? settings = null) =>
        new(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings ?? new DelegationSettings()),
            NullLogger<ProjectSetupService>.Instance);

    private static AgentService CreateAgentService(AppDbContext db) =>
        new(
            db,
            new CardWorkflowRunFactory(db, TimeProvider.System),
            new MockEventBus(),
            TimeProvider.System,
            new FileSystemDirectoryWriter(new FileSystem()),
            NullLogger<AgentService>.Instance);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string NewTemp() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-ready-{Guid.NewGuid():N}");

    private static void Cleanup(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private static async Task GitInitAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("init");
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("git failed to start");
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await process.StandardError.ReadToEndAsync());
    }

    private static async Task<Project> SeedProjectAsync(
        string? localPath = "unset",
        string gitUrl = "https://example.test/repo.git",
        bool gitHubIntegration = false)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Ready {Guid.NewGuid():N}",
            GitRepositoryUrl = gitUrl,
            LocalRepositoryPath = localPath == "unset" ? null : localPath,
            BaseBranch = "master",
            GitHubIntegrationEnabled = gitHubIntegration,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await using var db = CreateContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private static async Task<Board> SeedBoardAsync(Guid projectId, bool withActiveColumn = true)
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
        if (withActiveColumn)
        {
            foreach (var column in BoardService.CreateDefaultColumns(board, now))
                board.Columns.Add(column);
        }
        else
        {
            board.Columns.Add(new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = "done",
                Name = "Done",
                ColumnOrder = 0,
                CardStatus = CardStatus.Done,
                IsActive = false,
                IsTerminal = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await using var db = CreateContext();
        db.Boards.Add(board);
        await db.SaveChangesAsync();
        return board;
    }

    private static async Task<Agent> SeedAgentAsync(
        Guid boardId,
        string? workingDirectory = null,
        Guid? tuiProfileId = null,
        bool alwaysOn = false,
        bool isPoolDelegate = false)
    {
        var now = DateTime.UtcNow;
        var slug = Guid.NewGuid().ToString("N");
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"Agent {slug}",
            Slug = $"agent-{slug}",
            WorkingDirectory = workingDirectory ?? Path.Combine(Path.GetTempPath(), $"wd-{slug}"),
            BoardId = boardId,
            TuiProfileId = tuiProfileId,
            AlwaysOn = alwaysOn,
            IsPoolDelegate = isPoolDelegate,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }

    private static async Task<AgentTuiProfile> SeedProfileAsync(bool enabled, bool withRevision)
    {
        var now = DateTime.UtcNow;
        var profile = new AgentTuiProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = $"Profile {Guid.NewGuid():N}",
            Kind = AgentKind.ClaudeCode,
            IsEnabled = enabled,
            IsDefault = false,
            Source = AgentTuiProfileSource.Operator,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await using var db = CreateContext();
        db.AgentTuiProfiles.Add(profile);
        await db.SaveChangesAsync();
        if (withRevision)
        {
            var revision = new AgentTuiProfileRevision
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                RevisionNumber = 1,
                Executable = "claude",
                ArgumentsJson = "[]",
                DiscoveryArgumentsJson = "[]",
                VersionArgumentsJson = "[]",
                AuthenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
                NonSecretEnvironmentJson = "{}",
                SecretEnvironmentNamesJson = "[]",
                Guidance = string.Empty,
                CreatedAt = now,
            };
            db.AgentTuiProfileRevisions.Add(revision);
            await db.SaveChangesAsync();
            profile.ActiveRevisionId = revision.Id;
            await db.SaveChangesAsync();
        }

        return profile;
    }

    private static async Task SeedTemplateAsync()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.WorkflowTemplates.Add(new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = $"Tpl {Guid.NewGuid():N}",
            Description = "readiness",
            YamlDefinition = "name: x\nstages:\n  - name: A\n    executorType: agent\n    gateRequired: false\n",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task AttachBundlesAsync(Guid agentId, IReadOnlyList<string> keys)
    {
        await using var db = CreateContext();
        var agent = await db.Agents.SingleAsync(a => a.Id == agentId);
        await AgentBundleAttachments.SetAsync(db, agent, keys, DateTime.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    private static async Task SeedChannelAsync(Guid agentId)
    {
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.ChatChannels.Add(new ChatChannel
        {
            Id = Guid.NewGuid(),
            Provider = "telegram",
            ExternalId = $"chat-{Guid.NewGuid():N}",
            Kind = ChatChannelKind.Group,
            Title = "readiness",
            AgentId = agentId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }
}
