using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// Refuses a launch that the connected runner explicitly says it cannot observe. CARD-0511: the
/// build the refusal was decided on travels with the exception, so the supervisor can hold until
/// a DIFFERENT runner identity answers rather than laddering against the same stale binary.
/// </summary>
public sealed class RunnerCapabilityMismatchException(string message, RunnerBuildDto? build = null)
    : Exception(message)
{
    public RunnerBuildDto? Build { get; } = build;

    /// <summary>The runner identity this refusal was decided against; <c>"unknown"</c> when the
    /// runner could not say (an older runner, or a probe that found no endpoint).</summary>
    public string RunnerIdentity { get; } =
        Antiphon.Server.Application.Services.RunnerIdentity.Describe(build);
}
