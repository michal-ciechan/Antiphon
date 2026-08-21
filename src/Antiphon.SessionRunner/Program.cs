using Antiphon.Agents.Pty;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging with a bounded rolling file: one file per day, capped size, and retention by
// TIME with the file count kept only as a disk backstop (CARD-0043 — counting files is not counting
// days). Console stays so the supervisor still captures stdout.
//
// Measured 2026-08-13: this sink writes 5-140 KB/day (7 days on disk = 353 KB total), so the 14-file
// x 50 MB backstop is 700 MB of headroom it will never touch — the count cap only starts evicting
// inside the retained window above 5.8 MB/hour, ~1400x the measured rate. The time limit is 14 days
// rather than the 5-day floor CARD-0043 sets for the server log precisely BECAUSE it is this cheap:
// cutting it to 5 would delete nine days of history to save 200 KB.
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
            retainedFileCountLimit: 14,
            retainedFileTimeLimit: TimeSpan.FromDays(14));
});

builder.Services.Configure<SessionRunnerSettings>(builder.Configuration.GetSection("SessionRunner"));
// CARD-0120: the raw client is additive only. No launch or delivery path resolves it until the
// explicitly separate Herdr backend slices opt a session in; HerdrSettings.Enabled defaults false.
builder.Services.Configure<HerdrSettings>(builder.Configuration.GetSection("SessionRunner:Herdr"));
builder.Services.AddSingleton<HerdrClient>();
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
// CPU spin watchdog: an idle-at-the-prompt claude.exe can busy-loop a full core forever (stuck
// render/SSE retry; live incident 2026-08-08). When the transcript says the turn ended but the
// process stays hot for the whole sustained window, the session is killed with reason
// CpuSpinKilled, which the server maps to a clean, resumable stop.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IProcessCpuProbe, SystemProcessCpuProbe>();
builder.Services.AddHostedService<SessionCpuWatchdogService>();

// The pty backend switch (CARD-0037), defaulting OFF. Exported into this process's environment so it
// reaches the DETACHED pty-hosts for free: PtyHostLauncher starts them with UseShellExecute=false
// and no environment override, so they inherit this block. An env var already set on the runner wins
// over appsettings — that is how an operator flips one machine without editing config.
{
    var configured = builder.Configuration[PtyBackendPolicy.ConfigKey];
    if (!string.IsNullOrWhiteSpace(configured)
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(PtyBackendPolicy.EnvVar)))
    {
        Environment.SetEnvironmentVariable(PtyBackendPolicy.EnvVar, configured);
    }
}

var app = builder.Build();
// The assembly stamp and process start cannot change during this process lifetime. Capture them
// once; unlike the pty-backend flag there is no useful per-request re-resolution.
var runnerBuild = RunnerBuildIdentity.Resolve();

// CARD-0101: an unknown session id is routine (a caller racing a session's end, a stale id from
// before a restart) - it must answer 404, not crash the request pipeline with an unhandled
// KeyNotFoundException out of SessionRunnerRuntime.GetSession. Narrow on purpose: this is the ONLY
// exception type every session-lookup endpoint throws for "not found" today, so mapping anything
// wider here would hide a real bug as a 404 instead of surfacing it.
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
});

// Say which pseudoconsole every session on this runner will get, once, at startup. A "modern"
// request that fell back to the inbox conhost looks identical from everywhere else, and it silently
// re-arms the 1 KB clipping the ceilings exist for.
{
    var decision = PtyBackendPolicy.Resolve();
    if (decision.FellBack)
        app.Logger.LogWarning("PTY backend: {Decision}", decision);
    else
        app.Logger.LogInformation("PTY backend: {Decision}", decision);
}

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

// Which pseudoconsole every session here gets, on request. The server's delivery ceilings are
// coupled to this answer (CARD-0037), and it is the one thing it cannot infer from its own
// environment: runner and server are separate processes with separate config, so a server that
// assumed they matched would size bodies for a pty that cannot carry them. Resolved live rather
// than captured at startup so a runner restarted with a different flag reports the truth.
app.MapGet("/capabilities", () =>
{
    var decision = PtyBackendPolicy.Resolve();
    return Results.Ok(new RunnerCapabilitiesDto(
        decision.Backend.ToString(), decision.Requested, decision.Reason, decision.FellBack,
        SessionRunnerRuntime.SupportedTranscriptFormats, runnerBuild));
});

app.MapGet("/sessions", (SessionRunnerRuntime runtime) => Results.Ok(runtime.List()));

app.MapGet("/sessions/{id:guid}", (Guid id, SessionRunnerRuntime runtime) => Results.Ok(runtime.Get(id)));

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
