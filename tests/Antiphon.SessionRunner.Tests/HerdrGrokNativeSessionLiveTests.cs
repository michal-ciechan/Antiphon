using System.Diagnostics;
using Antiphon.Agents.Pty;
using Antiphon.FakeLlmApi;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0383 V4: in-process runner against the operator's live herdr. Does not touch the
/// production runner on 17204.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public class HerdrGrokNativeSessionLiveTests
{
    [Test]
    [Timeout(15_000)]
    public async Task Arm_A_unknown_resume_is_refused_before_allocation(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await HerdrLiveSession.SkipIfNotEligibleAsync();
        var sessionId = Guid.NewGuid();
        var resumeId = Guid.NewGuid();
        var root = CreateRoot();
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Grok = true });
        var herdr = LiveHerdrClient();
        await using var runtime = BuildRuntime(root.Logs, herdr);
        var label = $"card0383-live-a-{sessionId:N}"[..32];
        var sw = Stopwatch.StartNew();
        try
        {
            var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
                runtime.StartAsync(
                    GrokRequest(sessionId, root, stub, ["--resume", resumeId.ToString("D")], label),
                    CancellationToken.None));
            sw.Stop();
            Console.WriteLine($"Arm A elapsedMs={sw.ElapsedMilliseconds} code={ex.Code}");
            ex.Code.ShouldBe(HerdrProblemTypes.GrokNativeSessionMissing);
            sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
            var listed = await herdr.WorkspaceListAsync(CancellationToken.None);
            listed.Any(w => string.Equals(w.Label, label, StringComparison.Ordinal)).ShouldBeFalse();
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Arm_B_create_is_detected_as_grok(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await HerdrLiveSession.SkipIfNotEligibleAsync();
        var sessionId = Guid.NewGuid();
        var root = CreateRoot();
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Grok = true });
        var herdr = LiveHerdrClient();
        await using var runtime = BuildRuntime(root.Logs, herdr);
        var label = $"card0383-live-b-{sessionId:N}"[..32];
        var sw = Stopwatch.StartNew();
        try
        {
            var dto = await runtime.StartAsync(
                GrokRequest(sessionId, root, stub, ["--session-id", sessionId.ToString("D")], label),
                CancellationToken.None);
            sw.Stop();
            Console.WriteLine($"Arm B elapsedMs={sw.ElapsedMilliseconds} status={dto.Status}");
            dto.Status.ShouldBe("Running");
            sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
            GrokNativeSessionStore.Exists(root.GrokHome, sessionId).ShouldBeTrue();
            var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(root.Logs, sessionId));
            sidecar.ShouldNotBeNull();
            sidecar!.Origin.ShouldBe(HerdrPaneOrigins.Launched);

            await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
            var gone = await Should.ThrowAsync<HerdrApiException>(() =>
                herdr.PaneGetAsync(sidecar.PaneId, CancellationToken.None));
            gone.ShouldNotBeNull();
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    [Timeout(90_000)]
    public async Task Arm_C_resume_of_the_created_directory_is_detected(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await HerdrLiveSession.SkipIfNotEligibleAsync();
        var sessionId = Guid.NewGuid();
        var root = CreateRoot();
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Grok = true });
        var herdr = LiveHerdrClient();
        await using var runtime = BuildRuntime(root.Logs, herdr);
        var label = $"card0383-live-c-{sessionId:N}"[..32];
        try
        {
            var created = await runtime.StartAsync(
                GrokRequest(sessionId, root, stub, ["--session-id", sessionId.ToString("D")], label),
                CancellationToken.None);
            created.Status.ShouldBe("Running");
            await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
            GrokNativeSessionStore.Exists(root.GrokHome, sessionId).ShouldBeTrue();

            var sw = Stopwatch.StartNew();
            var dto = await runtime.StartAsync(
                GrokRequest(sessionId, root, stub, ["--resume", sessionId.ToString("D")], label),
                CancellationToken.None);
            sw.Stop();
            Console.WriteLine($"Arm C elapsedMs={sw.ElapsedMilliseconds} status={dto.Status}");
            dto.Status.ShouldBe("Running");
            sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
            var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(root.Logs, sessionId)).ShouldNotBeNull();
            var pane = await herdr.PaneGetAsync(sidecar.PaneId, CancellationToken.None);
            pane.Agent.ShouldBe(HerdrAgentKinds.Grok);
            await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    [Timeout(90_000)]
    public async Task Arm_D_detect_timeout_on_an_idle_shell_keeps_the_pane_for_relaunch(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await HerdrLiveSession.SkipIfNotEligibleAsync();
        var timeoutId = Guid.NewGuid();
        var grokId = Guid.NewGuid();
        var root = CreateRoot();
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Grok = true });
        var herdr = LiveHerdrClient(launchDetectTimeoutMs: 3_000);
        await using var runtime = BuildRuntime(root.Logs, herdr, launchDetectTimeoutMs: 3_000);
        var label = $"card0383-live-d-{timeoutId:N}"[..32];
        try
        {
            var timeoutRequest = new RunnerLaunchRequest(
                timeoutId,
                PwshExe(),
                ["-NoProfile", "-Command", "exit 1"],
                OverlayEnv(stub.BaseUrl, root.GrokHome),
                root.Cwd,
                Cols: 120,
                Rows: 30,
                Backend: SessionBackends.Herdr,
                TranscriptFormat: TranscriptFormats.Grok,
                Herdr: new HerdrLaunchOptions(
                    WorkspaceKey: label,
                    WorkspaceLabel: label,
                    WorkspaceCwd: root.Cwd,
                    PaneTitle: label,
                    AgentKind: HerdrAgentKinds.Grok,
                    AgentSlug: "card0383-live"));

            var ex = await Should.ThrowAsync<HerdrLaunchException>(() =>
                runtime.StartAsync(timeoutRequest, CancellationToken.None));
            ex.Code.ShouldBe(HerdrLaunchException.CodeDetectTimeout);
            var last = HerdrLastPane.TryLoad(root.Logs, timeoutId).ShouldNotBeNull();
            var paneStillThere = await herdr.PaneGetAsync(last.PaneId, CancellationToken.None);
            paneStillThere.ShouldNotBeNull();

            var grokRequest = GrokRequest(grokId, root, stub, ["--session-id", grokId.ToString("D")], label)
                with
                {
                    Herdr = GrokRequest(grokId, root, stub, ["--session-id", grokId.ToString("D")], label).Herdr!
                        with { ReusePaneOfSessionId = timeoutId },
                };
            var dto = await runtime.StartAsync(grokRequest, CancellationToken.None);
            dto.Status.ShouldBe("Running");
            var sidecar = HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(root.Logs, grokId)).ShouldNotBeNull();
            sidecar.PaneId.ShouldBe(last.PaneId);
            await runtime.KillAsync(grokId, TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static HerdrClient LiveHerdrClient(int launchDetectTimeoutMs = 60_000) =>
        new(new HerdrSettings
        {
            Enabled = true,
            Session = null,
            LaunchDetectTimeoutMs = launchDetectTimeoutMs,
        });

    private static SessionRunnerRuntime BuildRuntime(
        string logs,
        HerdrClient herdr,
        int launchDetectTimeoutMs = 60_000) =>
        new(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = logs,
                PtyHostLingerHours = 0.02,
            }),
            NullLogger<SessionRunnerRuntime>.Instance,
            herdr,
            new PowershellProcessProbe());

    private static RunnerLaunchRequest GrokRequest(
        Guid sessionId,
        Roots root,
        FakeLlmApiServer stub,
        IReadOnlyList<string> identity,
        string label) =>
        new(
            sessionId,
            HerdrLiveSession.GrokExePath,
            new[] { "--always-approve", "--no-alt-screen" }.Concat(identity).ToArray(),
            OverlayEnv(stub.BaseUrl, root.GrokHome),
            root.Cwd,
            Cols: 120,
            Rows: 30,
            Backend: SessionBackends.Herdr,
            TranscriptFormat: TranscriptFormats.Grok,
            Herdr: new HerdrLaunchOptions(
                WorkspaceKey: label,
                WorkspaceLabel: label,
                WorkspaceCwd: root.Cwd,
                PaneTitle: label,
                AgentKind: HerdrAgentKinds.Grok,
                AgentSlug: "card0383-live"));

    private static Dictionary<string, string> OverlayEnv(string stubBaseUrl, string grokHome)
    {
        var overlay = RealCliStubEnv.ForGrok(stubBaseUrl, "canary");
        return new Dictionary<string, string>(overlay.Env, StringComparer.OrdinalIgnoreCase)
        {
            ["GROK_HOME"] = grokHome,
            ["GROK_DISABLE_AUTOUPDATER"] = "1",
        };
    }

    private static string PwshExe()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pwsh = Path.Combine(pf, "PowerShell", "7", "pwsh.exe");
        if (File.Exists(pwsh))
            return pwsh;
        return Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
    }

    private sealed record Roots(string Root, string Logs, string Cwd, string GrokHome);

    private static Roots CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"antiphon-card0383-live-{Guid.NewGuid():N}");
        var logs = Path.Combine(root, "logs");
        var cwd = Path.Combine(root, "cwd");
        var grokHome = Path.Combine(root, "grok-home");
        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(cwd);
        Directory.CreateDirectory(grokHome);
        return new Roots(root, logs, cwd, grokHome);
    }

    private static void DeleteRoot(Roots root)
    {
        try
        {
            if (Directory.Exists(root.Root))
                Directory.Delete(root.Root, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
