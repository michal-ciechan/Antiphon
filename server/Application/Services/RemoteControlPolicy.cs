using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0212. The one place that turns the catalog's remoteControl capability into a decision.
/// Static and DI-free on purpose: AgentService, AgentControlService, CardService and
/// AgentSessionService are all hand-constructed in tests, and a constructor parameter here would
/// ripple through every harness for a pure lookup (precedent: AgentService.cs:1247).
/// </summary>
public static class RemoteControlPolicy
{
    public const string RefusalCode = "remote_control_refused";
    private static readonly AgentTuiRunnerCatalog Catalog = new();

    public static bool Permits(AgentKind kind) => Catalog.SupportsRemoteControl(kind);

    /// <summary>
    /// Throws 409 remote_control_refused when <paramref name="wanted"/> is true on a kind that
    /// cannot take it. <paramref name="what"/> names the request for the message
    /// ("agent 'X'", "start of agent 'X'", "spawn of card CARD-0012").
    /// </summary>
    public static void Require(AgentKind kind, bool wanted, string what)
    {
        if (!wanted || Permits(kind))
            return;

        throw new ConflictException(
            $"Remote control is not available for {kind} agents ({Reason(kind)}); {what} asked for it. "
            + "Send remoteControlEnabled: false (or omit remoteControl on the start request).",
            RefusalCode);
    }

    /// <summary>
    /// Message for the inherit-and-ignore arm (D3) and the deep gate. Logged at Warning.
    /// </summary>
    public static string IgnoredMessage(AgentKind kind, string what) =>
        $"Remote control is not available for {kind} agents ({Reason(kind)}); {what} asked for it and was ignored. "
        + "PATCH the agent with remoteControlEnabled: false to clear the stale flag. Nothing was typed.";

    private static string Reason(AgentKind kind)
    {
        if (!Enum.IsDefined(kind))
            return "not in the catalog";

        var capability = Catalog.Get(kind).Capabilities
            .FirstOrDefault(c => string.Equals(
                c.Name,
                AgentTuiRunnerCatalog.RemoteControlCapability,
                StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(capability?.Reason)
            ? "not in the catalog"
            : capability.Reason;
    }
}
