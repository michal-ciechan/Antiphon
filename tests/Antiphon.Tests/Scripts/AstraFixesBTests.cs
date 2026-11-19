using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// Astra audit a5e3ce5c witnesses: a failed deferred write is a refusal the summary can see,
// a journal chunk is acknowledged only after its record is on disk, and a loaded core whose
// identity or required types do not match the cache is refused before a chunk runs.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AstraFixesBTests
{
    [Test]
    public Task FlushFailure(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-astra-fixes-b.ps1", "AB", nameof(FlushFailure), 10,
            ["AB FlushFailure.not-complete-stale", "AB FlushFailure.refused", "AB FlushFailure.path-keeps-other",
             "AB FlushFailure.path-still-failed", "AB FlushFailure.later-flush-keeps-other",
             "AB FlushFailure.verdict", "AB FlushFailure.retry-keeps-failed",
             "AB FlushFailure.phase-quality-token", "AB FlushFailure.summary-calls-verdict",
             "AB FlushFailure.verdict-flushes"],
            ScriptHarnessOptions.Default, cancellationToken);

    [Test]
    public Task JournalPublish(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-astra-fixes-b.ps1", "AB", nameof(JournalPublish), 6,
            ["AB JournalPublish.ack-waits-for-disk", "AB JournalPublish.held-write-not-acked",
             "AB JournalPublish.throw-does-not-ack", "AB JournalPublish.close-holds-lock",
             "AB JournalPublish.unrelated-releases-lock", "AB JournalPublish.kill-keeps-acked-record"],
            ScriptHarnessOptions.Default with { ExecutionBudget = TimeSpan.FromSeconds(180) }, cancellationToken);

    [Test]
    public Task StaleCore(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-astra-fixes-b.ps1", "AB", nameof(StaleCore), 5,
            ["AB StaleCore.refused", "AB StaleCore.named-reason", "AB StaleCore.missing-type-at-load",
             "AB StaleCore.fresh-pwsh", "AB StaleCore.no-late-call"],
            ScriptHarnessOptions.Default with { ExecutionBudget = TimeSpan.FromSeconds(180) }, cancellationToken);

    [Test]
    public Task CompleteCorePath(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-astra-fixes-b.ps1", "AB", nameof(CompleteCorePath), 4,
            ["AB CompleteCorePath.refused", "AB CompleteCorePath.loaded-path",
             "AB CompleteCorePath.types-present", "AB CompleteCorePath.fresh-pwsh"],
            ScriptHarnessOptions.Default with { ExecutionBudget = TimeSpan.FromSeconds(180) }, cancellationToken);

    [Test]
    public Task CheckoutIdentity(CancellationToken cancellationToken) =>
        ScriptHarness.RunHarnessCaseAsync("test-astra-fixes-b.ps1", "AB", nameof(CheckoutIdentity), 2,
            ["AB CheckoutIdentity.rechecked", "AB CheckoutIdentity.stable-hash"],
            ScriptHarnessOptions.Default with { ExecutionBudget = TimeSpan.FromSeconds(180) }, cancellationToken);
}
