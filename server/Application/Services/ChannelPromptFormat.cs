using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The frozen envelope grammar for channel-originated prompts — the single authority for how an
/// inbound chat message is framed when typed into an agent's session, and for the batch markers
/// used when several queued messages are delivered in one turn. Everything that renders, parses,
/// or documents these shapes (bridge, queue batching, preamble text, fakeclaude, docs) references
/// these members; nothing re-declares the strings.
/// </summary>
public static class ChannelPromptFormat
{
    /// <summary>Marker heading the older messages of a batched delivery (context only).</summary>
    public const string BatchContextMarker = "[Chat messages since your last reply - for context]";

    /// <summary>Marker heading the newest message of a batched delivery (the one to respond to).</summary>
    public const string BatchCurrentMarker = "[Current message - respond to this]";

    /// <summary>
    /// One message's envelope: <c>[Telegram "Family" — Mike (@mciechan) 14:32] text</c>, or for a
    /// direct message <c>[Telegram direct message — Mike (@mciechan) 14:32] text</c>. The header is
    /// untrusted channel metadata by contract (the preamble says so); it exists to orient the agent,
    /// not to authenticate anyone.
    /// </summary>
    public static string Format(
        ChatChannel channel,
        string author,
        string? username,
        DateTimeOffset timestamp,
        string text,
        TimeZoneInfo timeZone)
    {
        var where = channel.Kind == ChatChannelKind.Direct
            ? "direct message"
            : $"\"{channel.Title ?? channel.ExternalId}\"";
        var who = string.IsNullOrWhiteSpace(username) ? author : $"{author} (@{username})";
        var localTime = TimeZoneInfo.ConvertTime(timestamp, timeZone).ToString("HH:mm");
        return $"[{Capitalize(channel.Provider)} {where} — {who} {localTime}] {text.Trim()}";
    }

    /// <summary>
    /// The batched-delivery body: all-but-newest under the context marker, the newest under the
    /// current marker. Callers pass already-enveloped bodies (each element is a
    /// <see cref="Format"/> result).
    /// </summary>
    public static string FormatBatch(IReadOnlyList<string> contextBodies, string currentBody)
    {
        if (contextBodies.Count == 0)
            return currentBody;

        return BatchContextMarker + "\n"
            + string.Join("\n", contextBodies)
            + "\n\n" + BatchCurrentMarker + "\n"
            + currentBody;
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
