using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseTagAllocatorTests
{
    [Test]
    public Task C1339_ExactRemoteRefs() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1339", "C1339_ExactRemoteRefs", 19,
            "C1339 G-1 exact-tag",
            "C1339 G-2 exact-head",
            "C1339 G-3 ordinal-case",
            "C1339 G-4 full-sha",
            "C1339 G-5 ambiguous-record",
            "C1339 G-6 query-failure",
            "C1339 G-7 candidate-long-read",
            "C1339 G-8 candidate-stamp-read",
            "C1339 G-9 candidate-recovery-read",
            "C1339 G-10 publisher-head-read",
            "C1339 G-11 publisher-recovery-read",
            "C1339 G-12 publisher-verify-read",
            "C1339 G-13 promotion-pre-read",
            "C1339 G-14 promotion-head-read",
            "C1339 G-15 promotion-existing-read",
            "C1339 G-16 promotion-recovery-read",
            "C1339 G-17 promotion-verify-read",
            "C1339 G-64 fixture-path-contained",
            "C1339 G-65 fixture-transport-allowlist");
}
