namespace Antiphon.Server.Application.Services;

/// <summary>
/// Frozen reply-contract semantics between an agent's turn output and the channel dispatcher.
/// </summary>
public static class ChannelContracts
{
    /// <summary>The token an agent replies with to suppress channel delivery for a turn.</summary>
    public const string NoReplyToken = "NO_REPLY";

    /// <summary>
    /// True when a turn's response is a silent turn: the ENTIRE response, trimmed, is exactly
    /// <see cref="NoReplyToken"/> (case-insensitive). Leading or trailing prose defeats it —
    /// a real answer that merely mentions NO_REPLY must still be delivered.
    /// </summary>
    public static bool IsNoReply(string? turnResponse) =>
        turnResponse is not null
        && string.Equals(turnResponse.Trim(), NoReplyToken, StringComparison.OrdinalIgnoreCase);
}
