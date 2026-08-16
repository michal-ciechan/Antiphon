using Antiphon.Agents.Pty;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Configuration for delegated agent tasks. The role→tier ladder lives here rather than in code so
/// the cost profile can be retuned without a deploy.
/// </summary>
public sealed class DelegationSettings
{
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>How many tasks may be Dispatched/Working at once across all roots.</summary>
    public int MaxConcurrentTasks { get; set; } = 6;

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

    /// <summary>
    /// The ceilings for one pseudoconsole. <see cref="PtyBackend.InboxConhost"/> — the default, and
    /// anything we are not sure about — returns exactly what shipped before CARD-0037.
    /// </summary>
    public PtyDeliveryCeilings CeilingsFor(PtyBackend backend, string reason) => backend switch
    {
        PtyBackend.ModernConPty => new PtyDeliveryCeilings(
            backend, ModernPtyBriefInlineMaxBytes, ModernPtyReplyInlineMaxChars,
            ModernPtySingleWriteMaxBytes, reason),
        _ => new PtyDeliveryCeilings(
            PtyBackend.InboxConhost, BriefInlineMaxBytes, ReplyInlineMaxChars,
            PtySingleChunkBytes, reason),
    };

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
        ["Plan"] = new() { Level = AgentModelLevel.Frontier },
        ["Code"] = new() { Level = AgentModelLevel.Frontier },
        ["Review"] = new() { Level = AgentModelLevel.Frontier },
        ["Debug"] = new() { Level = AgentModelLevel.High, EscalateTo = AgentModelLevel.Frontier, EscalateAfterMinutes = 25 },
        ["Coverage"] = new() { Level = AgentModelLevel.High },
        ["Merge"] = new() { Level = AgentModelLevel.High },
        ["Docs"] = new() { Level = AgentModelLevel.Medium },
        ["Commit"] = new() { Level = AgentModelLevel.Medium },
        // Low tier is safe for Test/Deploy because these RUN things and report what happened —
        // INTERPRETING a failure is a separate Debug task at High.
        ["Test"] = new() { Level = AgentModelLevel.Low, EscalateTo = AgentModelLevel.Medium },
        ["Deploy"] = new() { Level = AgentModelLevel.Low },
    };

    public AgentModelLevel DefaultLevel { get; set; } = AgentModelLevel.High;

    /// <summary>
    /// A Dispatched task whose session has ZERO transcript entries after this long never received
    /// its brief — it fails loudly instead of sitting Dispatched forever (CARD-0003/CARD-0020).
    /// Long enough for the stranded-queue watchdog (60s cadence) to redeliver a reverted brief
    /// several times first; a genuinely slow FIRST TURN still counts as started the moment any
    /// transcript entry lands, so this never fires on slow work.
    /// </summary>
    public int DeliveryFailTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// How long settlement waits for the turn-ending response's OWN text before giving up and
    /// settling on whatever the turn produced (CARD-0046).
    ///
    /// Claude Code writes one API response as several JSONL records — a signature-only
    /// <c>thinking</c> record, then the <c>text</c> record — and stamps EVERY one with the
    /// response's <c>stop_reason</c>, so the first thing that reaches us is a BARE TurnEnd. The wait
    /// is closed by IDENTITY (both records share one <c>message.id</c> → <c>ApiCallId</c>), and this
    /// is only the backstop for a response that never writes text at all: 1 in 180 in the measured
    /// corpus, an <c>end_turn</c> thinking record followed by <c>API Error: Connection lost
    /// mid-response</c>. Without the backstop such a task would sit Dispatched forever.
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

    // ── Scheduled check-ins on a running delegate (CARD-0047) ───────────────────────────────────
    //
    // An orchestrator hears nothing between dispatch and the report. These five knobs decide when a
    // deterministic probe of the delegate's state is gathered and delivered back to the caller.
    // None of them is a deadline: no code path fails, escalates or cancels a task off any of them.

    /// <summary>The whole feature's off switch. False and no task is ever armed or swept.</summary>
    public bool CheckEnabled { get; set; } = true;

    /// <summary>
    /// <see cref="AgentTask.ExpectedDurationMinutes"/> for a caller that declared nothing. Ten
    /// minutes is roughly the median delegated task here, so an undeclared task still gets one
    /// early check instead of silence.
    /// </summary>
    public int DefaultExpectedMinutes { get; set; } = 10;

    /// <summary>
    /// Floor on the gap between checks. The backoff starts at
    /// <c>max(this, ExpectedDurationMinutes / 2)</c>, so a task declared at 2 minutes still cannot
    /// generate a check a minute.
    /// </summary>
    public int CheckMinIntervalMinutes { get; set; } = 5;

    /// <summary>Ceiling on the doubling — a long task settles into a half-hourly heartbeat.</summary>
    public int CheckMaxIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// After this many checks the task stops being checked and the last note says so. At ~$0.01 a
    /// check the economics never bind; this exists so a forgotten immortal task doesn't check
    /// forever.
    /// </summary>
    public int CheckMaxCount { get; set; } = 10;

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

    /// <summary>A sub-orchestrator decomposes, which is expensive thinking — never below this.</summary>
    public AgentModelLevel MinOrchestratorLevel { get; set; } = AgentModelLevel.High;

    /// <summary>
    /// Arm the PreToolUse deny hook (block Edit/Write — "delegate this instead") in each
    /// orchestrator's worktree by default. Per-task <c>DenyDirectEdits</c> overrides this either
    /// way. The hook is only ever written into a task's OWN worktree, never a shared directory.
    /// </summary>
    public bool OrchestratorDenyHookEnabled { get; set; } = true;

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
        public AgentModelLevel? EscalateTo { get; set; }
        public int? EscalateAfterMinutes { get; set; }
        public int TimeoutMinutes { get; set; } = 60;
    }
}
