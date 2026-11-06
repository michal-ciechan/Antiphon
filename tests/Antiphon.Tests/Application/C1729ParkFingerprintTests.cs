using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class C1729ParkFingerprintTests
{
    [Test]
    public void Digest_matches_the_pinned_vector()
    {
        // Captured from BlockedTaskParkingService.Digest at 89bbb05617900d963a99feeabc5177ccefba56bc.
        ParkFingerprint.Digest("antiphon").ShouldBe(
            "F37E61914643F87FFB3AE33141E4D7B9103E7ABA72F979AC19839CED71A68D93",
            "park-digest-vector");
        ParkFingerprint.Digest("").ShouldBe(
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            "park-digest-empty");
        ParkFingerprint.Digest(null).ShouldBeNull("park-digest-null");
    }
}
