namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-1776. When the Linux server is given credential paths, a remote git command
/// refuses before the process starts if the mounted token or allow-list is missing,
/// empty, or unreadable. Desktop leaves both paths empty and is not probed.
/// The probe opens each file for read and checks that it is non-empty. It does not
/// read or cache the bytes, and it never returns them. Each check stats the files,
/// so the maximum staleness of a present-or-absent answer is zero.
/// </summary>
public static class ServerPushCredential
{
    public const string AbsentReason = "push_credential_absent";

    public static bool IsRemoteCommand(IReadOnlyList<string> arguments) =>
        arguments.Count > 0 && arguments[0] is "push" or "fetch" or "ls-remote" or "pull" or "clone";

    public static string? Refusal(string? tokenFile, string? policyFile)
    {
        var token = (tokenFile ?? "").Trim();
        var policy = (policyFile ?? "").Trim();
        if (token.Length == 0 && policy.Length == 0)
            return null;
        return Readable(token) && Readable(policy) ? null : AbsentReason;
    }

    private static bool Readable(string path)
    {
        if (path.Length == 0 || !Path.IsPathRooted(path))
            return false;
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.Directory) || info.Length <= 0)
                return false;
            // Open proves the server can read the mount. Length is a stat. A zero-length
            // file was already refused. No byte of the token is copied into the process.
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return RandomAccess.GetLength(handle) > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
