using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Test-only construction of a producer-originated <see cref="SessionRunnerExitedEvent"/>.
/// Captures the row's accepted generation at the call site rather than letting production
/// invent one from the current incarnation.
/// </summary>
internal static class SessionExitObservation
{
    public static async Task<SessionExitDisposition> ObserveMatchingAsync(
        AgentSessionRuntime runtime,
        Guid sessionId,
        int? exitCode,
        AgentExitReason reason,
        Func<AppDbContext> dbFactory,
        CancellationToken ct = default)
    {
        DateTime generation;
        await using (var db = dbFactory())
            generation = (await db.AgentSessions.SingleAsync(s => s.Id == sessionId, ct)).StartedAt;
        return await runtime.ObserveExitAsync(
            new SessionRunnerExitedEvent(sessionId, exitCode, reason, 0, generation), ct);
    }
}
