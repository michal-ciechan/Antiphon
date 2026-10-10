using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

public sealed class AgentTuiSettings
{
    public const int MaximumProbeTimeoutSeconds = 30;
    public const int MaximumProbeOutputBytes = 1024 * 1024;

    public int ProbeTimeoutSeconds { get; set; } = MaximumProbeTimeoutSeconds;
    public int MaxProbeOutputBytes { get; set; } = 64 * 1024;
    public string KeyRingPath { get; set; } = string.Empty;
    public AgentTuiKeyProtectionSettings KeyProtection { get; set; } = new();

    /// <summary>
    /// Whether startup imports the configured runner profiles and backfills every agent row that
    /// has no profile yet. True is the installation behaviour. A host that shares its database with
    /// something else - the test assembly's shared schema, where the backfill would stamp
    /// TuiProfileId onto agent rows other tests own - turns it off.
    /// </summary>
    public bool ImportProfilesOnStartup { get; set; } = true;

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
                ResolveUnixDataRoot(environment),
                environment.Platform,
                "antiphon",
                "data-protection-keys"),
            _ => throw new PlatformNotSupportedException(
                "Agent TUI key-ring path resolution is not supported on this platform.")
        };
    }

    private static string ResolveUnixDataRoot(AgentTuiPathEnvironment environment) =>
        AntiphonDataPaths.ResolveUnixDataRoot(environment);

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
        AgentTuiPlatform platform) =>
        AntiphonDataPaths.RequireAbsolutePath(path, name, platform);

    private static bool IsAbsolutePath(string? path, AgentTuiPlatform platform) =>
        AntiphonDataPaths.IsAbsolutePath(path, platform);

    private static string Combine(string root, AgentTuiPlatform platform, params string[] segments) =>
        AntiphonDataPaths.Combine(root, platform, segments);
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
