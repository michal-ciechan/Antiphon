using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseTagAllocatorTests
{
    [Test]
    public Task C1339_AllocationUtcDay() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1339", "C1339_AllocationUtcDay", 5,
            "C1339 G-33 utc-day",
            "C1339 G-34 one-clock-sample",
            "C1339 G-35 midnight-reuse",
            "C1339 G-36 stamp-day",
            "C1339 G-37 candidate-date-preserved");

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

    [Test]
    public Task C1339_RemoteTagAllocation() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1339", "C1339_RemoteTagAllocation", 19,
            "C1339 G-18 remote-finals",
            "C1339 G-19 remote-prerelease",
            "C1339 G-20 remote-final-closes",
            "C1339 G-21 canonical-inventory",
            "C1339 G-22 journal-union",
            "C1339 G-23 smallest-free",
            "C1339 G-24 legacy-occupancy",
            "C1339 G-25 inventory-required",
            "C1339 G-26 reservation-reuse",
            "C1339 G-27 candidate-sha-conflict",
            "C1339 G-28 sha-candidate-conflict",
            "C1339 G-29 selected-tag-recheck",
            "C1339 G-30 selected-final-recheck",
            "C1339 G-31 preview-reservation",
            "C1339 G-32 preview-authority",
            "C1339 G-67 greatest-open-n",
            "C1339 G-68 greatest-k",
            "C1339 G-69 no-invented-publications",
            "C1339 G-70 no-implicit-inventory");
}
