using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0644 V-1. An omitted workspace on a FRESH Worker or Orchestrator is a Worktree: persisted
/// at the production <see cref="AgentTaskService.CreateAsync"/> boundary, provisioned by the real
/// dispatcher into its own branch and checkout, and never a silent share of the caller's tree. A
/// directory that cannot branch refuses before any row exists. Explicit modes stay explicit, and
/// the structured bases (StartRef, RepairSource, SourceLanding, Runner, Interim Code) accept the
/// omitted default because it now IS the Worktree they require.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeDefaultAdmissionTests
{
    [Test]
    [Timeout(120_000)]
    public async Task FreshWorkerUsesWorktree()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var callerBranch = await HeadRefAsync(world.Repo.Path);
        var callerSha = await HeadShaAsync(world.Repo.Path);

        var created = await world.CreateTaskAsync(new CreateAgentTaskRequest("ordinary fresh work", Role: AgentTaskRole.Code));

        created.Workspace.ShouldBe(WorkspaceMode.Worktree, "an omitted workspace on a fresh worker is its own worktree");
        created.RepoPath.ShouldNotBeNull();

        var (task, sessionId) = await world.DispatchAsync();

        task.Status.ShouldBe(AgentTaskStatus.Dispatched);
        task.WorktreeBranch.ShouldBe("feat/card-task-" + DelegationReportFormatter.Short(task.Id));
        task.WorktreeBranch.ShouldNotBe(world.Owner.WorktreeBranch);
        var worktree = task.WorktreePath.ShouldNotBeNull();
        SamePath(worktree, world.Repo.Path).ShouldBeFalse("the delegate must not run in the caller's checkout");
        SamePath(worktree, world.Owner.WorktreePath!).ShouldBeFalse();
        (await HeadRefAsync(worktree)).ShouldBe("refs/heads/" + task.WorktreeBranch);

        await using (var db = world.CreateContext())
        {
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
            SamePath(session.Cwd, worktree).ShouldBeTrue($"launch cwd {session.Cwd} must be the task worktree {worktree}");
        }

        // The caller's own checkout is untouched: same branch, same HEAD.
        (await HeadRefAsync(world.Repo.Path)).ShouldBe(callerBranch);
        (await HeadShaAsync(world.Repo.Path)).ShouldBe(callerSha);
    }

    [Test]
    [Timeout(120_000)]
    public async Task FreshOrchestratorInOtherRepoUsesWorktree()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var other = Path.Combine(world.Repo.Path, "other-repo");
        Directory.CreateDirectory(other);
        (await ScratchGitRepo.GitInAsync(other, "init", "-b", "master")).Ok.ShouldBeTrue();
        await File.WriteAllTextAsync(Path.Combine(other, "README.md"), "other\n");
        (await ScratchGitRepo.GitInAsync(other, "add", "README.md")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(other, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "other")).Ok.ShouldBeTrue();

        // A different -Dir is a different LOCATION, not proof of isolation: two orchestrators sent to
        // the same other repo would otherwise share it. Omitted means Worktree there too.
        var created = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "plan the other repo", Kind: AgentTaskKind.Orchestrator, Role: AgentTaskRole.Plan, WorkingDirectory: other));

        created.Workspace.ShouldBe(WorkspaceMode.Worktree);
        SamePath(created.RepoPath.ShouldNotBeNull(), other).ShouldBeTrue();
    }

    [Test]
    [Timeout(120_000)]
    public async Task NonGitDefaultRefusesBeforeInsert()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var plain = Directory.CreateTempSubdirectory("c644-not-git").FullName;
        try
        {
            var before = await world.TaskCountAsync();
            foreach (var (row, request) in new (string, CreateAgentTaskRequest)[]
                     {
                         ("worker", new CreateAgentTaskRequest("no repo here", Role: AgentTaskRole.Code)),
                         ("orchestrator", new CreateAgentTaskRequest("no repo here", Kind: AgentTaskKind.Orchestrator, Role: AgentTaskRole.Plan)),
                     })
            {
                var refused = await Should.ThrowAsync<ValidationException>(
                    () => world.CreateTaskAsync(request, callerDirectory: plain), row);
                refused.Code.ShouldBe("workspace_default_not_git", row);
                refused.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.Workspace), row);
                var message = string.Join(" ", refused.Errors[nameof(CreateAgentTaskRequest.Workspace)]);
                message.ShouldContain("-Shared", Case.Sensitive, row);
                message.ShouldContain("-ReadOnly", Case.Sensitive, row);
                (await world.TaskCountAsync()).ShouldBe(before, row + ": a refusal must insert nothing");
            }

            // An explicit Shared request in the same directory is still supported.
            var shared = await world.CreateTaskAsync(
                new CreateAgentTaskRequest("explicitly shared", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared),
                callerDirectory: plain);
            shared.Workspace.ShouldBe(WorkspaceMode.Shared);
        }
        finally
        {
            try { Directory.Delete(plain, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    [Timeout(120_000)]
    public async Task ExplicitModesRemainExplicit()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var callerBranch = await HeadRefAsync(world.Repo.Path);

        var shared = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "shared on purpose", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared));
        shared.Workspace.ShouldBe(WorkspaceMode.Shared);

        var readOnly = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "read only", Role: AgentTaskRole.Review, Workspace: WorkspaceMode.ReadOnly));
        readOnly.Workspace.ShouldBe(WorkspaceMode.ReadOnly);

        var worktree = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "worktree", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree));
        worktree.Workspace.ShouldBe(WorkspaceMode.Worktree);

        var sharedOrchestrator = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "shared orchestrator", Kind: AgentTaskKind.Orchestrator, Role: AgentTaskRole.Plan,
            Workspace: WorkspaceMode.Shared));
        sharedOrchestrator.Workspace.ShouldBe(WorkspaceMode.Shared);

        (await HeadRefAsync(world.Repo.Path)).ShouldBe(callerBranch);
    }

    [Test]
    [Timeout(120_000)]
    public async Task DefaultWorktreeAdmitsStructuredBases()
    {
        await using var world = await RepairSourceWorld.CreateAsync(
            ordinaryCodeTask: true, createTaskThroughService: true, phoneHome: RunnerPolicy());

        // StartRef: the omitted default is the fresh Worktree a start ref needs.
        var startRef = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "continue from the sibling", Role: AgentTaskRole.Code)
        { WorktreeBaseRequestedRef = world.Owner.WorktreeBranch! });
        startRef.Workspace.ShouldBe(WorkspaceMode.Worktree);
        startRef.WorktreeBaseRequestedRef.ShouldBe(world.Owner.WorktreeBranch);

        // RepairSource: resolved Worktree, attributed to the owner.
        var repair = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "repair the owner", Role: AgentTaskRole.Code, RepairSourceTaskId: world.Owner.Id));
        repair.Workspace.ShouldBe(WorkspaceMode.Worktree);
        repair.RepairSourceTaskId.ShouldBe(world.Owner.Id);

        // Runner: the only admitted runner shape is Worktree, which the default now supplies.
        var runner = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "remote work", Role: AgentTaskRole.Code, AgentKind: AgentKind.ClaudeCode, RunnerId: "server2"));
        runner.Workspace.ShouldBe(WorkspaceMode.Worktree);
        runner.RunnerId.ShouldBe("server2");

        // SourceLanding and Interim Code pass their MODE gate on the default; the next admission step
        // (no custody backend / no Interim policy in this host) is what refuses, and inserts nothing.
        var before = await world.TaskCountAsync();
        var custody = await Should.ThrowAsync<HttpException>(() => world.CreateTaskAsync(new CreateAgentTaskRequest(
            "verify the landing", Role: AgentTaskRole.Mutation, SourceLandingOperationId: Guid.NewGuid())));
        custody.Code.ShouldBe("verification_custody_unsupported_backend",
            "omitted workspace must not be refused as a SourceLanding mode error");

        var interim = await Should.ThrowAsync<HttpException>(() => world.CreateTaskAsync(new CreateAgentTaskRequest(
            "interim code", Role: AgentTaskRole.Code,
            VerificationRound: VerificationRound.Interim,
            VerificationSubjectTaskId: world.Owner.Id,
            VerificationBaselineOutcomeId: Guid.NewGuid(),
            VerificationSelection: new VerificationSelectionReference("docs/plan.md", new string('a', 40), "S1"))));
        interim.Code.ShouldBe(InterimVerificationPolicy.BackstopUnreadyCode,
            "omitted workspace on Interim Code must pass the Worker Code/Worktree shape");
        (await world.TaskCountAsync()).ShouldBe(before);
    }

    [Test]
    [Timeout(120_000)]
    public async Task ExplicitIncompatibleModesStillRefuse()
    {
        await using var world = await RepairSourceWorld.CreateAsync(
            ordinaryCodeTask: true, createTaskThroughService: true, phoneHome: RunnerPolicy());
        var before = await world.TaskCountAsync();

        async Task Refused(string row, CreateAgentTaskRequest request, string? code)
        {
            var ex = await Should.ThrowAsync<HttpException>(() => world.CreateTaskAsync(request), row);
            if (code is not null)
                ex.Code.ShouldBe(code, row);
            (await world.TaskCountAsync()).ShouldBe(before, row + ": an explicit incompatible mode inserts nothing");
        }

        await Refused("start-ref-shared", new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared)
            { WorktreeBaseRequestedRef = world.Owner.WorktreeBranch! }, "worktree_start_ref_mode");
        await Refused("start-ref-readonly", new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.ReadOnly)
            { WorktreeBaseRequestedRef = world.Owner.WorktreeBranch! }, "worktree_start_ref_mode");
        await Refused("repair-shared", new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared,
            RepairSourceTaskId: world.Owner.Id), "repair_source_mode");
        await Refused("source-landing-shared", new CreateAgentTaskRequest("x", Role: AgentTaskRole.Mutation, Workspace: WorkspaceMode.Shared,
            SourceLandingOperationId: Guid.NewGuid()), "verification_source_mode");
        await Refused("mutation-readonly", new CreateAgentTaskRequest("x", Role: AgentTaskRole.Mutation, Workspace: WorkspaceMode.ReadOnly), null);
        await Refused("interim-code-shared", new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared,
            VerificationRound: VerificationRound.Interim), InterimVerificationPolicy.RoundRoleCode);
        await Refused("interim-review-default", new CreateAgentTaskRequest("x", Role: AgentTaskRole.Review,
            VerificationRound: VerificationRound.Interim), InterimVerificationPolicy.RoundRoleCode);
        await Refused("runner-shared", new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, AgentKind: AgentKind.ClaudeCode,
            Workspace: WorkspaceMode.Shared, RunnerId: "server2"), null);

        var plain = Directory.CreateTempSubdirectory("c644-not-git").FullName;
        try
        {
            var ex = await Should.ThrowAsync<ValidationException>(() => world.CreateTaskAsync(
                new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree), callerDirectory: plain));
            ex.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.Workspace));
            (await world.TaskCountAsync()).ShouldBe(before);
        }
        finally
        {
            try { Directory.Delete(plain, recursive: true); } catch (IOException) { }
        }
    }

    private static PhoneHomeLaunchPolicy RunnerPolicy() =>
        new(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true, AllowedRunnerId = "server2", AllowDelegatedTasks = true,
            HostWorkspaceRoot = Path.GetTempPath(), CallbackOrigin = "https://antiphon.example.invalid",
            SharedSecret = "x",
        }));

    private static async Task<string> HeadRefAsync(string dir) =>
        (await ScratchGitRepo.GitInAsync(dir, "symbolic-ref", "HEAD")).StdOut.Trim();

    private static async Task<string> HeadShaAsync(string dir) =>
        (await ScratchGitRepo.GitInAsync(dir, "rev-parse", "HEAD")).StdOut.Trim();

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).Replace('\\', '/').TrimEnd('/'),
            Path.GetFullPath(b).Replace('\\', '/').TrimEnd('/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
