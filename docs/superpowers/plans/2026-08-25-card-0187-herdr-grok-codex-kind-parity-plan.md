# CARD-0187 — herdr for Grok and Codex agents: Kind parity — investigate + design

**Date:** 2026-08-25 · **Card:** CARD-0187 (`a6cd2c19-1c56-4e22-b6a4-4b8e181c4d1f`) ·
**Status:** design (no implementation in this pass) ·
**Verified against:** `master` @ `02df16f` (CARD-0186 S1–S4 all landed). Every file:line below was
re-read out of the code on that commit; the card's own citations were written before CARD-0186 and
are re-derived here rather than trusted. **Herdr was probed live** — eight probes against the
operator's running herdr 0.8.2, recorded in
[docs/investigations/2026-08-25-herdr-kind-launch-probes-CARD-0187.md](../../investigations/2026-08-25-herdr-kind-launch-probes-CARD-0187.md)
(K1–K8; raw JSON under `.antiphon/herdr-probes-card0187/`). Referenced below as **K*n***.

**Established facts, not re-derived here:**
- CARD-0186's plan (`2026-08-25-card-0186-herdr-always-on-and-channel-bound-parity-plan.md`) and
  its four slices: `HerdrExitReasons` (`SessionRunnerContracts.cs:481`), the OS-pid adoption bar
  (`SessionRunnerRuntime.cs:465-553`), pending adoption + `/input` 503 deferral
  (`SessionMessageQueueService.cs:848-856`, `:1059-1075`, `:1385`), `HerdrUnreachable` /
  `HerdrPaneLeftOpen` incidents, own-child-only kills (`HerdrPaneChild.cs:188-232`). **All of it is
  Kind-agnostic — confirmed by reading**: none of those paths reads `AgentKind`, `Exe`, or a
  transcript format; they key on pane id, sidecar pid + start time, `Backend`, and `Pending`.
  Nothing here duplicates them.
- CARD-0160/0161/0162/0164 (lane, ceilings on `SessionBackend`, events-as-triggers,
  unobservable-baseline confirm) — shipped; `docs/herdr-sessions.md` is current against them.
- CARD-0182: the operator's Grok profile shape is a **wrapper** — `Exe = pwsh.exe`,
  `Args = -NoProfile -ExecutionPolicy Bypass -File …\gkp.ps1`, blank model argument
  (`docs/ai-agent-tui-configuration.md:76-90`). `gkp.ps1`/`cxp` are **not on this machine**
  (`.antiphon/card-0182-close-reason.md`; `Get-Command gkp,cxp,clproxy` → nothing).
- CARD-0056's constraint outranks everything below: a kill needs positive identity of the thing
  being killed; unclaimed never implies kill.

**Related:** CARD-0186 (companion, shipped), CARD-0163 (`pane.report_agent` / badges, deferred
S4b), CARD-0195 (Codex MCP-boot hang — open, testing caveat §7), CARD-0190 (Codex binds only after
its first prompt — open, unchanged on this lane), CARD-0167 (Codex `-c` proxy args through the
agent path — open, gates one canary shape in §8).

---

## 0. The finding that reshapes the card: `agent.start` is the wrong primitive, for Claude too

The card frames the work as "map Kind → herdr kind and let `agent.start` run grok/codex". The
live probes say `agent.start` cannot carry Antiphon's launches at all:

1. **It is a typed shell line.** herdr types
   `$p=Start-Process -FilePath <canonical exe> -ArgumentList '<args>' -NoNewWindow -Wait -PassThru`
   into the pane's Windows PowerShell 5.1 (K2/K3/K8). The schema has `name`, `kind`, `pane_id`,
   `args[]`, `timeout_ms` — **no exe, no env** (K3).
2. **It refuses a newline in any argument** — `invalid_agent_argument` (K4). Every
   standing-instruction bundle is a multi-line argument (`--append-system-prompt` at
   `AgentControlService.cs:281`, `AgentTaskDispatcher.cs:2057`, Codex `developer_instructions` at
   the same sites), so **a Claude herdr launch that carries a composed bundle fails today**. The
   CARD-0168 herdr canary passes only because it launches with `["--dangerously-skip-permissions"]`
   (`ClaudeHerdrRealCliStubProxyCanaryTests.cs:107-116`). CARD-0186's AlwaysOn lift made the
   defect reachable by any AlwaysOn Claude agent with instructions.
3. **It cannot launch Codex on this Windows machine** — `Start-Process -FilePath codex` picks the
   extensionless npm shim and dies (`%1 is not a valid Win32 application`), then times out after
   90 s (K2). Antiphon's catalogue uses `codex.cmd` for exactly this reason
   (`docs/agent-kinds.md` §2 table).
