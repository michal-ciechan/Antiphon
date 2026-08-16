# CARD-0056 — A failed launch must kill what it started, and reconciliation must see the mirror case

- **Status**: Planned (this document is the plan; nothing here is implemented)
- **Card**: CARD-0056 (`95f99895-359f-481a-a7e5-c3b3e15d99ed`) — "Three orchestrator sessions are
  connected where there should be one, and two running sessions are claimed by no agent"
- **Date**: 2026-08-16
- **Relates to**: the CARD-0056 triage (task e34e18d9 — root cause, verified here, not re-derived),
  CARD-0055 (transcript-confirmed delivery; its matcher and late-confirm shape are reused, its
  boot-prompt scope-out is partially lifted), CARD-0006 (C4 machinery), CARD-0047 slice 4A
  (confirmed **unrelated** — that is `AgentTaskDispatcher.TryReuseWarmAgentAsync`/`SpawnFresh` on
  delegated dispatch; nothing in this chain goes through it), the 2026-08-08 miss that created
  `VerifiedPromptSubmitter`.

## 0. The verified chain, plus what the capture adds

The triage's chain was verified against the code line by line and holds:

1. `"/remote-control"` (15 chars) is sent during a cardless interactive launch/restart by
   `SendRemoteControlCommandsAsync` (`server/Application/Services/AgentSessionService.cs:778`),
   called from `LaunchInteractiveProcessAsync` at `:348`.
2. `RunnerClaudeAdapter.SendPromptAsync` routes it through `VerifiedPromptSubmitter.SubmitAsync`
   (`src/Antiphon.Agents.Pty/VerifiedPromptSubmitter.cs:57-69`): type once, poll the rendered
   screen for `ComposerDeliveryEvidence` for `EvidenceTimeoutSeconds` (default 15,
   `SupervisionSettings.cs:74`), throw `PromptDeliveryException` if it never appears. **The body is
   typed exactly once; there is no re-type on evidence timeout.**
3. The catch at `AgentSessionService.cs:370-381` calls only `adapter.DisposeAsync()` —
   `RunnerClaudeAdapter.DisposeAsync()` is `=> ValueTask.CompletedTask` (`RunnerClaudeAdapter.cs:170`).
   The process leaks. The card-launch paths at `:183`/`:209` call `KillAsync` first; this path is
   missing it. **So is `StartAsync`'s own outer catch at `:234-235`** — a new finding beyond the
   triage: any card-launch exception thrown outside the two inner timeout branches (e.g.
   `WaitForReadyOrThrowAsync`, the remote-control commands, `SaveChangesAsync`) leaks the same way.
4. The outer catch (`:282-308`) marks the session and agent Failed; the AlwaysOn supervisor
   (`AgentSupervisorService.cs:110`, `:148-183`) correctly restarts against the false signal.
5. `SessionReconciliationService.ReconcileSessionsAsync` (`:74-177`) queries only `LiveStatuses`
   (`:27-28`) — DB-dead-vs-runner-alive is invisible forever. Its docstring (`:12-24`) names only
   the opposite case.

### New evidence: cefed08a's `.ansi` capture settles the fix-3 question

