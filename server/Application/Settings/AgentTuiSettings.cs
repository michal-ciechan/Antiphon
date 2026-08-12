using Microsoft.Extensions.Options;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

public sealed class AgentTuiSettings
{
    public const int MaximumProbeTimeoutSeconds = 30;
    public const int MaximumProbeOutputBytes = 1024 * 1024;

    public int ProbeTimeoutSeconds { get; set; } = MaximumProbeTimeoutSeconds;
    public int MaxProbeOutputBytes { get; set; } = 64 * 1024;
    public string KeyRingPath { get; set; } = string.Empty;
    public AgentTuiKeyProtectionSettings KeyProtection { get; set; } = new();

    public string ResolveKeyRingPath(AgentTuiPathEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (!string.IsNullOrWhiteSpace(KeyRingPath))
        {
            if (!IsAbsolutePath(KeyRingPath, environment.Platform))
            {
                throw new InvalidOperationException(
                    "AgentTui:KeyRingPath must be an absolute path.");
            }

            return KeyRingPath;
        }

        return environment.Platform switch
        {
            AgentTuiPlatform.Windows => Combine(
                RequireAbsolutePath(
                    environment.LocalApplicationData,
                    "LOCALAPPDATA",
                    environment.Platform),
                environment.Platform,
                "Antiphon",
                "DataProtection-Keys"),
            AgentTuiPlatform.Linux or AgentTuiPlatform.MacOS => Combine(
                environment.XdgDataHome is { } xdgDataHome
                && IsAbsolutePath(xdgDataHome, environment.Platform)
                    ? xdgDataHome
                    : Combine(
                        RequireAbsolutePath(
                            environment.HomeDirectory,
                            "home directory",
                            environment.Platform),
                        environment.Platform,
                        ".local",
                        "share"),
                environment.Platform,
                "antiphon",
                "data-protection-keys"),
            _ => throw new PlatformNotSupportedException(
                "Agent TUI key-ring path resolution is not supported on this platform.")
        };
    }

    public static AgentTuiDirectoryPermissionStrategy GetDirectoryPermissionStrategy(
        AgentTuiPlatform platform) => platform switch
        {
            AgentTuiPlatform.Windows => AgentTuiDirectoryPermissionStrategy.WindowsAccessControl,
            AgentTuiPlatform.Linux or AgentTuiPlatform.MacOS =>
                AgentTuiDirectoryPermissionStrategy.UnixOwnerOnly,
            _ => AgentTuiDirectoryPermissionStrategy.Unsupported
        };

    public static bool RequiresSecretProtection(AgentTuiAuthenticationMode authenticationMode) =>
        authenticationMode switch
        {
            AgentTuiAuthenticationMode.WrapperManaged => false,
            AgentTuiAuthenticationMode.ManagedEnvironment => true,
            _ => throw new ArgumentOutOfRangeException(
                nameof(authenticationMode),
                authenticationMode,
                "Unsupported Agent TUI authentication mode.")
        };

    private static string RequireAbsolutePath(
        string? path,
        string name,
        AgentTuiPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"The {name} path is unavailable.");
        if (!IsAbsolutePath(path, platform))
            throw new InvalidOperationException($"The {name} path must be absolute.");
        return path;
    }

    private static bool IsAbsolutePath(string? path, AgentTuiPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return platform switch
        {
            AgentTuiPlatform.Windows =>
                (path.Length >= 3
                 && char.IsAsciiLetter(path[0])
                 && path[1] == ':'
                 && path[2] is '\\' or '/')
                || path.StartsWith("\\\\", StringComparison.Ordinal)
                || path.StartsWith("//", StringComparison.Ordinal),
            AgentTuiPlatform.Linux or AgentTuiPlatform.MacOS => path[0] == '/',
            _ => false
        };
    }

    private static string Combine(string root, AgentTuiPlatform platform, params string[] segments)
    {
        var separator = platform == AgentTuiPlatform.Windows ? '\\' : '/';
        var normalizedRoot = root.TrimEnd('/', '\\');
        if (normalizedRoot.Length == 0)
            normalizedRoot = separator.ToString();

        var suffix = string.Join(separator, segments);
        return normalizedRoot == separator.ToString()
            ? normalizedRoot + suffix
            : normalizedRoot + separator + suffix;
    }
}

public sealed class AgentTuiKeyProtectionSettings
{
    public AgentTuiKeyProtectionMode Mode { get; set; } = AgentTuiKeyProtectionMode.Auto;
    public string CertificatePath { get; set; } = string.Empty;
    public string CertificatePrivateKeyPath { get; set; } = string.Empty;
    public string CertificateThumbprint { get; set; } = string.Empty;
    public string CertificateStoreName { get; set; } = "My";
    public string CertificateStoreLocation { get; set; } = "CurrentUser";
}

public enum AgentTuiKeyProtectionMode
{
    Auto = 0,
    DpapiCurrentUser = 1,
    DpapiLocalMachine = 2,
    X509Certificate = 3
}

public enum AgentTuiPlatform
{
    Windows = 0,
    Linux = 1,
    MacOS = 2,
    Other = 3
}

public enum AgentTuiDirectoryPermissionStrategy
{
    Unsupported = 0,
    WindowsAccessControl = 1,
    UnixOwnerOnly = 2
}

public sealed record AgentTuiPathEnvironment(
    AgentTuiPlatform Platform,
    string? LocalApplicationData,
    string? XdgDataHome,
    string? HomeDirectory);

public sealed class AgentTuiSettingsValidator : IValidateOptions<AgentTuiSettings>
{
    public ValidateOptionsResult Validate(string? name, AgentTuiSettings options)
    {
        var failures = new List<string>();

        if (options.ProbeTimeoutSeconds is <= 0 or > AgentTuiSettings.MaximumProbeTimeoutSeconds)
        {
            failures.Add(
                $"AgentTui:ProbeTimeoutSeconds must be between 1 and "
                + $"{AgentTuiSettings.MaximumProbeTimeoutSeconds}.");
        }

        if (options.MaxProbeOutputBytes is <= 0 or > AgentTuiSettings.MaximumProbeOutputBytes)
        {
            failures.Add(
                $"AgentTui:MaxProbeOutputBytes must be between 1 and "
                + $"{AgentTuiSettings.MaximumProbeOutputBytes}.");
        }

        if (options.KeyProtection is null)
            failures.Add("AgentTui:KeyProtection must be configured.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
