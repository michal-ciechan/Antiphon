using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public partial class DispatcherSiblingInspectionSchedulingTests
{
    [Test]
    [Arguments("task ID")]
    [Arguments("attempt")]
    [Arguments("status")]
    [Arguments("card")]
    [Arguments("repository")]
    [Arguments("runner")]
    [Arguments("mode")]
    [Arguments("requested ref")]
    [Arguments("base-task ID")]
    [Arguments("merge target")]
    [Arguments("effective fallback")]
    [Arguments("new request")]
    [Arguments("request state")]
    [Arguments("reviewed source")]
    [Arguments("adopted checkout")]
    public async Task C1349_StaleInspectionCannotDispatch(string field)
    {
        var fired = false;
        Guid? replacement = null;
        string? adopted = null;
        await using var f = await SiblingInspectionFixture.CreateAsync(async fixture =>
        {
            fired = true;
            await using var edit = fixture.Read();
            var row = await edit.AgentTasks.SingleAsync(t => t.Id == fixture.Task.Id);
            switch (field)
            {
                case "task ID":
                    row.Status = AgentTaskStatus.Canceled;
                    var next = fixture.NewTask(); replacement = next.Id; edit.AgentTasks.Add(next); break;
                case "attempt": row.Attempt++; break;
                case "status": row.Status = AgentTaskStatus.Canceled; break;
                case "card": row.CardId = (await DispatchSiblingInspectionTests.CardAsync(edit)).Id; break;
                case "repository":
                    row.RepoPath = await LinkedCheckoutAsync(fixture); break;
                case "runner": row.RunnerId = "fixture-reassigned-runner"; break;
                case "mode": row.RequestedWorktreeBaseMode = RequestedWorktreeBaseMode.Auto; break;
                case "requested ref": row.WorktreeBaseRequestedRef = "other-target"; break;
                case "base-task ID": row.RequestedWorktreeBaseTaskId = fixture.Source.Id; break;
                case "merge target": row.MergeTargetRef = "other-target"; break;
                case "effective fallback":
                    var project = await edit.Projects.SingleAsync(); project.BaseBranch = "other-target"; break;
                case "new request":
                    var source = await edit.AgentTasks.SingleAsync(t => t.Id == fixture.Source.Id);
                    var added = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = source.Id,
                        State = LandRequestState.Queued, IsPending = true, RequestedAt = DateTime.UtcNow,
                        ExpectedSourceSha = (await fixture.Repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim() };
                    source.CurrentLandRequestId = added.Id; edit.AgentTaskLandRequests.Add(added); break;
                case "request state":
                    var pending = await edit.AgentTaskLandRequests.SingleAsync();
                    pending.State = LandRequestState.Queued; break;
                case "reviewed source":
                    var reviewed = await edit.AgentTaskLandRequests.SingleAsync();
                    reviewed.ExpectedSourceSha = (await fixture.Repo.GitReadAsync("rev-parse", "master")).Trim(); break;
                case "adopted checkout":
                    // Exercise record delimiters where newlines are legal in filenames.
                    var adoptedName = Path.GetInvalidFileNameChars().Contains('\n') ? "adopted" : "adopted\ncheckout";
                    adopted = Path.Combine(fixture.Repo.WorktreeRoot, adoptedName);
                    var branch = "feat/card-task-" + row.Id.ToString("N")[..8];
                    await fixture.Repo.GitAsync("worktree", "add", "-b", branch, adopted, "master");
                    row.WorktreePath = adopted; row.WorktreeBranch = branch; break;
            }
            await edit.SaveChangesAsync();
        });
        await f.Repo.GitAsync("branch", "other-target", "master");
        await using (var seed = f.Read())
        {
            if (field == "effective fallback")
            {
                var project = await seed.Projects.SingleAsync();
                project.BaseBranch = "master";
                (await seed.AgentTasks.SingleAsync(t => t.Id == f.Task.Id)).ProjectId = project.Id;
            }
            if (field is "request state" or "reviewed source")
            {
                var source = await seed.AgentTasks.SingleAsync(t => t.Id == f.Source.Id);
                var request = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = source.Id,
                    State = LandRequestState.NeedsResolution, IsPending = true, RequestedAt = DateTime.UtcNow,
                    ExpectedSourceSha = (await f.Repo.GitReadAsync("rev-parse", source.WorktreeBranch!)).Trim() };
                source.CurrentLandRequestId = request.Id; seed.AgentTaskLandRequests.Add(request);
            }
            await seed.SaveChangesAsync();
        }
        await f.ObserveAsync();
        var probes = f.Git.Calls.Count;
        await f.TickAsync();
        fired.ShouldBeTrue("the mutation must occur between completed observation and the locked claim");
        await using (var read = f.Read())
        {
            var unchanged = await read.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
            unchanged.AgentSessionId.ShouldBeNull(field);
            unchanged.WorktreePath.ShouldBe(adopted, field);
            (await read.AgentSessions.CountAsync()).ShouldBe(0);
            (await read.AgentTaskDispatchWarningIntents.CountAsync()).ShouldBe(0);
            if (field is "new request" or "request state")
                (await read.AgentTaskLandRequests.SingleAsync()).State.ShouldBe(LandRequestState.Queued);
            if (field == "reviewed source")
                (await read.AgentTaskLandRequests.SingleAsync()).ExpectedSourceSha
                    .ShouldBe((await f.Repo.GitReadAsync("rev-parse", "master")).Trim());
        }
        // CARD-1428 D-3/D-4: the rejected claim released the observation it consumed. A claim that
        // found it stale re-enqueued the same intent at once; a claim that lost the row enqueued nothing.
        f.Queue.Outstanding.ShouldBe(field is "status" or "task ID" ? 0 : 1,
            "consumed authority cannot survive its rejected claim");
        if (field is "status" or "task ID")
        {
            await f.ObserveAsync();
            if (replacement is Guid id) f.Queue.Attempted(id).ShouldBeTrue();
        }
        else if (field == "adopted checkout")
        {
            await f.TickAsync();
            await using var read = f.Read();
            var adoptedTask = await read.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
            adoptedTask.WorktreePath.ShouldBe(adopted);
            (await f.Repo.GitReadAsync("worktree", "list", "--porcelain")).Split('\n')
                .Count(line => line == "worktree " + adopted).ShouldBe(1);
        }
        else
        {
            // Reassignment can be ineligible for dispatch; the current intent still has to be
            // independently inspectable and cannot retrieve the old worker's observation.
            // CARD-1428 D-3: the stale claim re-enqueued its own, stale intent; that owned worker
            // skips (changed intent) or re-observes (changed inventory) before the next look.
            await f.Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (field == "runner")
            {
                await using var reassignmentRead = f.Read();
                var reassigned = await reassignmentRead.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
                f.Queue.TryEnqueue(CardSiblingInspectionIntent.From(reassigned, "master")).ShouldBeTrue();
                await f.Queue.WhenIdleAsync();
            }
            // Same intent, changed inventory: the D-3 re-inspection is the replacement observation
            // (a sibling-land hold, which a tick consumes as before).
            else if (field is not ("new request" or "request state" or "reviewed source")) await f.ObserveAsync();
            f.Queue.Outstanding.ShouldBe(1);
            if (field is not ("card" or "repository")) f.Git.Calls.Count.ShouldBeGreaterThan(probes);
            await using var read = f.Read();
            var current = await read.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
            current.AgentSessionId.ShouldBeNull();
            var fallback = field == "effective fallback" ? "other-target" : "master";
            f.Queue.Peek(CardSiblingInspectionIntent.From(current, fallback)).ShouldNotBeNull()
                .Metadata.ShouldNotBeNull("replacement observation came from its own database scope");
        }
    }

    [Test]
    [Arguments("target moves")]
    [Arguments("requested ref moves")]
    [Arguments("pending ref moves")]
    [Arguments("ref missing")]
    [Arguments("probe error")]
    [Arguments("fence deadline")]
    [Arguments("unchanged")]
    public async Task C1349_LeasedReferenceFence(string scenario)
    {
        var fired = false;
        var fenceCommands = new List<string[]>();
        await using var f = await SiblingInspectionFixture.CreateAsync(async fixture =>
        {
            fired = true;
            var move = scenario == "requested ref moves" ? "start-here"
                : scenario == "pending ref moves" ? fixture.Source.WorktreeBranch! : "master";
            if (scenario.EndsWith("moves", StringComparison.Ordinal))
                await fixture.Repo.GitAsync("update-ref", "refs/heads/" + move,
                    scenario == "pending ref moves" ? "uncontained" : fixture.Source.WorktreeBranch!);
            if (scenario == "ref missing") await fixture.Repo.GitAsync("branch", "-D", "start-here");
            fixture.Git.Before = (args, token) =>
            {
                // The claim's own fence commands only: CARD-1428 D-3 re-enqueues the stale intent
                // from the tick, and that owned worker's inspection runs with Active > 0.
                if (fixture.Queue.Active > 0) return Task.FromResult<LandingGitResult?>(null);
                fenceCommands.Add(args.ToArray());
                if (args[0] == "rev-parse" && args.Any(a => a.EndsWith("^{commit}", StringComparison.Ordinal)))
                {
                    if (scenario == "fence deadline") fixture.Clock.Advance(TimeSpan.FromSeconds(5));
                    if (scenario == "probe error")
                        return Task.FromResult<LandingGitResult?>(new(128, "", "owned fence failure"));
                }
                return Task.FromResult<LandingGitResult?>(null);
            };
        });
        await f.Repo.GitAsync("branch", "start-here", "master");
        if (scenario == "pending ref moves")
        {
            await f.Repo.GitAsync("branch", "uncontained", f.Source.WorktreeBranch!);
            await f.Repo.GitAsync("update-ref", "refs/heads/" + f.Source.WorktreeBranch!, "master");
            await using var seed = f.Read();
            var source = await seed.AgentTasks.SingleAsync(t => t.Id == f.Source.Id);
            var pending = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = source.Id,
                State = LandRequestState.Running, IsPending = true, RequestedAt = DateTime.UtcNow };
            source.CurrentLandRequestId = pending.Id; seed.AgentTaskLandRequests.Add(pending);
            await seed.SaveChangesAsync();
        }
        if (scenario is "requested ref moves" or "ref missing")
        {
            await using var seed = f.Read();
            await seed.AgentTasks.Where(t => t.Id == f.Task.Id).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.WorktreeBaseRequestedRef, "start-here"));
        }
        await f.ObserveAsync();
        await f.TickAsync();
        fired.ShouldBeTrue();
        await using var read = f.Read();
        var row = await read.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
        if (scenario == "unchanged")
        {
            row.Status.ShouldBe(AgentTaskStatus.Dispatched, row.FailureReason);
            row.AgentSessionId.ShouldNotBeNull(); row.WorktreePath.ShouldNotBeNull();
            (await ScratchGitRepo.GitInAsync(row.WorktreePath!, "rev-parse", "HEAD")).StdOut.Trim()
                .ShouldBe((await f.Repo.GitReadAsync("rev-parse", "master")).Trim());
        }
        else
        {
            row.Status.ShouldBe(AgentTaskStatus.Queued, row.FailureReason);
            row.AgentSessionId.ShouldBeNull(); row.WorktreePath.ShouldBeNull();
            fenceCommands.ShouldNotContain(a => a[0] == "cherry" || a[0] == "for-each-ref" || a[0] == "merge-base");
            (await read.AgentTaskDispatchWarningIntents.CountAsync()).ShouldBe(0);
            f.Queue.Outstanding.ShouldBe(1, "CARD-1428 D-3: the stale claim re-enqueued the same intent");
        }
    }

    [Test]
    [Arguments("dedup")]
    [Arguments("active limit")]
    [Arguments("total capacity")]
    [Arguments("consume")]
    [Arguments("expiry")]
    [Arguments("fairness")]
    [Arguments("restart before enqueue")]
    [Arguments("restart after result")]
    [Arguments("error")]
    [Arguments("shutdown")]
    public async Task C1349_InspectionQueueRecovers(string scenario)
    {
        await using var f = await SiblingInspectionFixture.CreateAsync();
        var ids = new List<Guid> { f.Task.Id };
        var workingId = Guid.NewGuid();
        var workingSession = Guid.NewGuid();
        await using (var seed = f.Read())
        {
            seed.AgentSessions.Add(new AgentSession { Id = workingSession, DefinitionName = "working-fixture",
                Cwd = f.Repo.Path, Status = SessionStatus.Running, StartedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
            seed.AgentTasks.Add(new AgentTask { Id = workingId, RootTaskId = workingId,
                Title = "unrelated working seat", Goal = "keep running", Status = AgentTaskStatus.Working,
                AgentSessionId = workingSession, WorkingDirectory = f.Repo.Path,
                CreatedAt = DateTime.UtcNow, DispatchedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        if (scenario is "active limit" or "total capacity" or "fairness" or "shutdown")
        {
            await using var seed = f.Read();
            for (var i = 1; i < 33; i++)
            {
                var row = f.NewTask(); row.CreatedAt = row.CreatedAt.AddSeconds(i);
                seed.AgentTasks.Add(row); ids.Add(row.Id);
            }
            // Every result remains a land hold, so consumption cannot turn this into a launch census.
            var source = await seed.AgentTasks.SingleAsync(t => t.Id == f.Source.Id);
            var pending = new AgentTaskLandRequest { Id = Guid.NewGuid(), TaskId = source.Id,
                State = LandRequestState.Queued, IsPending = true, RequestedAt = DateTime.UtcNow };
            source.CurrentLandRequestId = pending.Id; seed.AgentTaskLandRequests.Add(pending);
            await seed.SaveChangesAsync();
        }
        if (scenario == "restart before enqueue")
        {
            await using var seed = f.Read();
            seed.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = f.Task.Id,
                Type = AgentTaskEventType.Held, Detail = "Held: same-card base inspection is pending.", At = DateTime.UtcNow });
            await seed.SaveChangesAsync();
            await f.RestartAsync();
        }
        f.Git.Block = scenario is "dedup" or "active limit" or "total capacity" or "shutdown";
        f.Git.Fail = scenario == "error";
        await f.TickAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if (f.Git.Block)
        {
            await f.Git.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (ids.Count > 1) await f.Git.TwoEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (scenario == "dedup")
            {
                await f.TickAsync().WaitAsync(TimeSpan.FromSeconds(10));
                f.Queue.Outstanding.ShouldBe(1); f.Queue.Active.ShouldBe(1); f.Git.Blocked.ShouldBe(1);
                f.Git.Calls.Count(c => c[0] == "for-each-ref").ShouldBe(1);
            }
            else
            {
                f.Queue.Outstanding.ShouldBe(32); f.Queue.Active.ShouldBe(2); f.Git.Blocked.ShouldBe(2);
                f.Queue.Attempted(ids.Last()).ShouldBeFalse();
                // This tick must return while both workers and every capacity slot remain held.
                await f.TickAsync().WaitAsync(TimeSpan.FromSeconds(10));
                f.Git.Release.Task.IsCompleted.ShouldBeFalse();
                f.Queue.Outstanding.ShouldBe(32); f.Queue.Active.ShouldBe(2);
            }
            if (scenario == "shutdown")
            {
                var stop = f.Queue.StopAsync(default);
                await stop.WaitAsync(TimeSpan.FromSeconds(10));
                f.Git.Canceled.ShouldBe(2);
                f.Git.Release.Task.IsCompleted.ShouldBeFalse("shutdown cancellation, not emergency fixture release, joins workers");
                f.Queue.Active.ShouldBe(0); f.Queue.Outstanding.ShouldBe(0);
            }
            f.Git.Block = false; f.Git.Release.TrySetResult();
        }
        if (scenario is "active limit" or "total capacity" or "fairness")
            await WaitForSaturatedQueueAsync(f, scenario);
        else
            await f.Queue.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (scenario == "shutdown")
        {
            await using var read = f.Read();
            var working = await read.AgentTasks.SingleAsync(t => t.Id == workingId);
            working.Status.ShouldBe(AgentTaskStatus.Working); working.AgentSessionId.ShouldBe(workingSession);
            (await read.AgentSessions.SingleAsync(s => s.Id == workingSession)).Status.ShouldBe(SessionStatus.Running);
            return;
        }
        if (scenario == "expiry")
        {
            // CARD-1428 D-5: a result outlives the old 30 s limit and expires at the retention cap.
            var intent = CardSiblingInspectionIntent.From(f.Task, "master");
            var before = f.Git.Calls.Count;
            f.Clock.Advance(TimeSpan.FromSeconds(30));
            f.Queue.Peek(intent).ShouldNotBeNull("a 30 s old result is still valid");
            f.Git.Calls.Count.ShouldBe(before, "no re-inspection inside the cap");
            f.Clock.Advance(SiblingInspectionResults.RetainedResultMaxAge);
            f.Queue.Peek(intent).ShouldBeNull("evicted at the cap");
            await f.ObserveAsync();
            f.Git.Calls.Count.ShouldBeGreaterThan(before);
            await using var check = f.Read();
            (await check.AgentTasks.SingleAsync(t => t.Id == f.Task.Id)).AgentSessionId.ShouldBeNull();
        }
        if (scenario == "restart after result")
        {
            await f.RestartAsync(); f.Queue.Outstanding.ShouldBe(0);
            await f.ObserveAsync();
        }
        if (scenario == "error")
        {
            var intent = CardSiblingInspectionIntent.From(f.Task, "master");
            var error = f.Queue.Peek(intent).ShouldNotBeNull();
            error.Reason.ShouldBe("git_inspection_error");
            error.Warnings.ShouldNotBeEmpty();
            // The hold an unknown-integration result causes consumes it (CARD-1428 retains a go result).
            f.Queue.Invalidate(f.Task.Id);
            f.Queue.Outstanding.ShouldBe(0);
            f.Git.Fail = false;
            await f.ObserveAsync();
        }
        if (scenario == "fairness")
        {
            await f.TickAsync(); // consumes the original 32 holds; the full queue cannot admit #33 yet
            f.Queue.Outstanding.ShouldBe(0);
            // CARD-1428 D-6: the capacity is in-flight work; hold the workers so the tick sees it full.
            f.Git.Block = true;
            await f.TickAsync();
            f.Queue.Attempted(ids.Last()).ShouldBeTrue("the unattempted task must precede held requeues");
            f.Queue.InFlight.ShouldBe(32);
            f.Git.Block = false; f.Git.Release.TrySetResult();
            await WaitForSaturatedQueueAsync(f, "fairness requeue");
            f.Queue.Outstanding.ShouldBe(32);
        }
        else if (ids.Count == 1)
        {
            await f.TickAsync();
            f.Queue.Outstanding.ShouldBe(0);
            await using var check = f.Read();
            var task = await check.AgentTasks.SingleAsync(t => t.Id == f.Task.Id);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, task.FailureReason);
            task.AgentSessionId.ShouldNotBeNull(); task.WorktreePath.ShouldNotBeNull();
        }
        else
        {
            await using var check = f.Read();
            (await check.AgentTasks.Where(t => ids.Contains(t.Id)).ToListAsync())
                .ShouldAllBe(t => t.Status == AgentTaskStatus.Queued && t.AgentSessionId == null && t.WorktreePath == null);
        }
    }

    private static async Task WaitForSaturatedQueueAsync(SiblingInspectionFixture f, string scenario)
    {
        // CARD-1552: the scheduling assertions above run behind barriers. Draining 32 real-Git
        // inspections is a throughput measurement: retain the old 15 s limit for no progress,
        // with a 48 s absolute cap (about twice the recorded Windows maximum of 23.4 s,
        // including teardown). Progress can renew the quiet window, never the absolute cap.
        var elapsed = Stopwatch.StartNew();
        var lastProgress = TimeSpan.Zero;
        var remaining = f.Queue.InFlight;
        var commands = f.Git.Calls.Count;
        var drain = f.Queue.WhenIdleAsync();
        Console.WriteLine($"Inspection drain {scenario}: inFlight={remaining}, active={f.Queue.Active}, gitCalls={commands}");
        try
        {
            while (!drain.IsCompleted)
            {
                await Task.WhenAny(drain, Task.Delay(50));
                var currentRemaining = f.Queue.InFlight;
                var currentCommands = f.Git.Calls.Count;
                if (currentRemaining < remaining || currentCommands > commands)
                    lastProgress = elapsed.Elapsed;
                remaining = currentRemaining;
                commands = currentCommands;
                var state = $"{scenario}: inFlight={remaining}, active={f.Queue.Active}, gitCalls={commands}, elapsed={elapsed.Elapsed}";
                elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(48), state);
                (elapsed.Elapsed - lastProgress).ShouldBeLessThan(TimeSpan.FromSeconds(15), state);
            }
            await drain;
            f.Queue.InFlight.ShouldBe(0);
            f.Queue.Active.ShouldBe(0);
        }
        finally
        {
            Console.WriteLine($"Inspection drain {scenario}: elapsed={elapsed.Elapsed.TotalSeconds:F3}s, inFlight={f.Queue.InFlight}, active={f.Queue.Active}, gitCalls={f.Git.Calls.Count}");
        }
    }

    private static async Task<string> LinkedCheckoutAsync(SiblingInspectionFixture f)
    {
        var path = Path.Combine(f.Repo.WorktreeRoot, "relocated");
        await f.Repo.GitAsync("worktree", "add", "--detach", path, "master");
        return path;
    }
}
