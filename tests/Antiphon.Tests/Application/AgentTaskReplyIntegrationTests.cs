using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The reply path: a delegate's finished turn becomes the task's result and a note for its parent.
///
/// The load-bearing behaviour here is the MARKER gate. Correlation matches the
/// <c>[antiphon-task:id]</c> marker carried in the brief, never prompt text — so a human typing in
/// a delegate's terminal can never be mistaken for that task finishing.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskReplyIntegrationTests
{
    [Test]
    public async Task a_marked_turn_settles_the_task_and_stores_the_report_verbatim()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        const string report = "Added Fizz(int) in Numbers.cs (+11 lines). 142 passed, 0 failed.";

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.", report);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(report, "the report is the deliverable — it is stored untouched");
        settled.CompletedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task an_unmarked_turn_leaves_the_task_running()
    {
        // A human typed in the delegate's terminal. Without the marker gate this would end the task
        // with the wrong text and send that to the caller as the result.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(sessionId, "what files are in this directory?", "Here's the listing: ...");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched, "a human's turn is not the delegate's report");
        stored.Result.ShouldBeNull();
    }

    [Test]
    public async Task another_tasks_marker_does_not_settle_this_task()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(Guid.NewGuid()) + "\n\nA different task entirely.",
            "Did the other thing.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task a_turn_with_no_assistant_text_yet_leaves_the_task_running()
    {
        // Claude sometimes writes the turn's stop marker BEFORE its reply text. Settling here would
        // record an empty report; the AssistantText's own arrival re-triggers settlement.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), assistantText: null);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
    }

    [Test]
    public async Task a_delegate_that_asks_a_question_comes_back_blocked_not_finished()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(task.Id),
            "Added Fizz(int).\n\nBuzz throws on negatives — should Fizz match that?");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Blocked, "it needs an answer, not a retry");
    }

    [Test]
    public async Task a_report_under_the_ceiling_is_not_spilled_to_a_file()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), new string('x', 18_000));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.ResultFilePath.ShouldBeNull();
        settled.Result!.Length.ShouldBe(18_000);
        Directory.Exists(Path.Combine(workspace.Path, ".antiphon")).ShouldBeFalse();
    }

    [Test]
    public async Task an_oversized_report_is_backstopped_to_a_file_by_the_server()
    {
        // The delegate was told to spill and didn't. The server writes the file itself, so the
        // excerpt the caller receives has somewhere real to point — and the full text survives.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        var huge = new string('y', 25_000);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), huge);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.ResultFilePath.ShouldNotBeNull();
        File.Exists(settled.ResultFilePath).ShouldBeTrue();
        (await File.ReadAllTextAsync(settled.ResultFilePath!)).Length.ShouldBe(25_000);
        settled.Result!.Length.ShouldBe(25_000, "the task row always keeps the untouched original");
    }

    [Test]
    public async Task a_spill_file_the_delegate_wrote_itself_is_used_as_is()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        var spillPath = Path.Combine(
            workspace.Path, ".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(spillPath)!);
        await File.WriteAllTextAsync(spillPath, "THE DELEGATE'S OWN FULL DETAIL");

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), new string('z', 25_000));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.ResultFilePath.ShouldBe(spillPath);
        (await File.ReadAllTextAsync(spillPath))
            .ShouldBe("THE DELEGATE'S OWN FULL DETAIL", "the delegate's own file must not be overwritten");
    }

    [Test]
    public async Task the_completion_note_is_delivered_into_the_parents_session()
    {
        // The whole point: the caller learns the outcome without reading a transcript.
        using var workspace = new TempWorkspace();
        var parentSessionId = await SeedSessionAsync(workspace.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, parentSessionId);

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Rewrote the section. 34 lines changed.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var queued = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSessionId)
            .ToListAsync();

        queued.Count.ShouldBe(1);
        queued[0].Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        queued[0].ConversationKey.ShouldBe($"task:{task.RootTaskId:N}", "same-root results coalesce");
        queued[0].Body.ShouldContain("Rewrote the section. 34 lines changed.");
        queued[0].Body.ShouldContain(DelegationReportFormatter.Short(task.Id));
        queued[0].Body.Contains('\r').ShouldBeFalse("a CR mid-body would submit the fragment before it");
    }

    [Test]
    public async Task a_task_with_no_parent_session_settles_without_delivering_anywhere()
    {
        // The manual entry point: the result lands on the board only.
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, parentSessionId: null);

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.");
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        // Scoped to THIS task — the fixture's database is shared, so a global count would pick up
        // rows other tests legitimately left behind.
        var shortId = DelegationReportFormatter.Short(task.Id);
        (await verify.SessionQueuedMessages.CountAsync(m => m.Body.Contains(shortId))).ShouldBe(0);
    }

    [Test]
    public async Task token_spend_is_rolled_up_onto_the_task()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);

        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.", inputTokens: 50_000, outputTokens: 4_000);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.TokensIn.ShouldBe(50_000);
        settled.TokensOut.ShouldBe(4_000);
        settled.CostUsd.ShouldBeGreaterThan(0m, "the per-root ceiling can only work if spend is recorded");
    }

    [Test]
    public async Task a_session_running_no_task_is_ignored()
    {
        using var workspace = new TempWorkspace();
        var sessionId = await SeedSessionAsync(workspace.Path);

        await SeedTurnAsync(sessionId, "just a chat", "sure thing");

        // Must be a clean no-op — every ordinary agent session hits this path on every turn-end.
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
    }

    // ---- ephemeral cleanup -----------------------------------------------------------------

    [Test]
    public async Task a_succeeded_ephemeral_delegate_is_stopped_and_its_agent_row_deleted()
    {
        // The delegate is spent: leaving the session running spends memory on nothing, and the
        // agent row would pile onto the agents page one settled task at a time.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, "task-cleanup");
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => { t.Ephemeral = true; t.AgentId = agentId; t.AgentName = "task-cleanup"; });

        var factory = new TestScopeFactory();
        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Done.");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        factory.Stopper.Killed.ShouldBe([sessionId]);
        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == agentId)).ShouldBeFalse("the throwaway row must go");
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.AgentName.ShouldBe("task-cleanup", "the snapshot keeps naming who ran the work");
    }

    [Test]
    public async Task a_blocked_delegate_keeps_its_session_and_agent()
    {
        // Blocked means the conversation continues — killing the session here would orphan the
        // -Reply path and force a cold retry of work that only needed an answer.
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, "task-blocked");
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => { t.Ephemeral = true; t.AgentId = agentId; });

        var factory = new TestScopeFactory();
        await SeedTurnAsync(
            sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Should negatives throw?");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        factory.Stopper.Killed.ShouldBeEmpty();
        await using var verify = CreateContext();
        (await verify.Agents.AnyAsync(a => a.Id == agentId)).ShouldBeTrue();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Blocked);
    }

    // ---- worktree merge-back on settle -----------------------------------------------------

    [Test]
    public async Task a_succeeded_worktree_task_lands_its_branch_and_says_so_in_the_note()
    {
        using var repo = new ScratchGitRepo("antiphon-reply-merge");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");
        var factory = new TestScopeFactory(repo.WorktreeRoot);

        var parentSessionId = await SeedSessionAsync(repo.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(repo.Path, parentSessionId, t =>
        {
            t.Workspace = WorkspaceMode.Worktree;
            t.RepoPath = repo.Path;
            t.MergeTargetRef = "feat/parent";
        });
        await CreateWorktreeForAsync(factory, task);
        await File.WriteAllTextAsync(Path.Combine(TaskWorktreePath(task)!, "feature.md"), "the work\n");

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Wrote feature.md.");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await repo.GitReadAsync("show", "feat/parent:feature.md")).ShouldBe("the work\n");
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Merged)).ShouldBeTrue();

        var note = await verify.SessionQueuedMessages
            .SingleAsync(m => m.AgentSessionId == parentSessionId);
        note.Body.ShouldContain("merged → feat/parent", customMessage: "the caller must learn the branch landed");
    }

    [Test]
    public async Task a_merge_conflict_blocks_the_task_and_spawns_a_merge_delegate()
    {
        // "Done" work that cannot land is not done. The task blocks, and the conflict goes to a
        // Merge-role delegate working in the conflicted worktree — never an automatic resolution.
        using var repo = new ScratchGitRepo("antiphon-reply-conflict");
        await repo.CommitFileAsync("shared.md", "original\n");
        await repo.GitAsync("branch", "feat/parent");
        var factory = new TestScopeFactory(repo.WorktreeRoot);

        var parentSessionId = await SeedSessionAsync(repo.Path);
        var (task, sessionId) = await SeedDispatchedTaskAsync(repo.Path, parentSessionId, t =>
        {
            t.Workspace = WorkspaceMode.Worktree;
            t.RepoPath = repo.Path;
            t.MergeTargetRef = "feat/parent";
        });
        await CreateWorktreeForAsync(factory, task);
        await File.WriteAllTextAsync(Path.Combine(TaskWorktreePath(task)!, "shared.md"), "delegate version\n");
        await repo.GitAsync("checkout", "feat/parent");
        await repo.CommitFileAsync("shared.md", "target version\n");

        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id), "Rewrote shared.md.");
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var blocked = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        blocked.Status.ShouldBe(AgentTaskStatus.Blocked);
        blocked.FailureReason.ShouldContain("conflict");

        var merge = await verify.AgentTasks.SingleAsync(t => t.ParentTaskId == task.Id);
        merge.Role.ShouldBe(AgentTaskRole.Merge);
        merge.ModelLevel.ShouldBe(AgentModelLevel.High, "conflict resolution is High-tier work by policy");
        merge.WorkingDirectory.ShouldBe(TaskWorktreePath(task), "it resolves IN the conflicted worktree");
        merge.ParentSessionId.ShouldBe(parentSessionId, "its report goes to the same caller");
        merge.Goal.ShouldContain("shared.md");
    }

    [Test]
    public async Task a_finished_merge_delegate_unblocks_its_conflicted_parent()
    {
        // The loop-closer: without it, a conflicted task stays Blocked forever after its conflict
        // was actually resolved.
        using var workspace = new TempWorkspace();
        var conflictedId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = conflictedId,
                RootTaskId = conflictedId,
                Title = "The conflicted task",
                Goal = "original work",
                Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = workspace.Path,
                WorktreePath = workspace.Path,
                WorktreeBranch = "feat/card-task-x",
                MergeTargetRef = "master",
                Status = AgentTaskStatus.Blocked,
                FailureReason = "Rebase onto master conflicted in 1 file(s).",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var (merge, mergeSessionId) = await SeedDispatchedTaskAsync(workspace.Path, configure: t =>
        {
            t.RootTaskId = conflictedId;
            t.ParentTaskId = conflictedId;
            t.Role = AgentTaskRole.Merge;
            t.ModelLevel = AgentModelLevel.High;
        });

        await SeedTurnAsync(
            mergeSessionId, DelegationReportFormatter.TaskMarker(merge.Id),
            "Resolved shared.md keeping the task's version; master fast-forwarded.");
        await CreateService().OnTurnEndAsync(mergeSessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == merge.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        var parent = await verify.AgentTasks.SingleAsync(t => t.Id == conflictedId);
        parent.Status.ShouldBe(AgentTaskStatus.Succeeded, "the conflict it was blocked on no longer exists");
        parent.FailureReason.ShouldBeNull();
    }

    private static async Task CreateWorktreeForAsync(TestScopeFactory factory, AgentTask seeded)
    {
        // The dispatcher's move, replayed: create the worktree and persist its coordinates.
        var worktrees = factory.ServiceProvider.GetRequiredService<DelegationWorktreeService>();
        await worktrees.CreateForTaskAsync(seeded, CancellationToken.None);
        await using var db = CreateContext();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == seeded.Id);
        row.WorktreePath = seeded.WorktreePath;
        row.WorktreeBranch = seeded.WorktreeBranch;
        await db.SaveChangesAsync();
    }

    private static string? TaskWorktreePath(AgentTask task) => task.WorktreePath;

    // ---- helpers ---------------------------------------------------------------------------

    private static AgentTaskReplyService CreateService(TestScopeFactory? factory = null)
    {
        var settings = new DelegationSettings { ReplyInlineMaxChars = 20_000 };
        return new AgentTaskReplyService(
            factory ?? new TestScopeFactory(),
            Options.Create(settings),
            new MockEventBus(),
            TimeProvider.System,
            NullLogger<AgentTaskReplyService>.Instance);
    }

    private static async Task<(AgentTask Task, Guid SessionId)> SeedDispatchedTaskAsync(
        string workingDirectory, Guid? parentSessionId = null, Action<AgentTask>? configure = null)
    {
        var sessionId = await SeedSessionAsync(workingDirectory);
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentSessionId = parentSessionId,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            Title = "Seeded delegate",
            Goal = "Do the thing.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Docs,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = DateTime.UtcNow,
            DispatchedAt = DateTime.UtcNow,
        };
        configure?.Invoke(task);

        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return (task, sessionId);
    }

    private static async Task<Guid> SeedAgentAsync(string workingDirectory, string name)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = name,
            WorkingDirectory = workingDirectory,
            Details = "Ephemeral delegate.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task<Guid> SeedSessionAsync(string cwd)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            CardId = null,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    /// <summary>A prompt, optional assistant text, then a TurnEnd — the shape a real turn leaves.</summary>
    private static async Task SeedTurnAsync(
        Guid sessionId, string prompt, string? assistantText, int? inputTokens = null, int? outputTokens = null)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        db.TranscriptEntries.Add(NewEntry(sessionId, ++seq, TranscriptKinds.UserPrompt, prompt));
        if (assistantText is not null)
        {
            var entry = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, assistantText);
            entry.InputTokens = inputTokens;
            entry.OutputTokens = outputTokens;
            db.TranscriptEntries.Add(entry);
        }
        var end = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        end.StopReason = "end_turn";
        db.TranscriptEntries.Add(end);
        await db.SaveChangesAsync();
    }

    private static TranscriptEntry NewEntry(Guid sessionId, long sequence, string kind, string? text) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        CreatedAt = DateTime.UtcNow,
    };

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>
    /// The reply service is a singleton that opens a DI scope per operation. This supplies the two
    /// services it resolves — a real DbContext and a queue whose runtime is never actually driven
    /// (delivery is asserted through the persisted queue rows, not a live pty).
    /// </summary>
    private sealed class TestScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly ServiceProvider _provider;

        /// <summary>Records what the settle path asked to stop — the ephemeral-cleanup assertion.</summary>
        public RecordingSessionStopper Stopper { get; } = new();

        public TestScopeFactory(string? worktreeRoot = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IEventBus, MockEventBus>();
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            // The settle path's collaborators: merge-back, the Merge-task spawner, ephemeral cleanup.
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IDelegateSessionStopper>(Stopper);
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddScoped<AgentTaskService>();
            services.AddSingleton(Options.Create(new GitSettings
            {
                WorktreeBasePath = worktreeRoot ?? Path.Combine(Path.GetTempPath(), "antiphon-reply-wt"),
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24,
            }));
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IWorktreeManager,
                Antiphon.Server.Infrastructure.Git.WorktreeManager>();
            services.AddSingleton<Antiphon.Server.Application.Interfaces.IGitService,
                Antiphon.Server.Infrastructure.Git.GitService>();
            services.AddScoped<DelegationWorktreeService>();
            _provider = services.BuildServiceProvider();
        }

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => _provider;
        public object? GetService(Type serviceType) => _provider.GetService(serviceType);
        public void Dispose() { }

        private sealed class TempWorkspaceMarker;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-reply-test").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
