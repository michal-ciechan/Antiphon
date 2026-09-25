using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Api.Endpoints;

public static class RunnerDefaultEndpoints
{
    public static void MapRunnerDefaultEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/runner-defaults").WithTags("RunnerDefaults");

        group.MapGet("/", async (RunnerDefaultSettingsService defaults, CancellationToken ct) =>
            Results.Ok(await defaults.GetAsync(ct)));

        group.MapPut("/", async (
            PutRunnerDefaultsRequest request,
            HttpContext http,
            RunnerDefaultSettingsService defaults,
            AgentTaskService tasks,
            CancellationToken ct) =>
        {
            var caller = await AgentTaskEndpoints.ResolvePollingCallerAsync(http, tasks, ct);
            return Results.Ok(await defaults.PutAsync(request, caller?.Task?.Id, ct));
        });

        group.MapGet("/revisions", async (
            long? beforeRevision,
            int? limit,
            RunnerDefaultSettingsService defaults,
            CancellationToken ct) =>
            Results.Ok(await defaults.RevisionsAsync(beforeRevision, limit ?? 50, ct)));
    }
}
