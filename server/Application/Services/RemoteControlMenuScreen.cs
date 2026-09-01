namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0292: recognizer for Claude Code's <c>/remote-control</c> MANAGEMENT MENU — the modal the
/// TUI opens when the command is sent to a session whose bridge is already live (Disconnect /
/// Show QR / Continue, "Enter to select . Esc to continue"). The menu on screen is positive
/// evidence the bridge is armed, not a degradation — but nobody is at a keyboard, so unless it is
/// dismissed it stands forever and every subsequent input is queued inside the TUI instead of
/// becoming a prompt (session 70eb4c2d sat wedged five hours reading healthy).
///
/// Conservative on purpose: BOTH literals must be present — the <c>Remote Control</c> heading
/// alone is too generic (it appears in scrollback and help text). A false negative degrades to
/// today's behaviour; a false positive costs one Esc, which is measured as a no-op on an idle
/// empty composer (<see cref="ProviderContractCatalog"/>, CARD-0137). Static and DI-free like
/// <see cref="RemoteControlPolicy"/> so the launch preamble and the health watch share one matcher.
/// </summary>
public static class RemoteControlMenuScreen
{
    /// <summary>The menu's first action row — unique to this modal.</summary>
    public const string DisconnectLiteral = "Disconnect this session";

    /// <summary>The footer half: the menu itself documents Esc as "continue".</summary>
    public const string FooterLiteral = "Esc to continue";

    public static bool IsPresent(string? renderedScreen) =>
        !string.IsNullOrEmpty(renderedScreen)
        && renderedScreen.Contains(DisconnectLiteral, StringComparison.Ordinal)
        && renderedScreen.Contains(FooterLiteral, StringComparison.Ordinal);
}
