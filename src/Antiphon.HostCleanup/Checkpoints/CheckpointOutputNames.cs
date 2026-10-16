using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public static class CheckpointOutputNames
{
    // CARD-1199: a build id is a directory name only when it is an isolated bin-<name> output.
    private static readonly Regex IsolatedOutputId = new(@"^bin-[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);

    public static bool IsPathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..")
            return false;
        if (value.Contains("..", StringComparison.Ordinal))
            return false;
        if (value.IndexOfAny(['/', '\\', ':']) >= 0)
            return false;
        return !Path.IsPathRooted(value);
    }

    public static bool IsIsolatedOutputId(string value) =>
        IsPathSegment(value) && IsolatedOutputId.IsMatch(value);

}
