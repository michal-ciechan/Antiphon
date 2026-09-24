using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0660 V-4 (D-3/D-7/D-8). A runner-bound Codex launch projects the desktop's standard Codex
/// executable, in any of its spellings, to the image's native <c>codex</c>, runs in the mirror, and
/// carries the runner's own <c>CODEX_HOME</c>. Credential environment names are refused by name
/// (never echoing a value), and every argv element the desktop built reaches the runner unchanged.
/// Local launches and the pinned Grok agent keep their exact contracts.
/// </summary>
[Category("Unit")]
public sealed class CodexPhoneHomeProjectionTests
{
    private const string Mirror = "/work/worktrees/task-deadbeef";

    [Test]
    public void Standard_exes_project_with_mirror_and_home()
    {
        var standard = new[]
        {
            "codex", "codex.cmd", "codex.exe", "CODEX.CMD",
            @"C:\Users\x\AppData\Roaming\npm\codex.cmd",
            @"C:\Program Files\nodejs\codex.exe",
            "/usr/local/bin/codex",
        };
        foreach (var exe in standard)
        {
            var spec = CodexSpec(exe, new Dictionary<string, string>
            {
                // A desktop home in the spec must never reach the runner, in any spelling.
                ["CODEX_HOME"] = @"C:\Users\x\.codex",
                ["codex_home"] = @"C:\Users\x\.codex",
                ["RUST_LOG"] = "warn",
            });

            var projected = Policy().Project(spec, PoolAgent(), Mirror);

            projected.Exe.ShouldBe("codex", exe);
            projected.Kind.ShouldBe(AgentKind.Codex, exe);
            projected.Cwd.ShouldBe(Mirror, exe);
            projected.Env["CODEX_HOME"].ShouldBe("/state/codex", exe);
            projected.Env.Keys.Count(key => string.Equals(key, "CODEX_HOME", StringComparison.OrdinalIgnoreCase))
                .ShouldBe(1, exe + ": exactly one CODEX_HOME, the runner's");
            projected.Env["RUST_LOG"].ShouldBe("warn", exe);
            projected.Env["ANTIPHON_API"].ShouldBe("https://antiphon.desktop.codeperf.net", exe);
            projected.MemoryLimitMb.ShouldBe(0, exe);
            projected.Backend.ShouldBe(SessionBackend.PtyHost, exe);
        }

        // The configured runner home is what is projected, not a constant.
        var custom = new PhoneHomeLaunchPolicy(Options.Create(Settings(s => s.ChildCodexHome = "/state/codex-b")));
        custom.Project(CodexSpec("codex.cmd"), PoolAgent(), Mirror).Env["CODEX_HOME"].ShouldBe("/state/codex-b");

        // Without a mirror a pool agent falls back to the runner workspace, never a Windows path.
        Policy().Project(CodexSpec("codex.cmd"), PoolAgent()).Cwd.ShouldBe("/work");

        // Look-alikes are host programs the image does not own.
        foreach (var lookalike in new[]
                 {
                     "codex-wrapper.cmd", "mycodex.exe", @"C:\tools\codex.bat", "codex.ps1",
                     "./codex/", "/opt/codex/0.156.1/package/vendor/codex-helper",
                 })
        {
            Should.Throw<ConflictException>(() => Policy().Project(CodexSpec(lookalike), PoolAgent(), Mirror))
                .Code.ShouldBe("phone_home_wrapper_refused", lookalike);
        }
    }

    [Test]
    public void Credential_names_are_refused_without_values()
    {
        var names = new[]
        {
            "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN",
            "openai_api_key", "Codex_Api_Key", "codex_access_token",
        };
        foreach (var name in names)
        {
            foreach (var value in new[] { "sk-sentinel-4471", "" })
            {
                var spec = CodexSpec("codex.cmd", new Dictionary<string, string> { [name] = value });

                var refused = Should.Throw<ConflictException>(() => Policy().Project(spec, PoolAgent(), Mirror));

                refused.Code.ShouldBe("phone_home_env_refused", name);
                refused.Message.ShouldContain(name.ToUpperInvariant(), Case.Sensitive, name);
                refused.Message.ShouldNotContain("sk-sentinel-4471");

                // The refusal is Codex's: a local Codex launch and a runner-bound Grok launch are
                // not this rule's business and keep the name as they were given it.
                Policy().Project(spec, LocalAgent(), Mirror).Env[name].ShouldBe(value, name);
                Policy().Project(spec with { Kind = AgentKind.Grok, Exe = "grok.exe" }, PoolAgent(), Mirror)
                    .Env[name].ShouldBe(value, name);
            }
        }
    }

