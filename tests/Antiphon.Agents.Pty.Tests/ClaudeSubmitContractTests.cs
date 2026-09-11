using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Unattended fakeclaude submit-contract cases. The live Claude canary lives in
/// <see cref="ClaudeSubmitContractLiveTests"/> (OptIn/Headed) so default safety
/// guards stay on these two fake rows.
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeSubmitContractTests
{
    [Test]
    [Arguments("fakeclaude")]
    public async Task Submitting_via_two_writes_completes_a_turn(string backend)
    {
        await using var runner = await ClaudeSubmitContractHarness.LaunchReadyAsync(backend);

        await EchoGatedSubmit.SendAsync(runner, ClaudeSubmitContractHarness.PromptFor(backend));

        var done = await runner.WaitForOutputAsync(
            text => ClaudeSubmitContractHarness.DonePattern.IsMatch(text),
            ClaudeSubmitContractHarness.DoneWaitFor(backend));
        done.ShouldBeTrue($"[{backend}] a properly-submitted turn must complete (\" for Ns\" must appear)");

        await ClaudeSubmitContractHarness.CleanupAsync(runner, backend);
    }

    // Text and the CR in a SINGLE write is a paste — it must NOT complete a turn. This is the exact
    // behaviour that stranded queued messages. CARD-0050 S3: this one-write arm stays time-based
    // (a single write can only be split, never merged) — do not move it onto EchoGatedSubmit.
    // FAKE BACKEND ONLY (2026-07-21): ConPTY does not
    // preserve write boundaries, so on the real CLI one write can surface to Claude as two reads
    // with a typed-Enter-sized gap and legitimately submit — observed failing 2 of 3 runs. The
    // no-submit direction is therefore untestable through this transport against real Claude; the
    // fake pins the modelled contract deterministically (12ms burst gap), and the direction our
    // stack actually RELIES on — two writes DO submit — stays real-Claude-canaried in
    // ClaudeSubmitContractLiveTests.
    [Test]
    [Arguments("fakeclaude")]
    public async Task Text_and_CR_in_one_write_does_not_submit(string backend)
    {
        await using var runner = await ClaudeSubmitContractHarness.LaunchReadyAsync(backend);

        await runner.WriteAsync(ClaudeSubmitContractHarness.PromptFor(backend) + "\r");

        var done = await runner.WaitForOutputAsync(
            text => ClaudeSubmitContractHarness.DonePattern.IsMatch(text),
            ClaudeSubmitContractHarness.NoSubmitWindowFor(backend));
        done.ShouldBeFalse($"[{backend}] text+CR in one write is a paste and must NOT submit");

        await ClaudeSubmitContractHarness.CleanupAsync(runner, backend);
    }
}

internal static class ClaudeSubmitContractHarness
{
    public static readonly Regex DonePattern = new(@" for \d+s", RegexOptions.Compiled);

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    public static string PromptFor(string backend) =>
        backend == "claude" ? "Reply with the single word PONG and nothing else." : "hello fake";

    public static TimeSpan DoneWaitFor(string backend) =>
        backend == "claude" ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(10);

    public static TimeSpan NoSubmitWindowFor(string backend) =>
        backend == "claude" ? TimeSpan.FromSeconds(8) : TimeSpan.FromSeconds(3);

    public static async Task<PtyAgentRunner> LaunchReadyAsync(string backend)
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
            ClSession.SkipIfNotEligible();
            var (app, args) = ClSession.BuildLaunch(ClSession.ResolveOrThrow(), "--dangerously-skip-permissions");
            await runner.StartAsync(app, args, cols: 200, rows: 60);
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
