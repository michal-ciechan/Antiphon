# Transcript adoption safety (CARD-0006)

**Status:** planned, not implemented.
**Scope:** session-runner transcript discovery (`TranscriptTailer`), plus a new runner→server
fault event and incident kind.
**All file/line citations are against the worktree at planning time (2026-08-09) and MUST be
re-checked before implementation** — one cited file (`SessionRunnerRuntime.cs`) already has an
uncommitted fix in `C:\src\Antiphon` that shifts its line numbers.

---

## 1. The problem

Observed live 2026-08-09, session `18c04655`:

```
WRN Session 18c04655-...: <session-id>.jsonl never appeared (Claude forked the id);
    adopting discovered transcript C:\Users\lndco\.claude\projects\C--src-Antiphon\37512455-...jsonl
INF Tailing transcript ...\37512455-....jsonl for session 18c04655-...
```

`37512455-…` was the **human operator's own Claude Code conversation**. Claude's transcript root
is per-cwd (`~/.claude/projects/<enc-cwd>/`), three Antiphon agents plus the operator all share
`C:/src/Antiphon`, and when the agent's own jsonl never landed, the cwd-discovery fallback bound
to the busiest file in the folder — the operator's.

Confirmed consequences:

- The agent reported **65 agent-touched files** that were the operator's edits (0 after a clean
  relaunch).
- Working/idle is computed from a stranger's turns, so `WhenIdle` deliveries strand or misfire.
- Worst case: a channel-bound agent's reply dispatcher
  (`server/Application/Services/ChannelReplyDispatcher.cs:81`, `OnTurnEndAsync`) relays the
  *other* session's turn text to Telegram — an unrelated private conversation leaves the machine.
- The only warning is one WRN log line nobody watches.

### 1.1 The first-write race is fixed — and does not close this card

The proximate cause of *this* occurrence was CARD-0018: `RunnerSession.WriteAsync` threw
`"Session has no live pty-host connection"` when input raced a cold host start, so the boot
prompt was silently lost, Claude never wrote a jsonl, and the discovery fallback had nothing
legitimate to find. Fixed 2026-08-09 (uncommitted in `C:\src\Antiphon` at planning time):
`WriteAsync`/`ResizeAsync` now `await AwaitClientAsync(ct)`
(`src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:628-646` and `:832` in the fixed tree),
pinned by `tests/Antiphon.SessionRunner.Tests/FirstWriteRaceTests.cs`. **Treat that fix as
landed.**

It narrows the window but does not close it: *any* future cause of a missing jsonl —
Claude crashing before the first prompt, a blocking hook, a CLI behavior change, a launch that
dies after registration, an attached human session where no prompt is ever submitted — re-opens
the wrong-adoption window. The adoption rules below are required regardless.

---

## 2. Why the fallbacks exist (do not delete them)

`src/Antiphon.SessionRunner/TranscriptTailer.cs` has four discovery paths, each earned by a live
miss:

| Path | Where | Why it exists |
|---|---|---|
| Exact-id fast path | `TranscriptTailer.cs:241-247` | Claude *sometimes* honours `--session-id` (`AgentSessionService.cs:686-718` passes the Antiphon `AgentSession.Id` as the Claude session id). |
| Cwd discovery, fresh files (`isNew`) | `TranscriptTailer.cs:249-265`, `DiscoverByCwd` `:322-345` | Interactive Claude does not reliably honour `--session-id` — it can fork to a self-chosen `<uuid>.jsonl` (observed 2026-07-22; doc comment `:13-19`). Without discovery, turn-end detection and channel reply routing silently die whenever Claude forks the id. |
| Cwd discovery, pre-existing actively-written files (`allowActivePreexisting` after `_readoptionGrace` 30s) | `TranscriptTailer.cs:41-45`, `:256`, `:335-338` | Runner restart: the new tailer starts *after* the fork happened, so the real transcript is "pre-existing" from its snapshot's point of view. Re-adopting a live session across a runner restart is a core pty-host-split guarantee. |
| Mid-session fork follow | `TryFindNewerFork`, `TranscriptTailer.cs:352-385`, scanned every 10s from `:107-119` | `/clear` forks the conversation to a fresh self-chosen file (live miss 2026-07-31: an AZ Care channel reply after `/clear` never reached Telegram); a `--resume` can also write a new file. Pinned by `TranscriptTailerCompactionTests`. |

