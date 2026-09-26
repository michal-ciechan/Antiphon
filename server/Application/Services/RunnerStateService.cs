using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0727 D-6. The durable drain row. The directory hears about it only after the save.</summary>
public interface IRunnerStateStore
{
    Task<SessionRunnerState?> FindAsync(string runnerId, CancellationToken ct);
    Task SaveAsync(SessionRunnerState row, CancellationToken ct);
}

public sealed class DbRunnerStateStore(AppDbContext db) : IRunnerStateStore
{
    public Task<SessionRunnerState?> FindAsync(string runnerId, CancellationToken ct) =>
        db.SessionRunnerStates.FirstOrDefaultAsync(s => s.RunnerId == runnerId, ct);

    public async Task SaveAsync(SessionRunnerState row, CancellationToken ct)
    {
        if (db.Entry(row).State == EntityState.Detached)
            db.SessionRunnerStates.Add(row);
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Hosts with no database (a connection-less test host) still answer the route.</summary>
public sealed class MemoryRunnerStateStore : IRunnerStateStore
{
    private readonly Dictionary<string, SessionRunnerState> _rows = new(StringComparer.Ordinal);

    public Task<SessionRunnerState?> FindAsync(string runnerId, CancellationToken ct) =>
        Task.FromResult(_rows.TryGetValue(runnerId, out var row) ? row : null);

    public Task SaveAsync(SessionRunnerState row, CancellationToken ct)
    {
        _rows[row.RunnerId] = row;
        return Task.CompletedTask;
    }
}

/// <summary>CARD-0727 D-9. Validates a drain, writes the row, then mirrors it into the directory.</summary>
public sealed class RunnerStateService(
    IRunnerStateStore store,
    PhoneHomeRunnerDirectory directory,
    IOptions<PhoneHomeRunnerSettings> settings,
    TimeProvider clock)
{
    public const int MaxReasonLength = 200;

    public async Task DrainAsync(
        string runnerId, string? reason, string? redirectTo, bool retireWhenIdle, CancellationToken ct)
    {
        var id = RequireKnown(runnerId);
        var why = RequireReason(reason);
        var target = string.IsNullOrWhiteSpace(redirectTo) ? null : redirectTo.Trim();
        if (target is not null)
            RequireRedirect(id, target);

        var now = clock.GetUtcNow();
        var row = await store.FindAsync(id, ct) ?? new SessionRunnerState { RunnerId = id };
        row.Draining = true;
        row.DrainedAt ??= now;
        row.DrainReason = why;
        row.RedirectTo = target;
        row.RetireWhenIdle = retireWhenIdle;
        row.UpdatedAt = now;
        await store.SaveAsync(row, ct);
        directory.ApplyState(id, ToState(row));
    }

    public async Task ClearAsync(string runnerId, string? reason, Guid? updatedByTaskId, CancellationToken ct)
    {
        var id = RequireKnown(runnerId);
        var why = RequireReason(reason);
        var now = clock.GetUtcNow();
        var row = await store.FindAsync(id, ct) ?? new SessionRunnerState { RunnerId = id };
        row.Draining = false;
        row.DrainedAt = null;
        row.DrainReason = why;
        row.RedirectTo = null;
        row.RetireWhenIdle = false;
        row.IdleObservedAt = null;
        row.RetiredAt = null;
        row.RetireReason = null;
        row.UpdatedAt = now;
        row.UpdatedByTaskId = updatedByTaskId;
        await store.SaveAsync(row, ct);
        directory.ApplyState(id, ToState(row));
    }

    public static RunnerState ToState(SessionRunnerState row) => new(
        row.Draining, row.DrainedAt, row.DrainReason, row.RedirectTo, row.RetireWhenIdle,
        row.IdleObservedAt, row.RetiredAt, row.RetireReason);

    private string RequireKnown(string runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId) || RunnerRequestIntent.IsDesktopAlias(runnerId))
            throw new NotFoundException("SessionRunner", runnerId);
        if (!directory.KnownRunnerIds.Contains(runnerId, StringComparer.Ordinal))
            throw new NotFoundException("SessionRunner", runnerId);
        return runnerId;
    }

    private static string RequireReason(string? reason)
    {
        var why = reason?.Trim() ?? "";
        if (why.Length == 0 || why.Length > MaxReasonLength)
            throw new BadRequestException("A drain reason is required and must be at most 200 characters.");
        return why;
    }

    private void RequireRedirect(string runnerId, string target)
    {
        if (RunnerRequestIntent.IsDesktopAlias(target) || string.Equals(target, runnerId, StringComparison.Ordinal))
            throw Redirect(target);
        var configured = PhoneHomeRunnerCatalog.Configured(settings.Value)
            .FirstOrDefault(entry => string.Equals(entry.Id, target, StringComparison.Ordinal));
        if (configured is null || !configured.Entry.Enabled)
            throw Redirect(target);
        var state = directory.DrainState(target);
        if (state is { Draining: true } || state?.RetiredAt is not null)
            throw Redirect(target);
    }

    private static ConflictException Redirect(string target) =>
        new($"Redirect '{target}' is not an eligible runner.", PhoneHomeProblemTypes.RedirectInvalid);
}