    [Test]
    public void Local_and_pinned_grok_contracts_are_unchanged()
    {
        // A desktop Codex launch is not projected at all: same exe, cwd, env and home.
        var local = CodexSpec("codex.cmd", new Dictionary<string, string> { ["CODEX_HOME"] = @"C:\Users\x\.codex" });
        Policy().Project(local, LocalAgent(), Mirror).ShouldBeSameAs(local);

        // The pinned CARD-0490 agent is still one Grok session: Codex is neither projected nor admitted.
        var policy = Policy();
        var pinned = PinnedAgent();
        Should.Throw<ConflictException>(() => policy.Project(CodexSpec("codex.cmd"), pinned))
            .Code.ShouldBe("phone_home_wrapper_refused");
        Should.Throw<ConflictException>(() => policy.RefuseUnsupportedStart(pinned, false, false, false, false, false,
                SessionBackend.PtyHost, AgentKind.Codex, null))
            .Code.ShouldBe("phone_home_kind_refused");

        var grok = new AgentLaunchSpec("grok", AgentKind.Grok, "grok.exe", ["--always-approve", "--no-alt-screen"],
            new Dictionary<string, string>(), @"C:\src\Antiphon", 80, 24);
        var pinnedGrok = policy.Project(grok, pinned);
        pinnedGrok.Exe.ShouldBe("grok");
        pinnedGrok.Cwd.ShouldBe("/work");
        pinnedGrok.Env.Keys.OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(["ANTIPHON_API", "CLAUDE_CONFIG_DIR", "GROK_HOME"]);

        // Pool Grok and Claude launches gain no Codex home either.
        policy.Project(grok, PoolAgent(), Mirror).Env.ContainsKey("CODEX_HOME").ShouldBeFalse();
        var claude = new AgentLaunchSpec("claude", AgentKind.ClaudeCode, "claude.exe", [],
            new Dictionary<string, string>(), @"C:\src\Antiphon", 80, 24);
        policy.Project(claude, PoolAgent(), Mirror).Env.ContainsKey("CODEX_HOME").ShouldBeFalse();
    }

