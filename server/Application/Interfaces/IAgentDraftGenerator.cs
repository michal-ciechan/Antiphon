using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

public interface IAgentDraftGenerator
{
    Task<AgentDraftSuggestion?> GenerateDraftAsync(string description, CancellationToken ct);
}

public sealed record AgentDraftSuggestion(
    string? Name,
    string? WorkingDirectory,
    string? Details,
    AgentAssignmentPolicy? AssignmentPolicy);
