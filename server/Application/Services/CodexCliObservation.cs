using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>Public diagnostic projection, independent of task admission and runner liveness.</summary>
public static class CodexCliObservation
{
    // Display policy only. Neither this age nor a model's recorded floor grants or refuses work.
    public static bool? DisplayStale(RunnerCodexCliVersionDto? sample, DateTimeOffset now)
    {
        if (sample?.CodexCliVersionCheckedAtUtc is not { } completed
            || CodexCliVersion.Parse(sample.CodexCliVersion) is null
            || DisplayError(sample, now) is { } error && !IsAdvisory(error))
            return null;
        return now - completed > TimeSpan.FromMinutes(15);
    }

    public static string? DisplayError(RunnerCodexCliVersionDto? sample, DateTimeOffset now)
    {
        if (sample?.CodexCliVersionError is { } error
            && !(IsAdvisory(error) && CodexCliVersion.Parse(sample.CodexCliVersion) is not null))
            return error is "invalid_output" or "executable_missing" or "nonzero_exit" or "stderr_output"
                or "output_truncated" or "timeout" or "cancelled" or "cleanup_unconfirmed"
                or "launcher_unverified" or "probe_busy" or "probe_unavailable" ? error : "probe_unavailable";
        if (sample?.CodexCliVersionCheckedAtUtc is { } completed
            && CodexCliVersion.Parse(sample.CodexCliVersion) is not null
            && completed - now > TimeSpan.FromMinutes(1)) return "clock_skew";
        if (sample?.CodexCliLauncherFingerprint is { } fingerprint
            && (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit))) return "launcher_mismatch";
        return sample?.CodexCliVersionError;
    }

    private static bool IsAdvisory(string error) => error is "stderr_output" or "output_truncated";
}
