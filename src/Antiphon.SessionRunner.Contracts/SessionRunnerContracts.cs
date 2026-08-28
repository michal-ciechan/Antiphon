namespace Antiphon.SessionRunner.Contracts;

public sealed record RunnerLaunchRequest(
    Guid SessionId,
    string Exe,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env,
    string Cwd,
    int Cols,
    int Rows,
    int MemoryLimitMb = 0,
    // When true the runner tails the agent's own session transcript and emits structured
    // SessionTranscript events. Which file and format is TranscriptFormat's job.
    bool TranscriptEnabled = false,
    // Which transcript the runner should tail (see TranscriptFormats). Null means Claude — the
    // only format that existed before this field, so an old server's requests keep their meaning.
    string? TranscriptFormat = null,
    // CARD-0160: which lane hosts the child. Null means pty-host — the only lane that existed
    // before this field, so an old server's requests keep their meaning. See SessionBackends.
    string? Backend = null,
    // Herdr-lane placement context, resolved by the server (the runner has no DB access).
    // Required when Backend == SessionBackends.Herdr; ignored otherwise.
    HerdrLaunchOptions? Herdr = null);

/// <summary>Values for <see cref="RunnerLaunchRequest.Backend"/> (CARD-0160).</summary>
public static class SessionBackends
{
    public const string PtyHost = "pty-host";
    public const string Herdr = "herdr";
}

/// <summary>
/// Herdr-lane placement context on <see cref="RunnerLaunchRequest"/> (CARD-0160). The server
/// resolves project/workspace identity; the runner treats <see cref="WorkspaceKey"/> as opaque.
/// </summary>
public sealed record HerdrLaunchOptions(
    // Stable grouping key for workspace-per-project: "project:<guid>", or "none" when the
    // session resolves to no project. The runner treats it as an opaque key.
    string WorkspaceKey,
    // workspace.create label — project name (or "Antiphon" for the catch-all).
    string WorkspaceLabel,
    // workspace.create cwd — project.LocalRepositoryPath; null for the catch-all workspace.
    string? WorkspaceCwd,
    // pane.rename / tab.create label — the agent's name the operator should see (CARD-0225),
    // never the shared TUI profile id. Independent of <see cref="AgentSlug"/>.
    string PaneTitle,
    // Herdr agent-manifest kind (see <see cref="HerdrAgentKinds"/>). Null keeps the pre-CARD-0187
    // meaning of "claude" so a new runner in front of an old server behaves exactly as today.
    string? AgentKind = null,
    // CARD-0211: Antiphon Agent.Slug; the runner applies it as the herdr agent name after
    // detection (sanitised to herdr's [a-z][a-z0-9_-]{0,31}, never stolen from a live agent).
    // Null = do not rename — an old server in front of a new runner launches exactly as today.
    string? AgentSlug = null);

/// <summary>Values for <see cref="RunnerLaunchRequest.TranscriptFormat"/>.</summary>
public static class TranscriptFormats
{
    /// <summary>Claude Code's per-cwd JSONL under <c>~/.claude/projects</c> (discovery + adoption rules apply).</summary>
    public const string Claude = "claude";

    /// <summary>
    /// Grok Build's ACP update stream, persisted live to
    /// <c>GROK_HOME/sessions/&lt;url-enc-cwd&gt;/&lt;session-id&gt;/updates.jsonl</c>. The path is
    /// DETERMINISTIC — we pass <c>--session-id</c> and grok honours it (measured 1.0.5, CARD-0080
    /// S1) — so none of the Claude discovery/claim/fork machinery applies.
    /// </summary>
    public const string Grok = "grok";

    /// <summary>
    /// Codex's rollout JSONL, <c>CODEX_HOME/sessions/YYYY/MM/DD/rollout-&lt;ts&gt;-&lt;uuid&gt;.jsonl</c>
    /// (CARD-0099 S1). Codex has no <c>--session-id</c> launch flag and the interactive TUI never
    /// prints its session id on screen, so unlike Grok the path must be DISCOVERED — under the
    /// full CARD-0006 rules (C1 claim, C2 <c>session_meta.cwd</c>, C3 first timestamp, C4 a prompt
    /// this session actually sent).
    /// </summary>
    public const string Codex = "codex";
}

