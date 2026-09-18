using Antiphon.Server.Application.Services;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class RunnerIdentityTests
{
    /// <summary>
    /// V-511-17. The formatter the runner-build hold compares on: a rebuild always moves
    /// ProcessStartUtc, a real fix also moves the SHA, and a runner that cannot say is "unknown"
    /// (which releases on any answered probe rather than pinning a hold forever).
    /// </summary>
    [Test]
    public void C511_V17_Describe_formats_sha_or_version_at_process_start_and_unknown_for_null()
    {
        RunnerIdentity.Describe(null).ShouldBe("unknown");

        var sha = new string('a', 40);
        var start = new DateTime(2026, 9, 13, 15, 50, 0, DateTimeKind.Utc);
        RunnerIdentity.Describe(new RunnerBuildDto($"1.0.0+{sha}", sha, DateTime.UnixEpoch, start))
            .ShouldBe($"{sha}@2026-09-13T15:50:00.0000000Z");

        RunnerIdentity.Describe(new RunnerBuildDto("1.2.3-dev", null, DateTime.UnixEpoch, start))
            .ShouldBe("1.2.3-dev@2026-09-13T15:50:00.0000000Z");
    }
}
