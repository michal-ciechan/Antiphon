using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Hangfire;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

public sealed class RunnerRetireJob(
    IServiceScopeFactory scopes,
    ISessionRunnerDirectory directory,
    IOptions<PhoneHomeRunnerSettings> settings,
    TimeProvider time,
    ILogger<RunnerRetireJob> logger)
{
    public const string RecurringJobId = "antiphon:runner-retire";

    [AutomaticRetry(Attempts = 0)]
    public async Task<int> RunAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await new RunnerRetireService(db, directory, settings.Value, time, logger).RunAsync(ct);
    }
}