**How the incident got through:** the operator's transcript pre-existed the launch, so it failed
`isNew` — but it was being actively written (operator typing), so after 30s the
`allowActivePreexisting` arm accepted it on nothing more than a cwd match and recency. Note that
even the `isNew` arm is holed: an operator who *starts a new conversation* in the same cwd after
the agent launches produces a file that passes `isNew` + cwd, and today's rules adopt it after
only the 10s `_exactIdGrace`.

A plan that deletes these fallbacks breaks id-forked sessions, `/clear`, and restart re-adoption.
The fix is to make each path require *evidence the file belongs to this session*.

---

## 3. Evaluating the card's proposed rules

**(a) "Never adopt a transcript another live session is already tailing."** Correct and cheap —
it prevents two Antiphon sessions from binding to the same file, and prevents session B stealing
session A's `/clear` fork. But it is *insufficient for the observed incident*: the operator's
conversation is not tailed by anyone, so no claim would have existed. Adopted as rule **C1**, but
it cannot be the primary defense.

**(b) "Reject any candidate whose first record predates this session's launch."** Right idea,
three corrections needed:

1. *"First record" must mean first **timestamped** record.* Real transcripts open with meta
   records that carry no timestamp at all (`last-prompt`, `custom-title`, `agent-name`, `mode`,
   `permission-mode` — verified against a live transcript in
   `~/.claude/projects/C--src-Antiphon/` on 2026-08-09).
2. *"Launch" must mean the **child process start** (`ChildStartTimeUtc`), not tailer start* —
   the tailer restarts on runner restart; the session does not. Both construction sites have the
   epoch available: `launched.ChildStartTimeUtc` at `SessionRunnerRuntime.cs:408` and
   `manifest.ChildStartTimeUtc` at `:453` (`PtyHostManifest.cs:20`). Allow a small skew slack
   (2s). Do **not** use NTFS creation time as the primary signal (creation-time tunneling makes
   it lie after delete/recreate patterns); use record timestamps, with file times advisory.
3. *It is wrong for resume forks.* Antiphon relaunches with `--resume <same id>`
   (`AgentSessionService.cs:709-716`; the id never changes, only the flag —
   `AgentControlService.cs:181-197`), and a resume can fork to a new file whose copied history
   carries **original** timestamps predating the relaunch. A bare timestamp rule would refuse the
   legitimate resume transcript. (Copied-history timestamp behavior is an assumption to pin with
   the canary test in §8.4.)

   And it is *insufficient*: an operator conversation started **after** the agent launch passes
   the timestamp check and still gets wrongly adopted.

Adopted with those corrections as rule **C3** — a necessary filter, never a sufficient one.

**(c) "Failing both, run with no transcript and raise a visible incident."** Adopted, with the
semantics defined in §6. The missing piece in the card is that (a)+(b) alone still cannot
distinguish two same-cwd files created after launch — a **positive identification signal** is
required (§4).

### 3.1 Positive identification options considered

