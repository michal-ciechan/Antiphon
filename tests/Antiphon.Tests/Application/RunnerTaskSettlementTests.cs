using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0657 S2. The real reply service settling a runner-bound Worktree task: the owning prompt,
// the delegate's report and a TurnEnd go in, and the desktop checkout is synchronized to the
// task's own pushed commit BEFORE anything attributes progress, resolves files or counts commits.
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RunnerTaskSettlementTests
{
    [Test]
    public async Task Runner_push_settles_success_without_claim()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var s = await world.Git.RunnerPushAsync("work.txt", "runner work");

        await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Task.FailureCode.ShouldBeNull();
        var evidence = world.Evidence()!;
        evidence.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved);
        var primary = evidence.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved);
        primary.Origin.ShouldBe(ProgressOrigin.Primary);
        primary.VerifiedSha.ShouldBe(s);
        evidence.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        evidence.RemoteSync.ConfirmedSha.ShouldBe(s);
        (await world.NoProgressIncidentsAsync()).ShouldBe(0);
        (await world.Git.HeadAsync()).ShouldBe(s);
        world.Task.ProgressBaselineJson.ShouldBe(world.Git.Task.ProgressBaselineJson);
        world.Task.WorktreeBaseSha.ShouldBe(world.Git.Baseline);

        var note = await world.NoteAsync();
        note.ShouldNotBeNull();
        note!.Body.ShouldContain(s);
        note.Body.ShouldContain(DelegationGitFacts.FormatHeader(1, 1));
        (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains("runner_sync", StringComparison.Ordinal));
    }

    [Test]
    public async Task No_push_settles_with_specific_reason()
    {
        // The branch is on origin, still at B: nothing new was pushed.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            await world.SettleAsync(RunnerSettlementWorld.Report("Done, all pushed."));
            AssertNoPush(world, RemoteSettlementSyncReasons.NoPushedProgress, "Done, all pushed.");
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged);
        }

        // The exact branch never reached origin at all.
        await using (var world = await RunnerSettlementWorld.CreateAsync(pushBranch: false))
        {
            await world.SettleAsync(RunnerSettlementWorld.Report("Done."));
            AssertNoPush(world, RemoteSettlementSyncReasons.BranchNotPushed, "Done.");
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged);
        }

        // The report claims C, but only an earlier S reached origin.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            var s = await world.Git.RunnerPushAsync("work.txt", "pushed");
            File.WriteAllText(Path.Combine(world.Git.Runner, "later.txt"), "unpushed");
            await world.Git.RunAsync(world.Git.Runner, "add", "later.txt");
            await world.Git.RunAsync(world.Git.Runner, "commit", "-m", "unpushed");
            var c = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");

            await world.SettleAsync(RunnerSettlementWorld.Report("Done.\n" + world.Claim(c)));

            AssertNoPush(world, RemoteSettlementSyncReasons.ReportedCommitNotPushed, "Done.");
            world.Evidence()!.ClaimedSha.ShouldBe(c);
            world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
            (await world.Git.HasObjectAsync(c)).ShouldBeFalse();
        }
    }

    [Test]
    public async Task Unmarked_completion_gets_the_code_progress_policy()
    {
        // (a) An unmarked report claims C, but only an earlier S reached origin.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            var s = await world.Git.RunnerPushAsync("work.txt", "pushed");
            File.WriteAllText(Path.Combine(world.Git.Runner, "later.txt"), "unpushed");
            await world.Git.RunAsync(world.Git.Runner, "add", "later.txt");
            await world.Git.RunAsync(world.Git.Runner, "commit", "-m", "unpushed");
            var c = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");
            await world.EndDelegateSessionAsync();

            await world.SettleAsync(RunnerSettlementWorld.Report("Done.\n" + world.Claim(c)), closingVerdict: false);

            AssertNoPush(world, RemoteSettlementSyncReasons.ReportedCommitNotPushed, "Done.");
            world.Task.ReportEvidence.ShouldBe(AgentTaskReportEvidence.UnmarkedAfterNudge);
            world.Evidence()!.ClaimedSha.ShouldBe(c);
            world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Completed);
        }

        // (b) An unmarked report after nothing new was pushed: the branch is still at B.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            await world.EndDelegateSessionAsync();

            await world.SettleAsync(RunnerSettlementWorld.Report("Done, all pushed."), closingVerdict: false);

            AssertNoPush(world, RemoteSettlementSyncReasons.NoPushedProgress, "Done, all pushed.");
            world.Task.ReportEvidence.ShouldBe(AgentTaskReportEvidence.UnmarkedAfterNudge);
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Completed);
        }
    }

    [Test]
    public async Task Bind_refusal_recovery_blocks_an_unconfirmed_runner_branch()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var older = await world.Git.RunnerPushAsync("work.txt", "runner work");
        var s = await world.Git.RunnerPushAsync("more.txt", "later runner work");
        world.Git.Git.Clear();
        var replies = world.Services.GetRequiredService<AgentTaskReplyService>();

        // The evidence names a commit, but origin's tip is a later one: the named commit is not the
        // pushed tip, so nothing correlates the tip with this task's work.
        await world.RecoverAsync(new DelegateBindRefusalEvidence([older], null));

        // Blocked with a repair handoff, not Succeeded, and the checkout was never fast-forwarded.
        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Task.FailureCode.ShouldBeNull();
        world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
        world.Task.NextHandoff!.ShouldContain("fresh completion report");
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.TipNotReported);
        world.Evidence()!.RemoteSync!.ObservedSha.ShouldBe(s);
        world.Git.Git.Commands.ShouldNotContain(x => IsMergeCommand(x));
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged
            || e.Type == AgentTaskEventType.Completed);
        world.Services.GetRequiredService<RecordingSessionStopper>().Killed.ShouldBeEmpty();
        Directory.Exists(world.Git.Worktree).ShouldBeTrue();
        world.Task.WorktreePath.ShouldBe(world.Git.Task.WorktreePath);

        // Downstream: the caller is told to decide, never to review, and land refuses the branch.
        var note = await world.NoteAsync();
        note.ShouldNotBeNull();
        note!.Body.ShouldContain("next=decide");
        note.Body.ShouldNotContain("next=review");
        await using (var scope = world.Services.CreateAsyncScope())
        {
            await Should.ThrowAsync<ConflictException>(() => scope.ServiceProvider
                .GetRequiredService<AgentTaskLandService>()
                .RequestAsync(world.TaskId, new LandAgentTaskRequest(), CancellationToken.None));
        }

        // Only a fresh, correlated report confirms S.
        await replies.AnswerAsync(world.TaskId, "Report the pushed commit.", AnswerOrigin.Cli, null, CancellationToken.None);
        await world.ReloadAsync();
        world.Task.Status.ShouldBe(AgentTaskStatus.Working);
        await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."),
            DelegationReportFormatter.TaskMarker(world.TaskId) + "\n\nReport the pushed commit.");

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
    }

    [Test]
    public async Task Bind_refusal_recovery_confirms_a_named_pushed_tip()
    {
        // (a) The recovery's git evidence names the pushed tip (abbreviated, as `log --oneline` does);
        // (b) the recovered transcript's done report names it in full.
        foreach (var row in new[] { "git-evidence", "reported-transcript" })
        {
            await using var world = await RunnerSettlementWorld.CreateAsync();
            var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
            var evidence = row == "git-evidence"
                ? new DelegateBindRefusalEvidence([s[..12]], null)
                : new DelegateBindRefusalEvidence([], world.WriteReportedTranscript($"Implemented and pushed {s}."));

            await world.RecoverAsync(evidence);

            // Settled like a correlated report: the own branch's tip is the named commit and descends from B.
            world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, row + ": " + Why(world));
            world.Task.FailureCode.ShouldBeNull(row);
            world.Task.NextStage.ShouldNotBe(PipelineHandoffKind.Decide, row);
            var progress = world.Evidence().ShouldNotBeNull(row);
            progress.Assessment.ShouldBe(CompletionProgressAssessment.ProgressObserved, row);
            progress.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved)
                .VerifiedSha.ShouldBe(s, row);
            progress.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Synchronized, row);
            progress.RemoteSync.ConfirmedSha.ShouldBe(s, row);
            (await world.Git.HeadAsync()).ShouldBe(s, row);
            world.Task.ProgressBaselineJson.ShouldBe(world.Git.Task.ProgressBaselineJson, row);
            var events = await world.EventsAsync();
            events.ShouldContain(e => e.Type == AgentTaskEventType.Merged && e.Detail.Contains(s, StringComparison.Ordinal), row);
            events.ShouldNotContain(e => e.Type == AgentTaskEventType.Blocked, row);
            (await world.NoProgressIncidentsAsync()).ShouldBe(0, row);
            var note = await world.NoteAsync();
            note.ShouldNotBeNull(row);
            note!.Body.ShouldContain(s, Case.Sensitive, row);
            note.Body.ShouldNotContain("next=decide", Case.Sensitive, row);
        }
    }

    [Test]
    public async Task Bind_refusal_recovery_stays_blocked_unless_the_named_tip_descends()
    {
        // The named commit IS origin's tip, but it does not descend from the dispatch base.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            await world.Git.EnsureRunnerAsync();
            await world.Git.RunAsync(world.Git.Runner, "checkout", "--orphan", "stray");
            File.WriteAllText(Path.Combine(world.Git.Runner, "stray.txt"), "unrelated history");
            await world.Git.RunAsync(world.Git.Runner, "add", "stray.txt");
            await world.Git.RunAsync(world.Git.Runner, "commit", "-m", "stray");
            var stray = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");
            await world.Git.RunAsync(world.Git.Runner, "push", "--force", "origin", "HEAD:" + world.Git.FullRef);

            await world.RecoverAsync(new DelegateBindRefusalEvidence([stray], null));

            world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
            world.Task.FailureCode.ShouldBeNull();
            world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
            world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.Diverged);
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
            (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged);
        }

        // The transcript reported done but named no commit: nothing to confirm, and nothing is fetched.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
            world.Git.Git.Clear();

            await world.RecoverAsync(new DelegateBindRefusalEvidence([], world.WriteReportedTranscript("All done and pushed.")));

            world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
            world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
            AssertNoSync(world, s);
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        }
    }

    [Test]
    public async Task Bind_refusal_recovery_commits_the_completion_obligation()
    {
        // Blocked (nothing named) and Succeeded (the named tip confirmed) recoveries of a profile-v1
        // task both owe the caller the durable CARD-0544 D-9 completion, committed with the settlement.
        foreach (var confirm in new[] { false, true })
        {
            var row = confirm ? "confirmed" : "blocked";
            await using var world = await RunnerSettlementWorld.CreateAsync(profiled: true);
            var s = await world.Git.RunnerPushAsync("work.txt", "runner work");

            await world.RecoverAsync(new DelegateBindRefusalEvidence(confirm ? [s] : [], null));

            world.Task.Status.ShouldBe(confirm ? AgentTaskStatus.Succeeded : AgentTaskStatus.Blocked, row + ": " + Why(world));
            var settlement = (await world.EventsAsync()).Single(e =>
                e.Type == (confirm ? AgentTaskEventType.Completed : AgentTaskEventType.Blocked)
                && e.Detail == world.Task.Result);
            var obligation = (await world.ObligationsAsync()).ShouldHaveSingleItem(row);
            obligation.SourceEventId.ShouldBe(settlement.Id, row);
            obligation.ParentSessionId.ShouldBe(world.CallerSessionId, row);
            var snapshot = TaskCompletionNotification.TryReadSnapshot(obligation.CompletionSnapshotJson).ShouldNotBeNull(row);
            snapshot.TaskId.ShouldBe(world.TaskId, row);
            snapshot.SourceEventId.ShouldBe(settlement.Id, row);
            snapshot.Status.ShouldBe(world.Task.Status, row);
            snapshot.RawResult.ShouldBe(world.Task.Result, row);
            snapshot.ProfileVersion.ShouldBe(1, row);
            snapshot.Round.ShouldBe(VerificationRound.Final, row);
            snapshot.NoteHeader.ShouldContain("verification=Final", Case.Sensitive, row);
            if (confirm)
                snapshot.NoteHeader.ShouldNotContain("next=decide", Case.Sensitive, row);
            else
            {
                snapshot.NextStage.ShouldBe("decide", row);
                snapshot.NoteHeader.ShouldContain("next=decide", Case.Sensitive, row);
            }
            obligation.Body.ShouldStartWith(snapshot.NoteHeader, Case.Sensitive, row);

            // Delivered through the outbox, not the legacy direct note.
            obligation.State.ShouldBe(LandNotificationState.AwaitingReceipt, row);
            var queued = (await world.QueuedForAsync(obligation.Id)).ShouldNotBeNull(row);
            queued.AgentSessionId.ShouldBe(world.CallerSessionId, row);
            queued.Body.ShouldBe(obligation.Body, row);
            obligation.QueueMessageId.ShouldBe(queued.Id, row);
        }
    }

    [Test]
    public async Task Settlement_sync_fences_a_competing_retirement()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(fenced: true);
        var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
        var journal = new WorkspaceReservationJournal(
            world.Services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
        // Retirement's own coordinates: worktree path, source full ref, repository path.
        var retirement = new WorkspaceReservationCommand(
            WorkspaceReservationKey.For(world.Git.Task.WorktreePath, world.Git.FullRef, world.Git.Task.RepoPath),
            WorkspaceReservationKind.Retirement, world.TaskId, RetirementId: Guid.NewGuid());
        world.Git.Git.BlockOn = args => args.Contains("--ff-only");

        // Settlement's sync holds the consumer slot while the fast-forward is in flight.
        var settle = world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
        (await System.Threading.Tasks.Task.WhenAny(world.Git.Git.Entered, settle)).ShouldBe(world.Git.Git.Entered);
        var during = await journal.TryClaimRetirementAsync(retirement, CancellationToken.None);
        world.Git.Git.OpenGate();
        await settle;

        during.Accepted.ShouldBeFalse();
        during.Reason.ShouldBe("workspace_in_use");
        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Synchronized);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
        world.Git.Git.Blocked.ShouldBe(1);
    }

    [Test]
    public async Task Sync_uncertainty_blocks_and_reply_retries()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync();
        var s = await world.Git.RunnerPushAsync("work.txt", "runner work");

        await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));
        }

        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Task.FailureCode.ShouldBeNull();
        world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
        world.Task.Result!.ShouldContain("Implemented and pushed.");
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Unavailable);
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy);
        (await world.EventsAsync()).ShouldContain(e => e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains(RemoteSettlementSyncReasons.LeaseBusy, StringComparison.Ordinal));
        (await world.NoProgressIncidentsAsync()).ShouldBe(0);
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);

        // The existing reply path: a new prompt watermark, never a replay of the old done token.
        var replies = world.Services.GetRequiredService<AgentTaskReplyService>();
        await replies.AnswerAsync(world.TaskId, "The lease is free again; report once more.",
            AnswerOrigin.Cli, null, CancellationToken.None);
        await replies.OnTurnEndAsync(world.SessionId, CancellationToken.None);
        await world.ReloadAsync();
        world.Task.Status.ShouldBe(AgentTaskStatus.Working);
        (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);

        await world.SettleAsync(RunnerSettlementWorld.Report("Reported again."),
            DelegationReportFormatter.TaskMarker(world.TaskId) + "\n\nThe lease is free again; report once more.");

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
        world.Evidence()!.Sources!.Single(x => x.Assessment == CompletionProgressAssessment.ProgressObserved)
            .VerifiedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
    }

    [Test]
    public async Task Refused_sync_never_autosaves_or_releases_workspace()
    {
        // Dirt on the desktop checkout.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            await world.Git.RunnerPushAsync("work.txt", "runner work");
            File.WriteAllText(Path.Combine(world.Git.Worktree, "scratch.txt"), "desktop dirt");

            await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));

            await AssertRefusedAsync(world, RemoteSettlementSyncReasons.Dirty);
            File.ReadAllText(Path.Combine(world.Git.Worktree, "scratch.txt")).ShouldBe("desktop dirt");
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        }

        // A desktop commit the runner never saw: divergent histories.
        await using (var world = await RunnerSettlementWorld.CreateAsync())
        {
            await world.Git.RunnerPushAsync("work.txt", "runner work");
            var local = await world.Git.DesktopCommitAsync("desktop.txt", "desktop only");

            await world.SettleAsync(RunnerSettlementWorld.Report("Implemented and pushed."));

            await AssertRefusedAsync(world, RemoteSettlementSyncReasons.Diverged);
            (await world.Git.HeadAsync()).ShouldBe(local);
        }
    }

    [Test]
    public async Task Uncorrelated_and_nonfinal_turns_never_fetch()
    {
        foreach (var verdict in new[] { "blocked", "failed" })
        {
            await using var world = await RunnerSettlementWorld.CreateAsync();
            var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
            world.Git.Git.Clear();

            await world.SettleAsync("I stopped here.\n" + DelegationReportFormatter.ReportToken(world.TaskId, verdict));

            world.Task.Status.ShouldBe(verdict == "blocked" ? AgentTaskStatus.Blocked : AgentTaskStatus.Failed);
            world.Task.FailureCode.ShouldBeNull();
            AssertNoSync(world, s);
            (await world.Git.HasObjectAsync(s)).ShouldBeFalse();
        }

        // A foreign prompt's report, then an unmarked answer to the owning prompt (nudged, not final).
        {
            await using var world = await RunnerSettlementWorld.CreateAsync();
            var s = await world.Git.RunnerPushAsync("work.txt", "runner work");
            world.Git.Git.Clear();

            await world.SettleAsync(RunnerSettlementWorld.Report("Someone else's work."),
                DelegationReportFormatter.TaskMarker(Guid.NewGuid()));
            world.Task.Status.ShouldBe(AgentTaskStatus.Working);
            AssertNoSync(world, s);

            await TurnSeeding.SeedTurnAsync(world.CreateContext, world.SessionId,
                DelegationReportFormatter.TaskMarker(world.TaskId), "Still working on it.", closingVerdict: false);
            await world.Services.GetRequiredService<AgentTaskReplyService>().OnTurnEndAsync(world.SessionId, CancellationToken.None);
            await world.ReloadAsync();
            world.Task.Status.ShouldBe(AgentTaskStatus.Working);
            AssertNoSync(world, s);
            (await world.Git.HeadAsync()).ShouldBe(world.Git.Baseline);
        }
    }

    [Test]
    public async Task Artifact_and_git_summary_use_fetched_commit()
    {
        await using var world = await RunnerSettlementWorld.CreateAsync(AgentTaskRole.Plan);
        await world.Git.EnsureRunnerAsync();
        Directory.CreateDirectory(Path.Combine(world.Git.Runner, "docs"));
        var s = await world.Git.RunnerPushAsync(Path.Combine("docs", "plan.md"), "the plan\n");
        // A stale same-named file in the main checkout must not stand in for the pushed one.
        Directory.CreateDirectory(Path.Combine(world.Git.Desktop, "docs"));
        File.WriteAllText(Path.Combine(world.Git.Desktop, "docs", "plan.md"), "stale\n");

        await world.SettleAsync(RunnerSettlementWorld.Report("Plan written.", next: "code", artifact: "docs/plan.md"));

        world.Task.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(world));
        world.Task.DeliverablePath.ShouldBe("docs/plan.md");
        world.Task.DeliverableRef.ShouldBe(s);
        world.Evidence()!.RemoteSync!.ConfirmedSha.ShouldBe(s);
        (await world.Git.HeadAsync()).ShouldBe(s);
        var note = await world.NoteAsync();
        note!.Body.ShouldContain(DelegationGitFacts.FormatHeader(1, 1));
    }

    private static void AssertNoPush(RunnerSettlementWorld world, string reason, string body)
    {
        world.Task.Status.ShouldBe(AgentTaskStatus.Failed, Why(world));
        world.Task.FailureCode.ShouldBe(AgentTaskFailureCode.CompletedWithoutProgress);
        world.Task.FailureReason!.ShouldContain(reason);
        world.Evidence()!.Assessment.ShouldBe(CompletionProgressAssessment.NoAttributedProgress);
        world.Evidence()!.Reason.ShouldBe(reason);
        world.Task.Result!.ShouldContain(body);
        Directory.Exists(world.Git.Worktree).ShouldBeTrue();
    }

    private static async Task AssertRefusedAsync(RunnerSettlementWorld world, string reason)
    {
        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, Why(world));
        world.Task.FailureCode.ShouldBeNull();
        world.Task.NextStage.ShouldBe(PipelineHandoffKind.Decide);
        world.Evidence()!.RemoteSync!.State.ShouldBe(RemoteSettlementSyncState.Refused);
        world.Evidence()!.RemoteSync!.Reason.ShouldBe(reason);
        (await world.NoProgressIncidentsAsync()).ShouldBe(0);
        (await world.EventsAsync()).ShouldNotContain(e => e.Type == AgentTaskEventType.Merged);
        (await world.EventsAsync()).ShouldContain(e => e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains(reason, StringComparison.Ordinal));
        world.Services.GetRequiredService<RecordingSessionStopper>().Killed.ShouldBeEmpty();
        Directory.Exists(world.Git.Worktree).ShouldBeTrue();
        world.Task.WorktreePath.ShouldBe(world.Git.Task.WorktreePath);
        // The caller dispatches from the header: decide, not the report's own next: review.
        var note = await world.NoteAsync();
        note!.Body.ShouldContain("next=decide");
        note.Body.ShouldNotContain("next=review");
    }

    /// <summary>Failure context only: the settled reason and the persisted progress/sync evidence.</summary>
    private static string Why(RunnerSettlementWorld world) =>
        world.Task.FailureReason + " | evidence=" + world.Task.CompletionProgressEvidenceJson;

    /// <summary>Outside the Shouldly expression tree: Split's optional argument cannot appear in one.</summary>
    private static bool IsMergeCommand(string command) => command.Split(' ').Contains("merge");

    private static void AssertNoSync(RunnerSettlementWorld world, string s)
    {
        world.Git.Git.Commands.ShouldNotContain(x => RunnerCompletionProgressTests.IsSyncCommand(x)
            || x.Contains(s, StringComparison.Ordinal));
    }
}
