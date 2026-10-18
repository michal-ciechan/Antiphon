using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1311 S2. <c>Test-C1311_LongLivedRcRef</c> calls the production ref parser,
/// credit kind, and candidate journal. A long-lived ref is <c>rc/YYYY-MM-DD</c>
/// with one candidate id per tested SHA. A legacy stamp ref still earns
/// <c>rc-release</c>.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseBranchModelGateTests
{
    [Test]
    public Task C1311_LongLivedRcRef() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1311", "C1311_LongLivedRcRef", 10,
            "C1311 LongLivedRcRef rc/2026-10-15 is accepted",
            "C1311 LongLivedRcRef refs/heads/rc/2026-10-15 normalizes to rc/2026-10-15",
            "C1311 LongLivedRcRef candidate id is rc-2026-10-15-bbbbbbbbbbbb",
            "C1311 LongLivedRcRef rc/2026-13-01 refuses ref-date",
            "C1311 LongLivedRcRef rc/20261015 refuses ref-shape",
            "C1311 LongLivedRcRef legacy stamp release/rc-20260923T083000Z is accepted",
            "C1311 LongLivedRcRef credit rc/rc/rc/2026-10-15 earns rc-release",
            "C1311 LongLivedRcRef credit rc/rc/release/rc-20260923T083000Z earns rc-release",
            "C1311 LongLivedRcRef omitted Ref refuses rc-ref-required",
            "C1311 LongLivedRcRef an existing rc/2026-10-15 tip is journalled and not pushed");
}
