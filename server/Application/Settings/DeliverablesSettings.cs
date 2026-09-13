namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0418: settlement-time source bundling. PDF rendering is no longer a server function.
/// <see cref="Enabled"/> gates source collection, not conversion.
/// </summary>
public sealed class DeliverablesSettings
{
    public const string SectionName = "Deliverables";

    /// <summary>Default on: every eligible task collects sources. False is an explicit kill switch.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Copy source <c>.md</c> files individually at or below this count; otherwise zip.</summary>
    public int MaxSourceFilesInline { get; set; } = 5;

    /// <summary>Total uncompressed source budget. Exceeding it omits later inputs with a warning.</summary>
    public long MaxUncompressedSourceBytes { get; set; } = 64L * 1024 * 1024;
}
