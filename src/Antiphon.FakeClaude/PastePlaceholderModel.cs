namespace Antiphon.FakeClaude;

/// <summary>
/// Models the OTHER half of the real composer's paste path (CARD-0037): a paste is not echoed, it
/// is COLLAPSED to <c>[Pasted text #N +M lines]</c> and the body never appears on the screen at all.
///
/// <para><b>Why it matters enough to model.</b> Delivery verification reads the rendered screen for
/// evidence of the body before it presses Enter (<c>ComposerDeliveryEvidence</c>, and the queue
/// withholds the Enter and can kill an always-on session when it finds none). Head-or-tail matching
/// finds nothing on a collapsed paste — so the moment the bracketed-paste markers actually reach
/// the TUI, every large delivery would verify as wedged. That is a behaviour worth having a peer
/// that can exhibit it, for the same reason the clip model exists: a fake that cannot produce the
/// failure cannot protect against it.</para>
///
/// <para><b>Opt-in, and it stays that way.</b> The line count at which real Claude collapses a paste
/// has not been measured — only that it does for the multi-KB bodies CARD-0027/0030 pasted — so a
/// default-on model would be asserting a threshold nobody observed. It is also the render every
/// existing fake-driven test reads the composer echo from, and those pins are worth keeping.
/// <see cref="MinLines"/> is the knob; 6 is a stand-in, not a measurement.</para>
/// </summary>
internal sealed class PastePlaceholderModel
{
    /// <summary>Off / unset = echo the paste, as before. <c>1</c>|<c>on</c>|<c>true</c> = collapse it.</summary>
    public const string ModeVar = "ANTIPHON_FAKE_PASTE_PLACEHOLDER";
    public const string MinLinesVar = "ANTIPHON_FAKE_PASTE_PLACEHOLDER_MIN_LINES";

    private int _pasteIndex;

    /// <summary>Pastes shorter than this are rendered verbatim, the way a short paste really is.</summary>
    public int MinLines { get; }

    internal PastePlaceholderModel(int minLines = 6) => MinLines = Math.Max(1, minLines);

    /// <summary>Null unless <see cref="ModeVar"/> asks for it — the fake's default is unchanged.</summary>
    public static PastePlaceholderModel? FromEnvironment()
    {
        var mode = (Environment.GetEnvironmentVariable(ModeVar) ?? string.Empty).Trim().ToLowerInvariant();
        if (mode is "" or "0" or "off" or "false") return null;
        if (mode is not ("1" or "on" or "true" or "yes"))
            throw new ArgumentException($"{ModeVar}='{mode}' is not a mode. Use 1 (on) or leave it unset.");

        return new PastePlaceholderModel(
            int.TryParse(Environment.GetEnvironmentVariable(MinLinesVar), out var n) && n > 0 ? n : 6);
    }

    /// <summary>Announced at startup so a test that believed it armed this must fail loudly if it did not.</summary>
    public string Describe() => $"PASTEPLACEHOLDER:minLines={MinLines}";

    /// <summary>
    /// What the composer shows for <paramref name="pasted"/>. The shape is the one every reader of
    /// this placeholder parses — <c>ComposerDeliveryEvidence</c>, <c>FakeVsRealClipParityTests</c>
    /// and the CARD-0030 bench all key off <c>[Pasted text #N +M lines]</c>, where M is the count
    /// BEYOND the first line and N increments per paste and is never reused.
    /// </summary>
    public string Render(string pasted)
    {
        var lines = pasted.TrimEnd('\n').Split('\n').Length;
        if (lines < MinLines)
            return pasted.Replace("\n", "\r\n");

        return $"[Pasted text #{++_pasteIndex} +{lines - 1} lines]";
    }
}
