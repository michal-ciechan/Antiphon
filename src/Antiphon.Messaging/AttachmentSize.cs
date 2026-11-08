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
    private const long GiB = 1024 * MiB;

    /// <summary>Binary units with at most one decimal, invariant culture: <c>2.5 GiB</c>, <c>17.6 MiB</c>,
    /// <c>14 MiB</c>, <c>2 KiB</c>, <c>3 bytes</c>, <c>1 byte</c>.</summary>
    public static string Format(long bytes) => Format(bytes, "0.#");

    /// <summary>
    /// <see cref="Format(long)"/> for a size over <paramref name="limit"/>, keeping one decimal when the
    /// rounded text would otherwise read the same as the limit's (<c>14.0 MiB</c> against <c>14 MiB</c>).
    /// </summary>
    public static string FormatOver(long bytes, long limit)
    {
        var text = Format(bytes);
        return bytes != limit && text == Format(limit) ? Format(bytes, "0.0") : text;
    }

    private static string Format(long bytes, string decimals) => bytes switch
    {
        >= GiB => (bytes / (double)GiB).ToString(decimals, CultureInfo.InvariantCulture) + " GiB",
        >= MiB => (bytes / (double)MiB).ToString(decimals, CultureInfo.InvariantCulture) + " MiB",
        >= KiB => (bytes / (double)KiB).ToString(decimals, CultureInfo.InvariantCulture) + " KiB",
        1 => "1 byte",
        _ => bytes.ToString(CultureInfo.InvariantCulture) + " bytes",
    };
}
