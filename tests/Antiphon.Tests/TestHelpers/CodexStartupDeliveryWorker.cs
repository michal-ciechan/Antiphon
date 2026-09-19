using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0574 V-7: owned-child crash cuts. The parent retains the scripted runner over a named
/// pipe; the child runs dispatcher/launch/queue against that runner and the parent's database.
/// </summary>
internal static class CodexStartupDeliveryWorker
{
    internal const string Marker = "ANTIPHON_C574_STARTUP_WORKER";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    internal sealed record Settings(
        string Connection,
        string Pipe,
        string Cut,
        string ReadyPath,
        Guid SessionId,
        Guid AgentId,
        Guid? TaskId,
        Guid? CallerSessionId,
        string? Body);

    internal static async Task RunAsync(string encoded)
    {
        var settings = JsonSerializer.Deserialize<Settings>(encoded, Json)
            ?? throw new InvalidOperationException("C574 worker settings");
        await using var pipe = new PipeSessionRunnerClient(settings.Pipe);
        var hang = new HangCutInterceptor(settings.Cut, settings.ReadyPath);
        await using var provider = BuildChildProvider(settings.Connection, pipe, hang);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var runtime = sp.GetRequiredService<AgentSessionRuntime>();

        if (settings.CallerSessionId is Guid caller)
        {
            var callerAdapter = new FakeAgentProtocolAdapter { ReadyResult = true };
            callerAdapter.OnSubmitted = async submitted =>
            {
                await BridgeQueueHarness.InsertEntryAsync(
                    caller, TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow,
                    connectionString: settings.Connection);
                await BridgeQueueHarness.InsertEntryAsync(
                    caller, TranscriptKinds.TurnEnd, stopReason: TranscriptKinds.StopReasons.EndTurn,
                    connectionString: settings.Connection);
            };
            runtime.Register(caller, callerAdapter);
        }

        if (settings.Cut is "failed-committed" or "note-committed" or "prompt-accepted"
            or "obligation-insert")
        {
            await sp.GetRequiredService<AgentTaskDispatcher>()
                .FailNeverStartedAsync(CancellationToken.None);
            return;
        }

        if (settings.Cut is "snapshot" or "running")
        {
            var generation = await StartedAtAsync(settings.Connection, settings.SessionId);
            await sp.GetRequiredService<AgentSessionService>().LaunchInteractiveAsync(
                settings.SessionId, settings.AgentId,
                Spec(settings.SessionId, generation, await CwdAsync(settings.Connection, settings.SessionId)),
                null, false, null, CancellationToken.None, acceptedGeneration: generation);
            return;
        }

        var queue = sp.GetRequiredService<SessionMessageQueueService>();
        await queue.EnqueueAsync(
            settings.SessionId, settings.Body ?? "c574 brief",
            MessageSendMode.WhenIdle, CancellationToken.None);
    }

