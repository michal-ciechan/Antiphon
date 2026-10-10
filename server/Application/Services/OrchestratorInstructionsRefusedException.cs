namespace Antiphon.Server.Application.Services;

/// <summary>
/// The renderer found secret-shaped text. The service keeps the previous file.
/// </summary>
public sealed class OrchestratorInstructionsRefusedException : InvalidOperationException
{
    public OrchestratorInstructionsRefusedException()
        : base("Orchestrator instructions refused: the render contained a secret-shaped token.")
    {
    }
}
