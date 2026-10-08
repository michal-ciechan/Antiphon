using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1153 S3: the phone-home operations through the real <c>PhoneHomeCommandDispatcher</c>
/// and <c>PhoneHomeRuntimeAdapter</c> with production framing in both directions. Design: V-11.
/// The unauthenticated-transport half is the existing
/// <c>PhoneHomeConnectionTests.Authentication_is_required_at_both_endpoints</c> (R-10).
/// </summary>
[Category("Integration")]
public class RunnerAbsenceEvidencePhoneHomeTests
{
    /// <summary>
    /// V-11. prepare-and-certify: both frames round-trip with matching epoch, request id and
    /// operation and the certificate carries the connection's store. unsupported-or-foreign-store:
    /// typed error frames (unsupported operation, foreign store) without entering the runtime.
    /// transcript-of-prepared-id: the Transcript operation for a prepared, never-created id stays
    /// a typed 404 error frame, never an empty result.
    /// </summary>
    [Test]
    [Arguments("prepare-and-certify")]
    [Arguments("unsupported-or-foreign-store")]
    [Arguments("transcript-of-prepared-id")]
    public Task C1153_Authenticated_operation_preserves_binding(string condition) =>
        Card1153Pending.Skip("S3", nameof(C1153_Authenticated_operation_preserves_binding));
}
