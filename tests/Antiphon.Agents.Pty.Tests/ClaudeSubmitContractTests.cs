using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Unattended fakeclaude submit-contract cases. The live Claude canary lives in
/// Antiphon.LiveClaude.Tests.ClaudeSubmitContractLiveTests (OptIn/Headed) so default safety
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
    // Antiphon.LiveClaude.Tests.ClaudeSubmitContractLiveTests.
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
