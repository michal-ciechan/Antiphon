using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// The <em>submit contract</em> — pinned against BOTH backends from one set of scenarios:
///  * <c>"fakeclaude"</c> — the deterministic fake, runs in CI (no opt-in).
///  * <c>"claude"</c> — the real CLI, opt-in headed (<c>ANTIPHON_HEADED_TESTS=1</c> + claude on PATH);
///    self-skips otherwise.
///
/// This is the drift canary. The fake encodes our understanding of how Claude's TUI handles input; the
/// real-Claude variant checks that understanding still holds. If a future Claude starts (or stops)
/// submitting on a paste-style write, the <c>"claude"</c> case fails and we learn the contract moved —
/// while the <c>"fakeclaude"</c> case keeps CI green and our other tests trustworthy.
///
/// Both cases assert via the <c>" for Ns"</c> done pattern (Claude's "Crunched for 3s" turn-end summary,
/// which the fake also emits). We deliberately do NOT key on the idle OSC title here: Claude can re-emit
/// it on an idle redraw, which would false-positive the "did not submit" assertion. The done pattern only
/// appears after a real processing turn, so it cleanly distinguishes "submitted" from "sitting in the composer".
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
[Category("Headed")]
public class ClaudeSubmitContractTests
{
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    // The two-write submit (body, brief gap, then a lone CR) must complete a turn on BOTH backends.
    [Test]
    [Arguments("fakeclaude")]
    [Arguments("claude")]
    public async Task Submitting_via_two_writes_completes_a_turn(string backend)
    {
        await using var runner = await LaunchReadyAsync(backend);

        // SendLineAsync IS the two-write shape (body, 20ms, "\r") that DeliverAsync mirrors.
        await runner.SendLineAsync(PromptFor(backend));

        var done = await runner.WaitForOutputAsync(
            text => DonePattern.IsMatch(text), DoneWaitFor(backend));
        done.ShouldBeTrue($"[{backend}] a properly-submitted turn must complete (\" for Ns\" must appear)");

        await CleanupAsync(runner, backend);
    }

    // Text and the CR in a SINGLE write is a paste — it must NOT complete a turn on EITHER backend.
    // This is the exact behaviour that stranded queued messages; the canary keeps real Claude honest about it.
    [Test]
    [Arguments("fakeclaude")]
    [Arguments("claude")]
    public async Task Text_and_CR_in_one_write_does_not_submit(string backend)
    {
        await using var runner = await LaunchReadyAsync(backend);

        await runner.WriteAsync(PromptFor(backend) + "\r");

        // If the paste wrongly submitted, the turn would complete and the done pattern would appear.
        var done = await runner.WaitForOutputAsync(
            text => DonePattern.IsMatch(text), NoSubmitWindowFor(backend));
        done.ShouldBeFalse($"[{backend}] text+CR in one write is a paste and must NOT submit");

        await CleanupAsync(runner, backend);
    }

    private static string PromptFor(string backend) =>
        backend == "claude" ? "Reply with the single word PONG and nothing else." : "hello fake";

    private static TimeSpan DoneWaitFor(string backend) =>
        backend == "claude" ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(10);

    // How long to wait while asserting NO submit happened. Generous enough for the real backend to have
    // started processing if it were going to, short enough to keep the suite snappy.
    private static TimeSpan NoSubmitWindowFor(string backend) =>
        backend == "claude" ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(3);

    private static async Task<PtyAgentRunner> LaunchReadyAsync(string backend)
    {
        var runner = new PtyAgentRunner();
        if (backend == "fakeclaude")
        {
            if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
            if (!File.Exists(FakeClaudeExe))
                throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

            await runner.StartAsync(FakeClaudeExe, Array.Empty<string>(), cols: 120, rows: 30);
            var ready = await runner.WaitForOutputAsync(s => s.Contains("Fake Claude ready"), TimeSpan.FromSeconds(15));
            ready.ShouldBeTrue("fake Claude should print its readiness banner");
        }
        else // "claude" — real CLI, opt-in headed
        {
            ClSession.SkipIfNotEligible();
            var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
            await runner.StartAsync(app, args, cols: 200, rows: 60);
            var ready = await new ClaudeReadyDetector().WaitAsync(runner);
            if (!ready) throw new SkipTestException("real Claude TUI did not reach a ready state");
        }

        runner.ClearLiveBuffer();
        return runner;
    }

    private static async Task CleanupAsync(PtyAgentRunner runner, string backend)
    {
        if (backend == "claude")
        {
            await runner.SendLineAsync("/exit");
            await Task.WhenAny(runner.Exited, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        await runner.KillAsync(TimeSpan.FromSeconds(2));
    }
}
