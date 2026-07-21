using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// End-to-end contract for VERIFIED delivery against BOTH backends (fakeclaude in CI, real Claude
/// opt-in headed): type the body exactly like <c>DeliverAsync</c> (one write, no CR), require
/// <see cref="ComposerDeliveryEvidence"/> — the REAL production predicate — to confirm the body
/// landed in the composer, then submit with a separate CR and require the turn to complete.
///
/// This is the delivery-verification analogue of <c>ClaudeSubmitContractTests</c>: the fake keeps
/// CI honest about the flow, the real-Claude variant is the drift canary proving the predicate
/// still recognises what an actual composer renders — for a short body, a huge single-line wall
/// (suffix-only rendering) and a multi-line wall (placeholder-or-tail rendering).
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
[Category("Headed")]
public class ClaudeVerifiedDeliveryTests
{
    private static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    [Test]
    [Arguments("fakeclaude", "short")]
    [Arguments("claude", "short")]
    [Arguments("claude", "hugeLine")]
    [Arguments("claude", "multiLine")]
    public async Task Evidence_appears_then_submit_completes_a_turn(string backend, string shape)
    {
        await using var runner = await LaunchReadyAsync(backend);
        var body = BodyFor(backend, shape);

        var before = runner.SnapshotScreen();
        await runner.WriteAsync(body); // the DeliverAsync body shape: one write, no CR

        var evidence = await WaitForEvidenceAsync(runner, before, body, TimeSpan.FromSeconds(15));
        evidence.ShouldBeTrue(
            $"[{backend}/{shape}] ComposerDeliveryEvidence must recognise the typed body on the rendered screen. "
            + $"Screen was:\n{runner.SnapshotScreen()}");

        await Task.Delay(20);
        await runner.WriteAsync("\r");

        var done = await runner.WaitForOutputAsync(
            text => DonePattern.IsMatch(text), DoneWaitFor(backend));
        done.ShouldBeTrue($"[{backend}/{shape}] the verified submit must complete a turn");

        await CleanupAsync(runner, backend);
    }

    private static async Task<bool> WaitForEvidenceAsync(
        PtyAgentRunner runner, string before, string body, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (ComposerDeliveryEvidence.IsVisible(before, runner.SnapshotScreen(), body))
                return true;
            await Task.Delay(250);
        }
        return ComposerDeliveryEvidence.IsVisible(before, runner.SnapshotScreen(), body);
    }

    private static string BodyFor(string backend, string shape)
    {
        if (backend == "fakeclaude")
            return "hello fake";

        return shape switch
        {
            "hugeLine" => BuildHugeLine(),
            "multiLine" => BuildMultiLine(),
            _ => "Reply with the single word PONG and nothing else.",
        };
    }

    // ~3k chars of ignorable filler ending in the actual instruction: the tail (which is what the
    // suffix-only composer rendering shows) carries the distinctive content.
    private static string BuildHugeLine()
    {
        var sb = new StringBuilder("Ignore the following filler tokens completely: ");
        for (var i = 0; sb.Length < 3000; i++) sb.Append($"filler{i:D4} ");
        sb.Append("— after ignoring all filler, reply with the single word PONG and nothing else.");
        return sb.ToString();
    }

    private static string BuildMultiLine()
    {
        var lines = new List<string> { "Read all lines below, then reply with the single word PONG and nothing else." };
        lines.AddRange(Enumerable.Range(0, 24).Select(i => $"ignorable filler line {i:D2}"));
        lines.Add("End of filler. Remember: reply with exactly PONG.");
        return string.Join("\n", lines);
    }

    // 4 min for real Claude: the filler-wall prompts occasionally produce a slow turn, and this
    // test asserts delivery mechanics, not model latency (a 2-min limit flaked once on 2026-07-21).
    private static TimeSpan DoneWaitFor(string backend) =>
        backend == "claude" ? TimeSpan.FromMinutes(4) : TimeSpan.FromSeconds(10);

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
        else
        {
            ClSession.SkipIfNotEligible();
            var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
            await runner.StartAsync(app, args, cols: 120, rows: 30); // production terminal size
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
