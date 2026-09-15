using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class CommitOnSettlePolicyTests
{
    [Test]
    public async Task Create_with_an_unknown_commitOnSettle_is_422()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        using var workspace = new TempWorkspace();

        var ex = await Should.ThrowAsync<ValidationException>(
            () => service.CreateAsync(
                NewRequest("do the work") with { CommitOnSettle = "Sometimes" },
                ManualCaller(workspace.Path),
                CancellationToken.None));
        ex.Errors.ShouldContainKey("CommitOnSettle");
    }

    [Test]
    [Arguments("Never", CommitOnSettlePolicy.Never)]
    [Arguments("Always", CommitOnSettlePolicy.Always)]
    [Arguments("Agent", CommitOnSettlePolicy.Agent)]
    public async Task Create_persists_each_commitOnSettle_value(string value, CommitOnSettlePolicy expected)
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        using var workspace = new TempWorkspace();

        var created = await service.CreateAsync(
            NewRequest("do the work") with { CommitOnSettle = value },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        var stored = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        stored.CommitOnSettle.ShouldBe(expected);
    }

    [Test]
    public async Task Follow_up_inherits_the_prior_tasks_Never_unless_overridden()
    {
        using var workspace = new TempWorkspace();
        var prior = await SeedTaskAsync(workspace.Path, CommitOnSettlePolicy.Never);

        await using var db = CreateContext();
        var inherited = await CreateService(db).CreateAsync(
            NewRequest("now add the edge cases") with
            {
                FollowUpOnTask = DelegationReportFormatter.Short(prior.Id),
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);
        (await db.AgentTasks.SingleAsync(t => t.Id == inherited.Id))
            .CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);

        var overridden = await CreateService(db).CreateAsync(
            NewRequest("force a commit") with
            {
                FollowUpOnTask = DelegationReportFormatter.Short(prior.Id),
                CommitOnSettle = "Always",
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);
        (await db.AgentTasks.SingleAsync(t => t.Id == overridden.Id))
            .CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Always);
    }

    [Test]
    [Arguments("DO NOT commit anything")]
    [Arguments("please don't commit")]
    [Arguments("run it without committing")]
    public async Task Do_not_commit_prose_only_warns_when_the_field_is_absent(string goal)
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        using var workspace = new TempWorkspace();

        var created = await service.CreateAsync(
            NewRequest(goal),
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("pass -NoCommit");
        (await db.AgentTasks.SingleAsync(t => t.Id == created.Id)).CommitOnSettle.ShouldBeNull();
    }

    [Test]
    public async Task Do_not_commit_prose_with_NoCommit_does_not_warn()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        using var workspace = new TempWorkspace();

        var created = await service.CreateAsync(
            NewRequest("DO NOT commit anything") with { CommitOnSettle = "Never" },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        (created.Warning ?? "").ShouldNotContain("pass -NoCommit");
        (await db.AgentTasks.SingleAsync(t => t.Id == created.Id))
            .CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
    }

    [Test]
    public async Task A_goal_that_merely_says_commit_does_not_warn()
    {
        await using var db = CreateContext();
        var service = CreateService(db);
        using var workspace = new TempWorkspace();

        var created = await service.CreateAsync(
            NewRequest("commit the docs change"),
            ManualCaller(workspace.Path),
            CancellationToken.None);

        (created.Warning ?? "").ShouldNotContain("pass -NoCommit");
    }

    [Test]
    public async Task Project_PUT_null_leaves_On_Inherit_clears_Off_sets()
    {
        await using var db = CreateContext();
        var projects = CreateProjectService(db);
        var created = await projects.CreateAsync(
            new CreateProjectRequest(
                $"c527-{Guid.NewGuid():N}",
                "https://example.test/repo.git",
                null,
                false,
                false,
                @"D:\src\c527",
                "master"),
            CancellationToken.None);

        var on = await projects.UpdateAsync(created.Id, Update(created, "On"), CancellationToken.None);
        on.CommitOnSettle.ShouldBe("On");
        on.EffectiveCommitOnSettle.ShouldBeTrue();

        var left = await projects.UpdateAsync(created.Id, Update(created, null), CancellationToken.None);
        left.CommitOnSettle.ShouldBe("On");
        left.EffectiveCommitOnSettle.ShouldBeTrue();

        var inherit = await projects.UpdateAsync(created.Id, Update(created, "Inherit"), CancellationToken.None);
        inherit.CommitOnSettle.ShouldBeNull();
        inherit.EffectiveCommitOnSettle.ShouldBeTrue();

        var off = await projects.UpdateAsync(created.Id, Update(created, "Off"), CancellationToken.None);
        off.CommitOnSettle.ShouldBe("Off");
        off.EffectiveCommitOnSettle.ShouldBeFalse();

        var ex = await Should.ThrowAsync<ValidationException>(
            () => projects.UpdateAsync(created.Id, Update(created, "Sideways"), CancellationToken.None));
        ex.Errors.ShouldContainKey("CommitOnSettle");
    }

    [Test]
    public async Task Merge_and_commit_children_are_created_with_Never()
    {
        using var workspace = new TempWorkspace();
        var parent = await SeedTaskAsync(workspace.Path, null);

        await using var db = CreateContext();
        var conflicted = await db.AgentTasks.SingleAsync(t => t.Id == parent.Id);
        conflicted.WorktreePath = workspace.Path;
        conflicted.WorktreeBranch = "feat/card-task-x";
        conflicted.MergeTargetRef = "master";
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var merge = await service.CreateMergeTaskAsync(
            conflicted, ["conflicted.cs"], CancellationToken.None);
        merge.ShouldNotBeNull();
        merge!.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
        merge.CommitBaselineSha.ShouldBeNull();

        const string head = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef";
        var commit = await service.CreateCommitTaskAsync(
            conflicted, "unattributable", ["x.md"], head, null, CancellationToken.None);
        commit.ShouldNotBeNull();
        commit!.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
        commit.CommitBaselineSha.ShouldBe(head);
    }

    private static UpdateProjectRequest Update(ProjectDto project, string? commitOnSettle) =>
        new(
            project.Name,
            project.GitRepositoryUrl,
            project.ConstitutionPath,
            project.GitHubIntegrationEnabled,
            project.NotificationsEnabled,
            project.LocalRepositoryPath,
            project.BaseBranch,
            CommitOnSettle: commitOnSettle);

    private static CreateAgentTaskRequest NewRequest(string goal) =>
        new(Goal: goal, Kind: AgentTaskKind.Worker, Role: AgentTaskRole.Custom);

    private static AgentTaskService.Caller ManualCaller(string directory) => new(null, null, directory);

    private static async Task<AgentTask> SeedTaskAsync(string workingDirectory, CommitOnSettlePolicy? policy)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Seeded Worker",
            Goal = "Seeded goal.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            Status = AgentTaskStatus.Succeeded,
            CommitOnSettle = policy,
            CreatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static AgentTaskService CreateService(AppDbContext db)
    {
        var settings = new DelegationSettings { MaxDepth = 5, MaxTasksPerRoot = 40, MaxCostUsdPerRoot = 5.00m };
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);
    }

    private static ProjectService CreateProjectService(AppDbContext db) =>
        new(
            db,
            new StubHttpClientFactory(),
            Options.Create(new GithubSettings()),
            NullLogger<ProjectService>.Instance,
            delegation: Options.Create(new DelegationSettings()));

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c527-policy").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
