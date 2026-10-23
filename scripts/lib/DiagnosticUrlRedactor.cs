using System.Text.RegularExpressions;
namespace Antiphon.DiagnosticText;
internal static class DiagnosticUrlRedactor
{
    // Existing slot diagnostics keep their HTTP-only policy; the collector uses all schemes.
    internal static string Redact(string value, bool allSchemes = false) => Regex.Replace(value,
        allSchemes ? @"[A-Za-z][A-Za-z0-9+.-]*://[^\s""'<>]+" : @"https?://[^\s""'<>]+", match =>
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return "[url]";
            try { return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Path); }
            catch { return "[url]"; }
        }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
