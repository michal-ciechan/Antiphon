using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.Application;

public partial class DelegationDispatchRecoveryBoundaryTests
{
    private const string EligibilityChanged = "eligibility-changed-before-later-save";

    [Test]
    [Arguments("user")]
    [Arguments("queued-command")]
    [Arguments("legacy-file")]
    [Arguments("pending-brief")]
    public async Task C1149_Native_attempt_keeps_the_failure_path(string identity)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var projectsRoot = Directory.CreateTempSubdirectory("c1149-native-projects").FullName;
        try
        {
            var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
            {
                Parent = false,
                Brief = identity == "pending-brief",
            });
            var jsonl = WriteNativeAttempt(seeded, projectsRoot, identity);
            var runner = new CountingRunner();
            var stopper = new RecordingSessionStopper();
            await using (var host = OpenSweep(
                schema.ConnectionString, runner, stopper, new FakeTimeProvider(DateTimeOffset.UtcNow),
                new DeadSessionFirstSeenState(), projectsRoot: projectsRoot))
                await host.DueAsync();

            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
            task.Status.ShouldBe(AgentTaskStatus.Failed, identity);
            task.CompletedAt.ShouldNotBeNull(identity);
            task.Attempt.ShouldBe(seeded.Attempt, identity);
            task.AgentSessionId.ShouldBe(seeded.SessionId, identity);
            task.FailureReason.ShouldNotBeNull(identity);
            task.FailureReason!.Contains(SessionReconciliationService.RunnerUnknownSessionReason, StringComparison.Ordinal)
                .ShouldBeTrue(identity);
            task.FailureReason.Contains(jsonl, StringComparison.Ordinal).ShouldBeTrue(identity);
            task.FailureReason.Contains("carries the brief and no report", StringComparison.Ordinal).ShouldBeTrue(identity);
            task.FailureReason.StartsWith("dispatch_launch_absent", StringComparison.Ordinal).ShouldBeFalse(identity);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == seeded.TaskId && e.Type == AgentTaskEventType.Blocked))
                .ShouldBe(0, identity);
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == seeded.SessionId)).ShouldBe(0, identity);
            var briefs = await db.SessionQueuedMessages.Where(m => m.ExecutionTaskId == seeded.TaskId).ToListAsync();
            if (identity == "pending-brief")
            {
                var brief = briefs.ShouldHaveSingleItem(identity);
                brief.Id.ShouldBe(seeded.BriefId, identity);
                brief.Status.ShouldBe(QueuedMessageStatus.Pending, identity);
                System.Text.Encoding.UTF8.GetBytes(brief.Body).ShouldBe(seeded.Body, identity);
            }
            else
            {
                briefs.ShouldBeEmpty(identity);
            }

            Quiet(runner, stopper, identity);
        }
        finally
        {
            try { if (Directory.Exists(projectsRoot)) Directory.Delete(projectsRoot, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Test]
    public async Task C1149_Failed_hold_does_not_persist_on_a_later_save()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var victimId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var survivorId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var victim = await SeedAsync(schema.ConnectionString, new AbsentShape { Parent = true, TaskId = victimId });
        var survivor = await SeedAsync(schema.ConnectionString, new AbsentShape { Parent = true, TaskId = survivorId });
        var fault = new EligibilitySaveFault { Connection = schema.ConnectionString, VictimId = victimId };
        var runner = new CountingRunner();
        var stopper = new RecordingSessionStopper();
        await using (var host = OpenSweep(
            schema.ConnectionString, runner, stopper, new FakeTimeProvider(DateTimeOffset.UtcNow),
            new DeadSessionFirstSeenState(), fault))
            await host.DueAsync();

        fault.Fired.ShouldBeTrue("D2");
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var untouched = await db.AgentTasks.SingleAsync(t => t.Id == victim.TaskId);
        untouched.Status.ShouldBe(AgentTaskStatus.Dispatched, "D2-victim");
        untouched.FailureReason.ShouldBeNull("D2-victim");
        untouched.Attempt.ShouldBe(victim.Attempt, "D2-victim");
        untouched.CompletedAt.ShouldBeNull("D2-victim");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == victim.TaskId && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(0, "D2-victim");
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == victim.ParentId)).ShouldBe(0, "D2-victim");
        var victimBrief = await db.SessionQueuedMessages.SingleAsync(m => m.Id == victim.BriefId);
        System.Text.Encoding.UTF8.GetBytes(victimBrief.Body).ShouldBe(victim.Body, "D2-victim");
        (await db.AgentSessions.SingleAsync(s => s.Id == victim.SessionId)).FailureReason
            .ShouldBe(EligibilityChanged, "D2-victim");

        var held = await db.AgentTasks.SingleAsync(t => t.Id == survivor.TaskId);
        held.Status.ShouldBe(AgentTaskStatus.Blocked, "D2-survivor");
        held.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason, "D2-survivor");
        held.Attempt.ShouldBe(survivor.Attempt, "D2-survivor");
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == survivor.TaskId && e.Type == AgentTaskEventType.Blocked))
            .ShouldBe(1, "D2-survivor");
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == survivor.TaskId)).ShouldBe(1, "D2-survivor");
        var survivorBrief = await db.SessionQueuedMessages.SingleAsync(m => m.Id == survivor.BriefId);
        System.Text.Encoding.UTF8.GetBytes(survivorBrief.Body).ShouldBe(survivor.Body, "D2-survivor");
        (await db.AgentSessions.SingleAsync(s => s.Id == survivor.SessionId)).FailureReason
            .ShouldBe(SessionReconciliationService.RunnerUnknownSessionReason, "D2-survivor");
        Quiet(runner, stopper, "D2");
    }

    [Test]
    [Arguments("eligible")]
    [Arguments("busy")]
    [Arguments("crash")]
    public async Task C1149_Caller_note_has_one_complete_user_prompt(string mode)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        // The sweep clock jumps the dead-session grace. Delivery confirmation waits on
        // TimeProvider.Delay, which a frozen clock never releases, so the caller harness
        // keeps the system clock. The note is written about two minutes before that clock.
        var sweepClock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-5));
        var parent = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
            ConnectionString = schema.ConnectionString,
        });
        var decoy = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
            ConnectionString = schema.ConnectionString,
        });
        try
        {
            var seeded = await SeedAsync(schema.ConnectionString, new AbsentShape
            {
                ExistingParentId = parent.SessionId,
            });
            var runner = new CountingRunner();
            var stopper = new RecordingSessionStopper();
            await using (var host = OpenSweep(
                schema.ConnectionString, runner, stopper, sweepClock, new DeadSessionFirstSeenState()))
                await host.DueAsync();

            SessionQueuedMessage note;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                var held = await db.AgentTasks.SingleAsync(t => t.Id == seeded.TaskId);
                held.Status.ShouldBe(AgentTaskStatus.Blocked, mode);
                held.FailureReason.ShouldBe(AgentTaskDispatcher.DispatchLaunchAbsentReason, mode);
                note = (await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.SourceTaskId == seeded.TaskId).ToListAsync()).ShouldHaveSingleItem(mode);
                note.AgentSessionId.ShouldBe(parent.SessionId, mode);
                note.ExecutionTaskId.ShouldBeNull(mode);
            }

            var floor = await PromptFloorAsync(schema.ConnectionString, parent.SessionId);
            if (mode == "crash")
            {
                var sessionId = parent.SessionId;
                var agentId = parent.AgentId;
                await parent.DisposeAsync();
                parent = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
                {
                    AlwaysOn = true,
                    PreserveDatabaseOnDispose = true,
                    ConnectionString = schema.ConnectionString,
                    AttachSessionId = sessionId,
                    AttachAgentId = agentId,
                });
            }

            if (mode == "busy")
            {
                await parent.MarkWorkingAsync();
                await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
                parent.Adapter.SubmittedBodies.ShouldBeEmpty(mode);
                (await PromptsAfterAsync(schema.ConnectionString, parent.SessionId, floor)).ShouldBeEmpty(mode);
                await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
                await parent.Queue.OnTurnEndAsync(parent.SessionId, CancellationToken.None);
            }
            else
            {
                await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            }

            await AssertOneReceiptAsync(schema.ConnectionString, note, parent.SessionId, floor, mode);
            parent.Adapter.SubmittedBodies.Count.ShouldBe(1, mode);
            await parent.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
            await AssertOneReceiptAsync(schema.ConnectionString, note, parent.SessionId, floor, mode + "-once");
            parent.Adapter.SubmittedBodies.Count.ShouldBe(1, mode + "-once");
            (await PromptsAfterAsync(schema.ConnectionString, decoy.SessionId, 0)).ShouldBeEmpty(mode);
            decoy.Adapter.SubmittedBodies.ShouldBeEmpty(mode);

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var brief = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == seeded.BriefId);
            brief.Status.ShouldBe(QueuedMessageStatus.Pending, mode);
            System.Text.Encoding.UTF8.GetBytes(brief.Body).ShouldBe(seeded.Body, mode);
            System.Text.Encoding.UTF8.GetBytes(brief.RemoteSpillBody!).ShouldBe(seeded.Spill, mode);
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == seeded.TaskId)).ShouldBe(1, mode);
            Quiet(runner, stopper, mode);
        }
        finally
        {
            await parent.DisposeAsync();
            await decoy.DisposeAsync();
        }
    }

    private static string WriteNativeAttempt(SeededAbsent seeded, string projectsRoot, string identity)
    {
        var encoded = DelegateBindRefusalRecovery.EncodeClaudeProjectDir(seeded.Cwd);
        var projectDir = Path.Combine(projectsRoot, encoded);
        Directory.CreateDirectory(projectDir);
        var fileName = identity == "legacy-file"
            ? "retained-legacy-brief.jsonl"
            : seeded.SessionId.ToString("D") + ".jsonl";
        var path = Path.Combine(projectDir, fileName);
        var marker = DelegationReportFormatter.TaskMarker(seeded.TaskId) + " the brief";
        var stamp = new DateTimeOffset(DateTime.SpecifyKind(seeded.StartedAt, DateTimeKind.Utc)).ToString("o");
        object record = identity == "queued-command"
            ? new
            {
                type = "attachment",
                uuid = Guid.NewGuid().ToString("D"),
                cwd = seeded.Cwd,
                timestamp = stamp,
                attachment = new { type = "queued_command", prompt = marker },
            }
            : new
            {
                type = "user",
                uuid = Guid.NewGuid().ToString("D"),
                cwd = seeded.Cwd,
                timestamp = stamp,
                message = new { role = "user", content = marker },
            };
        File.WriteAllText(path, JsonSerializer.Serialize(record) + "\n");
        return path;
    }

    private static async Task<long> PromptFloorAsync(string connection, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        return await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).MaxAsync(t => (long?)t.Sequence) ?? 0;
    }

    private static async Task<List<TranscriptEntry>> PromptsAfterAsync(string connection, Guid sessionId, long floor)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        return await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt && t.Sequence > floor)
            .ToListAsync();
    }

    private static async Task AssertOneReceiptAsync(
        string connection, SessionQueuedMessage note, Guid sessionId, long floor, string label)
    {
        var prompts = await PromptsAfterAsync(connection, sessionId, floor);
        var receipt = prompts.Where(x => x.Text is not null && PromptSubmissionMatch.IsCompleteIn(note.Body, x.Text))
            .ShouldHaveSingleItem(label);
        PromptSubmissionMatch.Normalize(receipt.Text!).ShouldBe(PromptSubmissionMatch.Normalize(note.Body), label);
        receipt.AgentSessionId.ShouldBe(note.AgentSessionId, label);
        prompts.Count.ShouldBe(1, label);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == note.Id);
        queued.SourceTaskId.ShouldBe(note.SourceTaskId, label);
        queued.DeliveryVerdict.ShouldBeOneOf(DeliveryVerdict.Delivered, DeliveryVerdict.LateConfirmed);
    }

    private sealed class EligibilitySaveFault : SaveChangesInterceptor
    {
        public string Connection { get; init; } = "";
        public Guid VictimId { get; init; }
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context is AppDbContext db)
            {
                var victim = db.ChangeTracker.Entries<AgentTask>().FirstOrDefault(e =>
                    e.Entity.Id == VictimId
                    && e.State == EntityState.Modified
                    && e.Entity.Status == AgentTaskStatus.Blocked);
                if (victim is not null && victim.Entity.AgentSessionId is Guid sessionId)
                {
                    Fired = true;
                    var reason = EligibilityChanged;
                    await using var side = new AppDbContext(TestDbFixture.CreateDbContextOptions(Connection));
                    await side.Database.ExecuteSqlInterpolatedAsync(
                        $"""UPDATE "AgentSessions" SET "FailureReason" = {reason} WHERE "Id" = {sessionId}""",
                        cancellationToken);
                    throw new IOException("injected absent-launch save fault");
                }
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