| Signal | Verdict |
|---|---|
| **PID correlation** | Rejected. No transcript record carries a pid (verified against `TranscriptNormalizer.cs` — fields parsed are `type/uuid/parentUuid/timestamp/message.*/isMeta/subtype`; real records add `userType, entrypoint, cwd, sessionId, version, gitBranch, slug`, none process-identifying). Probing open file handles is unreliable — Claude append-closes the file per write rather than holding it open. |
| **Per-session `CLAUDE_CONFIG_DIR`** | Rejected. It would give hermetic isolation, but it relocates *all* Claude state (credentials, settings, memory, skills, trust), and the runner-side reader resolves `CLAUDE_CONFIG_DIR` from its **own** process env (`TranscriptTailer.cs:422-428`), so per-child values desync the tailer unless plumbed everywhere. Too invasive for the win. |
| **Content correlation against input the runner wrote** (rule **C4**) | **Adopted, slice 1.** The runner sees every byte typed into the session (`RunnerSession.WriteAsync`, `SessionRunnerRuntime.cs:618` pre-fix / `:628` post-fix); queued deliveries arrive verbatim as bracketed-paste bodies (`SessionMessageQueueService.DeliverAsync`, `server/Application/Services/SessionMessageQueueService.cs:397-450`), and Claude records the submitted prompt verbatim as a `user` record. A candidate whose user-prompt text matches input this session actually received *is* this session's transcript. Today the runner keeps **no** record of written input — a small bounded input log is added (§4.2). |
| **`agent-name` record match** (rule **C2b**) | **Adopted as a cheap reject filter.** Every Antiphon launch passes `--name <agent.Name>` (`AgentControlService.cs:142`, `AgentTaskDispatcher.cs:362-386`), and real transcripts carry `{"type":"agent-name","agentName":"..."}` / `custom-title` meta records reflecting it (verified live 2026-08-09). A candidate carrying a *different* agent name — or none, like the operator's plain conversation… note operator sessions may carry their own names — cannot be rejected on absence alone across Claude versions, so: **mismatch ⇒ reject; absence ⇒ neutral**. Per-agent, not per-session, so it cannot disambiguate two sessions of one agent. |
| **SessionStart hook marker** (rule **M**) | **Adopted, slice 2 — the signal that beats heuristics.** Per current Claude Code docs (checked 2026-08-09): `SessionStart` hooks receive `session_id`, `transcript_path` (absolute jsonl path), `cwd`, and `source ∈ {startup, resume, clear, compact, fork}` on stdin — and re-fire with the **new** transcript path after `/clear` and on resume — exactly the events that move the file. A failed or timed-out hook never blocks the session. Injected per-launch (no shared-settings pollution) via `--settings <per-session file>` + a marker script; the tailer reads the marker file and gets the **authoritative** path, replacing discovery *and* fork-scan heuristics whenever present. **Key unknown:** the docs do not confirm that `--settings` merges a `hooks` section — that is the canary's first job (§8.4), and §4.1 names the fallback delivery if it doesn't. Markers can still be late or absent (the sampled live transcript shows a SessionStart hook being cancelled at its configured 10s timeout), so the hardened heuristics of slice 1 remain as the fallback, permanently. |

---

## 4. Design

### 4.1 Components

**`TranscriptClaimRegistry`** (new, runner-process singleton — one instance per
`SessionRunnerRuntime`, which is itself a process singleton):

- `bool TryClaim(string path, Guid sessionId)` — `ConcurrentDictionary<string, Guid>` keyed by
  `Path.GetFullPath(path)` with `OrdinalIgnoreCase` (Windows paths). `TryAdd`, or true if already
  owned by the same session. Atomic ⇒ race-free across concurrent launches without extra locks.
- `void ReleaseAll(Guid sessionId)` — called from `TranscriptTailer.DisposeAsync`.
- A session claims **every** path it ever tails (exact-id, discovered, each fork). Claims are
  *not* released on fork-switch — the old file is still this session's history and must never be
  adoptable by a sibling.
- **Restart survival:** claims are rebuilt from sidecars (below) inside
  `AdoptOrphanedHostsAsync`, which already must complete before the HTTP API starts listening
  (`SessionRunnerRuntime.cs:198-206`) — so a freshly launched session can never race the restore.
- **Known limitation:** the registry is per-runner-process. Two runners sharing one `~/.claude`
  (e.g. the manual-mode 17283 runner next to the 17204 daemon) are already unsupported; claims do
  not extend across them. Document, don't solve.

