using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-1. Each method runs the matching <c>Test-C599_*</c> case of
/// <c>scripts/test-release-gate.ps1</c> in a fresh results directory and requires exit 0 plus that
/// case's exact named assertion inventory. Every row drives the PRODUCTION policy reader, the
/// production discovery/diagnostic parsers and the production census against a real policy file
/// whose canonical hash the reader itself validates; nothing here asserts on source text.
///
/// <para>The decisive behaviour: in a profile lane <c>OptIn</c> alone no longer excludes a case,
/// so an unclassified UID stays required and fails the census instead of silently inheriting
/// manual status - while the default nightly lane keeps its old meaning exactly.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGatePolicyTests
{
    [Test]
    public Task C599_ProfileSchema() => RunCaseAsync("C599_ProfileSchema", 9,
        "C599 ProfileSchema v2 loads",
        "C599 ProfileSchema v1 still loads",
        "C599 ProfileSchema rc on a v1 policy refuses",
        "C599 ProfileSchema unknown profile refuses",
        "C599 ProfileSchema stale hash refuses",
        "C599 ProfileSchema shipped policy is v2",
        "C599 ProfileSchema shipped policy defines rc");

    [Test]
    public Task C599_ProfileSuites() => RunCaseAsync("C599_ProfileSuites", 9,
        "C599 ProfileSuites nightly excludes e2e",
        "C599 ProfileSuites rc requires e2e",
        "C599 ProfileSuites manual e2e is runnable in rc",
        "C599 ProfileSuites manual e2e stays out of nightly",
        "C599 ProfileSuites partial selection is incomplete",
        "C599 ProfileSuites exclusion without reason/owner refuses");

    [Test]
    public Task C599_MetadataParsers() => RunCaseAsync("C599_MetadataParsers", 11,
        "C599 MetadataParsers json parser preserves the raw OptIn category",
        "C599 MetadataParsers diagnostic parser preserves the raw category",
        "C599 MetadataParsers OptIn excluded by default but required in a profile",
        "C599 MetadataParsers json and diagnostic required uid sets are identical",
        "C599 MetadataParsers truncated diagnostic record throws",
        "C599 MetadataParsers duplicate uid throws");

    [Test]
    public Task C599_EligibilityCensus() => RunCaseAsync("C599_EligibilityCensus", 15,
        "C599 EligibilityCensus every uid has exactly one disposition",
        "C599 EligibilityCensus named live class is excluded",
        "C599 EligibilityCensus named live METHOD is excluded",
        "C599 EligibilityCensus sibling method of a mixed class stays required",
        "C599 EligibilityCensus OptIn alone does not exclude in the rc profile",
        "C599 EligibilityCensus a new unclassified case stays required",
        "C599 EligibilityCensus default lane still excludes OptIn",
        "C599 EligibilityCensus zero required uids is not ok");

    [Test]
    public Task C599_ExpandedCoverage() => RunCaseAsync("C599_ExpandedCoverage", 10,
        "C599 ExpandedCoverage control: both required rows executed is complete",
        "C599 ExpandedCoverage a removed expanded row is incomplete",
        "C599 ExpandedCoverage names the missing expanded row",
        "C599 ExpandedCoverage default lane would require zero uids here",
        "C599 ExpandedCoverage zero required uids is never green",
        "C599 ExpandedCoverage missing execution diagnostics is incomplete");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
