using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CodexLaunchRefusalTests
{
    [Test]
    public void Refusal_maps_to_409_with_its_code()
    {
        foreach (var code in new[]
                 {
                     CodexLaunchProblemTypes.CommandLineTooLong,
                     CodexLaunchProblemTypes.LauncherUnavailable,
                     CodexLaunchProblemTypes.LauncherUnsupported,
                 })
        {
            var result = CodexLaunchProblemMapper.Map(new CodexLaunchException(code, $"Session {Guid.NewGuid():D}: {code}: detail"));
            var problem = result.ShouldBeOfType<ProblemHttpResult>();
            problem.StatusCode.ShouldBe(409);
            problem.ProblemDetails.Type.ShouldBe(code);
        }
    }

    [Test]
    public async Task Pty_host_oversized_codex_launch_is_refused_before_a_session_is_registered()
    {
        using var layout = new CodexNpmLayout();
        var settings = BuildSettings();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();
        var sentinel = "C0497-R2-" + Guid.NewGuid().ToString("N");
        var portableExe = Path.Combine(layout.Root, "codex-native-test");
        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(portableExe, "#!/bin/sh\nprintf 'C0497-R2:%s\\n' \"$#\"\nwhile read line; do :; done\n");
            File.SetUnixFileMode(portableExe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var request = new RunnerLaunchRequest(
            sessionId,
            OperatingSystem.IsWindows() ? layout.ShimPath : portableExe,
            ["--no-alt-screen", "-c", "developer_instructions=" + sentinel + new string('X', 40_000)],
            new Dictionary<string, string>(),
            layout.Root,
            120,
            30,
            TranscriptFormat: TranscriptFormats.Codex,
            CommandLineBudgetChars: 30_000);

        if (OperatingSystem.IsWindows())
        {
            var ex = await Should.ThrowAsync<CodexLaunchException>(() =>
                runtime.StartAsync(request, CancellationToken.None));
            ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
            ex.Message.ShouldNotContain(sentinel);
            runtime.List().ShouldBeEmpty();
            runtime.StartCoreSessionRegistrations.ShouldBe(0);
            await Should.ThrowAsync<KeyNotFoundException>(() => runtime.GetAsync(sessionId, CancellationToken.None));
            File.Exists(PtyHostManifest.PathFor(settings.PtyHostManifestDir, sessionId)).ShouldBeFalse();
            File.Exists(Path.Combine(settings.SessionLogPath, $"{sessionId:N}.ansi.log")).ShouldBeFalse();
        }
        else
        {
            var started = await runtime.StartAsync(request, CancellationToken.None);
            started.Status.ShouldBe("Running");
            runtime.Get(sessionId).SessionId.ShouldBe(sessionId);
            runtime.StartCoreSessionRegistrations.ShouldBe(1);
            await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        DeleteLogRoot(settings.SessionLogPath);
    }

    [Test]
    public async Task Pty_host_oversized_codex_launch_is_refused_before_herdr_is_contacted()
    {
        using var layout = new CodexNpmLayout();
        var settings = BuildSettings();
        await using var fake = new FakeHerdrServer();
        fake.LaunchScriptAgentKind = HerdrAgentKinds.Codex;
        fake.Start();
        await fake.WaitUntilListeningAsync();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings),
            NullLogger<SessionRunnerRuntime>.Instance,
            new HerdrClient(new HerdrSettings { Enabled = true, Session = fake.Session, SocketPath = fake.EndpointPath, LaunchDetectTimeoutMs = 5_000 }),
            new PowershellProcessProbe());
        var sessionId = Guid.NewGuid();
        var sentinel = "C0497-R2H-" + Guid.NewGuid().ToString("N");
        var request = new RunnerLaunchRequest(
            sessionId,
            OperatingSystem.IsWindows() ? layout.ShimPath : HerdrTestProcess.ShellPath,
            ["--no-alt-screen", "-c", "developer_instructions=" + sentinel + new string('X', 40_000)],
            new Dictionary<string, string>(),
            settings.SessionLogPath,
            120,
            30,
            Backend: SessionBackends.Herdr,
            TranscriptFormat: TranscriptFormats.Codex,
            Herdr: new HerdrLaunchOptions(
                WorkspaceKey: $"codex-{sessionId:N}"[..32],
                WorkspaceLabel: "card0497-codex",
                WorkspaceCwd: settings.SessionLogPath,
                PaneTitle: "card0497-codex",
                AgentKind: HerdrAgentKinds.Codex),
            CommandLineBudgetChars: 30_000);

        if (OperatingSystem.IsWindows())
        {
            var ex = await Should.ThrowAsync<CodexLaunchException>(() =>
                runtime.StartAsync(request, CancellationToken.None));
            ex.Code.ShouldBe(CodexLaunchProblemTypes.CommandLineTooLong);
            ex.Message.ShouldNotContain(sentinel);
            fake.Requests.ShouldBeEmpty();
            fake.LastLaunchScriptContent.ShouldBeNull();
            runtime.List().ShouldBeEmpty();
            runtime.StartCoreSessionRegistrations.ShouldBe(0);
            File.Exists(HerdrLaunchScript.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
            File.Exists(HerdrPaneSidecar.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
            File.Exists(HerdrLastPane.PathFor(settings.SessionLogPath, sessionId)).ShouldBeFalse();
        }
        else
        {
            var started = await runtime.StartAsync(request, CancellationToken.None);
            started.Status.ShouldBe("Running");
            runtime.Get(sessionId).SessionId.ShouldBe(sessionId);
            fake.LastLaunchScriptContent.ShouldContain(sentinel);
            runtime.StartCoreSessionRegistrations.ShouldBe(1);
            await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        DeleteLogRoot(settings.SessionLogPath);
    }

    private static SessionRunnerSettings BuildSettings() => new()
    {
        SessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-c0497-refuse-{Guid.NewGuid():N}"),
        PtyHostLingerHours = 0.02,
    };

    private static void DeleteLogRoot(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
    }
}
