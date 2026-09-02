using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0084 S6 — the whole card in one pass, on FakeGrok: the caller's <c>-Kind Grok</c> choice
/// travels from <c>scripts/delegate.ps1</c> through task creation, dispatch, a REAL launched
/// process, brief delivery, turn settlement and pricing, and every one of those stages is the
/// production object, not a stand-in for it.
///
/// <para>S1-S5 each proved their own seam and stopped at its edge: S1 drove the spill gate through a
/// pty but built the brief by hand, S3 asserted the launch ARGUMENTS a dispatch composes but
/// deliberately spawned nothing, and S5 priced a settle whose transcript rows were seeded. The
/// failure this test exists to catch is the one none of them can see — a seam that is correct on
/// both sides and wrong in the middle. A kind that reaches the task row but not the session row, a
/// spec that names grok.exe while the pool hands back a warm Claude, a brief that spills to a file
/// the launched process never hears about, a settlement that reads the right rows and prices them
/// off the wrong ladder.</para>
///
/// <para><b>What is real here.</b> The script is the real <c>delegate.ps1</c> over real HTTP; the
/// request it posts is deserialized and handed to the real <see cref="AgentTaskService"/>; the
/// dispatch is <see cref="AgentTaskDispatcher.TickAsync"/>; the launch goes through
/// <see cref="AgentSessionLaunchQueue"/> into <see cref="AgentSessionService"/> and really starts
/// fakegrok.exe on a real ConPTY; the brief travels the <see cref="SessionMessageQueueService"/>
/// delivery path; the transcript is read by the real <c>GrokTranscriptTailer</c> and
/// <c>GrokTranscriptNormalizer</c> inside the runner; and settlement fires by itself, the way
/// production fires it, out of <see cref="AgentSessionRuntime.SyncTranscriptAsync"/>.</para>
///
/// <para><b>What is not.</b> Two seams, both named where they are used. The API ENDPOINT is a
/// three-line relay (<see cref="DelegateTaskApiRelay"/>) rather than the mapped route — everything
/// from the request DTO inward is the shipped code, and the route's only other job, caller
/// resolution, is mirrored exactly. And <see cref="SessionRunnerEventPump"/> — the hosted service
/// that feeds runner events to the server — is replaced by a loop over the runtime's own
/// <c>SyncTranscriptAsync</c>, which is the same persistence path that service's reconnect
/// catch-up takes.</para>
/// </summary>
[Category("Integration")]
[NotInParallel(["Headed", "AgentQueue"])]
[ParallelLimiter<ProcessSpawnLimit>]
public class GrokDelegateEndToEndTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static string FakeGrokExe =>
        Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.exe");

    private static string FakeClaudeExe =>
        Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    /// <summary>
    /// The backend this deployment runs, and the one that makes the test mean something: on the
    /// inbox conhost every brief already spills at 900 bytes, so "the Grok brief spilled" would pass
    /// with S1 reverted. The modern ceiling is 43 200 bytes — room to type this whole brief — so the
    /// spill can only come from the kind.
    /// </summary>
    private const string ModernBackend = "modern";

    /// <summary>
    /// A brief whose meaning lives in its line breaks: a command, a path, and a tail marker that a
    /// composer joining every line would fuse into the following word.
    /// </summary>
    private const string MultilineGoal =
        "HEAD-MARKER prove the Grok delegate path end to end.\n"
        + "run: dotnet run --project tests/Antiphon.Tests --treenode-filter \"/*/*/GrokDelegateEndToEndTests/*\"\n"
        + "read: docs/superpowers/plans/2026-08-18-grok-delegate-kind-card-0084.md\n"
        + "Report what the launched process actually received.\n"
        + "TAIL-MARKER";

    // ---- the capstone --------------------------------------------------------------------------

    [Test]
    public async Task a_Kind_Grok_worker_runs_from_the_delegate_script_to_a_grok_priced_settlement()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeGrokExe))
            throw new SkipTestException($"fakegrok.exe not staged at {FakeGrokExe} — build the solution first");

        using var workspace = new TempWorkspace();
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-e2e-grok-home-{Guid.NewGuid():N}");
        await using var harness = BuildHarness(workspace.Path, grokHome, grokExe: FakeGrokExe, configure: d =>
            // fakegrok's agent_message_chunk carries no promptId (its own contract comment, and
            // GrokTranscriptNormalizer's, both say so) while REAL grok's does — so the AssistantText
            // it produces cannot share the TurnEnd's ApiCallId, and CARD-0046's identity gate would
            // hold every settlement here for the full grace before settling it as
            // "final message never arrived". That is a property of the fake's metadata, not of the
            // delegation path this test is about, so it takes the documented escape hatch: at 0 the
            // settle behaves exactly as it did before CARD-0046, report included.
            d.FinalMessageGraceSeconds = 0);

        // The ceilings the whole test hangs on. If the modern backend were unavailable and the
        // profile had fallen back, "it spilled" would stop being evidence of anything.
        harness.Provider.GetRequiredService<PtyDeliveryProfile>().Ceilings.Backend
            .ShouldBe(DeliveryBackend.ModernConPty, "the spill assertions below are only meaningful on the raised ceilings");

        Guid sessionId = Guid.Empty;
        try
        {
            // ---- 1. the caller's choice, made the way an agent makes it ----------------------
            using var relay = new DelegateTaskApiRelay(workspace.Path, harness.Delegation);
            var run = await DelegateScriptRunner.RunAsync(
                relay.BaseUrl,
                "-Role", "Code", "-Kind", "Grok", "-Title", "CARD-0084 S6", "-Goal", MultilineGoal);

            run.ExitCode.ShouldBe(0, $"{run.Output}\n{relay.LastFailure}");
            run.Output.ShouldContain("[Grok]", customMessage:
                "the script echoes the RESOLVED kind — a caller who asked for Grok must be told they got it");

            await using var created = CreateContext();
            var queued = await created.AgentTasks.AsNoTracking()
                .SingleAsync(t => t.Title == "CARD-0084 S6" && t.WorkingDirectory == workspace.Path);
            queued.AgentKind.ShouldBe(AgentKind.Grok, "the choice reached the stored row");
            queued.ModelLevel.ShouldBe(AgentModelLevel.Frontier, "the Code role's tier, unchanged by the kind");
            queued.Status.ShouldBe(AgentTaskStatus.Queued);
            queued.Goal.ShouldContain("\n", customMessage:
                "the brief must really be multi-line or the spill proves nothing");

            // ---- 2. dispatch, and the launch it queues ---------------------------------------
            using (var scope = harness.Provider.CreateScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
                await dispatcher.TickAsync(CancellationToken.None);
            }

            await using (var afterTick = CreateContext())
            {
                var dispatched = await afterTick.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == queued.Id);
                dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched, dispatched.FailureReason ?? "no failure reason");
                sessionId = dispatched.AgentSessionId.ShouldNotBeNull();
            }

            // The runner→server ingestion stand-in, started before the launch completes so the
            // transcript is being persisted while the delegate is answering.
            using var pump = new CancellationTokenSource();
            var pumping = PumpTranscriptAsync(harness.Provider, sessionId, pump.Token);

            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromMinutes(2), CancellationToken.None);

            // ---- 3. what was actually launched ------------------------------------------------
            var spec = harness.Runner.SpecFor(sessionId).ShouldNotBeNull(
                "the dispatch must have started a session on the runner");
            var args = spec.Args.ToList();

            spec.Exe.ShouldBe(FakeGrokExe, "resolved BY KIND — never the default definition");
            spec.DefinitionName.ShouldBe("grok");
            spec.Kind.ShouldBe(AgentKind.Grok);
            args.ShouldContain("--always-approve", customMessage: "the definition's own template survives");
            args.ShouldNotContain("--name", customMessage: "--name is Claude-only; grok.exe would refuse to start");
            args[args.IndexOf("--model") + 1].ShouldBe("grok-4.6", "Frontier on Grok");
            args.ShouldContain("--rules");
            args.ShouldNotContain("--append-system-prompt", customMessage:
                "Grok's system-prompt channel is --rules; the bundle would be dropped in silence");
            args[args.IndexOf("--rules") + 1]
                .ShouldContain(InstructionBundles.TextOf(InstructionBundles.DelegateBasics));
            spec.Env.ShouldContainKey("ANTIPHON_TASK_ID");

            // Ground truth from the OTHER end of the pty: fakegrok writes summary.json out of the
            // arguments it really parsed, so this is the launched process agreeing about its own
            // model and session identity rather than the spec being read back to itself.
            var summary = JsonDocument.Parse(
                await File.ReadAllTextAsync(SessionFile(grokHome, workspace.Path, sessionId, "summary.json")));
            summary.RootElement.GetProperty("current_model_id").GetString().ShouldBe("grok-4.6");
            summary.RootElement.GetProperty("info").GetProperty("id").GetString()
                .ShouldBe(sessionId.ToString("D"), "the launch's --session-id is what the tailer follows");

            await using (var live = CreateContext())
            {
                var session = await live.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
                session.Status.ShouldBe(SessionStatus.Running);
                session.DefinitionName.ShouldBe("grok");
                session.AgentKind.ShouldBe(AgentKind.Grok, "what every downstream reader keys on");

                var agent = await live.Agents.AsNoTracking()
                    .SingleAsync(a => a.PersistentSessionId == sessionId.ToString("D"));
                agent.Kind.ShouldBe(AgentKind.Grok, "the pool row a later Grok task claims on");
            }

            // ---- 4. the brief: spilled, and its pointer join-proof ----------------------------
            var spill = Path.Combine(
                workspace.Path, ".antiphon", $"task-{DelegationReportFormatter.Short(queued.Id)}-brief.md");
            File.Exists(spill).ShouldBeTrue(
                $"a Grok brief must travel by file even under the modern ceiling — expected {spill}");
            var spilled = await File.ReadAllTextAsync(spill);
            spilled.ShouldContain("HEAD-MARKER");
            spilled.ShouldContain("run: dotnet run --project tests/Antiphon.Tests");
            spilled.ShouldContain("TAIL-MARKER");
            spilled.ShouldContain("\n", customMessage: "the file is where the structure survives");

            var marker = DelegationReportFormatter.TaskMarker(queued.Id);
            await using (var delivered = CreateContext())
            {
                var message = await delivered.SessionQueuedMessages.AsNoTracking()
                    .SingleAsync(m => m.AgentSessionId == sessionId);
                message.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
                message.Body.ShouldNotContain("\n", customMessage:
                    "the pointer is rendered already-joined, so Grok's newline drop is a no-op on it");
                message.Body.ShouldNotContain("TAIL-MARKER", customMessage: "the body itself is never typed");
                message.Status.ShouldBe(QueuedMessageStatus.Sent, await ScreenAsync(harness, sessionId));
            }

            // ---- 5. what the delegate received, as ITS transcript records it ------------------
            await WaitUntilAsync(
                async () =>
                {
                    await using var db = CreateContext();
                    return await db.TranscriptEntries.CountAsync(t =>
                        t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd) > 0;
                },
                TimeSpan.FromSeconds(60),
                async () => $"no TurnEnd row from the real Grok tailer. Screen:\n{await ScreenAsync(harness, sessionId)}");

            await using (var db = CreateContext())
            {
                var prompt = await db.TranscriptEntries.AsNoTracking()
                    .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt)
                    .OrderBy(t => t.Sequence)
                    .FirstAsync();
                var recorded = prompt.Text ?? string.Empty;

                recorded.ShouldContain(
                    $"task-{DelegationReportFormatter.Short(queued.Id)}-brief.md' Everything you need is there",
                    customMessage: "the joined pointer must not fuse the spill path into the next line");
                recorded.ShouldContain(marker, customMessage: "and settlement correlates on this marker");
                recorded.TrimEnd().ShouldEndWith(marker);
            }

            // ---- 6. settlement and price, both arriving on their own -------------------------
            // Nothing in this test calls OnTurnEndAsync: the turn boundary the pump persists fires
            // the same flush production fires, which settles the task.
            await WaitUntilAsync(
                async () =>
                {
                    await using var db = CreateContext();
                    return await db.AgentTasks.AsNoTracking()
                        .AnyAsync(t => t.Id == queued.Id && t.Status != AgentTaskStatus.Dispatched);
                },
                TimeSpan.FromSeconds(60),
                async () => $"the delegate's finished turn never settled the task. Screen:\n{await ScreenAsync(harness, sessionId)}");

            pump.Cancel();
            await pumping;

            await using var verify = CreateContext();
            var settled = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == queued.Id);
            settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
            settled.Result.ShouldNotBeNull().ShouldContain("FAKE response to:", customMessage:
                "the report is the delegate's own turn-ending text");
            settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
            settled.AgentKind.ShouldBe(AgentKind.Grok);

            // The usage counters are fakegrok's turn_completed.usage, read by the real normalizer
            // into the same four columns Claude's usage lands in — which is why S5 only had to
            // widen the PRICE, not the rollup.
            settled.TokensIn.ShouldBe(1);
            settled.TokensOut.ShouldBe(1);
            settled.CostPricingVersion.ShouldBe(DelegationCost.PricingVersion);

            var spend = new TokenSpend(
                settled.TokensIn, settled.CacheReadTokens, settled.CacheCreationTokens, settled.TokensOut);
            var pricing = harness.Delegation.Pricing;
            var atGrokRates = DelegationCost.Estimate(
                pricing, settled.ModelLevel, spend, settled.CompletedAt!.Value, AgentKind.Grok);
            var atClaudeRates = DelegationCost.Estimate(
                pricing, settled.ModelLevel, spend, settled.CompletedAt!.Value);

            // xAI's published grok-4.6 list: 1 x $2.00 in + 1 x $6.00 out, per million.
            atGrokRates.ShouldBe(0.000008m);
            // The Claude rung Frontier shares — fable at $10/$50 — is what this row would have cost
            // before S5, and what it must NOT cost now.
            atClaudeRates.ShouldBe(0.000060m);
            settled.CostUsd.ShouldBe(atGrokRates, "a Grok delegate is priced on Grok's ladder");
            settled.CostUsd.ShouldNotBe(atClaudeRates);
        }
        finally
        {
            await CleanupAsync(harness, sessionId, workspace.Path, grokHome);
        }
    }

    // ---- the Claude control --------------------------------------------------------------------

    /// <summary>
    /// The same journey with the flag omitted, which is what every existing caller does. Its value
    /// is entirely in being run through the SAME harness: a kind branch that broke Claude's launch,
    /// its inline brief or its price would show up here and nowhere else in this file.
    ///
    /// <para>Its turn is SEEDED rather than driven through fakeclaude, and deliberately: the Claude
    /// transcript tailer follows <c>~/.claude/projects</c> of the machine running the tests, so a
    /// runner-tailed Claude session would read a stranger's conversation (CARD-0006). The launch is
    /// real; the rows the settle path reads are the measured Claude shape, seeded the way
    /// <see cref="AgentTaskReplyIntegrationTests"/> seeds them, and carry the same one-in/one-out
    /// counters as the Grok run above so the two prices are directly comparable.</para>
    /// </summary>
    [Test]
    public async Task a_ClaudeCode_worker_still_launches_claude_types_its_brief_inline_and_prices_on_the_claude_ladder()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe))
            throw new SkipTestException($"fakeclaude.exe not staged at {FakeClaudeExe} — build the solution first");

        using var workspace = new TempWorkspace();
        var grokHome = Path.Combine(Path.GetTempPath(), $"antiphon-e2e-claude-home-{Guid.NewGuid():N}");
        await using var harness = BuildHarness(
            workspace.Path, grokHome, grokExe: FakeGrokExe, claudeExe: FakeClaudeExe);

        Guid sessionId = Guid.Empty;
        try
        {
            using var relay = new DelegateTaskApiRelay(workspace.Path, harness.Delegation);
            var run = await DelegateScriptRunner.RunAsync(
                relay.BaseUrl, "-Role", "Code", "-Title", "CARD-0084 S6 control", "-Goal", MultilineGoal);

            run.ExitCode.ShouldBe(0, $"{run.Output}\n{relay.LastFailure}");
            run.Output.ShouldContain("queued task");
            run.Output.ShouldNotContain("[ClaudeCode]", customMessage: "the default kind is not news to the caller");
            run.Output.ShouldNotContain("[Grok]");

            await using var created = CreateContext();
            var queued = await created.AgentTasks.AsNoTracking()
                .SingleAsync(t => t.Title == "CARD-0084 S6 control" && t.WorkingDirectory == workspace.Path);
            queued.AgentKind.ShouldBe(AgentKind.ClaudeCode, "an omitted -Kind resolves exactly as before");

            using (var scope = harness.Provider.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

            await using (var afterTick = CreateContext())
            {
                var dispatched = await afterTick.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == queued.Id);
                dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched, dispatched.FailureReason ?? "no failure reason");
                sessionId = dispatched.AgentSessionId.ShouldNotBeNull();
            }

            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromMinutes(2), CancellationToken.None);

            var spec = harness.Runner.SpecFor(sessionId).ShouldNotBeNull();
            var args = spec.Args.ToList();
            spec.DefinitionName.ShouldBe("claude");
            spec.Kind.ShouldBe(AgentKind.ClaudeCode);
            var name = args.IndexOf("--name");
            name.ShouldBeGreaterThanOrEqualTo(0, "--name is how a Claude delegate is identified on its command line");
            args[name + 1].ShouldBe($"task-{DelegationReportFormatter.Short(queued.Id)}");
            args[args.IndexOf("--model") + 1].ShouldBe("fable", "Frontier on Claude");
            args.ShouldContain("--append-system-prompt");
            args.ShouldNotContain("--rules", customMessage: "--rules is Grok's channel and Claude has no such flag");

            await using (var live = CreateContext())
            {
                var session = await live.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
                session.AgentKind.ShouldBe(AgentKind.ClaudeCode);
                session.DefinitionName.ShouldBe("claude");
            }

            // The contrast S1 exists for, on the SAME backend and the same brief: Claude's composer
            // keeps the line breaks, so the whole brief is typed and no file is written.
            var spill = Path.Combine(
                workspace.Path, ".antiphon", $"task-{DelegationReportFormatter.Short(queued.Id)}-brief.md");
            File.Exists(spill).ShouldBeFalse("a Claude brief of this size is typed inline; nothing spills");

            string body;
            await using (var delivered = CreateContext())
            {
                var message = await delivered.SessionQueuedMessages.AsNoTracking()
                    .SingleAsync(m => m.AgentSessionId == sessionId);
                body = message.Body;
                body.ShouldContain("TAIL-MARKER", customMessage: "the whole brief was typed, not a pointer to it");
                body.ShouldContain("\n", customMessage: "and it kept its line breaks");
            }

            // Settlement over the measured Claude turn shape: the report and the boundary share an
            // ApiCallId, so this side takes CARD-0046's normal Landed path with the shipped grace.
            var apiCallId = $"msg_{Guid.NewGuid():N}";
            await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, body, null);
            await SeedEntryAsync(
                sessionId,
                TranscriptKinds.AssistantText,
                "Done. 3 passed, 0 failed.\n" + DelegationReportFormatter.ReportToken(queued.Id, "done"),
                apiCallId);
            await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, apiCallId, inputTokens: 1, outputTokens: 1);

            await harness.Provider.GetRequiredService<AgentTaskReplyService>()
                .OnTurnEndAsync(sessionId, CancellationToken.None);

            await using var verify = CreateContext();
            var settled = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == queued.Id);
            settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
            settled.Result.ShouldNotBeNull().ShouldContain("3 passed", customMessage:
                "the report is the seeded turn-ending text");
            settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
            settled.TokensIn.ShouldBe(1);
            settled.TokensOut.ShouldBe(1);

            var spend = new TokenSpend(
                settled.TokensIn, settled.CacheReadTokens, settled.CacheCreationTokens, settled.TokensOut);
            var pricing = harness.Delegation.Pricing;
            settled.CostUsd.ShouldBe(
                DelegationCost.Estimate(pricing, settled.ModelLevel, spend, settled.CompletedAt!.Value),
                "the kind-free figure — the overlay must not move a Claude row by a cent");
            settled.CostUsd.ShouldBe(0.000060m);
            settled.CostUsd.ShouldBeGreaterThan(
                DelegationCost.Estimate(pricing, settled.ModelLevel, spend, settled.CompletedAt!.Value, AgentKind.Grok),
                "the same tokens on the same tier cost more on Claude's ladder — which is the whole point of S5");
        }
        finally
        {
            await CleanupAsync(harness, sessionId, workspace.Path, grokHome);
        }
    }

    // ---- harness -------------------------------------------------------------------------------

    private static Harness BuildHarness(
        string workspacePath,
        string grokHome,
        string grokExe,
        string? claudeExe = null,
        Action<DelegationSettings>? configure = null)
    {
        var sessionLogPath = Path.Combine(Path.GetTempPath(), $"antiphon-e2e-runner-{Guid.NewGuid():N}");
        var runner = new RecordingRunnerClient(new DirectSessionRunnerClient(sessionLogPath, ptyBackend: ModernBackend));

        var delegation = new DelegationSettings
        {
            // The fixture database is shared: leftover Dispatched rows from other suites must never
            // eat this harness's dispatch budget (CLAUDE.md — never assert on a global count).
            MaxConcurrentTasks = 512,
            AllowedRoots = [],
            PoolReservedForCallerMinutes = 0,
            PoolIdleRetireMinutes = 600,
            // A delegate that reports in seconds must not be swept as stalled mid-test.
            SubagentGraceMinutes = 0,
            CheckEnabled = false,
        };
        configure?.Invoke(delegation);

        var services = new ServiceCollection();
        // Warning-and-up to the console, which TUnit captures per test: OnTurnEndAsync swallows any
        // settlement exception as a Warning, so with no sink a settle that threw is indistinguishable
        // from one that never fired - both tests here sat red for four days reading "never settled"
        // with the real cause (a missing DI registration) logged into nothing (CARD-0230).
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        services.AddSingleton<IEventBus, MockEventBus>();
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
        services.AddSingleton(Options.Create(delegation));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition
            {
                Kind = "ClaudeCode",
                // Unresolvable unless a test asks for a real one: the Grok capstone must not be able
                // to start a Claude by accident, and a launch that tried would fail loudly.
                Exe = claudeExe ?? "claude-not-configured-for-this-test",
            };
            s.Definitions["grok"] = new AgentDefinition
            {
                Kind = "Grok",
                Exe = grokExe,
                ArgsTemplate = ["--always-approve", "--no-alt-screen"],
                // Where fakegrok writes its session files, and — the same value, read off the launch
                // env — where the runner's GrokTranscriptTailer looks for updates.jsonl.
                Env = new Dictionary<string, string>
                {
                    ["GROK_HOME"] = grokHome,
                    ["ANTIPHON_FAKE_REPORT_LINE"] = "1",
                },
            };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<ISessionRunnerClient>(runner);
        services.AddSingleton<IAgentProtocolAdapterFactory, AgentProtocolAdapterFactory>();
        services.AddSingleton<IWorkspaceHookRunner, Antiphon.Server.Infrastructure.WorkspaceHooks.WorkspaceHookRunner>();
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        // Settlement resolves the report's deliverable pointer through GitWorkspaceService since
        // c4d7e0d (2026-08-26); without it SettleAsync throws before SaveChanges and the task stays
        // Dispatched forever (CARD-0230). The helper is the one registration (CARD-0297).
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-e2e-wt-{Guid.NewGuid():N}"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<AgentTaskDispatcher>();
        // The deployment's own backend, stated rather than inherited: the brief ceilings are
        // conditional on it (CARD-0037) and this whole file turns on which ones are in force.
        services.AddSingleton(sp => new PtyDeliveryProfile(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ILogger<PtyDeliveryProfile>>(),
            sp.GetRequiredService<IOptions<DelegationSettings>>(),
            TimeProvider.System,
            backendOverride: ModernBackend));

        var provider = services.BuildServiceProvider();
        return new Harness(
            provider,
            runner,
            provider.GetRequiredService<AgentSessionLaunchQueue>(),
            delegation,
            workspacePath);
    }

    private sealed record Harness(
        ServiceProvider Provider,
        RecordingRunnerClient Runner,
        AgentSessionLaunchQueue LaunchQueue,
        DelegationSettings Delegation,
        string WorkspacePath) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Runner.DisposeAsync();
            await Provider.DisposeAsync();
        }
    }

    /// <summary>
    /// Stands in for <c>SessionRunnerEventPump</c> by driving the runtime's OWN catch-up, which is
    /// the same persistence the pump's reconnect path uses — and, because it is the real thing,
    /// carries the turn-boundary flush that settles the task with nothing in the test calling it.
    /// </summary>
    private static async Task PumpTranscriptAsync(IServiceProvider provider, Guid sessionId, CancellationToken ct)
    {
        var runtime = provider.GetRequiredService<AgentSessionRuntime>();
        while (!ct.IsCancellationRequested)
        {
            try { await runtime.SyncTranscriptAsync(sessionId, CancellationToken.None); }
            catch (Exception) { /* the session may not be live yet, or already gone */ }

            try { await Task.Delay(150, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition, TimeSpan timeout, Func<Task<string>> failure)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(250);
        }

        throw new ShouldAssertException(await failure());
    }

    private static async Task<string> ScreenAsync(Harness harness, Guid sessionId)
    {
        try
        {
            var snapshot = await harness.Runner.GetSnapshotAsync(sessionId, CancellationToken.None);
            return snapshot.RawOutput ?? string.Empty;
        }
        catch (Exception ex)
        {
            return $"(no screen: {ex.Message})";
        }
    }

    /// <summary>Where fakegrok — and the tailer — agree a session's files live.</summary>
    private static string SessionFile(string grokHome, string cwd, Guid sessionId, string name) =>
        Path.Combine(
            grokHome, "sessions", Uri.EscapeDataString(Path.GetFullPath(cwd)), sessionId.ToString("D"), name);

    private static async Task SeedEntryAsync(
        Guid sessionId, string kind, string? text, string? apiCallId,
        int? inputTokens = null, int? outputTokens = null)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq + 1,
            Kind = kind,
            Text = text,
            ApiCallId = apiCallId,
            StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Timestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task CleanupAsync(Harness harness, Guid sessionId, string workspacePath, string grokHome)
    {
        if (sessionId != Guid.Empty)
        {
            try { await harness.Runner.KillAsync(sessionId, CancellationToken.None); }
            catch (Exception) { /* best effort */ }

            await using var db = CreateContext();
            await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => t.AgentSessionId == sessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AgentSessionId, (Guid?)null));
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
        }

        try { Directory.Delete(grokHome, recursive: true); } catch (Exception) { /* best effort */ }
        _ = workspacePath;
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>A real directory on disk — the workspace resolver verifies existence.</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-grok-e2e").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a delegate's stray file lock must not fail the test */ }
        }
    }
}