4. **It cannot run a wrapper** (`gkp.ps1`, a pinned profile `Exe`) — canonical exe only (K3). The
   operator's own convention is the wrapper, never the bare binary.

Meanwhile the shape the operator already uses by hand works for every case: type a command into
the pane, let herdr's passive detection pick the agent up (`pane.get.agent` / `agent.get <paneId>`
/ `agent.wait <paneId>` all work with no `agent.start` and no name — K5/K6/K7), read the child pid
from `pane.process_info`. Through a **launch script** the typed line is ~60 characters and the
argument vector reaches the child byte-identical, newline included (K7). The foreground process
list named exactly our child in every shape measured (K5/K6/K7), so CARD-0186's own-child kill
discipline transposes unchanged.

So this card replaces the launch primitive rather than parameterising it, and in doing so fixes
the Claude lane's bundle defect as a side effect.

## 1. Verdict up front — the decisions

1. **The Kind gate becomes a supported-set check with one owner.** `ValidateSessionBackendPairing`
   (`AgentService.cs:1267-1283`) is rewritten to `(SessionBackend backend, AgentKind kind)` — the
   `alwaysOn`/`channelBound` parameters CARD-0186 left in place "until CARD-0187 rewrites the
   function" are deleted now. Allowed = `HerdrAgentKinds.TryMap(kind, out _)` succeeds:
   `ClaudeCode`, `Grok`, `Codex`. `OpenCode`/`Raw` stay **refused** with `herdr_refused`, the
   message naming the kind and the supported list. Same five sites keep calling it (create
   `:292`, post-profile `:346`, PATCH `:405`, `ChatChannelService.cs:60`,
   `AgentSessionService.EnsureHerdrLaunchAllowed` `:1122-1163`); the PATCH site drops its now-dead
   `finalAlwaysOn`/`channelBound` resolution (`:399-404`). §3.
2. **Kind → herdr kind lives in ONE place, and it is a contracts type.** `HerdrAgentKinds`
   moves from `src/Antiphon.SessionRunner/HerdrApiModels.cs:14-17` to
   `Antiphon.SessionRunner.Contracts` and gains `Grok = "grok"`, `Codex = "codex"`, plus
   `Supported` and `IsSupported(string)`. The server adds the enum edge in the same file's
   neighbour (`server/Application/Services/HerdrAgentKindMap.cs`: `TryMap(AgentKind) →
   string?`), which is what the gate, the launch spec and the wire field all call. No second
   table anywhere; a test pins that every `AgentKind` member is either mapped or explicitly in a
   `Refused` list. §3.
3. **The wire carries the kind.** `HerdrLaunchOptions` (`SessionRunnerContracts.cs:36-45`) gains
   `string? AgentKind = null` — **null keeps the old meaning, `"claude"`**, so a new runner in
   front of an old server behaves exactly as today. The runner validates the value against
   `HerdrAgentKinds.IsSupported` at `StartAsync`'s request validation (`SessionRunnerRuntime.cs:110-126`)
   and throws `ArgumentException` (→ 400) for anything else — never a silent Claude. §4.
