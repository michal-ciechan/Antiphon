using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0479 V-9..V-12 and R-9. Each method runs one case of
/// <c>scripts/test-mechanical-pc-contract.ps1</c>, which extracts the labelled snippets
/// from <c>docs/mechanical-pc-contract.md</c> and rehearses them on disposable files.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class MechanicalPcRehearsalTests
{
    [Test]
    public Task C479_V09a_BindingSource() => Run("binding-source", 5,
        "C479 binding-source:head-equals-l-accepts",
        "C479 binding-source:head-mismatch-refuses",
        "C479 binding-source:remote-advanced-accepts",
        "C479 binding-source:missing-digest-refuses",
        "C479 binding-source:member-hash-mismatch-refuses");

    [Test]
    public Task C479_V09b_BindingPreimage() => Run("binding-preimage", 3,
        "C479 binding-preimage:crlf-refuses",
        "C479 binding-preimage:candidate-at-c-refuses",
        "C479 binding-preimage:backend-drift-refuses");

    [Test]
    public Task C479_V10a_ApplyExact() => Run("apply-exact", 3,
        "C479 apply-exact:applies-once-accepts",
        "C479 apply-exact:mutant-sha256-matches",
        "C479 apply-exact:changed-file-set-matches");

    [Test]
    public Task C479_V10b_ApplyContext() => Run("apply-context", 2,
        "C479 apply-context:duplicate-refuses-no-write",
        "C479 apply-context:absent-refuses-no-write");

    [Test]
    public Task C479_V10c_ApplyClosure() => Run("apply-closure", 3,
        "C479 apply-closure:extra-file-restores-and-refuses",
        "C479 apply-closure:partial-second-preimage-restores-first",
        "C479 apply-closure:fixture-hash-refuses-before-write");

    [Test]
    public Task C479_V11a_OracleRows() => Run("oracle-rows", 9,
        "C479 oracle-rows:exact-negative-and-companions-accept",
        "C479 oracle-rows:extra-failure-rejects",
        "C479 oracle-rows:zero-rows-reject",
        "C479 oracle-rows:missing-row-rejects",
        "C479 oracle-rows:extra-row-rejects",
        "C479 oracle-rows:duplicate-row-rejects",
        "C479 oracle-rows:skipped-rejects",
        "C479 oracle-rows:driver-exit-2-rejects",
        "C479 oracle-rows:driver-exit-3-rejects");

    [Test]
    public Task C479_V11b_OracleProvenance() => Run("oracle-provenance", 3,
        "C479 oracle-provenance:preexisting-results-reject",
        "C479 oracle-provenance:unknown-build-source-rejects",
        "C479 oracle-provenance:same-output-identity-rejects");

    [Test]
    public Task C479_V12a_RestoreInterrupt() => Run("restore-interrupt", 3,
        "C479 restore-interrupt:after-apply-restores-preimage",
        "C479 restore-interrupt:after-red-restores-preimage",
        "C479 restore-interrupt:restored-requires-forced-rebuild");

    [Test]
    public Task C479_V12b_RestoreForeignEdit() => Run("restore-foreign-edit", 2,
        "C479 restore-foreign-edit:path-retained-and-reported",
        "C479 restore-foreign-edit:sibling-retained");

    [Test]
    public Task C479_V12c_InstantiateTokens() => Run("instantiate-tokens", 5,
        "C479 instantiate-tokens:unknown-token-refuses",
        "C479 instantiate-tokens:missing-value-refuses",
        "C479 instantiate-tokens:empty-results-refuses",
        "C479 instantiate-tokens:path-not-contained-refuses",
        "C479 instantiate-tokens:existing-attempt-refuses");

    private static Task Run(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-mechanical-pc-contract.ps1", "C479", caseName, expectedRows, requiredRows);
}
