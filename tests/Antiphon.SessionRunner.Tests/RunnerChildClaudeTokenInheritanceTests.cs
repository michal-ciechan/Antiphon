using System.Diagnostics;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0628 D-1. The operator's Claude credential on server2 is the <c>claude setup-token</c>
/// OAuth token in the runner CONTAINER's <c>CLAUDE_CODE_OAUTH_TOKEN</c>. The server refuses that
/// name in a runner-bound Claude launch env (D-4, <c>phone_home_env_refused</c>), so the only way
/// the child ever sees it is by inheritance: runner process → detached <c>Antiphon.PtyHost</c> →
/// pty child. This drives the whole runner path with the env the server actually projects (no
/// token in it) and asserts the child read the runner's value — a runtime or host that started
/// building the child's block from the launch env alone, or scrubbing credential names from it,
/// would leave a signed-out Claude on server2.
///
/// <para>Mutates the process environment, hence <c>[NotInParallel]</c> with no key.</para>
/// </summary>
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public class RunnerChildClaudeTokenInheritanceTests
{
    private const string TokenVariable = "CLAUDE_CODE_OAUTH_TOKEN";

    [Test]
    public async Task Pty_child_inherits_the_runner_containers_claude_oauth_token()
    {
        var sentinel = "sentinel-oauth-" + Guid.NewGuid().ToString("N")[..12];
        var outFile = Path.Combine(Path.GetTempPath(), $"c628-token-{Guid.NewGuid():N}.txt");
        var settings = new SessionRunnerSettings
        {
            SessionLogPath = TestSessionLogRoot.Create("claude-token-inherit"),
            PtyHostLingerHours = 0.02,
            PtyBackend = "inbox",
        };
        var sessionId = Guid.NewGuid();
        var previous = Environment.GetEnvironmentVariable(TokenVariable);
        Environment.SetEnvironmentVariable(TokenVariable, sentinel);
        var runtime = new SessionRunnerRuntime(Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        int? childPid = null;
        int? hostPid = null;
        try
        {
            // The projected shape (PhoneHomeLaunchPolicy.Project): store and callback, never the token.
            var env = new Dictionary<string, string>
            {
                ["CLAUDE_CONFIG_DIR"] = "/state/claude",
                ["ANTIPHON_API"] = "https://antiphon.example.invalid",
            };
            var (exe, args) = OperatingSystem.IsWindows()
                ? (Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    new[] { "/d", "/c", $"echo tok=[%{TokenVariable}%] cfg=[%CLAUDE_CONFIG_DIR%]>{outFile}" })
                : ("/bin/sh",
                    new[] { "-c", $"printf 'tok=[%s] cfg=[%s]' \"${TokenVariable}\" \"$CLAUDE_CONFIG_DIR\" > '{outFile}'" });
            var dto = await runtime.StartAsync(
                new RunnerLaunchRequest(sessionId, exe, args, env, Path.GetTempPath(), Cols: 100, Rows: 25),
                CancellationToken.None);
            childPid = dto.Pid;
            hostPid = dto.HostPid;

            var text = await ReadWhenWrittenAsync(outFile, TimeSpan.FromSeconds(20));
            text.ShouldContain($"tok=[{sentinel}]",
                customMessage: "the pty child must inherit the runner process's " + TokenVariable + "; it saw: " + text);
            text.ShouldContain("cfg=[/state/claude]",
                customMessage: "and the launch env is still layered on top: " + text);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenVariable, previous);
            try { await TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, sessionId, hostPid); }
            catch { /* The session may already have exited; the pid kill below is the backstop. */ }
            await runtime.DisposeAsync();
            if (childPid is int pid)
            {
                try { Process.GetProcessById(pid).Kill(entireProcessTree: true); }
                catch { /* Already gone. */ }
            }
            try { File.Delete(outFile); } catch { /* Best effort. */ }
        }
    }

    private static async Task<string> ReadWhenWrittenAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path))
                {
                    var text = await File.ReadAllTextAsync(path);
                    if (text.Contains("cfg=[", StringComparison.Ordinal))
                        return text;
                }
            }
            catch (IOException)
            {
                // Still being written.
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"the child never wrote {path} within {timeout.TotalSeconds:0}s");
    }
}
