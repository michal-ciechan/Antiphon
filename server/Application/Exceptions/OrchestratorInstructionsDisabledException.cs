namespace Antiphon.Server.Application.Exceptions;

/// <summary>CARD-0822. Generation, notices, and the route are off together.</summary>
public sealed class OrchestratorInstructionsDisabledException : HttpException
{
    public OrchestratorInstructionsDisabledException()
        : base(404, "Orchestrator instructions are disabled.", "orchestrator_instructions_disabled")
    {
    }
}
