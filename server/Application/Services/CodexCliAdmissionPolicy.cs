using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
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

    internal sealed record Admission(CodexCliProbeDescriptor Descriptor, RunnerCodexCliVersionDto? Sample,
        string? Warning);

    internal static async Task<Admission?> RequireAsync(CodexCliProbeDescriptor? descriptor,
        ISessionRunnerDirectory? runners, DelegationSettings settings, TimeProvider clock, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (descriptor?.Model is not { } model || ModelLevelAliases.MinimumCodexCliVersion(AgentKind.Codex, model) is not { } floor) return null;
        RunnerCodexCliVersionDto? sample = null;
        var reason = descriptor.Error ?? "probe_unavailable";
        if (descriptor.Request is not null && runners is not null)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8), clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            try
            {
                // Always ask for this descriptor. A bare default snapshot has no server-verifiable
                // resolution identity and cannot certify an exact profile. The runner owns its cache.
                var client = descriptor.RunnerId == "desktop" ? runners.Local : runners.ResolveForNewWork(descriptor.RunnerId);
                sample = await client.GetCodexCliVersionAsync(descriptor.Request, linked.Token).WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (HttpException) { throw; } // placement refusals retain their existing codes
            catch (Exception) when (!ct.IsCancellationRequested) { reason = "probe_unavailable"; }
        }
        ct.ThrowIfCancellationRequested();
        var refusal = Evaluate(descriptor.RunnerId, model, floor.ToString(), sample, clock.GetUtcNow(), settings.CodexCliVersionMaxAgeMinutes, reason);
        if (refusal is null) return new(descriptor, sample, null);
        var allowed = settings.CodexCliVersionOverrides.FirstOrDefault(item =>
            item.RunnerId == descriptor.RunnerId && item.Model == model && item.ExpiresAtUtc > clock.GetUtcNow()
            && item.AllowedRefusalCodes.Contains(refusal.Code!, StringComparer.Ordinal));
        if (allowed is null) throw refusal;
        return new(descriptor, sample, $"Codex CLI operator override: runner={descriptor.RunnerId} model={model} code={refusal.Code} "
            + $"expiresAtUtc={allowed.ExpiresAtUtc:O} reason={allowed.Reason}");
    }

    internal static CodexCliVersionRequiredException? Evaluate(string runner, string model, string floor,
        RunnerCodexCliVersionDto? sample, DateTimeOffset now, int maxAge, string missingReason = "evidence_missing")
    {
        var observed = CodexCliVersion.Parse(sample?.CodexCliVersion);
        string? code = null;
        var reason = missingReason;
        if (sample is null) code = CodexCliVersionRequiredException.Unknown;
        else if (sample.CodexCliVersionError is not null)
        {
            code = CodexCliVersionRequiredException.Unknown;
            // Diagnostic text from a remote/legacy peer never becomes a public problem member.
            reason = sample.CodexCliVersionError is "invalid_output" or "executable_missing" or "nonzero_exit" or "stderr_output"
                or "output_truncated" or "timeout" or "cancelled" or "cleanup_unconfirmed" or "launcher_unverified" or "probe_busy"
                ? sample.CodexCliVersionError : "probe_unavailable";
        }
        else if (sample.CodexCliVersionCheckedAtUtc is not { } completed || observed is null)
        { code = CodexCliVersionRequiredException.Unknown; reason = "evidence_missing"; }
        else if (completed - now > TimeSpan.FromMinutes(1))
        { code = CodexCliVersionRequiredException.Unknown; reason = "clock_skew"; }
        else if (sample.CodexCliLauncherFingerprint is not { Length: 64 } fingerprint || !fingerprint.All(Uri.IsHexDigit))
        { code = CodexCliVersionRequiredException.Unknown; reason = "launcher_mismatch"; }
        else if (now - completed > TimeSpan.FromMinutes(maxAge))
        { code = CodexCliVersionRequiredException.Stale; reason = "evidence_expired"; }
        else if (observed.CompareTo(CodexCliVersion.Parse(floor)!) < 0)
        { code = CodexCliVersionRequiredException.TooOld; reason = "below_floor"; }
        return code is null ? null : new(code, runner, model, floor, sample, reason, maxAge);
    }
}
