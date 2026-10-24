namespace Antiphon.HostCleanup.DiagnosticText;

// Keep the HostCleanup names fixed in source. The linked implementations retain their
// shared namespace and algorithms without an environment-derived MSBuild property.
internal sealed class DiagnosticTextRedactor(bool includePaths, IEnumerable<string>? projectDirectories = null)
{
    private readonly global::Antiphon.DiagnosticText.DiagnosticTextRedactor inner = new(includePaths, projectDirectories);

    public string Redact(string? text) => inner.Redact(text);
}

internal static class DiagnosticCredentialRedactor
{
    internal static SanitizedOutput Sanitize(string standardOutput, string standardError, IReadOnlyList<string> secretValues)
    {
        var result = global::Antiphon.DiagnosticText.DiagnosticCredentialRedactor.Sanitize(standardOutput, standardError, secretValues);
        return new(result.StandardOutput, result.StandardError, result.SensitiveOutputDetected);
    }

    internal sealed record SanitizedOutput(string StandardOutput, string StandardError, bool SensitiveOutputDetected);
}

internal static class DiagnosticUrlRedactor
{
    internal static string Redact(string value, bool allSchemes = false) =>
        global::Antiphon.DiagnosticText.DiagnosticUrlRedactor.Redact(value, allSchemes);
}
