using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The release identity answer admission accepts, as EF-translatable queries. Reply
/// continuation and the remote-pool Reply guidance compose these same expressions, so the
/// guidance names Reply only for a release the continuation would accept (CARD-1146).
/// </summary>
internal static class RunnerSeatReleaseQueries
{
    /// <summary>
    /// The current attempt's release: task, attempt, session, agent, runner, settlement
    /// revision and settled time, plus a server session on the task's runner whose store and
    /// StartedAt are the accepted generation. A task without a runner or session matches none;
    /// a missing session never infers a release.
    /// </summary>
    internal static IQueryable<RunnerSeatRelease> ForAttempt(AppDbContext db, AgentTask task)
    {
        var releases = db.RunnerSeatReleases.AsNoTracking();
        if (string.IsNullOrEmpty(task.RunnerId) || task.AgentSessionId is not Guid sessionId)
            return releases.Where(r => false);
        var taskId = task.Id;
        var attempt = task.Attempt;
        var agentId = task.AgentId;
        var runnerId = task.RunnerId;
        var revision = task.ConcurrencyToken;
        var settledAt = task.CompletedAt;
        return releases.Where(r =>
            r.TaskId == taskId
            && r.Attempt == attempt
            && r.SessionId == sessionId
            && r.AgentId == agentId
            && r.RunnerId == runnerId
            && r.SettlementRevision == revision
            && r.SettledAt == settledAt
            && db.AgentSessions.Any(s =>
                s.Id == sessionId
                && s.RunnerId == runnerId
                && s.RunnerStoreId == r.RunnerStoreId
                && s.StartedAt == r.AcceptedStartedAt));
    }

    /// <summary>
    /// A confirmed physical release: state, confirmation time, action and an outcome that
    /// left no process. Same whitelist as <see cref="TerminalRunnerSeatReleaseService.IsConfirmed"/>.
    /// </summary>
    internal static IQueryable<RunnerSeatRelease> Confirmed(IQueryable<RunnerSeatRelease> releases) =>
        releases.Where(r =>
            r.State == RunnerSeatReleaseState.Confirmed
            && r.ConfirmedAt != null
            && r.ActionId != null
            && (r.OutcomeCode == nameof(TerminalSeatReleaseOutcome.Released)
                || r.OutcomeCode == nameof(TerminalSeatReleaseOutcome.AlreadyExited)
                || r.OutcomeCode == nameof(TerminalSeatReleaseOutcome.AlreadyAbsent)));
}
