namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Settlement-time source document bundle settings.
/// </summary>
public sealed class DeliverablesSettings
{
    public const string SectionName = "Deliverables";

    public bool Enabled { get; set; } = true;

    /// <summary>Copy source <c>.md</c> files individually at or below this count; otherwise zip.</summary>
    public int MaxSourceFilesInline { get; set; } = 5;

    /// <summary>Maximum uncompressed bytes copied into one source bundle.</summary>
    public long MaxTotalSourceBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>Per-blob ceiling for recorded RefOnly sources, before and during the read.</summary>
    public long MaxSourceBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>Explicit persistent output store for RefOnly bundles; never a source-read fallback.</summary>
    public string? StorageRoot { get; set; }
}
