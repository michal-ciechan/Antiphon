using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>Clock-based evidence interpretation, independent of runner liveness.</summary>
public static class CodexCliAdmissionPolicy
{
    public static bool? DisplayStale(RunnerCodexCliVersionDto? sample, DateTimeOffset now, int maxAgeMinutes)
    {
        if (sample?.CodexCliVersionCheckedAtUtc is not { } completed || sample.CodexCliVersionError is not null
            || CodexCliVersion.Parse(sample.CodexCliVersion) is null || completed - now > TimeSpan.FromMinutes(1)) return null;
        return now - completed > TimeSpan.FromMinutes(maxAgeMinutes);
    }
}
