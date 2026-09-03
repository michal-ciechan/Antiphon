using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Configuration for delegated agent tasks. The role→tier ladder lives here rather than in code so
/// the cost profile can be retuned without a deploy.
/// </summary>
public sealed class DelegationSettings
{
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Recent-history window requested by delegations list clients unless they choose Show all.</summary>
    public int DefaultWindowDays { get; set; } = 7;

    /// <summary>How many tasks may be Dispatched/Working at once across all roots.</summary>
    public int MaxConcurrentTasks { get; set; } = 6;

    /// <summary>
    /// CARD-0147: absolute create-time cap on non-specialist tasks in Queued, Dispatched, or
    /// Working. Distinct from <see cref="MaxConcurrentTasks"/> (the dispatcher process ceiling).
    /// Must be a positive integer; there is always an absolute cap.
    /// </summary>
    public int MaxOpenTasks { get; set; } = 3;

    /// <summary>
    /// Backstop only. Nesting is INTENDED (orchestrator → sub-orchestrator → worker is depth 2 and
    /// ordinary), so depth is a poor runaway guard — <see cref="MaxCostUsdPerRoot"/> is the real one.
    /// </summary>
    public int MaxDepth { get; set; } = 5;

    public int MaxTasksPerRoot { get; set; } = 40;

    /// <summary>
    /// The real ceiling on a recursive tree: it can only run away by spending. Crossing it stops
    /// further dispatch for that root; work already in flight is left alone and still reports.
    ///
    /// PER ROOT, not per task — it bounds a whole delegation subtree, so a sub-orchestrator and
    /// everything below it share one budget.
    ///
    /// Raised from 5.00 on 2026-08-12 against measured spend: a single opus investigation costs
    /// $10-17 (CARD-0027 $10.48, CARD-0030 $16.57), so at 5.00 any root with more than one real
    /// delegate blocked almost immediately, and a sub-orchestrator was effectively unusable — its
    /// subtree would stall half-done with the earlier work already paid for. Note the ceiling only
    /// gates NEW dispatches, which is why those single tasks completed while over it.
    /// </summary>
    public decimal MaxCostUsdPerRoot { get; set; } = 50.00m;

    /// <summary>
    /// Per-tier list prices and cache multipliers, bound from <c>Delegation:Pricing</c>. The
    /// ceiling above is only meaningful if these are right — see <see cref="DelegationPricingSettings"/>.
    /// </summary>
    public DelegationPricingSettings Pricing { get; set; } = new();

    /// <summary>
    /// A report at or under this size is forwarded whole — the report IS the deliverable. Above it
    /// the delegate is told to spill to a file (its own judgement about what matters), and the
    /// server backstops with a head+tail excerpt if it didn't.
    ///
    /// It is bounded by the transport, not by taste: see <see cref="PtyInlineSafeChars"/>. It was
    /// 20 000 — which meant nothing ever excerpted, nothing ever spilled, and multi-KB reports went
    /// straight to a pty that silently ate their middles (live miss 2026-08-10).
    ///
    /// It is not set lower than it needs to be, either: the report IS the deliverable, and pushing
    /// an ordinary few-KB report through a file would cost the caller a read for no safety gain.
    /// 3 000 keeps the whole note — header included — inside the largest body MEASURED to arrive
    /// intact (4 262), so a report that fits is one we have evidence the terminal can carry.
    /// </summary>
    public int ReplyInlineMaxChars { get; set; } = 3_000;

    /// <summary>
    /// Replace a pending completion note with a short status-poll pointer when its parent session
    /// has already read the exact report. Disable to retain the full queued note in every case.
    /// </summary>
    public bool ShrinkPolledCompletionNotes { get; set; } = true;

    public int ReplyExcerptHeadChars { get; set; } = 1_800;
    public int ReplyExcerptTailChars { get; set; } = 900;

    /// <summary>
    /// The ceiling for a BRIEF, deliberately far below <see cref="ReplyInlineMaxChars"/>. A brief
    /// above it is written to a file and typed as a pointer instead.
    ///
    /// A report and a brief are not the same risk. The report IS the deliverable, so forwarding it
    /// whole is worth a large ceiling. A brief is only an instruction to go and do something — the
    /// full text is always on the task row and always readable from a file, so spilling it costs
    /// the delegate one read and nothing else. There is no reason to type a brief big enough to be
    /// mangled, and every reason not to.
    ///
    /// 900 comes from measurement, not caution. Four briefs stranded on 2026-08-11 —
    /// 1 366 -&gt; 380, 1 402 -&gt; 380, 1 431 -&gt; 409, 2 320 -&gt; 274 delivered — each keeping
    /// only its FINAL sub-1024-byte chunk, cut at byte 1024n-2, losing the head that carried the
    /// task. Every one sat under <see cref="ReplyInlineMaxChars"/> (3 000) and under
    /// <see cref="PtyInlineSafeChars"/> (4 000), so neither guard fired and neither was ever a
    /// safety guarantee for this failure mode.
    ///
    /// CARD-0027 then root-caused it: the transport is lossless and it is the receiving TUI that
    /// keeps one ~1024-byte read chunk per event-loop turn and discards the rest. Bodies of 810 and
    /// 972 bytes arrived whole 3/3; 1 026 and 1 350 lost their heads 3/3. So the boundary is ONE
    /// READ CHUNK, and a body inside it has no earlier chunk to lose.
    ///
    /// It is counted in UTF-8 BYTES, which is what the read quantum is measured in — NOT in
    /// <c>string.Length</c>. This gate shipped comparing UTF-16 chars, and briefs here are
    /// em-dash-heavy at 3 bytes each: a 900-CHARACTER brief can be 2 700 bytes, three chunks, and
    /// mangle exactly as before while passing the guard.
    ///
    /// <para>This number describes the INBOX CONHOST, which is where it came from: that binary
    /// strips the bracketed-paste markers, so the body arrives as typing and one read chunk is the
    /// boundary. On a pty served by the shipped modern pseudoconsole the boundary is somewhere else
    /// entirely — see <see cref="ModernPtyBriefInlineMaxBytes"/> — and which one applies is decided
    /// per delivery by <c>PtyDeliveryProfile</c>.</para>
    /// </summary>
    public int BriefInlineMaxBytes { get; set; } = 900;

