using System.Diagnostics;
using System.Text.Json;
using Antiphon.TestSupport.LiveClaude;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using Antiphon.Agents.Pty.Tests;

namespace Antiphon.LiveClaude.Tests;

/// <summary>Diagnostic startup experiment. No native admission or release credit is supplied here.</summary>
[NotInParallel("Headed")]
[Category("Headed")]
[Category("OptIn")]
[Category("HeadedCanary")]
[Explicit]
[ParallelLimiter<ProcessSpawnLimit>]
public partial class ClaudeRemoteControlAtStartupCanaryTests
{
    private static readonly TimeSpan BridgeAppear = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ArmWait = TimeSpan.FromSeconds(45);

    [Test]
    [Category("LiveClaude")]
    public async Task Control_auto_connects_a_bridge_session_before_the_first_user()
    {
        var executable = ClSession.RequireCore(LiveClaudeCore.Control);
        using var profile = ClSession.RequireProfile(executable);
        ClSession.SkipIfNotEligible(executable);
        try
        {
            var experiment = Experiment(executable, profile);
            var startup = SharedStartup();
            var control = await startup.ControlAsync(experiment, () => RunStartupControlAsync(executable, profile, experiment));
            LiveClaudeStartupEvidence.RequireControl(experiment, control);
        }
        catch (LiveClaudeRefusal e) when (LiveClaudeOutcome.SetupStatus(e.Reason) is string setup)
        { throw new SkipTestException(LiveClaudeEvidence.SetupSkip(setup)); }
    }

    [Test]
    [Category("LiveClaude")]
    public async Task Settings_file_prevents_auto_connect_and_keeps_startup_records()
    {
        var executable = ClSession.RequireCore(LiveClaudeCore.Off);
        using var profile = ClSession.RequireProfile(executable);
        ClSession.SkipIfNotEligible(executable);
        try
        {
            var experiment = Experiment(executable, profile);
            var startup = SharedStartup();
            var control = await startup.BeforeDependentAsync(experiment, () => RunStartupControlAsync(executable, profile, experiment), () => { });
            using var sandbox = CanarySandbox.Create(profile, "off");
            await using var runner = new PtyAgentRunner("modern");
            var launch = BuildProfileLaunch(profile, "off", sandbox.SessionId, sandbox.SettingsPath);
            await LaunchUntilReadyAsync(executable, runner, sandbox, launch);
            try
            {
                var observation = await ObserveOffAsync(runner, sandbox, control);
                LiveClaudeStartupEvidence.RequireOff(experiment, control, observation);
                var token = "RC-OFF-" + Guid.NewGuid().ToString("N")[..8];
                await runner.WriteAsync(token);
                (await runner.WaitForScreenAsync(s => ComposerDeliveryEvidence.FragmentIsVisible(s, token), TimeSpan.FromSeconds(8)))
                    .ShouldBeTrue("startup off composer unavailable");
            }
            finally { await ExitAsync(runner); }
        }
        catch (LiveClaudeRefusal e) when (LiveClaudeOutcome.SetupStatus(e.Reason) is string setup)
        { throw new SkipTestException(LiveClaudeEvidence.SetupSkip(setup)); }
    }

    [Test]
    [Category("LiveClaude")]
    public async Task Remote_control_slash_command_still_arms_after_settings_off()
    {
        var executable = ClSession.RequireCore(LiveClaudeCore.Slash);
        using var profile = ClSession.RequireProfile(executable);
        ClSession.SkipIfNotEligible(executable);
        try
        {
            var experiment = Experiment(executable, profile);
            var startup = SharedStartup();
            var control = await startup.BeforeDependentAsync(experiment, () => RunStartupControlAsync(executable, profile, experiment), () => { });
            using var sandbox = CanarySandbox.Create(profile, "slash");
            await using var runner = new PtyAgentRunner("modern");
            var launch = BuildProfileLaunch(profile, "slash", sandbox.SessionId, sandbox.SettingsPath);
            await LaunchUntilReadyAsync(executable, runner, sandbox, launch);
            try
            {
                var before = await ObserveOffAsync(runner, sandbox, control);
                LiveClaudeStartupEvidence.RequireOff(experiment, control, before);
                await SubmitRemoteControlAsync(runner);
                var after = await WaitForOwnedBridgeAsync(runner, profile, sandbox.SessionId, ArmWait);
                LiveClaudeStartupEvidence.RequireSlash(experiment, control, before, after);
            }
            finally { await ExitAsync(runner); }
        }
        catch (LiveClaudeRefusal e) when (LiveClaudeOutcome.SetupStatus(e.Reason) is string setup)
        { throw new SkipTestException(LiveClaudeEvidence.SetupSkip(setup)); }
    }

