using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>Reads the SourceLink identity the SDK already stamps into the runner assembly.</summary>
public static partial class RunnerBuildIdentity
{
    public static RunnerBuildDto Resolve()
    {
        var assembly = typeof(RunnerBuildIdentity).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        var assemblyPath = assembly.Location;
        var assemblyWriteTime = string.IsNullOrEmpty(assemblyPath)
            ? DateTime.MinValue
            : File.GetLastWriteTimeUtc(assemblyPath);

        return new RunnerBuildDto(
            informationalVersion,
            TryParseCommitSha(informationalVersion),
            assemblyWriteTime,
            Process.GetCurrentProcess().StartTime.ToUniversalTime());
    }

    public static string? TryParseCommitSha(string? informationalVersion)
    {
        var match = SourceRevisionRegex().Match(informationalVersion ?? string.Empty);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"\+([0-9a-f]{40})(?:$|[.+])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceRevisionRegex();
}
