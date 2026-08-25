using System.Diagnostics;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0045 slice 3: the seam that lets a host-mediated test declare a backend at all.
///
/// <para><b>What was missing.</b> <see cref="PtyAgentRunner"/>'s per-instance override (CARD-0037)
/// is unreachable from anything that spawns through the runtime: caller →
/// <see cref="SessionRunnerRuntime"/> → <c>PtyHostLauncher</c> → a DETACHED <c>Antiphon.PtyHost</c>
/// whose <c>HostSession</c> built a bare <c>new PtyAgentRunner()</c> and read the environment block
/// it inherited — three processes away, with nothing in between able to say otherwise.
/// <c>SessionRunnerSettings.PtyBackend</c> existed but only the daemon's <c>Program.cs</c> acted on
/// it, by exporting the env var. So every host-mediated test (the delegation brief ceiling, the
/// queue's pty integration, host adoption) ran on whichever backend the launching shell happened to
/// export, and could not have chosen otherwise.</para>
///
/// <para><b>Why an argument rather than env-on-StartInfo.</b> Both would work — the launcher's
/// <c>ProcessStartInfo</c> block survives the <c>--spawn</c> intermediary, which passes
/// <c>lpEnvironment = NULL</c>. An argument is preferred because it is VISIBLE: in the host's own
/// command line and in the log line beside its pid, at the moment someone is diagnosing why a live
/// session lost 42 KB of a body.</para>
///
/// <para>These tests mutate the process environment to prove precedence, hence
/// <c>[NotInParallel]</c> with no group key: they must not run beside anything that spawns a pty.</para>
/// </summary>
[NotInParallel]
public class PtyBackendSeamTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>
    /// The case the pinned tests depend on: a declared <c>inbox</c> must win against an exported
    /// <c>modern</c>. Without this, <c>DelegationBriefCeilingPtyTests</c> and
    /// <c>SessionMessageQueuePtyIntegrationTests</c> can pin nothing — the env guard would be their
    /// only protection, and a declaration that quietly loses is worse than no declaration.
    /// </summary>
    [Test]
    public async Task A_declared_inbox_backend_outranks_an_exported_modern_one()
    {
        var restore = Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar);
        Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, "modern");
        try
        {
            var logged = await LaunchAndReadHostBackendLineAsync(declared: "inbox");

            logged.ShouldContain(
                nameof(PtyBackend.InboxConhost),
                customMessage:
                "the host must use the backend its command line declared, not the one it inherited "
                + $"through the environment. Host log said: {logged}");
            logged.ShouldContain(
                "requested 'inbox'",
                customMessage: "and it must record that inbox is what was ASKED for: " + logged);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, restore);
        }
    }

    /// <summary>
    /// The other direction, and the one that proves the argument TRAVELS rather than merely agreeing
    /// with a coincidence: with the environment clean (the assembly guard cleared it), the only way
    /// a detached host can end up on the modern pseudoconsole is the argument.
    /// </summary>
    [Test]
    public async Task A_declared_modern_backend_reaches_the_host_with_a_clean_environment()
    {
        if (!ConPtyRedistributable.TryLocate(out _, out var why))
            throw new SkipTestException("no shipped conpty.dll: " + why);

        Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar).ShouldBeNullOrEmpty(
            "the assembly guard must have cleared it — otherwise this proves nothing");

        var logged = await LaunchAndReadHostBackendLineAsync(declared: "modern");

        logged.ShouldContain(
            nameof(PtyBackend.ModernConPty),
            customMessage:
            "an unset environment resolves to the inbox conhost, so a host on the modern "
            + $"pseudoconsole can only have got there from --pty-backend. Host log said: {logged}");
    }

    /// <summary>
    /// Nothing declared is the production shape and must stay exactly as CARD-0037 left it: the host
    /// inherits the runner's environment. The daemon exports <c>SessionRunner:PtyBackend</c> into it
    /// at startup, so this path is what actually carries the flag in the deployed runner.
    /// </summary>
    [Test]
    public async Task Declaring_nothing_still_inherits_the_environment()
    {
        var restore = Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar);
        Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, "inbox");
        try
        {
            var logged = await LaunchAndReadHostBackendLineAsync(declared: null);

            logged.ShouldContain(
                "requested 'inbox'",
                customMessage:
                "with no --pty-backend the host must still read ANTIPHON_PTY_BACKEND from the block "
                + $"it inherited — that is how the daemon propagates today. Host log said: {logged}");
        }
        finally
        {
            Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, restore);
        }
    }

    /// <summary>
    /// Starts one short session through a real runtime and returns the host's own
    /// "<c>pty backend:</c>" log line — the per-session record <c>HostSession</c> writes beside the
    /// child pid, which is the only place the decision is observable from outside the host.
    /// </summary>
    private static async Task<string> LaunchAndReadHostBackendLineAsync(string? declared)
    {
        var settings = new SessionRunnerSettings
        {
            SessionLogPath = TestSessionLogRoot.Create("backend-seam"),
            PtyHostLingerHours = 0.02,
            PtyBackend = declared,
        };
        var sessionId = Guid.NewGuid();

        var runtime = new SessionRunnerRuntime(
            Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance);
        int? childPid = null;
        int? hostPid = null;
        try
        {
            var request = new RunnerLaunchRequest(
                sessionId,
                Cmd,
                ["/d", "/q", "/k", "@echo off & prompt $G"],
                new Dictionary<string, string>(),
                Path.GetTempPath(),
                Cols: 100,
                Rows: 25);
            var dto = await runtime.StartAsync(request, CancellationToken.None);
            childPid = dto.Pid;
            hostPid = dto.HostPid;

            var logPath = Path.Combine(settings.PtyHostLogDir, $"{sessionId:N}.log");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                var line = TryReadBackendLine(logPath);
                if (line is not null)
                    return line;
                await Task.Delay(100);
            }

            throw new System.TimeoutException($"no 'pty backend:' line in {logPath} within 20s");
        }
        finally
        {
            try
            {
                await TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, sessionId, hostPid);
            }
            catch
            {
                // The session may never have started; the pid kill below is the backstop.
            }

            await runtime.DisposeAsync();
            KillBestEffort(childPid);
        }
    }

    private static string? TryReadBackendLine(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
                return null;
            using var stream = new FileStream(
                logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            return text.Split('\n')
                .FirstOrDefault(l => l.Contains("pty backend:", StringComparison.Ordinal))
                ?.Trim();
        }
        catch (IOException)
        {
            return null; // mid-append
        }
    }

    private static void KillBestEffort(int? pid)
    {
        if (pid is not int id)
            return;
        try
        {
            Process.GetProcessById(id).Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
    }
}
