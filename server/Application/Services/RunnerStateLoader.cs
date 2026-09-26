using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0727 D-6. Loads every <c>SessionRunnerStates</c> row into the directory before the
/// recovery pump. A host with no database starts empty.
/// </summary>
public sealed class RunnerStateLoader : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PhoneHomeRunnerDirectory _directory;

    public RunnerStateLoader(IServiceScopeFactory scopes, PhoneHomeRunnerDirectory directory)
    {
        _scopes = scopes;
        _directory = directory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetService<AppDbContext>();
        if (db is null)
            return;
        var rows = await db.SessionRunnerStates.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var row in rows)
            _directory.ApplyState(row.RunnerId, RunnerStateService.ToState(row));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
