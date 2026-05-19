using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
[NotInParallel("Pty")]
public class SessionRunnerRuntimeTests
{
    [Test]
    public async Task Session_runner_starts_shell_accepts_input_and_keeps_buffer()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-session-runner-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = Path.Combine(tempRoot, "logs")
            }),
            NullLogger<SessionRunnerRuntime>.Instance);
        var sessionId = Guid.NewGuid();

        try
        {
            var session = await runtime.StartAsync(
                new RunnerLaunchRequest(
                    sessionId,
                    Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    ["/d", "/q", "/k", "@echo off & prompt $G"],
                    new Dictionary<string, string>(),
                    tempRoot,
                    120,
                    30),
                CancellationToken.None);

            session.SessionId.ShouldBe(sessionId);
            session.Status.ShouldBe("Running");

            await runtime.SendInputAsync(sessionId, "echo SESSION_RUNNER_OK\r", CancellationToken.None);
            await WaitUntilAsync(() =>
                runtime.GetBuffer(sessionId).Buffer.Contains("SESSION_RUNNER_OK", StringComparison.OrdinalIgnoreCase));

            var buffer = runtime.GetBuffer(sessionId);
            buffer.LastSequence.ShouldBeGreaterThan(0);
            buffer.Buffer.ShouldContain("SESSION_RUNNER_OK", Case.Insensitive);

            var killed = await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
            killed.Status.ShouldBe("Exited");
        }
        finally
        {
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(25);
        }

        predicate().ShouldBeTrue();
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for PTY/session runner test directories.
        }
    }
}
