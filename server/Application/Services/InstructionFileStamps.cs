using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Content stamps for the standing instruction files a live session read at launch (CARD-0334 S1).
/// Same hash rule as <see cref="InstructionBundle.Version"/>: first 8 hex of SHA-256 over
/// LF-normalised, outer-trimmed text. Missing files are omitted; the stamp line is composition
/// order of the files that existed under cwd.
/// </summary>
public static class InstructionFileStamps
{
    public sealed record Entry(string RelativePath, string Version)
    {
        public string Stamp => $"{RelativePath} v{Version}";
    }

    public sealed record Result(IReadOnlyList<Entry> Files)
    {
        public static Result Empty { get; } = new([]);

        /// <summary>
        /// <c>"AGENTS.md v1a2b3c4d, docs/orchestration-loop.md v9e8d7c6b"</c>. Empty when no
        /// listed file existed — a real answer, distinct from a null column (no evidence).
        /// </summary>
        public string StampLine => string.Join(", ", Files.Select(f => f.Stamp));
    }

    /// <summary>
    /// Stamp the files that exist under <paramref name="cwd"/>. Absolute paths, <c>..</c>
    /// segments, and IO failures are skipped rather than thrown — a standing launch must not
    /// fail because an optional instruction file is unreadable.
    /// </summary>
    public static Result Compute(string cwd, IReadOnlyList<string> relativePaths)
    {
        if (string.IsNullOrWhiteSpace(cwd) || relativePaths.Count == 0)
            return Result.Empty;

        string cwdFull;
        try
        {
            cwdFull = Path.GetFullPath(cwd);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result.Empty;
        }

        if (!Directory.Exists(cwdFull))
            return Result.Empty;

        var files = new List<Entry>(relativePaths.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in relativePaths)
        {
            if (!IsSafeRelative(relative))
                continue;
            var key = relative.Replace('\\', '/');
            if (!seen.Add(key))
                continue;

            string combined;
            try
            {
                combined = Path.GetFullPath(Path.Combine(cwdFull, key.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!IsInside(cwdFull, combined) || !File.Exists(combined))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(combined);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            files.Add(new Entry(key, HashOf(Normalise(text))));
        }

        return files.Count == 0 ? Result.Empty : new Result(files);
    }

    /// <summary>LF-normalise and trim — the same transform <c>InstructionBundles</c> applies before hashing.</summary>
    public static string Normalise(string text) => text.ReplaceLineEndings("\n").Trim();

    /// <summary>First 8 lowercase hex digits of SHA-256. Identical to <see cref="InstructionBundle.Version"/>.</summary>
    public static string HashOf(string normalisedText) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalisedText)))[..8];

    internal static bool IsSafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (Path.IsPathRooted(path))
            return false;
        var normalised = path.Replace('\\', '/');
        if (normalised.StartsWith('/'))
            return false;
        foreach (var segment in normalised.Split('/'))
        {
            if (segment is "" or "..")
                return false;
        }

        return true;
    }

    private static bool IsInside(string cwdFull, string candidateFull)
    {
        var prefix = cwdFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return candidateFull.StartsWith(
            prefix,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
