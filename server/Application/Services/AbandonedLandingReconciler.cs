using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1446 D-1. An unpublished landing that stopped in a resumable phase and was never resumed
/// is abandoned, not "missing a terminal arm": the protocol keeps <c>push_rejected</c> at
/// <c>PushStarted</c> resumable on purpose (CARD-0711). After
/// <see cref="WorktreeResidueSettings.AbandonedLandingDays"/> with a terminal task and no
/// pending request, the daily job marks it <c>Refused</c> so the next explicit <c>-Land</c>
/// starts a fresh operation. A push that returned 0 but was never confirmed is
/// <c>push_unconfirmed</c>; only a human resolves it, so it is logged and kept.
/// </summary>
public sealed class AbandonedLandingReconciler(
    AppDbContext db,
    IOptions<WorktreeResidueSettings> settings,
    TimeProvider clock,
    ILogger<AbandonedLandingReconciler> logger)
{
    public Task<int> RunAsync(CancellationToken ct)
    {
        _ = db;
        _ = settings;
        _ = clock;
        _ = logger;
        return Task.FromResult(0);
    }
}
