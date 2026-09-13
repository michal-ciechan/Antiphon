namespace Antiphon.MarkdownPdf;

public sealed class MarkdownPdfSettings
{
    public string? BrowserPath { get; set; }
    public int RenderTimeoutSeconds { get; set; } = 20;
}