    /// <summary>
    /// The largest body we are willing to type into a TUI in one go, from MEASURED behaviour — not
    /// a guess. Aligning seven real deliveries against what the receiving Claude actually recorded
    /// (2026-08-10) put the cliff between 4 262 characters (intact) and 5 185 (mangled): above it a
    /// single write to the ConPTY input pipe loses whole 1024-byte chunks with no error, no short
    /// write and no exception, which is why it reads as a complete message.
    ///
    /// 4 000 is under the largest body observed to arrive intact, so it is a size we have direct
    /// evidence for rather than an extrapolation. <see cref="ReplyInlineMaxChars"/> sits below it
    /// so delegation bodies never reach the cliff; anything that still crosses it raises an
    /// incident rather than going quietly.
    ///
    /// This is a DAMAGE LIMIT, not a safety guarantee, and the difference cost three tasks. The
    /// 2026-08-10 reading — that the loss only takes the middle, so head and tail always survive —
    /// was contradicted on 2026-08-11 by four deliveries of 1 366-2 320 characters, all far UNDER
    /// this ceiling, which arrived as their final 1024-byte chunk alone: cut at byte 1024n-2, head
    /// gone. Nothing here can promise a typed body arrives whole. Anything whose correctness
    /// depends on a specific span surviving must not rely on a size check — see
    /// <see cref="DelegationReportFormatter.ReportingContract"/>, where the correlation marker is
    /// emitted at BOTH ends for exactly this reason.
    /// </summary>
    public int PtyInlineSafeChars { get; set; } = 4_000;

    /// <summary>
    /// Where typed-body loss actually STARTS: one ConPTY read chunk, in UTF-8 bytes.
    ///
    /// CARD-0027 root-caused the loss to the receiving TUI, which keeps ONE ~1024-byte read chunk
    /// per event-loop turn and discards the rest. Measured against real Claude: 810 and 972-byte
    /// bodies arrived whole 3/3; 1 026 and 1 350-byte bodies lost their heads 3/3; the cut sits at
    /// body byte 1029 at 7-byte resolution. A body inside one chunk has no earlier chunk to lose.
    ///
    /// This is the threshold the oversize incident should fire on, NOT
    /// <see cref="PtyInlineSafeChars"/>. That one is 4 000 CHARACTERS, so a body between roughly
    /// 1 KB and 4 KB was typed, clipped, and raised nothing at all — the exact window that
    /// swallowed four briefs on 2026-08-11 while the guard stayed quiet.
    ///
    /// <para>Also the inbox conhost's number, and the tripwire's threshold only there; on the modern
    /// pseudoconsole it is <see cref="ModernPtySingleWriteMaxBytes"/>. The tripwire itself is never
    /// removed — see that field for why an 86 400-byte limit is still a limit.</para>
    /// </summary>
    public int PtySingleChunkBytes { get; set; } = 1_024;

    // ── The same three ceilings, for a pty served by the SHIPPED modern pseudoconsole ────────────
    //
    // Everything above this line describes the inbox conhost, which strips the bracketed-paste
    // markers so every body arrives as typing. With conpty.dll + OpenConsole.exe in front of the
    // pty the markers survive, the TUI takes its paste path, and the numbers change by two orders
    // of magnitude. Which set is in force is decided per delivery by PtyDeliveryProfile from the
    // backend ACTUALLY serving the pty — never by these values existing.

    /// <summary>
    /// <see cref="BriefInlineMaxBytes"/> on the modern backend.
    ///
    /// 43 200 bytes, measured: whole 2/2 through the bench host AND 2/2 through the production path
    /// (<c>PtyAgentRunner</c> + <c>PtyInputEncoding</c>, one write, real Claude, 2026-08-12), and it
    /// is the largest size that ALSO survived a PACED delivery. It therefore keeps a 2x margin under
    /// <see cref="ModernPtySingleWriteMaxBytes"/> — which matters, because that envelope is one
    /// machine and one Claude version.
    ///
    /// At the inbox ceiling of 900 the inline path is unreachable in practice (the reporting
    /// contract alone is 838 bytes, so BuildBrief's floor is ~915 and EVERY brief spills). This is
    /// the number that re-opens it — which is the payoff CARD-0037 was for.
    /// </summary>
    public int ModernPtyBriefInlineMaxBytes { get; set; } = 43_200;

    /// <summary>
    /// <see cref="ReplyInlineMaxChars"/> on the modern backend: 14 400 = 43 200 / 3.
    ///
    /// Derived, not chosen. This ceiling is counted in UTF-16 CHARS while the transport envelope is
    /// UTF-8 BYTES, and an em-dash — which these reports are full of — costs 3 of them. Dividing by
    /// the worst-case expansion is what keeps a report that passes this gate inside
    /// <see cref="ModernPtyBriefInlineMaxBytes"/> on the wire. The char/byte confusion is not
    /// hypothetical: <see cref="BriefInlineMaxBytes"/> shipped once comparing <c>string.Length</c>
    /// and mangled four briefs that passed it.
    /// </summary>
    public int ModernPtyReplyInlineMaxChars { get; set; } = 14_400;

    /// <summary>
    /// <see cref="PtySingleChunkBytes"/>'s counterpart: the oversize tripwire on the modern backend.
    ///
    /// 86 400 bytes is the largest body MEASURED to arrive whole — 2/2 through the production write
    /// path, versus the inbox control in the same run keeping 25%. It is a ceiling on ONE WRITE and
    /// nothing else: the identical 86 400 bytes delivered PACED (1 KB chunks, 25 ms apart) read
    /// NOTHING, so a delivery that gets split on its way to the pty has no evidence behind it at
    /// any size. Our path does not split (SessionMessageQueueService issues one SendInputAsync for
    /// the body, the pty-host frame carries it whole, and PtyAgentRunner does one WriteAsync), and
    /// that must stay true for this number to mean anything.
    ///
    /// The tripwire is NOT deleted on the modern backend, only moved: anything past the measured
    /// envelope is beyond all evidence and still raises
    /// <c>AgentIncidentKind.OversizedTerminalDelivery</c>.
    /// </summary>
    public int ModernPtySingleWriteMaxBytes { get; set; } = 86_400;

    // ── Herdr pane.send_text ceilings (CARD-0161) ───────────────────────────────────────────────
    //
    // Separate knobs from modern because they are separate measurements — a herdr upgrade
    // re-measures herdr, not the pty. Numbers match modern because the envelope was measured
    // identical (86 400 exact: S1 twice + plan M1 + M2, 2026-08-23) and herdr 0.8.2 ships the
    // modern ConPTY runtime app-local.

    /// <summary>
    /// Brief inline ceiling on herdr <c>pane.send_text</c> (CARD-0161). 43 200 = 2× margin under
    /// the measured 86 400 single-write envelope (plan M1/M2, 2026-08-23, herdr 0.8.2 + Claude
    /// 2.1.241): exact byte-for-byte UserPrompt, zero ESC bytes in the record.
    /// </summary>
    public int HerdrPaneBriefInlineMaxBytes { get; set; } = 43_200;

    /// <summary>
    /// Reply inline ceiling on herdr: 14 400 = 43 200 / 3 (same char/byte derivation as modern).
    /// </summary>
    public int HerdrPaneReplyInlineMaxChars { get; set; } = 14_400;

    /// <summary>
    /// Oversize tripwire on herdr: 86 400 bytes — largest body measured exact through one
    /// <c>pane.send_text</c> (CARD-0161 plan M1 single-write AND M2 paced, 2026-08-23). Edge of
    /// the evidence, not a measured cliff; the single-write rule is kept.
    /// </summary>
    public int HerdrPaneSingleWriteMaxBytes { get; set; } = 86_400;

