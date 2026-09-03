namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0337: server-side document bundle (Markdown → PDF + sources) at task settlement.
/// </summary>
public sealed class DeliverablesSettings
{
    public const string SectionName = "Deliverables";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Absolute path to a Chromium-family browser. Null or empty auto-detects Edge, then Chrome.
    /// </summary>
    public string? BrowserPath { get; set; }

    public int RenderTimeoutSeconds { get; set; } = 20;

    /// <summary>Copy source <c>.md</c> files individually at or below this count; otherwise zip.</summary>
    public int MaxSourceFilesInline { get; set; } = 5;

    /// <summary>Beyond this many documents, skip the PDF and emit only the sources zip.</summary>
    public int MaxDocuments { get; set; } = 40;
}
