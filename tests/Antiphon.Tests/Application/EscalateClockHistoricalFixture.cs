using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.Application;

/// <summary>
/// Relative timelines of the two real 2026-08-11 Debug auto-escalations (CARD-0158). Absolute
/// source facts live in the comments; tests replay evidence rather than inventing shapes.
///
/// Both were idle-after-a-completed-turn: a genuine TurnEnd with a real completion message, then
/// ~25 minutes of silence, then AutoEscalateStalledAsync bumped them to Frontier. Neither prompt
/// carried the task marker (why settlement never collected them). CARD-0153's fingerprint
/// detector would have caught neither (<c>working=false</c> after the TurnEnd).
/// </summary>
internal static class EscalateClockHistoricalFixture
{
    /// <summary>
    /// Task <c>2c40e79f</c> / session <c>1b78bec1</c>.
    /// TurnEnd seq 179 at 08:47:31Z → escalation 09:12:35Z (quiet 25.07 min).
    /// Completion text: "Done. CARD-0006 did not cause this…" after commit+push.
    /// ~195 rows over 44.9 min; max gap 25.07 only at the end.
    /// </summary>
    internal static Task<(AgentTask Task, Guid SessionId)> Seed_2c40e79fAsync(
        AgentTaskStatus status = AgentTaskStatus.Dispatched,
        double quietAfterTurnEndMinutes = 25.5,
        double runMinutesBeforeTurnEnd = 44.9)
        => SeedAsync(
            status,
            quietAfterTurnEndMinutes,
            runMinutesBeforeTurnEnd,
            completionText: "Done. CARD-0006 did not cause this — the bind was a stranger's conversation.",
            unmarkedBrief: "Investigate why the transcript bound a stranger's conversation.");

    /// <summary>
    /// Task <c>9775fe45</c> / session <c>d681178e</c>.
    /// TurnEnd seq 238 at 08:50:53Z → escalation 09:15:55Z (quiet 25.03 min).
    /// Completion text: "Root cause found, fixed, and committed…"
    /// 59.9-min run.
    /// </summary>
    internal static Task<(AgentTask Task, Guid SessionId)> Seed_9775fe45Async(
        AgentTaskStatus status = AgentTaskStatus.Dispatched,
        double quietAfterTurnEndMinutes = 25.5,
        double runMinutesBeforeTurnEnd = 59.9)
        => SeedAsync(
            status,
            quietAfterTurnEndMinutes,
            runMinutesBeforeTurnEnd,
            completionText: "Root cause found, fixed, and committed. The delivery path mangled the marker.",
            unmarkedBrief: "Find why the Debug task never settled after reporting.");

    private static async Task<(AgentTask Task, Guid SessionId)> SeedAsync(
        AgentTaskStatus status,
        double quietAfterTurnEndMinutes,
        double runMinutesBeforeTurnEnd,
        string completionText,
        string unmarkedBrief)
    {
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var turnEndAt = now.AddMinutes(-quietAfterTurnEndMinutes);
        var dispatched = turnEndAt.AddMinutes(-runMinutesBeforeTurnEnd);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = turnEndAt,
        });

        var task = new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = "CARD-0158 historical fixture",
            Goal = unmarkedBrief,
            Role = AgentTaskRole.Debug,
            ModelLevel = AgentModelLevel.High,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            AgentSessionId = sessionId,
            Status = status,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
        };
        db.AgentTasks.Add(task);

        // Dense-enough progress so a fingerprint detector has rows to look at — then a TurnEnd.
        // The prompt deliberately has NO task marker: that is why neither historical case settled.
        long seq = 0;
        void Add(string kind, string? text, DateTime at, string? toolName = null, string? toolInput = null)
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = ++seq,
                Kind = kind,
                Uuid = $"hist-{Guid.NewGuid():N}",
                Role = kind is TranscriptKinds.UserPrompt or TranscriptKinds.QueuedUserPrompt
                    ? "user" : "assistant",
                Text = text,
                ToolName = toolName,
                ToolInput = toolInput,
                Timestamp = at,
                CreatedAt = at,
                StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
            });
        }

        Add(TranscriptKinds.UserPrompt, unmarkedBrief, dispatched.AddMinutes(1));
        var progressStart = dispatched.AddMinutes(2);
        var progressSpan = turnEndAt - progressStart - TimeSpan.FromMinutes(1);
        for (var i = 0; i < 12; i++)
        {
            var at = progressStart + TimeSpan.FromTicks(progressSpan.Ticks * i / 11);
            if (i % 3 == 0)
                Add(TranscriptKinds.ToolCall, null, at, "Read", "{\"path\":\"src/loop.cs\"}");
            else if (i % 3 == 1)
                Add(TranscriptKinds.ToolResult, "file contents", at);
            else
                Add(TranscriptKinds.Thinking, $"narrowing pass {i}", at);
        }

        Add(TranscriptKinds.AssistantText, completionText, turnEndAt.AddSeconds(-2));
        Add(TranscriptKinds.TurnEnd, null, turnEndAt);

        // The mark OnTurnEndAsync leaves when the finished turn fails the marker gate.
        var agentName = $"hist-{Guid.NewGuid():N}"[..16];
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = agentName,
            Slug = agentName,
            WorkingDirectory = Path.GetTempPath(),
            Details = "CARD-0158 historical fixture delegate.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.High,
            IsPoolDelegate = true,
            CreatedAt = dispatched,
            UpdatedAt = turnEndAt,
        };
        db.Agents.Add(agent);
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agent.Id,
            SessionId = sessionId,
            Kind = AgentIncidentKind.DelegateReportUncorrelated,
            Severity = AlertSeverity.Warning,
            Message = "Report could not be correlated to the task.",
            CreatedAt = turnEndAt.AddSeconds(1),
        });

        await db.SaveChangesAsync();
        return (task, sessionId);
    }

    /// <summary>
    /// Park a seeded fixture row so a later AutoEscalateStalledAsync opt-in sweep cannot
    /// pick it up (shared-Postgres rule).
    /// </summary>
    internal static async Task RetireAsync(Guid taskId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var row = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        row.Status = AgentTaskStatus.Failed;
        row.CompletedAt = DateTime.UtcNow;
        row.FailureReason ??= "retired by CARD-0158 fixture cleanup (shared Postgres)";
        await db.SaveChangesAsync();
    }
}
