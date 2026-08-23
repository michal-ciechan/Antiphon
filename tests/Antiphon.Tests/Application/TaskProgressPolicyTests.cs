using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0153 S1 — the shared progress policy, tested on its own because both callers act on it:
/// the dispatcher's sweep RAISES on a verdict and the attention projection lists one. A
/// disagreement between the two would have no single place to fix.
///
/// <para>Every assertion is scoped to the session this test seeded (the shared-Postgres rule),
/// and the policy only ever reads rows for one session id, so no test here needs the sweep
/// suites' <c>NotInParallel</c>.</para>
/// </summary>
[Category("Integration")]
public class TaskProgressPolicyTests
{
    [Test]
    public async Task The_loop_shape_is_a_stall()
    {
        // The 2026-08-23 shape: 14 rows in 40 min alternating the same Read / the same result /
        // a fresh Think. This is the test that would have been red that morning.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 50);
        await scenario.SeedLoopAsync(rows: 14, spanMinutes: 40);

        var verdict = await scenario.EvaluateAsync(task);

        verdict.ShouldNotBeNull();
        verdict.DistinctFingerprints.ShouldBe(2);
        verdict.RowCount.ShouldBe(14);
        verdict.StalledFor.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
        verdict.LastNovelKind.ShouldBe(TranscriptKinds.ToolResult);
        verdict.FailureReason.ShouldContain("rows=14");
        verdict.FailureReason.ShouldContain("distinct=2");
    }

