using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class CommitOnSettlePolicyTests
{
    private static AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions());
    private static AgentTaskService Service(AppDbContext db) => new(db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings { MaxDepth = 5, MaxTasksPerRoot = 40 }),
        new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System, NullLogger<AgentTaskService>.Instance);
    private static AgentTaskService.Caller Caller => new(null, null, Path.GetTempPath());

    [Test]
    public async Task Project_PUT_null_leaves_On_Inherit_clears_Off_sets()
    {
        await using var db = Db();
        var service = new ProjectService(db, new ClientFactory(), Options.Create(new GithubSettings()), NullLogger<ProjectService>.Instance);
        var project = await service.CreateAsync(new(Guid.NewGuid().ToString(), "https://example.test/repo.git", null, false, false, null, "master"), default);
        UpdateProjectRequest Request(string? value) => new(project.Name, project.GitRepositoryUrl, null, false, false, null, "master", CommitOnSettle: value);
        foreach (var (value, stored, effective) in new (string?, bool?, bool)[] { ("On", true, true), (null, true, true), ("Inherit", null, true), ("Off", false, false) })
        {
            var result = await service.UpdateAsync(project.Id, Request(value), default);
            result.CommitOnSettle.ShouldBe(stored); result.EffectiveCommitOnSettle.ShouldBe(effective);
        }
        var error = await Should.ThrowAsync<ValidationException>(() => service.UpdateAsync(project.Id, Request("Sideways"), default));
        error.Errors.ShouldContainKey("CommitOnSettle");
    }

    private sealed class ClientFactory : IHttpClientFactory { public HttpClient CreateClient(string name) => new(); }

    [Test]
    public async Task Create_with_an_unknown_commitOnSettle_is_422()
    {
        await using var db = Db(); var title = Guid.NewGuid().ToString();
        var error = await Should.ThrowAsync<ValidationException>(() => Service(db).CreateAsync(new("x", Title: title, CommitOnSettle: "Sometimes"), Caller, default));
        error.Errors.ShouldContainKey("CommitOnSettle");
        (await db.AgentTasks.AnyAsync(t => t.Title == title)).ShouldBeFalse();
    }

    [Test]
    [Arguments("Never")][Arguments("Always")][Arguments("Agent")]
    public async Task Create_persists_each_commitOnSettle_value(string value)
    {
        await using var db = Db();
        var created = await Service(db).CreateAsync(new("x", CommitOnSettle: value), Caller, default);
        db.ChangeTracker.Clear();
        (await db.AgentTasks.SingleAsync(t => t.Id == created.Id)).CommitOnSettle.ShouldBe(Enum.Parse<CommitOnSettlePolicy>(value));
    }

    [Test]
    public async Task Follow_up_inherits_the_prior_tasks_Never_unless_overridden()
    {
        await using var db = Db(); var service = Service(db);
        var prior = await service.CreateAsync(new("prior", CommitOnSettle: "Never"), Caller, default);
        var inherited = await service.CreateAsync(new("follow", FollowUpOnTask: prior.ShortId), Caller, default);
        var explicitTask = await service.CreateAsync(new("follow override", FollowUpOnTask: prior.ShortId, CommitOnSettle: "Always"), Caller, default);
        (await db.AgentTasks.SingleAsync(t => t.Id == inherited.Id)).CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
        (await db.AgentTasks.SingleAsync(t => t.Id == explicitTask.Id)).CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Always);
    }

    [Test]
    [Arguments("DO NOT commit anything")][Arguments("please don't commit")][Arguments("run it without committing")]
    public async Task Do_not_commit_prose_only_warns_when_the_field_is_absent(string goal)
    {
        await using var db = Db(); var created = await Service(db).CreateAsync(new(goal), Caller, default);
        created.Warning.ShouldContain("pass -NoCommit");
        (await db.AgentTasks.SingleAsync(t => t.Id == created.Id)).CommitOnSettle.ShouldBeNull();
    }

    [Test]
    public async Task Do_not_commit_prose_with_NoCommit_does_not_warn()
    {
        await using var db = Db();
        var created = await Service(db).CreateAsync(new("DO NOT commit anything", CommitOnSettle: "Never"), Caller, default);
        (created.Warning ?? "").ShouldNotContain("pass -NoCommit");
    }

    [Test]
    public async Task A_goal_that_merely_says_commit_does_not_warn()
    {
        await using var db = Db(); var created = await Service(db).CreateAsync(new("commit changes"), Caller, default);
        (created.Warning ?? "").ShouldNotContain("pass -NoCommit");
    }
}
