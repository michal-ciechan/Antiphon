using Antiphon.Tests.TestHelpers;
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
    public Task C1153_Validator_requires_every_fact(string condition) =>
        Card1153Pending.Skip("S3", nameof(C1153_Validator_requires_every_fact));
}