    /// <summary>Herdr-lane ceilings record (CARD-0161). Only consulted for SessionBackend.Herdr sessions.</summary>
    public PtyDeliveryCeilings HerdrCeilings(string reason) => new(
        DeliveryBackend.HerdrPane, HerdrPaneBriefInlineMaxBytes,
        HerdrPaneReplyInlineMaxChars, HerdrPaneSingleWriteMaxBytes, reason);

    /// <summary>
    /// The ceilings for one pseudoconsole. <see cref="PtyBackend.InboxConhost"/> — the default, and
    /// anything we are not sure about — returns exactly what shipped before CARD-0037.
    /// Maps onto <see cref="DeliveryBackend"/> (CARD-0161); herdr is not a PtyBackend value.
    /// </summary>
    public PtyDeliveryCeilings CeilingsFor(PtyBackend backend, string reason) => backend switch
    {
        PtyBackend.ModernConPty => new PtyDeliveryCeilings(
            DeliveryBackend.ModernConPty, ModernPtyBriefInlineMaxBytes, ModernPtyReplyInlineMaxChars,
            ModernPtySingleWriteMaxBytes, reason),
        _ => new PtyDeliveryCeilings(
            DeliveryBackend.InboxConhost, BriefInlineMaxBytes, ReplyInlineMaxChars,
            PtySingleChunkBytes, reason),
    };

    /// <summary>
    /// A DIFFERENT TRANSPORT ENTIRELY, so none of the ceilings above apply: instruction bundles
    /// (CARD-0058) ride <c>--append-system-prompt</c>, which is a launch ARGUMENT and never typed into
    /// a pty. What bounds it is <c>CreateProcessW</c>, whose command line may hold ~32 767 UTF-16
    /// characters including the terminator — so this budget is counted in CHARS, not in the UTF-8
    /// bytes every pty ceiling here is measured in.
    ///
    /// <para>30 000 leaves ~2 700 characters for the parts a composing caller cannot see: the
    /// resolved executable path, the definition's own base args, the <c>--session-id</c> or
    /// <c>--resume</c> the launch adds, and the quoting each argument costs on the way to the OS.</para>
    ///
    /// <para>It is a runaway stop, not a working constraint, and the measurement says so:
    /// <c>InstructionBundleTests</c> composes the worst case anyone can currently construct — every
    /// bundle in the catalog at once, plus the longest system-prompt append that ships (the Telegram
    /// preset) — and pins that it sits far under this number, with the actual measured size in the
    /// assertion message. That worst case measured <b>9 198 chars on 2026-08-17</b> (board-api 2 607,
    /// delegate-basics 2 216, check-interpreter 1 276, orchestrator 1 156, preset 1 802): 31% of this
    /// budget, while no real launch composes more than two bundles. Something that trips this guard is
    /// therefore a bundle that grew by an order of
    /// magnitude or a pasted document in an agent's append, and both want a human, not a truncation:
    /// <c>InstructionBundleComposer.EnsureWithinCommandLineBudget</c> THROWS. An agent silently
    /// running under half a contract, with nothing on screen to say so, is the failure mode that is
    /// worth failing a launch to avoid.</para>
    /// </summary>
    public int CommandLineBudgetChars { get; set; } = 30_000;

    /// <summary>
    /// Directory prefixes a task may run in. A SECURITY BOUNDARY: without it an agent that can
    /// delegate could point a task at any path the server user can read. Empty = only the parent's
    /// own working directory tree is allowed.
    /// </summary>
    public List<string> AllowedRoots { get; set; } = [];

    public int DefaultCols { get; set; } = 120;
    public int DefaultRows { get; set; } = 30;

    /// <summary>What ANTIPHON_API is set to in a delegate's environment — where it calls back.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:17202";

    /// <summary>Role → tier and per-role timeouts. Missing roles fall back to <see cref="DefaultLevel"/>.</summary>
    public Dictionary<string, RolePolicyEntry> RolePolicy { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Plan"] = new() { Level = AgentModelLevel.Frontier, RecommendedInFlight = 1 },
        ["Code"] = new() { Level = AgentModelLevel.Frontier, RecommendedInFlight = 1 },
        ["Review"] = new() { Level = AgentModelLevel.Frontier, RecommendedInFlight = 1 },
        // EscalateTo stays for the manual ladder (/escalate); EscalateAfterMinutes is deliberately
        // unset — the auto-trigger is disarmed by default (CARD-0158). Same pattern as Test below.
        ["Debug"] = new() { Level = AgentModelLevel.High, EscalateTo = AgentModelLevel.Frontier, RecommendedInFlight = 1 },
        ["Coverage"] = new() { Level = AgentModelLevel.High, RecommendedInFlight = 1 },
        // High: this role is the conflict resolver CreateMergeTaskAsync spawns after
        // TryMergeBackAsync already failed. Clean fast-forwards never reach it
        // (in-process git). A verify-merge-deploy is Test/Deploy/Commit, not Merge.
        ["Merge"] = new() { Level = AgentModelLevel.High, RecommendedInFlight = 1 },
        ["Docs"] = new() { Level = AgentModelLevel.Medium, RecommendedInFlight = 1 },
        ["Commit"] = new() { Level = AgentModelLevel.Medium, RecommendedInFlight = 1 },
        // Low tier is safe for Test/Deploy because these RUN things and report what happened —
        // INTERPRETING a failure is a separate Debug task at High.
        ["Test"] = new() { Level = AgentModelLevel.Low, EscalateTo = AgentModelLevel.Medium, RecommendedInFlight = 1 },
        ["Deploy"] = new() { Level = AgentModelLevel.Low, RecommendedInFlight = 1 },
    };

    public AgentModelLevel DefaultLevel { get; set; } = AgentModelLevel.High;

