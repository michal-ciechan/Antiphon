namespace Antiphon.Server.Application.Settings;

/// <summary>
/// The write path a delivery's ceilings were measured against (CARD-0161).
/// InboxConhost/ModernConPty mirror <c>PtyBackend</c>; HerdrPane is pane.send_text into the
/// operator's herdr (<c>SessionBackend.Herdr</c> sessions only). Never fed to spawning code.
/// </summary>
public enum DeliveryBackend
{
    InboxConhost = 0,
    ModernConPty = 1,
    HerdrPane = 2,
    /// <summary>CARD-1224. Raised UnixPty receipt only. The unraised outcome stays <see cref="InboxConhost"/>.</summary>
    UnixPty = 3,
}
