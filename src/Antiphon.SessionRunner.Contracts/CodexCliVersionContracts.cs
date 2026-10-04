namespace Antiphon.SessionRunner.Contracts;

/// <summary>Version-only resolution inputs. This is never an arbitrary process command.</summary>
public sealed record RunnerCodexCliProbeRequest(
    string Executable,
    string? ResolutionCwd = null,
    string? Path = null,
    string? PathExt = null,
    string? CodexJsPrefix = null);

/// <summary>
/// One completed runner-owned attempt. The legacy error field holds fixed failure or advisory
/// tokens, never process output. A valid version may accompany stderr_output/output_truncated;
/// neither observation nor diagnostic decides task admission.
/// </summary>
public sealed record RunnerCodexCliVersionDto(
    string? CodexCliVersion = null,
    DateTimeOffset? CodexCliVersionCheckedAtUtc = null,
    string? CodexCliVersionError = null,
    string? CodexCliLauncherFingerprint = null);