    internal static async Task CrashAsync(Settings settings, ScriptedCodexRunnerClient retained, string assembly)
    {
        using var host = new PipeSessionRunnerHost(settings.Pipe, retained);
        var hosted = host.RunAsync();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        foreach (var arg in new[]
                 {
                     assembly, "--treenode-filter",
                     "/*/*/CodexStartupDeliveryTests/Crash_cut_worker_entry",
                 })
            start.ArgumentList.Add(arg);
        start.Environment[Marker] = JsonSerializer.Serialize(settings, Json);
        using var worker = Process.Start(start)!;
        var stdout = worker.StandardOutput.ReadToEndAsync();
        var stderr = worker.StandardError.ReadToEndAsync();
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            if (settings.Cut == "snapshot")
            {
                while (!retained.WaitingOnSnapshot && !worker.HasExited)
                    await Task.Delay(50, budget.Token);
                retained.WaitingOnSnapshot.ShouldBeTrue(
                    worker.HasExited ? await stderr : "snapshot barrier not reached");
                await File.WriteAllTextAsync(settings.ReadyPath, worker.Id.ToString(), budget.Token);
            }
            else
            {
                while (!File.Exists(settings.ReadyPath) && !worker.HasExited)
                    await Task.Delay(50, budget.Token);
                File.Exists(settings.ReadyPath).ShouldBeTrue(
                    worker.HasExited ? await stderr : $"C574 {settings.Cut} cut not reached");
            }
        }
        finally
        {
            if (!worker.HasExited)
                worker.Kill(entireProcessTree: false);
            await worker.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            host.Stop();
            try { await hosted; }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    internal static Settings CreateSettings(
        string connection, string cut, Guid sessionId, Guid agentId,
        Guid? taskId = null, Guid? callerSessionId = null, string? body = null) =>
        new(
            connection,
            "c574-" + Guid.NewGuid().ToString("N"),
            cut,
            Path.Combine(Path.GetTempPath(), "c574-" + Guid.NewGuid().ToString("N")),
            sessionId, agentId, taskId, callerSessionId, body);

    private static ServiceProvider BuildChildProvider(
        string connection, ISessionRunnerClient runner, HangCutInterceptor hang)
    {
        var registry = new AgentRegistrySettings
        {
            CodexReadyMaxWaitMs = 800,
            CodexReadyQuietPeriodMs = 50,
            CodexBootStatusMaxWaitMs = 0,
            DefaultDefinition = "codex",
            Definitions = { ["codex"] = new AgentDefinition { Kind = "Codex", Exe = "codex.exe" } },
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(connection);
            o.AddInterceptors(hang);
        });
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings
        {
            DeliveryVerification = new DeliveryVerificationSettings
            {
                Enabled = true,
                EvidenceTimeoutSeconds = 1,
                PollIntervalMs = 50,
                PostSubmitAdvanceTimeoutSeconds = 1,
                StrandedAgeSeconds = 0,
                TranscriptConfirmTimeoutSeconds = 3,
                ReEnterIntervalSeconds = 1,
                PostFailureConfirmGraceSeconds = 3,
                UnobservableBaselineConfirmClockToleranceSeconds = 30,
                BootPromptRetryDelaySeconds = 0,
            },
        }));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            DeliveryFailTimeoutMinutes = 10,
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 6,
        }));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(registry));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
            new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(registry));
        services.AddSingleton<ISessionRunnerClient>(runner);
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<ILaunchOwnership>(sp => sp.GetRequiredService<AgentSessionLaunchQueue>());
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<CompletionNoteFlushQueue>();
        services.AddScoped<AgentTaskLandNotificationService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c574-wt"),
        });
        services.AddSingleton<CapacityRecoveryService>();
        services.AddScoped<AgentTaskService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<RoutingPinService>();
        services.AddScoped<ComplexityRoutingService>();
        services.AddScoped<AgentTaskDispatcher>();
        services.AddSingleton<IAgentProtocolAdapterFactory>(sp =>
            new AgentProtocolAdapterFactory(sp.GetRequiredService<IOptions<AgentRegistrySettings>>(), runner));
        services.AddSingleton<IWorktreeManager>(new BridgeQueueHarness.NoWorktreeManager());
        services.AddSingleton<IWorkspaceHookRunner>(
            new Antiphon.Server.Infrastructure.WorkspaceHooks.WorkspaceHookRunner(
                NullLogger<Antiphon.Server.Infrastructure.WorkspaceHooks.WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddScoped<AgentSessionService>();
        return services.BuildServiceProvider();
    }

    private static async Task<DateTime> StartedAtAsync(string connection, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        return await db.AgentSessions.Where(s => s.Id == sessionId).Select(s => s.StartedAt).SingleAsync();
    }

    private static async Task<string> CwdAsync(string connection, Guid sessionId)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
        return await db.AgentSessions.Where(s => s.Id == sessionId).Select(s => s.Cwd).SingleAsync();
    }

    private static AgentLaunchSpec Spec(Guid sessionId, DateTime generation, string cwd) => new(
        DefinitionName: "codex",
        Kind: AgentKind.Codex,
        Exe: "codex.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: cwd,
        Cols: 120,
        Rows: 30,
        SessionId: sessionId,
        AcceptedStartedAt: generation);

    private sealed class HangCutInterceptor(string cut, string readyPath) : SaveChangesInterceptor
    {
        private bool _afterSave;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            var entries = data.Context!.ChangeTracker;
            if (cut is "brief-insert" or "queue-insert"
                && entries.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added))
                await HangAsync(ct);
            if (cut == "obligation-insert"
                && entries.Entries<AgentTaskLandNotification>().Any(e => e.State == EntityState.Added
                    && e.Entity.Kind == LandNotificationKind.DeliveryFailure))
                await HangAsync(ct);
            if (cut == "failed-committed"
                && entries.Entries<AgentTask>().Any(e => e.State == EntityState.Modified
                    && e.Entity.Status == AgentTaskStatus.Failed))
                _afterSave = true;
            if (cut == "note-committed"
                && entries.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added
                    && e.Entity.SourceLandNotificationId != null))
                _afterSave = true;
            if (cut == "attempt-commit"
                && entries.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Modified
                    && e.Entity.Status == QueuedMessageStatus.Sent && e.Entity.DeliveryVerdict is null))
                _afterSave = true;
            if (cut == "running"
                && entries.Entries<AgentSession>().Any(e => e.State == EntityState.Modified
                    && e.Entity.Status == SessionStatus.Running))
                _afterSave = true;
            if (cut is "prompt-accepted" or "receipt"
                && entries.Entries<SessionQueuedMessage>().Any(e =>
                    e.Entity.DeliveryVerdict == DeliveryVerdict.Delivered))
                _afterSave = true;
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (_afterSave)
            {
                _afterSave = false;
                await HangAsync(ct);
            }

            return result;
        }

        private async Task HangAsync(CancellationToken ct)
        {
            await File.WriteAllTextAsync(readyPath, Environment.ProcessId.ToString(), ct);
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    private sealed class PipeSessionRunnerHost : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly NamedPipeServerStream _server;
        private readonly ISessionRunnerClient _inner;

        public PipeSessionRunnerHost(string name, ISessionRunnerClient inner)
        {
            _inner = inner;
            _server = new NamedPipeServerStream(
                name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        public async Task RunAsync()
        {
            await _server.WaitForConnectionAsync(_cts.Token);
            var reader = new StreamReader(_server, Encoding.UTF8, leaveOpen: true);
            var writer = new StreamWriter(_server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            while (!_cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (line is null)
                    break;
                var req = JsonSerializer.Deserialize<PipeRequest>(line, Json)
                    ?? throw new InvalidOperationException("pipe request");
                var resp = await DispatchAsync(req, _cts.Token);
                await writer.WriteLineAsync(JsonSerializer.Serialize(resp, Json));
            }
        }

        public void Stop() => _cts.Cancel();

        public void Dispose()
        {
            _cts.Cancel();
            _server.Dispose();
            _cts.Dispose();
        }

        private async Task<PipeResponse> DispatchAsync(PipeRequest req, CancellationToken ct)
        {
            try
            {
                switch (req.Op)
                {
                    case "Start":
                    {
                        var spec = JsonSerializer.Deserialize<AgentLaunchSpec>(req.Payload ?? "{}", Json)!;
                        return Ok(await _inner.StartAsync(req.SessionId, spec, ct));
                    }
                    case "Get":
                        return Ok(await _inner.GetAsync(req.SessionId, ct));
                    case "List":
                        return Ok(await _inner.ListAsync(ct));
                    case "GetBuffer":
                        return Ok(await _inner.GetBufferAsync(req.SessionId, ct));
                    case "GetSnapshot":
                        return Ok(await _inner.GetSnapshotAsync(req.SessionId, ct));
                    case "GetTranscript":
                        return Ok(await _inner.GetTranscriptAsync(req.SessionId, ct));
                    case "SendInput":
                        await _inner.SendInputAsync(req.SessionId, req.Payload ?? "", ct);
                        return new PipeResponse(true, null, "null");
                    case "Clear":
                        await _inner.ClearLiveBufferAsync(req.SessionId, ct);
                        return new PipeResponse(true, null, "null");
                    case "Resize":
                        await _inner.ResizeAsync(req.SessionId, 120, 30, ct);
                        return new PipeResponse(true, null, "null");
                    case "Kill":
                        return Ok(await _inner.KillAsync(req.SessionId, ct));
                    case "KillGeneration":
                    {
                        var generation = JsonSerializer.Deserialize<DateTime>(req.Payload ?? "null", Json);
                        return Ok(await _inner.KillGenerationAsync(req.SessionId, generation, ct));
                    }
                    default:
                        return new PipeResponse(false, "unknown-op", null);
                }
            }
            catch (Exception ex)
            {
                return new PipeResponse(false, ex.GetType().Name + ": " + ex.Message, null);
            }
        }

        private static PipeResponse Ok<T>(T value) =>
            new(true, null, JsonSerializer.Serialize(value, Json));
    }

    private sealed class PipeSessionRunnerClient(string name) : ISessionRunnerClient, IAsyncDisposable
    {
        private readonly NamedPipeClientStream _client = new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _connected;

        private async Task EnsureAsync(CancellationToken ct)
        {
            if (_connected)
                return;
            await _client.ConnectAsync(15_000, ct);
            _reader = new StreamReader(_client, Encoding.UTF8, leaveOpen: true);
            _writer = new StreamWriter(_client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
            _connected = true;
        }

        private async Task<T> CallAsync<T>(string op, Guid sessionId, string? payload, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            try
            {
                await EnsureAsync(ct);
                await _writer!.WriteLineAsync(
                    JsonSerializer.Serialize(new PipeRequest(op, sessionId, payload), Json).AsMemory(), ct);
                var line = await _reader!.ReadLineAsync(ct)
                    ?? throw new IOException("C574 pipe closed");
                var resp = JsonSerializer.Deserialize<PipeResponse>(line, Json)
                    ?? throw new IOException("C574 pipe response");
                if (!resp.Ok)
                    throw new InvalidOperationException(resp.Error);
                return JsonSerializer.Deserialize<T>(resp.Payload ?? "null", Json)!;
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            CallAsync<SessionRunnerSessionDto>("Start", sessionId, JsonSerializer.Serialize(spec, Json), ct);

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            CallAsync<IReadOnlyList<SessionRunnerSessionDto>>("List", Guid.Empty, null, ct);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            CallAsync<SessionRunnerSessionDto>("Get", sessionId, null, ct);

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            CallAsync<SessionRunnerBufferDto>("GetBuffer", sessionId, null, ct);

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            CallAsync<SessionRunnerSnapshotDto>("GetSnapshot", sessionId, null, ct);

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            CallAsync<SessionRunnerTranscriptDto>("GetTranscript", sessionId, null, ct);

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            CallAsync<object>("SendInput", sessionId, input, ct);

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            CallAsync<object>("Clear", sessionId, null, ct);

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            CallAsync<object>("Resize", sessionId, null, ct);

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            CallAsync<SessionRunnerSessionDto>("Kill", sessionId, null, ct);

        public Task<RunnerKillGenerationResult> KillGenerationAsync(
            Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct) =>
            CallAsync<RunnerKillGenerationResult>(
                "KillGeneration", sessionId, JsonSerializer.Serialize(expectedAcceptedStartedAt, Json), ct);

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async ValueTask DisposeAsync()
        {
            _reader?.Dispose();
            _writer?.Dispose();
            await _client.DisposeAsync();
            _gate.Dispose();
        }
    }

    private sealed record PipeRequest(string Op, Guid SessionId, string? Payload);
    private sealed record PipeResponse(bool Ok, string? Error, string? Payload);
}
