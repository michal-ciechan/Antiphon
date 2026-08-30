using System.Diagnostics;
using Antiphon.FakeLlmApi;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Shared AgentSessionService + DirectSessionRunnerClient plumbing for CARD-0168 B-server/B-herdr.
/// Mirrors <c>AgentSessionServiceIntegrationTests</c>, with transcript events forwarded and
/// Claude/Grok transcript tailing opted in.
/// </summary>
internal static class RealCliStubBServerHarness
{
    public const string PtyBackend = "modern";

    public static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    public static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        return services.BuildServiceProvider();
    }

    public sealed record Built(
        AgentSessionService Service,
        AgentSessionRuntime Runtime,
        SessionMessageQueueService Queue);

    public static Built BuildService(
        AppDbContext db,
        string worktreeRoot,
        MockEventBus eventBus,
        ServiceProvider provider,
        DirectSessionRunnerClient runnerClient,
        AgentKind kind,
        string exe)
    {
        var sessionSettings = Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 60_000,
            KillGraceMs = 5_000,
            SignalRMaxChunkChars = 16 * 1024,
            ReplayBufferMaxChars = 128 * 1024,
            SessionLogPath = Path.Combine(worktreeRoot, "session-logs"),
            RemoteControlArmTimeoutMs = 1_000,
            RemoteControlSetupTimeoutMs = 2_000,
        });
        var runtime = new AgentSessionRuntime(
            runnerClient,
            eventBus,
            sessionSettings,
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
        StartEventBridge(runnerClient, runtime);

        var hookRunner = new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance);
        var hookService = new WorkspaceHookService(hookRunner, NullLogger<WorkspaceHookService>.Instance);
        var worktreeManager = new WorktreeManager(
            Options.Create(new GitSettings
            {
                WorktreeBasePath = worktreeRoot,
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24
            }),
            TimeProvider.System,
            NullLogger<WorktreeManager>.Instance);

        var kindName = kind.ToString();
        var adapterFactory = new AgentProtocolAdapterFactory(
            Options.Create(new AgentRegistrySettings
            {
                DefaultDefinition = "stub-cli",
                Definitions =
                {
                    ["stub-cli"] = new AgentDefinition
                    {
                        Kind = kindName,
                        Exe = exe,
                        ArgsTemplate = kind == AgentKind.ClaudeCode
                            ? ["--dangerously-skip-permissions"]
                            : [],
                    }
                },
                ClaudeReadyQuietPeriodMs = 5_000,
                ClaudeReadyMaxWaitMs = 60_000,
                ClaudeReadyMinTotalWaitMs = 9_000,
                ClaudeDoneMaxWaitMs = 90_000,
                GrokReadyQuietPeriodMs = 3_000,
                GrokReadyMaxWaitMs = 60_000,
                GrokDoneMaxWaitMs = 90_000,
            }),
            runnerClient,
            Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings
                {
                    BootPromptRetryDelaySeconds = 0,
                    TranscriptConfirmTimeoutSeconds = 30,
                },
            }));

        var queue = BuildMessageQueue(provider, runtime, eventBus);
        var service = new AgentSessionService(
            db,
            worktreeManager,
            hookService,
            adapterFactory,
            runtime,
            eventBus,
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            sessionSettings,
            Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { BootPromptRetryDelaySeconds = 0 },
            }),
            TimeProvider.System,
            NullLogger<AgentSessionService>.Instance);
        return new Built(service, runtime, queue);
    }

    public static SessionMessageQueueService BuildMessageQueue(
        ServiceProvider provider, AgentSessionRuntime runtime, MockEventBus eventBus) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            runtime,
            eventBus,
            TimeProvider.System,
            NullLogger<SessionMessageQueueService>.Instance);

    public static void StartEventBridge(ISessionRunnerClient runnerClient, AgentSessionRuntime runtime)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in runnerClient.StreamEventsAsync(CancellationToken.None))
                {
                    if (evt.Output is not null)
                    {
                        await runtime.ObserveOutputAsync(
                            evt.Output.SessionId, evt.Output.Sequence, evt.Output.Text, CancellationToken.None);
                    }
                    else if (evt.Exited is not null)
                    {
                        await runtime.ObserveExitAsync(
                            evt.Exited.SessionId, evt.Exited.ExitCode, evt.Exited.ExitReason, CancellationToken.None);
                    }
                    // Do NOT forward SessionTranscript events. Live-stream arrival order
                    // (AssistantText+TurnEnd before a late UserPrompt) rebases the prompt past
                    // the end and IsWorkingAsync stays true forever — Max(activity.Ts) equals
                    // TurnEnd.Ts because AssistantText shares it, so the timestamp override
                    // does not fire. CatchUpTranscriptAsync pulls the runner snapshot in file
                    // order and persists one monotonic batch. The test polls CatchUp itself.
                }
            }
            catch
            {
                // Test bridge exits when the direct runner event stream is closed.
            }
        });
    }

    public static Graph CreateGraph(string repoPath)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = repoPath,
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Board {Guid.NewGuid():N}",
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "backlog",
            Name = "Backlog",
            ColumnOrder = 0,
            CardStatus = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.Columns.Add(column);
        var definition = new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Default",
            Content = "name: stub\nstages:\n  - name: Run\n    executorType: raw\n",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.WorkflowDefinitions.Add(definition);
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = $"S4-{Guid.NewGuid():N}"[..20],
            Title = "CARD-0168 B-server",
            Status = CardStatus.Backlog,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
            BoardColumn = column
        };
        board.Cards.Add(card);
        column.Cards.Add(card);
        return new Graph(project, card);
    }

    public static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
                return;
            await Task.Delay(200);
        }

        (await predicate()).ShouldBeTrue(because);
    }

    /// <summary>
    /// Timestamp-aware idle (same predicate FlushIfIdle uses). Waiting for a TurnEnd row is
    /// not enough: a late-arriving UserPrompt rebased past that TurnEnd reads as working until
    /// the timestamp override proves the activity predates the end.
    /// </summary>
    /// <summary>
    /// Poll the runner snapshot until <paramref name="ready"/>, then CatchUp once so
    /// PersistTranscriptAsync stores the file-order batch. Catching up earlier persists an
    /// incomplete snapshot and rebases a later UserPrompt past TurnEnd.
    /// </summary>
    public static async Task CatchUpWhenSnapshotReadyAsync(
        ISessionRunnerClient client,
        AgentSessionRuntime runtime,
        Guid sessionId,
        Func<SessionRunnerTranscriptDto, bool> ready,
        TimeSpan timeout,
        string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            SessionRunnerTranscriptDto snap;
            try
            {
                snap = await client.GetTranscriptAsync(sessionId, CancellationToken.None);
            }
            catch
            {
                await Task.Delay(250);
                continue;
            }

            if (ready(snap))
            {
                await runtime.CatchUpTranscriptAsync(sessionId, CancellationToken.None);
                return;
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException(because);
    }

    public static bool SnapshotHasBootTurn(SessionRunnerTranscriptDto snap, string bootNonce) =>
        snap.Entries.Any(e =>
            e.Kind == TranscriptKinds.UserPrompt
            && e.Text is not null
            && e.Text.Contains(bootNonce, StringComparison.Ordinal))
        && snap.Entries.Any(e => e.Kind == TranscriptKinds.TurnEnd);

    public static async Task WaitUntilIdleAsync(Built built, Guid sessionId, TimeSpan timeout)
    {
        try
        {
            await WaitUntilAsync(
                async () =>
                {
                    await built.Runtime.CatchUpTranscriptAsync(sessionId, CancellationToken.None);
                    await using var verify = CreateContext();
                    return !await SessionMessageQueueService.IsWorkingAsync(
                        verify, sessionId, CancellationToken.None);
                },
                timeout,
                "session must be idle (IsWorkingAsync false) before WhenIdle is enqueued");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                await DescribeTranscriptAsync(sessionId, built),
                ex);
        }
    }

    private static async Task<string> DescribeTranscriptAsync(Guid sessionId, Built built)
    {
        await using var verify = CreateContext();
        var entries = await verify.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .OrderBy(t => t.Sequence)
            .ToListAsync();
        var kinds = entries.Select(t =>
        {
            var text = (t.Text ?? "").Replace('\n', ' ');
            if (text.Length > 80) text = text[..80];
            var ts = t.Timestamp.HasValue ? t.Timestamp.Value.ToString("HH:mm:ss.fff") : "null";
            return $"{t.Sequence}:{t.Kind}@{ts}:{text}";
        });
        var working = await SessionMessageQueueService.IsWorkingAsync(
            verify, sessionId, CancellationToken.None);
        return $"working={working} live={built.Runtime.ListLiveSessions().Contains(sessionId)} "
            + $"transcript=[{string.Join(" | ", kinds)}]";
    }

    /// <summary>
    /// Poll CatchUp + FlushIfIdle until the session's queued row is Sent. A single flush at
    /// "TurnEnd visible" can no-op if the CLI starts another short turn (title, retry) before
    /// the test enqueues; re-flushing matches the supervisor tick that would otherwise deliver.
    /// </summary>
    public static async Task WaitForQueuedSentAsync(Built built, Guid sessionId, TimeSpan timeout)
    {
        try
        {
            await WaitUntilAsync(
                async () =>
                {
                    await built.Runtime.CatchUpTranscriptAsync(sessionId, CancellationToken.None);
                    await built.Queue.FlushIfIdleAsync(sessionId, CancellationToken.None);
                    await using var verify = CreateContext();
                    var row = await verify.SessionQueuedMessages.AsNoTracking()
                        .FirstOrDefaultAsync(m => m.AgentSessionId == sessionId);
                    return row is { Status: QueuedMessageStatus.Sent };
                },
                timeout,
                "WhenIdle delivery must reach Sent via transcript confirm");
        }
        catch (Exception ex)
        {
            await using var verify = CreateContext();
            var msgs = await verify.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == sessionId)
                .Select(m => $"{m.Status}/attempts={m.DeliveryAttempts}/base={m.LastDeliveryBaselineSequence}")
                .ToListAsync();
            throw new InvalidOperationException(
                $"WhenIdle did not reach Sent. msgs=[{string.Join("; ", msgs)}] "
                + await DescribeTranscriptAsync(sessionId, built),
                ex);
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    public sealed record Graph(Project Project, Card Card);

    public sealed class GitRepo : IDisposable
    {
        private GitRepo(string tempRoot, string repoPath, string worktreeRoot)
        {
            TempRoot = tempRoot;
            RepoPath = repoPath;
            WorktreeRoot = worktreeRoot;
        }

        public string TempRoot { get; }
        public string RepoPath { get; }
        public string WorktreeRoot { get; }

        public static async Task<GitRepo> CreateAsync()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-bserver-{Guid.NewGuid():N}");
            var repoPath = Path.Combine(tempRoot, "repo");
            var worktreeRoot = Path.Combine(tempRoot, "worktrees");
            Directory.CreateDirectory(repoPath);
            Directory.CreateDirectory(worktreeRoot);
            await RunGitAsync(repoPath, "init");
            await RunGitAsync(repoPath, "config", "user.email", "test@antiphon.dev");
            await RunGitAsync(repoPath, "config", "user.name", "Antiphon Test");
            await File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), "# Test");
            await RunGitAsync(repoPath, "add", "README.md");
            await RunGitAsync(repoPath, "commit", "-m", "init");
            await RunGitAsync(repoPath, "branch", "-M", "main");
            return new GitRepo(tempRoot, repoPath, worktreeRoot);
        }

        public void Dispose() => TryDelete(TempRoot);

        private static async Task RunGitAsync(string cwd, params string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("git failed to start");
            await p.WaitForExitAsync();
            if (p.ExitCode != 0)
            {
                var err = await p.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"git {string.Join(' ', args)}: {err}");
            }
        }
    }
}
