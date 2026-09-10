using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class MutationDispatchTests
{
    [Test]
    public async Task C470_retained_worktree_launch_is_fresh()
    {
        await using var h = await Harness.CreateAsync();
        await Git(h.Root, "init", "-b", "master");
        await Git(h.Root, "config", "user.name", "Mutation fixture");
        await Git(h.Root, "config", "user.email", "mutation@example.test");
        await File.WriteAllTextAsync(Path.Combine(h.Root, "base.txt"), "master");
        await Git(h.Root, "add", "base.txt");
        await Git(h.Root, "commit", "-m", "base");
        var master = await Git(h.Root, "rev-parse", "HEAD");
        var retained = Path.Combine(h.Root, "worktrees", "card-task-aabbccdd");
        await Git(h.Root, "worktree", "add", "-b", "code-a", retained);
        await File.WriteAllTextAsync(Path.Combine(retained, "sentinel.txt"), "unlanded Code");
        await Git(retained, "add", "sentinel.txt");
        await Git(retained, "commit", "-m", "Code A");
        var codeSha = await Git(retained, "rev-parse", "HEAD");
        codeSha.ShouldNotBe(master);
        var oldSession = Guid.NewGuid();
        var oldAgent = Guid.NewGuid();
        await using (var db = h.Context())
        {
            db.AgentSessions.Add(new AgentSession { Id = oldSession, DefinitionName = "claude", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running, Cwd = retained, Cols = 120, Rows = 30, CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
            db.Agents.Add(new Agent { Id = oldAgent, Name = "warm-code", Slug = "warm-code", WorkingDirectory = retained,
                Details = "Code A", Kind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Frontier,
                IsPoolDelegate = true, Status = AgentStatus.Idle, PoolIdleSince = DateTime.UtcNow.AddMinutes(-1),
                LaunchEnvJson = "{}", PersistentSessionId = oldSession.ToString(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var code = await h.Seed(AgentTaskRole.Code, AgentTaskStatus.Succeeded, retained);
        await using (var db = h.Context())
        {
            var owner = await db.AgentTasks.SingleAsync(t => t.Id == code.Id);
            owner.Workspace = WorkspaceMode.Worktree;
            owner.WorktreePath = retained;
            owner.WorktreeBranch = "code-a";
            owner.AgentId = oldAgent;
            owner.AgentSessionId = oldSession;
            owner.CompletedAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var mutation = await h.Seed(AgentTaskRole.Mutation, AgentTaskStatus.Queued, retained);
        await h.Tick();
        await using var verify = h.Context();
        var launched = await verify.AgentTasks.SingleAsync(t => t.Id == mutation.Id);
        launched.Status.ShouldBe(AgentTaskStatus.Dispatched);
        launched.AgentId.ShouldNotBe(oldAgent);
        launched.AgentSessionId.ShouldNotBe(oldSession);
        h.Factory.Adapters.ShouldHaveSingleItem().Started.ShouldBeTrue();
        var args = h.Factory.Adapters.Single().StartedArgs;
        var append = args.ToList().IndexOf("--append-system-prompt");
        append.ShouldBeGreaterThanOrEqualTo(0);
        args[append + 1].ShouldStartWith("[bundle:stage-mutation v");
        (await verify.AgentSessions.SingleAsync(s => s.Id == launched.AgentSessionId)).Cwd.ShouldBe(retained);
        (await verify.AgentTasks.SingleAsync(t => t.Id == code.Id)).WorktreePath.ShouldBe(retained);
        (await Git(retained, "rev-parse", "HEAD")).ShouldBe(codeSha);
        File.Exists(Path.Combine(retained, "sentinel.txt")).ShouldBeTrue();
        (await Git(retained, "diff", "--name-only")).ShouldBeEmpty();
        (await Git(retained, "diff", "--cached", "--name-only")).ShouldBeEmpty();
        (await Git(h.Root, "worktree", "list", "--porcelain")).Split("worktree ").Length.ShouldBe(3);
    }

    [Test]
    public async Task C470_process_cap_includes_mutation()
    {
        await using var h = await Harness.CreateAsync(cap: 1);
        var mutation = await h.Seed(AgentTaskRole.Mutation, AgentTaskStatus.Working, h.Root);
        var other = Directory.CreateDirectory(Path.Combine(h.Root, "other")).FullName;
        var code = await h.Seed(AgentTaskRole.Code, AgentTaskStatus.Queued, other);
        await h.Tick();
        await using (var db = h.Context())
            (await db.AgentTasks.SingleAsync(t => t.Id == code.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        h.Factory.Adapters.ShouldBeEmpty();
        await h.Settle(mutation.Id);
        await h.Tick();
        await using var verify = h.Context();
        (await verify.AgentTasks.SingleAsync(t => t.Id == code.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        h.Factory.Adapters.ShouldHaveSingleItem().Started.ShouldBeTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C470_mutation_obeys_writer_lease(bool mutationHolds)
    {
        await using var h = await Harness.CreateAsync();
        var holder = await h.Seed(mutationHolds ? AgentTaskRole.Mutation : AgentTaskRole.Debug, AgentTaskStatus.Working, h.Root);
        var waiting = await h.Seed(mutationHolds ? AgentTaskRole.Debug : AgentTaskRole.Mutation, AgentTaskStatus.Queued, h.Root);
        for (var i = 0; i < 3; i++) await h.Tick();
        await using (var db = h.Context())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == waiting.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
            var held = await db.AgentTaskEvents.Where(e => e.AgentTaskId == waiting.Id && e.Type == AgentTaskEventType.Held).ToListAsync();
            held.ShouldHaveSingleItem().Detail.ShouldContain(DelegationReportFormatter.Short(holder.Id));
        }
        h.Factory.Adapters.ShouldBeEmpty();
        await h.Settle(holder.Id);
        await h.Tick();
        await using var verify = h.Context();
        (await verify.AgentTasks.SingleAsync(t => t.Id == waiting.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        h.Factory.Adapters.ShouldHaveSingleItem().Started.ShouldBeTrue();
    }

    private sealed class CaptureFactory : IAgentProtocolAdapterFactory
    {
        public List<FakeAgentProtocolAdapter> Adapters { get; } = [];
        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            var adapter = new FakeAgentProtocolAdapter();
            Adapters.Add(adapter);
            return adapter;
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required BridgeQueueHarness Bridge { get; init; }
        public required CaptureFactory Factory { get; init; }
        public string Root => Bridge.TempRoot;
        public AppDbContext Context() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));
        public static async Task<Harness> CreateAsync(int cap = 6)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var factory = new CaptureFactory();
            var bridge = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString,
                Delegation = new DelegationSettings { MaxConcurrentTasks = cap, SerialiseSharedWriters = true },
                ConfigureServices = services =>
                {
                    services.AddSingleton<IAgentProtocolAdapterFactory>(factory);
                    services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new()
                    {
                        DefaultDefinition = "claude", Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "fixture-only" } }
                    }));
                    services.RemoveAll<IWorktreeManager>();
                    services.AddDelegationWorktreeGraph();
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
                    services.AddScoped<AgentTaskService>();
                    services.AddSingleton<AgentTaskReplyService>();
                    services.AddScoped<AgentTaskDispatcher>();
                }
            });
            return new() { Schema = schema, Bridge = bridge, Factory = factory };
        }
        public async Task<AgentTask> Seed(AgentTaskRole role, AgentTaskStatus status, string directory)
        {
            var id = Guid.NewGuid();
            var task = new AgentTask { Id = id, RootTaskId = id, Role = role, Kind = AgentTaskKind.Worker, Status = status,
                Title = role.ToString(), Goal = "CARD-0470 " + id, WorkingDirectory = directory, RepoPath = directory,
                Workspace = WorkspaceMode.Shared, Scope = "server/Application/**", AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier, Ephemeral = true, CreatedAt = DateTime.UtcNow };
            await using var db = Context();
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }
        public async Task Tick()
        {
            using var scope = Bridge.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(default);
            await Bridge.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
        }
        public async Task Settle(Guid id)
        {
            await using var db = Context();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == id);
            task.Status = AgentTaskStatus.Succeeded;
            task.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync()
        {
            await Bridge.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            foreach (var adapter in Factory.Adapters) await adapter.KillAsync(TimeSpan.FromSeconds(1), default);
            await Bridge.DisposeAsync();
            await Schema.DisposeAsync();
        }
    }

    private static async Task<string> Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await error);
        return (await output).Trim();
    }
}
