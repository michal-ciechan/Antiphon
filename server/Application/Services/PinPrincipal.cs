using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Resolved pin-API caller. Source is assigned by the server, never from the request body.</summary>
public sealed record PinPrincipal(
    bool IsOperator,
    Guid? UserId,
    Guid? SessionId,
    Guid? NamedAgentId)
{
    public static PinPrincipal Operator(Guid userId) => new(true, userId, null, null);

    public static PinPrincipal AgentSession(Guid sessionId, Guid namedAgentId) =>
        new(false, null, sessionId, namedAgentId);

    public PinInstructionSource Source => IsOperator ? PinInstructionSource.Operator : PinInstructionSource.Agent;
}
