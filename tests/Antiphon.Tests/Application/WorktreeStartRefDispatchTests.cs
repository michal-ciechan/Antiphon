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
        var before = await world.TaskCountAsync();
        var longest = new string('b', 300);

        // 300 characters is valid SYNTAX: it passes the selector check and is refused afterwards,
        // at create's availability check (CARD-0666), for naming nothing — a different failure
        // from "this is not a selector", and still before any row exists.
        var refused = await Should.ThrowAsync<ValidationException>(() => world.CreateTaskAsync(new CreateAgentTaskRequest(
            "long but syntactically valid", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
        { WorktreeBaseRequestedRef = longest }));

        refused.Code.ShouldBe(StartRefAvailability.NotFullShaCode);
        refused.Code.ShouldNotBe("worktree_start_ref_invalid");
        (await world.TaskCountAsync()).ShouldBe(before);
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("missing-ref", StartRefAvailability.NotFullShaCode)]
    [Arguments("blob-tag", StartRefAvailability.NotCommitCode)]
    [Arguments("sha-absent-on-origin", StartRefAvailability.NotOnOriginCode)]
    public async Task C666_CreateRefusesAStartRefItCannotMakeAvailable(string shape, string code)
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var before = await world.TaskCountAsync();
        var selector = shape switch
        {
            "missing-ref" => "no-such-ref-c613",
            "blob-tag" => "c613-blob",
            _ => "0123456789abcdef0123456789abcdef01234567",
        };
        if (shape == "blob-tag")
        {
            var blob = (await world.Repo.GitReadAsync("hash-object", "-w", "README.md")).Trim();
            await world.Repo.GitAsync("tag", selector, blob);
        }

        var refused = await Should.ThrowAsync<ValidationException>(() => world.CreateTaskAsync(new CreateAgentTaskRequest(
            "names no commit", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
        { WorktreeBaseRequestedRef = selector }));

        refused.Code.ShouldBe(code);
        refused.Message.ShouldContain($"'{selector}'");
        (await world.TaskCountAsync()).ShouldBe(before, "a start ref create cannot make available leaves no row");
    }

    [Test]
    [Timeout(120_000)]
    public async Task C666_CreateFetchesAnOriginOnlyStartShaAndDispatchCutsFromIt()
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        // Pushed from a second clone: on origin, never fetched into the server's repository.
        var sha = await world.CommitFromSecondCloneAsync("refs/heads/" + world.Owner.WorktreeBranch, "pushed from server2");
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "rev-parse", "--verify", "--quiet", sha + "^{commit}")).Ok
            .ShouldBeFalse("precondition: the start SHA must be missing locally");

        var created = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "continue server2's work", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
        { WorktreeBaseRequestedRef = sha });

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        (await ScratchGitRepo.GitInAsync(world.Repo.Path, "rev-parse", "--verify", "--quiet", sha + "^{commit}")).Ok
            .ShouldBeTrue("create fetched the start SHA before the row existed");

        var (task, _) = await world.DispatchAsync();

        task.Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await ScratchGitRepo.GitInAsync(task.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim().ShouldBe(sha);
        task.WorktreeBaseSha.ShouldBe(sha);
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("missing-ref", "no-such-ref-c613")]
    [Arguments("blob-tag", "c613-blob")]
    [Arguments("origin-only-sha", "")]
    public async Task C613_StartRefAdmissionMatrixRefusesProvisioningWithoutFallback(string shape, string selector)
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        if (shape == "blob-tag")
        {
            var blob = (await world.Repo.GitReadAsync("hash-object", "-w", "README.md")).Trim();
            await world.Repo.GitAsync("tag", selector, blob);
        }
        if (shape == "origin-only-sha")
            selector = await world.CommitFromSecondCloneAsync("refs/heads/" + world.Owner.WorktreeBranch, "only on origin");
        // Create now refuses these (CARD-0666), so the row is admitted with a good ref and the
        // selector changes underneath it: provisioning must still refuse without a fallback.
        var created = await world.CreateTaskAsync(new CreateAgentTaskRequest(
            "names no commit", Role: AgentTaskRole.Code, Workspace: WorkspaceMode.Worktree)
        { WorktreeBaseRequestedRef = world.Owner.WorktreeBranch! });
        await using (var db = world.CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
            row.WorktreeBaseRequestedRef = selector;
            await db.SaveChangesAsync();
        }

        var (task, _) = await world.DispatchAsync();

        // Refused, with NOTHING salvaged: provisioning never quietly substitutes master for a base
        // the caller explicitly named, which would hand the delegate a checkout it did not ask for.
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureReason.ShouldNotBeNull().ShouldContain("validation");
        task.FailureReason.ShouldContain($"'{selector}'");
        task.FailureReason.ShouldContain("Dispatch does not fetch");
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
        if (shape == "origin-only-sha")
            (await ScratchGitRepo.GitInAsync(world.Repo.Path, "rev-parse", "--verify", "--quiet", selector + "^{commit}")).Ok
                .ShouldBeFalse("dispatch refuses a missing start SHA without fetching it");
    }

    [Test]
    [Timeout(120_000)]
    [Arguments("shared", "worktree_start_ref_mode")]
    [Arguments("read-only", "worktree_start_ref_mode")]
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
    [Arguments("omitted-workspace")]
    public async Task C613_StartRefAdmissionMatrixAcceptsOrdinaryFreshWorktrees(string shape)
    {
        await using var world = await RepairSourceWorld.CreateAsync(ordinaryCodeTask: true, createTaskThroughService: true);
        var request = shape switch
        {
            "worker-plan" => new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan,
                Workspace: WorkspaceMode.Worktree),
            "worker-code" => new CreateAgentTaskRequest("code it", Role: AgentTaskRole.Code,
                Workspace: WorkspaceMode.Worktree),
            // CARD-0644 D-5: an omitted workspace IS the fresh Worktree a start ref needs.
            "omitted-workspace" => new CreateAgentTaskRequest("code it by default", Role: AgentTaskRole.Code),
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