**Transcript sidecar** (new, runner-owned — the pty-host manifest is host-written and
deliberately excludes env/extras, `PtyHostManifest.cs:8`; don't touch it):

- Path: `<SessionLogPath>/transcripts/<sessionId:N>.json`, written atomically (temp+rename, same
  pattern as `PtyHostManifest.SaveAtomic`).
- Written at `StartAsync` with `{sessionId, cwd, agentName (parsed from the launch args'
  --name), childStartUtc, transcriptPath: null}`; updated on every adopt/fork-switch with the
  path and `how: exact|marker|discovery|fork`.
- On runner restart, `AdoptAsync` passes the sidecar to the tailer: **if it names a path and the
  file exists, tail it directly** — no discovery at all. This *replaces* the
  `allowActivePreexisting`/`_readoptionGrace` arm, which is deleted.
- Pruned alongside pty-host logs in `CleanupPtyHostState` (`SessionRunnerRuntime.cs:153`), same
  14-day window, only for sessions no longer registered.

**Session input log** (new): a bounded (64 KiB) per-session append buffer fed from
`RunnerSession.WriteAsync` before forwarding to the host. Stored *normalized*: strip ESC
sequences (including bracketed-paste `\x1b[200~`/`\x1b[201~` wrappers), map `\r`/`\r\n` → `\n`.
Handed to the `TranscriptTailer` at construction. Not persisted — after a runner restart it is
empty, which is fine because restarted sessions re-tail via the sidecar, not discovery.

**Marker hook** (slice 2): per-session settings file
`<SessionLogPath>/claude-settings/<sessionId:N>.json` generated at launch, containing a
`SessionStart` hook that runs `scripts/antiphon-session-marker.ps1` (ASCII-only — CLAUDE.md
PowerShell 5.1 rule). The script reads stdin JSON and appends one line to the file named by env
`ANTIPHON_TRANSCRIPT_MARKER` (set per-launch next to the existing `ANTIPHON_*` block,
`AgentTaskDispatcher.cs:393-409`, but for *all* Claude launches, in
`AgentSessionService.BuildRuntimeLaunchSpec` `:665-684`). Marker file:
`<SessionLogPath>/transcript-markers/<sessionId:N>.marker.jsonl`, each line
`{source, session_id, transcript_path, cwd, ts}`. The launch args gain
`--settings <that file>`. The tailer polls the marker file first on every locate/fork-scan pass.

Hooks cannot read any session-identifying env var (none exists — they parse `session_id` from
stdin JSON), but they *do* inherit the launch env, which is how `ANTIPHON_TRANSCRIPT_MARKER`
reaches the script. **Fallback delivery** if the canary shows `--settings` does not merge a
`hooks` section: register the same hook once at user level (`~/.claude/settings.json`, synced
via the claude-home repo) written to no-op instantly when `ANTIPHON_TRANSCRIPT_MARKER` is unset —
it then fires harmlessly for human sessions and works for every Antiphon launch in any cwd.
Project-level `.claude/settings.json` is rejected (only covers repos that check it in).

### 4.2 Adoption rules

Named checks:

- **M — marker:** the marker file names a transcript path. Authoritative. Still runs C1 (a claim
  conflict on a marker path means two sessions were told the same file — a bug; refuse + fault
  event `MarkerConflict`).
- **C1 — unclaimed:** `TryClaim` succeeds. Claim-then-verify: claim first, release on
  verification failure, so two tailers can never interleave verify→adopt on one file.
- **C2 — cwd match:** existing `TranscriptCwdMatches` (`TranscriptTailer.cs:387-419`).
- **C2b — agent-name filter:** candidate carries an `agent-name`/`custom-title` meta record with
  a *different* name ⇒ reject. Absent ⇒ neutral. (Extracted in the same leading-lines probe as
  the cwd field.)
- **C3 — epoch:** first *timestamped* record ≥ `childStartUtc − 2s`. **Waived when the launch
  mode is resume/continue** (copied history legitimately predates the relaunch). Never sufficient
  alone.
- **C4 — content:** some user-prompt record (not a local-command record —
  `TranscriptKinds.IsLocalCommandRecord` family) whose normalized text is ≥ 12 chars and appears
  as a substring of the session's normalized input log. Long records match on their first ~200
  normalized chars (the input log is bounded).

**Decision procedure** (order matters):

1. **Marker present** → adopt its path (M + C1). Covers startup, id-fork, resume fork, `/clear`.
2. **Exact `<session-id>.jsonl` exists** → adopt (C1; the filename *is* the positive id).
3. **Sidecar names a path (restart re-adopt)** → tail it (C1). No discovery.
4. **Fresh discovery** (exact id never appeared, after `_exactIdGrace`):
   adopt iff **C1 ∧ C2 ∧ C2b ∧ C3 ∧ C4**. Multiple qualifiers → newest mtime among them (mtime
   is now only a tiebreaker, never evidence). Candidates are re-evaluated every poll — C4 can
   start failing-then-passing as records flush; that's just "wait".
5. **Fork-follow** (mid-session scan): marker wins; otherwise `TryFindNewerFork` keeps its
   created-after-tailer-start + newer-writes + C2 conditions and **adds C1 ∧ C4**. A `/clear`
   fork initially holds only the command record, so the switch defers until the next real prompt
   lands in the fork — harmless (the old file is quiet; working/idle correctly reads idle, the
   queued post-clear delivery fires, lands in the fork, C4 matches, switch happens). Slice 2
   makes the switch immediate via the `clear`-source marker.
6. **Migration shim** (restart re-adopt of a session with *no* sidecar — pre-deploy sessions
   only): allow the old active-write heuristic **only** when the candidate is the *unique*
   cwd-matching active file **and** C2b does not reject it, and emit an info-level
   `TranscriptBoundByDiscovery` event so it is visible. This shim can be removed one release
   later.
7. **Nothing qualifies** → keep polling, and surface a fault (§5). **Never bind on cwd+recency
   alone. Ever.**
8. **Child exit** ends adoption attempts: a dead child writes no new transcripts. If input had
   been delivered but no transcript ever bound, emit the fault immediately on exit.

`_readoptionGrace` / `allowActivePreexisting` / the `preexisting` snapshot
(`TranscriptTailer.cs:39-45`, `:222`, `:256`, `:304-316`, `:335-338`) are deleted — steps 3 and 6
replace them. This also fixes a latent hole: a very fast fork landing *before* the snapshot was
taken got classed pre-existing and became adoptable only via the 30s active arm.

**Replay of the incident under these rules:** operator's file — C1 passes (untailed), C2 passes,
C2b likely rejects (different/no agent name), C3 **fails** (first timestamped record predates
launch), C4 **fails** (its text was never written into this session). Refused on two-to-three
independent grounds; fault raised at the deadline; nothing binds. An operator conversation
*started after* the launch additionally fails C4. Two sibling agents forking concurrently in one
cwd are separated by C4 (each transcript contains only its own delivered prompt) with C1 as the
last-resort tiebreak.

**Residual false-negative, accepted:** a session whose only input was human keystrokes with
heavy interactive editing may never produce a C4 match (the input log holds keystrokes, not the
composed line). It runs transcript-less with a visible incident instead of guessing — and the
slice-2 marker eliminates the case. Antiphon-driven sessions always have queue-delivered verbatim
bodies, so agents are unaffected.

---

## 5. Surfacing the fault to a human

Today a locate fault is log-only (`ReportLocateFault`, `TranscriptTailer.cs:289-302`). New
plumbing, following the existing 5-touchpoint pattern for a runner event:

1. **Contract** — `SessionRunnerEventNames.SessionTranscriptFault` (+
   `SessionTranscriptBound` info) in
   `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:188`, payload record
   `RunnerTranscriptFaultEvent(Guid SessionId, string Kind, string Detail, string? CandidatePath)`
   with `Kind ∈ {AdoptionRefused, TranscriptMissing, ForkUnresolved, MarkerConflict}`.
2. **Publish** — from the tailer (pattern of `TranscriptTailer.cs:206`). Cadence: first fault
   60s after candidates start being refused (or immediately on child-exit-without-transcript /
   marker conflict), then at most every 5 minutes. `SessionTranscriptBound` fires once on any
   *heuristic* (non-exact, non-marker) adoption, info-level, for audit.
3. **DTO** — optional payload on `SessionRunnerEvent`
   (`server/Application/Dtos/SessionRunnerDtos.cs:74`).
4. **Parse** — new branch in `SessionRunnerHttpClient.ParseEvent`
   (`server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:193` — unknown events
   are silently dropped today, `:248`).
5. **Handle** — new branch in `SessionRunnerEventPump`
   (`server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:46-70`) resolving
   session → agent and calling `AgentSupervisorService.RecordIncidentAsync`
   (`server/Application/Services/AgentSupervisorService.cs:272` — also raises the 1:1 alert with
   dedup key `supervisor:{kind}:{agentId}`; caller saves changes).

New `AgentIncidentKind` values (append-only enum, `server/Domain/Enums/AgentIncidentKind.cs`):
`TranscriptBindFailed = 13` and `TranscriptBoundByDiscovery = 14` (the latter recorded with
`raiseAlert: false`, mirroring `ContextCompacted`).

**Severity:** `TranscriptBindFailed` is Warning by default, **Critical when the agent has any
channel binding** (the pump handler checks bindings; a channel-bound agent with no transcript
cannot dispatch replies at all, and a *wrongly bound* transcript is the privacy incident this
card exists for). Critical alerts reach Telegram through the existing
`AlertService → ChannelAlertRouter → AlertDigestFlusher` pipeline; all incidents appear in the
agent card's incident drawer (`GET /agents/{id}/incidents`, `AgentEndpoints.cs:96`;
`client/src/features/agents/AgentsPage.tsx:363`).