4. **One launch shape for every kind: a launch script typed into the pane.** `HerdrPaneChild.LaunchAsync`
   (`HerdrPaneChild.cs:75-152`) stops calling `agent.start` (`:110-118`). It writes
   `<SessionLogPath>/herdr/<sessionId:N>.launch.ps1` — `& '<request.Exe>' @('<arg>', …)` with
   `'` doubled, UTF-8 **with BOM** (PS 5.1 reads a BOM-less file as CP1252 and a bundle's em-dash
   would become a smart quote — AGENTS.md's own warning) — types `& '<path>'` via
   `pane.send_text` + `pane.send_keys ["enter"]` (both already wrapped, `HerdrClient.cs:283-304`),
   then polls `pane.get` until `Agent == expected kind`, then reads `pane.process_info` for
   `ChildPid`, writes the sidecar, deletes the script. **Env never enters the script** — it stays
   on `tab.create`/`pane.split` env (`HerdrPaneChild.cs:349-356`, probe P6), because a script on
   disk carrying a resolved `{{key:…}}` value is a leak the pty lane never has; args are already
   process-listing-visible by design (`AgentSessionService.cs:1101-1114`). `agent.start` is kept
   in `HerdrClient` for probes/tests but has no production caller. **Caller may overrule** toward
   "keep `agent.start` when eligible (canonical exe, no newline, not codex-on-windows)"; the
   alternative is rejected here because the eligibility predicate is exactly the set of facts that
   drifts, and two launch paths are two sets of failure modes to keep pinned. §4.
5. **Readiness is `pane.get.agent`, never `agent.wait`, and a wrong agent is a launch failure.**
   `agent.wait <paneId>` returns `agent_not_found` until detection has happened (K7). The poll
   (250 ms, bound `HerdrSettings.LaunchDetectTimeoutMs`, default 60 000 — K1 took 4.4 s, K5
   under 10 s, K2's failure took the full 90 s, and CARD-0195's MCP boot is the outlier) ends in
   exactly one of: `Agent == expected` → continue; `Agent` is some **other** kind → fail
   `"herdr detected '{actual}' where '{expected}' was expected"` (the profile's exe and the
   agent's Kind disagree — refuse, do not adopt); timeout → fail. Every failure runs the existing
   `StartHerdrAsync` catch (`SessionRunnerRuntime.cs:1003-1015`, `KillAsync` then dispose) and
   surfaces as the launch exception. Blocked-at-startup (Codex trust prompt, Grok device-code
   login) is **not** handled here — the per-kind adapters already own it
   (`RunnerCodexAdapter.cs:290-305`, `ProviderContractCatalog.cs:127-131`) and they read the
   runner's snapshot, which on herdr is `pane.read` (`HerdrPaneChild.cs:246-256`). §4.
6. **The pane shell is checked, not assumed.** Before typing, `pane.process_info.shell_pid` must
   resolve to `powershell.exe` or `pwsh.exe` (K8: herdr's Windows default is Windows PowerShell 5.1
   with a prompt hook). Anything else is an explicit `HerdrLaunchException("pane shell '{name}' is
   not PowerShell; set herdr default_shell or use PtyHost")` — never a typed line into a shell
   whose quoting we did not measure. §4.
7. **The transcript tailer on herdr is the pty selection, extracted — three hard-codes go.**
   `StartTranscriptTailer` (`SessionRunnerRuntime.cs:1019-1050`) always writes
   `Format = TranscriptFormats.Claude` (`:1036`) and always builds `TranscriptTailer` (`:1039`),
   ignoring `request.TranscriptFormat`; `AdoptHerdrAsync` (`:1588-1651`) always rebuilds
   `TranscriptTailer` (`:1637`) and never reads `TranscriptSidecar.Format` the way the pty adopt
   path does (`:1318-1350`). Both are replaced by two extracted methods the pty lane already
   contains in-line: `StartTailerFor(request, childStartUtc)` (the `:1121-1210` switch) and
   `RestoreTailerFromSidecar(sidecar, cwd, childStartUtc)` (the `:1318-1365` switch). Grok's
   deterministic path resolves from `request.Env` + cwd + session id exactly as on pty
   (`GrokTranscriptTailer.ResolveUpdatesPath`, K1 proves `--session-id` survives herdr); Codex's
   discovery runs C1–C4 over `CODEX_HOME/sessions` with the input log that
   `RunnerSession.WriteAsync` already feeds before every herdr write (`:1775-1790`). §5.
8. **Delivery needs no new verdicts; it needs the per-kind ceiling rule and one measurement.**
   CARD-0055/0164 confirmation is `TranscriptEntries` + `PromptSubmissionMatch`, kind-agnostic; the
   Grok/Codex prompt shapes are already `DeliveryVerification: Supported` in
   `ProviderContractCatalog.cs:106-108` / `:171-173`. The Grok join-safe rule
   (`PtyDeliveryCeilings.ForAgentKind`, `:99-117`) already applies to whatever ceilings the
   dispatcher resolves — including herdr's (`AgentTaskDispatcher.cs:1766-1767`,
   `AgentTaskReplyService.cs:355`) — so Grok briefs spill on herdr as on pty with no change.
   What is **unmeasured** is the 86 400 B single-write envelope for a Grok/Codex composer through
   `pane.send_text` (CARD-0161 measured Claude only). S3 measures it (probe D1, §8); until then
   the herdr ceilings for Grok/Codex are the herdr set with `ForAgentKind` applied, and the
   tripwire keeps `OversizedTerminalDelivery` in front of any surprise. §6.
9. **Working/idle and turn-end flush are the tailer's job — decision 7 is the whole fix.** With
   the right tailer, Grok's `turn_completed` and Codex's `event_msg/task_complete` land as
   `TurnEnd` rows (`ProviderContractCatalog.cs:101-105`, `:166-170`) and `IsWorkingAsync`,
   `FlushQueueOnIdleAsync` and `HerdrStatusCorroborationService` (`:55`, backend-only filter) need
   nothing. Two open Codex facts are inherited, not caused: a Codex session binds only after its
   first prompt (CARD-0190) and its MCP boot can swallow the boot prompt (CARD-0195). Both are
   stated as test caveats (§7) and pinned as skip-not-fail in the canary. §7.
10. **Kill semantics are unchanged and now measured for the new shapes.** `KillAsync`'s
    foreign-process guard (`HerdrPaneChild.cs:195-217`) compares the foreground list against
    `ChildPid`/`ShellPid`; the list is exactly our child under a `pwsh` wrapper (K6) and is the
    `cmd.exe` for a `.cmd` launcher (K5). `ChildPid` therefore names the wrapper's leaf or the
    `cmd.exe`, and `Kill(entireProcessTree: true)` (`:238`) already covers the node leaf under
    `codex.cmd`. The sidecar's `ChildPid` doc comment ("Claude's pid", `HerdrPaneSidecar.cs:23`)
    becomes "the agent child's pid". §4.
11. **Scope is three build slices, and S1 ships alone safely.** S1 (runner: launch script,
    tailer-per-format, adopt re-tail, wire field) changes no policy and fixes the Claude bundle
    defect; S2 (server: gate, map, wire, docs, client copy) unlocks Grok/Codex onto a mechanism
    that already works; S3 (real-CLI herdr canaries per kind + the delivery measurement + the
    kind-parametrised parity smoke) is the evidence. Order S1→S2→S3; S2 must not ship before S1
    (it would unlock Codex onto `agent.start`, which K2 proves cannot launch it). §8.

## 2. What is genuinely Claude-specific on the lane today (the four hard-codes)

| # | Hard-code | Where | Fixed by |
|---|---|---|---|
| H1 | `agent.start` with `HerdrAgentKinds.Claude` | `HerdrPaneChild.cs:110-118` | decision 4 (primitive replaced) |
| H2 | "request.Exe is not the stock claude launcher" warning, which is the only place the exe is looked at | `HerdrPaneChild.cs:83-89` | decision 4 (the exe is now what gets launched) |
| H3 | Launch tailer: `Format = Claude`, `new TranscriptTailer` regardless of `request.TranscriptFormat` | `SessionRunnerRuntime.cs:1019-1050` | decision 7 |
| H4 | Adopt re-tail: `new TranscriptTailer` regardless of `TranscriptSidecar.Format` | `SessionRunnerRuntime.cs:1633-1648` | decision 7 |

And one in the server, H0: the Kind arm of `ValidateSessionBackendPairing`
(`AgentService.cs:1277-1282`) plus the failure text at `AgentSessionService.cs:1153`
("Herdr launch refused: non-Claude agent."). Everything else on the lane reads pane id, pid, or
backend. The `FindArgValue(request.Args, "--name")` at `:1024` is Claude's `--name` and moves
with the extracted tailer selection (it is only read on the Claude branch, `:1180-1181`).

## 3. Gate, map, wire (decisions 1–3)

**Contracts** (`Antiphon.SessionRunner.Contracts`, so both processes share the strings):

```csharp
public static class HerdrAgentKinds
{
    public const string Claude = "claude";   // CARD-0160 P4
    public const string Grok   = "grok";     // CARD-0187 K1
    public const string Codex  = "codex";    // CARD-0187 K5
    public static IReadOnlyList<string> Supported { get; } = [Claude, Grok, Codex];
    public static bool IsSupported(string? kind) => kind is null || Supported.Contains(kind, Ordinal);
}
public sealed record HerdrLaunchOptions(string WorkspaceKey, string WorkspaceLabel,
    string? WorkspaceCwd, string PaneTitle,
    string? AgentKind = null);   // null = Claude (wire compat)
```

**Server** — `HerdrAgentKindMap.TryMap(AgentKind kind, out string herdrKind)`: `ClaudeCode`→
`claude`, `Grok`→`grok`, `Codex`→`codex`; `OpenCode`/`Raw` → false. `ValidateSessionBackendPairing`
becomes:

```csharp
internal static void ValidateSessionBackendPairing(SessionBackend backend, AgentKind kind)
{
    if (backend != SessionBackend.Herdr) return;
    if (!HerdrAgentKindMap.TryMap(kind, out _))
        throw new ConflictException(
            $"Herdr sessions are not available for {kind} agents (supported: ClaudeCode, Grok, Codex).",
            "herdr_refused");
}
```

`BuildRuntimeLaunchSpecAsync` (`AgentSessionService.cs:1064-1116`) sets
`HerdrLaunchOptions.AgentKind = HerdrAgentKindMap.TryMap(session.AgentKind)` — the session row's
kind, the same value the tailer format is derived from (`SessionRunnerHttpClient.cs:147-154`), so
the two cannot disagree. `EnsureHerdrLaunchAllowed` keeps its shape; `FailureReason` becomes
`"Herdr launch refused: {kind} is not supported on herdr."`. Its `alwaysOn`/`channelBound`
resolution (`:1139-1146`) is deleted with the parameters.

**Client.** `AgentSettingsModal.tsx` has no Kind↔backend coupling (grep: `sessionBackend` only at
`:76`, `:124`, `:187`, `:279-287`) — nothing to change; the option description in
`client/src/api/agents.ts:46` stays. A Raw/OpenCode agent choosing Herdr gets the 409 it gets
today, with the new message.

**Docs.** `docs/herdr-sessions.md` §1 table row → "`OpenCode` / `Raw` — no structured transcript,
screen-only lanes are not hosted"; §4 launch sequence rewritten to the script shape; §5 gains the
per-kind ceiling sentence and the unmeasured-envelope caveat; §8 gains rows for `wrong agent
detected`, `pane shell is not PowerShell`, `detection timeout`; §9 drops the Kind line.
`SessionBackend.cs:10-12` doc comment; `AGENTS.md`'s CARD-0160 bullet gets one sentence ("CARD-0187
lifted the Kind refusal for Grok and Codex; OpenCode/Raw stay refused; herdr launches type a launch
script, never `agent.start`"); `docs/agent-kinds.md` §1 table gains a "herdr?" column.

**Tests to flip / add** (`AgentSessionBackendTests`): `herdr_on_non_claude_is_refused` →
`herdr_on_grok_and_codex_is_allowed` + `herdr_on_opencode_and_raw_is_refused_naming_the_kind`;
`patch_kind_to_raw_on_a_herdr_agent_is_refused` (the request-final-Kind resolution at `:401`
must still run); `channel_bind_onto_a_grok_herdr_agent_is_allowed`. `SessionRunnerHttpClient`
test: a Grok spec on Herdr posts `Herdr.AgentKind == "grok"` and `TranscriptFormat == "grok"`; a
Claude spec posts `AgentKind == "claude"` and `TranscriptFormat == null` (the CARD-0112 old-runner
contract at `:152-153` is untouched). `HerdrAgentKindMapTests`: every `AgentKind` member is
mapped or listed refused — a new enum member fails the build's test, not a live launch.

## 4. The launch shape (decisions 4–6, 10)

`HerdrPaneChild.LaunchAsync`, new sequence (unchanged steps in plain text, new in **bold**):

1. `ConnectAndValidateAsync` → ensure workspace → allocate pane (`tab.create` / `pane.split`,
   **env = `request.Env` on both, unchanged code at `:349-356`**) → `pane.rename` →
   `pane.report_metadata`.
2. **Shell check** (decision 6): `pane.process_info` → `shell_pid` → `ForegroundProcesses` is empty
   at this point, so the shell is read from the process by pid (`Process.GetProcessById(shell).ProcessName`
   — local, herdr is on this machine) — `powershell`/`pwsh` or refuse.
3. **Write the launch script** `<SessionLogPath>/herdr/<sessionId:N>.launch.ps1` (BOM, `'` → `''`,
   one element per `request.Args` entry, `& '<exe>' @(...)`). The exe is `request.Exe` verbatim —
   `AgentExecutableResolver` already resolved it on the server (`AgentTuiLaunchResolver.cs:388-389`),
   so it is `…\codex.cmd`, `…\grok.exe`, `pwsh.exe`, or the operator's wrapper. PowerShell's `&`
   runs a `.cmd` through `cmd.exe` (K5's foreground entry).
4. **Type** `& '<script path>'` via `pane.send_text`, then `pane.send_keys ["enter"]`.
5. **Poll `pane.get`** every 250 ms until `Agent == expected` (K1/K5/K6/K7 all detect on the
   OSC-title rule within 10 s) or `LaunchDetectTimeoutMs`. Another kind → fail; timeout → fail
   (decision 5). On failure the script is **left in place** for diagnosis and the existing catch
   kills/disposes.
6. `pane.process_info` → `ChildPid` = first foreground entry (measured: the leaf under a wrapper,
   `cmd.exe` for `.cmd`), `ShellPid`; `LaunchedAtUtc`; sidecar (`HerdrPaneSidecar` gains
   `string? AgentKind` for the operator's benefit and for the S3 corroboration check — optional,
   nothing reads it on the hot path); **delete the script**; return `ChildStarted`.

`RunnerLaunchRequest.Exe` stops being decorative on this lane. The `Cols/Rows/MemoryLimitMb`
warning (`:91-95`) stays.

Why not `pane.send_input` (text + keys in one call, in the schema but not wrapped): it is
equivalent to the two calls we have, and the two-call shape is what every existing herdr test and
the queue's delivery path already exercise. Not worth a new wrapper for one call site.

Why a script and not a typed command line with the args inline: K4 shows a 9.7 KB argument typed
through the shell is fine, but a bundle is up to `CommandLineBudgetChars` = 30 000 chars
(`DelegationSettings.cs:272`) with newlines, and PS 5.1's console line editor is exactly the kind
of undocumented ceiling this codebase has paid for elsewhere (CARD-0027/0028). The script keeps
the typed line constant-length regardless of the launch.

## 5. Transcript, working/idle, adopt (decisions 7, 9)

`StartHerdrAsync` (`SessionRunnerRuntime.cs:955-1017`) replaces its `StartTranscriptTailer(request,
started.ChildStartUtc)` call (`:1002`) with `StartTailerFor(request, started.ChildStartUtc)`, the
extracted body of `StartAsync`'s `:1121-1210` — same three branches (Grok deterministic sidecar +
`GrokTranscriptTailer`; Codex sidecar + `CodexTranscriptTailer` with `ResolveSessionsRoot(request.Env)`;
Claude sidecar + `TranscriptTailer` with `--name`/resume detection), same `IsResumeLaunch` /
`IsCodexResumeLaunch` rules. `AdoptHerdrAsync` (`:1588-1651`) replaces its `:1633-1648` block with
`RestoreTailerFromSidecar(sidecar, cwd, childStartUtc)`, the extracted `:1318-1365` switch, with
the same "no sidecar / no path ⇒ Claude discovery under sidecar rules" tail. `TranscriptSidecar.Format`
already carries the format (`TranscriptSidecar.cs:44-48`); nothing new is persisted.

Per-kind consequences on herdr, all inherited from the pty lane and none new:

| Kind | Transcript bind on herdr | Working/idle + flush | Startup modal |
|---|---|---|---|
| Claude | C1–C4 discovery over `~/.claude/projects/<enc-cwd>` (S1 spike proved the cwd-keyed JSONL) | `TurnEnd` / interrupt / manual `CompactBoundary` | trust dialog auto-answered by the adapter (CARD-0047) |
| Grok | deterministic `GROK_HOME/sessions/<enc-cwd>/<id>/updates.jsonl` — K1: dir exists before the first prompt | `turn_completed` → `TurnEnd` (CARD-0080 S2) | device-code login is FailFast (adapter) — herdr will also show `blocked`, which only defers |
| Codex | discovery under `CODEX_HOME/sessions` with C1–C4; **binds only after the first prompt** (CARD-0190, unchanged) | `task_complete` → `TurnEnd` (CARD-0099/0108) | trust prompt auto-accepted by the adapter (`RunnerCodexAdapter.cs:290-305`) off `pane.read` |

`HerdrStatusCorroborationService` compares herdr's `agent_status` with `IsWorkingAsync` for every
Running herdr session (`:55`) — no kind filter, none needed; herdr's grok/codex detection uses the
same OSC-title rule class as its claude one (K1/K5 `osc_title_idle`), so disagreement stays a
Warning-only corroboration hint (CARD-0162 §5).

## 6. Delivery (decision 8)

Unchanged: `SessionDeliveryProfile` keys ceilings on the session's `SessionBackend` snapshot
(`:58-79`); the queue's spill path passes the session's `AgentKind` into `TypedBodySpill.Fit`
(`SessionMessageQueueService.cs:107-138`); the dispatcher applies `ForAgentKind` to the resolved
ceilings (`AgentTaskDispatcher.cs:1766-1767`), which zeroes Grok's inline brief ceiling and, by
the CARD-0099 default-deny arm, Codex's too. The confirm loop, `blocked` deferral, 503 deferral,
Enter-only retries and parking are all backend/pending-keyed.

**Measured on Claude only** (CARD-0161, 2026-08-23): 86 400 B exact through one `pane.send_text`
with the bracketed-paste wrapper. herdr's `pane.send_text` is documented to honour the pane's
bracketed-paste mode for `agent.prompt` (`herdr --skill`); whether the Grok and Codex composers
take the paste path through it — Grok joins typed lines on pty (CARD-0084); Codex's paste
behaviour is unmeasured (`ProviderContractCatalog.cs:173`) — is **probe D1** (§8). Its outcome
picks between "herdr ceilings apply to all three kinds" (nothing to build) and "per-kind herdr
ceilings" (`DelegationSettings.HerdrCeilings` grows a `ForAgentKind`-style override set, same
shape as the modern/inbox pair). Until D1 runs, the reply ceiling and the 86 400 tripwire stay
as they are for all kinds and the brief ceiling is already 0 for Grok/Codex — so the only
exposure is a channel reply or a `Now` send between 14 400 chars and 86 400 B into a Grok/Codex
composer, which `TruncatedTerminalDelivery` (CARD-0024) would park rather than double-send.

