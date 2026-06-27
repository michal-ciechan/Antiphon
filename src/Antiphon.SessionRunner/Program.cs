using Antiphon.Agents.Pty;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging with a bounded rolling file: one file per day, capped size, and only a window of
// files retained so logs can never run the disk out. Console stays so the supervisor still captures stdout.
builder.Host.UseSerilog((ctx, lc) =>
{
    var logPath = ctx.Configuration["Serilog:LogPath"]
        ?? Path.Combine(Path.GetTempPath(), "antiphon-logs");
    lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(logPath, "session-runner-.log"),
            rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: 50 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            retainedFileCountLimit: 14);
});

// Clean up stale PTY-audit dumps on startup regardless of whether auditing is enabled. A runaway audit
// dump here once filled a disk with ~894 GB; this keeps %TEMP%\antiphon-pty-audits bounded.
_ = Task.Run(() => PtySessionAudit.PruneOldAudits(TimeSpan.FromDays(2)));

builder.Services.Configure<SessionRunnerSettings>(builder.Configuration.GetSection("SessionRunner"));
builder.Services.AddSingleton<SessionRunnerRuntime>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.MapGet("/sessions", (SessionRunnerRuntime runtime) => Results.Ok(runtime.List()));

app.MapGet("/sessions/{id:guid}", (Guid id, SessionRunnerRuntime runtime) => Results.Ok(runtime.Get(id)));

app.MapPost("/sessions", async (
    RunnerLaunchRequest request,
    SessionRunnerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    var session = await runtime.StartAsync(request, cancellationToken);
    return Results.Created($"/sessions/{session.SessionId}", session);
});

app.MapGet("/sessions/{id:guid}/buffer", (Guid id, SessionRunnerRuntime runtime) =>
    Results.Ok(runtime.GetBuffer(id)));

app.MapGet("/sessions/{id:guid}/snapshot", (Guid id, SessionRunnerRuntime runtime) =>
    Results.Ok(runtime.GetSnapshot(id)));

app.MapGet("/sessions/{id:guid}/transcript", (Guid id, SessionRunnerRuntime runtime) =>
    Results.Ok(runtime.GetTranscript(id)));

app.MapPost("/sessions/{id:guid}/input", async (
    Guid id,
    RunnerInputRequest request,
    SessionRunnerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    await runtime.SendInputAsync(id, request.Input, cancellationToken);
    return Results.NoContent();
});

app.MapPost("/sessions/{id:guid}/clear-live-buffer", async (
    Guid id,
    SessionRunnerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    await runtime.ClearLiveBufferAsync(id, cancellationToken);
    return Results.NoContent();
});

app.MapPost("/sessions/{id:guid}/resize", async (
    Guid id,
    RunnerResizeRequest request,
    SessionRunnerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    await runtime.ResizeAsync(id, request.Cols, request.Rows, cancellationToken);
    return Results.NoContent();
});

app.MapPost("/sessions/{id:guid}/kill", async (
    Guid id,
    SessionRunnerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    var session = await runtime.KillAsync(id, TimeSpan.FromSeconds(5), cancellationToken);
    return Results.Ok(session);
});

app.MapGet("/events", async (HttpContext context, SessionRunnerRuntime runtime) =>
{
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.ContentType = "text/event-stream";

    var reader = runtime.Subscribe(context.RequestAborted);
    await foreach (var evt in reader.ReadAllAsync(context.RequestAborted))
    {
        await context.Response.WriteAsync($"event: {evt.EventName}\n", context.RequestAborted);
        await context.Response.WriteAsync($"data: {evt.Json}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }
});

app.Run();
