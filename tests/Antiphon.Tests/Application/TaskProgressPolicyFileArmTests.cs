using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0153 S2 — the workspace arm of <see cref="TaskProgressPolicy"/>. The arm can only ever
/// withhold a stall; a colleague's edits on a shared checkout make the detector quieter, never
/// louder.
/// </summary>
[Category("Integration")]
public class TaskProgressPolicyFileArmTests
{
    [Test]
    public async Task Loop_plus_a_recent_file_change_is_not_a_stall()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedLoopAsync();
        var arm = new TaskProgressPolicy.WorkspaceArm(
            true, LastFileChangeAt: DateTime.UtcNow.AddMinutes(-3), LastCommitAt: null, SharedCheckout: false);

        (await scenario.EvaluateAsync(task, arm)).ShouldBeNull();
    }

    [Test]
    public async Task Loop_plus_a_recent_commit_is_not_a_stall()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedLoopAsync();
        var arm = new TaskProgressPolicy.WorkspaceArm(
            true, LastFileChangeAt: null, LastCommitAt: DateTime.UtcNow.AddMinutes(-3), SharedCheckout: false);

        (await scenario.EvaluateAsync(task, arm)).ShouldBeNull();
    }

    [Test]
    public async Task A_file_changed_before_the_look_back_does_not_save_the_loop()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedLoopAsync();
        var arm = new TaskProgressPolicy.WorkspaceArm(
            true, LastFileChangeAt: DateTime.UtcNow.AddMinutes(-50), LastCommitAt: null, SharedCheckout: false);

        var verdict = await scenario.EvaluateAsync(task, arm);
        verdict.ShouldNotBeNull();
        verdict.Summary.ShouldContain("last file change");
        verdict.Summary.ShouldContain("ago");
    }

    [Test]
    public async Task No_workspace_arm_leaves_the_transcript_verdict_standing()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedLoopAsync();

        var missing = await scenario.EvaluateAsync(
            task, new TaskProgressPolicy.WorkspaceArm(false, null, null, false));
        missing.ShouldNotBeNull();
        missing.Summary.ShouldContain("no workspace arm");

        var none = await scenario.EvaluateAsync(task, workspace: null);
        none.ShouldNotBeNull();
        none.Summary.ShouldContain("no workspace arm");
    }

    [Test]
    public async Task A_shared_checkout_is_flagged_and_can_only_withhold()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedLoopAsync();
        var quiet = new TaskProgressPolicy.WorkspaceArm(
            true, LastFileChangeAt: DateTime.UtcNow.AddMinutes(-50), LastCommitAt: DateTime.UtcNow.AddMinutes(-80),
            SharedCheckout: true);
        var busy = quiet with { LastFileChangeAt = DateTime.UtcNow.AddMinutes(-3) };

        var stalled = await scenario.EvaluateAsync(task, quiet);
        stalled.ShouldNotBeNull("the arm did not create a stall; the transcript did");
        stalled.Summary.ShouldContain("shared checkout");

        (await scenario.EvaluateAsync(task, busy)).ShouldBeNull(
            "a colleague's edits make the detector quieter, never louder");
    }

    [Test]
    public async Task ProbeProgressAsync_reads_a_real_git_worktree()
    {
        using var repo = new ScratchGitRepo("card0153-file-arm");
        await repo.CommitFileAsync("README.md", "base\n");
        var dispatched = DateTime.UtcNow.AddMinutes(-40);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "fresh.cs"), "new\n");

        var files = new AgentFilesService(
            new AppDbContext(TestDbFixture.CreateDbContextOptions()),
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
            new AgentReviewCheckpointService(
                new AppDbContext(TestDbFixture.CreateDbContextOptions()),
                new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
                NullLogger<AgentReviewCheckpointService>.Instance),
            NullLogger<AgentFilesService>.Instance);

        var arm = await files.ProbeProgressAsync(repo.Path, dispatched, sharedCheckout: false, CancellationToken.None);
        arm.Available.ShouldBeTrue();
        arm.LastFileChangeAt.ShouldNotBeNull();
        arm.LastFileChangeAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-5));
        arm.LastCommitAt.ShouldNotBeNull("the baseline commit is newer than a 40-minute-ago dispatch");
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly Guid _sessionId = Guid.NewGuid();
        private readonly List<Guid> _tasks = [];
        private long _seq;

        public async Task<AgentTask> SeedLoopAsync()
        {
            var dispatched = DateTime.UtcNow.AddMinutes(-50);
            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = _sessionId,
                DefinitionName = "file-arm-test",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = dispatched,
                StartedAt = dispatched,
                LastSeenAt = DateTime.UtcNow,
            });
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "file arm test",
                Goal = "loop",
                Role = AgentTaskRole.Code,
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

            for (var i = 0; i < 14; i++)
            {
                var ago = 42 - i;
                var kind = i % 3 == 0 ? TranscriptKinds.ToolCall
                    : i % 3 == 1 ? TranscriptKinds.ToolResult
                    : TranscriptKinds.Thinking;
                var at = DateTime.UtcNow.AddMinutes(-ago);
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = _sessionId,
                    Sequence = ++_seq,
                    Kind = kind,
                    Uuid = $"filearm-{Guid.NewGuid():N}",
                    ToolName = kind == TranscriptKinds.ToolCall ? "Read" : null,
                    ToolInput = kind == TranscriptKinds.ToolCall ? "{\"path\":\"src/loop.cs\"}" : null,
                    Text = kind == TranscriptKinds.ToolResult ? "file contents of loop.cs"
                        : kind == TranscriptKinds.Thinking ? $"thinking {i}" : null,
                    Timestamp = at,
                    CreatedAt = at,
                });
            }
            await db.SaveChangesAsync();
            return task;
        }

        public async Task<TaskProgressPolicy.Verdict?> EvaluateAsync(
            AgentTask task, TaskProgressPolicy.WorkspaceArm? workspace)
        {
            await using var db = CreateContext();
            return await TaskProgressPolicy.EvaluateAsync(
                db, task, DateTime.UtcNow, new DelegationSettings(), CancellationToken.None, workspace);
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.TranscriptEntries.Where(e => e.AgentSessionId == _sessionId).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == _sessionId).ExecuteDeleteAsync();
        }

        private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());
    }
}
