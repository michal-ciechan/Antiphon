namespace Antiphon.Server.Application.Services;

/// <summary>
/// Normalizes filesystem paths to the project's canonical forward-slash form
/// (e.g. "c:\src\app" → "C:/src/app") and splits a typed prefix into the directory
/// whose children should be listed. Windows accepts forward slashes, so normalized
/// paths can be passed straight to <c>System.IO</c> / <c>IFileSystem</c>.
/// </summary>
public static class PathNormalizer
{
    /// <summary>Trim, convert backslashes to '/', collapse duplicate slashes, uppercase the
    /// drive letter, and drop the trailing slash except for a bare drive root ("C:/").</summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var s = path.Trim().Replace('\\', '/');
        while (s.Contains("//", StringComparison.Ordinal))
            s = s.Replace("//", "/", StringComparison.Ordinal);

        if (s.Length >= 2 && char.IsLetter(s[0]) && s[1] == ':')
            s = char.ToUpperInvariant(s[0]) + s[1..];

        // Bare drive "C:" → "C:/"
        if (s.Length == 2 && s[1] == ':')
            s += "/";

        // Strip a trailing slash unless it is a drive root ("C:/").
        if (s.Length > 3 && s.EndsWith('/'))
            s = s.TrimEnd('/');

        return s;
    }

    /// <summary>True for a bare drive root such as "C:/".</summary>
    public static bool IsDriveRoot(string normalized) =>
        normalized.Length == 3 && char.IsLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/';

    /// <summary>
    /// True when the raw typed path ends with a directory separator ("C:/src/"). This is the
    /// signal that the user wants <em>that directory's children</em>, not sibling matches for a
    /// partial name — a distinction <see cref="Normalize"/> erases by stripping the trailing slash,
    /// so it must be read from the raw input.
    /// </summary>
    public static bool EndsWithSeparator(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var trimmed = path.TrimEnd();
        return trimmed.EndsWith('/') || trimmed.EndsWith('\\');
    }

    /// <summary>
    /// Given a normalized typed prefix (no trailing slash), the directory whose immediate
    /// children should be listed to offer sibling matches. "C:/sr" → "C:/"; "C:/src/an" → "C:/src".
    /// Returns empty when there is no path separator to anchor on. For the "descend into this
    /// directory" case (trailing slash) use <see cref="EndsWithSeparator"/> and list the path itself.
    /// </summary>
    public static string GetListingDirectory(string normalized)
    {
        var lastSlash = normalized.LastIndexOf('/');
        if (lastSlash < 0)
            return string.Empty;

        var parentWithSlash = normalized[..(lastSlash + 1)];
        return IsDriveRoot(parentWithSlash) ? parentWithSlash : parentWithSlash.TrimEnd('/');
    }

    /// <summary>
    /// The partial leaf segment the user is typing — the part after the last separator.
    /// "C:/src/lea" → "lea"; "C:/src" → "src". This is what autocomplete matches sibling names
    /// against (the parent is found via <see cref="GetListingDirectory"/>).
    /// </summary>
    public static string GetLeaf(string normalized)
    {
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash < 0 ? normalized : normalized[(lastSlash + 1)..];
    }
}
