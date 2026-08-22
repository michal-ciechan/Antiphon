using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Server.Application.Services;

/// <summary>Canonical digest for the report content carried by a delegation completion note.</summary>
public static class DelegationNoteDigest
{
    /// <summary>
    /// Normalizes line endings, removes trailing whitespace from each line, then trims the whole
    /// report. Deliberately does not alter interior whitespace, case, or punctuation.
    /// </summary>
    public static string Normalize(string? reportText) =>
        string.Join("\n", (reportText ?? string.Empty).ReplaceLineEndings("\n")
            .Split('\n').Select(line => line.TrimEnd())).Trim();

    /// <summary>Returns the lowercase SHA-256 digest of <see cref="Normalize"/>.</summary>
    public static string Compute(string? reportText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(reportText)))).ToLowerInvariant();
}
