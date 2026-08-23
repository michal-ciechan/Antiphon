using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// The three delivery ceilings resolved for one <see cref="DeliveryBackend"/> write path
/// (CARD-0037 / CARD-0161).
///
/// <para>CARD-0030/CARD-0037: a pseudoconsole is served by a conhost binary, and the inbox one
/// (<c>%SystemRoot%\System32\conhost.exe</c>) strips <c>ESC[200~</c>/<c>ESC[201~</c> out of written
/// input, so every body we send arrives as TYPING and the TUI keeps one ~1 KB read chunk of it. The
/// shipped modern <c>conpty.dll</c> + <c>OpenConsole.exe</c> forward the markers, the TUI takes its
/// paste path instead, and real Claude accepted <b>86 400 bytes in a single write with zero loss,
/// 2/2</b>, through the production encoding (2026-08-12). CARD-0161: herdr's <c>pane.send_text</c>
/// is a third paste path with the same measured envelope (86 400 B exact, 2026-08-23 M1/M2).</para>
///
/// <para><b>Which is why these are a record and not constants.</b> A machine without the
/// redistributable falls back to the inbox conhost, and on that machine every number below has to
/// be the old one — raising them unconditionally would re-open the exact bug the ceilings were
/// introduced for. Herdr never becomes a <c>PtyBackend</c> value (process-wide spawn invariant).</para>
/// </summary>
/// <param name="Backend">The write path these numbers were measured against.</param>
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
    DeliveryBackend Backend,
    int BriefInlineMaxBytes,
    int ReplyInlineMaxChars,
    int SingleWriteMaxBytes,
    string Reason)
{
    /// <summary>
    /// True when these are the paste-path ceilings. Read it as "the markers reach the TUI", not as
    /// "large bodies are safe": everything here is a SINGLE-WRITE envelope. Herdr is a paste path
    /// (CARD-0161 M1/M2 measured placeholder collapse and marker survival).
    /// </summary>
    public bool IsPastePath => Backend != DeliveryBackend.InboxConhost;

    /// <summary>
    /// True when this agent kind's delivery must be rendered JOIN-SAFE: brief and refinement
    /// bodies always spill to a file (<see cref="ForAgentKind"/>) and only a pointer is typed,
    /// flattened server-side to one line so WE choose the separator
    /// (<c>DelegationReportFormatter.FlattenForJoiningComposer</c>).
    ///
    /// <para><b>The gate is default-deny — every kind except ClaudeCode</b> (CARD-0099 S2). Two
    /// different reasons put a kind here and it is worth keeping them apart:</para>
    ///
    /// <para><b>Grok — measured.</b> grok 1.0.5 (<c>GrokCanaryTests</c>,
    /// <c>FakeGrokContractTests</c>): a 4 450-character body arrives as 4 389 characters — every
    /// one of its 61 newlines dropped and the lines joined with NO separator. Nothing is lost; the
    /// structure is.</para>
    ///
    /// <para><b>Codex and every other non-Claude kind — unmeasured, therefore assumed hostile.</b>
    /// A composer contract that has never been measured cannot be relied on to keep the newlines we
    /// type, and the failure mode is silent: markdown headers fuse into the preceding sentence, list
    /// items concatenate, and — the hazard for a Code or Debug delegate specifically — a command, a
    /// test filter or a path merges with the next line's first word
    /// (<c>…task-xxxx-brief.mdEverything you need is there</c>). Spilling costs a delegate one file
    /// read; getting it wrong costs a whole task. Measurement can only ever RELAX this (move a kind
    /// out by naming it), never the reverse — see <c>CodexCanaryTests</c> for the Codex arm.</para>
    ///
    /// <para>This is a property of the receiving TUI, NOT of the pseudoconsole, which is why it is
    /// a separate axis from <see cref="Backend"/> and why it narrows only the inline ceiling below.
    /// Grok has no CARD-0027 one-chunk clip mode at all — a 4.4 KB TYPED body also lands whole — so
    /// the transport numbers here are right for it as they stand.</para>
    /// </summary>
    public static bool RequiresJoinSafeDelivery(AgentKind kind) => kind != AgentKind.ClaudeCode;

    /// <summary>
    /// The same pseudoconsole ceilings, narrowed for a kind whose composer is not trusted to keep
    /// the newlines we type (CARD-0084 S1, widened to default-deny by CARD-0099 S2). For such a
    /// kind the inline ceiling is <b>0</b>: every brief and every refinement takes the CARD-0025
    /// spill path, so the structure survives in a FILE, and only a pointer — flattened server-side
    /// so we choose the separator instead of the composer removing it — is ever typed.
    ///
    /// <para>Why a joined brief is a correctness problem and not a cosmetic one: markdown headers
    /// fuse into the preceding sentence, list items concatenate, and — the hazard for a Code or
    /// Debug delegate specifically — a command, a test filter or a path merges with the next line's
    /// first word (<c>…task-xxxx-brief.mdEverything you need is there</c>). Real briefs run 1.5-6 KB
    /// and the modern backend's inline ceiling is 43 200 bytes, so without this EVERY brief to a
    /// non-Claude delegate was typed inline and arrived as whatever its composer made of it.</para>
    ///
    /// <para><see cref="SingleWriteMaxBytes"/> and <see cref="ReplyInlineMaxChars"/> are deliberately
    /// untouched: the tripwire is about what the TRANSPORT has been measured to carry whole, and the
    /// report ceiling governs text travelling the other way, back to the caller.</para>
    /// </summary>
    public PtyDeliveryCeilings ForAgentKind(AgentKind kind) =>
        RequiresJoinSafeDelivery(kind)
            ? this with
            {
                BriefInlineMaxBytes = 0,
                Reason = $"{Reason}; {kind} {WhyJoinSafe(kind)}, so briefs and refinements always spill",
            }
            : this;

    /// <summary>Why this kind is on the join-safe path — a 0 ceiling in a log line has to explain itself.</summary>
    private static string WhyJoinSafe(AgentKind kind) => kind switch
    {
        AgentKind.Grok => "joins typed lines, measured (CARD-0084)",
        _ => "has no measured composer contract (CARD-0099 default-deny)",
    };

    public override string ToString() =>
        $"{Backend}: brief {BriefInlineMaxBytes:N0}B, reply {ReplyInlineMaxChars:N0} chars, "
        + $"single write {SingleWriteMaxBytes:N0}B ({Reason})";
}