public sealed record RunnerInputRequest(string Input);

public sealed record RunnerResizeRequest(int Cols, int Rows);

public sealed record RunnerSessionDto(
    Guid SessionId,
    int? Pid,
    DateTime StartedAt,
    string Status,
    int? ExitCode,
    string ExitReason,
    long LastSequence,
    // Pid of the detached pty-host process owning this session's ConPTY (null pre-split/unknown).
    int? HostPid = null,
    // True when this runner re-attached to a host that survived a previous runner's death.
    bool Adopted = false,
    // CARD-0161: herdr pane.get agent_status verbatim; null for pty sessions and older runners.
    // Only the literal "blocked" may gate delivery (done ≠ idle — measured 2026-08-23).
    string? AgentStatus = null,
    // CARD-0162: UTC when AgentStatus last CHANGED (hysteresis for corroboration). Null when
    // unobserved / pty / older runner. Additive — old servers ignore the extra JSON field.
    DateTime? AgentStatusSinceUtc = null,
    // CARD-0180 S4: whether a transcript is currently bound. Null on older runners / sessions
    // with no tailer (Raw). false = locating or refused; true = BoundTranscriptPath is set.
    bool? TranscriptBound = null,
    // CARD-0180 S4 / CARD-0181: bind method (`exact`/`sidecar`/`discovery`/`fork`/`deterministic`),
    // or null while unbound. Claim strength (Exact/Heuristic) is derived at claim time and is
    // not this field.
    string? TranscriptBindHow = null,
    // CARD-0190: why an unbound transcript has no path. Null when bound, on older runners, or
    // for deterministic tailers. Values: awaiting-input, locating, refused, missing.
    string? TranscriptUnboundReason = null,
    // CARD-0186 S2: which lane hosts the child. Null on older runners. Values: SessionBackends.
    string? Backend = null,
    // CARD-0186 S3: HerdrPendingReasons.Unreachable while adoption is waiting on herdr. Null otherwise.
    string? Pending = null,
    // CARD-0186 S3: single-session GET stamps this after a passing herdr liveness verify.
    // The list endpoint is cheap (no herdr calls) and reports the last stamp.
    DateTime? HerdrVerifiedAtUtc = null);

public sealed record RunnerBufferDto(
    Guid SessionId,
    string Buffer,
    long LastSequence);

public sealed record RunnerSnapshotDto(
    Guid SessionId,
    string RawOutput,
    string RenderedScreen,
    long LastSequence,
    DateTime StartedAt);

public sealed record RunnerOutputEvent(
    Guid SessionId,
    long Sequence,
    string Text);

public sealed record RunnerSessionStartedEvent(
    Guid SessionId,
    int? Pid,
    DateTime StartedAt);

public sealed record RunnerSessionExitedEvent(
    Guid SessionId,
    int? ExitCode,
    string ExitReason,
    long LastSequence);

/// <summary>
/// A restarted runner re-attached to a still-live pty-host (the session kept running the whole
/// time). Consumers should treat it as "still running, refresh buffers via the resync path" -
/// NOT as a fresh start.
/// </summary>
public sealed record RunnerSessionAdoptedEvent(
    Guid SessionId,
    int? Pid,
    DateTime StartedAt,
    long LastSequence);

/// <summary>
/// CARD-0162: herdr agent_status changed for a tracked pane. Additive SSE event — old servers
/// skip unknown names. PreviousAgentStatus is null for the first observation; lets the server
/// detect blocked-exit without holding its own per-session history.
/// </summary>
public sealed record RunnerAgentStatusEvent(
    Guid SessionId,
    // herdr agent_status verbatim (open vocabulary — consumers may only equality-match).
    string AgentStatus,
    string? PreviousAgentStatus,
    DateTime ObservedAtUtc);

