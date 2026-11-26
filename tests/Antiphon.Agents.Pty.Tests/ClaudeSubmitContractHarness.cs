using Antiphon.TestSupport.LiveClaude;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// The submit-contract launch, submit and cleanup shared by the fakeclaude cases (<see cref="ClaudeSubmitContractTests"/>)
/// and the live canary (Antiphon.LiveClaude.Tests.ClaudeSubmitContractLiveTests, which compiles this file).
/// </summary>
internal static class ClaudeSubmitContractHarness
{
    public static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    public static string PromptFor(string backend) =>
        backend == "claude" ? "Delivery canary " + Guid.NewGuid().ToString("N") + "\n" + new string('x', 210) + "\nThis is inert fixture text. Do not use any tools, files or commands. Reply with the single word PONG and nothing else. Tail " + Guid.NewGuid().ToString("N") : "hello fake";

    public static TimeSpan DoneWaitFor(string backend) =>
        backend == "claude" ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(10);

    public static TimeSpan NoSubmitWindowFor(string backend) =>
        backend == "claude" ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(3);

    public static async Task<PtyAgentRunner> LaunchReadyAsync(string backend, LiveClaudeExecutable? coreExecutable = null,
        LiveClaudeProfile? coreProfile = null, string? coreSession = null)
    {
        var runner = new PtyAgentRunner("inbox");
        if (backend == "fakeclaude")
        {
            if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
            if (!File.Exists(FakeClaudeExe))
                throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

            await runner.StartAsync(FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30);
            var ready = await runner.WaitForOutputAsync(s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(45));
            ready.ShouldBeTrue("fake Claude should print its readiness banner");
        }
        else
        {
            var executable = coreExecutable ?? ClSession.RequireCore(LiveClaudeCore.Submit);
            var profile = coreProfile ?? throw new LiveClaudeRefusal("missing-profile");
            ClSession.SkipIfNotEligible(executable);
            var launch = ClSession.BuildProfileLaunch(profile, "submit", coreSession ?? throw new LiveClaudeRefusal("missing-session"));
            var (app, args) = ClSession.BuildCoreLaunch(executable, launch.Args);
            await runner.StartAsync(app, args, cwd: launch.Cwd, cols: launch.Cols, rows: launch.Rows, env: launch.Env);
            executable.RequireNativeObservation();
            var ready = await new ClaudeReadyDetector().WaitAsync(runner);
            if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        }

        runner.Backend!.Backend.ShouldBe(
            PtyBackend.InboxConhost,
            "the submit contract is pinned against the inbox conhost on both arms — declared, not "
            + "inherited from ANTIPHON_PTY_BACKEND");
        runner.ClearLiveBuffer();
        return runner;
    }

    internal static async Task<(DeliveryRequest Request, IReadOnlyList<DeliveryRow> Rows)> SubmitClaudeAsync(
        PtyAgentRunner runner, LiveClaudeProfile profile, string sessionId, string body)
    {
        var delivery = new LiveClaudeDeliverySession(runner, profile, sessionId);
        var request = delivery.Capture(body);
        var rows = await LiveClaudeDeliveryEvidence.SubmitOnceAsync(request, () => delivery.SendAsync(body),
            () => delivery.ObserveAsync(request, completion: true), completion: true);
        LiveClaudeDeliveryEvidence.RequireSubmit(request, rows);
        return (request, rows);
    }

    public static async Task CleanupAsync(PtyAgentRunner runner, string backend)
    {
        if (backend == "claude")
        {
            await runner.SendLineAsync("/exit");
            await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }
}
