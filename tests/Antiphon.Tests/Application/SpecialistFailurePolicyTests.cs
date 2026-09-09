using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class SpecialistFailurePolicyTests
{
    [Test]
    [Arguments(SpecialistAttemptOutcome.InvalidReading)]
    [Arguments(SpecialistAttemptOutcome.Empty)]
    [Arguments(SpecialistAttemptOutcome.ToolAttempt)]
    [Arguments(SpecialistAttemptOutcome.IdentityMismatch)]
    [Arguments(SpecialistAttemptOutcome.InputUnsupported)]
    public void Card0415_V23_hard_contract_failure_quarantines_immediately(SpecialistAttemptOutcome outcome)
    {
        SpecialistFailurePolicy.Evaluate(outcome, 0, 3, true, true).ShouldBe(new(0, true, false));
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(10)]
    public void Card0415_V23_mixed_transients_quarantine_only_at_the_configured_threshold(int threshold)
    {
        SpecialistAttemptOutcome[] outcomes = [SpecialistAttemptOutcome.TimedOutAfterDispatch,
            SpecialistAttemptOutcome.DeliveryUnconfirmed, SpecialistAttemptOutcome.ExpiredBeforeDispatch,
            SpecialistAttemptOutcome.TaskFailedTransport, SpecialistAttemptOutcome.TaskFailedUnknown];
        var streak = 0;
        for (var i = 1; i <= threshold; i++)
        {
            var decision = SpecialistFailurePolicy.Evaluate(outcomes[(i - 1) % outcomes.Length], streak, threshold, true, true);
            decision.TransientFailures.ShouldBe(i);
            decision.Quarantine.ShouldBe(i == threshold);
            decision.Reset.ShouldBeFalse();
            streak = decision.TransientFailures;
        }
    }

    [Test]
    [Arguments(SpecialistAttemptOutcome.Held)]
    [Arguments(SpecialistAttemptOutcome.QuotaUnavailable)]
    [Arguments(SpecialistAttemptOutcome.AuthenticationUnavailable)]
    [Arguments(SpecialistAttemptOutcome.Busy)]
    [Arguments(SpecialistAttemptOutcome.DeclaredButUnprovisioned)]
    [Arguments(SpecialistAttemptOutcome.Disabled)]
    [Arguments(SpecialistAttemptOutcome.CallerCanceled)]
    [Arguments(SpecialistAttemptOutcome.HostShutdown)]
    public void Card0415_V23_nonfailure_verdicts_preserve_candidate_streak(SpecialistAttemptOutcome outcome) =>
        SpecialistFailurePolicy.Evaluate(outcome, 2, 3, true, true).ShouldBe(new(2, false, false));

    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public void Card0415_V23_late_or_unclaimed_verdicts_neither_fail_nor_reset(bool current, bool claimed)
    {
        foreach (var outcome in Enum.GetValues<SpecialistAttemptOutcome>())
            SpecialistFailurePolicy.Evaluate(outcome, 2, 3, current, claimed).ShouldBe(new(2, false, false));
    }

    [Test]
    [Arguments(SpecialistAttemptOutcome.ValidReading)]
    [Arguments(SpecialistAttemptOutcome.ValidQualificationBatch)]
    public void Card0415_V23_current_valid_success_resets_only_the_candidate(SpecialistAttemptOutcome outcome) =>
        SpecialistFailurePolicy.Evaluate(outcome, 2, 3, true, true).ShouldBe(new(0, false, true));

    [Test]
    public void Card0415_V12_defaults_preserve_full_budget_and_three_transient_threshold()
    {
        var settings = new DelegationSettings();
        settings.CheckInterpreterFirstAttemptSeconds.ShouldBeNull();
        settings.CheckInterpreterWaitSeconds.ShouldBe(60);
        settings.CheckInterpreterTransientFailureThreshold.ShouldBe(3);
        new DelegationSettingsValidator().Validate(null, settings).Succeeded.ShouldBeTrue();
    }

    [Test]
    [Arguments(0, 3, false)]
    [Arguments(-1, 3, false)]
    [Arguments(61, 3, false)]
    [Arguments(60, 3, true)]
    [Arguments(1, 3, true)]
    [Arguments(60, 1, false)]
    [Arguments(60, 11, false)]
    [Arguments(60, 2, true)]
    [Arguments(60, 10, true)]
    public void Card0415_V12_settings_enforce_allocation_and_threshold_bounds(int allocation, int threshold, bool valid)
    {
        var settings = new DelegationSettings { CheckInterpreterFirstAttemptSeconds = allocation,
            CheckInterpreterTransientFailureThreshold = threshold };
        new DelegationSettingsValidator().Validate(null, settings).Succeeded.ShouldBe(valid);
    }
}