/// <summary>
/// One normalized entry parsed from the agent's Claude Code JSONL session transcript.
/// <see cref="Sequence"/> is monotonic per session in file order (stable across re-tails of the
/// append-only file), so consumers can order and de-duplicate on (SessionId, Sequence).
/// </summary>
public sealed record RunnerTranscriptEvent(
    Guid SessionId,
    long Sequence,
    string Kind,
    string? Uuid,
    string? ParentUuid,
    DateTimeOffset? Timestamp,
    string? Role,
    string? Text,
    string? ToolName,
    string? ToolInput,
    string? ToolUseId,
    bool? ToolIsError,
    string? StopReason,
    // API-call attribution (assistant records only). ApiCallId is the Anthropic message id; the
    // several JSONL lines of one API call share it and repeat the SAME usage numbers, so consumers
    // must group by ApiCallId and count usage once — summing per line overcounts.
    string? ApiCallId = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? CacheReadTokens = null,
    int? CacheCreationTokens = null,
    // API-error stub evidence (CARD-0072): a turn killed by the API is written by Claude Code as a
    // synthetic assistant record carrying these top-level fields; the normalizer stamps them on the
    // stub's AssistantText and TurnEnd parts. Additive-optional ON PURPOSE — old runner → new
    // server deserializes nulls, new runner → old server ignores unknown members, so the detached
    // pty-hosts' shadow-copied binaries can lag with no lockstep deploy.
    bool? IsApiError = null,
    string? ApiErrorClass = null,
    int? ApiErrorStatus = null,
    // message.model on assistant records (CARD-0082). Additive-optional ON PURPOSE — same
    // old/new runner/server mix as the API-error fields above. Null on pre-carriage rows
    // and on API-error stubs (whose raw model is "<synthetic>", which is not a ceiling key).
    string? Model = null,
    // Grok turn_completed.usage.modelCalls (CARD-0157). Additive-optional ON PURPOSE — same
    // old/new runner/server mix. Null on pre-carriage rows and on every non-Grok kind.
    int? ModelCalls = null);

/// <summary>Full ordered transcript snapshot for a session (used for catch-up after a missed stream).</summary>
public sealed record RunnerTranscriptDto(
    Guid SessionId,
    IReadOnlyList<RunnerTranscriptEvent> Entries,
    long LastSequence);

/// <summary>Normalized transcript entry kinds (see <see cref="RunnerTranscriptEvent.Kind"/>).</summary>
public static class TranscriptKinds
{
    public const string UserPrompt = "UserPrompt";

    /// <summary>
    /// A prompt the TUI accepted into its own composer queue while busy and later submitted; it
    /// entered the conversation as an <c>attachment</c>/<c>queued_command</c> record, not as a
    /// <c>user</c> record.
    /// </summary>
    public const string QueuedUserPrompt = "QueuedUserPrompt";

    public const string AssistantText = "AssistantText";
    public const string Thinking = "Thinking";
    public const string ToolCall = "ToolCall";
    public const string ToolResult = "ToolResult";
    public const string TurnTitle = "TurnTitle";
    public const string TurnEnd = "TurnEnd";

    /// <summary>
    /// A context compaction boundary (Claude Code JSONL: type=system, subtype=compact_boundary —
    /// shape pinned by ClaudeCompactionCanaryTests / Fixtures/compact-boundary.jsonl). Never a turn
    /// end at the NORMALIZER level (no stop_reason is emitted), and never activity — compaction is
    /// housekeeping, not agent work. For the working/idle rules the trigger decides: a MANUAL
    /// boundary counts as a turn END (see <see cref="IsManualCompactBoundary"/>), an auto/unknown
    /// one stays pure housekeeping (CARD-0041).
    /// </summary>
    public const string CompactBoundary = "CompactBoundary";

    /// <summary>
    /// The trigger marker <c>TranscriptNormalizer.FromSystem</c> writes into a boundary's text
    /// ("Context compacted ({trigger})"). A MANUAL <c>/compact</c> only ever runs BETWEEN turns, so
    /// its boundary proves the previous turn is over and counts as that turn's end — nothing else
    /// ever will, since compaction makes no API call and writes no TurnEnd (live miss 2026-08-11,
    /// CARD-0041: a compacted session read "working" for two days and stranded its delegation
    /// brief). An AUTO boundary is the opposite: auto-compaction fires when a request starts over
    /// the context threshold, i.e. MID-turn, so treating it as an end would inject a WhenIdle
    /// message into a working composer and feed a false "proven idle" to the runner's CPU watchdog.
    /// A boundary with no trigger in its text (old rows) stays housekeeping — the conservative read.
    /// </summary>
    public const string ManualCompactMarker = "(manual)";

