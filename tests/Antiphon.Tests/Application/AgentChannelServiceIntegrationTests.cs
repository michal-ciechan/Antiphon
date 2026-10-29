using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentChannel")]
public class AgentChannelServiceIntegrationTests
{
    [Test]
    public async Task MentionScanner_extracts_at_mentions_from_ansi_stripped_text()
    {
        var scanner = new MentionScanner();

        var mentions = scanner.Extract("\u001b[32m@helper please review the patch\u001b[0m\r\nnoise @other: run tests");

        mentions.Count.ShouldBe(2);
        mentions[0].Target.ShouldBe("helper");
        mentions[0].Message.ShouldBe("please review the patch");
        mentions[1].Target.ShouldBe("other");
        mentions[1].Message.ShouldBe("run tests");
    }

    [Test]
    public async Task WaitUntilAsync_timeout_attaches_the_stage_trail()
    {
        Exception? caught = null;
        try
        {
            await WaitUntilAsync(
                () => false,
                () => $"{Environment.NewLine}last stage: debounce-fired",
                TimeSpan.FromMilliseconds(40));
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.ShouldNotBeNull();
        caught!.ToString().ShouldContain("WaitUntilAsync timed out");
        caught.ToString().ShouldContain("last stage: debounce-fired");
    }

    [Test]
    public async Task ChannelHub_mention_routes_to_target_session_stdin()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var sourceSession = NewSession(graph.SourceCard, "lead");
            var targetSession = NewSession(graph.TargetCard, "helper");
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.AddRange(sourceSession, targetSession);
            await db.SaveChangesAsync();
            graph.SourceCard.OwnerSessionId = sourceSession.Id;
            graph.TargetCard.OwnerSessionId = targetSession.Id;
            await db.SaveChangesAsync();

            var sourceAdapter = new FakeAgentProtocolAdapter();
            var targetAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [sourceAdapter, targetAdapter]);
            harness.Runtime.Register(sourceSession.Id, sourceAdapter);
            targetAdapter.OnSubmitted = async body =>
            {
                await BridgeQueueHarness.InsertEntryAsync(targetSession.Id, TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);
                await BridgeQueueHarness.InsertEntryAsync(targetSession.Id, TranscriptKinds.TurnEnd, stopReason: "end_turn");
            };
            harness.Runtime.Register(targetSession.Id, targetAdapter);

            sourceAdapter.Emit("@helper please inspect failing test");