    [Test]
    public void Arguments_survive_projection_byte_for_byte()
    {
        var instructions = "# Antiphon delegate\n\tTabbed \"quoted\" `ticks` C:\\src\\Antiphon\\path\n"
            + "unicode: \u00e9\u4e2d\u2014 end\r\ntrailing space ";
        var args = new List<string>
        {
            "--no-alt-screen",
            "--dangerously-bypass-approvals-and-sandbox",
            "-m", "gpt-6-sol",
            CodexLaunchArgs.ConfigFlag, CodexLaunchArgs.ReasoningEffortOverride(AgentModelLevel.High),
            CodexLaunchArgs.ConfigFlag, CodexLaunchArgs.DisablePasteBurst,
            CodexLaunchArgs.ConfigFlag, CodexLaunchArgs.DeveloperInstructions(instructions),
        };
        var cold = new AgentLaunchSpec("codex", AgentKind.Codex, @"C:\Users\x\AppData\Roaming\npm\codex.cmd",
            args.ToArray(), new Dictionary<string, string>(), @"C:\Antiphon\worktrees\card-task-deadbeef", 120, 40,
            SessionId: Guid.NewGuid());

        var projected = Policy().Project(cold, PoolAgent(), Mirror);

        projected.Args.ShouldBe(args);
        projected.Args[^1].ShouldBe("developer_instructions=" + instructions, "one argv element, no quoting of ours");
        projected.Args.Count(a => a.StartsWith("developer_instructions=", StringComparison.Ordinal)).ShouldBe(1);
        projected.SessionId.ShouldBe(cold.SessionId);
        projected.Cols.ShouldBe(120);
        projected.Rows.ShouldBe(40);

        // A relaunch projects the same desktop spec again and gets the same launch; projecting an
        // already-projected spec is a no-op on everything the runner sees.
        var relaunch = Policy().Project(cold with { SessionId = Guid.NewGuid() }, PoolAgent(), Mirror);
        relaunch.Args.ShouldBe(args);
        relaunch.Exe.ShouldBe("codex");
        relaunch.Env["CODEX_HOME"].ShouldBe("/state/codex");
        var again = Policy().Project(projected, PoolAgent(), Mirror);
        again.Args.ShouldBe(args);
        again.Exe.ShouldBe("codex");
        again.Env.OrderBy(p => p.Key, StringComparer.Ordinal).ShouldBe(projected.Env.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Test]
    public void Runner_gate_admits_codex_worker_tasks_only()
    {
        var policy = Policy();

        // A runner-bound delegated Worktree task may now be Codex, like Grok and Claude Code.
        foreach (var kind in new[] { AgentKind.Codex, AgentKind.Grok, AgentKind.ClaudeCode })
        {
            Should.NotThrow(() => policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true, worktree: true,
                sourceLanding: false, false, SessionBackend.PtyHost, kind, null), kind.ToString());
        }

        // Every other rule still holds for it.
        Should.Throw<ConflictException>(() => policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true,
                worktree: true, false, false, SessionBackend.PtyHost, AgentKind.Codex, @"C:\tools\codex-wrap.cmd"))
            .Code.ShouldBe("phone_home_wrapper_refused");
        Should.Throw<ConflictException>(() => policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true,
                worktree: false, false, false, SessionBackend.PtyHost, AgentKind.Codex, null))
            .Code.ShouldBe("phone_home_worktree_refused");
        Should.Throw<ConflictException>(() => policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true,
                worktree: true, false, false, SessionBackend.Herdr, AgentKind.Codex, null))
            .Code.ShouldBe("phone_home_backend_refused");

        // Not a runner SourceLanding custody kind, not a named runner agent, and OpenCode stays out.
        Should.Throw<ConflictException>(() => policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true,
                worktree: true, sourceLanding: true, false, SessionBackend.PtyHost, AgentKind.Codex, null))
            .Code.ShouldBe("phone_home_kind_refused");
        var named = new Agent { Id = Guid.NewGuid(), RunnerId = "server2", WorkingDirectory = @"C:\src\Antiphon" };
        Should.Throw<ConflictException>(() => policy.RefuseUnsupportedStart(named, false, delegatedTask: false,
                worktree: false, false, false, SessionBackend.PtyHost, AgentKind.Codex, null))
            .Code.ShouldBe("phone_home_kind_refused");
        Should.Throw<ConflictException>(() => policy.RefuseUnsupportedStart(PoolAgent(), false, delegatedTask: true,
                worktree: true, false, false, SessionBackend.PtyHost, AgentKind.OpenCode, null))
            .Code.ShouldBe("phone_home_kind_refused");
    }

    private static AgentLaunchSpec CodexSpec(string exe, Dictionary<string, string>? env = null) =>
        new("codex", AgentKind.Codex, exe, ["--no-alt-screen", "--dangerously-bypass-approvals-and-sandbox"],
            env ?? new Dictionary<string, string>(), @"C:\Antiphon\worktrees\card-task-deadbeef", 80, 24);

    private static Agent PoolAgent() => new()
    {
        Id = Guid.NewGuid(),
        RunnerId = "server2",
        IsPoolDelegate = true,
        WorkingDirectory = @"C:\Antiphon\worktrees\card-task-deadbeef",
    };

    private static Agent LocalAgent() => new()
    {
        Id = Guid.NewGuid(),
        IsPoolDelegate = true,
        WorkingDirectory = @"C:\Antiphon\worktrees\card-task-deadbeef",
    };

    private static readonly Guid PinnedId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static Agent PinnedAgent() => new()
    {
        Id = PinnedId,
        RunnerId = "server2",
        WorkingDirectory = @"C:\src\Antiphon",
    };

    private static PhoneHomeLaunchPolicy Policy() => new(Options.Create(Settings(_ => { })));

    private static PhoneHomeRunnerSettings Settings(Action<PhoneHomeRunnerSettings> mutate)
    {
        var settings = new PhoneHomeRunnerSettings
        {
            Enabled = true,
            AllowedRunnerId = "server2",
            AllowDelegatedTasks = true,
            StandingAgentId = PinnedId,
            HostWorkspaceRoot = @"C:\src\Antiphon",
            RunnerWorkspace = "/work",
            RunnerRepository = "/work/repos/antiphon",
            ChildGrokHome = "/state/grok",
            CallbackOrigin = "https://antiphon.desktop.codeperf.net",
            SharedSecret = "x",
        };
        mutate(settings);
        return settings;
    }
}
