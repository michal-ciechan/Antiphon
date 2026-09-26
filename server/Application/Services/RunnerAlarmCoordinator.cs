using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class RunnerAlarmCoordinator(
    IRunnerEligibilitySnapshotSource runners,
    IRunnerAlarmExclusion exclusion,
    RunnerAlarmState state,
    RepositoryChildJournalInspector journals,
    AppDbContext db,
    IRunnerAlarmNotifier notifier,
    CompletionNoteFlushQueue flushes,
    IOptions<AlarmSettings> settings,
    ILogger<RunnerAlarmCoordinator> logger)
{
    public Task EvaluateRunnersAsync(DateTimeOffset now, CancellationToken ct)
    {
        _ = (runners, exclusion, state, db, notifier, flushes, settings, logger, now, ct);
        return Task.CompletedTask;
    }

    public Task EvaluateJournalsAsync(IReadOnlyList<string>? repositories, DateTimeOffset now, CancellationToken ct)
    {
        _ = (journals, repositories, now, ct);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> RegisteredRepositoriesAsync(CancellationToken ct)
    {
        _ = ct;
        return Task.FromResult<IReadOnlyList<string>>([]);
    }
}
