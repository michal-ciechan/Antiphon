using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-1424 D-7: one capabilities read with its failure reason. <see cref="Capabilities"/> is set
/// on success; otherwise <see cref="FailureReason"/> names the failure and <see cref="Definite"/>
/// says whether it proves the runner is gone or refusing (rather than slow or throttled).
/// </summary>
public sealed record RunnerCapabilitiesProbe(RunnerCapabilitiesDto? Capabilities, string? FailureReason, bool Definite)
{
    /// <summary>A client that returns null without saying why (fakes, old clients).</summary>
    public const string NoAnswer = "no-answer";

    public static RunnerCapabilitiesProbe From(RunnerCapabilitiesDto? capabilities) =>
        capabilities is null ? new(null, NoAnswer, false) : new(capabilities, null, false);
}
