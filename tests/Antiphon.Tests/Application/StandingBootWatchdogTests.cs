using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1156 S1-S3 and S5 (option A, detection only): the real <c>BootReplyWatchdogService</c>
/// sweep over a taskless AlwaysOn session on an isolated PostgreSQL database with a
/// <c>FakeTimeProvider</c>, through the new <c>StandingBootWatchFixture</c>. Design V-2, V-5,
/// V-6, V-7 and V-12 in
/// <c>docs/superpowers/plans/2026-10-08-card-1156-alwayson-boot-watchdog-test-design.md</c>.
/// Every method asserts the fail-closed rule: the recording stopper is empty, the runner's
/// kill/start/compaction-stop/input/release counters are zero, the session row keeps its status,
/// termination source, EndedAt and owner pointer, the supervision row is byte-equal to its seeded
/// snapshot (or still absent), and no queue row, park row, alias hold or probe is created.
/// </summary>
[Category("Integration")]
public class StandingBootWatchdogTests
{
    /// <summary>
    /// V-2 (S1-S3). A nine-minute real prompt, no model row, live matching runner listing, one
    /// AlwaysOn owner per provider kind whose delivery verification is Supported (Grok with
    /// GrokRulesState Ready). <c>TranscriptWorkingStateQuery</c> reads Working = true before and
    /// after. Sweeps at 9 and 21 minutes and real <c>AgentSupervisorService.TickAsync</c> ticks
    /// between them keep the session and pointer: Warning then Error receipts, nothing else.
    /// </summary>
    [Test]
    [Arguments("claude-code")]
    [Arguments("grok")]
    [Arguments("codex")]
    public async Task C1156_Working_boot_keeps_its_session_and_supervisor_custody(string kind)
    {
        var agentKind = kind switch
        {
            "claude-code" => AgentKind.ClaudeCode,
            "grok" => AgentKind.Grok,
            "codex" => AgentKind.Codex,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        await using var f = await StandingBootWatchFixture.CreateAsync(new StandingBootWatchOptions { Kind = agentKind });
        (await f.WorkingAsync()).ShouldBeTrue("control: a real prompt with no turn boundary is Working");
        var before = await f.SnapshotAsync();
        before.Supervision.ShouldBe("absent", "control: no supervision row before the sweep");

        (await f.SweepAsync()).ShouldBe(1, f.Warnings());
        var receipts = await f.ReceiptsAsync();
        receipts.Count.ShouldBe(1, f.Warnings());
        receipts[0].Severity.ShouldBe(AlertSeverity.Warning);
        receipts[0].FailureReason.ShouldEndWith(";stage=detected");
        (await f.SnapshotAsync()).ShouldBe(before, "the 9-minute sweep wrote nothing but its receipt");

        var tempRoot = Path.Combine(Path.GetTempPath(), $"c1156-supervisor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            await using var harness = AgentSupervisionTests.BuildHarness(tempRoot, [], connectionString: f.ConnectionString);
            await harness.Supervisor().TickAsync(CancellationToken.None);
            var afterTick = await f.SnapshotAsync();
            await AssertSupervisorKeptCustodyAsync(f, before, afterTick);

            f.At(f.PromptAt.AddMinutes(21));
            (await f.SweepAsync()).ShouldBe(1, f.Warnings());
            (await f.SnapshotAsync()).ShouldBe(afterTick, "the 21-minute sweep wrote nothing but its receipt");
            await harness.Supervisor().TickAsync(CancellationToken.None);
            await AssertSupervisorKeptCustodyAsync(f, before, await f.SnapshotAsync());

            harness.Runner.KillCalls.ShouldBe(0, "the supervisor's runner killed nothing");
            harness.Runner.ConditionalInputCalls.ShouldBeEmpty();
            harness.Runner.CompactionStops.ShouldBeEmpty("no CARD-0079 stop was borrowed");
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup.
            }
        }

        receipts = await f.ReceiptsAsync();
        receipts.Select(r => r.Severity).ShouldBe([AlertSeverity.Warning, AlertSeverity.Error], f.Warnings());
        receipts[1].FailureReason.ShouldEndWith(";stage=operator");
        receipts.Select(r => r.FailureReason![..r.FailureReason!.IndexOf("stage=", StringComparison.Ordinal)])
            .Distinct().Count().ShouldBe(1, "both stages belong to one episode");
        receipts.ShouldAllBe(r => r.AgentId == f.AgentId && !r.Message.Contains(StandingBootWatchFixture.PromptCanary));
        f.AssertNothingDestructive();
        (await f.WorkingAsync()).ShouldBeTrue("still Working: nothing ended the turn");
        var session = await f.SessionAsync();
        session.Status.ShouldBe(SessionStatus.Running);
        session.EndedAt.ShouldBeNull();
        session.BootReplyDueAt.ShouldNotBeNull("the watch stays armed for the episode");
    }

