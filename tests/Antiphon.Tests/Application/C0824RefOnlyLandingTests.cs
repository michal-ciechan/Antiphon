using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)] // CARD-0824 provisional multi-step budget; first red/green supplies the measured sample.
public sealed class C0824RefOnlyLandingTests
{
    private const string Filter = "c0824-controlled-verifier";

    [Test]
    public async Task Late_land_uses_pinned_source_without_touching_decoy()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var binding = await RefOnlyWorkspaceTestSetup.BindAsync(h);
        binding.SourceSha.ShouldBe(source);
        for (var variant = 0; variant < 2; variant++)
        {
            if (variant != 0)
            {
                await ResetTargetsAsync(h);
                await h.ResetDatabaseAsync();
                binding = await RefOnlyWorkspaceTestSetup.BindAsync(h, removeAuthoring: false);
            }
            var decoyRef = "refs/heads/foreign-decoy-" + Guid.NewGuid().ToString("N");
            string? decoyAdmin = null;
            if (variant == 1)
            {
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", "-b", decoyRef[11..], binding.Identity, h.Fixture.SeedSha);
                decoyAdmin = (await h.Fixture.RequiredAsync(binding.Identity, "rev-parse", "--absolute-git-dir")).Trim();
                await File.WriteAllTextAsync(Path.Combine(binding.Identity, "keep.txt"), "foreign unique bytes\n");
                await File.WriteAllTextAsync(Path.Combine(decoyAdmin, "index.lock"), "foreign lock\n");
                await using var db = h.CreateContext();
                var id = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, Workspace = WorkspaceMode.Worktree,
                    RepoPath = h.Fixture.Repository, WorkingDirectory = h.Fixture.Repository, WorktreePath = binding.Identity,
                    WorktreeBranch = decoyRef[11..], Status = AgentTaskStatus.Succeeded, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var target = await h.Fixture.PushIndependentAsync("late-target-" + variant);
            await h.RequestAsync(Filter, source);
            await h.RestartServicesAsync();
            await h.SweepAsync(); // Lost in-memory enqueue is recovered from the committed request.
            h.Fixture.Git.Commands.Clear(); h.Fixture.Git.Trace.Clear(); h.Fixture.Git.IndexLockProbes.Clear();
            (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Complete, "publication-has-S");
            var op = (await h.OperationAsync()).ShouldNotBeNull("publication-has-S");
            op.RemoteConfirmedAt.ShouldNotBeNull("publication-has-S");
            op.OriginalSourceSha.ShouldBe(source, "candidate-is-S");
            op.GitDirectory.ShouldBeNull("source-admin=null");
            op.SourceDesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly);
            op.SourceTaskRefWorkspaceBindingId.ShouldBe(binding.Id);
            h.Fixture.Git.Commands.ShouldNotContain(c => Same(c.Directory, binding.Identity), "no-source-cwd");
            h.Fixture.Git.IndexLockProbes.ShouldNotContain(p => Same(p, binding.Identity), "no-source-probe");
            await using (var db = h.CreateContext())
                (await db.AgentTaskLandRequests.SingleAsync(r => r.Id == op.ApprovalLandRequestId)).SourceGitDirectory
                    .ShouldBeNull("request-source-admin=null");
            // Independent remote observer; rebase preserves the reviewed bytes and the independently advanced target.
            await h.Fixture.RequiredAsync(h.Fixture.Observer, "fetch", "origin", "refs/heads/master");
            var observed = (await h.Fixture.RequiredAsync(h.Fixture.Observer, "rev-parse", "FETCH_HEAD")).Trim();
            observed.ShouldBe(op.VerifiedSourceSha, "publication-has-S");
            (await h.Fixture.Git.RunAsync(h.Fixture.Observer, ["merge-base", "--is-ancestor", target, observed], CancellationToken.None))
                .Succeeded.ShouldBeTrue("publication-preserves-target-ancestry");
            (await h.Fixture.RequiredAsync(h.Fixture.Observer, "show", observed + ":feature.txt")).ShouldBe("valuable feature\n", "publication-has-S");
            await AssertSourceRetainedAsync(h, source);
            if (variant == 1)
            {
                (await File.ReadAllTextAsync(Path.Combine(binding.Identity, "keep.txt"))).ShouldBe("foreign unique bytes\n");
                (await File.ReadAllTextAsync(Path.Combine(decoyAdmin!, "index.lock"))).ShouldBe("foreign lock\n");
                (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", decoyRef)).Trim().ShouldBe(h.Fixture.SeedSha);
                (await h.Fixture.Git.LiveRegistrationsAsync(h.Fixture.Repository, CancellationToken.None))
                    .ShouldContain(r => r.Branch == decoyRef && Same(r.Path, binding.Identity));
            }
            else Path.Exists(binding.Identity).ShouldBeFalse();
        }
    }

