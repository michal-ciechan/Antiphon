using System.Diagnostics;
using Antiphon.Agents.Pty;
using Antiphon.FakeLlmApi;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// CARD-0383 V3: real <c>grok.exe</c> under a temp <c>GROK_HOME</c> with the zero-spend stub env.
/// Pins the on-disk existence bar and the measured <c>--resume</c> of an unknown id.
/// </summary>
[NotInParallel("Headed")]
[Category("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokNativeSessionCanaryTests
{
    [Test]
    [Timeout(60_000)]
    public async Task Resume_of_an_unknown_id_exits_nonzero_fast_and_creates_nothing(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Resume_of_an_unknown_id_exits_nonzero_fast_and_creates_nothing));
        var id = Guid.NewGuid();
        var cwd = GkSession.TempCwd();
        // Remote restore 404s only with a usable auth store; a temp GROK_HOME parks on OAuth
        // instead of exiting. The measured command used the operator home and created nothing.
        var grokHome = GkSession.DefaultGrokHome;
        var sw = Stopwatch.StartNew();
        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(
                GkSession.GrokExePath,
                ["--always-approve", "--no-alt-screen", "--resume", id.ToString("D")],
                cwd: cwd,
                env: new Dictionary<string, string>
                {
                    ["GROK_DISABLE_AUTOUPDATER"] = "1",
                },
                cols: 120,
                rows: 30);

            var finished = await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(15)));
            sw.Stop();
            log($"elapsedMs={sw.ElapsedMilliseconds} exited={runner.Exited.IsCompleted} screen={GkSession.Truncate(runner.SnapshotText(), 400)}");
            runner.Exited.IsCompleted.ShouldBeTrue("grok --resume of an unknown id must exit within 15s");
            finished.ShouldBe(runner.Exited);
            var code = await runner.Exited;
            code.ShouldNotBe(0, "unknown --resume must be non-zero");
            var screen = runner.SnapshotText() + runner.SnapshotScreen();
            screen.Contains("not found", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(
                "screen/stderr should name the miss. Output:\n" + GkSession.Tail(screen, 1500));
            GrokNativeSessionStore.Exists(grokHome, id).ShouldBeFalse();
        }
        finally
        {
            log($"elapsedMs={sw.ElapsedMilliseconds}");
            GkSession.BestEffortDelete(cwd);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Create_writes_the_session_directory_at_launch(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Create_writes_the_session_directory_at_launch));
        var id = Guid.NewGuid();
        var cwd = GkSession.TempCwd();
        var grokHome = Directory.CreateTempSubdirectory("antiphon-card0383-home").FullName;
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Grok = true });
        var env = OverlayEnv(stub.BaseUrl, grokHome);
        var sw = Stopwatch.StartNew();
        try
        {
            await using var runner = new PtyAgentRunner("modern");
            await runner.StartAsync(
                GkSession.GrokExePath,
                GkSession.LaunchArgs(id.ToString("D")),
                cwd: cwd,
                env: env,
                cols: 120,
                rows: 30);

            string? located = null;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                located = GrokNativeSessionStore.TryLocateSessionDirectory(grokHome, id);
                if (located is not null)
                    break;
                if (runner.Exited.IsCompleted)
                    break;
                await Task.Delay(100);
            }

            sw.Stop();
            log($"elapsedMs={sw.ElapsedMilliseconds} located={located} exited={runner.Exited.IsCompleted}");
            runner.Exited.IsCompleted.ShouldBeFalse("create must still be alive when the directory appears");
            located.ShouldNotBeNull("sessions/<enc-cwd>/<id>/ must exist at launch");
            Path.GetFileName(located).ShouldBe(id.ToString("D"));
            await runner.KillAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            log($"elapsedMs={sw.ElapsedMilliseconds}");
            GkSession.BestEffortDelete(grokHome);
            GkSession.BestEffortDelete(cwd);
        }
    }

    [Test]
    [Timeout(90_000)]
    public async Task Resume_of_that_directory_starts(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        GkSession.SkipIfNotEligible();
        var log = GkSession.MeasurementLog(nameof(Resume_of_that_directory_starts));
        var id = Guid.NewGuid();
        var cwd = GkSession.TempCwd();
        var grokHome = Directory.CreateTempSubdirectory("antiphon-card0383-home").FullName;
        await using var stub = await FakeLlmApiServer.StartAsync(new FakeLlmApiOptions { Grok = true });
        var env = OverlayEnv(stub.BaseUrl, grokHome);
        try
        {
            await using (var create = new PtyAgentRunner("modern"))
            {
                await create.StartAsync(
                    GkSession.GrokExePath,
                    GkSession.LaunchArgs(id.ToString("D")),
                    cwd: cwd, env: env, cols: 120, rows: 30);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline
                       && GrokNativeSessionStore.TryLocateSessionDirectory(grokHome, id) is null)
                {
                    if (create.Exited.IsCompleted)
                        throw new SkipTestException("grok exited before creating the session directory");
                    await Task.Delay(100);
                }

                GrokNativeSessionStore.Exists(grokHome, id).ShouldBeTrue();
                await create.KillAsync(TimeSpan.FromSeconds(3));
            }

            var sw = Stopwatch.StartNew();
            await using var resume = new PtyAgentRunner("modern");
            await resume.StartAsync(
                GkSession.GrokExePath,
                ["--always-approve", "--no-alt-screen", "--resume", id.ToString("D")],
                cwd: cwd, env: env, cols: 120, rows: 30);
            var quiet = await resume.WaitForQuietAsync(
                TimeSpan.FromMilliseconds(1000), TimeSpan.FromSeconds(20));
            sw.Stop();
            log($"elapsedMs={sw.ElapsedMilliseconds} quiet={quiet} exited={resume.Exited.IsCompleted} screenLen={resume.SnapshotText().Length}");
            resume.Exited.IsCompleted.ShouldBeFalse();
            resume.SnapshotText().Length.ShouldBeGreaterThan(0);
            await resume.KillAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            GkSession.BestEffortDelete(grokHome);
            GkSession.BestEffortDelete(cwd);
        }
    }

    private static Dictionary<string, string> OverlayEnv(string stubBaseUrl, string grokHome)
    {
        var overlay = RealCliStubEnv.ForGrok(stubBaseUrl, "canary");
        var env = new Dictionary<string, string>(overlay.Env, StringComparer.OrdinalIgnoreCase)
        {
            ["GROK_HOME"] = grokHome,
            ["GROK_DISABLE_AUTOUPDATER"] = "1",
        };
        return env;
    }
}
