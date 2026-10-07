using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BlockedTaskParkResumeTests
{
    private const string Answer = "continue from the parked tip";

    [Test]
    public async Task C1065_ReplyStartsOneAttemptFromPublishedSource()
    {
        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            var tip = await PublishAsync(f);
            var beforeHash = (await f.TaskAsync()).TokenHash;
            await StampAsync(f);
            await f.AnswerAsync(Answer);
            f.Launches.Calls.ShouldBeEmpty("G-153");
            var queued = await f.TaskAsync();
            queued.Id.ShouldBe(f.TaskId, "G-129");
            queued.Attempt.ShouldBe(2, "G-131");
            queued.AgentId.ShouldBe(f.AgentId, "G-130");
            queued.Status.ShouldBe(AgentTaskStatus.Queued, "G-129");
            queued.AgentSessionId.ShouldBeNull("G-132");
            queued.TokenHash.ShouldNotBeNull().ShouldNotBe(beforeHash, "G-133");
            queued.Result.ShouldBe("completed report", "G-129");
            queued.ResultFilePath.ShouldBe("report.md", "G-129");
            queued.ReleasedSeatAnswer.ShouldBe(Answer, "G-138");
            queued.RepliedAtSequence.ShouldBeNull("G-134");
            queued.ReportNudgedAt.ShouldBeNull("G-134");
            queued.ReportNudgeMessageId.ShouldBeNull("G-134");
            queued.NextCheckAt.ShouldBeNull("G-134");
            queued.CheckCount.ShouldBe(0, "G-134");
            var resumed = await f.ParkAsync();
            resumed.State.ShouldBe(AgentTaskParkState.Resumed, "G-131");
            resumed.ResumeAttempt.ShouldBe(2, "G-131");
            resumed.SourceSha.ShouldBe(tip, "G-135");
            await f.DispatchAsync();
            f.Launches.Calls.Count.ShouldBe(1, "G-132");
            var launched = await f.TaskAsync();
            launched.AgentId.ShouldBe(f.AgentId, "G-130");
            var sessionId = launched.AgentSessionId.ShouldNotBeNull("G-132");
            sessionId.ShouldNotBe(f.SessionId, "G-132");
            (await f.ParkAsync()).ResumeSessionId.ShouldBe(sessionId, "G-132");
            await using var db = f.Db();
            (await db.AgentTasks.CountAsync()).ShouldBe(1, "G-129");
            (await db.Agents.AnyAsync(a => a.Id == f.AgentId)).ShouldBeTrue("G-130");
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.SessionId)).ShouldBe(0, "G-132");
            var brief = await BriefAsync(f, sessionId);
            brief.ShouldContain($"Parked source SHA: {tip}; full ref: {resumed.FullRef}.", Case.Sensitive, "G-135");
            brief.ShouldNotContain($"Parked source SHA: {queued.WorktreeBaseSha}", Case.Sensitive, "G-135");
            brief.ShouldContain(Answer, Case.Sensitive, "G-138");
            await f.TryAnswerAsync(Answer);
            (await f.TaskAsync()).Attempt.ShouldBe(2, "G-131");
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            var tip = await PublishAsync(f);
            var fullRef = (await f.ParkAsync()).FullRef.ShouldNotBeNull();
            await StampAsync(f);
            await f.AnswerAsync(Answer);
            Directory.Delete(f.SourcePath, recursive: true);
            await f.DispatchAsync();
            Directory.Exists(f.SourcePath).ShouldBeTrue("G-135");
            (await f.GitAsync(f.SourcePath, "rev-parse", "HEAD")).ShouldBe(tip, "G-135");
            (await f.GitAsync(f.SourcePath, "symbolic-ref", "-q", "HEAD")).ShouldBe(fullRef, "G-135");
            var master = await f.GitAsync(f.SourcePath, "rev-parse", "master");
            tip.ShouldNotBe(master, "G-135");
            (await f.TaskAsync()).AgentId.ShouldBe(f.AgentId, "G-130");
            f.Launches.Calls.Count.ShouldBe(1, "G-135");
            var brief = await BriefAsync(f, (await f.TaskAsync()).AgentSessionId!.Value);
            brief.ShouldContain($"Parked source SHA: {tip}; full ref: {fullRef}.", Case.Sensitive, "G-135");
        }

        foreach (var boundary in new[] { "accept-before", "attempt-before" })
        {
            var cut = new ResumeSaveCut { Boundary = boundary };
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true,
                configureDb: options => options.AddInterceptors(cut));
            await PublishAsync(f);
            await StampAsync(f);
            cut.Armed = true;
            (await f.TryAnswerAsync(Answer)).ShouldNotBeNull(boundary);
            cut.Hit.ShouldBeTrue(boundary);
            var interrupted = await f.TaskAsync();
            interrupted.Attempt.ShouldBe(1, "G-131 " + boundary);
            interrupted.AgentId.ShouldBe(f.AgentId, "G-130 " + boundary);
            if (boundary == "accept-before") interrupted.ReleasedSeatAnswerId.ShouldBeNull(boundary);
            else interrupted.ReleasedSeatAnswer.ShouldBe(Answer, "G-138 " + boundary);
            cut.Armed = false;
            await f.AnswerAsync(Answer);
            var recovered = await f.TaskAsync();
            recovered.Attempt.ShouldBe(2, "G-131 " + boundary);
            recovered.AgentId.ShouldBe(f.AgentId, "G-130 " + boundary);
            recovered.ReleasedSeatAnswer.ShouldBe(Answer, boundary);
            await f.TryAnswerAsync(Answer);
            (await f.TaskAsync()).Attempt.ShouldBe(2, "G-131 " + boundary);
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await PublishAsync(f);
            await StampAsync(f);
            await f.AnswerAsync(Answer);
            f.Launches.OnLaunch = _ => throw new InvalidOperationException("injected launch");
            await f.DispatchAsync();
            f.Launches.Calls.Count.ShouldBe(1, "G-132");
            var launched = await f.TaskAsync();
            launched.Attempt.ShouldBe(2, "G-131");
            launched.AgentId.ShouldBe(f.AgentId, "G-130");
            launched.Status.ShouldBe(AgentTaskStatus.Dispatched, "G-132");
            await f.DispatchAsync();
            f.Launches.Calls.Count.ShouldBe(1, "G-131");
            (await f.TaskAsync()).Attempt.ShouldBe(2, "G-131");
        }

        var queueCut = new ResumeSaveCut { Boundary = "queue-before" };
        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true,
            configureDb: options => options.AddInterceptors(queueCut)))
        {
            await PublishAsync(f);
            await StampAsync(f);
            await f.AnswerAsync(Answer);
            queueCut.Armed = true;
            await f.DispatchAsync();
            queueCut.Hit.ShouldBeTrue("G-138");
            f.Launches.Calls.Count.ShouldBe(1, "G-132");
            var queued = await f.TaskAsync();
            queued.Attempt.ShouldBe(2, "G-131");
            queued.AgentId.ShouldBe(f.AgentId, "G-130");
            queued.ReleasedSeatAnswer.ShouldBe(Answer, "G-138");
            await f.DispatchAsync();
            f.Launches.Calls.Count.ShouldBe(1, "G-131");
            (await f.TaskAsync()).Attempt.ShouldBe(2, "G-131");
        }
    }

    [Test]
    public async Task C1065_ReplyRacePersistsOneAnswerAndOneOwner()
    {
        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await PublishAsync(f, handle: false);
            var session = f.SessionId;
            // Mid-turn keeps WhenIdle queued. An idle Running seat delivers at once and
            // waits out transcript confirmation on the fixture clock.
            await f.IngestAsync(TranscriptKinds.UserPrompt, "still busy", f.Now);
            await f.AnswerAsync(Answer);
            var live = await f.TaskAsync();
            live.Attempt.ShouldBe(1, "G-136");
            live.AgentSessionId.ShouldBe(session, "G-136");
            live.Status.ShouldBe(AgentTaskStatus.Working, "G-136");
            live.AgentId.ShouldBe(f.AgentId, "G-136");
            var park = await f.ParkAsync();
            park.State.ShouldBe(AgentTaskParkState.Held, "G-136");
            park.ReasonCode.ShouldBe("park_reply_before_reserve", "G-136");
            await using var db = f.Db();
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == session)).ShouldBe(1, "G-136");
            (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == session)).Body.ShouldContain(Answer, Case.Sensitive, "G-136");
            await f.ReleaseAsync();
            f.Wire.ConditionalCommands.ShouldBe(0, "G-136");
            (await f.TaskAsync()).Attempt.ShouldBe(1, "G-136");
            (await f.TaskAsync()).AgentSessionId.ShouldBe(session, "G-136");
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await PublishAsync(f, handle: false);
            (await f.RunAsync()).ReleaseId.ShouldNotBeNull("G-137");
            await f.AnswerAsync(Answer);
            var held = await f.TaskAsync();
            held.Attempt.ShouldBe(1, "G-137");
            held.Status.ShouldBe(AgentTaskStatus.Blocked, "G-137");
            held.AgentId.ShouldBe(f.AgentId, "G-137");
            held.AgentSessionId.ShouldBe(f.SessionId, "G-137");
            held.ReleasedSeatAnswer.ShouldBe(Answer, "G-137");
            held.ReleasedSeatAnswerTargetAttempt.ShouldBe(2, "G-137");
            var park = await f.ParkAsync();
            park.State.ShouldBe(AgentTaskParkState.ResumePending, "G-137");
            park.ResumeInputEventId.ShouldBe(held.ReleasedSeatAnswerId, "G-137");
            await using var db = f.Db();
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.SessionId)).ShouldBe(0, "G-137");
            f.Launches.Calls.ShouldBeEmpty("G-153");
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await PublishAsync(f);
            f.Launches.Calls.ShouldBeEmpty("G-153");
            await f.AnswerAsync(Answer);
            var continued = await f.TaskAsync();
            continued.Attempt.ShouldBe(2, "G-138");
            continued.AgentId.ShouldBe(f.AgentId, "G-138");
            continued.ReleasedSeatAnswer.ShouldBe(Answer, "G-138");
            (await f.ParkAsync()).State.ShouldBe(AgentTaskParkState.Resumed, "G-138");
            await using var db = f.Db();
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.SessionId)).ShouldBe(0, "G-137");
            f.Launches.Calls.ShouldBeEmpty("G-153");
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await PublishAsync(f, handle: false);
            f.Wire.DropReply = true;
            await f.ReleaseAsync();
            await f.TryAnswerAsync(Answer);
            var held = await f.TaskAsync();
            held.ReleasedSeatAnswer.ShouldBe(Answer, "G-137");
            held.Status.ShouldBe(AgentTaskStatus.Blocked, "G-137");
            held.Attempt.ShouldBe(1, "G-137");
            held.AgentId.ShouldBe(f.AgentId, "G-137");
            (await f.ParkAsync()).State.ShouldBe(AgentTaskParkState.ResumePending, "G-137");
            await using var db = f.Db();
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == f.SessionId)).ShouldBe(0, "G-137");
            f.Launches.Calls.ShouldBeEmpty("G-153");
        }

        await using (var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true))
        {
            await PublishAsync(f);
            var raced = await Task.WhenAll(f.TryAnswerAsync(Answer), f.TryAnswerAsync(Answer));
            raced.Count(static error => error is not null).ShouldBeLessThanOrEqualTo(1, "G-138");
            var task = await f.TaskAsync();
            task.Attempt.ShouldBe(2, "G-138");
            task.AgentId.ShouldBe(f.AgentId, "G-130");
            task.ReleasedSeatAnswer.ShouldBe(Answer, "G-138");
            (await f.ParkAsync()).State.ShouldBe(AgentTaskParkState.Resumed, "G-138");
            await f.TryAnswerAsync(Answer);
            (await f.TaskAsync()).Attempt.ShouldBe(2, "G-131");
        }
    }

    [Test]
    public async Task C1065_ResumePreservesSourceAndAdmissionRefusals()
    {
        foreach (var shape in new[]
        {
            "dirty", "sequencer", "advanced", "divergent", "missing-object", "changed-ref",
            "changed-endpoint", "missing-wrong-tip", "stale", "quota", "commit", "null-receipt",
            "mismatched-release", "capacity", "host", "kind", "platform", "scope", "workspace"
        })
        {
            await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
            var mode = shape == "scope" ? WorkspaceMode.Shared : WorkspaceMode.Worktree;
            var tip = await PublishAsync(f, mode);
            var parked = await f.ParkAsync();
            if (shape is "null-receipt" or "mismatched-release")
            {
                await using var db = f.Db();
                if (shape == "null-receipt")
                    await db.AgentTaskParks.Where(p => p.Id == parked.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.PublicationReceiptId, (Guid?)null));
                else
                    await db.AgentTaskParks.Where(p => p.Id == parked.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.RunnerSeatReleaseId, Guid.NewGuid()));
                var refused = await f.TryAnswerAsync(Answer);
                refused.ShouldBeOfType<ConflictException>(shape).Code
                    .ShouldBe("park_resume_refused:park_receipt_missing", "G-139 " + shape);
                var kept = await f.TaskAsync();
                kept.Attempt.ShouldBe(1, "G-139 " + shape);
                kept.AgentId.ShouldBe(f.AgentId, "G-139 " + shape);
                kept.Status.ShouldBe(AgentTaskStatus.Blocked, shape);
                kept.ReleasedSeatAnswer.ShouldBe(Answer, "G-139 " + shape);
                await using var after = f.Db();
                (await after.Agents.AnyAsync(a => a.Id == f.AgentId)).ShouldBeTrue("G-139 " + shape);
                f.Launches.Calls.ShouldBeEmpty("G-139 " + shape);
                (await f.ParkAsync()).State.ShouldBe(AgentTaskParkState.Parked, shape);
                continue;
            }

            if (shape == "stale")
            {
                (await f.TryAnswerAsync("stale round", round: 2)).ShouldBeOfType<ConflictException>("G-144");
                var stale = await f.TaskAsync();
                stale.Attempt.ShouldBe(1, "G-144");
                stale.ReleasedSeatAnswerId.ShouldBeNull("G-144");
                stale.AgentId.ShouldBe(f.AgentId, "G-144");
                f.Launches.Calls.ShouldBeEmpty("G-153");
                continue;
            }

            if (shape is "quota" or "commit")
            {
                await using var db = f.Db();
                var before = await f.TaskAsync();
                if (shape == "quota")
                    db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
                    {
                        Id = Guid.NewGuid(), Provider = before.AgentKind, SubscriptionKey = before.AgentKind.ToString(),
                        AgentSessionId = f.SessionId, RemainingPercent = 1, ParseStatus = SubscriptionUsageParseStatus.Parsed,
                        ObservedAt = f.Now, ResetsAt = f.Now.AddDays(3)
                    });
                else
                    db.AgentTaskEvents.Add(new AgentTaskEvent
                    {
                        Id = Guid.NewGuid(), AgentTaskId = f.TaskId, Type = AgentTaskEventType.CommitRecoveryStarted,
                        Detail = "settlement-to-preserve", At = f.Now
                    });
                await db.SaveChangesAsync();
                var refusal = await f.TryAnswerAsync(Answer);
                var accepted = await f.TaskAsync();
                accepted.ReleasedSeatAnswer.ShouldBe(Answer, "G-145 " + shape);
                accepted.Attempt.ShouldBe(1, shape == "quota" ? "G-145" : "G-152");
                accepted.AgentId.ShouldBe(f.AgentId, shape);
                f.Launches.Calls.ShouldBeEmpty("G-153");
                if (shape == "quota")
                    refusal.ShouldBeOfType<SubscriptionQuotaLowException>("G-145").Code
                        .ShouldBe(SubscriptionQuotaLowException.ErrorCode, "G-145");
                else
                    refusal.ShouldBeOfType<ConflictException>("G-152").Code
                        .ShouldBe("commit_recovery_pending", "G-152");
                continue;
            }

            await StampAsync(f);
            await f.AnswerAsync(Answer);
            f.Launches.Calls.ShouldBeEmpty("G-153 " + shape);
            var continued = await f.TaskAsync();
            continued.Attempt.ShouldBe(2, shape);
            continued.ReleasedSeatAnswer.ShouldBe(Answer, shape);
            var repo = continued.RepoPath.ShouldNotBeNull();
            var branch = continued.WorktreeBranch.ShouldNotBeNull();
            string? retainedHead = null;
            if (shape == "dirty")
                await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "dirty.txt"), "dirty bytes");
            else if (shape == "sequencer")
            {
                var gitDir = await f.GitAsync(f.SourcePath, "rev-parse", "--path-format=absolute", "--git-dir");
                await File.WriteAllTextAsync(Path.Combine(gitDir, "MERGE_HEAD"), tip + "\n");
            }
            else if (shape == "advanced")
            {
                await f.GitAsync(f.SourcePath, "commit", "--allow-empty", "-m", "advanced");
                retainedHead = await f.GitAsync(f.SourcePath, "rev-parse", "HEAD");
                retainedHead.ShouldNotBe(tip, shape);
            }
            else if (shape == "divergent")
            {
                await f.GitAsync(repo, "checkout", "--orphan", "c1065-divergent");
                await f.GitAsync(repo, "commit", "--allow-empty", "-m", "divergent");
                var side = await f.GitAsync(repo, "rev-parse", "HEAD");
                await f.GitAsync(repo, "checkout", "master");
                await f.GitAsync(f.SourcePath, "reset", "--hard", side);
                retainedHead = side;
            }
            else if (shape == "missing-object")
            {
                await using var db = f.Db();
                await db.AgentTaskParks.Where(p => p.Id == parked.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.SourceSha, new string('b', 40)));
            }
            else if (shape == "changed-ref")
            {
                await f.GitAsync(f.SourcePath, "branch", "c1065-other", tip);
                await f.GitAsync(f.SourcePath, "symbolic-ref", "HEAD", "refs/heads/c1065-other");
            }
            else if (shape == "changed-endpoint")
            {
                var url = await f.GitAsync(f.SourcePath, "remote", "get-url", "--push", "origin");
                await f.GitAsync(f.SourcePath, "remote", "set-url", "origin", url + "-moved");
            }
            else if (shape == "missing-wrong-tip")
            {
                Directory.Delete(f.SourcePath, recursive: true);
                await f.GitAsync(repo, "worktree", "prune");
                await f.GitAsync(repo, "commit", "--allow-empty", "-m", "other tip");
                var other = await f.GitAsync(repo, "rev-parse", "HEAD");
                other.ShouldNotBe(tip, shape);
                await f.GitAsync(repo, "branch", "-f", branch, other);
            }
            else if (shape == "capacity")
                f.Directory.Capacity = 0;
            else if (shape == "host")
            {
                await f.EditAsync((task, _) => task.RunnerId = "pinned-other");
                f.Directory.RefuseNewWork = true;
            }
            else if (shape == "kind")
                await f.EditAsync((task, _) => task.AgentKind = AgentKind.OpenCode);
            else if (shape == "platform")
                await f.EditAsync((task, _) => task.RequiredPlatform = RequiredPlatform.Windows);
            else if (shape == "scope")
            {
                await using var db = f.Db();
                var holder = Guid.NewGuid();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = holder, RootTaskId = holder, Goal = "hold the shared checkout",
                    Status = AgentTaskStatus.Working, Workspace = WorkspaceMode.Shared, Role = AgentTaskRole.Code,
                    RepoPath = repo, WorkingDirectory = continued.WorkingDirectory, CreatedAt = f.Now, Attempt = 1,
                    AgentKind = AgentKind.ClaudeCode
                });
                await db.SaveChangesAsync();
            }
            else if (shape == "workspace")
            {
                using var scope = f.Harness.Provider.CreateScope();
                var key = WorkspaceReservationKey.ForTask(continued.WorktreePath, continued.WorkingDirectory,
                    continued.WorktreeBranch, continued.RepoPath);
                var fence = await scope.ServiceProvider.GetRequiredService<IWorkspaceReservationJournal>()
                    .TryAdmitConsumerAsync(new(key, WorkspaceReservationKind.HistoricalFence, Guid.NewGuid()), default);
                fence.Accepted.ShouldBeTrue(shape);
            }

            await f.DispatchAsync();
            f.Launches.Calls.ShouldBeEmpty(shape);
            var held = await f.TaskAsync();
            held.ReleasedSeatAnswer.ShouldBe(Answer, shape);
            held.AgentId.ShouldBe(f.AgentId, shape);
            held.Attempt.ShouldBe(2, shape);
            if (shape == "kind")
            {
                held.AgentKind.ShouldBe(AgentKind.OpenCode, "G-148");
                held.RunnerId.ShouldBe("fixture", "G-148");
                held.Status.ShouldBe(AgentTaskStatus.Blocked, "G-148");
                held.AgentSessionId.ShouldBeNull("G-148");
            }
            else if (shape == "platform")
            {
                held.RequiredPlatform.ShouldBe(RequiredPlatform.Windows, "G-149");
                held.AgentSessionId.ShouldBeNull("G-149");
                held.Status.ShouldBe(AgentTaskStatus.Blocked, "G-149");
            }
            else if (shape == "host")
                held.RunnerId.ShouldBe("pinned-other", "G-147");
            else if (shape == "workspace")
                (held.Status is AgentTaskStatus.Queued or AgentTaskStatus.Failed).ShouldBeTrue("G-151");
            else
                held.Status.ShouldBe(AgentTaskStatus.Queued, shape);

            if (shape == "dirty")
                (await File.ReadAllTextAsync(Path.Combine(f.SourcePath, "dirty.txt"))).ShouldBe("dirty bytes", "G-141");
            else if (shape == "sequencer")
            {
                var gitDir = await f.GitAsync(f.SourcePath, "rev-parse", "--path-format=absolute", "--git-dir");
                File.Exists(Path.Combine(gitDir, "MERGE_HEAD")).ShouldBeTrue("G-141");
                (await f.GitAsync(f.SourcePath, "rev-parse", "HEAD")).ShouldBe(tip, "G-141");
            }
            else if (shape is "advanced" or "divergent")
                (await f.GitAsync(f.SourcePath, "rev-parse", "HEAD")).ShouldBe(retainedHead, "G-140");
            else if (shape == "changed-ref")
            {
                (await f.GitAsync(f.SourcePath, "rev-parse", "HEAD")).ShouldBe(tip, "G-142");
                (await f.GitAsync(f.SourcePath, "symbolic-ref", "-q", "HEAD")).ShouldBe("refs/heads/c1065-other", "G-142");
            }
            else if (shape == "changed-endpoint")
                (await f.GitAsync(f.SourcePath, "remote", "get-url", "--push", "origin")).ShouldEndWith("-moved", Case.Sensitive, "G-143");
            else if (shape == "missing-object")
            {
                Directory.Exists(f.SourcePath).ShouldBeTrue("G-140");
                (await f.GitAsync(f.SourcePath, "rev-parse", "HEAD")).ShouldBe(tip, "G-140");
            }
            else if (shape == "missing-wrong-tip")
                Directory.Exists(f.SourcePath).ShouldBeFalse("G-135");
        }
    }

    [Test]
    public async Task C1097_RefusedParkedResumeWarnsOncePerReason()
    {
        const string dirty = "park_resume_refused:park_dirty";
        const string moved = "park_resume_refused:park_source_changed";
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Blocked, parking: true);
        var tip = await PublishAsync(f);
        await StampAsync(f);
        await f.AnswerAsync(Answer);
        await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "dirty.txt"), "dirty bytes");

        await f.DispatchAsync();
        await f.DispatchAsync();
        await f.DispatchAsync();

        var first = await ResumeWarningsAsync(f);
        string.Join(",", first).ShouldBe(dirty, "G-9");
        f.Launches.Calls.ShouldBeEmpty("G-9");
        var held = await f.TaskAsync();
        held.Status.ShouldBe(AgentTaskStatus.Queued, "G-9");
        held.Attempt.ShouldBe(2, "G-9");
        held.ReleasedSeatAnswer.ShouldBe(Answer, "G-9");

        await OpenLaterParkEpisodeAsync(f);
        await f.DispatchAsync();
        string.Join(",", await ResumeWarningsAsync(f)).ShouldBe(dirty + "," + dirty, "G-9");
        f.Launches.Calls.ShouldBeEmpty("G-9");
        (await f.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Queued, "G-9");

        File.Delete(Path.Combine(f.SourcePath, "dirty.txt"));
        await f.GitAsync(f.SourcePath, "commit", "--allow-empty", "-m", "move the parked tip");
        await f.DispatchAsync();
        var reasons = string.Join(",", await ResumeWarningsAsync(f));
        reasons.ShouldBe(dirty + "," + dirty + "," + moved, "G-9");
        f.Launches.Calls.ShouldBeEmpty("G-9");

        await f.GitAsync(f.SourcePath, "reset", "--hard", tip);
        await f.DispatchAsync();
        f.Launches.Calls.Count.ShouldBe(1, "G-9");
        (await f.TaskAsync()).AgentSessionId.ShouldNotBeNull("G-9");
        string.Join(",", await ResumeWarningsAsync(f)).ShouldBe(reasons, "G-9");
    }

    [Test]
    public async Task C1097Telemetry_LookupTimeoutKeepsRefusedParkQueued()
    {
        var fault = new ParkResumeWarningFault { Armed = ParkResumeWarningFault.Cut.LookupTimeout };
        await using var f = await PrepareDirtyRefusalAsync(fault);
        var tick = await f.DispatchAsync();
        fault.Faults.ShouldBeGreaterThan(0, "G-9 lookup");
        await AssertRefusalUnchangedAsync(f, tick, "G-9 lookup");
        (await ResumeWarningsAsync(f)).Count.ShouldBe(1, "G-9 lookup");
    }

    [Test]
    public async Task C1097Telemetry_InsertCancellationKeepsRefusedParkQueued()
    {
        var fault = new ParkResumeWarningFault { Armed = ParkResumeWarningFault.Cut.InsertNotCallerCancel };
        await using var f = await PrepareDirtyRefusalAsync(fault);
        var tick = await f.DispatchAsync();
        fault.Faults.ShouldBeGreaterThan(0, "G-9 insert");
        await AssertRefusalUnchangedAsync(f, tick, "G-9 insert");
        (await ResumeWarningsAsync(f)).ShouldBeEmpty("G-9 insert");
    }

    [Test]
    public async Task C1097Telemetry_CallerCancellationStillPropagates()
    {
        var fault = new ParkResumeWarningFault { Armed = ParkResumeWarningFault.Cut.LookupCallerCancel };
        await using var f = await PrepareDirtyRefusalAsync(fault);
        using var caller = new CancellationTokenSource();
        fault.Caller = caller;
        await Should.ThrowAsync<OperationCanceledException>(() => f.DispatchAsync(caller.Token));
        fault.Faults.ShouldBeGreaterThan(0, "G-9 cancel");
        var held = await f.TaskAsync();
        held.Status.ShouldBe(AgentTaskStatus.Queued, "G-9 cancel");
        held.Attempt.ShouldBe(2, "G-9 cancel");
        held.ReleasedSeatAnswer.ShouldBe(Answer, "G-9 cancel");
        held.FailureReason.ShouldBeNull("G-9 cancel");
        held.AgentSessionId.ShouldBeNull("G-9 cancel");
        f.Launches.Calls.ShouldBeEmpty("G-9 cancel");
    }

    private static async Task<RunnerSeatReleaseFixture> PrepareDirtyRefusalAsync(ParkResumeWarningFault fault)
    {
        var f = await RunnerSeatReleaseFixture.CreateAsync(
            AgentTaskStatus.Blocked, parking: true, configureDb: options => options.AddInterceptors(fault));
        await PublishAsync(f);
        await StampAsync(f);
        await f.AnswerAsync(Answer);
        await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "dirty.txt"), "dirty bytes");
        return f;
    }

    private static async Task AssertRefusalUnchangedAsync(
        RunnerSeatReleaseFixture f, AgentTaskDispatcher.TickResult tick, string label)
    {
        tick.Failures.ShouldBe(0, label);
        tick.Dispatched.ShouldBe(0, label);
        tick.HeldOnLease.ShouldBe(0, label);
        tick.Eligible.ShouldBe(1, label);
        f.Launches.Calls.ShouldBeEmpty(label);
        var held = await f.TaskAsync();
        held.Status.ShouldBe(AgentTaskStatus.Queued, label);
        held.Attempt.ShouldBe(2, label);
        held.ReleasedSeatAnswer.ShouldBe(Answer, label);
        held.FailureReason.ShouldBeNull(label);
        held.AgentSessionId.ShouldBeNull(label);
    }

    private static async Task OpenLaterParkEpisodeAsync(RunnerSeatReleaseFixture f)
    {
        var prior = await f.ParkAsync();
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await using var db = f.Db();
        await using var tx = await db.Database.BeginTransactionAsync();
        (await db.AgentTaskParks.Where(p => p.Id == prior.Id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.State, AgentTaskParkState.Held)
            .SetProperty(p => p.PublicationReceiptId, (Guid?)null))).ShouldBe(1, "G-9");
        db.AgentTaskParks.Add(new AgentTaskPark
        {
            Id = Guid.NewGuid(),
            TaskId = prior.TaskId,
            Attempt = prior.Attempt,
            BlockEventId = Guid.NewGuid(),
            TaskConcurrencyToken = prior.TaskConcurrencyToken,
            Workspace = prior.Workspace,
            WorktreePath = prior.WorktreePath,
            FullRef = prior.FullRef,
            SourceSha = prior.SourceSha,
            EndpointFingerprint = prior.EndpointFingerprint,
            PublicationReceiptId = prior.PublicationReceiptId,
            RunnerSeatReleaseId = prior.RunnerSeatReleaseId,
            BlockedAt = prior.BlockedAt,
            State = AgentTaskParkState.Parked,
            ReasonCode = prior.ReasonCode,
            CreatedAt = f.Now,
            UpdatedAt = f.Now,
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static async Task<List<string>> ResumeWarningsAsync(RunnerSeatReleaseFixture f)
    {
        await using var db = f.Db();
        return await db.AgentTaskEvents.AsNoTracking()
            .Where(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Warning
                && e.Detail.StartsWith("park_resume_refused:"))
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => e.Detail)
            .ToListAsync();
    }

    private static async Task<string> PublishAsync(RunnerSeatReleaseFixture f,
        WorkspaceMode mode = WorkspaceMode.Worktree, bool handle = true)
    {
        await f.CreateSourceAsync(mode);
        var tip = await CommitTipAsync(f);
        await f.EditAsync((task, _) => task.ResultFilePath = "report.md");
        using var scope = f.Harness.Provider.CreateScope();
        var task = await f.TaskAsync();
        await using var db = f.Db();
        var block = await db.AgentTaskEvents.Where(e => e.AgentTaskId == f.TaskId && e.Type == AgentTaskEventType.Blocked)
            .OrderByDescending(e => e.At).Select(e => e.Id).FirstAsync();
        var id = (await scope.ServiceProvider.GetRequiredService<BlockedTaskParkingService>()
            .RegisterAsync(f.TaskId, task.Attempt, block, task.ConcurrencyToken, default)).ShouldNotBeNull();
        (await scope.ServiceProvider.GetRequiredService<TaskParkPublicationService>()
            .PrepareAsync(id, default)).Evidence.ShouldNotBeNull();
        f.Wire.ConditionalCommands.ShouldBe(0, "publication setup cannot release the seat");
        if (handle) await f.HandleParkAsync();
        var park = await f.ParkAsync();
        park.SourceSha.ShouldBe(tip, "publication records the pushed tip");
        park.PublicationReceiptId.ShouldNotBeNull();
        park.FullRef.ShouldBe("refs/heads/" + task.WorktreeBranch);
        tip.ShouldNotBe(await f.GitAsync(task.RepoPath!, "rev-parse", "master"));
        tip.ShouldNotBe(task.WorktreeBaseSha);
        return tip;
    }

    private static async Task<string> CommitTipAsync(RunnerSeatReleaseFixture f)
    {
        var branch = (await f.TaskAsync()).WorktreeBranch.ShouldNotBeNull();
        await File.WriteAllTextAsync(Path.Combine(f.SourcePath, "parked-tip.txt"), "parked tip");
        await f.GitAsync(f.SourcePath, "add", "parked-tip.txt");
        await f.GitAsync(f.SourcePath, "commit", "-m", "parked tip");
        await f.GitAsync(f.SourcePath, "push", "--no-follow-tags", "origin", $"HEAD:refs/heads/{branch}");
        return await f.GitAsync(f.SourcePath, "rev-parse", "HEAD");
    }

    private static Task StampAsync(RunnerSeatReleaseFixture f) => f.EditAsync((task, _) =>
    {
        task.RepliedAtSequence = 9;
        task.ReportNudgedAt = f.Now;
        task.ReportNudgeMessageId = Guid.NewGuid();
        task.NextCheckAt = f.Now.AddHours(1);
        task.CheckCount = 4;
        task.RemoteWorktreePath = task.WorktreePath;
    });

    private static async Task<string> BriefAsync(RunnerSeatReleaseFixture f, Guid sessionId)
    {
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == sessionId);
        return (row.RemoteSpillBody ?? "") + "\n" + row.Body;
    }

    private sealed class ParkResumeWarningFault : DbCommandInterceptor
    {
        public enum Cut { None, LookupTimeout, InsertNotCallerCancel, LookupCallerCancel }

        public Cut Armed { get; set; }
        public CancellationTokenSource? Caller { get; set; }
        public int Faults { get; private set; }
        private bool _sawLookup;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Observe(command);
            return ValueTask.FromResult(result);
        }

        private void Observe(DbCommand command)
        {
            var text = command.CommandText;
            if (IsLookup(text))
                _sawLookup = true;
            if (Armed == Cut.LookupTimeout && IsLookup(text))
            {
                Faults++;
                throw new TimeoutException("injected park-resume warning lookup timeout");
            }

            if (Armed == Cut.InsertNotCallerCancel && _sawLookup
                && text.StartsWith("INSERT INTO \"AgentTaskEvents\"", StringComparison.Ordinal))
            {
                Faults++;
                throw new OperationCanceledException(
                    "injected park-resume warning insert cancellation", CancellationToken.None);
            }

            if (Armed == Cut.LookupCallerCancel && IsLookup(text))
            {
                Faults++;
                var caller = Caller ?? throw new InvalidOperationException("caller token was not armed");
                caller.Cancel();
                throw new OperationCanceledException(caller.Token);
            }
        }

        private static bool IsLookup(string text) =>
            text.Contains("SELECT a.\"Detail\"", StringComparison.Ordinal)
            && text.Contains("FROM \"AgentTaskEvents\"", StringComparison.Ordinal)
            && text.Contains("\"At\" >=", StringComparison.Ordinal)
            && text.Contains("ORDER BY a.\"At\" DESC", StringComparison.Ordinal);
    }

    private sealed class ResumeSaveCut : SaveChangesInterceptor
    {
        public required string Boundary { get; init; }
        public bool Armed { get; set; }
        public bool Hit { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && !Hit && Matches(data.Context!))
            {
                Hit = true;
                throw new InvalidOperationException($"injected {Boundary}");
            }
            return ValueTask.FromResult(result);
        }

        private bool Matches(DbContext db) => Boundary switch
        {
            "accept-before" => db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 1 && e.Entity.ReleasedSeatAnswerId != null),
            "attempt-before" => db.ChangeTracker.Entries<AgentTask>().Any(e => e.Entity.Attempt == 2 && e.Entity.Status == AgentTaskStatus.Queued),
            "queue-before" => db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added),
            _ => false
        };
    }
}