## 7. Testing caveats the design inherits (not root-caused here)

- **CARD-0195 — Codex MCP boot can swallow the boot prompt.** Not reproduced in K5 (codex 0.147.0
  reached its composer in under 10 s with no `Starting MCP servers` stall), but the shape is
  live. On herdr the boot prompt goes through `VerifiedPromptSubmitter` off `pane.read` exactly
  as it does off the pty snapshot, so the failure mode is identical: a typed-but-inert composer,
  a degraded screen-only "confirmed", then the delivery watchdog. The S3 Codex canary must
  **skip, not fail**, on a measured boot stall (same rule the Claude herdr canary applies to a
  pane launch that does not complete in 90 s, `ClaudeHerdrRealCliStubProxyCanaryTests.cs:92-99`),
  and must say so in its skip reason.
- **CARD-0190 — Codex never binds without a prompt.** Identical on herdr; the canary sends one.
- **`launch_pending`/`interactive_ready`** on `HerdrAgentInfo` were only ever populated by
  `agent.start`; with passive detection `pane.get` carries `agent` + `agent_status` and nothing
  else. Nothing reads the two flags today (`grep interactive_ready src/ server/` → the DTO and
  the fake only); they stay on the record for wire compat.
- **`herdr agent list` shows no `name`** for a passively detected agent (K5). The pane keeps its
  `pane.rename` label and `antiphon-session` token, so the operator can still find it;
  `pane.report_agent` naming is CARD-0163's, unchanged.

