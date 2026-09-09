using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0032 slice 3 — transactional project, board, and first-agent setup.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ProjectSetupServiceTests
{
    [Test]
    public async Task Phone_catalog_and_explicit_setup_preserve_defaults()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        var catalog = await service.GetCatalogAsync(default);
        catalog.ReplyStyles.Select(s => s.Key).ShouldBe(["Normal", "Terse", "Caveman", "Brief", "Phone", "Explanatory"]);
        catalog.ReplyStyles.Single(s => s.Key == "Phone").Description.ShouldBe(
            "Minimal Telegram/Slack replies. Short bullets, about 5–7 words; no tables. Delegate reports keep their own contracts.");
        await SeededWorkflowTemplates.EnsureFullFeaturePipelineAsync(db);
        foreach (var preset in new[] { AgentPresets.Worker, AgentPresets.Orchestrator })
        foreach (var style in new AgentReplyStyle?[] { null, AgentReplyStyle.Phone })
        {
            var directory = NewTemp();
            try
            {
                Directory.CreateDirectory(directory);
                var result = await service.SetupAsync(new ProjectSetupRequest(directory,
                    Agent: new ProjectSetupAgentRequest(Preset: preset, ReplyStyle: style, AlwaysOn: false)), default);
                result.Agent!.ReplyStyle.ShouldBe(style ?? AgentReplyStyle.Normal);
                await using var read = CreateContext();
                (await read.Agents.AsNoTracking().SingleAsync(a => a.Id == result.Agent.Id))
                    .ReplyStyle.ShouldBe(style ?? AgentReplyStyle.Normal);
            }
            finally { Cleanup(directory); }
        }
    }

    [Test]
    public async Task Fresh_git_setup_installs_ignore_without_enabling_board_and_readiness_stays_read_only()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
        using var repo = new ScratchGitRepo("c408-setup"); await repo.CommitFileAsync("seed", "seed");
        await using var world = new CardFilePrivacyWorld(isolated.ConnectionString);
        var service = CreateService(db, world.Service(db));
        var result = await service.SetupAsync(new ProjectSetupRequest(repo.Path, Name: "C408 setup", CreateDirectory: false), default);
        result.Board.SyncCardFiles.ShouldBeFalse(); result.Project.RepositoryVisibility.ShouldBe(RepositoryVisibility.Unknown);
        var ignore = await File.ReadAllBytesAsync(Path.Combine(repo.Path, ".gitignore"));
        System.Text.Encoding.UTF8.GetString(ignore).ShouldContain("/docs/cards/");
        (await service.GetReadinessAsync(result.Project.Id, default)).CanDispatch.ShouldBe((await CreateService(db).GetReadinessAsync(result.Project.Id, default)).CanDispatch);
        (await File.ReadAllBytesAsync(Path.Combine(repo.Path, ".gitignore"))).ShouldBe(ignore);
        Directory.Exists(Path.Combine(repo.Path, "docs/cards")).ShouldBeFalse();
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).ShouldBeEmpty();
    }

    [Test]
    public async Task setup_creates_one_project_board_and_agent_linked_to_that_board()
    {
        var directory = NewTemp();
        try
        {
            Directory.CreateDirectory(directory);
            await using var db = CreateContext();
            var result = await CreateService(db).SetupAsync(
                new ProjectSetupRequest(directory, Name: "Setup Happy", Agent: new ProjectSetupAgentRequest()),
                CancellationToken.None);

            result.Board.SyncCardFiles.ShouldBeFalse();
            result.Project.RepositoryVisibility.ShouldBe(RepositoryVisibility.Unknown);
            result.Agent.ShouldNotBeNull();
            result.Agent!.BoardId.ShouldBe(result.Board.Id);
            (await db.Boards.CountAsync(b => b.ProjectId == result.Project.Id)).ShouldBe(1);
            (await db.Projects.CountAsync(p => p.Id == result.Project.Id)).ShouldBe(1);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Test]
    public async Task setup_rejects_a_directory_already_owned_by_a_project()
    {
        var directory = NewTemp();
        try
        {
            Directory.CreateDirectory(directory);
            await using var firstDb = CreateContext();
            var first = await CreateService(firstDb).SetupAsync(
                new ProjectSetupRequest(directory, Name: "First"), CancellationToken.None);
            await using var secondDb = CreateContext();
            var ex = await Should.ThrowAsync<ConflictException>(() => CreateService(secondDb).SetupAsync(
                new ProjectSetupRequest(directory, Name: "Second"), CancellationToken.None));
            ex.Message.ShouldContain(first.Project.Id.ToString());
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Test]
    public async Task setup_rejects_a_subdirectory_of_a_git_repository()
    {
        var root = NewTemp();
        try
        {
            await GitInitAsync(root);
            var nested = Path.Combine(root, "src");
            Directory.CreateDirectory(nested);
            await using var db = CreateContext();
            var ex = await Should.ThrowAsync<ValidationException>(() => CreateService(db).SetupAsync(
                new ProjectSetupRequest(nested), CancellationToken.None));
            ex.Errors.Values.SelectMany(errors => errors).ShouldContain(message => message.Contains(root));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public async Task setup_rolls_back_project_and_board_when_agent_creation_fails()
    {
        var directory = NewTemp();
        try
        {
            Directory.CreateDirectory(directory);
            var name = $"Rollback {Guid.NewGuid():N}";
            await using var db = CreateContext();
            await Should.ThrowAsync<NotFoundException>(() => CreateService(db).SetupAsync(
                new ProjectSetupRequest(
                    directory,
                    Name: name,
                    Agent: new ProjectSetupAgentRequest(TuiProfileId: Guid.NewGuid())),
                CancellationToken.None));

            (await db.Projects.CountAsync(p => p.Name == name)).ShouldBe(0);
            (await db.Boards.CountAsync(b => b.Name == name)).ShouldBe(0);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Test]
    public async Task orchestrator_preset_renders_the_project_facts_and_its_contract()
    {
        var directory = NewTemp();
        try
        {
            Directory.CreateDirectory(directory);
            await using var db = CreateContext();
            await SeededWorkflowTemplates.EnsureFullFeaturePipelineAsync(db);
            var result = await CreateService(db).SetupAsync(
                new ProjectSetupRequest(
                    directory,
                    Name: "Orchestra",
                    BoardName: "The Board",
                    Agent: new ProjectSetupAgentRequest(Preset: AgentPresets.Orchestrator)),
                CancellationToken.None);

            result.Agent!.AlwaysOn.ShouldBeTrue();
            result.Agent.RemoteControlEnabled.ShouldBeTrue();
            result.Agent.ReplyStyle.ShouldBe(AgentReplyStyle.Normal);
            result.Agent.AttachedBundleKeys.ShouldBe([InstructionBundles.Orchestrator, InstructionBundles.BoardApi]);
            result.Agent.SystemPromptAppend.ShouldContain("The Board");
            result.Agent.SystemPromptAppend.ShouldContain(directory);
            result.Agent.DefaultWorkflowTemplateId.ShouldBe(AgentPresets.FullFeaturePipelineTemplateId);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Test]
    public async Task explicit_prompt_and_bundles_override_the_preset()
    {
        var directory = NewTemp();
        try
        {
            Directory.CreateDirectory(directory);
            await using var db = CreateContext();
            await SeededWorkflowTemplates.EnsureFullFeaturePipelineAsync(db);
            var result = await CreateService(db).SetupAsync(
                new ProjectSetupRequest(
                    directory,
                    Agent: new ProjectSetupAgentRequest(
                        Preset: AgentPresets.Orchestrator,
                        BundleKeys: [InstructionBundles.BoardApi],
                        SystemPromptAppend: "Custom contract.")),
                CancellationToken.None);

            result.Agent!.AttachedBundleKeys.ShouldBe([InstructionBundles.BoardApi]);
            result.Agent.SystemPromptAppend.ShouldBe("Custom contract.");
        }
        finally
        {
            Cleanup(directory);
        }
    }

    private static ProjectSetupService CreateService(AppDbContext db, CardTaskFileService? cardFiles = null)
    {
        var eventBus = new MockEventBus();
        var agentService = new AgentService(
            db,
            new CardWorkflowRunFactory(db, TimeProvider.System),
            eventBus,
            TimeProvider.System,
            new NoOpDirectoryWriter(),
            NullLogger<AgentService>.Instance);
        return new ProjectSetupService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(new DelegationSettings()),
            NullLogger<ProjectSetupService>.Instance,
            new ProjectService(db, new StubHttpClientFactory(), Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance, cardFiles: cardFiles),
            new BoardService(db, eventBus, TimeProvider.System, cardFiles: cardFiles),
            agentService,
            directoryWriter: new NoOpDirectoryWriter(), cardFiles: cardFiles);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string NewTemp() => Path.Combine(Path.GetTempPath(), $"antiphon-setup-{Guid.NewGuid():N}");

    private static void Cleanup(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
    }

    private static async Task GitInitAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var info = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("init");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("git failed to start");
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await process.StandardError.ReadToEndAsync());
    }

    private sealed class NoOpDirectoryWriter : IDirectoryWriter
    {
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
