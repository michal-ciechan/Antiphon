using Antiphon.Agents.Pty;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// The three delivery ceilings that are COUPLED TO THE PSEUDOCONSOLE, resolved for one backend.
///
/// <para>CARD-0030/CARD-0037: a pseudoconsole is served by a conhost binary, and the inbox one
/// (<c>%SystemRoot%\System32\conhost.exe</c>) strips <c>ESC[200~</c>/<c>ESC[201~</c> out of written
/// input, so every body we send arrives as TYPING and the TUI keeps one ~1 KB read chunk of it. The
/// shipped modern <c>conpty.dll</c> + <c>OpenConsole.exe</c> forward the markers, the TUI takes its
/// paste path instead, and real Claude accepted <b>86 400 bytes in a single write with zero loss,
/// 2/2</b>, through <see cref="PtyAgentRunner"/> and the production encoding (2026-08-12).</para>
///
/// <para><b>Which is why these are a record and not constants.</b> A machine without the
/// redistributable falls back to the inbox conhost (<c>PtyBackendPolicy</c>), and on that machine
/// every number below has to be the old one — raising them unconditionally would re-open the exact
/// bug the ceilings were introduced for.</para>
/// </summary>
/// <param name="Backend">The pseudoconsole these numbers were measured against.</param>
/// <param name="BriefInlineMaxBytes">
/// Largest delegation brief typed inline (UTF-8 bytes); above it the brief spills to a file and a
/// pointer is typed instead.
/// </param>
/// <param name="ReplyInlineMaxChars">
/// Largest report forwarded whole to the caller (UTF-16 chars — see <see cref="DelegationSettings"/>
/// for why the two units differ and why the char ceiling is derived from the byte one).
/// </param>
/// <param name="SingleWriteMaxBytes">
/// The tripwire, in UTF-8 bytes: the largest body we have MEASURED arriving whole in ONE write on
/// this backend. A delivery past it is still sent — refusing would strand the message — but it
/// raises <c>AgentIncidentKind.OversizedTerminalDelivery</c> instead of going quietly.
/// </param>
/// <param name="Reason">Why this backend was chosen; carried so a log line can explain the numbers.</param>
public sealed record PtyDeliveryCeilings(
    PtyBackend Backend,
    int BriefInlineMaxBytes,
    int ReplyInlineMaxChars,
    int SingleWriteMaxBytes,
    string Reason)
{
    /// <summary>
    /// True when these are the paste-path ceilings. Read it as "the markers reach the TUI", not as
    /// "large bodies are safe": everything here is a SINGLE-WRITE envelope (see
    /// <see cref="DelegationSettings.ModernPtySingleWriteMaxBytes"/>).
    /// </summary>
    public bool IsPastePath => Backend == PtyBackend.ModernConPty;

    public override string ToString() =>
        $"{Backend}: brief {BriefInlineMaxBytes:N0}B, reply {ReplyInlineMaxChars:N0} chars, "
        + $"single write {SingleWriteMaxBytes:N0}B ({Reason})";
}
