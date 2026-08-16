namespace Antiphon.FakeClaude;

/// <summary>
/// Models the MEASURED failure state CARD-0055 exists for: a submitting Enter that does not submit
/// while the screen redraws anyway.
///
/// <para><b>What was measured.</b> Task 817682e9, session <c>cefed08a</c>: delivery
/// <c>ea2feb92</c>'s Enter was swallowed, the terminal's output advanced, the queue read that
/// advance as "submitted" and marked the message <b>Sent</b> at 15:16:20Z — and the body sat in the
/// composer until the NEXT delivery's Enter pushed it in 104 minutes later. The composer keeps the
/// body: that is why the fix's retry is an Enter re-press and never a re-type, and why a swallow
/// here must leave <c>composer</c> untouched.</para>
///
/// <para><b>Why the redraw is the point.</b> A fake that ate the CR silently would be a fake of a
/// dead terminal, which nothing mistakes for success. The defect is that the terminal looks alive:
/// the marker line below is what makes the output sequence advance, so a peer wired to the old
/// "output advanced ⇒ Delivered" rule still says Delivered and the transcript-confirm rule still
/// says no.</para>
///
/// <para>Opt-in (<see cref="CountVar"/>) and default OFF, like the clip and placeholder models: our
/// transport does not eat Enters, and every existing fake-driven test submits on the first one.</para>
/// </summary>
internal sealed class SwallowEnterModel
{
    /// <summary>Unset / <c>0</c> = submit normally. <c>n</c> = eat the first <em>n</em> submitting Enters.</summary>
    public const string CountVar = "ANTIPHON_FAKE_SWALLOW_ENTER";

    private int _remaining;

    internal SwallowEnterModel(int count) => _remaining = Math.Max(0, count);

    public int Remaining => _remaining;

    /// <summary>Null unless <see cref="CountVar"/> asks for it — the fake's default is unchanged.</summary>
    public static SwallowEnterModel? FromEnvironment()
    {
        var raw = (Environment.GetEnvironmentVariable(CountVar) ?? string.Empty).Trim();
        if (raw.Length == 0 || raw == "0")
            return null;
        if (!int.TryParse(raw, out var count) || count < 0)
        {
            throw new ArgumentException(
                $"{CountVar}='{raw}' is not a count. Use a non-negative integer or leave it unset.");
        }

        return count == 0 ? null : new SwallowEnterModel(count);
    }

    /// <summary>Announced at startup so a test that believed it armed this fails loudly if it did not.</summary>
    public string Describe() => $"SWALLOWENTER:count={_remaining}";

    /// <summary>
    /// True when this Enter is eaten. Only <em>submitting</em> Enters are counted — an Enter on an
    /// already-empty composer is a no-op in the real TUI (the contract the whole retry path leans
    /// on), so spending a swallow on one would model the wrong thing and would make "n Enters
    /// swallowed" mean something different depending on how many idle Enters preceded them.
    /// </summary>
    public bool ShouldSwallow()
    {
        if (_remaining <= 0)
            return false;

        _remaining--;
        return true;
    }
}