    /// <summary>
    /// Config defaults for a complexity tier with no active <c>ComplexityChains</c> row
    /// (CARD-0090). EMPTY as shipped — routing policy changed nine times in three days, so a
    /// seeded fable→opus→sol→grok chain would be wrong the moment it landed. A human writes
    /// the live lists with <c>complexity-chain.ps1 set</c>.
    /// </summary>
    public Dictionary<string, List<ComplexityCandidateSettings>> ComplexityChains { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A Dispatched task whose session has written no TURN PROMPT of its own after this long never
    /// received its brief — it fails loudly instead of sitting Dispatched forever
    /// (CARD-0003/CARD-0020). Long enough for the stranded-queue watchdog (60s cadence) to
    /// redeliver a reverted brief several times first; a genuinely slow FIRST TURN still counts as
    /// started the moment its prompt lands, so this never fires on slow work.
    ///
    /// <para>The predicate is <c>TranscriptPromptSpan.HasTurnPromptSinceAsync</c>, NOT "zero
    /// transcript entries" — that was true until CARD-0077 and is what this comment used to say. A
    /// REUSED warm-pool session inherits the previous task's history, so "any entry at all" was
    /// always true on one and this whole clock was unreachable for it, however completely the new
    /// brief was lost. Compaction's own housekeeping records do not count as a prompt either.
    /// A drained composer-queue command (<c>QueuedUserPrompt</c>) does count — it is the record
    /// of a body that reached the model while the TUI was busy, and there is no accompanying
    /// <c>user</c> record for the watchdog to fall back on (CARD-0135).</para>
    ///
    /// <para>This is the shared DELIVERY grace. The first-prompt watchdog still only asks whether
    /// work STARTED — a task that started and then ran forever is
    /// <see cref="DefaultTimeoutMinutes"/> / <c>RolePolicyEntry.TimeoutMinutes</c> and
    /// <see cref="ModelWaitDeadlineMinutes"/>/<see cref="LocalExecutionDeadlineMinutes"/>
    /// (CARD-0020 S2/S3). The same clock is the age past which a Pending caller-session
    /// Delegation or Check note is a <c>CallerNoteUndelivered</c> attention row (CARD-0267) —
    /// detection only, no fail-and-kill.</para>
    /// </summary>
    public int DeliveryFailTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// The hard wall-clock ceiling (minutes, from <c>DispatchedAt</c>) for a role that has no
    /// <see cref="RolePolicyEntry"/> of its own — <c>Custom</c> and <c>Check</c> in the shipped
    /// defaults. Mirrors <see cref="DefaultLevel"/>: an unlisted role is a role nobody configured,
    /// not a role nobody watches. <c>&lt;= 0</c> turns the ceiling off for those roles.
    /// </summary>
    public int DefaultTimeoutMinutes { get; set; } = 240;

    /// <summary>
    /// How long a WORKING session may sit with a model-wait phase as its last transcript entry
    /// before <c>AgentTaskDispatcher.FailOverdueTasksAsync</c> fails the task (CARD-0020 S3).
    /// Model-wait is <c>UserPrompt</c>, <c>ToolResult</c>, <c>Thinking</c> or <c>AssistantText</c>
    /// last: in all four the next thing that should happen is the model answering.
    ///
    /// <para><b>20 minutes is measured, not chosen.</b> Over the live corpus (10 days, queried
    /// 2026-08-20) the gap after a real prompt ran to p99 163 s, max 217 s; after a tool result,
    /// p99 60 s, max 1 478 s. 20 min is ~3x the observed maximum, so a single slow day is not an
    /// incident. The card's original proposal of ~60 s would have fired on roughly 1 turn in 25.
    /// <c>&lt;= 0</c> disables the model-wait deadline.</para>
    /// </summary>
    public int ModelWaitDeadlineMinutes { get; set; } = 20;

    /// <summary>
    /// The same clock for a WORKING session whose last entry is a <c>ToolCall</c> — the model has
    /// answered and a LOCAL tool (a build, a test suite, a long grep) is running (CARD-0020 S3).
    ///
    /// <para>Also measured: <c>ToolCall</c> to <c>ToolResult</c> ran to p99 134 s and max 5 311 s
    /// over 15 210 transitions, so 90 minutes is ~1x the observed maximum with half an hour of
    /// headroom. Note how little the phases actually separate — ~3.6x, not orders of magnitude —
    /// which is why phase-awareness is a TIGHTENING of the ceiling rather than the "catch a hung
    /// upstream call in a minute" mechanism the card imagined. <c>&lt;= 0</c> disables the
    /// local-execution deadline.</para>
    /// </summary>
    public int LocalExecutionDeadlineMinutes { get; set; } = 90;

    /// <summary>
    /// How long settlement waits for the turn-ending response's OWN text before giving up and
    /// settling on whatever the turn produced (CARD-0046).
    ///
    /// The wait is closed by IDENTITY, supplied by each provider's normalizer: Claude Code's
    /// <c>message.id</c>, Grok's <c>promptId</c>, and Codex TUI's <c>turn_id</c> on only its
    /// <c>final_answer</c> AgentMessage and matching <c>task_complete</c>. For Claude, one API
    /// response arrives as several JSONL records — a signature-only <c>thinking</c> record, then
    /// the <c>text</c> record — all with one <c>message.id</c>, so the first thing that reaches us
    /// can be a BARE TurnEnd. The wait is only the backstop for a response that never writes text
    /// at all: 1 in 180 in the measured corpus, an <c>end_turn</c> thinking record followed by
    /// <c>API Error: Connection lost mid-response</c>. Without the backstop such a task would sit
    /// Dispatched forever.
    ///
    /// Measured need is ~1.2 s (persist gap 0.01-1.17 s at a 300 ms tailer poll). 120 s absorbs a
    /// tailer stall or a stream gap and still sits far under
    /// <see cref="DeliveryFailTimeoutMinutes"/> (10 min).
    ///
    /// <para><b>Escape hatch:</b> <c>&lt;= 0</c> means "never defer" — settlement behaves exactly as
    /// it did before CARD-0046, including discarding the report. Only set it to prove a regression
    /// came from here.</para>
    /// </summary>
    public int FinalMessageGraceSeconds { get; set; } = 120;

    /// <summary>
    /// How long settlement waits for the BACKGROUND subagents a turn launched before giving up and
    /// settling without them (CARD-0046 slice 4).
    ///
    /// A delegate that spawns Claude Code's built-in <c>Agent</c> tool asynchronously gets "Async
    /// agent launched successfully" back immediately, writes an announcement and legitimately ENDS
    /// its turn — the work it was asked to do has not happened yet. Task 26421cf2 settled on that
    /// announcement at 07:44:10 and folded four reviews into a 6 195-character verdict at 07:48:06,
    /// four minutes after it had been priced and closed. The wait is closed by IDENTITY, not by a
    /// count: each notification names the <c>toolu_…</c> id of the launch it answers.
    ///
    /// This is only the backstop for a subagent that dies without ever notifying. Minutes, not
    /// seconds, because the thing being waited for is another agent doing real work — the measured
    /// four took 78-236 s. <c>&lt;= 0</c> disables the wait entirely (pre-slice-4 behaviour).
    /// </summary>
    public int SubagentGraceMinutes { get; set; } = 30;

    /// <summary>
    /// After the closing-line nudge has actually been TYPED (SessionQueuedMessages.SentAt), how
    /// long settle-anyway waits before accepting a TEXT-LESS post-nudge boundary as the delegate's
    /// non-answer (CARD-0248). 240 s ≈ the measured maximum prompt→response gap (217 s, see
    /// ModelWaitDeadlineMinutes) — inside it, the real answer text is very probably still coming.
    /// A post-nudge boundary WITH final-message text needs no window: the answer is the answer.
    /// </summary>
    public int ReportNudgeResponseSeconds { get; set; } = 240;

    /// <summary>
    /// Minimum interval between the deferred-report sweep re-handing an UNCHANGED boundary to
    /// settlement (CARD-0248). The sweep's predicates are monotonic, so without this it re-enters
    /// settlement every PollIntervalSeconds tick for the life of an affected task — the re-entry
    /// channel that ate the CARD-0159 nudge. Correctness never depends on this (settlement's own
    /// gates make re-entry inert); it bounds the query load and closes the class. A changed
    /// boundary always hands off immediately. &lt;= 0 restores per-tick re-handing (tests).
    /// </summary>
    public int ReportSweepRehandSeconds { get; set; } = 60;

    /// <summary>
    /// After the one closing-line nudge is recorded (<c>ReportNudgedAt</c>, enqueue time, not
    /// <c>SentAt</c>), how long the session may stay idle on that same unmarked boundary
    /// before the sweep Blocks the task as <c>UnmarkedWaiting</c> (CARD-0294). Default 5
    /// minutes — under <c>PastExpectedIdle</c>'s 30-minute floor, over a legitimate "about
    /// to type the marker" pause. Distinct from <see cref="ReportNudgeResponseSeconds"/>
    /// (text-less post-nudge boundary). <c>&lt;= 0</c> disarms the Blocked sweep; the
    /// attention row can still show.
    /// </summary>
    public int UnmarkedWaitingMinutes { get; set; } = 5;

    /// <summary>
    /// CARD-0299 S2. How many times a cold Codex delegate whose first delivery returned
    /// <c>NoSubmitOutput</c> may be killed and relaunched. Default 1. A second wedge
    /// Fails the task immediately instead of waiting out the 10-minute watchdog.
    /// </summary>
    public int BootWedgeRelaunchLimit { get; set; } = 1;

    /// <summary>
    /// How long an open task whose session is DEAD (<see cref="AgentTaskLiveness.IsDeadSession"/>)
    /// must keep looking dead before the dispatcher fails it (CARD-0021). Measured from the first
    /// sweep that saw it that way, in memory — a server restart only ever delays the failure.
    ///
    /// <para>The window is not politeness, it is the CARD-0056 brake. A DB row saying a session is
    /// Failed was once wrong about a healthy session — the operator's own — and reconciliation's
    /// third pass RE-ADOPTS such a row on positive evidence, flipping it back to Running (which
    /// removes the task from this sweep's predicate entirely). Three minutes is a dozen of that
    /// sweep's 15 s passes, and also enough for the transcript backfill a session-close triggers to
    /// give ordinary settlement its chance at a report that arrived just before the death.</para>
    ///
    /// <para><b>Escape hatch:</b> <c>&lt;= 0</c> disarms the sweep entirely — nothing is failed on a
    /// dead session and the state is left to the attention projection and a human, which is exactly
    /// the behaviour that shipped before this card.</para>
    /// </summary>
    public int DeadSessionFailGraceMinutes { get; set; } = 3;

    // ── Scheduled check-ins on a running delegate (CARD-0047) ───────────────────────────────────
    //
    // An orchestrator hears nothing between dispatch and the report. These five knobs decide when a
    // deterministic probe of the delegate's state is gathered and delivered back to the caller.
    // None of them is a deadline: no code path fails, escalates or cancels a task off any of them.

    /// <summary>The whole feature's off switch. False and no task is ever armed or swept.</summary>
    public bool CheckEnabled { get; set; } = true;

    /// <summary>
    /// How often the land sweep re-enqueues pending <c>LandRequestedAt</c> rows this process
    /// does not hold (CARD-0331). Also the Held retry cadence. Floor 1, ceiling 60.
    /// </summary>
    public int LandSweepSeconds { get; set; } = 5;

    /// <summary>
    /// Started-and-interrupted git attempts on one land request before the sweep refuses
    /// (CARD-0331). Held passes do not count. Floor 1, ceiling 10.
    /// </summary>
    public int LandMaxAttempts { get; set; } = 3;

    /// <summary>
    /// <see cref="AgentTask.ExpectedDurationMinutes"/> for a caller that declared nothing. Ten
    /// minutes is roughly the median delegated task here, so an undeclared task still gets one
    /// early check instead of silence.
    /// </summary>
    public int DefaultExpectedMinutes { get; set; } = 10;

    /// <summary>
    /// The base of the Fibonacci ramp (CARD-0061): interval(1) is this, interval(2) is twice this,
    /// and each interval after that is the sum of the previous two. No longer scaled by
    /// <see cref="AgentTask.ExpectedDurationMinutes"/> — the declared duration only schedules the
    /// FIRST check, not the ramp that follows it.
    /// </summary>
    public int CheckMinIntervalMinutes { get; set; } = 5;

    /// <summary>Ceiling on the Fibonacci ramp — a long task settles into an hourly heartbeat.</summary>
    public int CheckMaxIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// After this many checks the task stops being checked and the last note says so. At ~$0.01 a
    /// check the economics never bind; this exists so a forgotten immortal task doesn't check
    /// forever.
    /// </summary>
    public int CheckMaxCount { get; set; } = 10;

    /// <summary>
    /// Briefly wait for the completion note that settlement normally queues immediately after it
    /// writes the task status (CARD-0132). A superseded check is suppressed only when that note
    /// exists, so a transient settle-to-enqueue gap cannot make a scheduled check disappear.
    /// </summary>
    public int CompletionNoteGraceSeconds { get; set; } = 5;

    // ── The check interpreter: a standing specialist agent (CARD-0047 slice 4 amendment) ────────
    //
    // A check delivers a deterministic digest today and always will. These five knobs govern the
    // OPTIONAL layer on top of it: a long-running, supervised haiku agent that reads the bundle and
    // says what it looks like. Every failure mode of that agent degrades to the digest, so none of
    // these knobs can break a check — the first one turns the whole layer off, back to slice 3.

    /// <summary>
    /// Off and a check is exactly what slice 3 shipped: the digest, no prefix, no specialist, no
    /// interpretation task. This is the switch to reach for if the specialist ever misbehaves.
    /// </summary>
    public bool CheckInterpreterEnabled { get; set; } = true;

    /// <summary>
    /// Slug AND name of the standing specialist. The provisioner finds it by this exact slug, so
    /// changing it provisions a SECOND agent rather than renaming the first — delete the old row.
    /// </summary>
    public string CheckInterpreterAgentSlug { get; set; } = "antiphon-check-interpreter";

    /// <summary>
    /// The specialist's own scratch working directory. Null derives it: the first
    /// <see cref="AllowedRoots"/> entry plus <c>\.antiphon\check-interpreter</c>, or — when no roots
    /// are configured — a directory under the system temp path.
    ///
    /// <para>A DISTINCT cwd is not tidiness, it is the CARD-0006 mitigation by construction. Claude's
    /// transcript root is per-cwd, so an agent sharing <c>C:/src/Antiphon</c> with the operator and
    /// several other agents is one failed discovery away from binding a stranger's conversation. Its
    /// own directory gives it its own transcript root, and the question never arises.</para>
    /// </summary>
    public string? CheckInterpreterWorkingDirectory { get; set; }

    /// <summary>
    /// How long a check waits for its interpretation before delivering the digest degraded. Well
    /// under <see cref="CheckMinIntervalMinutes"/> on purpose: a check must never still be waiting
    /// when its successor comes due. The check worker is a single serial drainer, so this is also
    /// its worst-case stall per check.
    /// </summary>
    public int CheckInterpreterWaitSeconds { get; set; } = 60;

    /// <summary>
    /// At or above this many unfinished interpretation tasks on the specialist, a check skips
    /// creating one and degrades immediately. There is ONE specialist and many delegates can come
    /// due together; without this the queue grows and every check pays the full wait behind a pile.
    /// </summary>
    public int CheckInterpreterMaxBacklog { get; set; } = 2;

    // ── The diagnose seat: a standing specialist agent (CARD-0352) ─────────────────────────────
    //
    // Titles untitled tasks and labels unlabelled cards. Every failure mode degrades to today's
    // behaviour (the raw fallback title, the unlabelled card), so none of these knobs can break
    // create or the board. DiagnoseEnabled is the switch to reach for if the seat misbehaves.

    /// <summary>
    /// Off and neither job runs: no seat, no queue, no ledger row. Title create and card
    /// create stay byte-identical to today.
    /// </summary>
    public bool DiagnoseEnabled { get; set; } = true;

    /// <summary>Job 1: replace a long Goal-fallback title after create. Nested under <see cref="DiagnoseEnabled"/>.</summary>
    public bool DiagnoseTitleEnabled { get; set; } = true;

    /// <summary>Job 2: the periodic unlabelled-card sweep. Nested under <see cref="DiagnoseEnabled"/>.</summary>
    public bool DiagnoseSweepEnabled { get; set; } = true;

    /// <summary>
    /// Apply writes the labels; Shadow runs the seat and writes the ledger only. Default Apply —
    /// until CARD-0332 routes on the labels, a wrong one costs a human edit.
    /// </summary>
    public DiagnoseLabelMode DiagnoseLabelMode { get; set; } = DiagnoseLabelMode.Apply;

    /// <summary>
    /// Slug AND name of the standing specialist. The provisioner finds it by this exact slug, so
    /// changing it provisions a SECOND agent rather than renaming the first — delete the old row.
    /// </summary>
    public string DiagnoseAgentSlug { get; set; } = "antiphon-diagnose";

    /// <summary>
    /// The specialist's own scratch working directory. Null derives it: the first
    /// <see cref="AllowedRoots"/> entry plus <c>\.antiphon\diagnose</c>, or — when no roots are
    /// configured — a directory under the system temp path. A distinct cwd is the CARD-0006
    /// mitigation by construction, same as the check interpreter.
    /// </summary>
    public string? DiagnoseWorkingDirectory { get; set; }

    /// <summary>
    /// How long a diagnose request waits for an answer. Longer than the interpreter's 60 s
    /// because a cold first launch after deploy is the expected p90, and a dropped title is
    /// cosmetic rather than a missed check-in.
    /// </summary>
    public int DiagnoseWaitSeconds { get; set; } = 90;

    /// <summary>
    /// At or above this many unfinished Diagnose rows on the seat, a new request is dropped
    /// (titles) or retried next tick (cards). One specialist, serial drainer.
    /// </summary>
    public int DiagnoseMaxBacklog { get; set; } = 2;

    /// <summary>UTC-day cap on Diagnose-role spend. Crossing it writes <c>DegradedBudget</c> and creates no row.</summary>
    public decimal DiagnoseDailyBudgetUsd { get; set; } = 2.00m;

    /// <summary>
    /// A Goal-fallback title this long or shorter already *is* a title (CARD-0351's CLI warning
    /// threshold). Only longer fallbacks are queued for replacement.
    /// </summary>
    public int DiagnoseTitleMinFallbackChars { get; set; } = 80;

    /// <summary>Sweep period. Floored at 1 minute by the hosted service.</summary>
    public int DiagnoseSweepMinutes { get; set; } = 10;

    /// <summary>Cards enqueued per sweep tick.</summary>
    public int DiagnoseSweepBatch { get; set; } = 5;

    /// <summary>A <c>Diagnoses</c> row newer than this excludes the card from the next sweep.</summary>
    public int DiagnoseRetryHours { get; set; } = 24;

    /// <summary>
    /// This many non-<c>Applied</c> rows newer than the card's <c>UpdatedAt</c> exclude it until
    /// the card is edited (an edited card earns a fresh attempt).
    /// </summary>
    public int DiagnoseMaxAttemptsPerCard { get; set; } = 3;

    /// <summary>
    /// Card description budget in the LABELS brief. Head + tail, with an elision marker.
    /// Live open cards fit in one brief today (p90 ~6 715, max ~8 412).
    /// </summary>
    public int DiagnoseMaxInputChars { get; set; } = 12_000;

    /// <summary>A sub-orchestrator decomposes, which is expensive thinking — never below this.</summary>
    public AgentModelLevel MinOrchestratorLevel { get; set; } = AgentModelLevel.High;

    /// <summary>
    /// Arm the PreToolUse deny hook (block Edit/Write — "delegate this instead") in each
    /// orchestrator's worktree by default. Per-task <c>DenyDirectEdits</c> overrides this either
    /// way. The hook is only ever written into a task's OWN worktree, never a shared directory.
    /// </summary>
    public bool OrchestratorDenyHookEnabled { get; set; } = true;

    /// <summary>
    /// Name of the tracked file at each repo's root that names its areas (CARD-0063). A repo
    /// without one simply has no names — every scope token is read as a path, which is the
    /// behaviour that predates the map. Configurable so a repo that already owns the filename can
    /// choose another; never a path, always a basename resolved against the task's RepoPath.
    /// </summary>
    public string AreasFileName { get; set; } = "antiphon.areas.json";

    /// <summary>
    /// Hold a queued <c>Shared</c> task behind ANY running <c>Shared</c> task in the same repo,
    /// whether or not either declares a scope (CARD-0063 D3).
    ///
    /// <para>On by default because the delegate skill already states that a second write-capable
    /// task in a shared checkout is a collision <b>regardless of scope</b> — one <c>git status</c>,
    /// one <c>git add -A</c>, one <c>bin/</c> — and its 2026-08-18 live miss is exactly a caller
    /// forgetting to ask. With the <c>Held</c> event the wait is visible, and the caller can
    /// re-dispatch with <c>-Worktree</c>. Check-role and ReadOnly tasks are outside it.</para>
    ///
    /// <para>Turn it off to run deliberately sequential shared tasks across two checkouts of one
    /// repo, where the pair is safe and the operator knows it.</para>
    /// </summary>
    public bool SerialiseSharedWriters { get; set; } = true;

    /// <summary>
    /// Reuse settled delegates instead of spawning a fresh Claude per task. A Shared task's agent
    /// goes warm on settle and the next task in the same directory (at the same tier) takes it
    /// over — with a focused /compact first when the work is unrelated. Worktree delegates are
    /// never pooled: their directory dies with the merge.
    /// </summary>
    public bool PoolEnabled { get; set; } = true;

    /// <summary>
    /// How long after settling a warm delegate answers ONLY to the run that just used it, so a
    /// caller can send follow-up work to the same agent (same context) without racing the rest of
    /// the queue for it. After this it is back in the general pool.
    /// </summary>
    public int PoolReservedForCallerMinutes { get; set; } = 5;

    /// <summary>Idle this long and the warm delegate is retired — session stopped, row deleted.</summary>
    public int PoolIdleRetireMinutes { get; set; } = 60;

    /// <summary>
    /// At most this many warm delegates per directory; the oldest surplus is retired immediately.
    /// This is the knob that scales how many workers stay ready for a directory's queue.
    /// </summary>
    public int PoolMaxIdlePerDirectory { get; set; } = 3;

    public sealed class RolePolicyEntry
    {
        public AgentModelLevel Level { get; set; } = AgentModelLevel.High;

        /// <summary>
        /// The tier a manual <c>EscalateAsync</c> (human <c>/escalate</c>, or an explicit caller)
        /// resolves to for this role. <c>AgentTaskService.ResolveEscalationTarget</c> prefers this
        /// over rung-counting when set — that is the primary job of this field.
        ///
        /// <para>It is ALSO the target <c>AgentTaskDispatcher.AutoEscalateStalledAsync</c> re-runs
        /// a quiet task at, but ONLY when <see cref="EscalateAfterMinutes"/> is set too. The sweep
        /// drops any role missing either, so a half-configured role is silently never scanned.
        /// Shipped defaults arm <c>EscalateTo</c> alone on Debug (→ Frontier) and Test (→ Medium):
        /// a ladder a human climbs, not a clock. Re-arm the automatic trigger by setting
        /// <see cref="EscalateAfterMinutes"/> in appsettings.</para>
        ///
        /// <para><b>This ladder is a deliberate, narrow tier bump and NOT a health check</b>
        /// (CARD-0020 S4). A second gate skips any task already at or above the target, and Frontier
        /// is the top, so the most expensive work in the fleet is out of scope by construction.</para>
        ///
        /// <para><b>CARD-0158 (2026-08-23): the automatic trigger is disarmed by default.</b>
        /// Debug used to ship with <c>EscalateAfterMinutes = 25</c>. That clock fired exactly twice
        /// ever (both 2026-08-11), both on idle-after-a-completed-turn — a shape now owned 15
        /// minutes earlier by the delivery watchdog's uncorrelated-report arm (fail-with-pointer,
        /// kill withheld when working). Both historical firings killed sessions holding finished,
        /// already-pushed work and wasted Frontier retries. The remaining reachable territory for a
        /// 25-minute quiet clock is a false-positive trap: 25 min sits inside the measured
        /// 88.5-minute healthy local-execution window
        /// (<see cref="DelegationSettings.LocalExecutionDeadlineMinutes"/> = 90). Re-arm only with
        /// that history in mind.</para>
        ///
        /// <para><b>That narrowness is the design.</b> The usual cause of a stalled delegate is a
        /// LOST PROMPT, and escalating one launders an undelivered brief into a billed re-run on a
        /// bigger model — the same argument <c>FailNeverStartedAsync</c> makes in its own
        /// doc-comment. Do not widen this to Plan/Code/Review to get health coverage. Health is
        /// <see cref="TimeoutMinutes"/> / <see cref="DelegationSettings.DefaultTimeoutMinutes"/>
        /// (the hard ceiling) and <see cref="DelegationSettings.ModelWaitDeadlineMinutes"/> /
        /// <see cref="DelegationSettings.LocalExecutionDeadlineMinutes"/> (the phase-aware
        /// deadline) — both run for EVERY role in <c>FailOverdueTasksAsync</c>, and both FAIL and
        /// report rather than re-spending money. Loop detection is
        /// <c>TaskProgressPolicy</c> / CARD-0153 (detection-only; no auto-escalate).</para>
        /// </summary>
        public AgentModelLevel? EscalateTo { get; set; }

        /// <summary>
        /// Minutes with no transcript progress before the automatic escalate sweep applies
        /// <see cref="EscalateTo"/>. Null (the shipped default on every role, including Debug
        /// after CARD-0158) disarms the sweep for that role — <see cref="EscalateTo"/> alone is
        /// the manual ladder. See <see cref="EscalateTo"/> for why 25 was retired and why
        /// re-arming inside the 88-minute healthy local-execution window is a false-positive trap.
        /// </summary>
        public int? EscalateAfterMinutes { get; set; }

        /// <summary>
        /// Hard wall-clock ceiling in minutes, from <c>DispatchedAt</c>, on a Dispatched or Working
        /// task of this role. Past it <c>AgentTaskDispatcher.FailOverdueTasksAsync</c> FAILS the
        /// task — it never escalates it, never kills its session and never retries it (CARD-0020
        /// S2). <c>&lt;= 0</c> turns the ceiling off for this role.
        ///
        /// <para><b>Dead config until CARD-0020 S2</b>: declared here, defaulted to 60, and read
        /// nowhere in <c>server/</c> or <c>src/</c> — so a task that started and then ran forever
        /// had no deadline of any kind, which is the one claim of CARD-0020 that survived
        /// measurement.</para>
        ///
        /// <para><b>240, and specifically not the 60 it used to declare.</b> On the live database 5
        /// of 247 successful tasks (2.0%) ran past 60 minutes and the longest Succeeded task ran
        /// 2 732 minutes, so enabling the old default would have killed real work on day one. 240
        /// is ~3x the measured p99 of 88.6 minutes: high enough that crossing it is evidence of a
        /// stall rather than of a big job, low enough that nothing sits open for two days
        /// unnoticed.</para>
        /// </summary>
        public int TimeoutMinutes { get; set; } = 240;

        /// <summary>
        /// Which agent program this role's tasks run on. UNSET everywhere on purpose (CARD-0084 S2):
        /// unset means <see cref="AgentKind.ClaudeCode"/>, so every role behaves exactly as it did.
        ///
        /// <para>It exists so that PROMOTING a role to Grok — after the real mileage §4 of the plan
        /// asks for — is a config edit and a restart, reversible the same way, rather than a code
        /// change. Same allowlist as an explicit request: a role configured to a kind that is not
        /// delegatable fails the task's creation loudly rather than silently running Claude.</para>
        /// </summary>
        public AgentKind? Kind { get; set; }

        /// <summary>
        /// In-flight recommendation for this role (CARD-0304 / CARD-0147). Global, not per-board.
        /// Null means unbounded. A configured value must be positive. Create refuses when this
        /// role's open (Queued/Dispatched/Working) count meets the number, unless
        /// <c>ignoreConcurrencyLimit</c>. Does not change <see cref="DelegationSettings.MaxConcurrentTasks"/>
        /// dispatch — the pipeline endpoint still reports whether the current in-flight count is
        /// at or over it.
        /// </summary>
        public int? RecommendedInFlight { get; set; } = 1;
    }

    /// <summary>One complete pair in <see cref="DelegationSettings.ComplexityChains"/>.</summary>
    public sealed class ComplexityCandidateSettings
    {
        public AgentKind Kind { get; set; }
        public AgentModelLevel Level { get; set; }
    }

    /// <summary>
    /// The configured advisory in-flight recommendation for <paramref name="role"/>, or null when
    /// that role has no <see cref="RolePolicy"/> entry (shipped <c>Custom</c> / <c>Check</c>).
    /// </summary>
    public int? RecommendedInFlightFor(AgentTaskRole role) =>
        RolePolicy.TryGetValue(role.ToString(), out var entry) ? entry.RecommendedInFlight : null;

    /// <summary>
    /// Working-session stall detection (CARD-0153). Detection only: a Warning incident and an
    /// attention row, never a kill, never an auto-escalate, never an auto-compact. The phase
    /// deadline owns "nothing landed"; this owns "rows keep landing and none of them is new".
    /// </summary>
    public StallDetectionSettings StallDetection { get; set; } = new();

    /// <summary>
    /// Create-time snapshot of the caller's LLM-routing env onto the child task (CARD-0260 S1).
    /// Names are opaque passthrough; Antiphon does not interpret <c>X_LLM_*</c>.
    /// </summary>
    public LlmEnvInheritanceSettings LlmEnvInheritance { get; set; } = new();
}

/// <summary>Knobs for <c>TaskProgressPolicy</c> / the ninth dispatcher clock (CARD-0153).</summary>
public sealed class StallDetectionSettings
{
    /// <summary>The whole feature's off switch. False and the sweep loads nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minutes without a novel transcript (or file) row before a working task is stalled.
    /// 30 is the measured "too long" figure from 2026-08-23; one config key to move.
    /// </summary>
    public int StallMinutes { get; set; } = 30;

