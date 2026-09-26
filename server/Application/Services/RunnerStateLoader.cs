using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0727 D-6. Loads every <c>SessionRunnerStates</c> row into the directory before the
/// recovery pump. The red-commit body is a no-op; the production body applies each row.
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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Production applies each row. The red body keeps the directory empty so a rebuilt
        // directory does not yet refuse new work.
        _ = (_scopes, _directory, cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