`C:\logs\antiphon\session-runner\cefed08a….ansi.log` is **append-mode across relaunches** (created
2026-08-13 19:45Z). It contains exactly two boot banners: `v2.1.229` at byte 0 (the 2026-08-13
batch launch) and `v2.1.233` at byte 7,136,924 (today's 16:11:55Z relaunch).

- **The Aug-13 head shows the full scripted sequence succeeding**: `/remote-control` typed →
  autocomplete menu → submitted → "remote-control is active" → `/rename Antiphon-Orchestrator`.
  So cefed08a is not a manually-launched stranger: it is the **orchestrator's own session from the
  Aug-13 batch**, relaunched in place today (v2.1.229 → v2.1.233 suggests an upgrade restart).
  Server logs show it active all day (SessionFinished broadcasts from 07:21Z onward).
- **Today's boot section shows the banner, then the resumed conversation's history re-render — and
  the typed `/remote-control` NEVER echoes.** Every `remote-control` occurrence in the ~830 KB
  after today's banner is conversation content (the session discussing this very card). The write
  was swallowed by the TUI while it re-rendered a large resume history; over the following 4.5
  hours of capture it never surfaced.
- **`fdf1dd3d` — the supervisor's replacement, resuming the SAME conversation 60 seconds later —
  armed on its first try** (`remote-control is active` appears once in its capture).

Conclusions this forces:

- **Raising `EvidenceTimeoutSeconds` is insufficient by construction.** No poll duration reveals
  text that was never buffered into the composer. The evidence poll did its job — the text
  genuinely was not there. The defect is that the submitter types once and gives up, and that
  giving up fails a healthy session and leaks its process.
- **A retry works.** Same conversation, second launch, first-try success. The fix for the
  verification is re-sending, not waiting longer.
- The poll itself has no demonstrated race. The demonstrated race is **type-at-ready vs
  resume-history-render**: `WaitForReadyAsync`'s quiet-period readiness fires in a quiet gap
  before/during the history render, and input typed then is discarded.

## 1. Design decisions

### D1. Both leaking catches kill before disposing (fix 1)

In `LaunchInteractiveProcessAsync`'s catch (`:370-381`) and `StartAsync`'s outer catch
(`:218-238`): when `adapter` is non-null, `await adapter.KillAsync(KillGraceMs, CancellationToken.None)`
then `DisposeAsync`, matching `:183`/`:209`. `CancellationToken.None` matches the outer catch's
existing `SaveChangesAsync(CancellationToken.None)` cleanup posture. Details:

- The resume-not-found path (`sessionNotFound` → `ClaudeSessionNotFoundException` → fallback
  relaunch with the same session id) **requires** the kill: today the fallback only works if the
  first process happened to die on its own; killing first makes it correct by construction.
- Double-kill after `:183`/`:209` (which already killed, then something later throws into the
  outer catch) is harmless — the runner's kill on a dead session returns false.

### D2. Boot-prompt delivery retries the whole verified submit, at the caller (fix 3, primary)

New private helper in `AgentSessionService` — `SendBootPromptWithRetryAsync(adapter, body, ct)` —
wrapping `adapter.SendPromptAsync`: on `PromptDeliveryException`, wait
`BootPromptRetryDelaySeconds` (default 2), retry, up to `BootPromptAttempts` (default 3) total
attempts. Used by both `SendRemoteControlCommandsAsync` (`:778`) and `StartAsync`'s work prompt
(`:168`).

Why re-typing is safe where CARD-0055 D2 forbade it: the exception means **no composer evidence
appeared** — the same check that would gate an Enter says the composer does not hold the body, so
typing again cannot double-submit. (CARD-0055's Enter-only rule protects the post-evidence phase;
this retry runs only in the no-evidence phase.) The residual snapshot-blind case — text present
but the screen read fails — is bounded by D3's transcript late-confirm, and for the slash command
a doubled `/remote-control/remote-control` is an invalid command, not a work item.

Why caller-level rather than inside `VerifiedPromptSubmitter`: each `SendPromptAsync` already
re-runs the full cycle (`ClearLiveBufferAsync` → type → evidence → Enter → advance), so the loop
adds nothing the submitter needs to know about — and it keeps `src/Antiphon.Agents.Pty` and
`tests/Antiphon.Agents.Pty.Tests` untouched (the latter is owned by concurrent task a4389709).

`EvidenceTimeoutSeconds` stays 15 — worst case 3 × 15 s ≈ 45 s spread over ~49 s, which also
covers a history render longer than any single window. Raising the single window is refuted by
the capture: the first write's text never appears, at any timeout.

### D3. `/remote-control` becomes best-effort on interactive launches, with transcript late-confirm

**The false positive's blast radius, not the false positive, is what killed cefed08a**: a
monitoring command's delivery failure failed a healthy session. On the interactive path, after
D2's retries exhaust:

1. **Late-confirm against the transcript when ground truth exists** (CARD-0055's shape, applied
   to boot): before the first attempt, capture `baseline = max(Sequence)` over the session's
   `TranscriptEntries` — observability gate: only when ≥ 1 entry exists, which a resume-mode
   relaunch of a live session has (cefed08a had a full day of ingestion) and a fresh boot does not
   (the file is created by the first submit — CARD-0055 D4's scope-out stands for fresh boots).
   A `UserPrompt` row past the baseline with
   `TranscriptKinds.TryReadLocalCommandName(kind, text) == "remote-control"` — or
   `PromptSubmissionMatch.IsConfirmedBy("/remote-control", text)`, which the `<command-name>`
   wrapper satisfies (`/remote-control` normalizes to 15 chars ≥ `MinMatchChars`) — means the
   command actually submitted while the screen reads were blind: proceed as delivered.
2. **Otherwise: raise an `RcDegraded` incident (Warning) and CONTINUE the launch** — no
   `/rename` (never append to a possibly-held composer), no throw, session stays Running. The
   armed-marker timeout in `WaitForRemoteControlArmedAsync` (today log-only, `:803`) is upgraded
   to the same incident so a silent no-RC session is always visible.

The card **work prompt** (`StartAsync`) stays fatal on delivery failure — the prompt is the
session's purpose — but now with D2's retries before failing and D1's kill after.

### D4. Reconciliation grows a third pass: DB-dead-vs-runner-alive, re-adoption first (fix 2)

In `SessionReconciliationService`, after the existing two passes, iterate the same
`_runnerClient.ListAsync` result (one fetch per sweep): for each runner session with
`Status == "Running"`, load the DB row.

| DB state | Action |
|---|---|
| No row at all | Alert (Warning, dedup key `reconciler:orphans`). Never kill. |
| `Failed` | **Re-adopt** (the default), gated on positive health evidence below. |
| `Stopped` | The only auto-kill arm: stop intent was already expressed by an operator and the kill evidently failed — retry the runner kill; alert if it fails again. |
| `Starting`/`Running`/`Stopping` | Not this pass's business (pass 1 owns live rows). |

**Positive evidence required to re-adopt** — presence of health, not absence of bad news:

1. Runner reports `Running` with a `Pid` or `HostPid` (a real process exists —
   `SessionRunnerSessionDto`, `server/Application/Dtos/SessionRunnerDtos.cs:5`).
2. A per-session probe answers: `GET` the session's buffer/screen through the existing client —
   proof the detached pty-host's pipe is alive and serving, and that `LastSequence` is readable.
   (An idle session's sequence does not advance, so advancement is deliberately NOT required.)

Probe fails ⇒ change **nothing**; Error alert. Unresponsive-but-running is a state for a human.

**Re-adoption writes**: `Status = Running`, `EndedAt = null`, `ExitCode = null`,
`LastSeenAt = now`; the old `FailureReason` goes into the incident text, then cleared. If the
owning agent's `PersistentSessionId` still equals this session id, restore the agent
(`Failed → Running`, publish `AgentChanged`) — otherwise the session stays **unclaimed but
Running**, visible in the UI, and the operator decides. This is the cefed08a constraint honored
structurally: *unclaimed never implies kill*; killing requires either prior operator intent
(`Stopped`) or an explicit operator action against the now-visible session. New
`AgentIncidentKind.SessionReAdopted` (Warning) when an agent owns it; alert-only for unclaimed
sessions (incidents require an agent id). Setting: `SessionReconciliationSettings.ReAdoptEnabled`
(default true).

**Oscillation and races**: `SuperviseAsync` re-checks liveness every tick before a due restart
fires (`AgentSupervisorService.cs:110-145`), so a re-adoption landing before the backoff due time
cancels the duplicate restart naturally; if the restart already fired (5 s base backoff usually
beats the sweep), the end state is today's — one extra live session — but *visible* instead of
invisible. In-memory cap: a session re-adopted more than 3 times per server uptime stops being
re-adopted and escalates Critical (a flapping runner report is a state for a human, not a loop).
Pass 1 cannot re-close a re-adopted row while the runner keeps reporting Running, so the pair
cannot ping-pong on consistent data.

**Retroactive by construction**: the first sweep after deploy re-adopts both leaked sessions —
`cefed08a` (probe passes; unclaimed → Running + alert; the operator keeps or stops it) and
`e12439ee` (same; nothing claims it, so the operator reaps it via the existing kill UI). No
migration, no one-off script.

## 2. Slices

### Slice 1 — Kill before dispose (independent, land first; stops the process leak)

- `server/Application/Services/AgentSessionService.cs`: the catch at `:370-381` and the outer
  catch at `:218-238` per D1.
- **Tests** (new `tests/Antiphon.Tests/Application/AgentSessionLaunchFailureTests.cs`, using the
  existing `FakeAgentProtocolAdapter`): `SendPromptAsync` throws `PromptDeliveryException` ⇒
  `KillAsync` observed before `DisposeAsync`, session `Failed`; resume-not-found ⇒ first adapter
  killed, fallback relaunch still runs and succeeds; card path where `:209` already killed and a
  later save throws ⇒ second kill harmless.

### Slice 2 — `/remote-control` best-effort + incident (independent; alone would have kept cefed08a Running)

- `AgentSessionService.cs`: `SendRemoteControlCommandsAsync` catches `PromptDeliveryException`
  (interactive AND card paths — it is monitoring on both), skips the `/rename`, raises
  `RcDegraded`, returns; armed-marker timeout also raises `RcDegraded`. No enum change (kind 7
  exists).
- **Tests** (same new file): RC delivery throws ⇒ session Running, `/rename` never sent, incident
  recorded, launch note still delivered, `FlushSessionAsync` still runs; work-prompt failure on
  the card path still fails the launch (now with kill).

### Slice 3 — Boot retry loop + transcript late-confirm (needs 1; best with 2)

- `AgentSessionService.cs`: `SendBootPromptWithRetryAsync` per D2; baseline capture + late-confirm
  helper per D3 (queries `TranscriptEntries` via `_db`; matcher = `TranscriptKinds.TryReadLocalCommandName`
  / `PromptSubmissionMatch.IsConfirmedBy` from `Antiphon.SessionRunner.Contracts` — already
  referenced).
- `server/Application/Settings/SupervisionSettings.cs`: `BootPromptAttempts` (3),
  `BootPromptRetryDelaySeconds` (2) on `DeliveryVerificationSettings`. ⚠ CARD-0055 just landed in
  this file — rebase, don't merge blind.
- **Tests**: first attempt throws, second succeeds ⇒ exactly 2 `SendPromptAsync` calls, session
  Running; all attempts throw + a seeded `<command-name>/remote-control</command-name>` row past
  baseline ⇒ treated delivered, no incident; all throw + no row ⇒ slice-2 degraded path; fresh
  session (zero entries) skips transcript confirm entirely; work prompt exhausting retries ⇒
  Failed + killed. A direct `PromptSubmissionMatchTests` case pinning that the wrapper text
  confirms `/remote-control` (lives in `tests/Antiphon.SessionRunner.Tests` — NOT the pty test
  project).

### Slice 4 — Reconciliation third pass (independent; the safety net and the retroactive cleanup)

- `server/Application/Services/SessionReconciliationService.cs`: third pass per D4; docstring
  "Two passes" → three.
- `server/Application/Settings/` (`SessionReconciliationSettings`): `ReAdoptEnabled`.
- `server/Domain/Enums/AgentIncidentKind.cs`: `SessionReAdopted = 20`.
- **Tests** (extend `tests/Antiphon.Tests/Application/SessionReconciliationServiceTests.cs`; fake
  runner client is per-test so runner-side data is isolated, but scope all DB assertions to this
  test's rows per the shared-Postgres rule): Failed + runner-Running + probe OK ⇒ row Running,
  agent restored when pointer matches; pointer clobbered ⇒ row Running, agent untouched
  (the cefed08a shape); probe fails ⇒ nothing changes + alert; Stopped + runner-Running ⇒ kill
  retried; runner session with no DB row ⇒ alert only; re-adopt cap ⇒ fourth flap escalates and
  stops; runner unreachable ⇒ pass skipped (existing behavior).

### Slice 5 — Docs, ops, close (last)

- CLAUDE.md gotcha bullet: a launch-failure catch must kill what it started (`DisposeAsync` on
  `RunnerClaudeAdapter` is a no-op); a monitoring command's failure must never fail a healthy
  session; DB-dead-vs-runner-alive is reconciliation's third pass and resolves by re-adoption,
  never by inferring kill from "unclaimed".
- Ops: run one sweep in prod; confirm `cefed08a` and `e12439ee` re-adopted and visible; operator
  reaps `e12439ee` (nothing claims it) via the kill UI; `cefed08a` is the operator's to keep.
- Close CARD-0056 with commit hashes; cross-note CARD-0055 (its D4 scope-out is now lifted for
  resume-mode boots only) and CARD-0047 4A (confirmed unrelated).

**Landing order** 1 → 2 → 3 → 4 → 5. Each is independently landable and testable; 1 stops the
leak, 2 removes the false-Failed, 3 makes the delivery actually succeed, 4 heals history and
future misses.

## Concurrent-work collision map

- **a4389709** (`tests/Antiphon.Agents.Pty.Tests`, `src/Antiphon.FakeClaude`): avoided by design —
  `VerifiedPromptSubmitter` and its test suite are untouched (D2 is caller-level). A fakeclaude
  boot-swallow model (`ANTIPHON_FAKE_BOOT_SWALLOW`, echoing nothing for the first N ms after
  banner) would let slice 3 be pinned through a real ConPTY — deliberately left OUT of this plan;
  offer it to a4389709's lane as a follow-up.
- **61595f32** (`AgentTaskDispatcher`, check services): no planned file overlaps, but confirm
  before slice 4 that "check services" does not include `SessionReconciliationService`, and
  expect `SupervisionSettings.cs` (slice 3) to be a shared-edit hotspot.
- `SessionMessageQueueService` is NOT touched (the late-confirm helper lives in
  `AgentSessionService` and shares only `PromptSubmissionMatch`).

## 3. What I could not determine, and what settles it

1. **What triggered cefed08a's 16:11:55Z relaunch** (operator restart for v2.1.233 vs supervisor
   resume). The server log around 17:11:5x local would say; it does not change the design.
2. **Whether swallowed boot input can surface late** (OS-buffered, echoing minutes later). The
   capture says never within 4.5 h, so this plan treats it as lost. A headed canary — write into
   a real Claude mid-resume-render, watch for late echo — would pin it. If it CAN surface late,
   D2's retry could double-type; the transcript late-confirm and the invalid-command shape of a
   doubled slash command bound the damage. A measured composer-clear keystroke (Esc/Ctrl-U
   semantics on an idle composer) is the missing mitigation — unmeasured today (CARD-0028
   measured typing loss, not clear keys).
3. **Whether a local-command record is written when the transcript file does not exist yet**
   (fresh-boot `/remote-control` creating the file). Untested; irrelevant here because fresh
   boots keep screen-only verification, but it decides whether CARD-0055's boot scope-out could
   ever be lifted for fresh sessions too.
4. **Which agent owned `e12439ee`** — cascade-deleted trail (per the triage); slice 4 surfaces it
   as unclaimed for manual reaping regardless, and slice 5 reaps it.
5. **Whether the per-session probe can hang on a half-dead pty-host.** The runner client's
   existing HTTP timeout bounds it; if it proves slow in practice, add a per-probe timeout knob
   to `SessionReconciliationSettings`.
