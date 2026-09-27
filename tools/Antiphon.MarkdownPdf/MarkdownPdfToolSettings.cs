namespace Antiphon.MarkdownPdf;

public sealed class MarkdownPdfToolSettings
{
    public string? BrowserPath { get; set; }
    public int RenderTimeoutSeconds { get; set; } = 20;
}
