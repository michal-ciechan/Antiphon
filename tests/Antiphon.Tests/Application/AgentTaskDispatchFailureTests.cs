using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
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
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Directory.CreateTempSubdirectory("antiphon-0220-wt").FullName,
            WorktreeAddTimeoutSeconds = worktreeAddTimeoutSeconds,
        }));
        services.AddSingleton<IWorktreeManager, WorktreeManager>();
        services.AddSingleton<IGitService, GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), provider);
    }

    private static async Task SeedParentSessionAsync(Guid parentSessionId)
    {
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = parentSessionId,
            DefinitionName = "card0220-parent",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            StartedAt = DateTime.UtcNow.AddHours(-1),
            LastSeenAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

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