## 8. Probes and measurements the build needs

| Probe | Question | Run by | Decides |
|---|---|---|---|
| **C1** | Shell-launched **Claude** (`& '<script>'` → `claude.exe --dangerously-skip-permissions`) is detected as `claude` by herdr's passive detection, as grok/codex were (K5–K7) | S1, before switching the Claude lane off `agent.start` | Whether decision 4 needs a Claude-only `agent.start` exception (expected: no — the operator's own `clproxy` panes are shell-launched) |
| **E2** | `pane.split --env` carries env like `tab.create --env` (P6 measured `tab.create` only) | S1 | Whether the 2nd–4th pane in a tab get their resolved keys; if not, allocator must `tab.create` for every launch |
| **D1** | 43 200 B and 86 400 B multi-line bodies via `pane.send_text` + Enter into a **Grok** and a **Codex** composer on herdr; `UserPrompt` record byte-exact? | S3 (with the FakeLlmApi stub so no real turn is spent) | Whether per-kind herdr ceilings exist (§6) |
| **K9** | `LaunchDetectTimeoutMs` floor: cold-start codex with MCP boot on this machine | S3 | The default (60 s proposed) and the skip threshold for the canary |

C1 and E2 are minutes each and touch only a throwaway workspace; results go next to K1–K8 in the
investigation doc and into the slice report.

