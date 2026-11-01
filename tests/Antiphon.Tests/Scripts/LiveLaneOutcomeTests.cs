using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneOutcomeTests
{
    [Test]
    public Task ProviderOutcomeIsDistinct() => Run("ProviderOutcomeIsDistinct", "green", "raw-child", "evidence",
        "raw-passing-1", "raw-passing-2", "raw-passing-3", "raw-passing-8",
        "run-id-invalid-1", "run-id-invalid-2", "run-id-invalid-3", "run-id-invalid-4",
        "run-id-invalid-5", "run-id-invalid-6", "run-id-invalid-7", "run-id-invalid-8",
        "run-id-valid-1", "run-id-valid-2", "drift", "pre-retry", "quota-retry", "post-submit");

    [Test]
    public Task MidRunOutageAfterReceiptIsInconclusive() => Run("MidRunOutageAfterReceiptIsInconclusive",
        "transcript-positive", "prompt-partial", "session-foreign", "request-foreign", "error-before-prompt",
        "untyped-error", "probe-positive", "account-foreign", "endpoint-foreign", "dependency-foreign",
        "post-probe-bound", "post-probe-count", "post-probe-no-prompt");

    [Test]
    public Task HealthyProviderAssertionStaysRed() => Run("HealthyProviderAssertionStaysRed", "healthy-red",
        "unproven-red", "mixed-red", "mixed-invalid", "probe-two-failed-False", "probe-two-failed-True");

    [Test]
    public Task TypedSetupOutcomes() => Run("TypedSetupOutcomes", "external-setup", "unclassified-setup",
        "setup-contract", "missing-prerequisite", "unknown-skip", "setup-no-prompt",
        "failed-setup-missing", "failed-setup-empty", "failed-setup-LegacySkip", "failed-setup-ready",
        "failed-setup-wrong-case-field", "failed-setup-AuthMissing", "failed-setup-AuthMissing-proof");

    [Test]
    public Task SkipAndZeroNeverGreen() => Run("SkipAndZeroNeverGreen", "exact-roster", "skipped", "invalid-schema");

    [Test]
    public Task StaleSharedLockRefuses() => Run("StaleSharedLockRefuses", "stale", "age", "busy");

    private static Task Run(string method, params string[] cases)
    {
        var names = new[] { "entry" }.Concat(cases).Select(c => $"C1545 {method}/{c}").ToArray();
        return ScriptHarness.RunHarnessCaseAsync("test-live-lane-outcome.ps1", "C1545", "C1545_" + method,
            names.Length, names, new ScriptHarnessOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10),
                PostExitDrainBudget: TimeSpan.FromSeconds(10)), CancellationToken.None);
    }
}