    private static async Task<StartupControl> RunStartupControlAsync(LiveClaudeExecutable executable,
        LiveClaudeProfile profile, StartupExperiment experiment)
    {
        using var sandbox = CanarySandbox.Create(profile, "control");
        await using var runner = new PtyAgentRunner("modern");
        var launch = BuildProfileLaunch(profile, "control", sandbox.SessionId, sandbox.SettingsPath);
        await LaunchUntilReadyAsync(executable, runner, sandbox, launch);
        var clock = Stopwatch.StartNew();
        try
        {
            var bridge = await WaitForOwnedBridgeAsync(runner, profile, sandbox.SessionId, BridgeAppear);
            var owner = Owner(runner, profile, sandbox.SessionId);
            if (!LiveClaudeStartupEvidence.OwnedBridge(owner, bridge)) throw new LiveClaudeRefusal("premise-absent");
            var control = new StartupControl(experiment, owner, bridge, clock.Elapsed, CountUserPrompts(profile, sandbox.SessionId), ReadLoadedSettings(profile, sandbox.SessionId));
            LiveClaudeStartupEvidence.RequireControl(experiment, control);
            LiveClaudeStartupEvidence.RequireSettings(control, owner, control.Settings);
            return control;
        }
        finally { await ExitAsync(runner); }
    }

    private static StartupExperiment Experiment(LiveClaudeExecutable executable, LiveClaudeProfile profile)
    {
        var context = executable.RequireContext();
        var settings = profile.Project(profile.Arguments("control", Guid.Empty.ToString("D"), null));
        RequireHookConfiguration(profile);
        return new(context.Binding.Run + ":" + context.Binding.Attempt, context.LockOwner,
            context.Image.Path + ":" + context.Image.Sha256, context.Binding.Profile, settings.Digest);
    }

    private static StartupOwner Owner(PtyAgentRunner runner, LiveClaudeProfile profile, string session)
        => new(runner.Pid ?? throw new LiveClaudeRefusal("bridge-unobserved"), runner.StartedAt.ToString("O"), session, profile.Paths.Cwd);

    private static async Task<StartupObservation> ObserveOffAsync(PtyAgentRunner runner, CanarySandbox sandbox, StartupControl control)
    {
        var clock = Stopwatch.StartNew();
        var owner = Owner(runner, sandbox.Profile, sandbox.SessionId);
        return await LiveClaudeStartupEvidence.ObserveOffAsync(owner, control, () => clock.Elapsed,
            () => Task.FromResult(ReadOwnedBridge(runner, sandbox.Profile, sandbox.SessionId)),
            step => Task.Delay(step), () => ReadLoadedSettings(sandbox.Profile, sandbox.SessionId));
    }

    internal static async Task<StartupBridge?> EnsureMenuBridgeAsync(PtyAgentRunner runner, LiveClaudeProfile profile, string session)
    {
        var bridge = await LiveClaudeStartupEvidence.EstablishBridgeAsync(Owner(runner, profile, session),
            () => Task.FromResult(ReadOwnedBridge(runner, profile, session)), async () =>
            {
                await SubmitRemoteControlAsync(runner);
                return await WaitForOwnedBridgeAsync(runner, profile, session, ArmWait);
            });
        LiveClaudeStartupEvidence.RequireBridge(Owner(runner, profile, session), bridge);
        return bridge;
    }

