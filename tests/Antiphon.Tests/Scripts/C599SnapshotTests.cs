using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C599SnapshotTests
{
    [Test]
    public Task Finally_restores_after_a_throwing_mutation() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1731", "C1731_SnapshotIsolation", 5,
            "C1731 first variant starts from a valid control",
            "C1731 throwing mutation fails and runs cleanup",
            "C1731 finally restores every byte and absence after failure",
            "C1731 next variant sees a valid isolated control",
            "C1731 missing parent and binary file are recreated");

    [Test]
    public Task Restore_runs_once_and_only_writes_changed_bytes() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1731", "C1731_SnapshotWork", 3,
            "C1731 one mandatory restore per failed variant",
            "C1731 unchanged snapshot files are not rewritten",
            "C1731 same length and timestamp cannot hide changed bytes");

    [Test]
    public Task Evidence_path_refusals_match_the_seeded_set() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1731", "C1731_EvidenceRefusals", 13,
            "C1731 ordinary relative evidence resolves under the candidate root",
            "C1731 missing evidence resolves when no component is a reparse point",
            "C1731 blank and empty evidence refuses",
            "C1731 absolute evidence refuses",
            "C1731 parent-relative escape refuses",
            "C1731 a path that normalizes inside the root resolves to that file",
            "C1731 a linked directory pointing outside refuses",
            "C1731 a linked directory whose target stays inside the root still refuses",
            "C1731 a linked file refuses",
            "C1731 a broken link refuses",
            "C1731 a hard link resolves",
            "C1731 seeded evidence results match the recorded set",
            "C1731 an accepted path refuses after that component becomes a link");

    [Test]
    public Task Evidence_path_walk_finishes_within_fifteen_seconds() =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C1731", "C1731_EvidenceWalk", 1,
            "C1731 six hundred eleven ordinary resolutions finish within fifteen seconds");
}
