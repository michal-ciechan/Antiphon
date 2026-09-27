using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
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

    public Task<int> RunAsync(CancellationToken ct) => throw new NotImplementedException();
}