    private static async Task<StartupBridge?> WaitForOwnedBridgeAsync(PtyAgentRunner runner, LiveClaudeProfile profile, string session, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var bridge = ReadOwnedBridge(runner, profile, session);
            if (LiveClaudeStartupEvidence.OwnedBridge(Owner(runner, profile, session), bridge) || clock.Elapsed >= timeout) return bridge;
            await Task.Delay(200);
        }
    }

    private static StartupBridge? ReadOwnedBridge(PtyAgentRunner runner, LiveClaudeProfile profile, string session)
    {
        // Existing profile/path seams remain exercised; neither result can establish bridge ownership.
        _ = ReadSessionFile(profile.Paths, session, profile.Paths.Cwd);
        _ = FindSessionJsonl(profile.Paths, session);
        if (runner.Exited.IsCompleted) return new(Owner(runner, profile, session), null, false);
        return LiveClaudeStartupEvidence.ReadBridge(profile.Paths.ConfigDir, Owner(runner, profile, session));
    }

    private static StartupSettings ReadLoadedSettings(LiveClaudeProfile profile, string session)
    {
        var jsonl = FindSessionJsonl(profile.Paths, session);
        return LiveClaudeStartupEvidence.ReadSettings(MarkerFile(profile), jsonl is null ? [] : ReadTypes(jsonl));
    }

    private static int CountUserPrompts(LiveClaudeProfile profile, string session)
    {
        var jsonl = FindSessionJsonl(profile.Paths, session);
        if (jsonl is null) throw new LiveClaudeRefusal("premise-absent");
        return ReadTypes(jsonl).Count(t => t == "user");
    }

    private static List<string> ReadTypes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var types = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() is { } t) types.Add(t);
        }
        return types;
    }

    private static async Task LaunchUntilReadyAsync(LiveClaudeExecutable executable, PtyAgentRunner runner,
        CanarySandbox sandbox, LiveClaudeProfileSpec launch)
    {
        var (app, launchArgs) = ClSession.BuildCoreLaunch(executable, launch.Args);
        await runner.StartAsync(app, launchArgs, cwd: launch.Cwd, cols: launch.Cols, rows: launch.Rows, env: launch.Env);
        executable.RequireNativeObservation();
        var ready = await new ClaudeReadyDetector().WaitAsync(runner);
        if (!ready) throw new LiveClaudeRefusal("tui-unready");
        var blocking = await ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync(
            _ => Task.FromResult(runner.SnapshotScreen()), async (data, _) => await runner.WriteAsync(data));
        if (blocking.Outcome == ClaudeStartupBlockOutcome.TrustNotCleared) throw new LiveClaudeRefusal("trust-uncleared");
        if (blocking.Outcome == ClaudeStartupBlockOutcome.TrustCleared)
        {
            var settled = await new ClaudeReadyDetector {MinTotalWait=TimeSpan.Zero,MaxWait=TimeSpan.FromSeconds(30)}.WaitAsync(runner);
            if (!settled) throw new LiveClaudeRefusal("tui-unready");
        }
    }

    private static async Task SubmitRemoteControlAsync(PtyAgentRunner runner)
    {
        await runner.WriteAsync("/remote-control");
        (await runner.WaitForScreenAsync(s => ComposerDeliveryEvidence.FragmentIsVisible(s,"/remote-control"),TimeSpan.FromSeconds(8)))
            .ShouldBeTrue("remote control command not ready for Enter");
        await Task.Delay(800);
        await runner.WriteAsync("\r");
        await Task.Delay(500);
        if (runner.SnapshotScreen().Contains("/launch-remote",StringComparison.Ordinal)
            || runner.SnapshotScreen().Contains("/rc-status",StringComparison.Ordinal)
            || ComposerDeliveryEvidence.FragmentIsVisible(runner.SnapshotScreen(),"/remote-control")) await runner.WriteAsync("\r");
    }

    private static async Task ExitAsync(PtyAgentRunner runner)
    {
        try { await runner.SendLineAsync("/exit"); } catch { }
        await Task.WhenAny(runner.Exited,Task.Delay(TimeSpan.FromSeconds(5)));
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }

}
