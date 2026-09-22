using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0501 review F2 / re-review F2 — the handoff nothing owned: a check note PRODUCED by the
/// real <see cref="AgentTaskCheckService"/> must arrive as one complete, correlated
/// <c>UserPrompt</c>, at BOTH ends of the chain.
///
/// <para>The existing coverage stops one step short on each side of this seam.
/// <c>AgentTaskCheckInterpreterTests</c> runs real production but reads the result back off the
/// QUEUE ROW (<c>NotesToCallerAsync</c>) — its caller session has no adapter and its interpretation
/// task is settled by hand, so nothing is ever dispatched and nothing is ever typed.
/// <c>SessionMessageQueueWedgedHeadTests</c> and its siblings run real delivery and recovery but
/// hand-seed the queue row. CARD-0501's live failure lived exactly between them: real notes were
/// produced for sessions whose queue head could not move, and no test could have noticed, because
/// no test ever carried one note from production to a prompt.</para>
///
/// <para>The chain has TWO delivery legs and both are exercised here against real producers:
/// <list type="number">
/// <item>the INTERPRETER leg — <see cref="SpecialistTaskRunner"/> creates the interpretation task
/// and the real <see cref="AgentTaskDispatcher"/> places it on the standing interpreter's live
/// session, which enqueues an execution brief there. That brief is the row shape that stranded in
/// the live incident, so the assertion is the interpreter's own complete correlated
/// <c>UserPrompt</c>, taken AFTER its attempt floor: the first submit is swallowed and the
/// Enter-only recovery is what finishes it;</item>
/// <item>the RECIPIENT leg — the note the interpretation produced reaching the caller session,
/// for a recipient that is BUSY at production time (the note waits for a flush) and one already
/// ELIGIBLE (the enqueue delivers inline), each finished by the same recovery.</item>
/// </list>
/// Both persistence failures on the way are covered too: the interpretation task that cannot be
/// written (the interpreter leg degrades and the note still ships whole) and the queue row that
/// cannot be written (the recipient leg reports DeliveryFailed, leaves nothing partial, and a later
/// run delivers).</para>
///
/// <para>The one thing not driven by production is the interpreter AGENT: no real Claude reads the
/// brief, so its task row is settled directly with a reading. That boundary is named in the H-x
/// inventory in <c>docs/investigations/2026-09-15-card-0501-code-verification.md</c>.</para>
/// </summary>
[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
[ParallelLimiter<ProcessSpawnLimit>]
public partial class CheckNoteDeliveryHandoffTests
{
    private const string Reading =
        "On track — three commits in the last 6 minutes; tests ran green (no action needed).";

    // ---- harness ------------------------------------------------------------------------------

    /// <summary>
    /// One isolated schema, one recipient session with an adapter (the BridgeQueueHarness one), and
    /// — when <c>withInterpreter</c> — the real check-interpreter graph plus a real dispatcher, so
    /// an interpretation is produced, dispatched and delivered rather than imagined.
    /// </summary>
    private sealed class Handoff : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly string _scratch;

        private Handoff(IsolatedTestSchema schema, BridgeQueueHarness harness, string scratch, string slug)
        {
            _schema = schema;
            _scratch = scratch;
            H = harness;
            Slug = slug;
        }

        public BridgeQueueHarness H { get; }
        public string Slug { get; }
        public Guid SessionId => H.SessionId;
        public string ConnectionString => _schema.ConnectionString;
        public FakeAgentProtocolAdapter Adapter => H.Adapter;
        public Guid InterpreterAgentId { get; private set; }
        public Guid InterpreterSessionId { get; private set; }
        public FakeAgentProtocolAdapter InterpreterAdapter { get; private set; } = new();

        public static async Task<Handoff> CreateAsync(bool withInterpreter,
            Action<DbContextOptionsBuilder>? configureDbContext = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var scratch = Directory.CreateTempSubdirectory("antiphon-c501-handoff").FullName;
            var slug = $"c501-interp-{Guid.NewGuid():N}"[..24];
            try
            {
                var delegation = new DelegationSettings
                {
                    // The schema is this test's own, but the dispatcher tick and the sweeps are
                    // still global over it; keep every budget and janitor out of the way.
                    MaxConcurrentTasks = 512,
                    RolePolicy = new(StringComparer.OrdinalIgnoreCase),
                    FinalMessageGraceSeconds = 0,
                    SubagentGraceMinutes = 0,
                    PoolIdleRetireMinutes = 525_600,
                    PoolMaxIdlePerDirectory = int.MaxValue,
                    CheckInterpreterEnabled = withInterpreter,
                    CheckInterpreterAgentSlug = slug,
                    CheckInterpreterWorkingDirectory = scratch,
                    CheckInterpreterWaitSeconds = 60,
                };
                var harness = await BridgeQueueHarness.CreateAsync(new()
                {
                    ConnectionString = schema.ConnectionString,
                    Delegation = delegation,
                    ConfigureDbContext = configureDbContext,
                    // A failed first delivery must not kill the recipient out from under the test;
                    // the subject here is the note's journey, not the always-on recovery rule.
                    AlwaysOn = false,
                    ConfigureDeliveryVerification = v => v.PostFailureConfirmGraceSeconds = 0,
                    ConfigureServices = services =>
                    {
                        services.AddScoped<DelegateCheckProbe>();
                        services.AddScoped<AgentTaskCheckService>();
                        if (!withInterpreter)
                            return;
                        // Built by hand, without AgentControlService: the provisioner's start is
                        // best-effort, and this harness attaches the live session itself rather
                        // than launching a real one.
                        services.AddScoped(sp => new CheckInterpreterProvisioner(
                            sp.GetRequiredService<AppDbContext>(),
                            sp.GetRequiredService<IOptions<DelegationSettings>>(),
                            sp.GetRequiredService<TimeProvider>(),
                            sp.GetRequiredService<ILogger<CheckInterpreterProvisioner>>()));
                        services.AddScoped<AgentTaskService>();
                        services.AddSingleton<AgentTaskCheckQueue>();
                        services.AddSingleton<AgentTaskReplyService>();
                        services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
                        services.AddSingleton<DelegationWorkspaceResolver>();
                        // TryAdd throughout, so the harness's own fake IWorktreeManager survives;
                        // nothing here reaches git, because the interpretation task is Shared.
                        services.AddDelegationWorktreeGraph(new GitSettings
                        {
                            WorktreeBasePath = Path.Combine(scratch, "worktrees"),
                        });
                        services.AddScoped<AgentTaskDispatcher>();
                    },
                });
                return new Handoff(schema, harness, scratch, slug);
            }
            catch
            {
                await schema.DisposeAsync();
                try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
                throw;
            }
        }

        public AppDbContext CreateContext() =>
            new(TestDbFixture.CreateDbContextOptions(_schema.ConnectionString));

        public T Resolve<T>() where T : notnull =>
            H.Provider.CreateScope().ServiceProvider.GetRequiredService<T>();

        /// <summary>
        /// The REAL provisioner creates the standing interpreter; this only gives it the live
        /// session a supervised specialist would already have, and an adapter to type into. AlwaysOn
        /// is dropped afterwards for the same reason the recipient drops it: a swallowed first
        /// submit must not kill the session whose recovery is the subject.
        /// </summary>
        public async Task EnsureInterpreterAsync()
        {
            var agent = await Resolve<CheckInterpreterProvisioner>().EnsureAsync(CancellationToken.None);
            agent.ShouldNotBeNull();
            InterpreterAgentId = agent.Id;
            InterpreterSessionId = Guid.NewGuid();

            var now = DateTime.UtcNow;
            await using (var db = CreateContext())
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = InterpreterSessionId,
                    DefinitionName = "fake",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running,
                    Cwd = agent.WorkingDirectory,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = now,
                    StartedAt = now,
                    LastSeenAt = now,
                });
                await db.SaveChangesAsync();
                await db.Agents.Where(a => a.Id == agent.Id).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.PersistentSessionId, InterpreterSessionId.ToString("D"))
                    .SetProperty(a => a.AlwaysOn, false));
            }

            InterpreterAdapter = new FakeAgentProtocolAdapter();
            var sid = InterpreterSessionId;
            var store = _schema.ConnectionString;
            // A standing interpreter has taken turns before. Without one ended turn on record its
            // transcript baseline is unobservable, delivery falls back to SCREEN evidence, and a
            // swallowed submit would be reported Delivered with nothing in the transcript at all -
            // which is the one thing this class exists to refuse to accept as proof.
            await BridgeQueueHarness.InsertEntryAsync(sid, TranscriptKinds.TurnEnd,
                stopReason: "end_turn", connectionString: store);
            InterpreterAdapter.OnSubmitted = async submitted =>
            {
                await BridgeQueueHarness.InsertEntryAsync(sid, TranscriptKinds.UserPrompt, submitted,
                    timestamp: DateTime.UtcNow, connectionString: store);
                await BridgeQueueHarness.InsertEntryAsync(sid, TranscriptKinds.TurnEnd,
                    stopReason: "end_turn", connectionString: store);
            };
            H.Runtime.Register(InterpreterSessionId, InterpreterAdapter);
        }

        public async Task<List<string>> PromptsAsync(Guid sessionId)
        {
            await using var db = CreateContext();
            return await db.TranscriptEntries.AsNoTracking()
                .Where(e => e.AgentSessionId == sessionId && e.Kind == TranscriptKinds.UserPrompt)
                .OrderBy(e => e.Sequence)
                .Select(e => e.Text!)
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await H.DisposeAsync();
            await _schema.DisposeAsync();
            try { Directory.Delete(_scratch, recursive: true); } catch (IOException) { }
        }
    }

    // ---- seeding ------------------------------------------------------------------------------

    /// <summary>A Dispatched delegate task whose caller is the harness's live recipient session.</summary>
    private static async Task<Guid> SeedCheckedDelegateAsync(Handoff h)
    {
        var delegateSessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var dispatched = DateTime.UtcNow.AddMinutes(-11);

        await using var db = h.CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = delegateSessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = DateTime.UtcNow,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            ParentSessionId = h.SessionId,
            ReplyTo = AgentTaskReplyTo.Session,
            Title = "CARD-0501 F2 checked delegate",
            Goal = "do the checked thing",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            AgentSessionId = delegateSessionId,
            Status = AgentTaskStatus.Dispatched,
            ExpectedDurationMinutes = 10,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
        });
        await db.SaveChangesAsync();
        // The real dispatcher tick runs while the check waits, and a Dispatched task whose session
        // is neither LIVE nor visibly working is exactly what dead-session reconciliation and the
        // no-report sweeps settle - which would make every case a supersession instead of a
        // delivery. The subject here is the note's journey, so the delegate is alive and mid-turn
        // for the length of the test, which is also the only state a check is ever taken in.
        h.H.Runtime.Register(delegateSessionId, new FakeAgentProtocolAdapter());
        await BridgeQueueHarness.InsertEntryAsync(delegateSessionId, TranscriptKinds.UserPrompt,
            "the delegate is working on it", timestamp: DateTime.UtcNow,
            connectionString: h.ConnectionString);
        return taskId;
    }

    private static async Task<SessionQueuedMessage> CheckRowAsync(Handoff h)
    {
        await using var db = h.CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == h.SessionId && m.Origin == QueuedMessageOrigin.Check)
            .SingleAsync();
    }

    /// <summary>The interpretation row SpecialistTaskRunner created, once it exists.</summary>
    private static async Task<AgentTask> WaitForInterpretationAsync(Handoff h)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            await using (var db = h.CreateContext())
            {
                var row = await db.AgentTasks.AsNoTracking()
                    .Where(t => t.AgentId == h.InterpreterAgentId && t.Role == AgentTaskRole.Check)
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefaultAsync();
                if (row is not null)
                    return row;
            }
            await Task.Delay(25);
        }

        throw new TimeoutException("The check never created an interpretation task.");
    }

    private static async Task SettleInterpretationAsync(Handoff h, Guid id, string reading)
    {
        await using var db = h.CreateContext();
        var row = await db.AgentTasks.SingleAsync(t => t.Id == id);
        row.Status = AgentTaskStatus.Succeeded;
        row.Result = reading;
        row.CostUsd = 0.0031m;
        row.CompletedAt = DateTime.UtcNow;
        row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync();
    }

    // ---- shared assertions --------------------------------------------------------------------

    /// <summary>
    /// The invariant every case ends on at the RECIPIENT: ONE prompt, whole, traceable to the task
    /// the check was about, and past its attempt floor (a first delivery was charged and failed).
    /// A containment match is not enough — a merged or truncated prompt would pass that.
    /// </summary>
    private static async Task AssertNoteArrivedWholeAsync(
        Handoff h, Guid taskId, string? carries = null, bool spilled = false)
    {
        var row = await CheckRowAsync(h);
        row.ConversationKey.ShouldBe(AgentTaskCheckService.ConversationKey(taskId));
        row.SourceTaskId.ShouldBe(taskId);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        row.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(1);

        var prompts = await h.PromptsAsync(h.SessionId);
        prompts.ShouldBe([row.Body], "exactly the produced note, once, and nothing riding with it");
        h.Adapter.SubmittedBodies.ShouldBe([row.Body]);

        // A note over the brief ceiling is written out and typed as a pointer (CARD-0025). That is
        // still "arrived whole" — but the text to read is then the FILE, and this is where it is
        // proved intact rather than assumed.
        var note = spilled ? await ReadSpilledNoteAsync(h, row.Body) : row.Body;
        note.ShouldStartWith($"[check {DelegationReportFormatter.Short(taskId)} ");
        if (carries is not null)
            note.ShouldContain(carries);
    }

    private static async Task<string> ReadSpilledNoteAsync(Handoff h, string pointer)
    {
        var inbox = Path.Combine(h.H.TempRoot, "workspace", ".antiphon", "inbox");
        var file = Directory.GetFiles(inbox, "*.md").ShouldHaveSingleItem();
        pointer.ShouldContain(Path.GetFileName(file),
            customMessage: "the pointer names the file it spilled to");
        return await File.ReadAllTextAsync(file);
    }

    /// <summary>
    /// The invariant the INTERPRETER leg ends on: the execution brief the dispatcher enqueued is
    /// one whole prompt in the interpreter's own transcript, correlated by the interpretation
    /// task's marker, and its row records the failed first attempt the recovery finished.
    /// </summary>
    private static async Task AssertBriefArrivedWholeAsync(Handoff h, Guid interpretationId)
    {
        await using var db = h.CreateContext();
        var brief = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == h.InterpreterSessionId)
            .SingleAsync();
        brief.Status.ShouldBe(QueuedMessageStatus.Sent);
        brief.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        brief.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(
            1, "the brief is asserted AFTER its attempt floor, not on a clean first submit");
        brief.Body.ShouldContain(DelegationReportFormatter.TaskMarker(interpretationId));

        // Exact, not containment - a merged or truncated prompt would pass containment. The outer
        // trim is the composer's own: a queue row keeps the trailing newline its producer wrote and
        // the terminal does not, and that difference is not what "whole" is about.
        var prompts = await h.PromptsAsync(h.InterpreterSessionId);
        prompts.Count.ShouldBe(1, "one whole brief, once, and nothing riding with it");
        prompts[0].ReplaceLineEndings("\n").Trim()
            .ShouldBe(brief.Body.ReplaceLineEndings("\n").Trim());
        h.InterpreterAdapter.SubmittedBodies.Count.ShouldBe(1);
    }

    /// <summary>
    /// Produce, dispatch, recover and settle one interpretation. Returns its task id.
    /// Runs the check on its own thread because <c>RunCheckAsync</c> polls until the
    /// interpretation settles — the dispatch and the recovery happen while it waits.
    /// </summary>
    private static async Task<(Task<AgentTaskCheckService.CheckOutcome> Run, Guid Interpretation)>
        StartCheckAndDeliverTheBriefAsync(Handoff h, Guid taskId)
    {
        var run = Task.Run(() => h.Resolve<AgentTaskCheckService>().RunCheckAsync(taskId, CancellationToken.None));

        // The interpreter's own first submit is swallowed: the brief stands in its composer
        // unsubmitted, which is exactly the shape the stranded Check briefs were found in.
        h.InterpreterAdapter.SwallowSubmits = 99;
        var interpretation = await WaitForInterpretationAsync(h);
        await h.Resolve<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

        await using (var db = h.CreateContext())
        {
            var placed = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == interpretation.Id);
            placed.Status.ShouldBe(AgentTaskStatus.Dispatched, "the real dispatcher placed it");
            placed.AgentSessionId.ShouldBe(h.InterpreterSessionId,
                "on the standing interpreter's LIVE session — no second session was launched");
        }

        // Recovery, not a re-type: the body is standing whole in this generation, so the sweep
        // Enters it. That is what finally puts the brief in the interpreter's transcript.
        h.InterpreterAdapter.SwallowSubmits = 0;
        var typedBefore = h.InterpreterAdapter.Inputs.Count(i => i != "\r");
        await h.H.Queue.FlushSessionAsync(h.InterpreterSessionId, CancellationToken.None);
        h.InterpreterAdapter.Inputs.Count(i => i != "\r")
            .ShouldBe(typedBefore, "recovery Enters the standing body, it never retypes it");

        await AssertBriefArrivedWholeAsync(h, interpretation.Id);
        await SettleInterpretationAsync(h, interpretation.Id, Reading);
        return (run, interpretation.Id);
    }

    // ---- the real chain, both recipient shapes ------------------------------------------------

    [Test]
    public async Task A_dispatched_interpretation_reaches_an_already_eligible_recipient_whole()
    {
        await using var h = await Handoff.CreateAsync(withInterpreter: true);
        await h.EnsureInterpreterAsync();
        await h.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var taskId = await SeedCheckedDelegateAsync(h);

        // The recipient is idle, so production's own enqueue types the note. Its first Enter is
        // swallowed too: the note stands in the composer and the row falls back to Pending.
        h.Adapter.SwallowSubmits = 99;
        var (run, _) = await StartCheckAndDeliverTheBriefAsync(h, taskId);
        (await run).ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        var attempted = await CheckRowAsync(h);
        attempted.Status.ShouldBe(QueuedMessageStatus.Pending);
        attempted.DeliveryAttempts.ShouldBe(1);
        (await h.PromptsAsync(h.SessionId)).ShouldBeEmpty("nothing was submitted yet");

        h.Adapter.SwallowSubmits = 0;
        var typedBefore = h.Adapter.Inputs.Count(i => i != "\r");
        await h.H.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.Count(i => i != "\r").ShouldBe(typedBefore, "recovery never retypes the body");
        await AssertNoteArrivedWholeAsync(h, taskId, carries: Reading);
    }

    [Test]
    public async Task A_dispatched_interpretation_reaches_a_busy_recipient_whole_after_recovery()
    {
        await using var h = await Handoff.CreateAsync(withInterpreter: true);
        await h.EnsureInterpreterAsync();
        var taskId = await SeedCheckedDelegateAsync(h);

        // Busy at production time: the note is persisted and nothing is typed into its turn.
        await h.H.MarkWorkingAsync();
        h.Adapter.SwallowSubmits = 99;
        var (run, _) = await StartCheckAndDeliverTheBriefAsync(h, taskId);
        (await run).ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        var queued = await CheckRowAsync(h);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
        queued.DeliveryAttempts.ShouldBe(0, "a busy recipient is not typed into");
        h.Adapter.Inputs.ShouldBeEmpty();

        // The turn ends; the first flush types it and has its Enter swallowed.
        await h.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.H.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        (await CheckRowAsync(h)).DeliveryAttempts.ShouldBe(1);
        (await h.PromptsAsync(h.SessionId)).ShouldBeEmpty();

        h.Adapter.SwallowSubmits = 0;
        await h.H.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        await AssertNoteArrivedWholeAsync(h, taskId, carries: Reading);
    }

    // ---- persistence failures on each leg -----------------------------------------------------

    /// <summary>Fails the first INSERT into one table, then lets everything through.</summary>
    private sealed class FailFirstInsert(string table) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public int Hits { get; private set; }

        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains($"INSERT INTO \"{table}\"", StringComparison.Ordinal))
            {
                Armed = false;
                Hits++;
                throw new InvalidOperationException($"c501-fixture-{table}-write-failed");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    // The INTERPRETER leg's persistence failure: SpecialistTaskRunner cannot write the run task, so
    // the interpretation degrades to QueueFailed. The note must still SHIP — carrying the digest and
    // saying why — and still arrive whole. An unavailable interpreter silences no check, and it must
    // not leave the recipient with a half-note either.
    [Test]
    public async Task An_interpretation_that_cannot_be_persisted_still_delivers_a_degraded_note_whole()
    {
        var failure = new FailFirstInsert("AgentTasks");
        await using var h = await Handoff.CreateAsync(withInterpreter: true,
            configureDbContext: o => o.AddInterceptors(failure));
        await h.EnsureInterpreterAsync();
        await h.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var taskId = await SeedCheckedDelegateAsync(h);

        failure.Armed = true;
        (await h.Resolve<AgentTaskCheckService>().RunCheckAsync(taskId, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        failure.Hits.ShouldBe(1, "the interpretation task write is the one that failed");
        await using (var db = h.CreateContext())
        {
            // CARD-0501 re-review R2 (F2a): this used to read `t.Status != Queued`, which carved out
            // the one row that actually survived. A failed SaveChanges does not untrack what it
            // tried to insert — EF only accepts changes on success — so the abandoned run stayed
            // Added on the SCOPED context and the check's own next save published it. NO row is the
            // assertion: a Queued interpretation task is a dispatchable one.
            (await db.AgentTasks.CountAsync(t => t.AgentId == h.InterpreterAgentId))
                .ShouldBe(0, "nothing the failed write left behind was ever placed, run, or published");
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.InterpreterSessionId))
                .ShouldBe(0, "and no brief was enqueued for a run the check already gave up on");
        }

        // The degraded note keeps the WHOLE digest where an interpretation would have replaced it,
        // which puts it over the brief ceiling: it ships as a pointer, and the file is the note.
        await AssertNoteArrivedWholeAsync(h, taskId,
            carries: AgentTaskCheckService.InterpreterDownMarker, spilled: true);
    }

    // The consequence the count above only implies, driven to the surface: the REAL dispatcher gets
    // a tick after the failed write. If the abandoned row were still there it would be Queued,
    // pinned to the standing interpreter, and indistinguishable from real work — so the dispatcher
    // would place it on the interpreter's live session, enqueue a brief for a check that already
    // shipped its degraded note, bill the run and occupy the seat against the NEXT interpretation
    // (PlaceOnStandingAgentAsync allows one task at a time on a live composer). "Still tracked"
    // and "still dispatchable" are the same defect seen from two ends.
    [Test]
    public async Task An_abandoned_interpretation_is_never_dispatched_after_its_write_failed()
    {
        var failure = new FailFirstInsert("AgentTasks");
        await using var h = await Handoff.CreateAsync(withInterpreter: true,
            configureDbContext: o => o.AddInterceptors(failure));
        await h.EnsureInterpreterAsync();
        await h.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var taskId = await SeedCheckedDelegateAsync(h);

        failure.Armed = true;
        (await h.Resolve<AgentTaskCheckService>().RunCheckAsync(taskId, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        await h.Resolve<AgentTaskDispatcher>().TickAsync(CancellationToken.None);

        await using (var db = h.CreateContext())
        {
            (await db.AgentTasks.CountAsync(t => t.AgentId == h.InterpreterAgentId))
                .ShouldBe(0, "there is nothing for the dispatcher to find");
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.InterpreterSessionId))
                .ShouldBe(0, "so the interpreter's seat is free for the next real interpretation");
        }

        h.InterpreterAdapter.Inputs.ShouldBeEmpty("and nothing was typed at it");
        (await h.PromptsAsync(h.InterpreterSessionId)).ShouldBeEmpty();
    }

    // The RECIPIENT leg's persistence failure: the queue row cannot be written, so the check reports
    // DeliveryFailed and leaves NOTHING behind — no half-note, no phantom prompt, no timeline row
    // claiming the caller was told. The next run then delivers the whole thing, which is what makes
    // an advisory check safe to retry at all.
    [Test]
    public async Task A_note_whose_queue_row_cannot_be_persisted_fails_cleanly_and_the_retry_delivers()
    {
        var failure = new FailFirstInsert("SessionQueuedMessages");
        await using var h = await Handoff.CreateAsync(withInterpreter: false,
            configureDbContext: o => o.AddInterceptors(failure));
        await h.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var taskId = await SeedCheckedDelegateAsync(h);

        failure.Armed = true;
        (await h.Resolve<AgentTaskCheckService>().RunCheckAsync(taskId, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.DeliveryFailed);

        failure.Hits.ShouldBe(1);
        await using (var db = h.CreateContext())
        {
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId))
                .ShouldBe(0, "no partial row");
            (await db.AgentTaskEvents.CountAsync(
                    e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Check))
                .ShouldBe(0, "and no timeline row claiming the caller was told");
        }

        (await h.PromptsAsync(h.SessionId)).ShouldBeEmpty();
        h.Adapter.Inputs.ShouldBeEmpty();

        (await h.Resolve<AgentTaskCheckService>().RunCheckAsync(taskId, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        await AssertNoteArrivedWholeAsync(h, taskId);
    }

    // ---- the plain (interpreter-off) digest note, unchanged -----------------------------------
    //
    // Kept from the first F2 pass: byte for byte the shape of the 65 rows that were stranded on
    // session cea73d57, on the enqueue path a host with no interpreter takes.

    [Test]
    public async Task A_digest_note_reaches_an_already_eligible_recipient_as_one_whole_prompt()
    {
        await using var h = await Handoff.CreateAsync(withInterpreter: false);
        await h.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var taskId = await SeedCheckedDelegateAsync(h);

        h.Adapter.SwallowSubmits = 99;
        (await h.Resolve<AgentTaskCheckService>().RunCheckAsync(taskId, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        var attempted = await CheckRowAsync(h);
        attempted.Status.ShouldBe(QueuedMessageStatus.Pending);
        attempted.DeliveryAttempts.ShouldBe(1);
        (await h.PromptsAsync(h.SessionId)).ShouldBeEmpty("nothing was submitted yet");

        h.Adapter.SwallowSubmits = 0;
        var typedBefore = h.Adapter.Inputs.Count(i => i != "\r");
        await h.H.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.Count(i => i != "\r").ShouldBe(typedBefore, "recovery never retypes the body");
        await AssertNoteArrivedWholeAsync(h, taskId);
    }

    [Test]
    public async Task A_digest_note_reaches_a_busy_recipient_as_one_whole_prompt_after_recovery()
    {
        await using var h = await Handoff.CreateAsync(withInterpreter: false);
        var taskId = await SeedCheckedDelegateAsync(h);

        await h.H.MarkWorkingAsync();
        h.Adapter.SwallowSubmits = 99;
        (await h.Resolve<AgentTaskCheckService>().RunCheckAsync(taskId, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        var queued = await CheckRowAsync(h);
        queued.Status.ShouldBe(QueuedMessageStatus.Pending);
        queued.DeliveryAttempts.ShouldBe(0, "a busy recipient is not typed into");
        h.Adapter.Inputs.ShouldBeEmpty();

        await h.H.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.H.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        (await CheckRowAsync(h)).DeliveryAttempts.ShouldBe(1);
        (await h.PromptsAsync(h.SessionId)).ShouldBeEmpty();

        h.Adapter.SwallowSubmits = 0;
        await h.H.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);

        await AssertNoteArrivedWholeAsync(h, taskId);
    }
}
