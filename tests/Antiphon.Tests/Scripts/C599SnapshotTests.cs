using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
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
}
