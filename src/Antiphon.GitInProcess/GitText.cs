namespace Antiphon.GitInProcess;

/// <summary>Small git spellings shared by the server's and the session runner's git code.</summary>
public static class GitText
{
    /// <summary>Reflog message prefix of the land's observation pins.</summary>
    public const string ObservationMessagePrefix = "antiphon observe ";

    /// <summary>A full lowercase SHA-1 or SHA-256 object id.</summary>
    public static bool IsOid(string value) => value.Length is 40 or 64
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>Same full path, ignoring a trailing separator; case-insensitive on Windows.</summary>
    public static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
