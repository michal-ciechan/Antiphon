namespace Antiphon.Server.Application.Services;

// Compatibility facade: pure policies are shared with failure-boundary diagnostics.
public sealed class DiagnosticsRedactor(bool includePaths, IEnumerable<string>? projectDirectories = null)
{
    private readonly Antiphon.DiagnosticText.DiagnosticTextRedactor redactor = new(includePaths, projectDirectories);
    public string Redact(string? text) => redactor.Redact(text);
}