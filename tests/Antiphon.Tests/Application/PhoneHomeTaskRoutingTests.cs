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
    public void Only_grok_runs_a_runner_bound_task()
    {
        var policy = Policy();
        foreach (var kind in new[] { AgentKind.ClaudeCode, AgentKind.Codex, AgentKind.Raw })
        {
            Should.Throw<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
                    policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true, worktree: true, false, false,
                        SessionBackend.PtyHost, kind, null))
                .Code.ShouldBe("phone_home_kind_refused");
        }

        // Grok is admitted, and is the only kind the image carries.
        policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true, worktree: true, false, false,
            SessionBackend.PtyHost, AgentKind.Grok, null);
    }

    [Test]
    public void Task_session_projects_into_the_mirror_not_the_workspace_root()
    {
        // G-20's other half: the session's runner cwd is the MIRROR, and the desktop path is what
        // the spec carried in. The two never collapse into one.
        var policy = Policy();
        var agent = PoolAgent();
        var spec = new Antiphon.Server.Application.Dtos.AgentLaunchSpec(
            @"C:\tools\grok.exe", AgentKind.Grok, "grok", [], new Dictionary<string, string>(),
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
    public void Source_landing_is_refused_until_cut_b()
    {
        // Cut B (D-19) is what teaches the runner to hold a binding; until then a tracked task
        // there would have no receipt to seal.
        Should.Throw<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
                Policy().RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true, worktree: true,
                    sourceLanding: true, false, SessionBackend.PtyHost, AgentKind.Grok, null))
            .Code.ShouldBe("phone_home_sourcelanding_refused");
    }

    private static Agent PoolAgent() => new()
    {
        Id = Guid.NewGuid(),
        RunnerId = "server2",
        IsPoolDelegate = true,
        WorkingDirectory = @"C:\Antiphon\worktrees\card-task-deadbeef",
    };

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
