using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0511 D-4. The identity of the runner process a launch decision was made against.
/// A rebuild always changes <see cref="RunnerBuildDto.ProcessStartUtc"/> and a real fix changes the
/// SHA too, so either is enough to justify exactly one fresh attempt after a runner-build hold.
/// Null build = an older runner that cannot say = <c>"unknown"</c>, which releases on any answer.
/// </summary>
public static class RunnerIdentity
{
    public const string Unknown = "unknown";

    public static string Describe(RunnerBuildDto? build)
    {
        if (build is null)
            return Unknown;
        var stamp = build.CommitSha is { Length: > 0 } sha ? sha : build.InformationalVersion;
        return $"{stamp}@{build.ProcessStartUtc:O}";
    }
}
