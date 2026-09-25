using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0589 D-3: registration and the <c>/build-slots</c> routes, shared by <c>Program.cs</c> and
/// the loopback test host so both serve the same code. Problem answers carry the refusal members at
/// the top level (<c>type</c>, <c>occupied</c>, <c>queuePosition</c>, <c>retryAfterMs</c>, ...)
/// for <c>scripts/lib/build-slot.ps1</c> to branch on.
/// </summary>
public static class BuildSlotRoutes
{
    public static IServiceCollection AddBuildSlotBroker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BuildSlotSettings>()
            .Bind(configuration.GetSection(BuildSlotSettings.SectionName))
            .PostConfigure(settings => settings.Validate())
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessLivenessProbe, SystemProcessLivenessProbe>();
        services.TryAddSingleton<IHostMemoryProbe, SystemHostMemoryProbe>();
        services.TryAddSingleton<BuildSlotBroker>();
        services.AddHostedService<BuildSlotSweepService>();
        return services;
    }

    public static IEndpointRouteBuilder MapBuildSlotRoutes(this IEndpointRouteBuilder app)
    {
        app.MapPost("/build-slots", (BuildSlotRequest? request, BuildSlotBroker broker) =>
        {
            if (request is null)
                return Problem(StatusCodes.Status400BadRequest, BuildSlotProblemTypes.Invalid, "request body required");
            broker.TryAcquire(request, out var outcome);
            return outcome switch
            {
                BuildSlotOutcome.Granted granted => Results.Ok(granted.Grant),
                BuildSlotOutcome.Busy busy => Problem(StatusCodes.Status409Conflict, BuildSlotProblemTypes.Busy, null, new()
                {
                    ["occupied"] = busy.Refusal.Occupied,
                    ["budget"] = busy.Refusal.Budget,
                    ["queuePosition"] = busy.Refusal.QueuePosition,
                    ["retryAfterMs"] = busy.Refusal.RetryAfterMs,
                }),
                BuildSlotOutcome.MemoryFloor floor => Problem(StatusCodes.Status409Conflict, BuildSlotProblemTypes.MemoryFloor, null, new()
                {
                    ["availableMb"] = floor.Refusal.AvailableMb,
                    ["floorMb"] = floor.Refusal.FloorMb,
                    ["queuePosition"] = floor.Refusal.QueuePosition,
                    ["retryAfterMs"] = floor.Refusal.RetryAfterMs,
                }),
                BuildSlotOutcome.Invalid invalid => Problem(StatusCodes.Status400BadRequest, BuildSlotProblemTypes.Invalid, invalid.Reason),
                _ => throw new InvalidOperationException($"unmapped build-slot outcome {outcome}"),
            };
        });

        app.MapDelete("/build-slots/{leaseId:guid}", (Guid leaseId, BuildSlotBroker broker) =>
            broker.Release(leaseId)
                ? Results.NoContent()
                : Problem(StatusCodes.Status404NotFound, BuildSlotProblemTypes.Unknown, $"no lease {leaseId}"));

        app.MapGet("/build-slots", (BuildSlotBroker broker) => Results.Ok(broker.List()));
        return app;
    }

    private static IResult Problem(int status, string type, string? detail, Dictionary<string, object?>? extensions = null) =>
        Results.Problem(title: type, type: type, detail: detail, statusCode: status, extensions: extensions);
}

/// <summary>Reaps between acquires, so a dead holder frees its slot even when nobody is waiting.</summary>
public sealed class BuildSlotSweepService(
    BuildSlotBroker broker,
    IOptions<BuildSlotSettings> settings,
    TimeProvider time,
    ILogger<BuildSlotSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(settings.Value.SweepIntervalMs);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, time, stoppingToken);
                broker.Sweep();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Build slot sweep failed; retrying next interval");
            }
        }
    }
}