---

## 6. A session with no transcript at all

What actually degrades, and the decisions:

- **Working/idle:** with zero entries, both server (`SessionMessageQueueService.IsWorkingAsync`,
  `server/Application/Services/SessionMessageQueueService.cs:565-605` — empty ⇒ `false`) and
  client (`SessionTranscriptPanel.tsx:113`) read **idle**. **Keep this.** It is not a bug to
  paper over: the launch flow *depends* on it — the boot prompt / launch note is enqueued
  `WhenIdle` *before* any transcript exists (`AgentSessionService.DeliverLaunchNoteAsync:804-832`,
  `AgentTaskDispatcher.cs:331-336`), so "no transcript ⇒ working" would deadlock every fresh
  launch. The runner-side `TranscriptWorkingState.IsProvenIdle` stays conservative
  (`src/Antiphon.SessionRunner/TranscriptWorkingState.cs:49` — empty is *not proven idle*), which
  is correct for its consumer (the CPU-spin watchdog).
- **Queued deliveries:** continue to flow (they degrade to send-immediately, exactly today's
  semantics for a not-yet-created transcript). We deliberately do **not** hold deliveries: a
  silently stuck queue is a worse failure than an eagerly delivered message, and the incident
  makes the state visible.
- **Channel reply dispatch:** cannot function — `OnTurnEndAsync` fires off ingested turn ends,
  and there are none. It fails *safe* (nothing is relayed; in particular nobody else's
  conversation is relayed). The Critical-when-channel-bound severity above is the compensation.
