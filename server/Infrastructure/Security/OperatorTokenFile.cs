using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Antiphon.Server.Infrastructure.Security;

/// <summary>
/// CARD-0653: the credential behind the operator-only force-release routes. A random value in a
/// file only the operator's own account can read (Windows: owner-only ACL, set at creation; else
/// mode 0600). The server creates it; <c>scripts/runner-slots.ps1</c> reads it and sends it as
/// <see cref="Header"/>. A loopback address is not a credential: the public vhost reaches Kestrel
/// through Caddy and Vite as a loopback connection. The value is never logged or echoed.
/// </summary>
public static class OperatorTokenFile
{
    public const string Header = "X-Antiphon-Operator-Token";

    /// <summary>CARD-0676 F-1: the setting. It guards every operator surface, not only the runner.</summary>
    public const string PathKey = "Operator:TokenPath";

    /// <summary>The CARD-0653 key, still honoured when <see cref="PathKey"/> is not set.</summary>
    public const string LegacyPathKey = "PhoneHomeRunner:OperatorTokenPath";

    /// <summary>The configured token path: <see cref="PathKey"/>, else its legacy alias, else empty.</summary>
    public static string ConfiguredPath(IConfiguration configuration)
    {
        var current = configuration[PathKey];
        return string.IsNullOrWhiteSpace(current) ? configuration[LegacyPathKey] ?? "" : current;
    }

    /// <summary>The configured path, or <see cref="DefaultPath"/> when none is set.</summary>
    public static string ResolvePath(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultPath() : configured;

    public static string DefaultPath()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Antiphon",
                "operator-token");
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(dataHome, "antiphon", "operator-token");
    }

    /// <summary>
    /// Read the token, creating it with a fresh random value on first use. The value is written
    /// to an owner-only temp file and published under the final name only if that name does not
    /// exist yet, so a concurrent reader sees either no file or the whole token, and exactly one
    /// first-use caller's token wins (CARD-0676). Every other caller reads the winner's file.
    /// </summary>
    public static string ReadOrCreate(string path)
    {
        var existing = TryRead(path);
        if (existing is not null)
            return existing;

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = CreateOwnerOnly(temp))
            {
                stream.Write(Encoding.ASCII.GetBytes(token));
                stream.Flush(flushToDisk: true);
            }

            if (TryPublish(temp, full))
                return token;
        }
        catch (IOException) when (File.Exists(full))
        {
            // Windows: another caller's no-overwrite move took the name first.
        }
        finally
        {
            File.Delete(temp);
        }

        // Another caller published theirs first; theirs is the token.
        return TryRead(full) ?? throw new InvalidOperationException("The operator token file is empty.");
    }

    /// <summary>
    /// Give <paramref name="temp"/> the name <paramref name="full"/> only if that name is free, as
    /// one atomic step; false when another caller holds it. Windows' no-overwrite move is that
    /// step. On Unix .NET's <see cref="File.Move(string, string, bool)"/> checks and then renames
    /// (measured: 8 tokens from 16 concurrent callers), so a hard link, which fails with EEXIST,
    /// is used instead; the caller then deletes the temp name.
    /// </summary>
    private static bool TryPublish(string temp, string full)
    {
        if (OperatingSystem.IsWindows())
        {
            File.Move(temp, full, overwrite: false);
            return true;
        }

        if (Link(temp, full) == 0)
            return true;
        var errno = Marshal.GetLastPInvokeError();
        if (errno == EEXIST)
            return false;
        throw new IOException(
            $"Could not publish the operator token file '{full}': {Marshal.GetPInvokeErrorMessage(errno)} (errno {errno}).");
    }

    // EEXIST is 17 on Linux and macOS alike.
    private const int EEXIST = 17;

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string oldPath, string newPath);

    /// <summary>Constant-time comparison. An empty expectation never matches.</summary>
    public static bool Matches(string expected, string? provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided))
            return false;
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    /// <summary>
    /// Windows briefly holds a just-renamed file (the rename itself, a scanner) and a plain read
    /// then fails with a sharing violation. Read with a permissive share mode and retry that one
    /// failure a bounded number of times; anything else, or a violation that outlasts it, throws.
    /// </summary>
    private static string? TryRead(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.ASCII);
                var text = reader.ReadToEnd().Trim();
                return text.Length == 0 ? null : text;
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < ReadAttempts)
            {
                Thread.Sleep(ReadRetryDelay);
            }
        }
    }

    private const int ReadAttempts = 40;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(25);

    // ERROR_SHARING_VIOLATION (32) and ERROR_LOCK_VIOLATION (33) as HRESULTs.
    private static bool IsSharingViolation(IOException ex) =>
        (ex.HResult & 0xFFFF) is 32 or 33;

    private static FileStream CreateOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
            return CreateWindowsOwnerOnly(path);
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
    }

    [SupportedOSPlatform("windows")]
    private static FileStream CreateWindowsOwnerOnly(string path)
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var user = identity.User ?? throw new InvalidOperationException("The current Windows account has no SID.");
        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        return new FileInfo(path).Create(
            FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.ReadData, FileShare.None, 4096,
            FileOptions.None, security);
    }
}
