using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public static class StandingSpecialistHealthPolicy
{
    public static StandingSpecialistHealthStatus Project(
        StandingSpecialistHealth health, bool intentionallyDisabled, bool definitivelyExhausted,
        bool usingFallback, bool readinessDefect, DateTime now)
    {
        if (intentionallyDisabled) return StandingSpecialistHealthStatus.Disabled;
        if (definitivelyExhausted || health.UnavailableSince is not null
            || health.ConsecutiveFailedRequests >= 3
            || (health.FirstFailureAt is { } first && now - first >= TimeSpan.FromMinutes(5))
            || (health.StarvedSince is { } starved && now - starved >= TimeSpan.FromMinutes(5)))
            return StandingSpecialistHealthStatus.Unavailable;
        if (health.FirstFailureAt is not null || health.ConsecutiveFailedRequests > 0)
            return StandingSpecialistHealthStatus.Suspect;
        if (usingFallback) return StandingSpecialistHealthStatus.UsingFallback;
        if (readinessDefect) return StandingSpecialistHealthStatus.DegradedReadiness;
        return StandingSpecialistHealthStatus.Healthy;
    }

    public static void ApplyRealCheck(StandingSpecialistHealth health, SpecialistRequest request, SpecialistAttempt? winner, DateTime now)
    {
        if (request.Purpose != SpecialistRequestPurpose.Check || request.HealthAppliedAt is not null
            || request.CompletedAt is null || request.Status == SpecialistRequestStatus.Canceled) return;
        request.HealthAppliedAt = now;
        if (request.Outcome is SpecialistAttemptOutcome.Busy or SpecialistAttemptOutcome.Disabled
            or SpecialistAttemptOutcome.CallerCanceled or SpecialistAttemptOutcome.HostShutdown) return;
        health.LastRequestId = request.Id;
        health.LastAttemptTaskId = winner?.TaskId ?? health.LastAttemptTaskId;
        if (request.Status == SpecialistRequestStatus.Succeeded && winner is not null
            && winner.Id == request.WinnerAttemptId && winner.Outcome == SpecialistAttemptOutcome.ValidReading
            && winner.CompletedAt <= request.DeadlineAt)
        {
            health.FirstFailureAt = null;
            health.StarvedSince = null;
            if (health.UnavailableSince is not null) health.ResolvedAt = now;
            health.UnavailableSince = null;
            health.ConsecutiveFailedRequests = 0;
            health.LastValidCheckAt = winner.CompletedAt;
            if (health.ActiveCandidateId != winner.CandidateId) health.ActiveSince = now;
            health.ActiveCandidateId = winner.CandidateId;
            health.Reason = null;
        }
        else
        {
            health.FirstFailureAt ??= request.CompletedAt;
            health.ConsecutiveFailedRequests++;
            health.Reason ??= request.Reason;
        }
        health.UpdatedAt = now;
    }
}
