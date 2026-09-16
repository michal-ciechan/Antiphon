using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>Shared production routes, also hosted on isolated loopback by wire tests.</summary>
public static class SessionReadLaunchRoutes
{
    public static void MapSessionGetRoute(this WebApplication app)
    {
        app.MapGet("/sessions/{id:guid}", async (Guid id, SessionRunnerRuntime runtime, CancellationToken ct) =>
            Results.Ok(await runtime.GetAsync(id, ct)));
    }

    public static void MapSessionLaunchRoute(this WebApplication app)
    {
        app.MapPost("/sessions", async (
            RunnerLaunchRequest request,
            SessionRunnerRuntime runtime,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var session = await runtime.StartAsync(request, cancellationToken);
                return Results.Created($"/sessions/{session.SessionId}", session);
            }
            catch (UnsupportedTranscriptFormatException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (GrokRulesLaunchException ex)
            {
                return GrokRulesProblemMapper.Map(ex);
            }
            catch (CodexLaunchException ex)
            {
                return CodexLaunchProblemMapper.Map(ex);
            }
            catch (GrokRulesTransportException ex)
            {
                return Results.Problem(title: ex.Code, detail: ex.Message, statusCode: ex.StatusCode, type: ex.Code);
            }
            catch (HerdrLaunchException ex)
            {
                return HerdrProblemMapper.MapLaunch(ex);
            }
        });
    }
}
