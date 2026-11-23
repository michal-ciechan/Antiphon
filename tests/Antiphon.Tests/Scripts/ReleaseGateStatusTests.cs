using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-7. <c>Test-C599_NoActivation</c> of <c>scripts/test-release-gate.ps1</c> runs a real publication through
/// the PRODUCTION publisher (still PowerShell) and inspects the git calls it actually made: no restart, kill or deploy,
/// no force push, no tag deletion. The status itself is C# since unit 12 of 2026-11-22-remaining-ps1-to-csharp.md
/// (<c>antiphon-scripts release-status</c>): its identity rows and its no-activation row run in process in
/// <see cref="ReleaseStatusTests"/>, so this case no longer reads the deleted release-status.ps1.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateStatusTests
{
    [Test]
    public Task C599_NoActivation() => RunCaseAsync("C599_NoActivation", 9,
        "C599 NoActivation publish-release performs no restart-apphost",
        "C599 NoActivation control: the publication ran",
        "C599 NoActivation the publication really pushed something",
        "C599 NoActivation no observed push carried a force flag or plus refspec",
        "C599 NoActivation no observed call deleted or re-created a tag",
        "C599 NoActivation deletion matcher distinguishes a populated source refspec");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