    [Test]
    public async Task Verification_resume_binds_mode_and_binding()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var variants = new[] { "task-mode", "task-binding", "op-mode", "op-binding", "ticket-mode", "ticket-binding", "task-ref", "task-repo", "valid" };
        foreach (var variant in variants)
        {
            if (variant != variants[0]) await h.ResetDatabaseAsync();
            var binding = await RefOnlyWorkspaceTestSetup.BindAsync(h, removeAuthoring: variant == variants[0]);
            await h.RequestAsync(Filter, source);
            if (variant.StartsWith("ticket-", StringComparison.Ordinal))
            {
                var result = await ResumeWithChangedTicketAsync(h, variant);
                result.Published.ShouldBeFalse("resume-refused:" + variant);
                result.Reason.ShouldBe("verification_ticket_changed", "resume-refused:" + variant);
            }
            else
            {
                var reached = false;
                h.Verifier.Barrier = async () =>
                {
                    reached = true;
                    var leases = h.Services.GetRequiredService<IRepositoryMutationLease>();
                    await using (var gap = await leases.TryAcquirePreflightAsync(h.Fixture.Repository,
                                     RepositoryPreflight.Fetch(Guid.NewGuid(), source), new(null, "c0824-window-witness"), CancellationToken.None))
                        gap.ShouldNotBeNull("verification-released-repository-lease");
                    await using var db = h.CreateContext();
                    var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
                    var op = await db.AgentTaskLandings.SingleAsync(o => o.TaskId == task.Id && o.Active);
                    if (variant == "task-mode") task.DesktopWorkspaceKind = DesktopWorkspaceKind.Checkout;
                    if (variant == "task-binding")
                    {
                        var changed = Guid.NewGuid();
                        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"TaskRefWorkspaceBindings\" SET \"Id\" = {changed} WHERE \"Id\" = {binding.Id}");
                        task.TaskRefWorkspaceBindingId = changed;
                    }
                    if (variant == "op-mode") op.SourceDesktopWorkspaceKind = DesktopWorkspaceKind.Checkout;
                    if (variant == "op-binding") op.SourceTaskRefWorkspaceBindingId = Guid.NewGuid();
                    if (variant == "task-ref") task.WorktreeBranch = "foreign-ref";
                    if (variant == "task-repo") task.RepoPath = h.Fixture.Remote;
                    await db.SaveChangesAsync();
                };
                await h.RunQueuedAsync();
                reached.ShouldBeTrue("verification-boundary-reached:" + variant);
                var op = (await h.OperationAsync()).ShouldNotBeNull();
                if (variant == "valid") op.RemoteConfirmedAt.ShouldNotBeNull("valid-frozen-tuple-lands");
                else
                {
                    op.RemoteConfirmedAt.ShouldBeNull("resume-refused:" + variant);
                    op.LastReason.ShouldBe("task_coordinates_changed", "resume-refused:" + variant);
                }
            }
            if (variant != "valid")
                (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", "refs/heads/master")).Trim().ShouldBe(h.Fixture.SeedSha, "target-unpublished:" + variant);
            await AssertSourceRetainedAsync(h, source);
        }
    }

    [Test]
    public async Task Foreign_source_registration_holds_without_mutation()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        foreach (var variant in new[] { "initial", "resume" })
        {
            if (variant == "resume") await h.ResetDatabaseAsync();
            await RefOnlyWorkspaceTestSetup.BindAsync(h, removeAuthoring: variant == "initial");
            var foreign = Path.Combine(h.Fixture.Root, "foreign-source-" + variant);
            async Task AttachAsync() { await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "add", foreign, h.Fixture.SourceRef); }
            if (variant == "initial") await AttachAsync();
            else h.Verifier.Barrier = AttachAsync;
            await h.RequestAsync(Filter, source);
            h.Fixture.Git.Trace.Clear();
            await h.RunQueuedAsync();
            var op = await h.OperationAsync();
            await using var db = h.CreateContext();
            var request = await db.AgentTaskLandRequests.OrderByDescending(r => r.RequestedAt).FirstAsync();
            (op?.LastReason ?? request.SourceRefusalReason).ShouldStartWith("ref_only_source_registered", customMessage: "source-registration-refused:" + variant);
            (op?.RemoteConfirmedAt).ShouldBeNull();
            h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("reset") || a.Contains("remove"));
            (await File.ReadAllTextAsync(Path.Combine(foreign, "feature.txt"))).ShouldBe("valuable feature\n");
            await AssertSourceRetainedAsync(h, source);
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "remove", foreign);
        }
    }

    [Test]
    public async Task Recovery_and_interrupted_reset_refuse_before_any_write()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var binding = await RefOnlyWorkspaceTestSetup.BindAsync(h);
        foreach (var variant in new[] { "reviewed", "adopt", "resumed-reset", "dead-child", "adopted" })
        {
            if (variant != "reviewed")
            {
                await h.ResetDatabaseAsync();
                binding = await RefOnlyWorkspaceTestSetup.BindAsync(h, removeAuthoring: false);
            }
            h.Fixture.Git.Trace.Clear(); h.Fixture.Git.Commands.Clear();
            if (variant is "reviewed" or "adopt")
            {
                var authority = await RecoveryAuthorityAsync(h, binding.SourceSha, variant == "adopt");
                var queued = await h.RequestAsync(Filter, binding.SourceSha, authority.ReviewId,
                    recoverReviewedSource: variant == "reviewed", adoptFromTaskId: authority.SourceId);
                h.Fixture.Git.Trace.Clear(); h.Fixture.Git.Commands.Clear();
                await h.RunQueuedAsync();
                await using var observed = h.CreateContext();
                (await observed.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId)).SourceRefusalReason
                    .ShouldStartWith("ref_only_land_recovery_unsupported", customMessage: "ref_only_land_recovery_unsupported:" + variant);
            }
            else
            {
                var queued = await h.RequestAsync(Filter, binding.SourceSha);
                await using (var db = h.CreateContext())
                {
                    var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId);
                    request.RecoveryMode = LandRecoveryMode.AdoptReviewedSource;
                    request.SourceAdvanceChildOperation = "source-adopt-reset";
                    request.SourceResolutionState = LandSourceResolutionState.AdvanceStarted;
                    request.RecoveryAdoptedAt = variant == "adopted" ? DateTime.UtcNow : null;
                    if (variant == "dead-child")
                    {
                        request.SourceAdvanceChildProcessId = int.MaxValue;
                        request.SourceAdvanceChildStartTicks = 1;
                    }
                    await db.SaveChangesAsync();
                }
                h.Fixture.Git.Trace.Clear(); h.Fixture.Git.Commands.Clear();
                await h.RunQueuedAsync();
                await using var observed = h.CreateContext();
                (await observed.AgentTaskLandRequests.SingleAsync(r => r.Id == queued.RequestId)).SourceRefusalReason
                    .ShouldStartWith("ref_only_land_recovery_unsupported", customMessage: "ref_only_land_recovery_unsupported:resumed-reset");
            }
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("reset") || a.Contains("push") || a.Contains("update-ref") || a.Contains("fetch"), "recovery-writes=0");
            Path.Exists(binding.Identity).ShouldBeFalse();
            await AssertSourceRetainedAsync(h, binding.SourceSha);
        }
    }

    [Test]
    public async Task Main_and_land_guards_still_fence_ref_only_publication()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        foreach (var variant in new[] { "wrong-approved-S", "main-lock", "endpoint-drift", "land-lock", "land-sequencer", "land-dirt", "valid" })
        {
            if (variant != "wrong-approved-S") await h.ResetDatabaseAsync();
            var binding = await RefOnlyWorkspaceTestSetup.BindAsync(h, removeAuthoring: variant == "wrong-approved-S");
            var mainLock = Path.Combine(binding.Common, "index.lock");
            var reached = false;
            h.Verifier.Barrier = null;
            if (variant == "main-lock") await File.WriteAllTextAsync(mainLock, "fixture main lock");
            if (variant is "endpoint-drift" or "land-lock" or "land-sequencer" or "land-dirt")
                h.Verifier.Barrier = async () =>
                {
                    reached = true;
                    var op = (await h.OperationAsync()).ShouldNotBeNull();
                    var admin = (await h.Fixture.RequiredAsync(op.LandWorktreePath!, "rev-parse", "--absolute-git-dir")).Trim();
                    if (variant == "endpoint-drift")
                    {
                        await using var db = h.CreateContext();
                        var persisted = await db.AgentTaskLandings.SingleAsync(o => o.Id == op.Id);
                        persisted.RemoteFingerprint = new string('f', 64); await db.SaveChangesAsync();
                    }
                    if (variant == "land-lock") await File.WriteAllTextAsync(Path.Combine(admin, "index.lock"), "fixture land lock");
                    if (variant == "land-sequencer") Directory.CreateDirectory(Path.Combine(admin, "sequencer"));
                    if (variant == "land-dirt") await File.AppendAllTextAsync(Path.Combine(op.LandWorktreePath!, "keep.txt"), "late dirt\n");
                };
            await h.RequestAsync(Filter, variant == "wrong-approved-S" ? h.Fixture.SeedSha : source);
            try
            {
                await h.RunAsync();
                var op = await h.OperationAsync();
                if (variant == "valid")
                {
                    op.ShouldNotBeNull().RemoteConfirmedAt.ShouldNotBeNull("candidate-is-S");
                    op.OriginalSourceSha.ShouldBe(source);
                }
                else
                {
                    (op?.RemoteConfirmedAt).ShouldBeNull("target-unpublished:" + variant);
                    await using var db = h.CreateContext();
                    var request = await db.AgentTaskLandRequests.OrderByDescending(r => r.RequestedAt).FirstAsync();
                    var actual = op?.LastReason ?? request.SourceRefusalReason ?? request.TerminalFailureCode ?? request.HoldReasonCode;
                    var expected = variant switch
                    {
                        "wrong-approved-S" => "reviewed_source_mismatch", "main-lock" or "land-lock" => "git_index_lock_held",
                        "endpoint-drift" => "verification_ticket_changed", "land-sequencer" => "verification_sequencer_changed",
                        "land-dirt" => "verification_tracked_tree_changed", _ => throw new InvalidOperationException(),
                    };
                    actual.ShouldStartWith(expected, customMessage: "target-unpublished:" + variant);
                    if (variant is "endpoint-drift" or "land-lock" or "land-sequencer" or "land-dirt")
                        reached.ShouldBeTrue("guard-boundary-reached:" + variant);
                    (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", "refs/heads/master")).Trim()
                        .ShouldBe(h.Fixture.SeedSha, "target-unpublished:" + variant);
                }
                await AssertSourceRetainedAsync(h, source);
            }
            finally
            {
                if (variant == "main-lock") File.Delete(mainLock);
                var op = await h.OperationAsync();
                if (op?.LandWorktreePath is { } land && Directory.Exists(land))
                {
                    var admin = (await h.Fixture.RequiredAsync(land, "rev-parse", "--absolute-git-dir")).Trim();
                    if (variant == "land-lock") File.Delete(Path.Combine(admin, "index.lock"));
                    if (variant == "land-sequencer") Directory.Delete(Path.Combine(admin, "sequencer"));
                    if (variant == "land-dirt") await h.Fixture.RequiredAsync(land, "reset", "--hard", source);
                }
            }
        }
    }

    private static async Task<LandingProtocolResult> ResumeWithChangedTicketAsync(LandingSafetyHarness h, string variant)
    {
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == task.CurrentLandRequestId);
        request.State = LandRequestState.Running; task.LandAttempt = request.Attempt; task.LandStartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var leases = h.Services.GetRequiredService<IRepositoryMutationLease>();
        var protocol = scope.ServiceProvider.GetRequiredService<AgentTaskLandingProtocol>();
        PreparedLandingVerification ticket;
        await using (var lease = (await leases.TryAcquireAsync(h.Fixture.Repository, CancellationToken.None)).ShouldNotBeNull())
        {
            var resolution = await new AgentTaskLandSourceResolver(db, h.Fixture.Git, leases, h.Clock, landWorkspace: protocol.LandWorkspace)
                .ResolveAsync(task, request, lease, CancellationToken.None);
            resolution.Reason.ShouldBeNull();
            var prepared = await protocol.PrepareAsync(task, lease, request, CancellationToken.None);
            ticket = prepared.Verification.ShouldNotBeNull("verification-boundary-reached:" + variant);
            ticket.SourceDesktopWorkspaceKind.ShouldBe(DesktopWorkspaceKind.RefOnly, "ticket-mode-captured");
        }
        ticket = variant == "ticket-mode" ? ticket with { SourceDesktopWorkspaceKind = DesktopWorkspaceKind.Checkout }
            : ticket with { SourceTaskRefWorkspaceBindingId = Guid.NewGuid() };
        await using var resumedLease = (await leases.TryAcquireAsync(h.Fixture.Repository, CancellationToken.None)).ShouldNotBeNull();
        await db.Entry(task).ReloadAsync(); await db.Entry(request).ReloadAsync();
        return await protocol.ResumeAsync(task, resumedLease, request, ticket, new(true, "controlled verifier"), CancellationToken.None);
    }

    private static async Task<(Guid ReviewId, Guid? SourceId)> RecoveryAuthorityAsync(LandingSafetyHarness h, string sha, bool adopt)
    {
        await using var db = h.CreateContext();
        var owner = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        var subject = owner;
        if (adopt)
        {
            var now = DateTime.UtcNow;
            var project = new Project { Id = Guid.NewGuid(), Name = "c0824 recovery", LocalRepositoryPath = h.Fixture.Repository, CreatedAt = now, UpdatedAt = now };
            var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "c0824 recovery", CreatedAt = now, UpdatedAt = now };
            var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Ready", StateKey = "ready", CreatedAt = now, UpdatedAt = now };
            var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id, Identifier = "CARD-0824", Title = "fixture", CreatedAt = now, UpdatedAt = now };
            db.Projects.Add(project); db.Boards.Add(board); db.BoardColumns.Add(column); db.Cards.Add(card);
            owner.CardId = card.Id; owner.ProjectId = project.Id;
            var id = Guid.NewGuid();
            var branch = "feat/card-task-" + id.ToString("N");
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "refs/heads/" + branch, sha);
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", "refs/heads/" + branch);
            subject = new AgentTask { Id = id, RootTaskId = id, Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code,
                Workspace = WorkspaceMode.Worktree, Status = AgentTaskStatus.Succeeded, ProjectId = project.Id, CardId = card.Id,
                RepoPath = h.Fixture.Repository, WorkingDirectory = h.Fixture.Repository, WorktreePath = Path.Combine(h.Fixture.Root, "repair-source"),
                WorktreeBranch = branch, WorktreeBaseSha = h.Fixture.SeedSha, CreatedAt = now, CompletedAt = now };
            db.AgentTasks.Add(subject);
        }
        var review = new StageOutcome { Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Delegate, SubjectTaskId = subject.Id, ReviewedSourceSha = sha,
            ReviewedSourceClean = true, ReviewedRepositoryPath = h.Fixture.Repository, ReviewedSourceRef = "refs/heads/" + subject.WorktreeBranch,
            CommissionedRound = VerificationRound.Final, OrdinaryScopeCompleted = VerificationScope.Full, RecordedAt = DateTime.UtcNow };
        db.StageOutcomes.Add(review); await db.SaveChangesAsync();
        return (review.Id, adopt ? subject.Id : null);
    }

    private static async Task AssertSourceRetainedAsync(LandingSafetyHarness h, string source)
    {
        var local = await h.Fixture.Git.RunAsync(h.Fixture.Repository, ["show-ref", "--verify", "--hash", h.Fixture.SourceRef], CancellationToken.None);
        local.Succeeded.ShouldBeTrue("source-ref-and-pins-retained");
        local.Output.Trim().ShouldBe(source, "source-ref-and-pins-retained");
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(source);
    }
    private static async Task ResetTargetsAsync(LandingSafetyHarness h)
    {
        await h.Fixture.RequiredAsync(h.Fixture.Remote, "update-ref", "refs/heads/master", h.Fixture.SeedSha);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "reset", "--hard", h.Fixture.SeedSha);
    }
    private static bool Same(string left, string right) => WorkspaceReservationKey.PathsEqual(left, right);
}
