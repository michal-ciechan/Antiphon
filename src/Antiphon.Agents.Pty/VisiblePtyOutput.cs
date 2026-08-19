namespace Antiphon.Agents.Pty;

/// <summary>
/// Visible-output gate for quiet-period ready/done detectors (CARD-0052).
/// Host-init CSI and OSC titles are not life: <see cref="AnsiStripper"/> already
/// drops them, so a title-only snapshot must not start the quiet clock.
/// </summary>
public static class VisiblePtyOutput
{
    public static bool HasVisibleOutput(string? snapshot) =>
        !string.IsNullOrWhiteSpace(AnsiStripper.Clean(snapshot));
}
