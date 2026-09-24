using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-0604 D-15, G-18/G-19/G-20. A runner-bound task is admitted in exactly one shape, and a
// runner that cannot take it leaves the task Queued rather than running it on the desktop under a
// routing decision the operator made deliberately.
[Category("Unit")]
public sealed class PhoneHomeTaskRoutingTests
{
    [Test]
    public void Shared_workspace_is_refused()
    {
        // G-18. A Shared-workspace remote task has no canonical desktop worktree to fast-forward
        // and no branch to exchange: the design has nothing to sync, so it is refused at the
        // policy, not discovered at settlement.
        var policy = Policy();
        var agent = PoolAgent();
        Should.Throw<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
                policy.RefuseUnsupportedStart(agent, false, delegatedTask: true, worktree: false, false, false,
                    SessionBackend.PtyHost, AgentKind.Grok, null))
            .Code.ShouldBe("phone_home_worktree_refused");
    }

    [Test]
    public void Delegated_tasks_are_refused_until_the_runner_is_a_pool()
    {
        // The same task shape against a runner whose AllowDelegatedTasks is off.
        var policy = new PhoneHomeLaunchPolicy(Options.Create(Settings(allowDelegatedTasks: false)));
        Should.Throw<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
                policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true, worktree: true, false, false,
                    SessionBackend.PtyHost, AgentKind.Grok, null))
            .Code.ShouldBe("phone_home_task_refused");
        policy.AllowDelegatedTasks.ShouldBeFalse();
    }

    [Test]
    public void Only_grok_and_claude_run_a_runner_bound_task()
    {
        var policy = Policy();
        foreach (var kind in new[] { AgentKind.Codex, AgentKind.OpenCode, AgentKind.Raw })
        {
            Should.Throw<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
                    policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true, worktree: true, false, false,
                        SessionBackend.PtyHost, kind, null))
                .Code.ShouldBe("phone_home_kind_refused");
        }

        foreach (var kind in new[] { AgentKind.Grok, AgentKind.ClaudeCode })
            policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true, worktree: true, false, false,
                SessionBackend.PtyHost, kind, null);
    }

    [Test]
    public void Task_session_projects_claude_exe_and_config_dir()
    {
        var spec = new Antiphon.Server.Application.Dtos.AgentLaunchSpec(
            "claude", AgentKind.ClaudeCode, DesktopExe(@"C:\Users\x\.local\bin\claude.exe"), [],
            new Dictionary<string, string>
            {
                ["DISABLE_AUTOUPDATER"] = "1",
                ["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"] = "1",
            }, @"C:\Antiphon\worktrees\card-task-deadbeef", 80, 24);
        var projected = Policy().Project(spec, PoolAgent(), "/work/worktrees/task-deadbeef");
        projected.Exe.ShouldBe("claude");
        projected.Cwd.ShouldBe("/work/worktrees/task-deadbeef");
        projected.Env["CLAUDE_CONFIG_DIR"].ShouldBe("/state/claude");
        projected.Env["GROK_HOME"].ShouldBe("/state/grok");
        projected.Env["ANTIPHON_API"].ShouldBe("https://antiphon.desktop.codeperf.net");
        projected.Env["DISABLE_AUTOUPDATER"].ShouldBe("1");
        projected.Env["CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN"].ShouldBe("1");
    }

    [Test]
    public void Runner_bound_claude_launch_refuses_anthropic_credential_names()
    {
        foreach (var name in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN",
            "CLAUDE_CODE_OAUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", "CCR_OAUTH_TOKEN_FILE" })
        {
            var spec = new Antiphon.Server.Application.Dtos.AgentLaunchSpec(
                "claude", AgentKind.ClaudeCode, "claude.exe", [],
                new Dictionary<string, string> { [name] = "secret-sentinel" }, "/work", 80, 24);
            var exception = Should.Throw<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
                Policy().Project(spec, PoolAgent()));
            exception.Code.ShouldBe("phone_home_env_refused");
            exception.Message.ShouldContain(name);
            exception.Message.ShouldNotContain("secret-sentinel");
            Policy().Project(spec with { Kind = AgentKind.Grok, Exe = "grok.exe" }, PoolAgent())
                .Env[name].ShouldBe("secret-sentinel");
        }
    }

    [Test]
    public void Task_session_projects_into_the_mirror_not_the_workspace_root()
    {
        // G-20's other half: the session's runner cwd is the MIRROR, and the desktop path is what
        // the spec carried in. The two never collapse into one.
        var policy = Policy();
        var agent = PoolAgent();
        var spec = new Antiphon.Server.Application.Dtos.AgentLaunchSpec(
            "grok", AgentKind.Grok, DesktopExe(@"C:\tools\grok.exe"), [], new Dictionary<string, string>(),
            @"C:\Antiphon\worktrees\card-task-deadbeef", 80, 24);

        var projected = policy.Project(spec, agent, "/work/worktrees/task-deadbeef");
        projected.Cwd.ShouldBe("/work/worktrees/task-deadbeef");
        projected.Exe.ShouldBe("grok");
        projected.Env["ANTIPHON_API"].ShouldBe("https://antiphon.desktop.codeperf.net");
        projected.Env["GROK_HOME"].ShouldBe("/state/grok");
        projected.VerificationBinding.ShouldBeNull("Cut A never sends a binding to the runner");

        // Without a mirror the projection falls back to the workspace root, which is the named
        // agent's shape - never a Windows path.
        policy.Project(spec, agent).Cwd.ShouldBe("/work");
    }

    [Test]
    public void An_agent_on_another_runner_is_not_runner_bound()
    {
        // G-19's precondition: only the one configured runner id makes an agent remote. A row
        // naming some other runner is not silently adopted by this policy.
        var policy = Policy();
        policy.IsRunnerBound("server2").ShouldBeTrue();
        policy.IsRunnerBound("grok-linux").ShouldBeFalse();
        policy.IsRunnerBound((string?)null).ShouldBeFalse();
        policy.IsRunnerBound(new Agent { Id = Guid.NewGuid(), RunnerId = "somewhere-else" }).ShouldBeFalse();
        policy.IsRunnerBound(new Agent { Id = Guid.NewGuid(), RunnerId = "server2" }).ShouldBeTrue();

        var disabled = new PhoneHomeLaunchPolicy(Options.Create(Settings(allowDelegatedTasks: true, enabled: false)));
        disabled.IsRunnerBound("server2").ShouldBeFalse("a disabled runner binds nothing");
    }

    [Test]
    public void Source_landing_task_is_admitted_after_cut_b()
    {
        // Cut B (D-19) taught the runner to hold a binding, and create admits a runner-bound
        // SourceLanding Mutation after asking that runner for custody. CARD-0659: the launch gate
        // must agree, or every admitted remote Mutation fails at dispatch.
        Should.NotThrow(() =>
            Policy().RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true, worktree: true,
                sourceLanding: true, false, SessionBackend.PtyHost, AgentKind.Grok, null));
    }

    private static Agent PoolAgent() => new()
    {
        Id = Guid.NewGuid(),
        RunnerId = "server2",
        IsPoolDelegate = true,
        WorkingDirectory = @"C:\Antiphon\worktrees\card-task-deadbeef",
    };

    /// <summary>CARD-0681: the spec's Exe is a desktop-host path, and the projection reads its file
    /// name with the host's Path. Off Windows a backslash is not a separator, so the Windows fixture
    /// is given in its host (Unix) form there; on Windows it is used unchanged.</summary>
    private static string DesktopExe(string windowsPath) => OperatingSystem.IsWindows()
        ? windowsPath
        : windowsPath.Replace(@"C:\", "/", StringComparison.Ordinal).Replace('\\', '/');

    private static PhoneHomeLaunchPolicy Policy() => new(Options.Create(Settings(allowDelegatedTasks: true)));

    private static PhoneHomeRunnerSettings Settings(bool allowDelegatedTasks, bool enabled = true) => new()
    {
        Enabled = enabled,
        AllowedRunnerId = "server2",
        AllowDelegatedTasks = allowDelegatedTasks,
        HostWorkspaceRoot = @"C:\src\Antiphon",
        RunnerWorkspace = "/work",
        RunnerRepository = "/work/repos/antiphon",
        ChildGrokHome = "/state/grok",
        CallbackOrigin = "https://antiphon.desktop.codeperf.net",
        SharedSecret = "x",
    };
}
