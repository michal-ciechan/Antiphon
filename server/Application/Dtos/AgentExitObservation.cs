using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Dtos;

/// <summary>Actual process evidence, separate from the legacy integer adapter completion.</summary>
public sealed record AgentExitObservation(int? ExitCode, int? ExitSignal = null, ProcessStopIntentDto? ExitStopIntent = null)
{
    /// <summary>Legacy adapter value only; never persist this as an observed native code.</summary>
    public int CompatibilityCode => ExitSignal is > 0 ? -1 : ExitCode ?? -1;
    public static async Task<int> CompatibilityAsync(Task<AgentExitObservation> completion) =>
        (await completion).CompatibilityCode;
}
