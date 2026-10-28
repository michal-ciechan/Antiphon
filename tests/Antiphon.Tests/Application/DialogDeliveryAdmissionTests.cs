using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1591. The queue names each withhold. A null description is exactly the write allow,
/// and every other case stays a withhold.
/// </summary>
[Category("Unit")]
public class DialogDeliveryAdmissionTests
{
    [Test]
    public void C1591_NamesEachWithholdAndNeverAllowsAWrite()
    {
        var id = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var epoch = Guid.NewGuid();
        var samples = new List<ProviderDialogObservation?>();

        ProviderDialogObservation Observe(
            ProviderDialogState state,
            bool known,
            bool adopted = false,
            bool uncertain = false,
            ProviderDialogState? screen = null,
            DateTime? started = null,
            Guid? session = null,
            Guid? runtime = null)
        {
            var observation = new ProviderDialogObservation(
                session ?? id,
                runtime ?? epoch,
                started ?? generation,
                4,
                5,
                state,
                InputHistoryKnown: known,
                Adopted: adopted,
                InputUncertain: uncertain,
                ScreenState: screen ?? state);
            samples.Add(observation);
            return observation;
        }

        var clear = Observe(ProviderDialogState.Clear, known: true);
        DialogDeliveryAdmission.Describe(clear, id, generation).ShouldBeNull();
        ProviderDialogObservationService.Refusal(clear, id, generation).ShouldBeNull();

        samples.Add(null);
        var missing = DialogDeliveryAdmission.Describe(null, id, generation);
        missing.ShouldNotBeNull();
        missing.Value.Text.ShouldBe(DialogDeliveryAdmission.SnapshotUnavailable);
        missing.Value.Code.ShouldBe(ConditionalInputOutcomes.ObservationUnknown);
        missing.Value.ChargeAttempt.ShouldBeFalse();

        var wrongSession = Observe(ProviderDialogState.Clear, known: true, session: Guid.NewGuid());
        var wrong = DialogDeliveryAdmission.Describe(wrongSession, id, generation);
        wrong.ShouldNotBeNull();
        wrong.Value.Text.ShouldBe(DialogDeliveryAdmission.SnapshotUnavailable);
        wrong.Value.ChargeAttempt.ShouldBeFalse();

        var emptyEpoch = Observe(ProviderDialogState.Clear, known: true, runtime: Guid.Empty);
        var epochWithhold = DialogDeliveryAdmission.Describe(emptyEpoch, id, generation);
        epochWithhold.ShouldNotBeNull();
        epochWithhold.Value.Text.ShouldBe(DialogDeliveryAdmission.SnapshotUnavailable);
        epochWithhold.Value.ChargeAttempt.ShouldBeFalse();

        var staleObservation = Observe(
            ProviderDialogState.Clear, known: true, started: generation.AddMinutes(-5));
        var stale = DialogDeliveryAdmission.Describe(staleObservation, id, generation);
        stale.ShouldNotBeNull();
        stale.Value.Text.ShouldBe(DialogDeliveryAdmission.GenerationMismatch);
        stale.Value.ChargeAttempt.ShouldBeTrue();
        stale.Value.Code.ShouldBe(ConditionalInputOutcomes.ObservationUnknown);

        var blockedObservation = Observe(ProviderDialogState.Blocked, known: true);
        var blocked = DialogDeliveryAdmission.Describe(blockedObservation, id, generation);
        blocked.ShouldNotBeNull();
        blocked.Value.Text.ShouldBe(DialogDeliveryAdmission.BlocksInput);
        blocked.Value.Code.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        blocked.Value.ChargeAttempt.ShouldBeFalse();

        var adoptedClear = Observe(
            ProviderDialogState.Unknown, known: false, adopted: true, screen: ProviderDialogState.Clear);
        var adopted = DialogDeliveryAdmission.Describe(adoptedClear, id, generation);
        adopted.ShouldNotBeNull();
        adopted.Value.Text.ShouldBe(DialogDeliveryAdmission.AdoptedSession);
        adopted.Value.ChargeAttempt.ShouldBeTrue();
        ProviderDialogObservationService.Refusal(adoptedClear, id, generation)
            .ShouldBe(ConditionalInputOutcomes.ObservationUnknown);

        var adoptedModal = Observe(
            ProviderDialogState.Unknown, known: false, adopted: true, screen: ProviderDialogState.Blocked);
        var adoptedBlocked = DialogDeliveryAdmission.Describe(adoptedModal, id, generation);
        adoptedBlocked.ShouldNotBeNull();
        adoptedBlocked.Value.Text.ShouldBe(DialogDeliveryAdmission.BlocksInput);
        adoptedBlocked.Value.Code.ShouldBe(ConditionalInputOutcomes.ModalBlocked);
        adoptedBlocked.Value.ChargeAttempt.ShouldBeFalse();
        ProviderDialogObservationService.Refusal(adoptedModal, id, generation)
            .ShouldBe(ConditionalInputOutcomes.ObservationUnknown);

        var uncertainObservation = Observe(
            ProviderDialogState.Unknown, known: false, uncertain: true, screen: ProviderDialogState.Clear);
        var uncertain = DialogDeliveryAdmission.Describe(uncertainObservation, id, generation);
        uncertain.ShouldNotBeNull();
        uncertain.Value.Text.ShouldBe(DialogDeliveryAdmission.InputUncertain);
        uncertain.Value.ChargeAttempt.ShouldBeTrue();

        var partialObservation = Observe(
            ProviderDialogState.Unknown, known: false, screen: ProviderDialogState.Unknown);
        var partial = DialogDeliveryAdmission.Describe(partialObservation, id, generation);
        partial.ShouldNotBeNull();
        partial.Value.Text.ShouldBe(DialogDeliveryAdmission.ScreenUnknown);
        partial.Value.ChargeAttempt.ShouldBeTrue();

        var adoptedPartial = Observe(
            ProviderDialogState.Unknown, known: false, adopted: true, screen: ProviderDialogState.Unknown);
        var adoptedPartialWithhold = DialogDeliveryAdmission.Describe(adoptedPartial, id, generation);
        adoptedPartialWithhold.ShouldNotBeNull();
        adoptedPartialWithhold.Value.Text.ShouldBe(DialogDeliveryAdmission.ScreenUnknown);

        var genericObservation = Observe(
            ProviderDialogState.Unknown, known: false, screen: ProviderDialogState.Clear);
        var generic = DialogDeliveryAdmission.Describe(genericObservation, id, generation);
        generic.ShouldNotBeNull();
        generic.Value.Text.ShouldBe(DialogDeliveryAdmission.ObservationUnavailable);
        generic.Value.ChargeAttempt.ShouldBeTrue();

        foreach (var sample in samples)
        {
            var described = DialogDeliveryAdmission.Describe(sample, id, generation);
            var refusal = ProviderDialogObservationService.Refusal(sample, id, generation);
            (described is null).ShouldBe(refusal is null);
        }
    }
}
