using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class ChangeAwareReleaseBudgetTests
{
    // CARD-1722: nearest-rank p95 of Investigate 5c10955b's five rc samples;
    // ParameterHops has only the caller's g120 Windows sample (provisional p95).
    [Test]
    [Arguments("C1313_BaselineAuthority", 294.207, 263.002)]
    [Arguments("C1313_ParameterHops", 183.8, 183.8)]
    public async Task Slow_case_passes_measured_budget_to_harness(string caseName, double p95Seconds, double medianSeconds)
    {
        ScriptProcessRequest? observed = null;
        var options = ScriptHarnessOptions.Default with
        {
            // Stop at the launch boundary: inspect the real request without a slow child.
            OwnerFactory = request => { observed = request; throw new LaunchObservedException(); },
        };
        var cases = new ChangeAwareReleaseTests(options);
        await Should.ThrowAsync<LaunchObservedException>(() => caseName switch
        {
            "C1313_BaselineAuthority" => cases.C1313_BaselineAuthority(),
            "C1313_ParameterHops" => cases.C1313_ParameterHops(),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        });

        observed.ShouldNotBeNull();
        observed.CaseName.ShouldBe(caseName);
        observed.ExecutionBudget.TotalSeconds.ShouldBeGreaterThanOrEqualTo(600);
        observed.ExecutionBudget.TotalSeconds.ShouldBeGreaterThanOrEqualTo(2 * p95Seconds);
        observed.ExecutionBudget.TotalSeconds.ShouldBeGreaterThanOrEqualTo(1.5 * medianSeconds);
        // A case override must leave the shared default and cleanup contract intact.
        ScriptHarnessOptions.Default.ExecutionBudget.ShouldBe(TimeSpan.FromSeconds(300));
        observed.CleanupBudget.ShouldBe(ScriptHarnessOptions.Default.CleanupBudget);
    }

    private sealed class LaunchObservedException : Exception;
}