            // Route returns when the mention is accepted. The submitting Enter records
            // the body before OnSubmitted commits the UserPrompt, so wait for both.
            await WaitUntilAsync(
                () => targetAdapter.SubmittedBodies.Count == 1
                    && CountUserPrompts(targetSession.Id, "[channel from lead] please inspect failing test") == 1,
                () => DescribeChannelPipeline(harness, sourceSession.Id, targetAdapter));
            targetAdapter.SubmittedBodies.ShouldBe(["[channel from lead] please inspect failing test"]);
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == targetSession.Id && t.Kind == TranscriptKinds.UserPrompt
                && t.Text == "[channel from lead] please inspect failing test")).ShouldBe(1);
            harness.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "ChannelMessage"
                && e.Group == AgentChannelGroups.Card(graph.TargetCard.Id));
            // Stages that precede SendInput. The submitted body is the signal that
            // the route finished and the Enter ran; these must already be on the trail.
            var stages = harness.RouteDiagnostics.Snapshot().Select(s => s.Stage).ToList();
            stages.ShouldContain(MentionRouteDiagnostics.DeltaObserved);
            stages.ShouldContain(MentionRouteDiagnostics.PendingScheduled);
            stages.ShouldContain(MentionRouteDiagnostics.DebounceDelayStarted);
            stages.ShouldContain(MentionRouteDiagnostics.DebounceFired);
            stages.ShouldContain(MentionRouteDiagnostics.CommandEnqueued);
            stages.ShouldContain(MentionRouteDiagnostics.CommandDequeued);
            stages.ShouldContain(MentionRouteDiagnostics.RouteStarted);
            stages.ShouldContain(MentionRouteDiagnostics.SourceQueryReturned);
            stages.ShouldContain(MentionRouteDiagnostics.TargetQueryReturned);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Channel_mention_to_missing_runtime_target_publishes_ignored_event()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var sourceSession = NewSession(graph.SourceCard, "lead");
            var targetSession = NewSession(graph.TargetCard, "helper");
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.AddRange(sourceSession, targetSession);
            await db.SaveChangesAsync();
            graph.SourceCard.OwnerSessionId = sourceSession.Id;
            graph.TargetCard.OwnerSessionId = targetSession.Id;
            await db.SaveChangesAsync();

            var sourceAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [sourceAdapter]);
            harness.Runtime.Register(sourceSession.Id, sourceAdapter);

            sourceAdapter.Emit("@helper are you online?");

            await WaitUntilAsync(
                () => harness.EventBus.PublishedEvents.Any(e => e.EventName == "ChannelMentionIgnored"),
                () => DescribeChannelPipeline(harness, sourceSession.Id));
            harness.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "ChannelMentionIgnored"
                && e.Group == AgentChannelGroups.Card(graph.SourceCard.Id));
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Channel_mention_split_across_pty_chunks_routes_once_after_pending_line_settles()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var sourceSession = NewSession(graph.SourceCard, "lead");
            var targetSession = NewSession(graph.TargetCard, "helper");
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.AddRange(sourceSession, targetSession);
            await db.SaveChangesAsync();
            graph.SourceCard.OwnerSessionId = sourceSession.Id;
            graph.TargetCard.OwnerSessionId = targetSession.Id;
            await db.SaveChangesAsync();

            var sourceAdapter = new FakeAgentProtocolAdapter();
            var targetAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [sourceAdapter, targetAdapter]);
            harness.Runtime.Register(sourceSession.Id, sourceAdapter);
            targetAdapter.OnSubmitted = async body =>
            {
                await BridgeQueueHarness.InsertEntryAsync(targetSession.Id, TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);
                await BridgeQueueHarness.InsertEntryAsync(targetSession.Id, TranscriptKinds.TurnEnd, stopReason: "end_turn");
            };
            harness.Runtime.Register(targetSession.Id, targetAdapter);

            sourceAdapter.Emit("@helper p");
            await Task.Delay(450);
            targetAdapter.SentInput.ShouldBeEmpty();
            sourceAdapter.Emit("lease ");
            sourceAdapter.Emit("inspect failing test");

            await WaitUntilAsync(
                () => targetAdapter.SubmittedBodies.Count == 1,
                () => DescribeChannelPipeline(harness, sourceSession.Id, targetAdapter));

            CountOccurrences(targetAdapter.SentInput, "[channel from lead]").ShouldBe(1);
            targetAdapter.SubmittedBodies.ShouldBe(["[channel from lead] please inspect failing test"]);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // '@helper please inspect' is pending, then ' it\n' completes the line while the liveness
    // read is still in flight. The debounce must not queue the shorter text as well.
    [Test]
    public async Task Held_liveness_read_routes_the_split_mention_once() =>
        await AssertSplitMentionDeliveredOnceAsync(SplitLiveness.Live);

    [Test]
    public async Task Failed_liveness_read_still_delivers_the_split_mention_once() =>
        await AssertSplitMentionDeliveredOnceAsync(SplitLiveness.Failed);

    [Test]
    public async Task Not_live_stamp_still_delivers_the_split_mention_once() =>
        await AssertSplitMentionDeliveredOnceAsync(SplitLiveness.NotLive);

    // The desktop pump and a phone-home worker both await ObserveOutputAsync before the next
    // event. A delivery whose transcript never arrives must not hold that call: the next observe
    // has to finish, the second mention has to be queued, and the held delivery must not be
    // marked unconfirmed just because the pump could not record a transcript.
    [Test]
    public async Task Held_flush_does_not_block_the_next_observe_or_drop_the_second_mention()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var sourceSession = NewSession(graph.SourceCard, "lead");
            var helper = NewSession(graph.TargetCard, "helper");
            var other = NewSession(graph.TargetCard, "other");
            // Raw delivers blind. Claude waits on the transcript, which is the 30s hold.
            helper.AgentKind = AgentKind.ClaudeCode;
            other.AgentKind = AgentKind.ClaudeCode;
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.AddRange(sourceSession, helper, other);
            await db.SaveChangesAsync();

            var sourceAdapter = new FakeAgentProtocolAdapter();
            var helperAdapter = new FakeAgentProtocolAdapter();
            var otherAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [sourceAdapter, helperAdapter, otherAdapter]);
            harness.Runtime.Register(sourceSession.Id, sourceAdapter);
            harness.Runtime.Register(helper.Id, helperAdapter);
            harness.Runtime.Register(other.Id, otherAdapter);
            // A Claude session with no dialog observation is withheld before SendInput.
            // Bind the persisted generation and a readable composer so the first delivery
            // types, submits, and then waits on a transcript that never arrives.
            await helperAdapter.EnableClaudeDialogObservationAsync(TestDbFixture.ConnectionString, helper.Id);
            await otherAdapter.EnableClaudeDialogObservationAsync(TestDbFixture.ConnectionString, other.Id);

            var started = DateTime.UtcNow;
            var pumpDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(async () =>
            {
                try
                {
                    await harness.Runtime.ObserveOutputAsync(sourceSession.Id, 1, "@helper one\n", CancellationToken.None);
                    await harness.Runtime.ObserveOutputAsync(sourceSession.Id, 2, "@other two\n", CancellationToken.None);
                    pumpDone.TrySetResult();
                }
                catch (Exception ex)
                {
                    pumpDone.TrySetException(ex);
                }
            });

            var finished = await Task.WhenAny(pumpDone.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            finished.ShouldBe(pumpDone.Task,
                $"the next observe on the same pump was still blocked after {(DateTime.UtcNow - started).TotalMilliseconds:F0}ms."
                + DescribeChannelPipeline(harness, sourceSession.Id, helperAdapter));
            await pumpDone.Task;

            await WaitUntilAsync(
                () => MentionRows(db, other.Id, "two") == 1,
                () => DescribeChannelPipeline(harness, sourceSession.Id, otherAdapter));
            (await FreshMentionAsync(other.Id, "two")).ShouldNotBeNull();

            await WaitUntilAsync(
                () => helperAdapter.SubmittedBodies.Count == 1,
                () => DescribeChannelPipeline(harness, sourceSession.Id, helperAdapter),
                TimeSpan.FromSeconds(8));
            var submitted = helperAdapter.SubmittedBodies.Single();
            await harness.Runtime.ObserveTranscriptAsync(
                new SessionRunnerTranscriptEvent(
                    helper.Id, 1, TranscriptKinds.UserPrompt, "held-flush-confirm", null, DateTimeOffset.UtcNow,
                    "user", submitted, null, null, null, null, null),
                CancellationToken.None);

            await WaitUntilAsync(
                () => FreshMention(helper.Id, "one") is { DeliveryVerdict: DeliveryVerdict.Delivered },
                () => DescribeMention(helper.Id, "one") + DescribeChannelPipeline(harness, sourceSession.Id, helperAdapter),
                TimeSpan.FromSeconds(8));
            FreshMention(helper.Id, "one")!.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
            (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(20));
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Queued_commands_at_dispose_do_not_leave_the_observer_blocked()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var sourceSession = NewSession(graph.SourceCard, "lead");
            var helper = NewSession(graph.TargetCard, "helper");
            var other = NewSession(graph.TargetCard, "other");
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.AddRange(sourceSession, helper, other);
            await db.SaveChangesAsync();

            var sourceAdapter = new FakeAgentProtocolAdapter();
            var helperAdapter = new FakeAgentProtocolAdapter();
            var otherAdapter = new FakeAgentProtocolAdapter();
            var harness = BuildHarness(tempRoot, [sourceAdapter, helperAdapter, otherAdapter]);
            harness.Runtime.Register(sourceSession.Id, sourceAdapter);
            harness.Runtime.Register(helper.Id, helperAdapter);
            harness.Runtime.Register(other.Id, otherAdapter);
            var router = harness.Provider.GetRequiredService<AgentMentionRouter>();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            router.RouteGateEntered = entered;
            router.BeforeRouteGate = gate;
            Exception? blocked = null;
            try
            {
                var observe = harness.Runtime.ObserveOutputAsync(
                    sourceSession.Id, 1, "@helper one\n@other two\n", CancellationToken.None);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                router.Dispose();
                var finished = await Task.WhenAny(observe, Task.Delay(TimeSpan.FromSeconds(3)));
                finished.ShouldBe(observe, "a command still queued at dispose left the observer waiting");
                await observe;
            }
            catch (Exception ex)
            {
                blocked = ex;
            }
            finally
            {
                gate.TrySetResult();
            }

            // The container disposes this singleton again. Keep the observer assertion as the
            // failure when both fire; a second Dispose that throws is still a failure on its own.
            Exception? disposeError = null;
            try
            {
                await harness.DisposeAsync();
            }
            catch (Exception ex)
            {
                disposeError = ex;
            }

            if (blocked is not null)
                throw blocked;
            if (disposeError is not null)
                throw disposeError;
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Route_refuses_a_source_whose_generation_changed_after_the_line()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var sourceSession = NewSession(graph.SourceCard, "lead");
            var helper = NewSession(graph.TargetCard, "helper");
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.AddRange(sourceSession, helper);
            await db.SaveChangesAsync();

            var sourceAdapter = new FakeAgentProtocolAdapter();
            var helperAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [sourceAdapter, helperAdapter]);
            harness.Runtime.Register(sourceSession.Id, sourceAdapter);
            harness.Runtime.Register(helper.Id, helperAdapter);
            var router = harness.Provider.GetRequiredService<AgentMentionRouter>();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            router.RouteGateEntered = entered;
            router.BeforeRouteGate = gate;
            try
            {
                var observe = harness.Runtime.ObserveOutputAsync(
                    sourceSession.Id, 1, "@helper hello\n", CancellationToken.None);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var started = sourceSession.StartedAt.AddMinutes(5);
                await using (var edit = CreateContext())
                {
                    var row = await edit.AgentSessions.SingleAsync(s => s.Id == sourceSession.Id);
                    row.StartedAt = started;
                    await edit.SaveChangesAsync();
                }

                gate.TrySetResult();
                await WaitUntilAsync(
                    () => harness.RouteDiagnostics.Snapshot().Any(e => e.Stage == MentionRouteDiagnostics.RouteReturned),
                    () => DescribeChannelPipeline(harness, sourceSession.Id, helperAdapter));
                FreshMention(helper.Id, "hello").ShouldBeNull();
                harness.RouteDiagnostics.Snapshot().ShouldContain(e =>
                    e.Stage == MentionRouteDiagnostics.RouteReturned && e.Detail == "ok=false reason=other-agent");
                await observe.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                gate.TrySetResult();
            }
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static int CountUserPrompts(Guid sessionId, string text)
    {
        using var db = CreateContext();
        return db.TranscriptEntries.Count(t => t.AgentSessionId == sessionId
            && t.Kind == TranscriptKinds.UserPrompt && t.Text == text);
    }

    private static int MentionRows(AppDbContext db, Guid sessionId, string text) =>
        db.SessionQueuedMessages.Count(m => m.AgentSessionId == sessionId
            && m.Origin == QueuedMessageOrigin.Mention && m.Body.Contains(text));

    private static SessionQueuedMessage? FreshMention(Guid sessionId, string text)
    {
        using var db = CreateContext();
        return db.SessionQueuedMessages.AsNoTracking().SingleOrDefault(m => m.AgentSessionId == sessionId
            && m.Origin == QueuedMessageOrigin.Mention && m.Body.Contains(text));
    }

    private static Task<SessionQueuedMessage?> FreshMentionAsync(Guid sessionId, string text) =>
        Task.FromResult(FreshMention(sessionId, text));

    private static string DescribeMention(Guid sessionId, string text)
    {
        var row = FreshMention(sessionId, text);
        return row is null
            ? $"{Environment.NewLine}mention '{text}': (no row)"
            : $"{Environment.NewLine}mention '{text}': status={row.Status} verdict={row.DeliveryVerdict}";
    }

    [Test]
    public async Task Channel_delegate_claims_via_optimistic_concurrency()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            var token = graph.TargetCard.ConcurrencyToken;
            var firstAdapter = new FakeAgentProtocolAdapter { PromptOutput = "CLAIMED_FIRST", TurnCompleted = true };
            var secondAdapter = new FakeAgentProtocolAdapter { PromptOutput = "CLAIMED_SECOND", TurnCompleted = true };
            await using var firstHarness = BuildHarness(tempRoot, [firstAdapter]);
            await using var secondHarness = BuildHarness(tempRoot, [secondAdapter]);
            var firstService = firstHarness.Scope.ServiceProvider.GetRequiredService<AgentChannelService>();
            var secondService = secondHarness.Scope.ServiceProvider.GetRequiredService<AgentChannelService>();
            var request = new ChannelDelegateCardRequest(
                graph.TargetCard.Id,
                token,
                "Take this delegated task",
                DefinitionName: "fake");

            var attempts = await Task.WhenAll(
                CaptureAsync(() => firstService.DelegateCardAsync(request, CancellationToken.None)),
                CaptureAsync(() => secondService.DelegateCardAsync(request, CancellationToken.None)));
            await firstHarness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await secondHarness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            attempts.Count(a => a.Result is not null).ShouldBe(1);
            attempts.Count(a => a.Exception is ConflictException).ShouldBe(1);
            await using var verify = CreateContext();
            var card = await verify.Cards
                .Include(c => c.AgentSessions)
                .Include(c => c.BoardColumn)
                .SingleAsync(c => c.Id == graph.TargetCard.Id);
            card.OwnerSessionId.ShouldNotBeNull();
            card.Status.ShouldBe(CardStatus.Review);
            card.BoardColumn.StateKey.ShouldBe("review");
            card.AgentSessions.Count.ShouldBe(1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static async Task<AttemptResult> CaptureAsync(Func<Task<SpawnCardResult>> action)
    {
        try
        {
            return new AttemptResult(await action(), null);
        }
        catch (Exception ex)
        {
            return new AttemptResult(null, ex);
        }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(string tempRoot, IReadOnlyList<IAgentProtocolAdapter> adapters)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 1_000,
            KillGraceMs = 100,
            SignalRMaxChunkChars = 16 * 1024,
            ReplayBufferMaxChars = 128 * 1024,
            SessionLogPath = Path.Combine(tempRoot, "session-logs")
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            InternalTrackerRepositoryPathPrefix = tempRoot
        }));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
        {
            DefaultDefinition = "fake",
            Definitions = { ["fake"] = new AgentDefinition { Kind = "Raw", Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe") } }
        }));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<MentionScanner>();
        services.AddSingleton<MentionRouteDiagnostics>();
        services.AddScoped<AgentChannelService>();
        services.AddSingleton<AgentMentionRouter>();
        services.AddSingleton<IWorktreeManager>(new FakeWorktreeManager(Path.Combine(tempRoot, "worktrees")));
        services.AddSingleton<IAgentProtocolAdapterFactory>(new QueueAdapterFactory(adapters));
        services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        // OrchestratorService depends on AgentSessionLaunchComposer since 1b1b667 (2026-08-26);
        // this harness's copy of that registration was missed at the time.
        services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings()));
        services.AddScoped<AgentSessionLaunchComposer>();
        services.AddScoped<OrchestratorService>();
        // CardService now depends on AgentReviewCheckpointService (files-review checkpoints);
        // register it and its GitWorkspaceService dep alongside, as ReviewLoopTests does.
        services.AddGitWorkspaceService();
        services.AddScoped<AgentReviewCheckpointService>();
        services.AddScoped<CardService>();
        services.AddLogging();
        // Transcript confirm reads persisted UserPrompt rows. Without the store,
        // ObserveTranscriptAsync records nothing and a verified delivery never confirms.
        services.AddSingleton(Options.Create(new SessionStateSettings()));
        services.AddSingleton<ISessionStateLoader, SessionStateLoader>();
        services.AddSingleton<SessionStateStore>();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(
            provider,
            scope,
            provider.GetRequiredService<AgentSessionRuntime>(),
            provider.GetRequiredService<AgentSessionLaunchQueue>(),
            eventBus,
            provider.GetRequiredService<MentionRouteDiagnostics>());
    }

    private static Graph NewGraph(string tempRoot)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Channel Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/channel.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Channel Board {Guid.NewGuid():N}",
            TrackerKind = TrackerKind.Internal,
            MaxConcurrentSessions = 2,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);
        var backlog = NewColumn(board, "backlog", "Backlog", 0, CardStatus.Backlog, isActive: false);
        var active = NewColumn(board, "in-progress", "In Progress", 1, CardStatus.InProgress, isActive: true);
        var review = NewColumn(board, "review", "Review", 2, CardStatus.Review, isActive: false);
        board.Columns.Add(backlog);
        board.Columns.Add(active);
        board.Columns.Add(review);
        var definition = new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Default",
            Content = """
                name: E11
                stages:
                  - name: Run
                    executorType: raw
                """,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.WorkflowDefinitions.Add(definition);
        var sourceCard = NewCard(board, active, "CARD-0001", "Source");
        var targetCard = NewCard(board, backlog, "CARD-0002", "Target");
        board.Cards.Add(sourceCard);
        board.Cards.Add(targetCard);
        active.Cards.Add(sourceCard);
        backlog.Cards.Add(targetCard);
        return new Graph(project, board, sourceCard, targetCard);
    }

    private static BoardColumn NewColumn(Board board, string stateKey, string name, int order, CardStatus status, bool isActive)
    {
        var now = DateTime.UtcNow;
        return new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = stateKey,
            Name = name,
            ColumnOrder = order,
            CardStatus = status,
            IsActive = isActive,
            IsTerminal = false,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
    }

    private static Card NewCard(Board board, BoardColumn column, string identifier, string title)
    {
        var now = DateTime.UtcNow;
        return new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = title,
            Description = title,
            Status = column.CardStatus,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
            BoardColumn = column
        };
    }

    private static AgentSession NewSession(Card card, string definitionName)
    {
        var now = DateTime.UtcNow;
        return new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            DefinitionName = definitionName,
            AgentKind = AgentKind.Raw,
            Status = SessionStatus.Running,
            Cwd = $"D:/worktrees/{card.Identifier}",
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            Card = card
        };
    }

    private static async Task CleanupProjectsByTempRootAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
            return;

        var boardIds = await db.Boards.Where(b => projectIds.Contains(b.ProjectId)).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        var sessionIds = await db.AgentSessions.Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value)).Select(s => s.Id).ToListAsync();
        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteUpdateAsync(updates => updates.SetProperty(c => c.OwnerSessionId, (Guid?)null));
        await db.RetrySchedules.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.RunAttempts.Where(a => cardIds.Contains(a.CardId)).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Worktrees.Where(w => cardIds.Contains(w.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private enum SplitLiveness
    {
        Live,
        Failed,
        NotLive,
    }

    private async Task AssertSplitMentionDeliveredOnceAsync(SplitLiveness outcome)
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = NewGraph(tempRoot);
            var sourceSession = NewSession(graph.SourceCard, "lead");
            var targetSession = NewSession(graph.TargetCard, "helper");
            db.Projects.Add(graph.Project);
            await db.SaveChangesAsync();
            db.AgentSessions.AddRange(sourceSession, targetSession);
            await db.SaveChangesAsync();
            graph.SourceCard.OwnerSessionId = sourceSession.Id;
            graph.TargetCard.OwnerSessionId = targetSession.Id;
            await db.SaveChangesAsync();

            var sourceAdapter = new FakeAgentProtocolAdapter();
            var targetAdapter = new FakeAgentProtocolAdapter();
            await using var harness = BuildHarness(tempRoot, [sourceAdapter, targetAdapter]);
            harness.Runtime.Register(sourceSession.Id, sourceAdapter);
            harness.Runtime.Register(targetSession.Id, targetAdapter);
            var router = harness.Provider.GetRequiredService<AgentMentionRouter>();
            var clock = new FakeTimeProvider();
            router.Clock = clock;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? observing = null;
            try
            {
                await router.ObserveCompletedAsync(
                    sourceSession.Id, "@helper please inspect", capture: null, CancellationToken.None);
                await WaitUntilAsync(
                    () => HasStage(harness.RouteDiagnostics, sourceSession.Id, MentionRouteDiagnostics.DebounceDelayStarted),
                    () => harness.RouteDiagnostics.Format(sourceSession.Id));
                await Task.Yield();

                observing = router.ObserveCompletedAsync(
                    sourceSession.Id,
                    " it\n",
                    async ct =>
                    {
                        entered.TrySetResult();
                        await release.Task.WaitAsync(ct);
                        if (outcome == SplitLiveness.Failed)
                            throw new InvalidOperationException("liveness read failed");

                        await using var stampDb = CreateContext();
                        var row = await stampDb.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sourceSession.Id);
                        return new MentionSourceLiveness(
                            row.StartedAt,
                            row.CardId,
                            row.StandingAgentId,
                            outcome == SplitLiveness.Live);
                    },
                    CancellationToken.None);

                await WaitUntilAsync(
                    () => entered.Task.IsCompleted,
                    () => harness.RouteDiagnostics.Format(sourceSession.Id));
                await WaitUntilAsync(
                    () =>
                    {
                        if (DebounceResolvedWhileHeld(harness.RouteDiagnostics, sourceSession.Id))
                            return true;

                        clock.Advance(TimeSpan.FromMilliseconds(300));
                        return DebounceResolvedWhileHeld(harness.RouteDiagnostics, sourceSession.Id);
                    },
                    () => harness.RouteDiagnostics.Format(sourceSession.Id));
                release.TrySetResult();
                await observing;
                observing = null;

                var trail = harness.RouteDiagnostics.Format(sourceSession.Id);
                CountStages(harness.RouteDiagnostics, sourceSession.Id, MentionRouteDiagnostics.CommandEnqueued)
                    .ShouldBe(1, trail);
                await WaitUntilAsync(
                    () => MentionBodies(targetSession.Id).Count == 1,
                    () => harness.RouteDiagnostics.Format(sourceSession.Id),
                    TimeSpan.FromSeconds(20));
                await WaitUntilAsync(
                    () => RouteAttemptsSettled(harness.RouteDiagnostics, sourceSession.Id),
                    () => harness.RouteDiagnostics.Format(sourceSession.Id),
                    TimeSpan.FromSeconds(20));
                trail = harness.RouteDiagnostics.Format(sourceSession.Id);
                CountStages(harness.RouteDiagnostics, sourceSession.Id, MentionRouteDiagnostics.CommandEnqueued)
                    .ShouldBe(1, trail);
                var bodies = MentionBodies(targetSession.Id);
                bodies.Count.ShouldBe(1, trail);
                bodies.Single().ShouldContain("please inspect it");
            }
            finally
            {
                release.TrySetResult();
                clock.Advance(TimeSpan.FromMinutes(1));
                if (observing is not null)
                {
                    try { await observing; } catch (OperationCanceledException) { }
                }
            }
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static List<string> MentionBodies(Guid sessionId)
    {
        using var db = CreateContext();
        return db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId && m.Origin == QueuedMessageOrigin.Mention)
            .Select(m => m.Body)
            .ToList();
    }

    private static bool HasStage(MentionRouteDiagnostics diagnostics, Guid sourceId, string stage) =>
        diagnostics.Snapshot().Any(s => s.SourceSessionId == sourceId && s.Stage == stage);

    private static int CountStages(MentionRouteDiagnostics diagnostics, Guid sourceId, string stage) =>
        diagnostics.Snapshot().Count(s => s.SourceSessionId == sourceId && s.Stage == stage);

    private static bool DebounceResolvedWhileHeld(MentionRouteDiagnostics diagnostics, Guid sourceId)
    {
        foreach (var stage in diagnostics.Snapshot())
        {
            if (stage.SourceSessionId != sourceId)
                continue;

            if (stage.Stage is MentionRouteDiagnostics.DebounceFired
                or MentionRouteDiagnostics.DebounceStale
                or MentionRouteDiagnostics.DebounceCancelled
                or MentionRouteDiagnostics.CommandEnqueued)
                return true;
        }

        return false;
    }

    private static bool RouteAttemptsSettled(MentionRouteDiagnostics diagnostics, Guid sourceId)
    {
        var stages = diagnostics.Snapshot().Where(s => s.SourceSessionId == sourceId).ToArray();
        var enqueued = stages.Count(s => s.Stage == MentionRouteDiagnostics.CommandEnqueued);
        var dequeued = stages.Count(s => s.Stage == MentionRouteDiagnostics.CommandDequeued);
        var finished = stages.Count(s => s.Stage is MentionRouteDiagnostics.RouteReturned or MentionRouteDiagnostics.RouteFailed);
        if (enqueued == 0 || enqueued != dequeued || dequeued != finished)
            return false;

        for (var i = stages.Length - 1; i >= 0; i--)
        {
            var stage = stages[i].Stage;
            if (stage == MentionRouteDiagnostics.DebounceDelayStarted)
                return false;

            if (stage is MentionRouteDiagnostics.DebounceFired
                or MentionRouteDiagnostics.DebounceStale
                or MentionRouteDiagnostics.DebounceCancelled
                or MentionRouteDiagnostics.DebounceNoMentions
                or MentionRouteDiagnostics.DebounceNotReady
                or MentionRouteDiagnostics.PendingCleared)
                return true;
        }

        return true;
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        Func<string>? timeoutDetail = null,
        TimeSpan? timeout = null)
    {
        var started = DateTime.UtcNow;
        var budget = timeout ?? TimeSpan.FromSeconds(5);
        var deadline = started + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(25);
        }

        var elapsedMs = (DateTime.UtcNow - started).TotalMilliseconds;
        var detail = timeoutDetail?.Invoke();
        predicate().ShouldBeTrue(
            $"WaitUntilAsync timed out after {elapsedMs:F0}ms (budget {budget.TotalMilliseconds:F0}ms)."
            + (detail ?? ""));
    }

    private static string DescribeChannelPipeline(
        Harness harness,
        Guid sourceSessionId,
        FakeAgentProtocolAdapter? target = null)
    {
        var last = harness.RouteDiagnostics.Last;
        var lastLabel = last is null
            ? "(none — ObserveDelta never recorded)"
            : $"{last.Value.Stage}{(string.IsNullOrEmpty(last.Value.Detail) ? "" : " " + last.Value.Detail)}";
        var events = harness.EventBus.PublishedEvents.Count == 0
            ? "(none)"
            : string.Join(", ", harness.EventBus.PublishedEvents.Select(e => e.EventName));
        var live = string.Join(", ", harness.Runtime.ListLiveSessions());
        var input = target is null
            ? "(no target adapter)"
            : TruncateForTimeout(target.SentInput);
        return $"""

            last stage: {lastLabel}
            stages:
            {harness.RouteDiagnostics.Format(sourceSessionId)}
            events: {events}
            live sessions: {live}
            target.SentInput: {input}
            """;
    }

    private static string TruncateForTimeout(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "(empty)";
        const int max = 200;
        return text.Length <= max ? text : text[..max] + $"… ({text.Length} chars)";
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-channel-tests-{Guid.NewGuid():N}");

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp worktrees.
        }
    }

    private sealed record Graph(Project Project, Board Board, Card SourceCard, Card TargetCard);

    private sealed record AttemptResult(SpawnCardResult? Result, Exception? Exception);

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        AgentSessionRuntime Runtime,
        AgentSessionLaunchQueue LaunchQueue,
        MockEventBus EventBus,
        MentionRouteDiagnostics RouteDiagnostics) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class QueueAdapterFactory : IAgentProtocolAdapterFactory
    {
        private readonly Queue<IAgentProtocolAdapter> _adapters;

        public QueueAdapterFactory(IEnumerable<IAgentProtocolAdapter> adapters)
        {
            _adapters = new Queue<IAgentProtocolAdapter>(adapters);
        }

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            if (_adapters.TryDequeue(out var adapter))
                return adapter;

            throw new InvalidOperationException("No fake adapter was queued for channel dispatch.");
        }
    }

    private sealed class FakeWorktreeManager : IWorktreeManager
    {
        private readonly string _worktreeRoot;

        public FakeWorktreeManager(string worktreeRoot)
        {
            _worktreeRoot = worktreeRoot;
        }

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(_worktreeRoot);
            var worktreePath = Path.Combine(_worktreeRoot, $"card-{cardId}");
            Directory.CreateDirectory(worktreePath);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new WorktreeInfo(cardId, repoPath, worktreePath, $"feat/card-{cardId}", baseRef, now, now));
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
    {
        public OptionsMonitorStub(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
