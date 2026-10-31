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
        var sessionId = Guid.NewGuid().ToString("D");
        await using var runner = await ClaudeSubmitContractHarness.LaunchReadyAsync(backend, executable, profile, sessionId);
        var body = ClaudeSubmitContractHarness.PromptFor(backend);
        var (request, rows) = await ClaudeSubmitContractHarness.SubmitClaudeAsync(runner, profile, sessionId, body);
        LiveClaudeDeliveryEvidence.RequireSubmit(request, rows);

        await ClaudeSubmitContractHarness.CleanupAsync(runner, backend);
    }
}
