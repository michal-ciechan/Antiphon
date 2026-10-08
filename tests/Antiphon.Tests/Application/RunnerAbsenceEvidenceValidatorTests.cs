using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1153 S3: the pure <c>RunnerAbsenceEvidenceValidator</c> one-condition flip table
/// (the S1 lesson: a whitelist of positive conditions; anything unknown keeps today's
/// behaviour). Each case first admits an independently built pristine certificate (signed with
/// the synthetic key, elapsed under 5 s, fresh), then changes exactly one condition and
/// requires refusal. Member flips come from <c>Agents.AbsenceCertificateShape.Flip</c>;
/// elapsed-over-5s, age-expired, unauthenticated and bad-MAC are validator inputs.
/// generation-sub-microsecond is the documented tolerance and must admit.
/// </summary>
[Category("Unit")]
public class RunnerAbsenceEvidenceValidatorTests
{
    private static readonly DateTime Generation = new DateTime(2026, 10, 8, 5, 6, 7, DateTimeKind.Utc).AddTicks(1230);
    private static readonly DateTimeOffset SentAt = new(2026, 10, 8, 6, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("version-missing")]
    [Arguments("version-0")]
    [Arguments("version-2")]
    [Arguments("outcome-missing")]
    [Arguments("outcome-other")]
    [Arguments("sessionId-missing")]
    [Arguments("sessionId-mismatch")]
    [Arguments("sessionId-empty")]
    [Arguments("generation-missing")]
    [Arguments("generation-one-microsecond")]
    [Arguments("store-missing")]
    [Arguments("store-mismatch")]
    [Arguments("store-empty")]
    [Arguments("epoch-missing")]
    [Arguments("epoch-empty")]
    [Arguments("nonce-missing")]
    [Arguments("nonce-mismatch")]
    [Arguments("complete-missing")]
    [Arguments("complete-false")]
    [Arguments("creationObserved-missing")]
    [Arguments("creationObserved-true")]
    [Arguments("native-missing")]
    [Arguments("native-true")]
    [Arguments("sidecar-missing")]
    [Arguments("sidecar-true")]
    [Arguments("process-missing")]
    [Arguments("process-true")]
    [Arguments("identityClosed-missing")]
    [Arguments("identityClosed-false")]
    [Arguments("unknown-extra-member")]
    [Arguments("elapsed-over-5s")]
    [Arguments("age-expired")]
    [Arguments("unauthenticated")]
    [Arguments("bad-MAC")]
    [Arguments("generation-sub-microsecond")]
    public Task C1153_Validator_requires_every_fact(string condition)
    {
        var sessionId = Guid.NewGuid();
        var store = Guid.NewGuid();
        var epoch = Guid.NewGuid();
        var nonce = RunnerAbsenceEvidence.NewNonce();
        var expected = new RunnerAbsenceExpectation(sessionId, Generation, store, nonce);
        var receivedAt = SentAt.AddSeconds(1);
        var pristine = AbsenceCertificateShape.Pristine(sessionId, Generation, store, epoch, nonce, receivedAt.UtcDateTime);

        // Independent pristine positive under exactly the same expectation and clock.
        var control = RunnerAbsenceEvidenceValidator.Validate(pristine.DeepClone(), expected,
            RunnerAbsenceAuthentication.Verified, SentAt, receivedAt, receivedAt.AddSeconds(1));
        control.Kind.ShouldBe(SessionRunnerAbsenceEvidenceKind.Proven, control.Reason);
        control.Evidence!.SessionId.ShouldBe(sessionId);
        control.Evidence.RunnerStoreId.ShouldBe(store);
        control.Evidence.RuntimeEpoch.ShouldBe(epoch);
        control.Evidence.ExpiresAt.ShouldBe(receivedAt.AddSeconds(5));

        var shape = pristine;
        var authentication = RunnerAbsenceAuthentication.Verified;
        var received = receivedAt;
        var now = receivedAt.AddSeconds(1);
        switch (condition)
        {
            case "elapsed-over-5s": received = SentAt.AddSeconds(5).AddMilliseconds(1); now = received; break;
            case "age-expired": now = receivedAt.AddSeconds(5).AddMilliseconds(1); break;
            case "unauthenticated": authentication = RunnerAbsenceAuthentication.Missing; break;
            case "bad-MAC": authentication = RunnerAbsenceAuthentication.Mismatch; break;
            default: shape = AbsenceCertificateShape.Flip(pristine, condition); break;
        }

        var result = RunnerAbsenceEvidenceValidator.Validate(shape, expected, authentication, SentAt, received, now);

        if (condition == "generation-sub-microsecond")
        {
            result.Kind.ShouldBe(SessionRunnerAbsenceEvidenceKind.Proven, "sub-microsecond is the documented generation tolerance");
            return Task.CompletedTask;
        }

        result.Kind.ShouldBe(SessionRunnerAbsenceEvidenceKind.Unknown, $"{condition} must refuse");
        result.Evidence.ShouldBeNull();
        result.IsProven.ShouldBeFalse();
        return Task.CompletedTask;
    }
}
