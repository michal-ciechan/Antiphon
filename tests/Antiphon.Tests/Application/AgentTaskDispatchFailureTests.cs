using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0220 S3: a dispatch that dies before a session exists must fail ONE task, tell the caller,
/// and leave the rest of the tick running.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentQueue")]
public class AgentTaskDispatchFailureTests
{
    [Test]
    public async Task a_dispatch_that_throws_before_a_session_exists_tells_the_caller()
    {
        using var notGit = new TempWorkspace();
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(parentSessionId);

        var (dispatcher, _) = CreateHarness();
        var task = await SeedQueuedWorktreeTaskAsync(
            notGit.Path,
            parentSessionId: parentSessionId,
            replyTo: AgentTaskReplyTo.Session);
        await DrainOtherQueuedAsync(task.Id);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Failed);
        stored.DispatchedAt.ShouldBeNull();
        stored.FailureReason.ShouldNotBeNull();
        stored.FailureReason.ShouldStartWith("Dispatch failed before a session existed:");

        var note = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == parentSessionId)
            .ToListAsync();
        note.ShouldHaveSingleItem();
        note[0].Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        note[0].NoteHeader.ShouldNotBeNull();
        note[0].NoteHeader!.ShouldStartWith($"[task {DelegationReportFormatter.Short(task.Id)} failed]");
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_git_timeout_fails_one_task_not_the_tick(CancellationToken ct)
    {
        using var slow = new ScratchGitRepo("card0220-slow");
        using var healthy = new ScratchGitRepo("card0220-ok");
        await slow.CommitFileAsync("README.md", "slow\n");
        await healthy.CommitFileAsync("README.md", "ok\n");

        var hooks = Path.Combine(slow.Path, ".git-hooks-sleep");
        Directory.CreateDirectory(hooks);
        await File.WriteAllTextAsync(Path.Combine(hooks, "post-checkout"), "#!/bin/sh\nsleep 30\nexit 0\n");
        await slow.GitAsync("config", "core.hooksPath", hooks);

        var (dispatcher, _) = CreateHarness(worktreeAddTimeoutSeconds: 1);
        var slowTask = await SeedQueuedWorktreeTaskAsync(
            slow.Path, createdAt: DateTime.UtcNow.AddSeconds(-2));
        var healthyTask = await SeedQueuedWorktreeTaskAsync(
            healthy.Path, createdAt: DateTime.UtcNow.AddSeconds(-1));
        await DrainOtherQueuedAsync(slowTask.Id, healthyTask.Id);

        var result = await dispatcher.TickAsync(ct);

        result.Failures.ShouldBe(1);
        result.Dispatched.ShouldBe(1);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == slowTask.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.DispatchedAt.ShouldBeNull();
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason.ShouldContain("timed out");

        var dispatched = await verify.AgentTasks.SingleAsync(t => t.Id == healthyTask.Id);
        dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        dispatched.DispatchedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task a_dispatch_failure_arms_a_reminder()
    {
        using var notGit = new TempWorkspace();
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(parentSessionId);

        var (dispatcher, _) = CreateHarness();
        var task = await SeedQueuedWorktreeTaskAsync(
            notGit.Path,
            parentSessionId: parentSessionId,
            replyTo: AgentTaskReplyTo.Session);
        await DrainOtherQueuedAsync(task.Id);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Failed);
        stored.DispatchedAt.ShouldBeNull();
        stored.CheckCount.ShouldBe(0);
        stored.NextCheckAt.ShouldNotBeNull();
        stored.CompletedAt.ShouldNotBeNull();
        stored.NextCheckAt!.Value.ShouldBe(
            stored.CompletedAt!.Value.AddMinutes(5), TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task a_lost_failure_note_is_re_sent_on_the_ramp()
    {
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(parentSessionId);
        var task = await SeedFailedNeverDispatchedAsync(parentSessionId);
        await DrainOtherQueuedAsync();

        var before = DateTime.UtcNow;
        var (dispatcher, _) = CreateHarness();
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.CheckCount.ShouldBe(1);
        stored.NextCheckAt.ShouldNotBeNull();
        // After reminder #1 the check sweep's own NextInterval(checkNumber: 1) is 5 minutes.
        // The plan's test list said 10; that would skip a ramp step and miss the ~5.6h budget.
        stored.NextCheckAt!.Value.ShouldBe(before.AddMinutes(5), TimeSpan.FromMinutes(1));

        var notes = await NotesForAsync(verify, task.Id);
        notes.ShouldHaveSingleItem();
        notes[0].Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        notes[0].NoteHeader.ShouldNotBeNull();
        notes[0].NoteHeader!.ShouldStartWith($"[task {DelegationReportFormatter.Short(task.Id)} failed]");
        notes[0].NoteHeader.ShouldContain("reminder 1/10");

        var warning = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("Failure reminder #1 queued: first note absent");
    }

    [Test]
    public async Task a_parked_failure_note_is_re_sent()
    {
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(parentSessionId);
        var task = await SeedFailedNeverDispatchedAsync(parentSessionId);
        await SeedDelegationNoteAsync(
            parentSessionId, task.Id, QueuedMessageStatus.Pending, deliveryAttempts: 3);
        await DrainOtherQueuedAsync();

        var (dispatcher, _) = CreateHarness();
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var notes = await NotesForAsync(verify, task.Id);
        notes.Count.ShouldBe(2, "the parked original stays; a new reminder is queued");
        notes.ShouldContain(n => n.NoteHeader != null && n.NoteHeader.Contains("reminder 1/10"));
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).CheckCount.ShouldBe(1);

        var warning = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("first note parked");
    }

    [Test]
    public async Task a_pending_note_is_not_duplicated()
    {
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(parentSessionId);
        var task = await SeedFailedNeverDispatchedAsync(parentSessionId);
        await SeedDelegationNoteAsync(
            parentSessionId, task.Id, QueuedMessageStatus.Pending, deliveryAttempts: 0);
        await DrainOtherQueuedAsync();

        var before = DateTime.UtcNow;
        var (dispatcher, _) = CreateHarness();
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await NotesForAsync(verify, task.Id)).ShouldHaveSingleItem();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.CheckCount.ShouldBe(0, "an in-flight look must not spend a reminder");
        stored.NextCheckAt.ShouldNotBeNull();
        stored.NextCheckAt!.Value.ShouldBe(before.AddMinutes(5), TimeSpan.FromMinutes(1));
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning)).ShouldBe(0);
    }

    [Test]
    public async Task a_sent_note_disarms_the_reminder()
    {
        await AssertDisarmAsync(async (parent, task) =>
            await SeedDelegationNoteAsync(parent, task, QueuedMessageStatus.Sent));
    }

    [Test]
    public async Task a_status_poll_disarms_the_reminder()
    {
        await AssertDisarmAsync((_, _) => Task.CompletedTask, lastPolledResultAt: DateTime.UtcNow);
    }

    [Test]
    public async Task a_read_disarms_the_reminder()
    {
        await AssertDisarmAsync((_, _) => Task.CompletedTask, readAt: DateTime.UtcNow);
    }

    [Test]
    public async Task a_dropped_note_disarms()
    {
        await AssertDisarmAsync(async (parent, task) =>
            await SeedDelegationNoteAsync(parent, task, QueuedMessageStatus.Canceled));
    }

    [Test]
    public async Task a_gone_caller_disarms_the_reminder()
    {
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(parentSessionId, SessionStatus.Stopped);
        var task = await SeedFailedNeverDispatchedAsync(parentSessionId);
        await DrainOtherQueuedAsync();

        var (dispatcher, _) = CreateHarness();
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.NextCheckAt.ShouldBeNull();
        (await NotesForAsync(verify, task.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task the_reminder_budget_ends_and_says_so()
    {
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(parentSessionId);
        var task = await SeedFailedNeverDispatchedAsync(parentSessionId, checkCount: 9);
        await DrainOtherQueuedAsync();

        var (dispatcher, _) = CreateHarness();
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.CheckCount.ShouldBe(10);
        stored.NextCheckAt.ShouldBeNull();

        var notes = await NotesForAsync(verify, task.Id);
        notes.ShouldHaveSingleItem();
        notes[0].NoteHeader.ShouldContain("reminder 10/10");
        notes[0].NoteHeader.ShouldContain("final reminder");
        notes[0].Body.ShouldContain("final reminder");

        var warning = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("final reminder — the 10-reminder budget is spent");
    }

    [Test]
    public async Task a_reminder_is_never_armed_for_a_board_only_task()
    {
        using var notGit = new TempWorkspace();
        var (dispatcher, _) = CreateHarness();
        var task = await SeedQueuedWorktreeTaskAsync(notGit.Path, replyTo: AgentTaskReplyTo.None);
        await DrainOtherQueuedAsync(task.Id);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Failed);
        stored.DispatchedAt.ShouldBeNull();
        stored.NextCheckAt.ShouldBeNull();
        stored.CheckCount.ShouldBe(0);
        (await NotesForAsync(verify, task.Id)).ShouldBeEmpty();
    }

    private static async Task AssertDisarmAsync(
        Func<Guid, Guid, Task> seed,
        DateTime? lastPolledResultAt = null,
        DateTime? readAt = null)
    {
        var parentSessionId = Guid.NewGuid();
        await SeedParentSessionAsync(parentSessionId);
        var task = await SeedFailedNeverDispatchedAsync(
            parentSessionId, lastPolledResultAt: lastPolledResultAt, readAt: readAt);
        await seed(parentSessionId, task.Id);
        await DrainOtherQueuedAsync();

        var (dispatcher, _) = CreateHarness();
        await dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.NextCheckAt.ShouldBeNull();
        var notes = await NotesForAsync(verify, task.Id);
        notes.ShouldNotContain(n => n.NoteHeader != null && n.NoteHeader.Contains("reminder "));
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning)).ShouldBe(0);
    }

    private static (AgentTaskDispatcher Dispatcher, ServiceProvider Provider) CreateHarness(
        int worktreeAddTimeoutSeconds = 180)
    {
        var stopper = new RecordingSessionStopper();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Directory.CreateTempSubdirectory("antiphon-0220-wt").FullName,
            WorktreeAddTimeoutSeconds = worktreeAddTimeoutSeconds,
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), provider);
    }

    private static async Task SeedParentSessionAsync(
        Guid parentSessionId, SessionStatus status = SessionStatus.Running)
    {
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = parentSessionId,
            DefinitionName = "card0220-parent",
            AgentKind = AgentKind.ClaudeCode,
            Status = status,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            StartedAt = DateTime.UtcNow.AddHours(-1),
            LastSeenAt = DateTime.UtcNow,
            EndedAt = status is SessionStatus.Stopped or SessionStatus.Failed
                ? DateTime.UtcNow.AddMinutes(-5)
                : null,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<AgentTask> SeedFailedNeverDispatchedAsync(
        Guid parentSessionId,
        int checkCount = 0,
        DateTime? lastPolledResultAt = null,
        DateTime? readAt = null)
    {
        var id = Guid.NewGuid();
        var failedAt = DateTime.UtcNow.AddMinutes(-10);
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "card-0231 never dispatched",
            Goal = "exercise an unacknowledged pre-dispatch failure",
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = Path.GetTempPath(),
            ParentSessionId = parentSessionId,
            ReplyTo = AgentTaskReplyTo.Session,
            Status = AgentTaskStatus.Failed,
            FailureReason = "Dispatch failed before a session existed: the directory is not a git repo",
            CreatedAt = failedAt,
            CompletedAt = failedAt,
            DispatchedAt = null,
            NextCheckAt = DateTime.UtcNow.AddMinutes(-1),
            CheckCount = checkCount,
            LastPolledResultAt = lastPolledResultAt,
            ReadAt = readAt,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task SeedDelegationNoteAsync(
        Guid sessionId,
        Guid taskId,
        QueuedMessageStatus status,
        int deliveryAttempts = 0)
    {
        await using var db = CreateContext();
        var next = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId)
            .MaxAsync(m => (long?)m.Sequence) ?? 0;
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = $"[task {DelegationReportFormatter.Short(taskId)} failed] original note",
            NoteHeader = $"[task {DelegationReportFormatter.Short(taskId)} failed]",
            Status = status,
            Sequence = next + 1,
            Origin = QueuedMessageOrigin.Delegation,
            ConversationKey = $"task:{taskId:N}",
            SourceTaskId = taskId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-8),
            DeliveryAttempts = deliveryAttempts,
            SentAt = status == QueuedMessageStatus.Sent ? DateTime.UtcNow.AddMinutes(-7) : null,
            CanceledAt = status == QueuedMessageStatus.Canceled ? DateTime.UtcNow.AddMinutes(-1) : null,
        });
        await db.SaveChangesAsync();
    }

    private static Task<List<SessionQueuedMessage>> NotesForAsync(AppDbContext db, Guid taskId) =>
        db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.SourceTaskId == taskId && m.Origin == QueuedMessageOrigin.Delegation)
            .OrderBy(m => m.Sequence)
            .ToListAsync();

    private static async Task<AgentTask> SeedQueuedWorktreeTaskAsync(
        string repoPath,
        Guid? parentSessionId = null,
        AgentTaskReplyTo replyTo = AgentTaskReplyTo.None,
        DateTime? createdAt = null)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "card-0220 dispatch",
            Goal = "exercise a pre-session dispatch failure",
            Role = AgentTaskRole.Code,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = repoPath,
            RepoPath = repoPath,
            ParentSessionId = parentSessionId,
            ReplyTo = replyTo,
            Status = AgentTaskStatus.Queued,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
        await using var db = CreateContext();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task DrainOtherQueuedAsync(params Guid[] keep)
    {
        await using var db = CreateContext();
        var leftovers = await db.AgentTasks
            .Where(t => t.Status == AgentTaskStatus.Queued && !keep.Contains(t.Id))
            .ToListAsync();
        foreach (var leftover in leftovers)
        {
            leftover.Status = AgentTaskStatus.Canceled;
            leftover.CompletedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-0220-notgit").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
