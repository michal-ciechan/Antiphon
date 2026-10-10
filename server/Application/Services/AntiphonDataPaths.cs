using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Shared resolution of Antiphon's per-user data root. Key-ring paths and the operator token
/// keep their existing results; the orchestrator instructions file is a sibling, not a credential.
/// </summary>
public static class AntiphonDataPaths
{
    public const string OrchestratorInstructionsFileName = "ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md";

    public static string ResolveOrchestratorInstructionsPath(
        AgentTuiPathEnvironment environment,
        string? configuredPath)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!IsAbsolutePath(configuredPath, environment.Platform))
            {
                throw new InvalidOperationException(
                    "Delegation:OrchestratorInstructions:Path must be an absolute path.");
            }

            return configuredPath;
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
                "orchestrator",
                OrchestratorInstructionsFileName),
            AgentTuiPlatform.Linux or AgentTuiPlatform.MacOS => Combine(
                ResolveUnixDataRoot(environment),
                environment.Platform,
                "antiphon",
                "orchestrator",
                OrchestratorInstructionsFileName),
            _ => throw new PlatformNotSupportedException(
                "Orchestrator instructions path resolution is not supported on this platform."),
        };
    }

    public static string ResolveUnixDataRoot(AgentTuiPathEnvironment environment)
    {
        if (string.IsNullOrEmpty(environment.XdgDataHome))
        {
            return Combine(
                RequireAbsolutePath(
                    environment.HomeDirectory,
                    "home directory",
                    environment.Platform),
                environment.Platform,
                ".local",
                "share");
        }

        if (!IsAbsolutePath(environment.XdgDataHome, environment.Platform))
            throw new InvalidOperationException("The XDG_DATA_HOME path must be absolute.");

        return environment.XdgDataHome;
    }

    public static string RequireAbsolutePath(
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

    public static bool IsAbsolutePath(string? path, AgentTuiPlatform platform)
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
            _ => false,
        };
    }

    public static string Combine(string root, AgentTuiPlatform platform, params string[] segments)
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
