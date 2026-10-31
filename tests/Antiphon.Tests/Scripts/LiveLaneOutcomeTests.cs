using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneOutcomeTests
{
    [Test]
    public Task ProviderOutcomeIsDistinct() => Run("ProviderOutcomeIsDistinct", "green", "raw-child", "evidence",
        "drift", "pre-retry", "quota-retry", "post-submit");

    [Test]
    public Task MidRunOutageAfterReceiptIsInconclusive() => Run("MidRunOutageAfterReceiptIsInconclusive",
        "transcript-positive", "prompt-partial", "session-foreign", "request-foreign", "error-before-prompt",
        "untyped-error", "probe-positive", "account-foreign", "endpoint-foreign", "dependency-foreign",
        "post-probe-bound", "post-probe-count", "post-probe-no-prompt");

    [Test]
    public Task HealthyProviderAssertionStaysRed() => Run("HealthyProviderAssertionStaysRed", "healthy-red",
        "unproven-red", "mixed-red", "mixed-invalid");

    [Test]
    public Task TypedSetupOutcomes() => Run("TypedSetupOutcomes", "external-setup", "unclassified-setup",
        "setup-contract", "missing-prerequisite", "unknown-skip", "setup-no-prompt");

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
