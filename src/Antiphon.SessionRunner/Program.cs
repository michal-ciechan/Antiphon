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

builder.Services.Configure<SessionRunnerSettings>(builder.Configuration.GetSection("SessionRunner"));
builder.Services.AddSingleton<SessionRunnerRuntime>();
builder.Services.AddHealthChecks();
// Prune PTY-audit dumps on startup and periodically, keeping them within the configured age + count caps
// (regardless of whether auditing is enabled). A runaway audit dump here once filled a disk with ~894 GB.
// Also prunes pty-host shadow-copy dirs and stale host logs.
builder.Services.AddHostedService<AuditCleanupService>();
// Liveness backstop: a session once sat "Running" on a dead PID for a week because the exit was
// never observed — the sweep catches vanished processes and emits the missed SessionExited.
builder.Services.AddSingleton<IProcessLivenessProbe, SystemProcessLivenessProbe>();
builder.Services.AddHostedService<SessionLivenessSweepService>();

var app = builder.Build();

// Readiness gating: adopt pty-hosts that survived the previous runner BEFORE the HTTP API starts
// listening. The server's reconciler treats "runner doesn't know this session" as fatal, so the
// runner must never answer /sessions with a half-adopted list — during the sweep the port is
// simply down, which the reconciler already treats as "skip this cycle".
{
    var runtime = app.Services.GetRequiredService<SessionRunnerRuntime>();
    var probe = app.Services.GetRequiredService<IProcessLivenessProbe>();
    var adopted = await runtime.AdoptOrphanedHostsAsync(probe, CancellationToken.None);
    if (adopted > 0)
        app.Logger.LogInformation("Adopted {Count} surviving pty-host session(s) from a previous runner", adopted);
}

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

app.MapPost("/sessions/kill-all", async (
    SessionRunnerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    var killed = await runtime.KillAllAsync(TimeSpan.FromSeconds(5), cancellationToken);
    return Results.Ok(killed);
});

app.MapPost("/sessions/{id:guid}/kill", async (
    Guid id,
    SessionRunnerRuntime runtime,
    CancellationToken cancellationToken) =>
{
    var session = await runtime.KillAsync(id, TimeSpan.FromSeconds(5), cancellationToken);
    return Results.Ok(session);
});

app.MapGet("/events", async (HttpContext context, SessionRunnerRuntime runtime, IConfiguration config) =>
{
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.ContentType = "text/event-stream";

    var ct = context.RequestAborted;

    // Flush headers immediately: without this a client that connects during a quiet spell can't
    // tell "connected, nothing happening" from "not responding" and its timeout kills the stream
    // before the first event. Periodic SSE comments then keep watchdogs and proxies from reaping
    // an idle-but-healthy connection (clients must ignore lines starting with ':').
    await context.Response.WriteAsync(": connected\n\n", ct);
    await context.Response.Body.FlushAsync(ct);
    var keepAlive = TimeSpan.FromSeconds(
        Math.Clamp(config.GetValue("Events:KeepAliveSeconds", 15), 1, 3600));

    var reader = runtime.Subscribe(ct);
    while (!ct.IsCancellationRequested)
    {
        var waitForEvent = reader.WaitToReadAsync(ct).AsTask();
        var winner = await Task.WhenAny(waitForEvent, Task.Delay(keepAlive, ct));
        if (winner != waitForEvent)
        {
            await context.Response.WriteAsync(": keepalive\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
            continue;
        }

        if (!await waitForEvent)
            break; // channel completed — runtime shutting down

        while (reader.TryRead(out var evt))
        {
            await context.Response.WriteAsync($"event: {evt.EventName}\n", ct);
            await context.Response.WriteAsync($"data: {evt.Json}\n\n", ct);
        }
        await context.Response.Body.FlushAsync(ct);
    }
});

app.Run();
