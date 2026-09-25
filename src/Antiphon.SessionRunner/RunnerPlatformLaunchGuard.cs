using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0710. The executing host checks a specific requirement against its own OS before any
/// session, custody or child exists. A null requirement is Any and is not checked.
/// </summary>
public static class RunnerPlatformLaunchGuard
{
    public const string Mismatch = "runner_platform_mismatch";
    public const string Invalid = "runner_platform_requirement_invalid";

    public static void RefuseBeforeLaunch(string? requirement)
    {
        if (string.IsNullOrWhiteSpace(requirement))
            return;
        var required = RunnerPlatformWire.Normalize(requirement);
        if (required is null)
            throw new RunnerPlatformLaunchException(Invalid,
                $"Platform requirement '{requirement.Trim()}' is not windows or linux.");
        var actual = RunnerPlatformWire.FromOperatingSystem();
        if (!string.Equals(required, actual, StringComparison.Ordinal))
            throw new RunnerPlatformLaunchException(Mismatch,
                $"This runner is '{actual ?? "unknown"}' and cannot run a {required} task.");
    }
}

public sealed class RunnerPlatformLaunchException : Exception
{
    public RunnerPlatformLaunchException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
    public int StatusCode => 409;
}
