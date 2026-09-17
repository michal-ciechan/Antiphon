using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0547 S2: settlement consults the shared obligation rule; CommitFailed resolves only after a
// verified empty history search.
public partial class AgentTaskReplyIntegrationTests
{
    private static async Task<List<AgentTaskEvent>> RecoveryEventsAsync(Guid taskId)
    {
        await using var db = CreateContext();
        return await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == taskId
                && (e.Type == AgentTaskEventType.Committed || e.Type == AgentTaskEventType.CommitRecoveryStarted
                    || e.Type == AgentTaskEventType.CommitRecoveryNotNeeded || e.Type == AgentTaskEventType.CommitRecoveryAbandoned))
            .OrderBy(e => e.At).ToListAsync();
    }

    private static async Task<IReadOnlyList<CommitRecoveryObligations.Pending>> UnresolvedAsync(Guid taskId)
    {
        await using var db = CreateContext();
        return await CommitRecoveryObligations.LoadUnresolvedAsync(db, taskId, CancellationToken.None);
    }

    [Test]
    [Arguments("fixed")]
    [Arguments("still-failing")]
    public async Task C547_CommitFailed_after_a_verified_empty_search_resolves_and_a_later_settle_proceeds(string second)
    {
        var seeded = await SeedC527Async(prefix: "c547");
        using var repo = seeded.Repo;
        await repo.InstallFailingPreCommitHookAsync("gate says no");
        var a = Path.Combine(repo.Path, "a.md");
        await File.WriteAllTextAsync(a, "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", a, DateTime.UtcNow);
        const string report = "Wrote `a.md`.";
        var calls = new List<string>();
        var spy = new RecordingGitWorkspaceService();
        spy.BeforeRun = args => { lock (calls) calls.Add(string.Join(' ', args)); return Task.CompletedTask; };
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy, saveInterceptor: new ThrowOnceSaveInterceptor());
        AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);

        await using (var db = CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
            (await db.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
        }
        var events = await RecoveryEventsAsync(seeded.Task.Id);
        var started = events.Where(e => e.Type == AgentTaskEventType.CommitRecoveryStarted).ShouldHaveSingleItem();
        started.Detail.Length.ShouldBe(64);
        var notNeeded = events.Where(e => e.Type == AgentTaskEventType.CommitRecoveryNotNeeded).ShouldHaveSingleItem();
        notNeeded.Detail.ShouldBe($"{started.Id:D} CommitFailed");
        events.ShouldNotContain(e => e.Type == AgentTaskEventType.Committed);
        var commitCall = calls.FindIndex(c => c.StartsWith("commit", StringComparison.Ordinal));
        commitCall.ShouldBeGreaterThanOrEqualTo(0);
        calls.FindLastIndex(c => c.StartsWith("log --all --reflog", StringComparison.Ordinal)).ShouldBeGreaterThan(commitCall);
        (await UnresolvedAsync(seeded.Task.Id)).ShouldBeEmpty();

        if (second == "fixed")
            File.Delete(Path.Combine(repo.Path, ".git", "hooks", "pre-commit"));
        var retry = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        AttachTerminal(retry, seeded.Parent);
        await CreateService(retry).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(retry).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        await using (var db = CreateContext())
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        events = await RecoveryEventsAsync(seeded.Task.Id);
        events.Count(e => e.Type == AgentTaskEventType.CommitRecoveryStarted).ShouldBe(2);
        if (second == "fixed")
        {
            var committed = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
            await AssertC527RecoveredReceiptAsync(seeded.Task, seeded.Parent, report, committed, 1);
            events.Count(e => e.Type == AgentTaskEventType.CommitRecoveryNotNeeded).ShouldBe(1);
            events.Count(e => e.Type == AgentTaskEventType.Committed).ShouldBe(1);
        }
        else
        {
            await using (var db = CreateContext())
                (await db.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id)).Goal.ShouldContain("gate says no");
            var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
            receipt.Prompt.Text.ShouldContain("commit task ");
            var starts = events.Where(e => e.Type == AgentTaskEventType.CommitRecoveryStarted).Select(e => $"{e.Id:D} CommitFailed").ToArray();
            var resolutions = events.Where(e => e.Type == AgentTaskEventType.CommitRecoveryNotNeeded).Select(e => e.Detail).ToArray();
            resolutions.Length.ShouldBe(2);
            resolutions.ShouldBe(starts, ignoreOrder: true);
            events.ShouldNotContain(e => e.Type == AgentTaskEventType.Committed);
        }
        CommitRecoveryObligations.Unresolved(events, seeded.Task.Id).ShouldBeEmpty();
        spy.Verbs.ShouldNotContain("push");
    }

    [Test]
    public async Task C547_CommitFailed_with_a_failed_history_search_leaves_the_obligation_pending()
    {
        var seeded = await SeedC527Async(prefix: "c547");
        using var repo = seeded.Repo;
        await repo.InstallFailingPreCommitHookAsync("gate says no");
        var a = Path.Combine(repo.Path, "a.md");
        await File.WriteAllTextAsync(a, "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", a, DateTime.UtcNow);
        var spy = new RecordingGitWorkspaceService();
        spy.OverrideRun = args => spy.Verbs.Contains("commit") && args.Contains("--all")
            ? (128, "", "history unavailable") : null;
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy, saveInterceptor: new ThrowOnceSaveInterceptor());
        AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `a.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);

        (await RecoveryEventsAsync(seeded.Task.Id)).ShouldNotContain(e => e.Type == AgentTaskEventType.CommitRecoveryNotNeeded);
        (await UnresolvedAsync(seeded.Task.Id)).ShouldHaveSingleItem();

        // The search works again, but nothing proved the attempt left no commit: settlement holds.
        spy.OverrideRun = null;
        var retry = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var terminal = AttachTerminal(retry, seeded.Parent);
        await CreateService(retry).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(retry).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        await AssertC527RecoveryPendingAsync(seeded.Task.Id);
        terminal.SubmittedBodies.ShouldBeEmpty();
        (await UnresolvedAsync(seeded.Task.Id)).ShouldHaveSingleItem();
        spy.Verbs.Count(v => v == "commit").ShouldBe(1);
    }

    [Test]
    public async Task C547_an_unresolved_obligation_from_another_digest_does_not_hold_this_settlement()
    {
        var seeded = await SeedC527Async(prefix: "c547");
        using var repo = seeded.Repo;
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var foreignId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = foreignId,
                AgentTaskId = seeded.Task.Id,
                Type = AgentTaskEventType.CommitRecoveryStarted,
                Detail = new string('a', 64),
                At = DateTime.UtcNow.AddMinutes(-5),
            });
            await db.SaveChangesAsync();
        }
        var a = Path.Combine(repo.Path, "a.md");
        await File.WriteAllTextAsync(a, "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", a, DateTime.UtcNow);
        const string report = "Wrote `a.md`.";
        var factory = C527Factory(repo.WorktreeRoot);
        AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        await using (var db = CreateContext())
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldNotBe(baseline);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain("git=committed:");
        // D-1's Committed arm is task-scoped (its Detail carries no digest), so this settlement's
        // later Committed row also closes the foreign obligation. The plan's V-547-18 expected it
        // to survive; that contradicts D-1 and is recorded for Review (D-7 territory).
        (await RecoveryEventsAsync(seeded.Task.Id)).ShouldContain(e => e.Id == foreignId);
        (await UnresolvedAsync(seeded.Task.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task C547_an_abandoned_obligation_is_invisible_to_a_later_same_digest_settlement()
    {
        var seeded = await SeedC527Async(prefix: "c547");
        using var repo = seeded.Repo;
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var a = Path.Combine(repo.Path, "a.md");
        await File.WriteAllTextAsync(a, "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", a, DateTime.UtcNow);
        const string report = "Wrote `a.md`.";
        var spy = new RecordingGitWorkspaceService();
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy, saveInterceptor: new ThrowOnceSaveInterceptor());
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), report);
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await AssertC527RecoveryPendingAsync(seeded.Task.Id);
        var first = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        first.ShouldNotBe(baseline);

        await repo.GitAsync("reset", "--hard", baseline);
        await repo.GitAsync("reflog", "expire", "--expire=now", "--all");
        await using (var db = CreateContext())
        {
            var pending = (await CommitRecoveryObligations.LoadUnresolvedAsync(db, seeded.Task.Id, CancellationToken.None))
                .ShouldHaveSingleItem();
            db.AgentTaskEvents.Add(CommitRecoveryObligations.Abandon(pending, "test", "discarded", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        await File.WriteAllTextAsync(a, "a again");

        var retry = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        AttachTerminal(retry, seeded.Parent);
        await CreateService(retry).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(retry).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        await using (var db = CreateContext())
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        var second = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        second.ShouldNotBe(baseline);
        second.ShouldNotBe(first);
        var receipt = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, report);
        receipt.Prompt.Text.ShouldContain($"git=committed:{second[..7]}");
        var events = await RecoveryEventsAsync(seeded.Task.Id);
        events.Count(e => e.Type == AgentTaskEventType.CommitRecoveryStarted).ShouldBe(2);
        events.Count(e => e.Type == AgentTaskEventType.CommitRecoveryAbandoned).ShouldBe(1);
        events.Count(e => e.Type == AgentTaskEventType.Committed).ShouldBe(1);
        CommitRecoveryObligations.Unresolved(events, seeded.Task.Id).ShouldBeEmpty();
        spy.Verbs.Count(v => v == "commit").ShouldBe(2);
        spy.Verbs.ShouldNotContain("push");
    }
}
