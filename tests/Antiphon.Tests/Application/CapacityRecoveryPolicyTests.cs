using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class CapacityRecoveryPolicyTests
{
    private static readonly Guid WaitA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid WaitB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Test]
    public void Card0412_V08_default_spacing_and_inclusive_jitter()
    {
        CapacityRecoveryPolicy.DefaultAdmissionIntervalSeconds.ShouldBe(60);
        CapacityRecoveryPolicy.DefaultJitterSeconds.ShouldBe(30);
        var jitter = CapacityRecoveryPolicy.StableJitterSeconds(WaitA, 0, 30);
        jitter.ShouldBeGreaterThanOrEqualTo(0);
        jitter.ShouldBeLessThanOrEqualTo(30);
        CapacityRecoveryPolicy.StableJitterSeconds(WaitA, 0, 30).ShouldBe(jitter);
        CapacityRecoveryPolicy.StableJitterSeconds(WaitA, 1, 30).ShouldNotBe(jitter);
        CapacityRecoveryPolicy.StableJitterSeconds(WaitA, 0, 0).ShouldBe(0);
    }

    [Test]
    public void Card0412_V08_checked_in_jitter_vectors()
    {
        var actual = (
            CapacityRecoveryPolicy.StableJitterSeconds(WaitA, 0, 30),
            CapacityRecoveryPolicy.StableJitterSeconds(WaitA, 1, 30),
            CapacityRecoveryPolicy.StableJitterSeconds(WaitB, 0, 30));
        actual.ShouldBe((0, 26, 21));
    }

    [Test]
    public void Card0412_V08_oldest_blocked_then_id()
    {
        var older = new CapacityRecoveryWait
        {
            Id = WaitB,
            BlockedAt = new DateTime(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc),
        };
        var younger = new CapacityRecoveryWait
        {
            Id = WaitA,
            BlockedAt = new DateTime(2026, 9, 6, 8, 1, 0, DateTimeKind.Utc),
        };
        CapacityRecoveryPolicy.CompareGrantOrder(older, younger).ShouldBeLessThan(0);
        var sameTimeLowerId = new CapacityRecoveryWait
        {
            Id = WaitA,
            BlockedAt = older.BlockedAt,
        };
        CapacityRecoveryPolicy.CompareGrantOrder(sameTimeLowerId, older).ShouldBeLessThan(0);
    }

    [Test]
    public void Card0412_V08_grant_candidates_include_admitted_awaiting_revalidation()
    {
        var ready = new CapacityRecoveryWait { State = CapacityRecoveryWaitState.Ready };
        var pending = new CapacityRecoveryWait { State = CapacityRecoveryWaitState.ActionPending };
        var admitted = new CapacityRecoveryWait
        {
            State = CapacityRecoveryWaitState.Admitted,
            NeedsRevalidationGrant = true,
        };
        var admittedSpent = new CapacityRecoveryWait
        {
            State = CapacityRecoveryWaitState.Admitted,
            NeedsRevalidationGrant = false,
        };
        CapacityRecoveryPolicy.IsGrantCandidate(ready).ShouldBeTrue();
        CapacityRecoveryPolicy.IsGrantCandidate(pending).ShouldBeTrue();
        CapacityRecoveryPolicy.IsGrantCandidate(admitted).ShouldBeTrue();
        CapacityRecoveryPolicy.IsGrantCandidate(admittedSpent).ShouldBeFalse();
    }

    [Test]
    public void Card0412_V08_enqueue_does_not_consume_attempts()
    {
        CapacityRecoveryPolicy.AttemptWouldExhaust(0, 3).ShouldBeFalse();
        CapacityRecoveryPolicy.AttemptWouldExhaust(3, 3).ShouldBeTrue();
    }

    [Test]
    public void Card0412_V26_settings_bounds()
    {
        CapacityRecoveryPolicy.ValidateOrThrow(new CapacityRecoverySettings
        {
            AdmissionIntervalSeconds = 1,
            JitterSeconds = 0,
            MaxEpisodeAttempts = 1,
            ReconciliationBatchSize = 1,
        });
        CapacityRecoveryPolicy.ValidateOrThrow(new CapacityRecoverySettings
        {
            AdmissionIntervalSeconds = 3600,
            JitterSeconds = 300,
            MaxEpisodeAttempts = 10,
            ReconciliationBatchSize = 1000,
        });
        Should.Throw<ArgumentOutOfRangeException>(() => CapacityRecoveryPolicy.ValidateOrThrow(
            new CapacityRecoverySettings { AdmissionIntervalSeconds = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() => CapacityRecoveryPolicy.ValidateOrThrow(
            new CapacityRecoverySettings { AdmissionIntervalSeconds = 3601 }));
        Should.Throw<ArgumentOutOfRangeException>(() => CapacityRecoveryPolicy.ValidateOrThrow(
            new CapacityRecoverySettings { JitterSeconds = -1 }));
        Should.Throw<ArgumentOutOfRangeException>(() => CapacityRecoveryPolicy.ValidateOrThrow(
            new CapacityRecoverySettings { JitterSeconds = 301 }));
        Should.Throw<ArgumentOutOfRangeException>(() => CapacityRecoveryPolicy.ValidateOrThrow(
            new CapacityRecoverySettings { MaxEpisodeAttempts = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() => CapacityRecoveryPolicy.ValidateOrThrow(
            new CapacityRecoverySettings { MaxEpisodeAttempts = 11 }));
        Should.Throw<ArgumentOutOfRangeException>(() => CapacityRecoveryPolicy.ValidateOrThrow(
            new CapacityRecoverySettings { ReconciliationBatchSize = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() => CapacityRecoveryPolicy.ValidateOrThrow(
            new CapacityRecoverySettings { ReconciliationBatchSize = 1001 }));
    }

    [Test]
    public void Card0412_V26_unspecified_attempts_honor_wall_death_cap()
    {
        var settings = new CapacityRecoverySettings { MaxEpisodeAttempts = null };
        CapacityRecoveryPolicy.EffectiveMaxAttempts(settings, 3).ShouldBe(3);
        CapacityRecoveryPolicy.EffectiveMaxAttempts(settings, 5).ShouldBe(5);
        settings.MaxEpisodeAttempts = 2;
        CapacityRecoveryPolicy.EffectiveMaxAttempts(settings, 5).ShouldBe(2);
    }

    [Test]
    public void Card0412_V08_eligible_at_adds_stable_jitter()
    {
        var clear = new DateTime(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc);
        var due = CapacityRecoveryPolicy.EligibleAt(clear, WaitA, 0, 30);
        due.ShouldBe(clear.AddSeconds(CapacityRecoveryPolicy.StableJitterSeconds(WaitA, 0, 30)));
        var crash = clear.AddMinutes(5);
        CapacityRecoveryPolicy.EligibleAt(clear, WaitA, 0, 30, crash).ShouldBe(crash);
    }
}