    [Test]
    public async Task A_slow_single_tool_call_is_not_a_stall()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 40);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.ToolCall, "Read", "{\"path\":\"slow.cs\"}", null, 35));

        var stall = await scenario.EvaluateAsync(task);
        stall.ShouldBeNull("MinRowsInWindow is 6; one ToolCall is a slow tool, not a loop");

        var deadline = await scenario.EvaluateDeadlineAsync(task, new DelegationSettings
        {
            LocalExecutionDeadlineMinutes = 20,
            ModelWaitDeadlineMinutes = 0,
            DefaultTimeoutMinutes = 240,
        });
        deadline.ShouldNotBeNull();
        deadline.Kind.ShouldBe(TaskDeadlinePolicy.DeadlineKind.LocalExecution);
        deadline.Breached.ShouldBeTrue("the phase deadline owns 'nothing landed'");
    }

    [Test]
    public async Task Distinct_tool_calls_are_progress_however_repetitive_the_tool_is()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 50);
        for (var i = 0; i < 14; i++)
        {
            await scenario.SeedEntriesAsync(
                (TranscriptKinds.ToolCall, "Edit", $"{{\"path\":\"f{i}.cs\",\"old\":\"a\",\"new\":\"b{i}\"}}", null, 40 - i));
        }

        (await scenario.EvaluateAsync(task)).ShouldBeNull();
    }

    [Test]
    public async Task Thinking_never_counts_as_progress()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 50);
        for (var i = 0; i < 20; i++)
        {
            await scenario.SeedEntriesAsync(
                (TranscriptKinds.Thinking, null, null, $"thinking thoughts number {i} are unique", 40 - i));
        }

        var verdict = await scenario.EvaluateAsync(task);
        verdict.ShouldNotBeNull("row count satisfies the gate; none of them is progress-bearing");
        verdict.DistinctFingerprints.ShouldBe(0);
        verdict.LastNovelKind.ShouldBeNull();
    }

    [Test]
    public async Task A_user_prompt_resets_the_clock()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 50);
        await scenario.SeedLoopAsync(rows: 14, spanMinutes: 40);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.UserPrompt, null, null, "continue", 5));

        (await scenario.EvaluateAsync(task)).ShouldBeNull();
    }

    [Test]
    public async Task Idle_is_out_of_scope()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 50);
        await scenario.SeedLoopAsync(rows: 14, spanMinutes: 40);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.TurnEnd, null, null, null, 3),
            (TranscriptKinds.UserPrompt, null, null,
                $"{TranscriptKinds.LocalCommandStdoutPrefix}status</local-command-stdout>", 1));

        (await scenario.EvaluateAsync(task)).ShouldBeNull(
            "IsWorkingAsync is the shared one: a <local-command-stdout> tail is not activity");
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            (await SessionMessageQueueService.IsWorkingAsync(
                    db, scenario.SessionId, CancellationToken.None))
                .ShouldBeFalse("a naive last-kind rule would mis-read the local-command as working");
        }
    }

    [Test]
    public async Task Warm_pool_tail_is_not_this_task()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 40);
        await scenario.SeedLoopAsync(rows: 14, spanMinutes: 40, endMinutesAgo: 41);

        (await scenario.EvaluateAsync(task)).ShouldBeNull(
            "rows before DispatchedAt are the previous occupant's; this task has none of its own");
    }

    [Test]
    public async Task Timestamp_tie_break_matches_LoadLastEntryAsync()
    {
        // Highest Sequence is a backfilled OLD row (CARD-0055 0.27% shape). Time order sees the
        // first occurrence at T-40 and stalls; sequence order would see the first at T-5 and
        // withhold.
        await using var scenario = new Scenario();
        var task = await scenario.SeedTaskAsync(dispatchedMinutesAgo: 50);
        var now = DateTime.UtcNow;
        for (var i = 0; i < 8; i++)
        {
            await scenario.SeedEntryAtAsync(
                TranscriptKinds.ToolCall, "Read", "{\"path\":\"loop.cs\"}", null,
                at: now.AddMinutes(-(5 - i * 0.4)), sequence: i + 1);
        }
        await scenario.SeedEntryAtAsync(
            TranscriptKinds.ToolCall, "Read", "{\"path\":\"loop.cs\"}", null,
            at: now.AddMinutes(-40), sequence: 99);

        var verdict = await scenario.EvaluateAsync(task);
        verdict.ShouldNotBeNull("the first occurrence is 40 minutes old by Timestamp, not by Sequence");
        verdict.StalledFor.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
    }

    [Test]
    public void Shipped_defaults_are_the_measured_ones()
    {
        var stall = new DelegationSettings().StallDetection;
        stall.Enabled.ShouldBeTrue();
        stall.StallMinutes.ShouldBe(30);
        stall.LookBackMinutes.ShouldBe(45);
        stall.MinRowsInWindow.ShouldBe(6);
        stall.EscalateToErrorAfterMinutes.ShouldBe(90);
    }

    /// <summary>
    /// CARD-0158 V3 — the investigation's central negative finding as executable documentation.
    /// Re-keying AutoEscalateStalledAsync onto TaskProgressPolicy would have made the only two
    /// historical escalations (both idle-after-TurnEnd, working=false) impossible. Anyone
    /// proposing that re-key trips over this comment first.
    /// </summary>
    [Test]
    public async Task The_fingerprint_detector_declines_the_2026_08_11_shape_by_design()
    {
        var (task, _) = await EscalateClockHistoricalFixture.Seed_9775fe45Async(
            status: AgentTaskStatus.Working);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var verdict = await TaskProgressPolicy.EvaluateAsync(
            db, task, DateTime.UtcNow, new DelegationSettings(), CancellationToken.None);

        verdict.ShouldBeNull(
            "working=false after TurnEnd: the fingerprint detector declines idle-after-done by design");
        await EscalateClockHistoricalFixture.RetireAsync(task.Id);
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly Guid _sessionId = Guid.NewGuid();
        private readonly List<Guid> _tasks = [];
        private long _seq;

        public Guid SessionId => _sessionId;

        public async Task<AgentTask> SeedTaskAsync(
            int dispatchedMinutesAgo, AgentTaskRole role = AgentTaskRole.Code)
        {
            var dispatched = DateTime.UtcNow.AddMinutes(-dispatchedMinutesAgo);
            await using var db = CreateContext();
            if (!await db.AgentSessions.AnyAsync(s => s.Id == _sessionId))
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = _sessionId,
                    DefinitionName = "progress-policy-test",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running,
                    Cwd = Path.GetTempPath(),
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = dispatched,
                    StartedAt = dispatched,
                    LastSeenAt = DateTime.UtcNow,
                });
            }

            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "progress policy test",
                Goal = "loop or work",
                Role = role,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                AgentSessionId = _sessionId,
                Status = AgentTaskStatus.Working,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            _tasks.Add(id);
            return task;
        }

        public async Task SeedLoopAsync(int rows, int spanMinutes, int endMinutesAgo = 2)
        {
            var startMinutesAgo = endMinutesAgo + spanMinutes;
            var step = spanMinutes / Math.Max(rows - 1, 1.0);
            for (var i = 0; i < rows; i++)
            {
                var ago = startMinutesAgo - (int)Math.Round(i * step);
                var kind = i % 3 == 0 ? TranscriptKinds.ToolCall
                    : i % 3 == 1 ? TranscriptKinds.ToolResult
                    : TranscriptKinds.Thinking;
                await SeedEntriesAsync(kind switch
                {
                    TranscriptKinds.ToolCall => (kind, "Read", "{\"path\":\"src/loop.cs\"}", null, ago),
                    TranscriptKinds.ToolResult => (kind, null, null, "file contents of loop.cs", ago),
                    _ => (kind, null, null, $"thinking pass {i}", ago),
                });
            }
        }

        public async Task SeedEntriesAsync(
            params (string Kind, string? ToolName, string? ToolInput, string? Text, int MinutesAgo)[] entries)
        {
            await using var db = CreateContext();
            foreach (var (kind, toolName, toolInput, text, minutesAgo) in entries)
            {
                var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
                db.TranscriptEntries.Add(Entry(kind, toolName, toolInput, text, at, ++_seq));
            }
            await db.SaveChangesAsync();
        }

        public async Task SeedEntryAtAsync(
            string kind, string? toolName, string? toolInput, string? text, DateTime at, long sequence)
        {
            await using var db = CreateContext();
            db.TranscriptEntries.Add(Entry(kind, toolName, toolInput, text, at, sequence));
            if (sequence > _seq) _seq = sequence;
            await db.SaveChangesAsync();
        }

        public async Task<TaskProgressPolicy.Verdict?> EvaluateAsync(
            AgentTask task, DelegationSettings? settings = null)
        {
            await using var db = CreateContext();
            return await TaskProgressPolicy.EvaluateAsync(
                db, task, DateTime.UtcNow, settings ?? new DelegationSettings(), CancellationToken.None);
        }

        public async Task<TaskDeadlinePolicy.Verdict?> EvaluateDeadlineAsync(
            AgentTask task, DelegationSettings settings)
        {
            await using var db = CreateContext();
            return await TaskDeadlinePolicy.EvaluateAsync(
                db, task, DateTime.UtcNow, settings, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.TranscriptEntries.Where(e => e.AgentSessionId == _sessionId).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == _sessionId).ExecuteDeleteAsync();
        }

        private TranscriptEntry Entry(
            string kind, string? toolName, string? toolInput, string? text, DateTime at, long sequence) => new()
        {
            Id = Guid.NewGuid(),
            AgentSessionId = _sessionId,
            Sequence = sequence,
            Kind = kind,
            Uuid = $"progress-{Guid.NewGuid():N}",
            Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
            Text = text,
            ToolName = toolName,
            ToolInput = toolInput,
            Timestamp = at,
            CreatedAt = at,
            StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
        };

        private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
    }
}
