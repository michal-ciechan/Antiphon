using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// CARD-1153 S3: the production <c>SessionRunnerHttpClient</c> over a <c>StubHandler</c>
/// (the <c>SessionRunnerGenerationWireTests</c> shape), FakeTimeProvider, synthetic key.
/// Design: V-12, V-13. Shapes come from <see cref="AbsenceCertificateShape"/>; the pristine
/// control is signed by Code with the production signer.
/// </summary>
[Category("Integration")]
public class SessionRunnerAbsenceEvidenceClientTests
{
    /// <summary>
    /// V-12. Every shape yields the unsupported/unknown application result, never a validated
    /// proof, and exactly one POST was sent. ordinary-transcript is a complete empty transcript
    /// body served on the certify route; it is not a certificate.
    /// </summary>
    [Test]
    [Arguments("404")]
    [Arguments("501")]
    [Arguments("empty-200")]
    [Arguments("malformed")]
    [Arguments("ordinary-transcript")]
    [Arguments("missing-presence-field")]
    [Arguments("incomplete")]
    [Arguments("wrong-ID")]
    [Arguments("wrong-generation")]
    [Arguments("wrong-store")]
    [Arguments("wrong-nonce")]
    [Arguments("unknown-version")]
    [Arguments("unsigned")]
    [Arguments("bad-MAC")]
    public Task C1153_Rejects_noncertificate_wire_shapes(string shape) =>
        Card1153Pending.Skip("S3", nameof(C1153_Rejects_noncertificate_wire_shapes));

    /// <summary>
    /// V-13. within-5s-valid admits; over-5s-deadline (clock advanced inside the handler) and
    /// expired-validated-result (clock advanced after receipt) refuse; caller-cancellation
    /// propagates OperationCanceledException and is not a failure result. Every case sends
    /// exactly one request with one fresh nonce. No wall-clock sleep.
    /// </summary>
    [Test]
    [Arguments("within-5s-valid")]
    [Arguments("over-5s-deadline")]
    [Arguments("expired-validated-result")]
    [Arguments("caller-cancellation")]
    public Task C1153_Freshness_and_cancellation_are_bounded(string condition) =>
        Card1153Pending.Skip("S3", nameof(C1153_Freshness_and_cancellation_are_bounded));
}
