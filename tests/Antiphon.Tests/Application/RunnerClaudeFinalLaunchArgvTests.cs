using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The queued phone-home spec is projected before <c>ApplyOff</c>. The argv Claude actually
/// receives is the one <see cref="AgentSessionService"/> builds afterwards.
/// </summary>
[Category("Integration")]
public sealed class RunnerClaudeFinalLaunchArgvTests
{
    [Test]
    public async Task Runner_bound_claude_final_argv_has_no_windows_path()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var now = DateTime.UtcNow;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "runner-claude-" + Guid.NewGuid().ToString("N")[..8],
            Slug = "runner-claude-" + Guid.NewGuid().ToString("N")[..8],
            WorkingDirectory = @"C:\src\Antiphon",
            Kind = AgentKind.ClaudeCode,
            IsPoolDelegate = true,
            SessionBackend = SessionBackend.PtyHost,
            Status = AgentStatus.Idle,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            StandingAgentId = agent.Id,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            SessionBackend = SessionBackend.PtyHost,
            Status = SessionStatus.Starting,
            Cwd = @"C:\src\Antiphon\worktrees\card-task-remote",
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            RunnerId = "server2",
            RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work/worktrees/task-remote",
        };
        db.Agents.Add(agent);
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        var stored = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);

        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        var service = BuildService(db, adapter);
        var projected = new AgentLaunchSpec(
            "claude",
            AgentKind.ClaudeCode,
            "claude",
            ["--dangerously-skip-permissions"],
            new Dictionary<string, string>
            {
                ["GROK_HOME"] = "/state/grok",
                ["CLAUDE_CONFIG_DIR"] = "/state/claude",
                ["ANTIPHON_API"] = "https://antiphon.desktop.codeperf.net",
                ["DISABLE_AUTOUPDATER"] = "1",
                ["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "1",
            },
            "/work/worktrees/task-remote",
            120,
            30);

        var launch = await Should.ThrowAsync<InvalidOperationException>(() => service.LaunchInteractiveAsync(
            session.Id,
            agent.Id,
            projected,
            remoteControlName: null,
            resume: false,
            notes: null,
            CancellationToken.None,
            initialPrompt: null,
            acceptedGeneration: stored.StartedAt));

        launch.Message.ShouldContain(AgentSessionService.NotReadyBase);
        adapter.Started.ShouldBeTrue();
        adapter.StartedExe.ShouldBe("claude");
        adapter.StartedCwd.ShouldBe("/work/worktrees/task-remote");
        adapter.StartedArgs.ShouldContain("--session-id");
        adapter.StartedArgs.ShouldContain(session.Id.ToString("D"));
        var settingsAt = adapter.StartedArgs.ToList().IndexOf(ClaudeRemoteControlLaunchArgs.SettingsFlag);
        settingsAt.ShouldBeGreaterThanOrEqualTo(0);
        adapter.StartedArgs.Count(a => a == ClaudeRemoteControlLaunchArgs.SettingsFlag).ShouldBe(1);
        adapter.StartedArgs[settingsAt + 1].ShouldBe(ClaudeRemoteControlLaunchArgs.RunnerOffSettingsPath);

        AssertNoWindowsPath("cwd", adapter.StartedCwd!);
        AssertNoWindowsPath("exe", adapter.StartedExe!);
        foreach (var arg in adapter.StartedArgs)
            AssertNoWindowsPath("argv", arg);
        foreach (var pair in adapter.StartedEnv)
        {
            AssertNoWindowsPath("env name", pair.Key);
            AssertNoWindowsPath("env value", pair.Value);
        }
    }

    private static void AssertNoWindowsPath(string label, string value)
    {
        value.Contains('\\').ShouldBeFalse(label + " contains a backslash: " + value);
        Regex.IsMatch(value, @"(^|[^A-Za-z0-9])[A-Za-z]:[\\/]")
            .ShouldBeFalse(label + " contains a drive path: " + value);
    }

    private static AgentSessionService BuildService(AppDbContext db, FakeAgentProtocolAdapter adapter)
    {
        var scopes = new NullScopeFactory();
        var bus = new MockEventBus();
        var settings = Options.Create(new AgentSessionSettings());
        var runtime = new AgentSessionRuntime(
            bus, settings, scopes, TimeProvider.System, NullLogger<AgentSessionRuntime>.Instance);
        var hooks = new WorkspaceHookService(
            new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance),
            NullLogger<WorkspaceHookService>.Instance);
        return new AgentSessionService(
            db,
            new UnusedWorktreeManager(),
            hooks,
            new SingleAdapterFactory(adapter),
            runtime,
            bus,
            new SessionMessageQueueService(
                scopes, runtime, bus, TimeProvider.System, NullLogger<SessionMessageQueueService>.Instance),
            scopes,
            settings,
            Options.Create(new SupervisionSettings()),
            TimeProvider.System,
            NullLogger<AgentSessionService>.Instance);
    }

    private sealed class SingleAdapterFactory(FakeAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class NullScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new NullScope();

        private sealed class NullScope : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type serviceType) => null;
            public void Dispose() { }
        }
    }

    private sealed class UnusedWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task TouchAsync(string worktreePath, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<int> PruneStaleAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
