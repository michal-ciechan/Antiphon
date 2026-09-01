namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0241 S4: recognizer for Grok's in-turn <c>ask_user_question</c> popup. Same
/// shape as <see cref="RemoteControlMenuScreen"/> — static, DI-free, two independent
/// literals, both required. The act is TYPE THE ANSWER, never Esc: putting this
/// chrome in Grok <c>DetectFragments</c> would teach CARD-0137 to dismiss a live
/// question (the incident's second send).
///
/// Conservative on purpose. The 2026-08-23 incident did not capture a pty screen
/// (CARD-0159 §9), so the literals stay empty until
/// <c>GrokQuestionPopupCanaryTests</c> writes them from a real <c>SnapshotScreen</c>.
/// Empty literals mean <see cref="IsPresent"/> is always false: withhold-Esc is a
/// no-op, which degrades to today's behaviour. Do not guess fragments from the
/// JSONL question text — that is the tool payload, not the chrome.
/// </summary>
public static class GrokQuestionPopup
{
    /// <summary>
    /// First independent chrome literal. Empty until the headed canary measures it.
    /// </summary>
    public const string HeadingLiteral = "";

    /// <summary>
    /// Second independent chrome literal. Empty until the headed canary measures it.
    /// </summary>
    public const string FooterLiteral = "";

    public static bool IsPresent(string? renderedScreen) =>
        !string.IsNullOrEmpty(renderedScreen)
        && HeadingLiteral.Length > 0
        && FooterLiteral.Length > 0
        && renderedScreen.Contains(HeadingLiteral, StringComparison.Ordinal)
        && renderedScreen.Contains(FooterLiteral, StringComparison.Ordinal);
}
