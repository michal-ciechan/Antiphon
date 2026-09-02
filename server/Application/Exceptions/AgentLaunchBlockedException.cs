using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0324: <c>WaitForReadyAsync</c> returned false and the adapter named why.
/// Message is <see cref="AgentLaunchBlock.Reason"/> so existing launch catches persist it
/// as <c>AgentSession.FailureReason</c> unchanged.
/// </summary>
public sealed class AgentLaunchBlockedException : InvalidOperationException
{
    public AgentLaunchBlock Block { get; }

    public AgentLaunchBlockedException(AgentLaunchBlock block)
        : base(block.Reason)
    {
        Block = block;
    }
}