    /// <summary>True when a transcript entry is a MANUAL compaction boundary (see <see cref="ManualCompactMarker"/>).</summary>
    public static bool IsManualCompactBoundary(string? kind, string? text) =>
        kind == CompactBoundary
        && text is not null
        && text.Contains(ManualCompactMarker, StringComparison.Ordinal);

    /// <summary>
    /// Compaction writes a synthetic USER record carrying the summary of the conversation so far.
    /// Nobody typed it, NO TurnEnd follows it, and counting it as activity left a compacted session
    /// reading "working" forever with every WhenIdle delivery stranded (live miss 2026-08-11,
    /// CARD-0041). The real record carries <c>isCompactSummary: true</c>, but matching is by TEXT:
    /// the rule must also heal already-stored rows, and the client only ever sees the text.
    /// <para>The prefix stops where the wording starts varying, and that is MEASURED, not cautious:
    /// the 2026-08-11 live record read "…from a previous conversation THAT RAN OUT OF CONTEXT. The
    /// summary below covers…" while the 2026-08-13 canary capture read "…from a previous
    /// conversation. The summary below covers…" — same CLI family, same manual trigger. Do not
    /// lengthen this constant past the common prefix (fixture:
    /// tests/Antiphon.Tests/Agents/Fixtures/compact-full-manual.jsonl).</para>
    /// </summary>
    public const string CompactionContinuationPromptPrefix =
        "This session is being continued from a previous conversation";

    /// <summary>True when a transcript entry is a compaction continuation prompt (see <see cref="CompactionContinuationPromptPrefix"/>).</summary>
    public static bool IsCompactionContinuationPrompt(string? kind, string? text) =>
        kind == UserPrompt
        && text is not null
        && text.TrimStart().StartsWith(CompactionContinuationPromptPrefix, StringComparison.Ordinal);

    /// <summary>
    /// SERVER-SYNTHESIZED (never read from a JSONL): written when a session is relaunched and its
    /// persisted transcript still reads mid-turn — the old process died (reboot, crash, kill)
    /// before its TurnEnd could land, and nothing is running any more. Counts as a turn END for
    /// working/idle purposes, exactly like the interrupt marker: without it the relaunched
    /// session reads "working" forever and every WhenIdle delivery strands (live miss 2026-08-08,
    /// Antiphon-Opus stuck "Working" across a restart).
    /// </summary>
    public const string SessionRestartBoundary = "SessionRestartBoundary";

    /// <summary>
    /// The marker Claude Code writes into its JSONL as a USER message when a turn is aborted —
    /// "[Request interrupted by user]" (Esc) or "[Request interrupted by user for tool use]"
    /// (tool call rejected). An interrupted turn produces NO TurnEnd, so this marker IS the
    /// turn's end for working/idle purposes: treating it as activity left sessions permanently
    /// "working" and stranded every WhenIdle delivery (live miss 2026-07-29). Shape pinned
    /// against real Claude by ClaudeInterruptCanaryTests.
    /// </summary>
    public const string InterruptedPromptPrefix = "[Request interrupted";

