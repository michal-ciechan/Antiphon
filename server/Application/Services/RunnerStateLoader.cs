using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0727 D-6. Loads every <c>SessionRunnerStates</c> row into the directory before the
/// recovery pump. A host with no database starts empty.
/// </summary>
public sealed class RunnerStateLoader : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PhoneHomeRunnerDirectory _directory;
    private readonly ILogger<RunnerStateLoader> _logger;

    public RunnerStateLoader(
        IServiceScopeFactory scopes,
        PhoneHomeRunnerDirectory directory,
        ILogger<RunnerStateLoader>? logger = null)
    {
        _scopes = scopes;
        _directory = directory;
        _logger = logger ?? NullLogger<RunnerStateLoader>.Instance;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetService<AppDbContext>();
        if (db is null)
            return;
        var rows = await db.SessionRunnerStates.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            try
            {
                _directory.ApplyState(row.RunnerId, RunnerStateService.ToState(row));
            }
            catch (NotFoundException)
            {
                _logger.LogWarning(
                    "Skipping SessionRunnerStates row {RunnerId}: that id is not configured.",
                    row.RunnerId);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
