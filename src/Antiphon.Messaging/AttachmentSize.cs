using System.Globalization;

namespace Antiphon.Messaging;

/// <summary>
/// Human-readable attachment sizes for notes and notices about files that did not travel inline
/// (see <see cref="Attachment.InlineLimitBytes"/>), so a gateway and a consumer word sizes the same way.
/// </summary>
public static class AttachmentSize
{
    private const long KiB = 1024;
    private const long MiB = 1024 * KiB;

    /// <summary>Binary units with at most one decimal, invariant culture: <c>17.6 MiB</c>, <c>14 MiB</c>, <c>2 KiB</c>, <c>3 bytes</c>.</summary>
    public static string Format(long bytes) => bytes switch
    {
        >= MiB => (bytes / (double)MiB).ToString("0.#", CultureInfo.InvariantCulture) + " MiB",
        >= KiB => (bytes / (double)KiB).ToString("0.#", CultureInfo.InvariantCulture) + " KiB",
        _ => bytes.ToString(CultureInfo.InvariantCulture) + " bytes",
    };
}
