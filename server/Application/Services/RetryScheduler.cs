using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class RetryScheduler
{
    private readonly OrchestratorSettings _settings;

    public RetryScheduler(IOptions<OrchestratorSettings> settings)
    {
        _settings = settings.Value;
    }

    public TimeSpan GetContinuationDelay() =>
        TimeSpan.FromMilliseconds(Math.Max(1, _settings.ContinuationDelayMs));

    public TimeSpan GetFailureDelay(int failureNumber)
    {
        var ordinal = Math.Max(1, failureNumber);
        var uncapped = Math.Max(1L, _settings.FailureBackoffBaseMs);
        for (var i = 1; i < ordinal; i++)
        {
            uncapped *= 2;
            if (uncapped >= _settings.FailureBackoffMaxMs)
                break;
        }

        return TimeSpan.FromMilliseconds(Math.Min(uncapped, Math.Max(1, _settings.FailureBackoffMaxMs)));
    }

    public async Task<RetrySchedule> ScheduleFailureAsync(
        AppDbContext db,
        Guid cardId,
        string error,
        DateTime utcNow,
        CancellationToken ct)
    {
        var schedule = await GetOrCreateScheduleAsync(db, cardId, ct);

        schedule.AttemptCount++;
        schedule.LastAttemptAt = utcNow;
        schedule.LastError = error;
        schedule.NextRetryAt = schedule.AttemptCount >= schedule.MaxAttempts
            ? null
            : utcNow + GetFailureDelay(schedule.AttemptCount);
        return schedule;
    }

    public async Task<RetrySchedule> ScheduleContinuationAsync(
        AppDbContext db,
        Guid cardId,
        DateTime utcNow,
        CancellationToken ct)
    {
        var schedule = await GetOrCreateScheduleAsync(db, cardId, ct);

        schedule.LastAttemptAt = utcNow;
        schedule.LastError = null;
        schedule.NextRetryAt = utcNow + GetContinuationDelay();
        return schedule;
    }

    private static async Task<RetrySchedule> GetOrCreateScheduleAsync(
        AppDbContext db,
        Guid cardId,
        CancellationToken ct)
    {
        var schedule = await db.RetrySchedules
            .FirstOrDefaultAsync(r => r.CardId == cardId, ct);
        if (schedule is not null)
            return schedule;

        schedule = new RetrySchedule
        {
            Id = Guid.NewGuid(),
            CardId = cardId
        };
        db.RetrySchedules.Add(schedule);
        try
        {
            await db.SaveChangesAsync(ct);
            return schedule;
        }
        catch (DbUpdateException)
        {
            db.Entry(schedule).State = EntityState.Detached;
            return await db.RetrySchedules.SingleAsync(r => r.CardId == cardId, ct);
        }
    }
}
