namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Why <c>WaitForReadyAsync</c> returned false, when the adapter can name it (CARD-0324).
/// Null on the adapter keeps today's generic "Agent process did not become ready."
/// </summary>
public sealed record AgentLaunchBlock(
    AgentLaunchBlockKind Kind,
    string Reason,
    string? GrokHome = null);

public enum AgentLaunchBlockKind
{
    ProviderSignInRequired = 1,
    TrustDialogNotCleared = 2,
}
