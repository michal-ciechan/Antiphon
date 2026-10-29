using Antiphon.Agents.Pty;
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

    public static void MapSessionTranscriptRoute(this WebApplication app)
    {
        app.MapGet("/sessions/{id:guid}/transcript", (Guid id, SessionRunnerRuntime runtime) =>
            Results.Ok(runtime.GetTranscript(id)));
    }

    // CARD-0101: an unknown session id is routine (a caller racing a session's end, a stale id from
    // before a restart) - it must answer 404, not crash the request pipeline with an unhandled
    // KeyNotFoundException out of SessionRunnerRuntime.GetSession. Narrow on purpose: this is the ONLY
    // exception type every session-lookup endpoint throws for "not found" today, so mapping anything
    // wider as a 404 would hide a real bug. Unmapped faults stay a separate 500 below.
    internal const string UnhandledExceptionProblemType = "runner_unhandled_exception";

    public static void UseRunnerExceptionMapping(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (KeyNotFoundException ex)
            {
                app.Logger.LogInformation("404: {Message}", ex.Message);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { error = ex.Message });
            }
            catch (VerificationCustodyException ex)
            {
                await Results.Problem(title: ex.Code, type: ex.Code, statusCode: StatusCodes.Status409Conflict)
                    .ExecuteAsync(context);
            }
            catch (SessionIdentityClosedException ex)
            {
                // CARD-1153 A-3: a certified never-created id refuses every later creation; not retryable.
                await Results.Problem(title: RunnerAbsenceEvidence.ClosedIdentityProblemType, detail: ex.Message,
                        type: RunnerAbsenceEvidence.ClosedIdentityProblemType, statusCode: StatusCodes.Status409Conflict)
                    .ExecuteAsync(context);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An unmapped launch failure was an empty 500. Name the exception type and send
                // only its message, redacted and clipped. The stack stays in this log line.
                // Cancellation still propagates, and a response that has already started is rethrown.
                var detail = CommandLineDisclosure.ForProblemDetail(ex.Message);
                // Do not pass ex: the default formatter appends ex.ToString(), which would put the
                // raw message (and any instructions argv still in it) back into the runner log.
                app.Logger.LogError("{Failure}", RedactedFailureLog(ex));
                if (context.Response.HasStarted)
                    throw;
                context.Response.Clear();
                await Results.Problem(
                        title: ex.GetType().FullName,
                        detail: detail,
                        statusCode: StatusCodes.Status500InternalServerError,
                        type: UnhandledExceptionProblemType)
                    .ExecuteAsync(context);
            }
        });
    }

    private static string RedactedFailureLog(Exception ex)
    {
        var sb = new StringBuilder();
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (sb.Length > 0)
                sb.Append(" ---> ");
            sb.Append(current.GetType().FullName)
                .Append(": ")
                .Append(CommandLineDisclosure.ForProblemDetail(current.Message))
                .Append(Environment.NewLine)
                .Append(current.StackTrace);
        }

        return sb.ToString();
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
            catch (RunnerPlatformLaunchException ex)
            {
                return Results.Problem(title: ex.Code, detail: ex.Message, statusCode: ex.StatusCode, type: ex.Code);
            }
            catch (UnixPtyArgvException ex)
            {
                return Results.Problem(title: ex.Code, detail: ex.Message, statusCode: 409, type: ex.Code);
            }
        });
    }

    public static void MapPlatformConstrainedLaunchRoute(this WebApplication app)
    {
        app.MapPost("/sessions/platform-constrained", async (
            RunnerLaunchRequest request,
            SessionRunnerRuntime runtime,
            CancellationToken cancellationToken) =>
        {
            if (!RunnerPlatformWire.IsSpecific(request.RequiredPlatform))
            {
                return Results.Problem(
                    title: RunnerPlatformLaunchGuard.Invalid,
                    detail: "A platform-constrained launch requires requiredPlatform windows or linux.",
                    statusCode: 409,
                    type: RunnerPlatformLaunchGuard.Invalid);
            }

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
            catch (RunnerPlatformLaunchException ex)
            {
                return Results.Problem(title: ex.Code, detail: ex.Message, statusCode: ex.StatusCode, type: ex.Code);
            }
            catch (UnixPtyArgvException ex)
            {
                return Results.Problem(title: ex.Code, detail: ex.Message, statusCode: 409, type: ex.Code);
            }
        });
    }
}
