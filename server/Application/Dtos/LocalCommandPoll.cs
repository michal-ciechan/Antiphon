using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// A local TUI command to type into a live session without going through the message queue
/// (CARD-0143). Shares the per-session lock; none of the prompt-delivery verdicts, incidents,
/// retries, or always-on kills.
/// </summary>
public sealed record LocalCommandPoll(
    AgentKind Kind,
    string Command,
    IReadOnlyList<string> Navigation,
    bool OpensOverlay,
    int OverlaySettleMs,
    int PanelTimeoutSeconds);

public abstract record LocalCommandPollResult
{
    public sealed record Skipped(string Reason) : LocalCommandPollResult;

    /// <summary>Composer never showed the body; Enter was withheld. Nothing queued, no incident, no kill.</summary>
    public sealed record NotAccepted : LocalCommandPollResult;

    /// <summary>Enter was sent but the output sequence never advanced within the panel timeout.</summary>
    public sealed record PanelNotRendered : LocalCommandPollResult;

    public sealed record Sent(string Buffer) : LocalCommandPollResult;
}