/// <summary>
/// The launch spec, kept, for every session the runner is asked to start — the argv the delegate
/// process really received, observed at the last seam before the OS rather than recomposed from the
/// dispatcher afterwards. Everything else forwards verbatim to a real
/// <see cref="DirectSessionRunnerClient"/>.
/// </summary>
internal sealed class RecordingRunnerClient : ISessionRunnerClient, IAsyncDisposable
{
    private readonly DirectSessionRunnerClient _inner;
    private readonly Dictionary<Guid, AgentLaunchSpec> _specs = [];
    private readonly object _gate = new();

    public RecordingRunnerClient(DirectSessionRunnerClient inner) => _inner = inner;

    public AgentLaunchSpec? SpecFor(Guid sessionId)
    {
        lock (_gate)
            return _specs.TryGetValue(sessionId, out var spec) ? spec : null;
    }

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
    {
        lock (_gate)
            _specs[sessionId] = spec;
        return _inner.StartAsync(sessionId, spec, ct);
    }

    public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
        // Through the interface: the in-proc client does not override this default member, and its
        // "cannot say" answer is what leaves PtyDeliveryProfile's local decision standing.
        ((ISessionRunnerClient)_inner).GetCapabilitiesAsync(ct);

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => _inner.ListAsync(ct);

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        _inner.GetAsync(sessionId, ct);

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        _inner.GetBufferAsync(sessionId, ct);

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
        _inner.GetSnapshotAsync(sessionId, ct);

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
        _inner.GetTranscriptAsync(sessionId, ct);

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        _inner.SendInputAsync(sessionId, input, ct);

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
        _inner.ClearLiveBufferAsync(sessionId, ct);

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        _inner.ResizeAsync(sessionId, cols, rows, ct);

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        _inner.KillAsync(sessionId, ct);

