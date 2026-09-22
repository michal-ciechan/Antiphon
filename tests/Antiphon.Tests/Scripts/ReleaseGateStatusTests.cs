using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-7. Each method runs the matching <c>Test-C599_*</c> case of
/// <c>scripts/test-release-gate.ps1</c> against the PRODUCTION status script and the production
/// publication journal.
///
/// <para>The decisive behaviours: the running SHA is matched EXACTLY against published manifests,
/// so a newer build reports <c>unreleased integration build</c> rather than borrowing the latest
/// release's identity; a reserved-but-unpublished tag is not released; missing publication data is
/// <c>unknown</c>, not the latest release; and no release operation restarts, deploys, kills or
/// checks anything out.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateStatusTests
{
    [Test]
    public Task C599_StatusIdentity() => RunCaseAsync("C599_StatusIdentity", 7,
        "C599 StatusIdentity no publication journal is unknown, not latest",
        "C599 StatusIdentity a reserved but unpublished tag is not released",
        "C599 StatusIdentity an exact sha match reports released with its tag",
        "C599 StatusIdentity the release url is the GitHub tag address",
        "C599 StatusIdentity a newer sha is NOT labelled as the latest release",
        "C599 StatusIdentity a newer sha carries no tag");

    [Test]
    public Task C599_NoActivation() => RunCaseAsync("C599_NoActivation", 13,
        "C599 NoActivation release-status performs no restart-apphost",
        "C599 NoActivation release-status performs no git checkout",
        "C599 NoActivation publish-release performs no restart-apphost",
        "C599 NoActivation control: the publication ran",
        "C599 NoActivation the publication really pushed something",
        "C599 NoActivation no observed push carried a force flag or plus refspec",
        "C599 NoActivation no observed call deleted or re-created a tag");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
