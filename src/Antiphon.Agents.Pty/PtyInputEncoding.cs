namespace Antiphon.Agents.Pty;

/// <summary>
/// The ONE way to encode a text body for typing into an agent TUI (REQUIREMENT — live misses
/// 2026-07-29, 2026-07-31, 2026-08-08; mechanism nailed down by probe runs against real Claude):
///
/// 1. Line endings normalize to LF: a <c>\n</c> in written PTY input is ALWAYS a literal newline
///    to Claude, while a <c>\r</c> MID-body acts as Enter and submits the fragment before it.
///    CR/CRLF bodies (Windows raw-string literals, Telegram sources) fragment without this — and
///    when the trailing CR falls inside the TUI's paste heuristic window it is folded to a
///    newline instead, leaving the whole body stranded UNSUBMITTED in the composer (the
///    2026-08-08 relaunch: a CRLF card prompt sat in the composer for half an hour).
/// 2. Multi-line bodies travel wrapped in bracketed paste (<c>\e[200~..\e[201~</c>): ConPTY
///    chunks large writes at arbitrary boundaries, and without the markers the paste heuristic
///    fragments the body at line breaks (2026-07-29: a 2.4 KB message arrived as its last
///    fragment only).
/// 3. The submitting <c>\r</c> is the CALLER's job and must go as a separate, delayed,
///    unbracketed write — never concatenated to the body.
///
/// Pinned against a live ConPTY peer by FakeClaudeContractTests; the queue path's use is pinned
/// by SessionMessageQueuePtyIntegrationTests. Any code path that types into a session must go
/// through this.
/// </summary>
public static class PtyInputEncoding
{
    public const string PasteStart = "\x1b[200~";
    public const string PasteEnd = "\x1b[201~";

    /// <summary>LF-normalized, end-trimmed body — safe to write and to match against the screen.</summary>
    public static string NormalizeBody(string body) => body.ReplaceLineEndings("\n").TrimEnd();

    /// <summary>Wraps an already-normalized multi-line body in bracketed paste; single lines pass through.</summary>
    public static string WrapIfMultiline(string normalizedBody) =>
        normalizedBody.Contains('\n')
            ? PasteStart + normalizedBody + PasteEnd
            : normalizedBody;

    /// <summary>Normalize + wrap in one step, for callers that don't need the intermediate form.</summary>
    public static string EncodeBody(string body) => WrapIfMultiline(NormalizeBody(body));
}