    /// <summary>
    /// How far back the fingerprint window looks. Must exceed <see cref="StallMinutes"/> so a
    /// borderline verdict still contains the last novel row.
    /// </summary>
    public int LookBackMinutes { get; set; } = 45;

    /// <summary>
    /// Below this many rows in the look-back window the session is slow, not looping, and the
    /// phase deadline owns it. Default 6 is "at least one row every ~7 minutes" inside 45.
    /// </summary>
    public int MinRowsInWindow { get; set; } = 6;

    /// <summary>
    /// Re-raise the stall incident as Error (Critical if the owner is channel-bound) once the
    /// stall has lasted this long. Default 90 = the local-execution deadline, so a wedged task
    /// about to be failed by that clock has an Error row first. <c>&lt;= 0</c> disables the step.
    /// </summary>
    public int EscalateToErrorAfterMinutes { get; set; } = 90;
}

/// <summary>
/// CARD-0260/CARD-0263: which caller-env names a child task inherits and which of those names
/// mark a local key-proxy for the create-time project-marker gate.
/// </summary>
public sealed class LlmEnvInheritanceSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Opaque passthrough names copied from the caller's Antiphon-visible env layers.
    /// <c>X_LLM_*</c> are Mikey.LlmKeyProxy conventions, unknown to this code.
    /// </summary>
    public List<string> Names { get; set; } =
    [
        "X_LLM_PROJECT",
        "X_LLM_KEY",
        "ANTHROPIC_BASE_URL",
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_CUSTOM_HEADERS",
        "GROK_BASE_URL",
        "GROK_CLI_CHAT_PROXY_BASE_URL",
        "GROK_XAI_API_BASE_URL",
        "OPENAI_BASE_URL",
        "OPENAI_API_KEY",
    ];

