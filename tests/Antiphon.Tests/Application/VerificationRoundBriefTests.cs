using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-8 / G-68. The current task profile rides every brief shape — fresh inline, warm
/// refocus, follow-up, and the spilled file behind a pointer (whose own header names the round) —
/// because a warm agent keeps whatever bundle it launched with.
/// </summary>
[Category("Unit")]
public sealed class VerificationRoundBriefTests
{
    private static readonly Guid Owner = Guid.Parse("c5440000-0000-0000-0000-0000000000aa");
    private static readonly Guid Baseline = Guid.Parse("c5440000-0000-0000-0000-0000000000bb");
    private const string BaselineSha = "0123456789abcdef0123456789abcdef01234567";

    private static readonly DelegationSettings Settings = new()
    {
        ReplyInlineMaxChars = 20_000,
        ReplyExcerptHeadChars = 6_000,
        ReplyExcerptTailChars = 6_000,
    };

    [Test]
    public void C544_ProfileInEveryBrief()
    {
        var dir = Directory.CreateTempSubdirectory("antiphon-c544-brief").FullName;
        try
        {
            var rows = new List<(string Row, AgentTask Task, string[] Expected, string[] Absent)>
            {
                ("final-code", Task(AgentTaskRole.Code, WorkspaceMode.Worktree, VerificationRound.Final, dir),
                    ["--- verification profile ---", "round: Final (profile v1)",
                     "ordinary scope: the whole Unit lane, every named full affected integration class, every ordinary V-n/R-n and required manual acceptance in the plan.",
                     "final-review: none pending from this task"],
                    ["round: Interim", "final-review: PENDING"]),
                ("final-review", Task(AgentTaskRole.Review, WorkspaceMode.ReadOnly, VerificationRound.Final, dir),
                    ["round: Final (profile v1)", "including every row an earlier Interim round deferred",
                     "review evidence: add `ordinaryScopeCompleted: Full|Interim|None` once"],
                    ["round: Interim"]),
                ("interim-code", Task(AgentTaskRole.Code, WorkspaceMode.Worktree, VerificationRound.Interim, dir),
                    ["round: Interim (profile v1); this round can never approve a land",
                     $"subject (original landing owner): {Owner:D}",
                     $"baseline outcome: {Baseline:D} at reviewed SHA {BaselineSha}",
                     $"selection: docs/plans/c544.md@{BaselineSha} section \"Round selection\"",
                     "ordinary scope: cumulative new/changed cases since the baseline (earlier repair cases included) + tests for every unresolved finding + the named adjacent smoke in the selection",
                     "final-review: PENDING - a Final Review of the original owner must rerun the full ordinary scope before land."],
                    ["round: Final"]),
                ("interim-review", Task(AgentTaskRole.Review, WorkspaceMode.ReadOnly, VerificationRound.Interim, dir),
                    ["round: Interim (profile v1)", "final-review: PENDING", $"baseline outcome: {Baseline:D}"],
                    ["round: Final"]),
            };

            var legacy = Task(AgentTaskRole.Code, WorkspaceMode.Worktree, VerificationRound.Final, dir);
            legacy.VerificationProfileVersion = null;
            legacy.VerificationRound = null;
            DelegationReportFormatter.BuildBrief(legacy, Settings).ShouldNotContain("--- verification profile ---",
                customMessage: "legacy tasks carry no profile, never an implied Full");

            foreach (var (row, task, expected, absent) in rows)
            {
                var fresh = DelegationReportFormatter.BuildBrief(task, Settings);
                var warm = DelegationReportFormatter.BuildBrief(task, Settings, refocus: true);
                var followUp = Clone(task);
                followUp.FollowUpOfTaskId = Guid.NewGuid();
                var follow = DelegationReportFormatter.BuildBrief(followUp, Settings);

                var spilledTask = Clone(task);
                spilledTask.Goal = string.Join(" ", Enumerable.Range(0, 900).Select(i => $"context{i:D3}"));
                var pointer = AgentTaskDispatcher.FitBriefForTyping(spilledTask, Settings);
                pointer.ShouldContain("YOUR BRIEF IS NOT IN THIS MESSAGE", customMessage: $"{row}: spill expected");
                pointer.ShouldContain($" verification={task.VerificationRound}", customMessage: $"{row}: pointer names the round");
                if (task.VerificationRound == VerificationRound.Interim)
                    pointer.ShouldContain($"baseline={Baseline:N} final-review=pending", customMessage: $"{row}: pointer obligation");
                var spill = File.ReadAllText(Path.Combine(dir, ".antiphon",
                    $"task-{DelegationReportFormatter.Short(spilledTask.Id)}-brief.md"));

                foreach (var (shape, text) in new[] { ("fresh", fresh), ("warm", warm), ("follow-up", follow), ("spill", spill) })
                {
                    foreach (var line in expected)
                        text.ShouldContain(line, customMessage: $"{row}/{shape}: {line}");
                    foreach (var line in absent)
                        text.ShouldNotContain(line, customMessage: $"{row}/{shape} must not say: {line}");
                    text.ShouldContain("PCs: every PC stays pending for method-scoped SourceLanding Mutation",
                        customMessage: $"{row}/{shape}: PC obligation");
                }
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
        }
    }

    private static AgentTask Task(AgentTaskRole role, WorkspaceMode workspace, VerificationRound round, string dir)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = $"C544 {role} {round}",
            Goal = "Repair the parser.",
            Kind = AgentTaskKind.Worker,
            Role = role,
            Stage = role == AgentTaskRole.Review ? OrchestrationStage.Review : null,
            ModelLevel = AgentModelLevel.High,
            Workspace = workspace,
            WorkingDirectory = dir,
            VerificationProfileVersion = 1,
            VerificationRound = round,
        };
        if (round == VerificationRound.Interim)
        {
            task.VerificationSubjectTaskId = Owner;
            task.VerificationBaselineOutcomeId = Baseline;
            task.VerificationAdmissionJson = new VerificationAdmission(1, VerificationRound.Interim, Owner, Baseline, BaselineSha,
                Guid.NewGuid(), 3, CardVerificationPolicy.AllowInterim,
                new VerificationSelectionReference("docs/plans/c544.md", BaselineSha, "Round selection"),
                new InterimReadinessSnapshot("docs/investigations/q.md", BaselineSha, "p", "s", "run", "job", ["r1"],
                    new DateTime(2026, 9, 17, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 17, 8, 5, 0, DateTimeKind.Utc)),
                new DateTime(2026, 9, 17, 8, 5, 0, DateTimeKind.Utc)).Serialize();
        }
        return task;
    }

    private static AgentTask Clone(AgentTask source)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id, RootTaskId = id, Title = source.Title, Goal = source.Goal, Kind = source.Kind, Role = source.Role,
            Stage = source.Stage, ModelLevel = source.ModelLevel, Workspace = source.Workspace,
            WorkingDirectory = source.WorkingDirectory, VerificationProfileVersion = source.VerificationProfileVersion,
            VerificationRound = source.VerificationRound, VerificationSubjectTaskId = source.VerificationSubjectTaskId,
            VerificationBaselineOutcomeId = source.VerificationBaselineOutcomeId,
            VerificationAdmissionJson = source.VerificationAdmissionJson,
        };
    }
}
