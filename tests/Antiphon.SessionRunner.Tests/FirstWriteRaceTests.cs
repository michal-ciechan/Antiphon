using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0018: a session is registered in the runtime BEFORE its pty-host pipe exists (the host
/// process takes ~a second to spawn on a cold start), so input that raced the launch used to die
/// with "Session has no live pty-host connection" — an unhandled 500 that silently discarded every
/// delegated task's boot prompt. The write path must WAIT for the host instead.
/// </summary>
[NotInParallel("SessionLiveness")]
public class FirstWriteRaceTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task input_racing_a_cold_launch_waits_for_the_host_and_delivers()
    {
        var logRoot = Path.Combine(Path.GetTempPath(), $"antiphon-first-write-race-{Guid.NewGuid():N}");
        var runtime = new SessionRunnerRuntime(
            Options.Create(new SessionRunnerSettings
            {
                SessionLogPath = logRoot,
                PtyHostLingerHours = 0.02,
            }),
            NullLogger<SessionRunnerRuntime>.Instance);

        var sessionId = Guid.NewGuid();
        try
        {
            // Start the launch but do NOT await it: StartAsync registers the session synchronously
            // (before its first await), which is exactly the window the live miss hit.
            var launch = runtime.StartAsync(
                new RunnerLaunchRequest(
                    sessionId,
                    Cmd,
                    ["/d", "/q", "/k", "@echo off & prompt $G"],
                    new Dictionary<string, string>(),
                    Path.GetTempPath(),
                    Cols: 100,
                    Rows: 25),
                CancellationToken.None);

            // Input races the cold start. Pre-fix this threw InvalidOperationException
            // ("Session has no live pty-host connection") immediately.
            var input = runtime.SendInputAsync(sessionId, "echo RACE-MARKER-OK\r", CancellationToken.None);

            await launch;
            await input;

            // The write must have LANDED, not merely not-thrown: the echo's output shows up in
            // the session buffer once cmd has processed it.
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline
                && !runtime.GetBuffer(sessionId).Buffer.Contains("RACE-MARKER-OK"))
            {
                await Task.Delay(100);
            }

            runtime.GetBuffer(sessionId).Buffer.ShouldContain(
                "RACE-MARKER-OK",
                customMessage: "the raced first write must reach the terminal once the host is up");
        }
        finally
        {
            try
            {
                await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch
            {
                // Session may never have fully started; the linger TTL cleans up any orphan host.
            }
            await runtime.DisposeAsync();
            // Shadow-copied pty-host binaries under logRoot stay locked until the lingering host
            // exits — best-effort only, the linger TTL prunes the rest.
            try { Directory.Delete(logRoot, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
