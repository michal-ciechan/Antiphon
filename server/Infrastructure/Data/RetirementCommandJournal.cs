using Antiphon.Server.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data;

public sealed class RetirementCommandJournal(IServiceScopeFactory scopes, TimeProvider clock) : IRetirementCommandJournal
{
    public async Task<bool> TryCommitIntentAsync(Guid attemptId, Guid commandId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var attempt = await db.TaskWorktreeRetirementAttempts.SingleOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null) return false;
        if (attempt.CommandIntentId is Guid existing) return existing == commandId;
        attempt.CommandIntentId = commandId;
        attempt.CommandIntentAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task RecordComponentAsync(Guid attemptId, bool? directory, bool? registration, bool? branch, string? residue, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var attempt = await db.TaskWorktreeRetirementAttempts.SingleOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null) return;
        if (directory is not null) attempt.DirectoryRemoved = directory;
        if (registration is not null) attempt.RegistrationRemoved = registration;
        if (branch is not null) attempt.BranchRemoved = branch;
        attempt.Residue = residue is { Length: > 400 } ? residue[..400] : residue;
        attempt.FinishedAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }
}
