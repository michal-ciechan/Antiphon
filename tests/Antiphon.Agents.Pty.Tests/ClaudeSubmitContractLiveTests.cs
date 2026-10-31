using Antiphon.TestSupport.LiveClaude;
using Antiphon.Agents.Pty;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Live Claude submit-contract canary. Inventoried separately from the unattended
/// fakeclaude cases in <see cref="ClaudeSubmitContractTests"/>.
/// </summary>
[NotInParallel("Headed")]
[Category("Pty")]
[Category("Headed")]
[Category("OptIn")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ClaudeSubmitContractLiveTests
{
    [Test]
    [Arguments("claude")]
    public async Task Submitting_via_two_writes_completes_a_turn(string backend)
    {
        var executable = ClSession.RequireCore(LiveClaudeCore.Submit);
        using var profile = ClSession.RequireProfile(executable);
        await using var runner = await ClaudeSubmitContractHarness.LaunchReadyAsync(backend, executable, profile);

        await runner.SendLineAsync(ClaudeSubmitContractHarness.PromptFor(backend));

        var done = await runner.WaitForOutputAsync(
            text => ClaudeSubmitContractHarness.DonePattern.IsMatch(text),
            ClaudeSubmitContractHarness.DoneWaitFor(backend));
        done.ShouldBeTrue($"[{backend}] a properly-submitted turn must complete (\" for Ns\" must appear)");

        await ClaudeSubmitContractHarness.CleanupAsync(runner, backend);
    }
}