    /// <summary>
    /// V-5 (S3). One receipt per episode key and stage across repeated-tick, fresh-provider and
    /// concurrent-sweeps (two providers meeting at the writer's session lock); new-prompt,
    /// new-generation and new-resume-clock each mint a new key with its own receipt and the old
    /// episode leaves the current projection; legacy-cleared-watch (both watch columns null after
    /// an old raise with an old <c>bootSeq=</c> incident) self-heals through the existing
    /// predicate and records the new key once, never a second old-format receipt.
    /// <para>new-generation and new-resume-clock each move exactly one identity component (the
    /// accepted generation; the resume clock, kept before the prompt) so the key's g= and l=
    /// parts are each proved necessary on their own; a new prompt would also change p=, which is
    /// the new-prompt argument.</para>
    /// </summary>
    [Test]
    [Arguments("repeated-tick")]
    [Arguments("fresh-provider")]
    [Arguments("concurrent-sweeps")]
    [Arguments("new-prompt")]
    [Arguments("new-generation")]
    [Arguments("new-resume-clock")]
    [Arguments("legacy-cleared-watch")]
    public async Task C1156_Episodes_deduplicate_and_reopen_only_for_new_identity(string shape)
    {
        var options = new StandingBootWatchOptions
        {
            ResumedAfterStart = shape is "new-generation" or "new-resume-clock" ? TimeSpan.FromHours(1) : null,
            Arm = shape != "legacy-cleared-watch",
        };
        await using var f = await StandingBootWatchFixture.CreateAsync(options);
        if (shape == "legacy-cleared-watch")
        {
            await using var db = f.Read();
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = f.AgentId,
                SessionId = f.SessionId,
                Kind = AgentIncidentKind.LivenessProbeFailed,
                Severity = AlertSeverity.Warning,
                Message = "an old raise that cleared the watch",
                FailureReason = BootReplyWatchdogService.EpisodeKey(1),
                CreatedAt = f.Now0.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
            var session = await f.SessionAsync();
            session.BootPromptSequence.ShouldBeNull("control: the legacy raise left the watch cleared");
            session.BootReplyDueAt.ShouldBeNull();
        }

        var before = await f.SnapshotAsync();
        var expected = 1;
        switch (shape)
        {
            case "repeated-tick":
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                (await f.SweepAsync()).ShouldBe(0, f.Warnings());
                (await f.SweepAsync()).ShouldBe(0, f.Warnings());
                break;
            case "fresh-provider":
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                await using (var second = f.Recreate())
                    (await second.SweepAsync()).ShouldBe(0, second.Warnings());
                (await f.SweepAsync()).ShouldBe(0, f.Warnings());
                break;
            case "concurrent-sweeps":
            {
                var hold = new HoldAfterDedup();
                f.WriterInterceptors = [hold];
                var first = Task.Run(() => f.SweepAsync());
                (await hold.Held.Task.WaitAsync(TimeSpan.FromSeconds(20))).ShouldBeTrue("sweep A reached its dedup read");
                await using (var second = f.Recreate())
                {
                    second.WriterInterceptors = [hold];
                    (await second.SweepAsync().WaitAsync(TimeSpan.FromSeconds(20)))
                        .ShouldBe(0, "sweep B skips the row A holds: " + second.Warnings());
                }

                hold.Release.TrySetResult();
                (await first.WaitAsync(TimeSpan.FromSeconds(20))).ShouldBe(1, f.Warnings());
                hold.Arrivals.ShouldBe(1, "only sweep A reached the dedup read; B declined at the lock");
                break;
            }
            case "new-prompt":
            {
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                await f.AddEntryAsync(TranscriptKinds.UserPrompt, "a refinement, still unanswered", f.Now0);
                (await f.SweepAsync()).ShouldBe(0, f.Warnings());
                var rearmed = await f.SessionAsync();
                rearmed.BootPromptSequence.ShouldBe(2, "the watch now names the refinement");
                rearmed.BootReplyDueAt.ShouldBe(f.Now0.AddMinutes(8));
                f.At(f.Now0.AddMinutes(8));
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                f.At(f.Now0.AddMinutes(12));
                (await f.SweepAsync()).ShouldBe(0, "nothing further on either episode yet: " + f.Warnings());
                expected = 2;
                before = before with { PromptRows = before.PromptRows + 1 };
                break;
            }
            case "new-generation":
            case "new-resume-clock":
            {
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                await using (var db = f.Read())
                {
                    if (shape == "new-generation")
                    {
                        await db.AgentSessions.Where(s => s.Id == f.SessionId)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedAt, f.StartedAt.AddHours(-1)));
                    }
                    else
                    {
                        await db.AgentSessions.Where(s => s.Id == f.SessionId)
                            .ExecuteUpdateAsync(s => s.SetProperty(
                                x => x.LaunchResumedAt, (DateTime?)f.StartedAt.AddHours(2)));
                    }
                }

                before = await f.SnapshotAsync();
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                (await f.SweepAsync()).ShouldBe(0, f.Warnings());
                expected = 2;
                break;
            }
            case "legacy-cleared-watch":
            {
                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                (await f.SweepAsync()).ShouldBe(0, f.Warnings());
                var incidents = await f.IncidentsAsync();
                incidents.Count(i => i.FailureReason == BootReplyWatchdogService.EpisodeKey(1))
                    .ShouldBe(1, "no second old-format receipt");
                incidents.Count.ShouldBe(2, "the old receipt plus exactly one new-key receipt");
                var healed = await f.SessionAsync();
                healed.BootPromptSequence.ShouldBe(1, "the cleared watch self-healed onto the same prompt");
                healed.BootReplyDueAt.ShouldNotBeNull();
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), shape, null);
        }

        var receipts = await f.ReceiptsAsync();
        receipts.Count.ShouldBe(expected, f.Warnings());
        receipts.ShouldAllBe(r => r.Severity == AlertSeverity.Warning && r.FailureReason!.EndsWith(";stage=detected"));
        receipts.Select(r => r.FailureReason).Distinct().Count().ShouldBe(expected, "one receipt per key and stage");
        if (shape == "new-prompt")
            receipts.Select(r => r.FailureReason!.Split(';')[3]).ShouldBe(["p=1", "p=2"]);
        if (shape == "new-generation")
        {
            receipts.Select(r => r.FailureReason!.Split(';')[1]).Distinct().Count().ShouldBe(2, "a new g=");
            receipts.Select(r => r.FailureReason!.Split(';')[2]).Distinct().Count().ShouldBe(1, "the same l=");
            receipts.Select(r => r.FailureReason!.Split(';')[3]).Distinct().Count().ShouldBe(1, "the same p=");
        }

        if (shape == "new-resume-clock")
        {
            receipts.Select(r => r.FailureReason!.Split(';')[1]).Distinct().Count().ShouldBe(1, "the same g=");
            receipts.Select(r => r.FailureReason!.Split(';')[2]).Distinct().Count().ShouldBe(2, "a new l=");
            receipts.Select(r => r.FailureReason!.Split(';')[3]).Distinct().Count().ShouldBe(1, "the same p=");
        }

        (await f.SnapshotAsync()).ShouldBe(before, "custody is untouched");
        f.AssertNothingDestructive();
    }

    /// <summary>
    /// V-6 (S3). Faults on the writer's own context: read-fault (the dedup read), insert-fault
    /// (the INSERT command), save-fault (<c>SavingChangesAsync</c>), commit-fault (the transaction
    /// commit), publish-fault (the event bus throws after the commit), caller-cancel (the sweep's
    /// token is cancelled at the writer's lock statement and the cancellation propagates out of
    /// <c>SweepAsync</c>). No stop, no supervision mutation, no rolled-back row, nothing staged on
    /// the sweep's context; a publish fault keeps its one committed receipt; after the fault clears
    /// a fresh tick records exactly once.
    /// <para>A trailing armed-but-answered session B follows the faulted session A in the sweep's
    /// enumeration, so a telemetry row staged on the sweep's own context would be committed by B's
    /// disarm save.</para>
    /// </summary>
    [Test]
    [Arguments("read-fault")]
    [Arguments("insert-fault")]
    [Arguments("save-fault")]
    [Arguments("commit-fault")]
    [Arguments("publish-fault")]
    [Arguments("caller-cancel")]
    public async Task C1156_Telemetry_faults_preserve_custody_and_future_writes(string fault)
    {
        var bus = new MockEventBus { ThrowOnceOnEvent = fault == "publish-fault" ? "AgentChanged" : null };
        await using var f = await StandingBootWatchFixture.CreateAsync(new StandingBootWatchOptions { EventBus = bus });
        var trailing = await SeedAnsweredArmedSessionAsync(f);
        await using (var db = f.Read())
        {
            var order = await db.AgentSessions
                .Where(s => StandingBootWatchObservation.LiveStatuses.Contains(s.Status))
                .Select(s => s.Id)
                .ToListAsync();
            order.IndexOf(f.SessionId).ShouldBeLessThan(order.IndexOf(trailing),
                "precondition: the sweep enumerates the faulted session before the trailing one");
        }

        using var cancel = new CancellationTokenSource();
        var faults = new WriterFaults(fault, cancel);
        f.WriterInterceptors = [faults, faults.Save, faults.Commit];
        var before = await f.SnapshotAsync();

        if (fault == "caller-cancel")
        {
            await Should.ThrowAsync<OperationCanceledException>(() => f.SweepAsync(ct: cancel.Token));
        }
        else
        {
            (await f.SweepAsync()).ShouldBe(fault == "publish-fault" ? 1 : 0, f.Warnings());
            (await f.SessionAsync(trailing)).BootPromptSequence
                .ShouldBeNull("control: the trailing session's disarm save ran after the fault");
        }

        if (fault == "publish-fault")
        {
            f.Warnings().ShouldContain("Could not publish the standing boot receipt");
            bus.PublishedEvents.ShouldBeEmpty("the one publish threw");
        }
        else
        {
            faults.Fired.ShouldBeTrue($"the {fault} must actually fire");
        }

        if (fault is not ("publish-fault" or "caller-cancel"))
            f.Warnings().ShouldContain("Could not record standing boot receipt");
        if (fault == "caller-cancel")
        {
            f.Warnings().ShouldNotContain("Could not record",
                customMessage: "the caller's cancellation propagates; it is never swallowed as a telemetry fault\n" + f.Warnings());
        }

        f.Warnings().ShouldNotContain("Boot-reply sweep failed", customMessage: "a telemetry fault is isolated in the writer");
        (await f.ReceiptsAsync()).Count.ShouldBe(fault == "publish-fault" ? 1 : 0,
            "a failed telemetry transaction leaves nothing behind; a failed publish is after the commit");
        (await f.SnapshotAsync()).ShouldBe(before, "a telemetry fault changes no custody or supervision state");
        f.AssertNothingDestructive();

        faults.Clear();
        (await f.SweepAsync()).ShouldBe(fault == "publish-fault" ? 0 : 1, f.Warnings());
        (await f.SweepAsync()).ShouldBe(0, f.Warnings());
        var receipts = await f.ReceiptsAsync();
        receipts.Count.ShouldBe(1, "after the fault clears the episode is recorded exactly once: " + f.Warnings());
        receipts[0].FailureReason.ShouldEndWith(";stage=detected");
        (await f.SnapshotAsync()).ShouldBe(before);
        f.AssertNothingDestructive();
    }

    /// <summary>
    /// V-7 (S3). Fresh evidence committed before the writer's final read prevents the insert:
    /// reply-during-pull (the real runtime catch-up stores an AssistantText from the runner
    /// transcript), queued-owner-before-insert, blocked-owner-before-insert,
    /// pointer-changed-before-insert, generation-changed-before-insert (each committed on a second
    /// connection as the writer's lock statement executes), session-lock-held (a second connection
    /// holds an uncommitted UPDATE of the session row; the writer returns inside 30 s, logs nothing
    /// above Debug, writes nothing; after rollback the next tick records once).
    /// </summary>
    [Test]
    [Arguments("reply-during-pull")]
    [Arguments("queued-owner-before-insert")]
    [Arguments("blocked-owner-before-insert")]
    [Arguments("pointer-changed-before-insert")]
    [Arguments("generation-changed-before-insert")]
    [Arguments("session-lock-held")]
    public async Task C1156_Fresh_evidence_revokes_stale_emission(string change)
    {
        await using var f = await StandingBootWatchFixture.CreateAsync(new StandingBootWatchOptions
        {
            MinimumLogLevel = change == "session-lock-held" ? LogLevel.Debug : LogLevel.Information,
        });
        var before = await f.SnapshotAsync();
        switch (change)
        {
            case "reply-during-pull":
            {
                f.Runner.Transcript = id => new SessionRunnerTranscriptDto(id,
                [
                    new SessionRunnerTranscriptEvent(
                        id, 2, TranscriptKinds.AssistantText, $"c1156-reply-{id:N}", null,
                        new DateTimeOffset(f.Now0, TimeSpan.Zero), "assistant", "the reply the tailer had not stored",
                        null, null, null, null, null),
                ], 2);
                (await f.SweepAsync()).ShouldBe(0, f.Warnings());
                f.Runner.TranscriptPulls.ShouldBe(1, "the sweep pulled before deciding");
                await using (var db = f.Read())
                {
                    (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == f.SessionId
                        && t.Kind == TranscriptKinds.AssistantText)).ShouldBe(1, "the real runtime stored the pulled reply");
                }

                (await f.SessionAsync()).BootPromptSequence.ShouldBeNull("the answered watch is disarmed");
                (await f.SweepAsync()).ShouldBe(0, f.Warnings());
                f.Runner.TranscriptPulls.ShouldBe(1, "an answered session is never pulled again");
                (await f.ReceiptsAsync()).ShouldBeEmpty("the reply the pull landed ended the episode");
                (await f.SnapshotAsync()).ShouldBe(before);
                break;
            }
            case "queued-owner-before-insert":
            case "blocked-owner-before-insert":
            case "pointer-changed-before-insert":
            case "generation-changed-before-insert":
            {
                var interleave = new WriterInterleaving(async () =>
                {
                    await using var db = f.Read();
                    switch (change)
                    {
                        case "queued-owner-before-insert":
                        case "blocked-owner-before-insert":
                        {
                            var id = Guid.NewGuid();
                            db.AgentTasks.Add(new AgentTask
                            {
                                Id = id,
                                RootTaskId = id,
                                Title = "binds the standing session",
                                Goal = "owns the session from now on",
                                Role = AgentTaskRole.Code,
                                ModelLevel = AgentModelLevel.Frontier,
                                Workspace = WorkspaceMode.Shared,
                                WorkingDirectory = Path.GetTempPath(),
                                AgentSessionId = f.SessionId,
                                Status = change == "queued-owner-before-insert" ? AgentTaskStatus.Queued : AgentTaskStatus.Blocked,
                                CreatedAt = f.Now0,
                            });
                            await db.SaveChangesAsync();
                            break;
                        }
                        case "pointer-changed-before-insert":
                        {
                            var elsewhere = Guid.NewGuid().ToString("D");
                            await db.Agents.Where(a => a.Id == f.AgentId)
                                .ExecuteUpdateAsync(a => a.SetProperty(x => x.PersistentSessionId, elsewhere));
                            break;
                        }
                        default:
                            await db.AgentSessions.Where(s => s.Id == f.SessionId)
                                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedAt, f.StartedAt.AddHours(-1)));
                            break;
                    }
                });
                f.WriterInterceptors = [interleave];
                (await f.SweepAsync()).ShouldBe(0, f.Warnings());
                interleave.Arrivals.ShouldBe(1, "the change ran inside the writer, after the decision");
                await using (var db = f.Read())
                {
                    var pointer = f.SessionId.ToString("D");
                    var landed = change switch
                    {
                        "pointer-changed-before-insert" => !await db.Agents.AnyAsync(
                            a => a.Id == f.AgentId && a.PersistentSessionId == pointer),
                        "generation-changed-before-insert" => await db.AgentSessions.AnyAsync(
                            s => s.Id == f.SessionId && s.StartedAt == f.StartedAt.AddHours(-1)),
                        _ => await db.AgentTasks.AnyAsync(t => t.AgentSessionId == f.SessionId),
                    };
                    landed.ShouldBeTrue($"control: the {change} committed before the writer's lock statement");
                }

                (await f.ReceiptsAsync()).ShouldBeEmpty($"{change}: the decided episode is no longer current");
                f.Warnings().ShouldNotContain("Could not record", customMessage: "a mismatch is silent, not a fault\n" + f.Warnings());
                var after = await f.SnapshotAsync();
                after.Supervision.ShouldBe(before.Supervision);
                after.QueueBodies.ShouldBe(before.QueueBodies);
                (after.Parks, after.Releases, after.Holds, after.RestartIncidents, after.PromptRows)
                    .ShouldBe((before.Parks, before.Releases, before.Holds, before.RestartIncidents, before.PromptRows));
                var session = await f.SessionAsync();
                session.Status.ShouldBe(SessionStatus.Running);
                session.EndedAt.ShouldBeNull();
                session.TerminationSource.ShouldBe(default(SessionTerminationSource));
                break;
            }
            case "session-lock-held":
            {
                // Warm the model so first-use model-building logs fall outside the measured window.
                await using (var warm = f.Read())
                    await warm.AgentSessions.AnyAsync();

                await using var holder = new NpgsqlConnection(f.ConnectionString);
                await holder.OpenAsync();
                await using var hold = await holder.BeginTransactionAsync();
                await using (var update = new NpgsqlCommand(
                    "UPDATE \"AgentSessions\" SET \"LastSeenAt\" = \"LastSeenAt\" WHERE \"Id\" = @id", holder, hold))
                {
                    update.Parameters.AddWithValue("id", f.SessionId);
                    (await update.ExecuteNonQueryAsync()).ShouldBe(1, "the holder writes the session row");
                }

                await using (var probe = new NpgsqlConnection(f.ConnectionString))
                {
                    await probe.OpenAsync();
                    await using var check = new NpgsqlCommand(
                        "SELECT 1 FROM \"AgentSessions\" WHERE \"Id\" = @id FOR UPDATE NOWAIT", probe);
                    check.Parameters.AddWithValue("id", f.SessionId);
                    (await Should.ThrowAsync<PostgresException>(() => check.ExecuteScalarAsync()))
                        .SqlState.ShouldBe(PostgresErrorCodes.LockNotAvailable, "control: the row is really held");
                }

                var mark = f.LogEntries().Count;
                try
                {
                    (await f.SweepAsync().WaitAsync(TimeSpan.FromSeconds(30))).ShouldBe(0, f.Warnings());
                }
                finally
                {
                    await hold.RollbackAsync();
                }

                var during = f.LogEntries().Skip(mark).ToList();
                var described = string.Join('\n', during.Select(e => $"{e.Level} {e.Category} [{e.EventId.Id}]: {e.Message}"));
                during.Where(e => e.Level > LogLevel.Debug && e.EventId.Id != RelationalEventId.CommandExecuted.Id)
                    .ShouldBeEmpty($"expected contention logs nothing above Debug\n{described}");
                during.Where(e => e.Exception is not null)
                    .ShouldBeEmpty($"expected contention is a result, not an exception\n{described}");
                during.ShouldContain(
                    e => e.Level == LogLevel.Debug && e.Message.Contains("is missing or mid-update"),
                    $"the writer met the held row and said so at Debug\n{described}");
                (await f.ReceiptsAsync()).ShouldBeEmpty("a held session row writes nothing");

                (await f.SweepAsync()).ShouldBe(1, f.Warnings());
                (await f.ReceiptsAsync()).Count.ShouldBe(1, "after the holder rolls back the next tick records once");
                (await f.SnapshotAsync()).ShouldBe(before);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }

        f.AssertNothingDestructive();
    }

    /// <summary>
    /// V-12 (S5). Evidence variants that must never authorize recovery: runtime-absent (no runtime
    /// registered: stored-evidence detection), pull-fault (the runner transcript request throws),
    /// successful-unchanged-pull, runner-absent (the runner lists nothing), non-working (prompt then
    /// an interrupt marker: Working false), queued-prompt (QueuedUserPrompt only: the receipt says
    /// "queued prompt record" and "no reply observed", never "accepted" or "delivered", and the
    /// prompt is never resent), legacy-latch (seeded ConsecutiveFailures 2, LivenessLatchedAt set,
    /// a future NextRestartAt and nondefault unrelated fields: the row is byte-equal afterwards),
    /// terminal-task-history (a Succeeded and a Failed task on this session do not withhold the
    /// standing detection), non-alwayson-legacy-diagnostic (an owner with AlwaysOn false keeps the
    /// existing generic <c>bootSeq=</c> receipt and the existing disarm, with no supervision row
    /// and no stop). Every variant: empty stopper, zero runner counters, no queue or park row,
    /// message/byte snapshot unchanged, no probe enqueued.
    /// <para>Implemented with S1-S3 because the checkpoint table runs it (CP-8) in the S1-S3 group.</para>
    /// </summary>
    [Test]
    [Arguments("runtime-absent")]
    [Arguments("pull-fault")]
    [Arguments("successful-unchanged-pull")]
    [Arguments("runner-absent")]
    [Arguments("non-working")]
    [Arguments("queued-prompt")]
    [Arguments("legacy-latch")]
    [Arguments("terminal-task-history")]
    [Arguments("non-alwayson-legacy-diagnostic")]
    public async Task C1156_Evidence_variants_never_authorize_recovery(string variant)
    {
        var options = variant switch
        {
            "runner-absent" => new StandingBootWatchOptions { Listed = false },
            "non-working" => new StandingBootWatchOptions { InterruptAfterPrompt = true },
            "queued-prompt" => new StandingBootWatchOptions { PromptKind = TranscriptKinds.QueuedUserPrompt },
            "legacy-latch" => new StandingBootWatchOptions { LegacyState = true },
            "terminal-task-history" => new StandingBootWatchOptions { TerminalTaskHistory = true },
            "non-alwayson-legacy-diagnostic" => new StandingBootWatchOptions { AlwaysOn = false },
            "runtime-absent" or "pull-fault" or "successful-unchanged-pull" => new StandingBootWatchOptions(),
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
        };
        await using var f = await StandingBootWatchFixture.CreateAsync(options);
        if (variant == "pull-fault")
            f.Runner.TranscriptFault = new HttpRequestException("C1156 runner unreachable");
        if (variant == "non-working")
            (await f.WorkingAsync()).ShouldBeFalse("control: the interrupt marker ended the turn");
        if (variant == "legacy-latch")
            (await f.SnapshotAsync()).Supervision.ShouldContain("ConsecutiveFailures=", customMessage: "control: the legacy row is seeded");
        var before = await f.SnapshotAsync();
        var runtime = variant != "runtime-absent";

        var legacy = variant == "non-alwayson-legacy-diagnostic";
        (await f.SweepAsync(runtime)).ShouldBe(1, f.Warnings());
        var afterFirst = await f.SessionAsync();
        (await f.SweepAsync(runtime)).ShouldBe(0, "a repeated detection adds nothing: " + f.Warnings());

        // The standing watch stays armed, so the repeat is the cheap recorded-key path with no pull.
        // The generic diagnostic keeps today's shape: it disarms, the next tick self-heals the watch
        // and pulls once more before finding its bootSeq= receipt already on record.
        f.Runner.TranscriptPulls.ShouldBe(!runtime ? 0 : legacy ? 2 : 1, "pulls per cold observation, none without a runtime");
        f.Runner.Lists.ShouldBe(0, "runner listing is not evidence on this path");
        f.Warnings().ShouldNotContain("Boot-reply sweep failed");
        var incidents = await f.IncidentsAsync();
        incidents.Count.ShouldBe(1, f.Warnings());
        var incident = incidents[0];
        incident.Message.ShouldNotContain(StandingBootWatchFixture.PromptCanary);
        incident.Message.ShouldContain("Detection only");
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        if (legacy)
        {
            incident.FailureReason.ShouldBe(BootReplyWatchdogService.EpisodeKey(1), "the generic diagnostic keeps its old key");
            afterFirst.BootPromptSequence.ShouldBeNull("the generic diagnostic keeps its disarm");
            (await f.ReceiptsAsync()).ShouldBeEmpty("a non-AlwaysOn owner gets no standing receipt");
        }
        else
        {
            incident.FailureReason.ShouldNotBeNull();
            incident.FailureReason.ShouldStartWith(StandingBootWatchPolicy.KeyPrefix);
            incident.FailureReason.ShouldEndWith(";stage=detected");
            (await f.SessionAsync()).BootReplyDueAt.ShouldNotBeNull("the standing watch stays armed");
        }

        if (variant == "queued-prompt")
        {
            incident.Message.ShouldContain("queued prompt record");
            incident.Message.ShouldContain("no reply observed");
            incident.Message.ShouldNotContain("accepted");
            incident.Message.ShouldNotContain("delivered");
        }

        (await f.SnapshotAsync()).ShouldBe(before, $"{variant}: custody, supervision, queue bytes and prompts unchanged");
        f.AssertNothingDestructive();
    }

    private static async Task<Guid> SeedAnsweredArmedSessionAsync(StandingBootWatchFixture f)
    {
        var id = Guid.NewGuid();
        await using var db = f.Read();
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            DefinitionName = "standing-boot-trailing",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = f.StartedAt,
            StartedAt = f.StartedAt,
            LastSeenAt = f.StartedAt,
            BootPromptSequence = 1,
            BootReplyDueAt = f.PromptAt.AddMinutes(8),
        });
        db.TranscriptEntries.Add(StandingBootWatchFixture.Entry(id, 1, TranscriptKinds.UserPrompt, "trailing", f.PromptAt));
        db.TranscriptEntries.Add(StandingBootWatchFixture.Entry(
            id, 2, TranscriptKinds.AssistantText, "answered", f.PromptAt.AddSeconds(30)));
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// After each receipt, a real supervisor tick: the live-session branch may create the
    /// supervision row or stamp LastHealthyAt/UpdatedAt, and nothing else. No failure count, latch,
    /// scheduled restart or restart incident; session, pointer, queue and prompts unchanged.
    /// </summary>
    private static async Task AssertSupervisorKeptCustodyAsync(
        StandingBootWatchFixture f, CustodySnapshot before, CustodySnapshot after)
    {
        after.Session.ShouldBe(before.Session);
        after.AgentPointer.ShouldBe(before.AgentPointer);
        (after.QueueRows, after.QueueBodies).ShouldBe((before.QueueRows, before.QueueBodies));
        (after.Parks, after.Releases, after.Holds, after.RestartIncidents, after.PromptRows)
            .ShouldBe((before.Parks, before.Releases, before.Holds, 0, before.PromptRows));
        await using var db = f.Read();
        var state = await db.AgentSupervisionStates.AsNoTracking().SingleOrDefaultAsync(s => s.AgentId == f.AgentId);
        if (state is null)
            return;
        state.ConsecutiveFailures.ShouldBe(0, after.Supervision);
        state.RestartBackoffFailures.ShouldBe(0, after.Supervision);
        state.LivenessLatchedAt.ShouldBeNull(after.Supervision);
        state.NextRestartAt.ShouldBeNull(after.Supervision);
        state.Suspended.ShouldBeFalse(after.Supervision);
    }

    /// <summary>
    /// Holds sweep A's writer right after its dedup read (inside its transaction, holding the
    /// session row lock) until the test releases it; only the first arrival is held. Bounded.
    /// </summary>
    private sealed class HoldAfterDedup : DbCommandInterceptor
    {
        private int _arrivals;

        public TaskCompletionSource<bool> Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Arrivals => Volatile.Read(ref _arrivals);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("FROM \"AgentIncidents\"", StringComparison.Ordinal)
                && Interlocked.Increment(ref _arrivals) == 1)
            {
                Held.TrySetResult(true);
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Runs one change on a second connection just as the writer's session lock statement
    /// executes: the sweep has already decided, and only the writer's revalidation stands between
    /// that decision and the insert.
    /// </summary>
    private sealed class WriterInterleaving(Func<Task> change) : DbCommandInterceptor
    {
        private int _arrivals;

        public int Arrivals => Volatile.Read(ref _arrivals);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"AgentSessions\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && Interlocked.Increment(ref _arrivals) == 1)
            {
                await change();
            }

            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// One fault at a time on the writer's own context, each firing once: the dedup read, the
    /// receipt INSERT, <c>SavingChangesAsync</c>, the commit, or (caller-cancel) the sweep's own
    /// token cancelled at the session lock statement.
    /// </summary>
    private sealed class WriterFaults : DbCommandInterceptor
    {
        private readonly string _fault;
        private readonly CancellationTokenSource _cancel;
        private volatile bool _armed = true;

        public WriterFaults(string fault, CancellationTokenSource cancel)
        {
            _fault = fault;
            _cancel = cancel;
            Save = new SaveFault(this);
            Commit = new CommitFault(this);
        }

        public bool Fired { get; private set; }

        public ISaveChangesInterceptor Save { get; }

        public DbTransactionInterceptor Commit { get; }

        public void Clear() => _armed = false;

        private bool Take(string fault)
        {
            if (!_armed || _fault != fault)
                return false;
            _armed = false;
            Fired = true;
            return true;
        }

        private void Maybe(DbCommand command)
        {
            var sql = command.CommandText;
            if (sql.TrimStart().StartsWith("SELECT", StringComparison.Ordinal)
                && sql.Contains("FROM \"AgentIncidents\"", StringComparison.Ordinal) && Take("read-fault"))
                throw new InvalidOperationException("C1156 injected read fault");
            if (sql.Contains("INSERT INTO \"AgentIncidents\"", StringComparison.Ordinal) && Take("insert-fault"))
                throw new InvalidOperationException("C1156 injected insert fault");
            if (sql.Contains("\"AgentSessions\"", StringComparison.Ordinal)
                && sql.Contains("FOR UPDATE", StringComparison.Ordinal) && Take("caller-cancel"))
            {
                _cancel.Cancel();
                throw new OperationCanceledException(_cancel.Token);
            }
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Maybe(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Maybe(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private sealed class SaveFault(WriterFaults owner) : SaveChangesInterceptor
        {
            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                if (owner.Take("save-fault"))
                    throw new InvalidOperationException("C1156 injected save fault");
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        private sealed class CommitFault(WriterFaults owner) : DbTransactionInterceptor
        {
            public override ValueTask<InterceptionResult> TransactionCommittingAsync(
                DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
                CancellationToken cancellationToken = default)
            {
                if (owner.Take("commit-fault"))
                    throw new InvalidOperationException("C1156 injected commit fault");
                return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
            }
        }
    }
}
