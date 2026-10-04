using Antiphon.Agents.Pty;

namespace Antiphon.SessionRunner;

/// <summary>Daemon composition only; direct runtimes keep their explicit instance override.</summary>
public static class PtyBackendConfiguration
{
    public static string EffectiveRequest(string? configured, string? environment) =>
        !string.IsNullOrEmpty(environment) ? environment : configured ?? string.Empty;

    public static void LogDecision(ILogger logger, PtyBackendDecision decision)
    {
        if (decision.RequiresWarning)
            logger.LogWarning("PTY backend: {Decision}", decision);
        else
            logger.LogInformation("PTY backend: {Decision}", decision);
    }
}
