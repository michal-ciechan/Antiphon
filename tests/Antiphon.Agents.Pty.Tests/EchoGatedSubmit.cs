using Antiphon.Agents.Pty;
using Shouldly;

namespace Antiphon.Agents.Pty.Tests;

/// <summary>
/// Two-write submit that waits for the composer echo BEFORE sending CR (CARD-0050 S3).
///
/// fakeclaude groups input by arrival-time burst gap (<c>ANTIPHON_FAKE_BURST_MS</c>, 12ms).
/// The writer's 20ms body→CR spacing compresses below that at the reader when ConPTY
/// delivery of the body lags under load, so a legitimate two-write submit is misread as
/// one paste burst — the composer holds the body and the CR never submits. An echo means
/// the body burst was already consumed, so the CR cannot join it. Mirrors
/// <see cref="VerifiedPromptSubmitter"/> and production <see cref="EchoGatedLineSender"/>
/// (evidence-gated, not time-gated). This remains a test helper in this slice; it may be replaced
/// with the production helper later, but is intentionally not changed here.
///
/// The one-write (paste) arm stays time-based: a single write can only be split, never
/// merged, so it does not have this race. Do not retune <c>ANTIPHON_FAKE_BURST_MS</c>.
/// </summary>
internal static class EchoGatedSubmit
{
    internal static readonly TimeSpan DefaultEvidenceTimeout = TimeSpan.FromSeconds(10);

    public static async Task SendAsync(
        PtyAgentRunner runner,
        string body,
        TimeSpan? evidenceTimeout = null,
        CancellationToken ct = default)
    {
        var screenBefore = runner.SnapshotScreen();
        await runner.WriteAsync(body, ct);

        // Evidence is the body the composer actually renders — paste markers are stripped by
        // the TUI (and by fakeclaude) before echo, so matching the wrapped form would miss.
        var evidenceBody = body.Replace("\x1b[200~", "").Replace("\x1b[201~", "");
        var timeout = evidenceTimeout ?? DefaultEvidenceTimeout;
        var echoed = await runner.WaitForScreenAsync(
            screen => ComposerDeliveryEvidence.IsVisible(screenBefore, screen, evidenceBody),
            timeout,
            ct);
        echoed.ShouldBeTrue(
            $"Composer did not echo the body ({body.Length} chars) within {timeout.TotalSeconds:F0}s — "
            + "CR withheld so a lagged body cannot merge with Enter. Screen:\n"
            + runner.SnapshotScreen());

        await runner.WriteAsync("\r", ct);
    }
}
