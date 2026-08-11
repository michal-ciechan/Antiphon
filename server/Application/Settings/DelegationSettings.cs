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
    /// </summary>
    public decimal MaxCostUsdPerRoot { get; set; } = 5.00m;

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