    public string ProjectMarkerName { get; set; } = "X_LLM_PROJECT";

    public List<string> ProxyUrlNames { get; set; } =
    [
        "ANTHROPIC_BASE_URL",
        "GROK_BASE_URL",
        "GROK_CLI_CHAT_PROXY_BASE_URL",
        "GROK_XAI_API_BASE_URL",
        "OPENAI_BASE_URL",
    ];

    public List<string> ProxyHostMarkers { get; set; } = ["localhost", "127.0.0.1"];

    public bool RequireProjectAtProxy { get; set; } = true;
}

/// <summary>
/// Startup check for <see cref="DelegationSettings.RolePolicyEntry.RecommendedInFlight"/>:
/// null (unbounded) or a positive integer. Zero and negatives fail the host rather than
/// silently treating a nonsense cap as "no recommendation" (CARD-0304).
/// </summary>
public sealed class DelegationSettingsValidator : IValidateOptions<DelegationSettings>
{
    public ValidateOptionsResult Validate(string? name, DelegationSettings options)
    {
        var failures = new List<string>();
        if (options.LandSweepSeconds is < 1 or > 60)
        {
            failures.Add("Delegation:LandSweepSeconds must be between 1 and 60.");
        }

        if (options.LandMaxAttempts is < 1 or > 10)
        {
            failures.Add("Delegation:LandMaxAttempts must be between 1 and 10.");
        }

        if (options.MaxOpenTasks <= 0)
        {
            failures.Add("Delegation:MaxOpenTasks must be a positive integer.");
        }

        foreach (var (role, entry) in options.RolePolicy)
        {
            if (entry.RecommendedInFlight is { } value && value <= 0)
            {
                failures.Add(
                    $"Delegation:RolePolicy:{role}:RecommendedInFlight must be a positive integer or null (unbounded).");
            }
        }

        foreach (var (tier, candidates) in options.ComplexityChains)
        {
            if (!Enum.TryParse<TaskComplexity>(tier, ignoreCase: true, out var complexity)
                || !Enum.IsDefined(complexity))
            {
                failures.Add(
                    $"Delegation:ComplexityChains has unknown tier '{tier}'. Use Hard, Medium, or Easy.");
                continue;
            }

            if (candidates is null || candidates.Count == 0)
                continue;
            if (candidates.Count > 8)
            {
                failures.Add(
                    $"Delegation:ComplexityChains:{tier} has {candidates.Count} candidates; the maximum is 8.");
            }

            var seen = new HashSet<(AgentKind, AgentModelLevel)>();
            foreach (var candidate in candidates)
            {
                if (!AgentTaskService.DelegatableKinds.Contains(candidate.Kind))
                {
                    failures.Add(
                        $"Delegation:ComplexityChains:{tier} names {candidate.Kind}, which is not a delegate kind.");
                }

                if (!seen.Add((candidate.Kind, candidate.Level)))
                {
                    failures.Add(
                        $"Delegation:ComplexityChains:{tier} lists {candidate.Kind}/{candidate.Level} twice.");
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// CARD-0352 job 2: whether a successful card diagnosis writes labels or only the ledger.
/// </summary>
public enum DiagnoseLabelMode
{
    Apply = 0,
    Shadow = 1,
}