    public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
        _inner.StreamEventsAsync(ct);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>
/// <c>POST /api/agent-tasks</c> over real HTTP, answered by the real
/// <see cref="AgentTaskService.CreateAsync"/>.
///
/// <para>An <see cref="HttpListener"/> rather than a test host because the point is to run
/// <c>delegate.ps1</c> exactly as an agent runs it — and because booting the whole application would
/// bring its hosted dispatcher with it, which would race this test for its own task.</para>
///
/// <para>The one thing this relay decides for itself is the CALLER, and it decides it the way the
/// mapped route does for a request with no task token: no parent, no reply routing, the caller's
/// working directory (<c>AgentTaskEndpoints.ResolveCallerAsync</c>). Everything after that —
/// validation, the role policy, the kind allowlist, tier resolution, the stored row and its events —
/// is the shipped service.</para>
/// </summary>
internal sealed class DelegateTaskApiRelay : IDisposable
{
    // Mirrors the application's own configuration (Program.cs: web defaults + string enums), so a
    // body the real endpoint would reject cannot quietly succeed here.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;
    private readonly string _callerDirectory;
    private readonly DelegationSettings _settings;

    public DelegateTaskApiRelay(string callerDirectory, DelegationSettings settings)
    {
        _callerDirectory = callerDirectory;
        _settings = settings;
        BaseUrl = $"http://localhost:{FreePort()}/";
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _pump = Task.Run(PumpAsync);
    }

    public string BaseUrl { get; }

    /// <summary>Whatever the service threw, so a failed script run can say why rather than "exit 1".</summary>
    public Exception? LastFailure { get; private set; }

    private async Task PumpAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception) { return; /* stopped */ }

            try
            {
                await HandleAsync(context);
            }
            catch (Exception ex)
            {
                LastFailure = ex;
                var problem = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new { detail = ex.Message }, Json));
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                try
                {
                    await context.Response.OutputStream.WriteAsync(problem);
                    context.Response.Close();
                }
                catch (Exception) { /* the client may already be gone */ }
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();
        var request = JsonSerializer.Deserialize<CreateAgentTaskRequest>(raw, Json)
            ?? throw new InvalidOperationException($"delegate.ps1 posted a body that is not a create request: {raw}");

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var service = new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(_settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance);

        var created = await service.CreateAsync(
            request,
            new AgentTaskService.Caller(null, null, _callerDirectory),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToUtf8Bytes(created, Json);
        context.Response.StatusCode = 201;
        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(payload);
        context.Response.Close();
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch (Exception) { /* already stopped */ }
        try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { /* pump is best-effort */ }
        _listener.Close();
        _cts.Dispose();
    }
}
