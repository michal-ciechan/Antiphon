using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    private static async Task SeedFileEditAsync(Guid sessionId, string toolName, string absolutePath, DateTime at)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;
        var pathKey = toolName == "NotebookEdit" ? "notebook_path" : "file_path";
        var entry = NewEntry(sessionId, seq + 1, TranscriptKinds.ToolCall, null);
        entry.Timestamp = at;
        entry.CreatedAt = at;
        entry.ToolName = toolName;
        entry.ToolUseId = $"toolu_{Guid.NewGuid():N}";
        entry.ToolInput = JsonSerializer.Serialize(new Dictionary<string, string> { [pathKey] = absolutePath });
        db.TranscriptEntries.Add(entry);
        await db.SaveChangesAsync();
    }

    private static TestScopeFactory C527Factory(
        string worktreeRoot,
        DelegationSettings? delegation = null,
        bool routingPins = false,
        RecordingGitWorkspaceService? gitSpy = null,
        SaveChangesInterceptor? saveInterceptor = null) =>
        new(
            worktreeRoot,
            supervision: new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings
                {
                    Enabled = true,
                    EvidenceTimeoutSeconds = 1,
                    PollIntervalMs = 50,
                    PostSubmitAdvanceTimeoutSeconds = 1,
                    TranscriptConfirmTimeoutSeconds = 3,
                    ReEnterIntervalSeconds = 1,
                },
            },
            delegation: delegation ?? new DelegationSettings { PtySingleChunkBytes = 43_200 },
            routingPins: routingPins,
            gitSpy: gitSpy,
            saveInterceptor: saveInterceptor);

    private static async Task<(ScratchGitRepo Repo, AgentTask Task, Guid SessionId, Guid Parent)> SeedC527Async(
        Action<AgentTask>? configure = null, string prefix = "c527")
    {
        var repo = new ScratchGitRepo(prefix);
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitignore"), "*.secret\n");
        await repo.CommitFileAsync("tracked.txt", "tracked\n");
        var parent = await SeedSessionAsync(repo.Path);
        await SeedParentHistoryAsync(parent);
        var (task, sessionId) = await SeedDispatchedTaskAsync(repo.Path, parent, t =>
        {
            t.RepoPath = repo.Path;
            t.Workspace = WorkspaceMode.Shared;
            t.Role = AgentTaskRole.Custom;
            t.AgentKind = AgentKind.ClaudeCode;
            t.DispatchedAt = DateTime.UtcNow.AddMinutes(-1);
            configure?.Invoke(t);
        });
        return (repo, task, sessionId, parent);
    }

    [Test]
    public async Task C527_tier1_commits_the_transcript_footprint_and_delivers_committed_to_the_parent()
    {
        var spy = new RecordingGitWorkspaceService();
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        var a = Path.Combine(repo.Path, "a.md");
        var b = Path.Combine(repo.Path, "b.md");
        await File.WriteAllTextAsync(a, "a");
        await File.WriteAllTextAsync(b, "b");
        await SeedFileEditAsync(seeded.SessionId, "Write", a, DateTime.UtcNow);
        await SeedFileEditAsync(seeded.SessionId, "Write", b, DateTime.UtcNow);
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        AttachTerminal(factory, seeded.Parent);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote a.md and b.md.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);

        var body = await repo.GitReadAsync("log", "-1", "--format=%B");
        body.ShouldContain($"antiphon-task: {seeded.Task.Id}");
        body.ShouldContain("antiphon-commit: gated");
        var names = (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        names.ShouldBe(["a.md", "b.md"], ignoreOrder: true);
        (await repo.GitReadAsync("status", "--porcelain")).Trim().ShouldBeEmpty();
        await using var verify = CreateContext();
        var committed = await verify.AgentTaskEvents.SingleAsync(e =>
            e.AgentTaskId == seeded.Task.Id && e.Type == AgentTaskEventType.Committed);
        committed.Detail.ShouldContain("a.md");
        committed.Detail.ShouldContain("b.md");
        var note = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, "Wrote a.md and b.md.");
        note.Prompt.Text.ShouldContain("git=committed:");
        note.Prompt.Text.ShouldContain("(2 files)");
        spy.Verbs.ShouldContain("commit");
        spy.Verbs.ShouldNotContain("push");
    }

    [Test]
    public async Task C527_residual_dirty_path_outside_the_footprint_is_left_and_named()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "b.md"), "b");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "other.md"), "other");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "a.md"), DateTime.UtcNow);
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "b.md"), DateTime.UtcNow);
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote a.md and b.md.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);

        var names = (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        names.ShouldBe(["a.md", "b.md"], ignoreOrder: true);
        (await repo.GitReadAsync("status", "--porcelain")).Trim().ShouldContain("?? other.md");
        await using var verify = CreateContext();
        var note = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent);
        note.NoteHeader.ShouldContain("committed:");
        note.NoteHeader.ShouldContain("1 other dirty path(s) left as found");
        (await verify.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == seeded.Task.Id && e.Type == AgentTaskEventType.Warning)).ShouldBeFalse();
    }

    [Test]
    public async Task C527_report_named_path_is_footprint_when_the_transcript_is_empty()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        Directory.CreateDirectory(Path.Combine(repo.Path, "docs"));
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "docs", "x.md"), "x");
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `docs/x.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim().ShouldBe("docs/x.md");
        await using var verify = CreateContext();
        (await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent))
            .NoteHeader.ShouldContain("committed:");
    }

    [Test]
    public async Task C527_notebook_path_edits_count_as_footprint()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        var nb = Path.Combine(repo.Path, "n.ipynb");
        await File.WriteAllTextAsync(nb, "{}");
        await SeedFileEditAsync(seeded.SessionId, "NotebookEdit", nb, DateTime.UtcNow);
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "edited notebook");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim().ShouldBe("n.ipynb");
    }

    [Test]
    public async Task C527_unattributable_dirty_tree_spawns_a_commit_child_routed_by_the_card_pin()
    {
        var seeded = await SeedC527Async(t => t.AgentKind = AgentKind.Codex);
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        Guid cardId;
        await using (var db = CreateContext())
        {
            var card = await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0527T");
            cardId = card.Id;
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id);
            task.CardId = card.Id;
            db.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                Role = AgentTaskRole.Commit,
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                CandidatesJson = RoutingCandidate.Serialize(
                [
                    new(AgentKind.Codex, AgentModelLevel.Low),
                    new(AgentKind.ClaudeCode, AgentModelLevel.Medium),
                ]),
                Reason = "commit pin",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var factory = C527Factory(repo.WorktreeRoot, routingPins: true);
        AttachTerminal(factory, seeded.Parent);
        try
        {
            await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "all done");
            await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
            await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.RoutingPins.Where(p => p.CardId == cardId).ExecuteDeleteAsync();
        }

        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        await using var verify = CreateContext();
        var child = await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id);
        child.Role.ShouldBe(AgentTaskRole.Commit);
        child.Kind.ShouldBe(AgentTaskKind.Worker);
        child.AgentKind.ShouldBe(AgentKind.Codex);
        child.ModelLevel.ShouldBe(AgentModelLevel.Low);
        child.RoutingPinId.ShouldNotBeNull();
        child.Workspace.ShouldBe(WorkspaceMode.Shared);
        child.WorkingDirectory.ShouldBe(repo.Path);
        child.Ephemeral.ShouldBeTrue();
        child.MaxAttempts.ShouldBe(2);
        child.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
        child.CommitBaselineSha.ShouldBe(head);
        child.Title.ShouldBe("Commit: Seeded delegate");
        child.Goal.ShouldContain("x.md");
        child.Goal.ShouldContain("unattributable");
        var created = await verify.AgentTaskEvents.SingleAsync(e =>
            e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Created);
        created.Detail.ShouldContain("unattributable");
        var note = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, "all done");
        note.Prompt.Text.ShouldContain($"git=uncommitted:1 → commit task {DelegationReportFormatter.Short(child.Id)}");
    }

    [Test]
    public async Task C527_commit_child_falls_back_to_role_policy_with_a_warning_when_no_pin_service()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "all done");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await using var verify = CreateContext();
        var child = await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id);
        child.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        child.ModelLevel.ShouldBe(AgentModelLevel.Medium);
        child.RoutingPinId.ShouldBeNull();
        (await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Warning)
            .ToListAsync())
            .ShouldContain(e => e.Detail.Contains("pin", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task C527_NoCommit_task_leaves_the_tree_with_the_no_commit_header()
    {
        var seeded = await SeedC527Async(t => t.CommitOnSettle = CommitOnSettlePolicy.Never);
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `x.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        await using var verify = CreateContext();
        (await verify.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
        var note = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent);
        note.NoteHeader.ShouldContain("git=uncommitted:1 (no-commit)");
        note.Body.ShouldContain("1 file(s) left uncommitted as requested (-NoCommit).");
        note.Body.ShouldNotContain("Commit before building on it");
    }

    [Test]
    public async Task C527_project_off_inherited_skips_with_todays_warning()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        await using (var db = CreateContext())
        {
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"c527-off-{Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/x.git",
                CommitOnSettle = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Projects.Add(project);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id);
            task.ProjectId = project.Id;
            task.CommitOnSettle = null;
            await db.SaveChangesAsync();
        }

        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `x.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        await using var verify = CreateContext();
        (await verify.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
        var note = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent);
        note.NoteHeader.ShouldContain("uncommitted:1 (commit-on-settle off)");
        note.Body.ShouldContain("Commit before building on it.");
    }

    [Test]
    public async Task C527_task_Always_beats_project_off()
    {
        var seeded = await SeedC527Async(t => t.CommitOnSettle = CommitOnSettlePolicy.Always);
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        await using (var db = CreateContext())
        {
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"c527-off-{Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/x.git",
                CommitOnSettle = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Projects.Add(project);
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id);
            task.ProjectId = project.Id;
            await db.SaveChangesAsync();
        }

        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `x.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await using var verify = CreateContext();
        (await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent))
            .NoteHeader.ShouldContain("committed:");
        (await verify.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == seeded.Task.Id && e.Type == AgentTaskEventType.Committed)).ShouldBeTrue();
    }

    [Test]
    public async Task C527_Agent_policy_skips_tier1_and_spawns_the_child()
    {
        var seeded = await SeedC527Async(t => t.CommitOnSettle = CommitOnSettlePolicy.Agent);
        using var repo = seeded.Repo;
        var path = Path.Combine(repo.Path, "a.md");
        await File.WriteAllTextAsync(path, "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", path, DateTime.UtcNow);
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote a.md");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        await using var verify = CreateContext();
        (await verify.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == seeded.Task.Id && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
        var child = await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id);
        child.Goal.ShouldContain("agent-policy");
        (await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent))
            .NoteHeader.ShouldContain("→ commit task");
    }

    [Test]
    public async Task C527_global_off_with_project_null_skips()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var settings = new DelegationSettings { CommitOnSettle = false, PtySingleChunkBytes = 43_200 };
        var factory = C527Factory(repo.WorktreeRoot, delegation: settings);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `x.md`.");
        await CreateService(factory, settings).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        await using var verify = CreateContext();
        (await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent))
            .NoteHeader.ShouldContain("(commit-on-settle off)");
    }

    [Test]
    public async Task C527_deleted_gitignore_refuses_spawns_the_child_and_warns()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        File.Delete(Path.Combine(repo.Path, ".gitignore"));
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `x.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBeEmpty();
        await using var verify = CreateContext();
        var child = await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id);
        child.Goal.ShouldContain("ignore-rules-changed");
        child.Goal.ShouldContain(".gitignore");
        (await verify.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == seeded.Task.Id && e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains(".gitignore"))).ShouldBeTrue();
    }

    [Test]
    public async Task C527_held_lease_is_RepositoryBusy_and_spawns_the_child()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var factory = C527Factory(repo.WorktreeRoot);
        var leases = factory.ServiceProvider.GetRequiredService<Antiphon.Server.Application.Interfaces.IRepositoryMutationLease>();
        await using var held = await leases.TryAcquireAsync(repo.Path, CancellationToken.None);
        held.ShouldNotBeNull();
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `x.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id)).Goal.ShouldContain("repository-busy");
    }

    [Test]
    public async Task C527_hook_failure_is_CommitFailed_and_the_child_brief_carries_stderr()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await repo.InstallFailingPreCommitHookAsync("gate says no");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "x.md"), DateTime.UtcNow);
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `x.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await using var verify = CreateContext();
        var child = await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id);
        child.Goal.ShouldContain("gate says no");
        (await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent))
            .NoteHeader.ShouldNotContain("git=committed:");
        (await verify.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == seeded.Task.Id && e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains("CommitFailed"))).ShouldBeTrue();
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBe("x.md");
    }

    [Test]
    public async Task C527_commit_child_audit_flags_an_ignored_path_committed_outside_the_gate()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.secret"), "s");
        await repo.GitAsync("add", "-f", "a.secret");
        await repo.GitAsync("commit", "-m", "oops");
        var sha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim()[..7];
        var factory = C527Factory(repo.WorktreeRoot);
        AttachTerminal(factory, seeded.Parent);
        var (child, childSession) = await SeedDispatchedTaskAsync(repo.Path, seeded.Parent, t =>
        {
            t.Role = AgentTaskRole.Commit;
            t.ParentTaskId = seeded.Task.Id;
            t.CommitBaselineSha = baseline;
            t.RepoPath = repo.Path;
            t.Workspace = WorkspaceMode.Shared;
        });
        await SeedTurnAsync(childSession, DelegationReportFormatter.TaskMarker(child.Id), "committed oops");
        await CreateService(factory).OnTurnEndAsync(childSession, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        await using var verify = CreateContext();
        (await verify.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Warning
            && e.Detail.Contains("REVERT commit") && e.Detail.Contains("a.secret"))).ShouldBeTrue();
        (await verify.AgentIncidents.AnyAsync(i =>
            i.SessionId == childSession && i.Kind == AgentIncidentKind.DelegateCommitAudit)).ShouldBeTrue();
        var note = await AssertParentReceivedNoteAsync(seeded.Parent, child, "committed oops");
        note.Prompt.Text.ShouldContain($"REVERT commit {sha}: it contains ignored path(s) a.secret");
    }

    [Test]
    public async Task C527_commit_child_audit_flags_a_commit_without_the_gate_trailer()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "clean.md"), "c");
        await repo.GitAsync("add", "clean.md");
        await repo.GitAsync("commit", "-m", "ungated");
        var sha = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim()[..7];
        var factory = C527Factory(repo.WorktreeRoot);
        var (child, childSession) = await SeedDispatchedTaskAsync(repo.Path, seeded.Parent, t =>
        {
            t.Role = AgentTaskRole.Commit;
            t.ParentTaskId = seeded.Task.Id;
            t.CommitBaselineSha = baseline;
            t.RepoPath = repo.Path;
        });
        await SeedTurnAsync(childSession, DelegationReportFormatter.TaskMarker(child.Id), "committed");
        await CreateService(factory).OnTurnEndAsync(childSession, CancellationToken.None);
        await using var verify = CreateContext();
        var warning = await verify.AgentTaskEvents.SingleAsync(e =>
            e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain($"commit {sha} was made outside the gate");
        (await verify.AgentIncidents.AnyAsync(i => i.SessionId == childSession)).ShouldBeFalse();
        warning.Detail.ShouldNotContain("REVERT");
    }

    [Test]
    public async Task C527_commit_child_audit_flags_upstream_movement()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await repo.AddBareOriginAsync();
        var baseline = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "p.md"), "p");
        await repo.GitAsync("add", "p.md");
        await repo.GitAsync("commit", "-m", "push me");
        await repo.GitAsync("push", "origin", "master");
        var origin = (await repo.GitReadAsync("rev-parse", "origin/master")).Trim()[..7];
        var factory = C527Factory(repo.WorktreeRoot);
        var (child, childSession) = await SeedDispatchedTaskAsync(repo.Path, seeded.Parent, t =>
        {
            t.Role = AgentTaskRole.Commit;
            t.CommitBaselineSha = baseline;
            t.RepoPath = repo.Path;
        });
        await SeedTurnAsync(childSession, DelegationReportFormatter.TaskMarker(child.Id), "pushed");
        await CreateService(factory).OnTurnEndAsync(childSession, CancellationToken.None);
        await using var verify = CreateContext();
        var warning = await verify.AgentTaskEvents.FirstAsync(e =>
            e.AgentTaskId == child.Id && e.Type == AgentTaskEventType.Warning && e.Detail.Contains("pushed"));
        warning.Detail.ShouldContain(origin);
    }

    [Test]
    public async Task C527_task_cap_leaves_the_header_and_warning()
    {
        var settings = new DelegationSettings { MaxTasksPerRoot = 1, PtySingleChunkBytes = 43_200 };
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var factory = C527Factory(repo.WorktreeRoot, delegation: settings);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "all done");
        await CreateService(factory, settings).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await using var verify = CreateContext();
        (await verify.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id)).ShouldBeFalse();
        var note = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent);
        note.NoteHeader.ShouldContain("uncommitted:1 (commit task not spawned: run at task cap)");
        note.Body.ShouldContain("Commit before building on it.");
    }

    [Test]
    public async Task C527_gate_refusal_with_no_child_possible_renders_commit_refused()
    {
        var settings = new DelegationSettings { MaxTasksPerRoot = 1, PtySingleChunkBytes = 43_200 };
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        File.Delete(Path.Combine(repo.Path, ".gitignore"));
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        var factory = C527Factory(repo.WorktreeRoot, delegation: settings);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `x.md`.");
        await CreateService(factory, settings).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await using var verify = CreateContext();
        var note = await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent);
        note.NoteHeader.ShouldContain("commit refused: ignore rules changed (.gitignore)");
        note.Body.ShouldContain(".gitignore");
    }

    [Test]
    public async Task C527_tracked_then_ignored_path_refuses_the_whole_footprint()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, ".gitignore"), "tracked.txt\n");
        await repo.GitAsync("add", ".gitignore");
        await repo.GitAsync("commit", "-m", "ignore tracked");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "tracked.txt"), "changed");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "new.txt"), "new");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "tracked.txt"), DateTime.UtcNow);
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "new.txt"), DateTime.UtcNow);
        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote tracked and new");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        var status = await repo.GitReadAsync("status", "--porcelain");
        status.ShouldContain("tracked.txt");
        status.ShouldContain("new.txt");
        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id)).Goal.ShouldContain("ignored-path-staged");
    }

    [Test]
    public async Task C527_foreign_staged_path_survives_tier1_untouched()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "foo.txt"), "foo");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "bar.txt"), "bar");
        await repo.GitAsync("add", "foo.txt");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "bar.txt"), DateTime.UtcNow);
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote `bar.txt`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("diff", "--cached", "--name-only")).Trim().ShouldBe("foo.txt");
        (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")).Trim().ShouldBe("bar.txt");
    }

    [Test]
    public async Task C527_busy_parent_receives_the_committed_note_on_its_turn_end()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "a.md"), DateTime.UtcNow);
        var factory = C527Factory(repo.WorktreeRoot);
        var terminal = AttachTerminal(factory, seeded.Parent);
        await SeedEntryAsync(seeded.Parent, TranscriptKinds.AssistantText, "still working", DateTime.UtcNow);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote a.md");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(seeded.Parent, CancellationToken.None);
        terminal.Inputs.Count.ShouldBe(0);
        await using (var verify = CreateContext())
        {
            (await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent))
                .Status.ShouldBe(QueuedMessageStatus.Pending);
        }

        await SeedEntryAsync(seeded.Parent, TranscriptKinds.TurnEnd, null, DateTime.UtcNow);
        await Queue(factory).OnTurnEndAsync(seeded.Parent, CancellationToken.None);
        var note = await AssertParentReceivedNoteAsync(seeded.Parent, seeded.Task, "Wrote a.md");
        note.Prompt.Text.ShouldContain("git=committed:");
        terminal.SubmittedBodies.Count.ShouldBe(1);
    }

    [Test]
    public async Task C527_settle_save_failure_after_the_commit_recovers_without_a_second_commit()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "a.md"), "a");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "a.md"), DateTime.UtcNow);
        var interceptor = new ThrowOnceSaveInterceptor();
        var factory = C527Factory(repo.WorktreeRoot, saveInterceptor: interceptor);
        var before = int.Parse((await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim());
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "Wrote a.md");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var factory2 = C527Factory(repo.WorktreeRoot);
        await CreateService(factory2).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var after = int.Parse((await repo.GitReadAsync("rev-list", "--count", "HEAD")).Trim());
        after.ShouldBe(before + 1);
        (await repo.GitReadAsync("log", "--grep", seeded.Task.Id.ToString("D"), "--format=%H"))
            .Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(1);
        await using var verify = CreateContext();
        (await verify.AgentTaskEvents.CountAsync(e =>
            e.AgentTaskId == seeded.Task.Id && e.Type == AgentTaskEventType.Committed)).ShouldBe(1);
        (await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent))
            .NoteHeader.ShouldContain("committed:");
        (await verify.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
    }

    [Test]
    [Arguments(Ineligible.Blocked)]
    [Arguments(Ineligible.Failed)]
    [Arguments(Ineligible.ReadOnly)]
    [Arguments(Ineligible.CommitRole)]
    [Arguments(Ineligible.MutationRole)]
    [Arguments(Ineligible.SourceLanding)]
    public async Task C527_ineligible_tasks_never_run_the_hook(Ineligible c)
    {
        var spy = new RecordingGitWorkspaceService();
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "x.md"), "x");
        await SeedFileEditAsync(seeded.SessionId, "Write", Path.Combine(repo.Path, "x.md"), DateTime.UtcNow);
        await using (var db = CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id);
            switch (c)
            {
                case Ineligible.ReadOnly: task.Workspace = WorkspaceMode.ReadOnly; break;
                case Ineligible.CommitRole: task.Role = AgentTaskRole.Commit; break;
                case Ineligible.MutationRole: task.Role = AgentTaskRole.Mutation; break;
                case Ineligible.SourceLanding:
                {
                    var landingId = Guid.NewGuid();
                    db.AgentTaskLandings.Add(new AgentTaskLanding
                    {
                        Id = landingId,
                        TaskId = task.Id,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync();
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE \"AgentTasks\" SET \"SourceLandingOperationId\" = {landingId} WHERE \"Id\" = {task.Id}");
                    break;
                }
            }

            if (c != Ineligible.SourceLanding)
                await db.SaveChangesAsync();
        }

        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var factory = C527Factory(repo.WorktreeRoot, gitSpy: spy);
        var marker = DelegationReportFormatter.TaskMarker(seeded.Task.Id);
        if (c == Ineligible.Blocked)
            await SeedTurnAsync(seeded.SessionId, marker, "ask?\n" + DelegationReportFormatter.ReportToken(seeded.Task.Id, "blocked"));
        else if (c == Ineligible.Failed)
            await SeedTurnAsync(seeded.SessionId, marker, "failed\n" + DelegationReportFormatter.ReportToken(seeded.Task.Id, "failed"), closingVerdict: false);
        else
            await SeedTurnAsync(seeded.SessionId, marker, "Wrote `x.md`.");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var headAfter = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        headAfter.ShouldBe(head);
        spy.Verbs.ShouldNotContain("add");
        spy.Verbs.ShouldNotContain("commit");
        await using var verify = CreateContext();
        (await verify.AgentTasks.AnyAsync(t => t.ParentTaskId == seeded.Task.Id && t.Role == AgentTaskRole.Commit)).ShouldBeFalse();
        (await verify.AgentTaskEvents.AnyAsync(e =>
            e.AgentTaskId == seeded.Task.Id && e.Type == AgentTaskEventType.Committed)).ShouldBeFalse();
    }

    [Test]
    public async Task C527_edits_before_dispatch_are_not_in_the_footprint()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        var path = Path.Combine(repo.Path, "old.md");
        await File.WriteAllTextAsync(path, "old");
        await using (var db = CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Task.Id);
            await SeedFileEditAsync(seeded.SessionId, "Write", path, task.DispatchedAt!.Value.AddSeconds(-1));
        }

        var head = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "done");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        (await repo.GitReadAsync("rev-parse", "HEAD")).Trim().ShouldBe(head);
        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == seeded.Task.Id)).Goal.ShouldContain("unattributable");
    }

    [Test]
    public async Task C527_more_than_twenty_dirty_paths_are_all_attributed_from_the_transcript()
    {
        var seeded = await SeedC527Async();
        using var repo = seeded.Repo;
        for (var i = 0; i < 25; i++)
        {
            var path = Path.Combine(repo.Path, $"f{i:00}.md");
            await File.WriteAllTextAsync(path, "x");
            await SeedFileEditAsync(seeded.SessionId, "Write", path, DateTime.UtcNow);
        }

        var factory = C527Factory(repo.WorktreeRoot);
        await SeedTurnAsync(seeded.SessionId, DelegationReportFormatter.TaskMarker(seeded.Task.Id), "wrote many files");
        await CreateService(factory).OnTurnEndAsync(seeded.SessionId, CancellationToken.None);
        var names = (await repo.GitReadAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        names.Length.ShouldBe(25);
        await using var verify = CreateContext();
        (await verify.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == seeded.Parent))
            .NoteHeader.ShouldContain("(25 files)");
    }

    public enum Ineligible { Blocked, Failed, ReadOnly, CommitRole, MutationRole, SourceLanding }

    private sealed class ThrowOnceSaveInterceptor : SaveChangesInterceptor
    {
        private int _remaining = 1;

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (System.Threading.Interlocked.Exchange(ref _remaining, 0) == 1)
                throw new InvalidOperationException("save failed after commit");
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (System.Threading.Interlocked.Exchange(ref _remaining, 0) == 1)
                throw new InvalidOperationException("save failed after commit");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