- **Ingestion/UI:** the transcript panel stays empty; the session's ansi buffer/screen remain
  fully live (they come from the pty stream, not the jsonl).
- The tailer **keeps polling for the session's lifetime** (existing behavior, `LocateAsync`
  doc `TranscriptTailer.cs:210-213`) — if a legitimate transcript appears late and passes the
  rules, it binds late, a catch-up `SyncTranscriptAsync` heals the gap (that path already exists
  for restart backfill), and a `Recovered`-style info incident is *not* needed —
  `SessionTranscriptBound` covers it.

---

## 7. Implementation slices

1. **Slice 1 — safety rules (closes CARD-0006):** claim registry, sidecar, input log, rules
   C1–C4 + decision procedure, deletion of the active-preexisting arm, migration shim, fault
   event + incident kinds + severity routing. No new Claude-side behavior assumptions beyond
   what canary tests already pin.
2. **Slice 2 — positive identity (marker hook):** per-session `--settings` + `SessionStart`
   marker script + env var + tailer marker-first path + `MarkerConflict` handling. Gated on the
   canary test of §8.4 passing against the pinned Claude version.
3. **Slice 3 (optional):** UI badge on the session header when a `TranscriptBindFailed`
   incident is open, so the state is visible without opening the incident drawer.

---

## 8. Test plan (TUnit)

Style: file-driven tailer tests with `CLAUDE_CONFIG_DIR` pointed at a temp tree,
`[NotInParallel("ClaudeConfigDirEnv")]`, short ctor-injected graces — exactly like
`tests/Antiphon.SessionRunner.Tests/TranscriptTailerCompactionTests.cs:27-63`. Integration
assertions scoped to rows the test created (CLAUDE.md shared-Postgres rule).

### 8.1 `TranscriptAdoptionSafetyTests` (runner, new — the core of the card)

- `Preexisting_actively_written_transcript_in_same_cwd_is_never_adopted` — a cwd-matching file
  that predates the tailer and keeps being appended to (simulated operator). Old code adopts it
  after 30s; new code must refuse and publish `SessionTranscriptFault`. **This is the test that
  would have caught the live incident.**
- `Fresh_file_created_after_launch_without_matching_content_is_refused` — passes C2+C3, fails
  C4 (text never written to this session). Pins that timestamp alone is insufficient — the
  "operator starts a new conversation after launch" case.
- `Candidate_with_mismatched_agent_name_record_is_refused` (C2b) and
  `Candidate_with_no_agent_name_record_is_not_rejected_for_absence`.
- `Two_sessions_in_one_cwd_adopt_their_own_forks_not_each_others` — two tailers, two forked
  files, each containing only the prompt delivered to its own session; mtimes ordered so the old
  newest-wins rule would swap them. Each must bind to its own (C4), and no file is double-tailed
  (C1).
