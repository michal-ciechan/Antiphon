using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1153 S3: the production HTTP route extension on a random-port in-process Kestrel host
/// (the <c>TerminalSeatReleaseTests.SeatWire</c> shape), real JSON contracts, a synthetic key,
/// the real evidence service over a temp root. Design: V-9, V-10. No LocalHttpRunner.exe.
/// </summary>
[Category("Integration")]
public class RunnerAbsenceEvidenceContractTests
{
    /// <summary>
    /// V-9. Prepare then certify returns the full documented shape with Cache-Control: no-store;
    /// GET /sessions/{id} and /sessions/{id}/transcript stay 404 for that prepared id; an
    /// arbitrary id answers 404 on certify with no certificate body.
    /// </summary>
    [Test]
    public Task C1153_Real_unknown_transcript_has_a_separate_certificate() =>
        Card1153Pending.Skip("S3", nameof(C1153_Real_unknown_transcript_has_a_separate_certificate));

    /// <summary>
    /// V-10. No accepted certificate and no state change for: no key configured, a wrong request
    /// MAC, a request field changed after signing (route id versus body id), and one altered
    /// response bit. Canary: no key bytes or key path in any response or captured log line.
    /// </summary>
    [Test]
    [Arguments("missing-key")]
    [Arguments("wrong-request-MAC")]
    [Arguments("changed-request-field")]
    [Arguments("altered-response-bit")]
    public Task C1153_Http_authentication_covers_request_and_response(string condition) =>
        Card1153Pending.Skip("S3", nameof(C1153_Http_authentication_covers_request_and_response));
}
