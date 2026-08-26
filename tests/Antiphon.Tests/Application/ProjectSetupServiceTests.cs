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
public class ProjectSetupServiceTests
{
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
            var result = await CreateService(db).SetupAsync(
                new ProjectSetupRequest(
                    directory,
                    Name: "Orchestra",
                    BoardName: "The Board",
                    Agent: new ProjectSetupAgentRequest(Preset: AgentPresets.Orchestrator)),
                CancellationToken.None);

            result.Agent!.AlwaysOn.ShouldBeTrue();
            result.Agent.ReplyStyle.ShouldBe(AgentReplyStyle.Normal);
            result.Agent.AttachedBundleKeys.ShouldBe([InstructionBundles.Orchestrator, InstructionBundles.BoardApi]);
            result.Agent.SystemPromptAppend.ShouldContain("The Board");
            result.Agent.SystemPromptAppend.ShouldContain(directory);
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

    private static ProjectSetupService CreateService(AppDbContext db)
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
            new ProjectService(db, new StubHttpClientFactory(), Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance),
            new BoardService(db, eventBus, TimeProvider.System),
            agentService,
            directoryWriter: new NoOpDirectoryWriter());
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