## 9. Slices, order, verification

| Slice | Scope | Tests | Band |
|---|---|---|---|
| **S1 — runner launch shape + tailer parity** | §4 in full (`HerdrPaneChild` launch script, shell check, `pane.get` poll, wrong-agent/timeout failures, script cleanup); §5 (`StartTailerFor` / `RestoreTailerFromSidecar` extractions, `StartHerdrAsync` + `AdoptHerdrAsync` rewired); contracts `HerdrAgentKinds` move + `HerdrLaunchOptions.AgentKind` (null = claude); request validation; `HerdrSettings.LaunchDetectTimeoutMs`; probes C1, E2 | **`FakeHerdrServer`**: `pane.send_text` of `& '<x>.launch.ps1'` marks the pane's `Agent` to a configurable kind after a configurable delay (or never, or a different kind); `pane.get` returns it; `pane.process_info` lists a child. **New `HerdrLaunchShapeTests`** (runner): script content pins (`'` doubling, BOM, newline arg survives, env absent from the script), typed line is exactly `& '<path>'`, poll succeeds/wrong-kind fails/timeout fails and each failure tears down (`KillAsync` observed), script deleted on success and kept on failure, shell check refuses a non-PowerShell `shell_pid`. **`HerdrRunnerSessionTests`** gain `Grok_request_starts_GrokTranscriptTailer_with_deterministic_sidecar` and `Codex_request_starts_CodexTranscriptTailer` (assert `TranscriptSidecar.Format`); **`HerdrAdoptionSweepTests`** gain `R1_readopt_restores_the_sidecars_format` for grok and codex. Existing herdr tests keep passing with the fake's default kind `claude`. | 1–1½ days |
| **S2 — server gate, map, wire, docs** | §3 in full; `EnsureHerdrLaunchAllowed` text + dead-param removal; `docs/herdr-sessions.md`, `AGENTS.md` bullet, `agent-kinds.md`, `SessionBackend.cs` comment | `AgentSessionBackendTests` flips + 3 new (§3); `HerdrAgentKindMapTests`; `SessionRunnerHttpClient` wire test; `HerdrAlwaysOnChannelParityTests`' launch definition parametrised over `ClaudeCode`/`Grok`/`Codex` with the fake CLIs (`Antiphon.FakeClaude`, `Antiphon.FakeGrok`; Codex has no fake — its arm uses the `Cmd` stub the parity test already launches and asserts only the launch/adopt/exit path, not transcript) | ½ day |
| **S3 — evidence** | Real-CLI herdr canaries: `GrokHerdrRealCliStubProxyCanaryTests` (B-server shape of `GrokRealCliStubProxyCanaryTests.cs:143` with `SessionBackend.Herdr`, oracle = stub nonce + `UserPrompt` confirm, sidecar + `pane.get.agent == "grok"`), `CodexHerdrRealCliStubProxyCanaryTests` (B-runner shape of `CodexRealCliStubProxyCanaryTests.cs:140` on the herdr lane — the B-agent path stays deferred to CARD-0167 exactly as on pty); probe D1 and its ceiling decision; probe K9 → default timeout | Both canaries `[Explicit]`, `Category("RealCliStubProxy")`, composed gate `ANTIPHON_REAL_CLI_STUB_TESTS=1` + herdr reachable, skip-not-fail on a measured launch/boot stall (§7). Plus `PtyDeliveryCeilingsTests`/`DeliveryBackendCeilingsTests` cases if D1 yields per-kind herdr ceilings | 1 day |

S1 is independently deployable and is worth deploying alone: it fixes the newline-bundle defect
on the Claude lane (§0 item 2) with the Kind gate still in place. S2 depends on S1 (decision 11).
S3 depends on both and on a herdr window (its canaries run against the operator's live herdr,
like the CARD-0168 one). Suggested dispatch per this session's routing: S1 → Grok (runner-only,
the herdr fakes are the seam; shared tree is fine if no other worker is in
`src/Antiphon.SessionRunner`), S2 → Grok, S3 → Codex luna or Grok with the herdr window.

## 10. Card acceptance → where it lands

| Card checklist item | Slice |
|---|---|
| Create + start a non-AlwaysOn **Grok** agent on Herdr → live pane with detected `grok`, `hostPid=null`, sidecar written | S1 (mechanism, K1/K6 measured) + S2 (gate) ; live proof S3 canary |
| Same for **Codex** | S1 (K5 measured — through `codex.cmd`, never `agent.start`) + S2; S3 canary with the CARD-0195 caveat |
| Queue/input delivery reaches the Grok/Codex composer (not only `herdr agent prompt`) | unchanged path (`pane.send_text` + Enter, CARD-0161); S3 D1 measures the envelope |
| Working/idle and turn-end flush correct per kind (no permanent Working badge) | S1 decision 7 (right tailer ⇒ right `TurnEnd`) ; pinned by the S1 tailer tests and the S2 parity smoke |
| Refusal remains for unspiked kinds; Claude path unchanged | S2 (`OpenCode`/`Raw` refused, named); Claude path **changed** deliberately — off `agent.start` onto the script (decision 4), pinned by C1 and the existing Claude herdr tests |
| Tests: gate allows Grok/Codex; launch maps kind → herdr kind; ≥1 fake/integration path per kind without live keys | S2 gate tests; S1 `HerdrLaunchShapeTests` (kind expected vs detected); S2 parametrised parity smoke (fake CLIs, no keys) |

## 11. Open questions for the operator

1. **Drop `agent.start` entirely (decision 4), or keep it as the eligible fast path?** This
   design drops it. Keeping it means maintaining the eligibility predicate (no newline in any arg,
   canonical exe, not codex-on-windows) and two launch paths' worth of tests; the only thing it
   buys is herdr's `name` on the agent (K5: passively detected agents have none) — which
   CARD-0163's `pane.report_agent` is the proper home for.
2. **Should S1 ship before this card is otherwise touched?** It fixes a live defect on the Claude
   lane (§0 item 2) that CARD-0186 exposed; it is worth a standalone deploy and a check of any
   AlwaysOn Claude herdr agent that carries standing instructions.
3. **Detection timeout default** — 60 s proposed (K1 4.4 s, K5 <10 s, K2 failure 90 s). CARD-0195
   argues for longer on Codex; K9 measures before the number is chosen.
4. **Per-kind herdr ceilings** are decided by D1, not here; if the operator wants Grok/Codex on
   herdr before D1 runs, the exposure is stated in §6 and is parked-not-lost.
5. **Grok wrapper (`gkp.ps1`) live verification** still needs the machine that has it
   (CARD-0182 deferred the same step); K6 measured the shape with a stand-in wrapper.
