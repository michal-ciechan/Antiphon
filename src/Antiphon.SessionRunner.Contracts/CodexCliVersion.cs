using System.Globalization;
using System.Text.RegularExpressions;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>A strict SemVer value. Build metadata is retained but has no ordering weight.</summary>
public sealed class CodexCliVersion : IComparable<CodexCliVersion>, IComparable
{
    private readonly string _text;
    private readonly string[] _prerelease;

    private CodexCliVersion(string text, int major, int minor, int patch, string[] prerelease)
    {
        _text = text;
        Major = major;
        Minor = minor;
        Patch = patch;
        _prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>The first valid whole banner line in bounded LF/CRLF stdout; EOF may end the last line.</summary>
    public static CodexCliVersion? ParseBanner(string? output)
    {
        if (output is null || output.Length > 4096)
            return null;
        var lines = output.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var record = i < lines.Length - 1 && lines[i].EndsWith('\r') ? lines[i][..^1] : lines[i];
            var match = Regex.Match(record, @"\Acodex(?:-cli)? (?:version )?v?([^\r\n ]+)\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success && Parse(match.Groups[1].Value) is { } version)
                return version;
        }
        return null;
    }

    public static CodexCliVersion? Parse(string? text)
    {
        if (text is null || text.Length > 4096)
            return null;
        var match = Regex.Match(text,
            @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z",
            RegexOptions.CultureInvariant);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return null;
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (prerelease.Any(id => IsNumeric(id) && id.Length > 1 && id[0] == '0'))
            return null;
        return new CodexCliVersion(text, major, minor, patch, prerelease);
    }

    public int CompareTo(CodexCliVersion? other)
    {
        if (other is null) return 1;
        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (_prerelease.Length == 0)
            return other._prerelease.Length == 0 ? 0 : 1;
        if (other._prerelease.Length == 0) return -1;
        for (var i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
        {
            var left = _prerelease[i];
            var right = other._prerelease[i];
            var leftNumeric = IsNumeric(left);
            var rightNumeric = IsNumeric(right);
            var result = leftNumeric && rightNumeric
                ? left.Length.CompareTo(right.Length)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1 : 0;
            if (result == 0) result = string.CompareOrdinal(left, right);
            if (result != 0) return result;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    int IComparable.CompareTo(object? obj) => obj is null ? 1
        : obj is CodexCliVersion version ? CompareTo(version)
        : throw new ArgumentException("A Codex CLI version is required.", nameof(obj));

    public override string ToString() => _text;

    private static bool IsNumeric(string identifier) => identifier.All(c => c is >= '0' and <= '9');
}
