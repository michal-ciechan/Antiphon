using System.Runtime.InteropServices;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0324 S4: dispatch a registry-Grok pool task against fakegrok in sign-in mode with the
/// credential probe disabled. The session fails inside the ready window, nothing is typed
/// into the sign-in screen, and the dead-session sweep codes the task AuthenticationRequired.
/// </summary>
[Category("Integration")]
[NotInParallel(["Headed", "AgentQueue"])]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokSignInRuntimeTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeGrokExe =>
        Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.exe");

    private const string ModernBackend = "modern";

    // 2x the 32s stored serial duration (nightly-class-costs.json, run 8f055f87,
    // n=1, may-understate, not a p95). The method timeout is above the two
    // sequential 64s caps so each cap can report before TUnit cancels.
    private static readonly TimeSpan SignInWallCap = TimeSpan.FromSeconds(64);

    [Test]
    [Timeout(160_000)]
    public async Task A_sign_in_mode_fakegrok_pool_task_never_types_the_brief(CancellationToken cancellationToken)
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeGrokExe))
            throw new SkipTestException($"fakegrok.exe not staged at {FakeGrokExe} — build the solution first");

        using var workspace = new TempDir("antiphon-signin-ws");
        using var grokHome = new TempDir("antiphon-signin-home");
        var inputLog = Path.Combine(grokHome.Path, "signin-input.log");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var harness = BuildHarness(workspace.Path, grokHome.Path, inputLog, clock);

        var now = clock.GetUtcNow().UtcDateTime;
        var parentSessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = parentSessionId,
                DefinitionName = "claude",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = workspace.Path,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = $"CARD-0324 sign-in {taskId:N}"[..40],
                Goal = "HEAD-MARKER this brief must never be typed into the sign-in screen\nTAIL-MARKER",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path,
                Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.Session,
                ParentSessionId = parentSessionId,
                CreatedAt = now,
                ConcurrencyToken = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        Guid sessionId;
        try
        {
            using (var scope = harness.Provider.CreateScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                await dispatcher.TickAsync(CancellationToken.None);
            }

            await using (var after = CreateContext())
            {
                var dispatched = await after.AgentTasks.SingleAsync(t => t.Id == taskId);
                dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched, dispatched.FailureReason);
                sessionId = dispatched.AgentSessionId.ShouldNotBeNull();
            }

            // The 30s budget stays on the test clock. 64s wall is 2x the 32s stored
            // serial duration (run 8f055f87, n=1, may-understate, not a p95).
            using (var idleWall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                idleWall.CancelAfter(SignInWallCap);
                try
                {
                    await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(30), idleWall.Token, clock);
                }
                catch (OperationCanceledException) when (idleWall.IsCancellationRequested
                    && !cancellationToken.IsCancellationRequested)
                {
                    throw new System.TimeoutException(
                        $"launch idle exceeded the {SignInWallCap.TotalSeconds:0}s wall cap before the test clock elapsed.");
                }
            }

            await using (var live = CreateContext())
            {
                var session = await live.AgentSessions.SingleAsync(s => s.Id == sessionId);
                session.Status.ShouldBe(SessionStatus.Failed);
                session.LaunchBlock.ShouldBe(SessionLaunchBlock.ProviderSignInRequired);
                session.FailureReason.ShouldContain("grok login");
            }

            if (File.Exists(inputLog))
            {
                var typed = await File.ReadAllTextAsync(inputLog);
                typed.ShouldBeEmpty("the brief must never be typed into the sign-in screen");
            }

            (await CreateContext().AgentIncidents.CountAsync(i =>
                i.Kind == AgentIncidentKind.ProviderSignInRequired
                && i.FailureReason == GrokSignInIncident.EpisodeKey(grokHome.Path)))
                .ShouldBe(1);

            using (var scope = harness.Provider.CreateScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                var runner = harness.Provider.GetRequiredService<ISessionRunnerClient>();
                // Gate 2 forgets the grace stamp while the runner still lists the session Running.
                // That tick must not be the observation the two-minute advance is waiting on.
                if (await RunnerListsRunningAsync(runner, sessionId))
                {
                    await dispatcher.TickAsync(cancellationToken);
                    var runningDeadline = TimeProvider.System.GetUtcNow() + SignInWallCap;
                    while (await RunnerListsRunningAsync(runner, sessionId))
                    {
                        if (TimeProvider.System.GetUtcNow() >= runningDeadline)
                            throw new System.TimeoutException(
                                $"runner stayed Running for {SignInWallCap.TotalSeconds:0}s wall time.");
                        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                    }
                }

                await dispatcher.TickAsync(cancellationToken);
                clock.Advance(TimeSpan.FromMinutes(2));
                await dispatcher.TickAsync(cancellationToken);
            }

            await using var verify = CreateContext();
            var failed = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
            failed.Status.ShouldBe(AgentTaskStatus.Failed);
            failed.FailureCode.ShouldBe(AgentTaskFailureCode.AuthenticationRequired);
            failed.FailureReason.ShouldContain("grok login");

            var notes = await verify.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == parentSessionId)
                .Select(m => m.Body)
                .ToListAsync();
            notes.ShouldContain(b => b.Contains("grok login"));
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.SessionQueuedMessages.Where(m => m.AgentSessionId == parentSessionId)
                .ExecuteDeleteAsync();
            await cleanup.AgentTaskEvents.Where(e => e.AgentTaskId == taskId).ExecuteDeleteAsync();
            await cleanup.AgentTasks.Where(t => t.Id == taskId).ExecuteDeleteAsync();
            await cleanup.AgentIncidents.Where(i =>
                i.Kind == AgentIncidentKind.ProviderSignInRequired
                && i.FailureReason == GrokSignInIncident.EpisodeKey(grokHome.Path))
                .ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task idle_budget_follows_the_test_clock_while_a_launch_is_incomplete()
    {
        var clock = new FakeTimeProvider();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var wait = AgentSessionLaunchQueue.WaitForPendingAsync(
                () => [blocked.Task], TimeSpan.FromSeconds(30), CancellationToken.None, clock);
            clock.Advance(TimeSpan.FromSeconds(30));
            var finished = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMilliseconds(500)));
            finished.ShouldBe(wait, "idle budget must follow the test clock");
            await Should.ThrowAsync<OperationCanceledException>(wait);
        }
        finally
        {
            blocked.TrySetResult();
        }
    }

    private static async Task<bool> RunnerListsRunningAsync(ISessionRunnerClient runner, Guid sessionId)
    {
        var listed = await runner.ListAsync(CancellationToken.None);
        return listed.Any(session => session.SessionId == sessionId
            && string.Equals(session.Status, "Running", StringComparison.OrdinalIgnoreCase));
    }

    private static Harness BuildHarness(
        string workspacePath, string grokHome, string inputLog, FakeTimeProvider clock)
    {
        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-signin-runner-{Guid.NewGuid():N}");
        var runner = new RecordingRunnerClient(
            new DirectSessionRunnerClient(sessionLogPath, ptyBackend: ModernBackend));
        var deadSessions = new DeadSessionFirstSeenState();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        services.AddSingleton<IEventBus, MockEventBus>();
        // Ready-wait and launch timers run in real time; only the dispatcher grace is advanced.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(new SupervisionSettings
        {
            DeliveryVerification = new DeliveryVerificationSettings
            {
                PollIntervalMs = 200,
                TranscriptConfirmTimeoutSeconds = 25,
            },
        }));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            MaxConcurrentTasks = 512,
            AllowedRoots = [],
            DeadSessionFailGraceMinutes = 1,
            CheckEnabled = false,
            SubagentGraceMinutes = 0,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.GrokCredentialProbeEnabled = false;
            s.GrokReadyQuietPeriodMs = 50;
            s.GrokReadyMaxWaitMs = 8000;
            s.GrokReadyMinTotalWaitMs = 0;
            s.Definitions["claude"] = new AgentDefinition
            {
                Kind = "ClaudeCode",
                Exe = "claude-not-configured-for-this-test",
            };
            s.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok",
                Exe = FakeGrokExe,
                ArgsTemplate = ["--always-approve", "--no-alt-screen"],
                Env = new Dictionary<string, string>
                {
                    ["GROK_HOME"] = grokHome,
                    ["ANTIPHON_FAKE_SIGN_IN"] = "1",
                    ["ANTIPHON_FAKE_SIGN_IN_EXIT_MS"] = "8000",
                    ["ANTIPHON_FAKE_INPUT_LOG"] = inputLog,
                },
            };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<ISessionRunnerClient>(runner);
        services.AddSingleton<IAgentProtocolAdapterFactory, AgentProtocolAdapterFactory>();
        services.AddSingleton<IWorkspaceHookRunner, WorkspaceHookRunner>();
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-signin-wt-{Guid.NewGuid():N}"),
        });
        services.AddSingleton(deadSessions);
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<AgentTaskDispatcher>(sp, clock));
        services.AddSingleton(sp => new PtyDeliveryProfile(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<PtyDeliveryProfile>>(),
            sp.GetRequiredService<IOptions<DelegationSettings>>(),
            clock,
            backendOverride: ModernBackend));

        var provider = services.BuildServiceProvider();
        return new Harness(provider, provider.GetRequiredService<AgentSessionLaunchQueue>());
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed record Harness(ServiceProvider Provider, AgentSessionLaunchQueue LaunchQueue)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Provider.DisposeAsync();
    }

    private sealed class TempDir(string prefix) : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory(prefix).FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