- `A_file_claimed_by_another_live_tailer_is_refused_even_when_it_qualifies` (C1 alone).
- `Resume_fork_with_copied_old_timestamps_is_adopted_on_content_match` — launch mode resume, C3
  waived, C4 matched by the auto-continue prompt. Pins the precedence "content beats timestamp".
- `Discovery_refusal_publishes_fault_event_and_rebinds_late_when_a_valid_file_appears`.
- `Child_exit_with_delivered_input_and_no_transcript_faults_immediately`.

### 8.2 Fork-follow and restart

- `Clear_fork_of_a_sibling_session_is_not_followed` — sessions A and B in one cwd; A's `/clear`
  fork must not be adopted by B's fork scan (C1 + C4). Extends the existing fork test in
  `TranscriptTailerCompactionTests.cs:69`.
- `Clear_fork_is_followed_once_it_contains_this_sessions_next_prompt` — pins the deliberate
  deferral of §4.2 step 5.
- `Sidecar_path_is_retailed_directly_after_restart_with_no_discovery` — construct tailer the
  way `AdoptAsync` will, with a sidecar naming the fork; a *newer, busier* stranger file in the
  same cwd must be ignored.
- `Restart_without_sidecar_uses_migration_shim_only_for_unique_candidate` — two active
  candidates ⇒ refuse + fault; one ⇒ adopt + `TranscriptBoundByDiscovery` info event.
- `Claims_are_restored_from_sidecars_before_new_adoption_runs` — runtime-level test through
  `AdoptOrphanedHostsAsync` ordering.

### 8.3 Server-side (`Antiphon.Tests`, integration)

- `Transcript_fault_event_creates_incident_and_alert` — pump receives
  `SessionTranscriptFault` → `AgentIncident(Kind=TranscriptBindFailed, Warning)` row scoped to
  the test's agent; with a channel binding present ⇒ Critical.
- `Heuristic_bind_event_creates_info_incident_without_alert` (mirrors `ContextCompacted`).
- `Empty_transcript_still_reads_idle` — pin the §6 decision so nobody "fixes" it later
  (server `IsWorkingAsync` + client `isWorking` test file both get a case if not already pinned).

### 8.4 Canary + fakeclaude (slice 2 gate)

- `ClaudeSessionStartHookCanaryTests` (headed, `[NotInParallel("Headed")]`,
  `ANTIPHON_HEADED_TESTS=1`): pins against real Claude that (a) a `--settings <file>` hooks
  block is honored — **the one fact current docs do not confirm**; if it fails, switch to the
  user-level fallback of §4.1, (b) `SessionStart` stdin carries `session_id` + `transcript_path`
  + `cwd` + `source` (docs-confirmed; pin anyway — the repo has already caught docs and reality
  diverging: docs say `--session-id` is always honoured, yet the id-fork that motivates this
  whole card was observed live 2026-07-22 and 2026-08-09), (c) it re-fires with the new path
  after `/clear` and on `--resume`, (d) whether a resume fork's copied records keep original
  timestamps (the C3-waiver assumption). Follows the `ClaudeLocalCommandCanaryTests` pattern.
- `FakeClaudeContractTests` extension: fakeclaude writes the marker file when
  `ANTIPHON_TRANSCRIPT_MARKER` is set, modeling the canary-measured behavior, so runner tests can
  exercise the marker-first path unheaded.

---

## 9. Decisions made, and what's deliberately open

**Decided:** content correlation (C4) is the mandatory positive signal for any heuristic
adoption; timestamp (C3) is a filter, waived on resume; the already-tailing check (C1) is kept
but understood to be incapable of catching human-session collisions; `allowActivePreexisting`
is deleted in favor of the sidecar; no transcript ⇒ idle + flowing deliveries + safe-dead channel
dispatch + Warning/Critical incident; marker hook is the end-state positive id but ships as a
separate slice behind a canary.

**Open:**

- Exact C4 normalization constants (12-char minimum, 200-char window, 64 KiB log) — tune during
  implementation; the *shape* (normalized substring, non-command records only) is decided.
- Whether the migration shim (§4.2 step 6) ships at all, vs. accepting one-time
  `TranscriptBindFailed` incidents for live pre-deploy sessions after the first restart.
- Slice 3 UI badge.
- Whether `SessionTranscriptBound` should also fire for marker-based binds (audit completeness)
  or only heuristic ones (noise).
- Marker script transport details (`pwsh` vs `powershell.exe` fallback; ASCII-only rule applies
  either way).
