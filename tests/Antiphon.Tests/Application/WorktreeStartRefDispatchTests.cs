using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0613 V-2/R-1/R-2. A start ref goes request -> database -> real dispatcher -> real worktree
/// -> captured progress baseline. The source branch stays occupied in its own checkout throughout:
/// selecting a BASE must never take over a sibling's branch.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class WorktreeStartRefDispatchTests
{
    [Test]
    [Timeout(120_000)]
    [Arguments("branch")]
    [Arguments("commit-tag")]
    [Arguments("full-sha")]
    public async Task C613_StartRefCreatesOwnBranchAndBaseline(string selector)
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        // The source branch is checked out in the owner's worktree — exactly the situation that
        // made callers write "git checkout -B <branch> <sha>" into a goal in the first place.
        (await ScratchGitRepo.GitInAsync(world.Owner.WorktreePath!, "symbolic-ref", "HEAD")).StdOut.Trim()
            .ShouldBe(world.OwnerRef);
        await world.Repo.GitAsync("tag", "c613-tag", world.OwnerSha);
        var requested = selector switch
        {
            "branch" => world.Owner.WorktreeBranch!,
            "commit-tag" => "c613-tag",
            _ => world.OwnerSha,
        };

        var created = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "continue the sibling's work",
            Role: AgentTaskRole.Code,
            Workspace: WorkspaceMode.Worktree,
            MergeTargetRef: "master")
        { WorktreeBaseRequestedRef = requested });

        created.WorktreeBaseRequestedRef.ShouldBe(requested);
        created.WorktreeBaseRef.ShouldBeNull("the base is recorded at PROVISIONING, not at create");
        created.MergeTargetRef.ShouldBe("master");

        var (task, _) = await world.DispatchAsync();

        task.Status.ShouldBe(AgentTaskStatus.Dispatched);
        task.WorktreeBranch.ShouldBe("feat/card-task-" + DelegationReportFormatter.Short(task.Id));
        task.WorktreeBranch.ShouldNotBe(world.Owner.WorktreeBranch);
        task.WorktreePath.ShouldNotBe(world.Owner.WorktreePath);
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim()
            .ShouldBe(world.OwnerSha);
        task.WorktreeBaseRequestedRef.ShouldBe(requested);
        task.WorktreeBaseRef.ShouldBe(requested);
        task.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Explicit);
        task.WorktreeBaseSha.ShouldBe(world.OwnerSha);
        task.WorktreeBaseTaskId.ShouldBeNull();
        // A start ref selects a base and NOTHING else: the merge destination is independent.
        task.MergeTargetRef.ShouldBe("master");

        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson).ShouldNotBeNull();
        baseline.Primary.FullRef.ShouldBe("refs/heads/" + task.WorktreeBranch);
        baseline.Primary.LocalSha.ShouldBe(world.OwnerSha);
        baseline.Primary.RegisteredCheckout.ShouldBe(task.WorktreePath);
        baseline.RepairSource.ShouldBeNull();

        // The source checkout is untouched: same branch, same HEAD, still its own worktree.
        (await ScratchGitRepo.GitInAsync(world.Owner.WorktreePath!, "symbolic-ref", "HEAD")).StdOut.Trim()
            .ShouldBe(world.OwnerRef);
        (await ScratchGitRepo.GitInAsync(world.Owner.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim()
            .ShouldBe(world.OwnerSha);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C613_OmittedStartRefKeepsTheOriginalPrecedence()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "ordinary work", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
            MergeTargetRef: world.Owner.WorktreeBranch));

        var (task, _) = await world.DispatchAsync();

        task.WorktreeBaseRequestedRef.ShouldBeNull();
        task.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.MergeTarget);
        task.WorktreeBaseRef.ShouldBe(world.Owner.WorktreeBranch);
    }

    [Test]
    [Timeout(120_000)]
    public async Task C613_ReusedWorktreeKeepsRecordedBase()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "continue", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
        { WorktreeBaseRequestedRef = world.Owner.WorktreeBranch! });
        var (first, _) = await world.DispatchAsync();
        var recordedRef = first.WorktreeBaseRef.ShouldNotBeNull();
        var recordedSha = first.WorktreeBaseSha.ShouldNotBeNull();
        recordedSha.ShouldBe(world.OwnerSha);

        // Work the delegate has already done in that checkout, and a source ref that moves on.
        await File.WriteAllTextAsync(Path.Combine(first.WorktreePath!, "continued.md"), "in progress\n");
        var movedSha = await world.CommitInOwnerTreeAsync("source moved on", push: false);
        movedSha.ShouldNotBe(world.OwnerSha);

        // A requeued attempt re-provisions the SAME task. Reuse must preserve the first decision.
        await using (var db = world.CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == first.Id);
            row.Status = AgentTaskStatus.Queued;
            row.AgentSessionId = null;
            await db.SaveChangesAsync();
        }
        var (again, _) = await world.DispatchAsync();

        again.WorktreeBaseRef.ShouldBe(recordedRef);
        again.WorktreeBaseSha.ShouldBe(recordedSha);
        again.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Explicit);
        again.WorktreeBaseRequestedRef.ShouldBe(world.Owner.WorktreeBranch);
        again.WorktreePath.ShouldBe(first.WorktreePath);
        File.Exists(Path.Combine(again.WorktreePath!, "continued.md"))
            .ShouldBeTrue("reuse must not throw away the previous attempt's work");
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("blank", "")]
    [Arguments("whitespace", "   ")]
    [Arguments("tab", "\t")]
    [Arguments("outer-whitespace", " master ")]
    [Arguments("leading-option", "--upload-pack=calc")]
    [Arguments("control-character", "mas\u0007ter")]
    [Arguments("too-long", "!301")]
    public async Task C613_StartRefAdmissionMatrixRefusesInvalidSelectors(string shape, string selector)
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var before = await world.TaskCountAsync();
        var value = selector == "!301" ? new string('b', 301) : selector;

        var refused = await Should.ThrowAsync<ValidationException>(() => world.CreateTaskAsync(
            new CreateAgentTaskRequest($"refused: {shape}", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
            { WorktreeBaseRequestedRef = value }));

        refused.Code.ShouldBe("worktree_start_ref_invalid");
        refused.Errors.Keys.ShouldContain(nameof(CreateAgentTaskRequest.WorktreeBaseRequestedRef));
        (await world.TaskCountAsync()).ShouldBe(before, "an invalid selector must not be queued");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C613_StartRefAdmissionMatrixAcceptsTheColumnLimit()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var longest = new string('b', 300);

        // 300 characters is valid input: it is refused later, at PROVISIONING, for naming nothing —
        // which is a different failure from "this is not a selector".
        var created = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "long but syntactically valid", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
        { WorktreeBaseRequestedRef = longest });
        created.WorktreeBaseRequestedRef.ShouldBe(longest);

        var (task, _) = await world.DispatchAsync();
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.WorktreePath.ShouldBeNull();
        task.WorktreeBaseRef.ShouldBeNull("provisioning never falls back to master for an EXPLICIT request");
        task.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Unset);
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("missing-ref", "no-such-ref-c613")]
    [Arguments("blob-tag", "c613-blob")]
    public async Task C613_StartRefAdmissionMatrixRefusesProvisioningWithoutFallback(string shape, string selector)
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        if (shape == "blob-tag")
        {
            var blob = (await world.Repo.GitReadAsync("hash-object", "-w", "README.md")).Trim();
            await world.Repo.GitAsync("tag", selector, blob);
        }
        await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "names no commit", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
        { WorktreeBaseRequestedRef = selector });

        var (task, _) = await world.DispatchAsync();

        // Refused, with NOTHING salvaged: provisioning never quietly substitutes master for a base
        // the caller explicitly named, which would hand the delegate a checkout it did not ask for.
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureReason.ShouldNotBeNull().ShouldContain("validation");
        task.WorktreePath.ShouldBeNull();
        task.WorktreeBranch.ShouldBeNull();
        task.WorktreeBaseRef.ShouldBeNull();
        task.WorktreeBaseSha.ShouldBeNull();
        task.WorktreeBaseSource.ShouldBe(WorktreeBaseSource.Unset);
        task.WorktreeBaseRequestedRef.ShouldBe(selector, "the caller's selector is preserved, not rewritten");
        task.AgentSessionId.ShouldBeNull();
        var identifier = "task-" + DelegationReportFormatter.Short(task.Id);
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "worktree", "list", "--porcelain")).StdOut
            .ShouldNotContain(identifier);
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("shared", "worktree_start_ref_mode")]
    [Arguments("read-only", "worktree_start_ref_mode")]
    [Arguments("omitted-workspace", "worktree_start_ref_mode")]
    [Arguments("agent-pin", "worktree_start_ref_mode")]
    [Arguments("follow-up", "worktree_start_ref_mode")]
    [Arguments("repair-source", "worktree_start_ref_mode")]
    [Arguments("source-landing", "worktree_start_ref_mode")]
    public async Task C613_StartRefAdmissionMatrixRefusesCompetingModes(string shape, string code)
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var before = await world.TaskCountAsync();
        var request = shape switch
        {
            "shared" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Shared),
            "read-only" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Review, Workspace: WorkspaceMode.ReadOnly),
            "omitted-workspace" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code),
            "agent-pin" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                AgentId: Guid.NewGuid()),
            "follow-up" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree,
                FollowUpOnTask: DelegationReportFormatter.Short(world.Owner.Id)),
            "repair-source" => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
                { RepairSourceTaskId = world.Owner.Id },
            _ => new CreateAgentTaskRequest("x", Role: AgentTaskRole.Mutation, Workspace: WorkspaceMode.Worktree)
                { SourceLandingOperationId = Guid.NewGuid() },
        };

        var refused = await Should.ThrowAsync<ValidationException>(() => world.CreateTaskAsync(
            request with { WorktreeBaseRequestedRef = world.Owner.WorktreeBranch }));

        refused.Code.ShouldBe(code);
        (await world.TaskCountAsync()).ShouldBe(before, "a refused mode must not leave a row or a launch behind");
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("worker-plan")]
    [Arguments("worker-code")]
    [Arguments("orchestrator")]
    public async Task C613_StartRefAdmissionMatrixAcceptsOrdinaryFreshWorktrees(string shape)
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var request = shape switch
        {
            "worker-plan" => new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan,
                Workspace: WorkspaceMode.Worktree),
            "worker-code" => new CreateAgentTaskRequest("code it", Role: AgentTaskRole.Code,
                Workspace: WorkspaceMode.Worktree),
            _ => new CreateAgentTaskRequest("orchestrate it", Kind: AgentTaskKind.Orchestrator,
                Role: AgentTaskRole.Plan, Workspace: WorkspaceMode.Worktree),
        };

        var created = await world.CreateTaskAsync(
            request with { WorktreeBaseRequestedRef = world.Owner.WorktreeBranch });

        created.WorktreeBaseRequestedRef.ShouldBe(world.Owner.WorktreeBranch);
        created.Workspace.ShouldBe(WorkspaceMode.Worktree);
        created.Status.ShouldBe(AgentTaskStatus.Queued);
    }
}