    /// <summary>True when a transcript entry is an interrupt marker (see <see cref="InterruptedPromptPrefix"/>).</summary>
    public static bool IsInterruptPrompt(string? kind, string? text) =>
        kind == UserPrompt
        && text is not null
        && text.TrimStart().StartsWith(InterruptedPromptPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Local slash-commands (/model, /status, …) write their invocation and output into the JSONL
    /// as USER messages wrapped in these tags — and produce NO TurnEnd (no API call happens).
    /// They are housekeeping, not agent work: counting them as activity flipped the session to
    /// permanently "working" and stranded every WhenIdle delivery until the next real turn
    /// (live miss 2026-07-31: /model in the AZ Care session stranded a Telegram message).
    /// </summary>
    public const string LocalCommandPrefix = "<command-name>";
    public const string LocalCommandStdoutPrefix = "<local-command-stdout>";

    /// <summary>True when a transcript entry is a local slash-command record (see <see cref="LocalCommandPrefix"/>).</summary>
    public static bool IsLocalCommandRecord(string? kind, string? text)
    {
        if (kind != UserPrompt || text is null)
            return false;
        var trimmed = text.TrimStart();
        return trimmed.StartsWith(LocalCommandPrefix, StringComparison.Ordinal)
            || trimmed.StartsWith(LocalCommandStdoutPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The command a <see cref="IsLocalCommandRecord"/> wrapper invoked ("/compact"), read out of
    /// its <c>&lt;command-name&gt;</c> tag — or null when this is not such a record. The wrapper is
    /// the PROOF that the text Claude also recorded verbatim was a local command and not a prompt;
    /// see <see cref="IsRawLocalCommandEcho"/>.
    /// </summary>
    public static string? TryReadLocalCommandName(string? kind, string? text)
    {
        if (kind != UserPrompt || text is null)
            return null;
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith(LocalCommandPrefix, StringComparison.Ordinal))
            return null;

        var close = trimmed.IndexOf("</command-name>", StringComparison.Ordinal);
        if (close <= LocalCommandPrefix.Length)
            return null;
        var name = trimmed[LocalCommandPrefix.Length..close].Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// True when this USER record is the literal text a local slash-command was typed as. Claude
    /// records that raw line as a plain user record IN ADDITION to the
    /// <see cref="IsLocalCommandRecord"/> wrapper (CARD-0041), so a transcript carries the same
    /// <c>/compact …</c> twice in two different shapes.
    ///
    /// <para>Matching raw <c>/</c>-prefixed text ON ITS OWN stays rejected — a real prompt may begin
    /// with a slash — so this needs <paramref name="invokedCommands"/>: the command names actually
    /// read out of wrappers in the same span (<see cref="TryReadLocalCommandName"/>). A command that
    /// exists produces a wrapper and matches here; a prompt that merely starts with a slash produces
    /// no wrapper and cannot.</para>
    ///
    /// <para>SETTLEMENT ONLY — including the delivery watchdog that asks whether THIS task ever
    /// began (CARD-0077). No working/idle rule may consume it: there the manual compact boundary
    /// already outranks this record (CARD-0041), and reproducing the judgement in three lockstep
    /// implementations would be three chances to disagree. The watchdog is not a working/idle
    /// rule; it never asks whether the session is working, only whether a non-housekeeping
    /// UserPrompt landed after this task's dispatch.</para>
    /// </summary>
    public static bool IsRawLocalCommandEcho(
        string? kind, string? text, IReadOnlyCollection<string> invokedCommands)
    {
        if (kind != UserPrompt || text is null || invokedCommands.Count == 0)
            return false;
        if (IsLocalCommandRecord(kind, text))
            return false;

        var trimmed = text.TrimStart();
        foreach (var name in invokedCommands)
        {
            if (name.Length == 0 || !trimmed.StartsWith(name, StringComparison.Ordinal))
                continue;
            // "/compact" must not match a prompt that opens "/compacting is broken" — the
            // invocation ends at the command name or at whitespace before its arguments.
            if (trimmed.Length == name.Length || char.IsWhiteSpace(trimmed[name.Length]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when a transcript entry is part of an API-error stub — the ONE synthetic assistant
    /// record Claude Code writes when a turn is killed by the API itself (usage limit, 5xx,
    /// auth-expired), after its own retry is exhausted. Measured shape (23 real occurrences,
    /// CARD-0072 sweep; 2026-08-17 live misses: two limit-killed delegates settled their error text
    /// as a completed report): <c>message.model == "&lt;synthetic&gt;"</c>, top-level
    /// <c>error</c>/<c>isApiErrorMessage: true</c>/<c>apiErrorStatus</c>, the error string as an
    /// ordinary text block, and an ordinary <c>stop_sequence</c> TurnEnd of its own.
    ///
    /// <para>STRUCTURAL, unlike the three text-matching siblings above, because for once the raw
    /// record hands us real fields — and text matching is rejected outright: an agent legitimately
    /// WRITING ABOUT these errors must never trip it. <c>StopReason == "stop_sequence"</c> is also
    /// not a synonym (35 stored stop_sequence rows against 2 known stubs).</para>
    ///
    /// <para>NOT a working-rule input: the stub carries its own TurnEnd, so all three lockstep
    /// working/idle implementations already read idle with zero changes. This predicate is a
    /// consumer-side discriminator (settlement, reply dispatch, retry, display) only.</para>
    /// </summary>
    public static bool IsApiErrorStub(string? kind, bool? isApiError) =>
        isApiError == true
        && kind is AssistantText or TurnEnd;

    /// <summary>Claude Code's built-in subagent tool, as it appears in a ToolCall's tool name.</summary>
    public const string AgentToolName = "Agent";

    /// <summary>
    /// The head of the ToolResult Claude Code returns for a BACKGROUND <see cref="AgentToolName"/>
    /// spawn: "Async agent launched successfully. (This tool result is internal metadata …)". A
    /// synchronous Agent call returns the subagent's actual answer instead, so this string is what
    /// separates "the work is done" from "the work has not started" (pinned from session ac09cffd
    /// seq 11; the launch itself is invisible to Antiphon — the built-in tool is not a delegate).
    /// </summary>
    public const string AsyncAgentLaunchMarker = "Async agent launched";

    /// <summary>
    /// The USER record Claude Code writes when a background subagent finishes. It carries the
    /// subagent's whole report and is NOT a prompt anybody typed — a turn that answers one is still
    /// answering the brief that launched it.
    /// </summary>
    public const string TaskNotificationPrefix = "<task-notification>";

    /// <summary>True when a transcript entry is a background-subagent notification.</summary>
    public static bool IsTaskNotificationPrompt(string? kind, string? text) =>
        kind == UserPrompt
        && text is not null
        && text.TrimStart().StartsWith(TaskNotificationPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The <c>toolu_…</c> id a notification answers, read out of its <c>&lt;tool-use-id&gt;</c> tag.
    /// This is the STRONG link between a launch and its completion: pairing by id makes four
    /// launches and three notifications an unambiguous "one still running", where counting them
    /// would have to assume nothing else ever launches an agent in the same turn.
    /// </summary>
    public static string? TryReadNotifiedToolUseId(string? text)
    {
        if (text is null)
            return null;

        const string open = "<tool-use-id>";
        const string close = "</tool-use-id>";
        var start = text.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += open.Length;
        var end = text.IndexOf(close, start, StringComparison.Ordinal);
        if (end <= start)
            return null;

        var id = text[start..end].Trim();
        return id.Length == 0 ? null : id;
    }
}

/// <summary>
/// Exit reason strings the RUNNER synthesises (as opposed to the pty-host's PtyExitReason values,
/// which pass through verbatim). The server maps these onto its AgentExitReason enum by name, so
/// any string added here needs a matching enum member server-side to survive the parse.
/// </summary>
public static class RunnerExitReasons
{
    /// <summary>
    /// The CPU watchdog killed the session: its process was burning a core while the transcript
    /// said the turn had ended (idle TUI busy-loop, live incident 2026-08-08 — claude.exe pegged
    /// at ~110% of a core for 80+ CPU-minutes at an idle prompt). The work itself completed, so
    /// the server treats this as a clean stop and the session stays resumable.
    /// </summary>
    public const string CpuSpinKilled = "CpuSpinKilled";
}

/// <summary>
/// Exit reason strings the herdr lane synthesises (CARD-0186 S2). The server maps these onto
/// <c>AgentExitReason</c> by name — each constant here is the enum member's name. All four land
/// as Failed (never Stopped): Stopped is operator intent and is reconciliation's only auto-kill
/// arm. Replaces the four previously-unparsed literals that became <c>Unknown</c> on the wire.
/// </summary>
public static class HerdrExitReasons
{
    /// <summary>
    /// Adoption-time (and orphan) verdict: pane missing or our child pid is gone from
    /// <c>pane.process_info</c>. P7 measured this as the live herdr-restart shape (restored-empty
    /// pane, Claude dead with herdr).
    /// </summary>
    public const string RestartPresumedDead = "HerdrRestartPresumedDead";

    /// <summary>Runtime close: pump/liveness proved the pane is gone (child OS-dead).</summary>
    public const string PaneClosed = "HerdrPaneClosed";

    /// <summary>
    /// Herdr unreachable at runner restart AND the sidecar's ChildPid is OS-dead. Needs no socket.
    /// </summary>
    public const string ChildGone = "HerdrChildGone";

    /// <summary>
    /// Kill refused to <c>pane.close</c> because a foreign foreground process was present; our
    /// child was killed by pid and the pane left open (P8: herdr itself would have killed the
    /// foreign process — the refusal is ours).
    /// </summary>
    public const string PaneLeftOpen = "HerdrPaneLeftOpen";
}

/// <summary>
/// CARD-0186 S3: values for <see cref="RunnerSessionDto.Pending"/>. A pending session is listed
/// as Starting + Adopted; the liveness sweep re-runs the adoption bar until it reaches a verdict.
/// </summary>
public static class HerdrPendingReasons
{
    /// <summary>
    /// Runner restart (or mid-session herdr drop) could not reach herdr, and the sidecar's child
    /// is still OS-alive. Input returns 503 <see cref="HerdrProblemTypes.Unreachable"/>.
    /// </summary>
    public const string Unreachable = "HerdrUnreachable";
}

/// <summary>
/// CARD-0186 S3: RFC 9457 problem-details <c>type</c> values the runner emits on herdr faults.
/// </summary>
public static class HerdrProblemTypes
{
    /// <summary>
    /// 503 on <c>/input</c>, <c>/kill</c>, <c>/resize</c>, <c>/snapshot</c> when herdr is
    /// unreachable. The server maps this to a delivery deferral, never a kill.
    /// </summary>
    public const string Unreachable = "herdr_unreachable";
}

/// <summary>
/// The runner could not safely bind a transcript to a session (CARD-0006). Emitted INSTEAD of
/// silently adopting a file that might belong to somebody else — the live miss on 2026-08-09 bound
/// an agent to the human operator's own Claude conversation on nothing more than "same cwd, written
/// recently", and the only warning was one WRN line nobody watches.
/// </summary>
/// <param name="UnboundSeconds">
/// How long this session has been CONTINUOUSLY in this fault, in seconds — reset to zero the moment
/// the condition clears. CARD-0101: without it every repeat of an ongoing fault looks identical on
/// the wire, so a session unbound for three hours and one unbound for one minute produce the same
/// Warning row and nothing ever escalates (measured: 37 identical incidents over 180 minutes for
/// session 5409c537, all Warning, none reaching a human). The runner reports the elapsed FACT; the
/// server owns the threshold policy, so an older runner that sends 0 simply never escalates.
/// </param>
/// <param name="Repeat">
/// 1 for the first report of this continuous fault, incrementing on each rate-limited repeat.
/// Carried so the escalated incident can say how long it has been shouting.
/// </param>
public sealed record RunnerTranscriptFaultEvent(
    Guid SessionId,
    string Kind,
    string Detail,
    string? CandidatePath,
    double UnboundSeconds = 0,
    int Repeat = 1);

/// <summary>
/// A transcript was bound by HEURISTIC (cwd discovery / fork follow / migration shim) rather than
/// by the exact <c>&lt;session-id&gt;.jsonl</c> filename. Info-level audit: the binding passed every
/// adoption rule, but which file an agent is reading from should never be invisible.
/// </summary>
public sealed record RunnerTranscriptBoundEvent(
    Guid SessionId,
    string TranscriptPath,
    string How);

/// <summary>Values for <see cref="RunnerTranscriptFaultEvent.Kind"/>.</summary>
public static class TranscriptFaultKinds
{
    /// <summary>Candidate transcripts existed in this session's cwd but none proved it owned them.</summary>
    public const string AdoptionRefused = "AdoptionRefused";

    /// <summary>
    /// No transcript can be found at all — the projects root is missing/unreadable, the child
    /// exited after input was delivered without ever producing one, or the child is still alive
    /// with input delivered and zero cwd-matching candidates after the refusal window.
    /// </summary>
    public const string TranscriptMissing = "TranscriptMissing";

    /// <summary>A mid-session fork was detected but could not be attributed to this session.</summary>
    public const string ForkUnresolved = "ForkUnresolved";

    /// <summary>Two sessions were pointed at the same transcript (slice 2, SessionStart marker).</summary>
    public const string MarkerConflict = "MarkerConflict";

    /// <summary>
    /// CARD-0181: this session had been tailing a file whose basename is another session's id;
    /// the namesake reclaimed it. A one-off, not a continuing refusal.
    /// </summary>
    public const string ClaimRevoked = "ClaimRevoked";
}

/// <summary>How a transcript came to be bound (<see cref="RunnerTranscriptBoundEvent.How"/>).</summary>
public static class TranscriptBindMethods
{
    /// <summary>Claude honoured <c>--session-id</c>: the filename itself is the positive id.</summary>
    public const string Exact = "exact";

    /// <summary>Re-tail after a runner restart, from the runner's own transcript sidecar.</summary>
    public const string Sidecar = "sidecar";

    /// <summary>Cwd discovery of an id-forked transcript, positively identified by content.</summary>
    public const string Discovery = "discovery";

    /// <summary>Mid-session fork (e.g. <c>/clear</c>) followed.</summary>
    public const string Fork = "fork";

    /// <summary>Historical; never written since CARD-0181. Stored sidecars and incident rows may still carry it.</summary>
    public const string MigrationShim = "migration-shim";

    /// <summary>
    /// The path was computed, not discovered: Grok's session store is keyed by the
    /// <c>--session-id</c> we chose, so the transcript's location is known before launch.
    /// </summary>
    public const string Deterministic = "deterministic";
}

/// <summary>
/// What this runner's sessions are actually served by (<c>GET /capabilities</c>).
///
/// <para>The delivery ceilings are COUPLED to the pseudoconsole (CARD-0037): the inbox conhost
/// strips the bracketed-paste markers and clips every body at ~1 KB, the shipped modern pair
/// forwards them and takes 86 400 bytes in one write. The server sizes bodies against ONE of those
/// two envelopes, so it must be able to ASK rather than assume its own environment matches the
/// runner's — a server configured <c>modern</c> in front of an inbox runner would type bodies 48x
/// larger than that pty can carry, silently.</para>
/// </summary>
/// <param name="PtyBackend"><c>InboxConhost</c> or <c>ModernConPty</c> — the resolved value, not the request.</param>
/// <param name="PtyBackendRequested">The raw flag value that was resolved.</param>
/// <param name="PtyBackendReason">Human-readable explanation, always populated.</param>
/// <param name="PtyBackendFellBack">True when modern was asked for and the redistributable was not there.</param>
public sealed record RunnerCapabilitiesDto(
    string PtyBackend,
    string PtyBackendRequested,
    string PtyBackendReason,
    bool PtyBackendFellBack,
    // CARD-0112: formats this particular runner binary has a tailer branch for. Null means an
    // older runner cannot say; callers must treat that as no evidence, not as an omission.
    IReadOnlyList<string>? TranscriptFormats = null,
    // CARD-0112: additive so old/new server and runner binaries stay wire-compatible.
    RunnerBuildDto? Build = null,
    // CARD-0160: session backends this runner can host. Null = older runner = no evidence
    // (same contract as TranscriptFormats). A herdr launch is refused unless this contains "herdr".
    IReadOnlyList<string>? SessionBackends = null,
    // CARD-0179 R3: git SHA stamped at build time (InformationalVersion +SourceRevisionId).
    // Null on an older runner that predates the field.
    string? Version = null);

/// <summary>Build identity of the running session-runner process (CARD-0112).</summary>
public sealed record RunnerBuildDto(
    string InformationalVersion,
    string? CommitSha,
    DateTime AssemblyWriteTimeUtc,
    DateTime ProcessStartUtc);

public static class SessionRunnerEventNames
{
    public const string SessionStarted = "SessionStarted";
    public const string SessionAdopted = "SessionAdopted";
    public const string SessionOutput = "SessionOutput";
    public const string SessionExited = "SessionExited";
    public const string SessionError = "SessionError";
    public const string SessionHeartbeat = "SessionHeartbeat";
    public const string SessionTranscript = "SessionTranscript";
    public const string SessionTranscriptFault = "SessionTranscriptFault";
    public const string SessionTranscriptBound = "SessionTranscriptBound";
    // CARD-0162: herdr agent_status change. Additive — ParseEvent returns null on unknown names.
    public const string SessionAgentStatus = "SessionAgentStatus";
}
