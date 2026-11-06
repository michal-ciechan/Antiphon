using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824: provisional real Git/schema/auth and consumer budget.
public sealed class C0824RefOnlyConsumerTests
{
    [Test]
    public async Task Child_auth_uses_explicit_repo_ref_and_remote_execution()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        var (token, hash) = AgentTaskService.NewToken();
        f.Task.TokenHash = hash;
        f.Task.Kind = AgentTaskKind.Orchestrator;
        var session = new AgentSession { Id = Guid.NewGuid(), Cwd = f.Identity,
            RunnerId = f.Task.RunnerId, RunnerStoreId = Guid.NewGuid(), RunnerCwd = f.Task.RemoteWorktreePath,
            Status = SessionStatus.Running, StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        f.Db.AgentSessions.Add(session); f.Task.AgentSessionId = session.Id;
        await f.Db.SaveChangesAsync();
        var service = Service(f);
        var caller = await service.AuthenticateAsync(token, default);
        caller.WorkingDirectory.ShouldBe(f.Task.RemoteWorktreePath, "caller-remote-context");
        caller.MayDelegate.ShouldBeTrue("reached:U1:orchestrator:authenticated");
        var child = await service.CreateAsync(new CreateAgentTaskRequest(Goal: "isolated child"), caller, default);
        var row = await f.Db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == child.Id);
        row.Workspace.ShouldBe(WorkspaceMode.Worktree, "child-own-worktree");
        row.RepoPath.ShouldBe(f.Git.Desktop);
        row.WorktreeBaseRequestedRef.ShouldBe(f.Git.Baseline, "child-own-worktree-from=S");
        await f.Git.RunnerPushAsync("later.txt", "parent moved after authentication");
        row.WorktreeBaseRequestedRef.ShouldBe(f.Git.Baseline, "frozen-child-source");
        foreach (var mode in new[] { WorkspaceMode.Shared, WorkspaceMode.ReadOnly })
        {
            var refusal = await Should.ThrowAsync<ConflictException>(() => service.CreateAsync(
                new CreateAgentTaskRequest(Goal: "unsafe shared child", Workspace: mode,
                    WorkingDirectory: f.Git.Desktop), caller, default));
            refusal.Code.ShouldBe("ref_only_child_requires_worktree", "shared-main-refused");
        }
        (await service.CreateMergeTaskAsync(f.Task, ["conflict.cs"], default, "master"))
            .ShouldBeNull("merge-helper-no-identity");
        f.Task.Kind = AgentTaskKind.Worker; await f.Db.SaveChangesAsync();
        caller = await service.AuthenticateAsync(token, default);
        await Should.ThrowAsync<ForbiddenException>(() => service.CreateAsync(
            new CreateAgentTaskRequest(Goal: "worker cannot delegate"), caller, default));
        session.RunnerStoreId = null; await f.Db.SaveChangesAsync();
        (await Should.ThrowAsync<ConflictException>(() => service.AuthenticateAsync(token, default)))
            .Code.ShouldBe("ref_only_execution_binding_required");
        Directory.Exists(f.Identity).ShouldBeFalse("no-identity-materialization");
    }

    [Test]
    public async Task Decision_and_control_paths_do_not_grant_identity_file_access()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        Directory.CreateDirectory(f.Identity);
        var session = new AgentSession { Id = Guid.NewGuid(), Cwd = f.Identity,
            RunnerId = f.Task.RunnerId, RunnerStoreId = Guid.NewGuid(), RunnerCwd = f.Task.RemoteWorktreePath,
            Status = SessionStatus.Running, StartedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
        f.Db.AgentSessions.Add(session); f.Task.AgentSessionId = session.Id; f.Task.Status = AgentTaskStatus.Working;
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(), AgentTaskRole.Code,
            WorkspaceMode.Worktree, InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        f.Task.InternalDecisionPolicyJson = InternalDecisionPolicy.Serialize(policy);
        f.Task.InternalDecisionPolicyHash = InternalDecisionPolicy.Hash(f.Task.InternalDecisionPolicyJson);
        await f.Db.SaveChangesAsync();
        var caller = new AgentTaskService.Caller(f.Task, session.Id, f.Identity);
        var request = new InternalDecisionQuestionRequest(Guid.NewGuid(), f.Task.Attempt, "backup-transport",
            InternalDecisionCategory.ShellTransport, ["scripts/deploy-gym-stat.ps1"], null, null,
            InternalDecisionImpact.None, "Repair quoting?", "Preserve the command arguments.", "Exact bytes compared.");
        var result = await new AgentTaskDecisionQuestionService(f.Db).CheckAsync(f.Task.Id, request, caller, default);
        result.Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman, "identity-never-grants-files");
        result.Reason.ShouldBe("ref_only_remote_path_verification_required", "remote-proof-unavailable-is-explicit");
        (await File.ReadAllTextAsync(Path.Combine(f.Git.Desktop, "base.txt"))).ShouldNotBeNull();
    }

    [Test]
    public async Task Same_repo_tasks_keep_distinct_ownership_and_display_mode()
    {
        await using var f = await RefOnlySyncFixture.CreateAsync();
        f.Task.Title = "recorded ref only";
        await f.Db.SaveChangesAsync();
        var items = await new HomeTaskService(f.Db, TimeProvider.System).GetAsync(default);
        var item = items.Items.Single(i => i.Id == f.Task.Id);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(item));
        json.RootElement.TryGetProperty("DesktopWorkspaceKind", out var mode)
            .ShouldBeTrue("display-recorded-mode");
        mode.GetInt32().ShouldBe((int)DesktopWorkspaceKind.RefOnly, "display-ref-only");
        var other = new AgentTask { Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(),
            RepoPath = f.Task.RepoPath, WorkingDirectory = f.Task.WorkingDirectory,
            WorktreePath = Path.Combine(f.Git.Root, ".ref-only", Guid.NewGuid().ToString("N")),
            WorktreeBranch = "feat/card-task-other", Status = AgentTaskStatus.Queued };
        var first = WorkspaceReservationKey.ForTask(f.Task.WorktreePath, f.Task.WorkingDirectory,
            f.Task.WorktreeBranch, f.Task.RepoPath);
        var second = WorkspaceReservationKey.ForTask(other.WorktreePath, other.WorkingDirectory,
            other.WorktreeBranch, other.RepoPath);
        first.ShouldNotBe(second, "owners-distinct");
        item.WorktreePath.ShouldBe(f.Identity, "identity-is-history");
        item.RunnerId.ShouldBe(f.Task.RunnerId);
        Directory.Exists(f.Identity).ShouldBeFalse();
    }

    private static AgentTaskService Service(RefOnlySyncFixture f) => new(f.Db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings { MaxDepth = 5, MaxTasksPerRoot = 40 }),
        new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
        NullLogger<AgentTaskService>.Instance);
}
