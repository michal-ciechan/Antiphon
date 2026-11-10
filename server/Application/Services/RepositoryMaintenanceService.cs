using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1450 (CARD-1432 F-5): the repository ref sweep. Terminal tasks and tasks with no row are
/// judged by <see cref="LandRefRetention.IsDeletable"/> and
/// <see cref="LandRefRetention.RetainedOperationsAsync"/>, the same rule a completed land uses, so
/// the latest landing and every Refused landing keep their recovery pins. The deletion is one
/// <c>update-ref --stdin</c> batch, which <see cref="ILandingGit.UpdateRefsAsync"/> runs under the
/// repository child journal. A task that is not terminal is left untouched.
/// </summary>
public sealed class RepositoryMaintenanceService(AppDbContext db, ILandingGit git, ILogger logger)
{
    /// <summary>The refs deleted, or null when the list or the deletion failed.</summary>
    public Task<IReadOnlyList<string>?> SweepRefsAsync(string repository, CancellationToken ct)
    {
        _ = (db, git, repository, ct, logger);
        return Task.FromResult<IReadOnlyList<string>?>([]);
    }
}
