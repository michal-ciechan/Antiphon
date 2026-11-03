using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// A native Windows phone-home runner (a laptop beside server2): the desktop projects a launch onto
/// its drive-rooted workspace and homes, keeps projecting agents to bare names the runner resolves,
/// matches a drive-rooted Raw allow list in either slash spelling, and accepts the runner's
/// slash-normalized verification coordinates under its own roots while still refusing escapes.
/// </summary>
[Category("Unit")]
public sealed class NativeWindowsRunnerProjectionTests
{
    private const string Laptop = "laptop";
    private const string Pwsh = @"C:\Program Files\PowerShell\7\pwsh.exe";

    [Test]
    public void Claude_projects_to_the_bare_name_and_the_windows_homes()
    {
        var spec = new AgentLaunchSpec("claude", AgentKind.ClaudeCode, @"C:\Users\x\.local\bin\claude.exe", ["--verbose"],
            new Dictionary<string, string>(), @"C:\Antiphon\worktrees\card-task-deadbeef", 80, 24);

        var projected = Policy().Project(spec, Agent(), "C:/Antiphon/runner/work/worktrees/task-deadbeef");

        projected.Exe.ShouldBe("claude");
        projected.Cwd.ShouldBe("C:/Antiphon/runner/work/worktrees/task-deadbeef");
        projected.Env["CLAUDE_CONFIG_DIR"].ShouldBe(@"C:\Users\me\.claude");
        projected.Env["GROK_HOME"].ShouldBe(@"C:\Users\me\.grok");
        projected.Env["ANTIPHON_API"].ShouldBe("https://antiphon.desktop.codeperf.net");
        projected.Args.ShouldBe(["--verbose"]);
    }

    [Test]
    public void Raw_exe_matches_a_drive_rooted_allow_list_in_either_spelling()
    {
        foreach (var exe in new[] { Pwsh, "C:/Program Files/PowerShell/7/pwsh.exe", @"c:\program files\powershell\7\PWSH.EXE" })
        {
            var spec = new AgentLaunchSpec("raw", AgentKind.Raw, exe, [], new Dictionary<string, string>(),
                @"C:\src\Antiphon", 80, 24);
            Policy().Project(spec, Agent()).Exe.ShouldBe(Pwsh, exe);
            Policy().Project(spec, Agent()).Cwd.ShouldBe("C:/Antiphon/runner/work", exe);
        }

        foreach (var exe in new[] { @"C:\Program Files\PowerShell\7\pwsh.exe.bak", @"D:\Program Files\PowerShell\7\pwsh.exe", "/usr/local/bin/pwsh" })
        {
            var spec = new AgentLaunchSpec("raw", AgentKind.Raw, exe, [], new Dictionary<string, string>(),
                @"C:\src\Antiphon", 80, 24);
            Should.Throw<ConflictException>(() => Policy().Project(spec, Agent()))
                .Code.ShouldBe("phone_home_wrapper_refused", exe);
        }
    }

    [Test]
    public void Verification_coordinates_root_under_a_windows_workspace()
    {
        const string workspace = @"C:\Antiphon\runner\work";
        const string worktrees = workspace + "/worktrees";

        RemoteVerificationWorkspace.RequirePrefix("C:/Antiphon/runner/work/worktrees/task-1", worktrees, allowExact: false);
        RemoteVerificationWorkspace.RequirePrefix("c:/antiphon/runner/work/worktrees/task-1", worktrees, allowExact: false);
        RemoteVerificationWorkspace.RequirePrefix("C:/Antiphon/runner/work/repos/antiphon",
            @"C:\Antiphon\runner\work\repos\antiphon", allowExact: true);

        foreach (var outside in new[]
                 {
                     "C:/Antiphon/runner/work/worktrees", @"C:\Antiphon\runner\work\worktrees\task-1",
                     "C:/Antiphon/runner/work/worktrees/../../x", "C:/Antiphon/runner/workspace/worktrees/task-1",
                     "D:/Antiphon/runner/work/worktrees/task-1", "/work/worktrees/task-1", "",
                 })
        {
            Should.Throw<ConflictException>(() => RemoteVerificationWorkspace.RequirePrefix(outside, worktrees, allowExact: false))
                .Message.ShouldContain("verification_creation_outside_runner_repository", Case.Sensitive, outside);
        }

        // The Linux runner's rule is unchanged: POSIX, case-sensitive.
        RemoteVerificationWorkspace.RequirePrefix("/work/worktrees/task-1", "/work/worktrees", allowExact: false);
        Should.Throw<ConflictException>(() =>
            RemoteVerificationWorkspace.RequirePrefix("/Work/worktrees/task-1", "/work/worktrees", allowExact: false));
        Should.Throw<ConflictException>(() =>
            RemoteVerificationWorkspace.RequirePrefix("C:/work/worktrees/task-1", "/work/worktrees", allowExact: false));
    }

    private static Agent Agent() => new()
    {
        Id = Guid.NewGuid(),
        Name = "laptop-raw",
        RunnerId = Laptop,
        WorkingDirectory = @"C:\src\Antiphon",
    };

    private static PhoneHomeLaunchPolicy Policy() => new(Options.Create(new PhoneHomeRunnerSettings
    {
        Enabled = true,
        Runners = new(StringComparer.OrdinalIgnoreCase)
        {
            [Laptop] = new PhoneHomeRunnerEntry
            {
                Enabled = true,
                DisplayName = "Laptop",
                AllowDelegatedTasks = true,
                HostWorkspaceRoot = @"C:\src\Antiphon",
                RunnerWorkspace = "C:/Antiphon/runner/work",
                RunnerRepository = "C:/Antiphon/runner/work/repos/antiphon",
                RawExeAllowList = [Pwsh],
                ChildGrokHome = @"C:\Users\me\.grok",
                ChildClaudeHome = @"C:\Users\me\.claude",
                ChildCodexHome = @"C:\Users\me\.codex",
                MaxCapacity = 2,
                CallbackOrigin = "https://antiphon.desktop.codeperf.net",
                SharedSecret = "laptop-secret",
            },
        },
    }));
}
