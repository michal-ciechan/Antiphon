namespace Antiphon.SessionRunner.Contracts;

/// <summary>Version-only resolution inputs. This is never an arbitrary process command.</summary>
public sealed record RunnerCodexCliProbeRequest(
    string Executable,
    string? ResolutionCwd = null,
    string? Path = null,
    string? PathExt = null,
    string? CodexJsPrefix = null);

/// <summary>One completed runner-owned attempt. Errors are fixed tokens, never process output.</summary>
public sealed record RunnerCodexCliVersionDto(
    string? CodexCliVersion = null,
    DateTimeOffset? CodexCliVersionCheckedAtUtc = null,
    string? CodexCliVersionError = null,
    string? CodexCliLauncherFingerprint = null);
