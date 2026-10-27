using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestSelectionTests
{
    [Test]
    public Task C1313_InputRefusal() => Run("C1313_InputRefusal", 2, "invalid-intent-refused", "git-read-refused");
    [Test]
    public Task C1313_CoreAndUnknownFallback() => Run("C1313_CoreAndUnknownFallback", 2, "core-seven", "unknown-full");
    [Test]
    public Task C1313_PinnedPolicy() => Run("C1313_PinnedPolicy", 2, "candidate-blob", "stale-policy-refused");
    [Test]
    [Arguments("delete")]
    [Arguments("case-rename")]
    [Arguments("nul-paths")]
    public Task C1313_PathIdentity(string variant) => Run("C1313_PathIdentity_" + variant.Replace('-', '_'), 1,
        variant == "delete" ? "old-new-union" : "ordinal-paths");
    [Test]
    public Task C1313_AuthorityChanges() => Run("C1313_AuthorityChanges", 2, "authority-full", "nonancestor-full");
    [Test]
    public Task C1313_DeterministicReasons() => Run("C1313_DeterministicReasons", 1, "deterministic-reasons");
    [Test]
    public Task C1313_SelectorLineEndings() => Run("C1313_SelectorLineEndings", 8,
        "selector-blob-has-cr", "crlf-selector-accepted", "crlf-selector-hash-is-lf", "crlf-producer-hash-is-lf",
        "selector-content-refused", "selector-case-refused", "selector-space-refused", "embedded-cr-refused");
    private static Task Run(string name, int rows, params string[] labels) =>
        ScriptHarness.RunHarnessCaseAsync("test-test-selection.ps1", "C1313", name, rows,
            labels.Select(label => "C1313 " + label).ToArray());
}
