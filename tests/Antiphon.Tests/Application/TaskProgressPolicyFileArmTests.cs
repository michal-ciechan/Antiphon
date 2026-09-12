using Antiphon.Server.Application.Dtos;
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
        var arm = new WorkspaceProgressArm(
            true, LastFileChangeAt: DateTime.UtcNow.AddMinutes(-3), LastCommitAt: null, SharedCheckout: false);

        (await scenario.EvaluateAsync(task, arm)).ShouldBeNull();
    }

    [Test]
    public async Task Loop_plus_a_recent_commit_is_not_a_stall()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedLoopAsync();
        var arm = new WorkspaceProgressArm(
            true, LastFileChangeAt: null, LastCommitAt: DateTime.UtcNow.AddMinutes(-3), SharedCheckout: false);

        (await scenario.EvaluateAsync(task, arm)).ShouldBeNull();
    }

    [Test]
    public async Task A_file_changed_before_the_look_back_does_not_save_the_loop()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedLoopAsync();
        var arm = new WorkspaceProgressArm(
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
            task, new WorkspaceProgressArm(false, null, null, false));
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
        var quiet = new WorkspaceProgressArm(
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

    [Test]
    public async Task C499_R21_AFailedSubProbeMarksTheArmUnavailableAndKeepsPositives()
    {
        using var repo = new ScratchGitRepo("card0499-r21");
        await repo.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "fresh.cs"), "new\n");
        var gitdir = Path.Combine(repo.Path, ".git");
        var broken = Directory.CreateTempSubdirectory("card0499-r21-broken").FullName;
        await File.WriteAllTextAsync(Path.Combine(broken, ".git"), "gitdir: " + Path.Combine(broken, "missing.git") + "\n");

        var files = CreateFiles();
        var statusFail = await files.ProbeProgressAsync(broken, DateTime.UtcNow.AddMinutes(-40), false, CancellationToken.None);
        statusFail.Available.ShouldBeFalse();

        var ok = await files.ProbeProgressAsync(repo.Path, DateTime.UtcNow.AddMinutes(-40), false, CancellationToken.None);
        ok.Available.ShouldBeTrue();
        ok.LastFileChangeAt.ShouldNotBeNull();

        var stubStatusFail = new AgentFilesService(
            new AppDbContext(TestDbFixture.CreateDbContextOptions()),
            new FailingGitWorkspace(statusFails: true, logFails: false),
            new AgentReviewCheckpointService(
                new AppDbContext(TestDbFixture.CreateDbContextOptions()),
                new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
                NullLogger<AgentReviewCheckpointService>.Instance),
            NullLogger<AgentFilesService>.Instance);
        var armStatus = await stubStatusFail.ProbeProgressAsync(repo.Path, DateTime.UtcNow.AddMinutes(-40), false, CancellationToken.None);
        armStatus.Available.ShouldBeFalse();

        var stubLogFail = new AgentFilesService(
            new AppDbContext(TestDbFixture.CreateDbContextOptions()),
            new FailingGitWorkspace(statusFails: false, logFails: true),
            new AgentReviewCheckpointService(
                new AppDbContext(TestDbFixture.CreateDbContextOptions()),
                new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
                NullLogger<AgentReviewCheckpointService>.Instance),
            NullLogger<AgentFilesService>.Instance);
        var armLog = await stubLogFail.ProbeProgressAsync(repo.Path, DateTime.UtcNow.AddMinutes(-40), false, CancellationToken.None);
        armLog.Available.ShouldBeFalse();
        armLog.LastFileChangeAt.ShouldNotBeNull();
        Directory.Delete(broken, true);
    }

    [Test]
    public async Task C499_R22_APartialPositiveArmStillWithholdsTheStall()
    {
        await using var scenario = new Scenario();
        var task = await scenario.SeedLoopAsync();
        var arm = new WorkspaceProgressArm(
            false, LastFileChangeAt: DateTime.UtcNow.AddMinutes(-3), LastCommitAt: null, SharedCheckout: false);
        (await scenario.EvaluateAsync(task, arm)).ShouldBeNull();
    }

    private static AgentFilesService CreateFiles() =>
        new(
            new AppDbContext(TestDbFixture.CreateDbContextOptions()),
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
            new AgentReviewCheckpointService(
                new AppDbContext(TestDbFixture.CreateDbContextOptions()),
                new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
                NullLogger<AgentReviewCheckpointService>.Instance),
            NullLogger<AgentFilesService>.Instance);

    private sealed class FailingGitWorkspace(bool statusFails, bool logFails) : GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance)
    {
        public override async Task<GitWorkspaceService.GitStrictList<GitChange>> TryGetChangesAsync(string workingDirectory, CancellationToken ct)
            => statusFails
                ? new(false, [], 128)
                : await base.TryGetChangesAsync(workingDirectory, ct);

        public override async Task<GitWorkspaceService.GitStrictList<GitCommit>> TryGetRecentCommitsAsync(string workingDirectory, int limit, CancellationToken ct)
            => logFails
                ? new(false, [], 128)
                : await base.TryGetRecentCommitsAsync(workingDirectory, limit, ct);
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
            AgentTask task, WorkspaceProgressArm? workspace)
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
