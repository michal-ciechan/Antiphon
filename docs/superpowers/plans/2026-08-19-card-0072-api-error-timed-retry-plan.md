# CARD-0072 — a timed retry for a dead API-error turn: what it costs, and the one slice the spec mis-scoped

- **Date**: 2026-08-19
- **Status**: Plan (planning only — no fix written in this pass)
- **Card**: CARD-0072 (*A dead API-error turn (529/429) is invisible to every safety net — propose a
  timed retry*)
- **Governing spec**: `docs/superpowers/specs/2026-08-17-usage-limit-and-api-error-resilience.md`
  (§2: "0072 closes when **S1 + S5** land"; S1 shipped `3c9728f`)
- **Siblings**: CARD-0071 closed today (`b4fda1a` / `254f2f9` / `aedbc6b`) — a dead turn can no
  longer be published or settled as success. CARD-0022 owns the Wall class end to end (S4 + S6).
  CARD-0083 owns making detection provider-neutral; its S1 survey landed today and says Grok/Codex
  walls are **Unknown**, so this card stays Claude-shaped.
- **Evidence**: live database (`antiphon` on 17280), the running runner (pid 43424), the shipped
  code, and CARD-0072's own all-time sweep (23 stubs / 14 sessions — **not re-run**, per the spec).

> **This is a planning document only.** Nothing below was implemented in this pass.

---

## Verdict up front

| Question | Answer |
|---|---|
| Is there already a scoped design? | **Yes** — spec §D3/§D4/§D8 and slice **S5**. Followed, not re-designed. |
| Can CARD-0072 close on the spec's S5 as written? | **No.** S5 bundles the Wall arm (needs S4: `UsageLimitResetParser` + `UsageLimitState`) with the Transient ladder. S4 is **CARD-0022's** slice and unbuilt, so "0072 closes on S1+S5" and "S5 depends on S4" contradict each other. **S5 must be split.** |
| What does CARD-0072 actually own, then? | **S5a** — the Transient/Unknown ladder, the NeedsHuman terminal, the durable schedule, and the give-up policy. **S5b** (parse the reset, schedule one resume at it) rides CARD-0022 with S4. |
| Does the retry mechanism need new scaffolding? | **No.** A third sweep in `AgentSupervisorHostedService`, beside CARD-0067's and CARD-0082's. The spec's `ApiErrorRetryQueue` / `ApiErrorRetryHostedService` should **not** be built — §3. |
| Biggest hazard? | Not the re-prompt. It is **`AgentTaskDispatcher.SettleDeferredReportsAsync` losing its self-limiting property** the moment a stub-killed task stays `Working` — §5.1. |
| Any coverage nobody has? | Yes: a **task-less session** (the orchestrator `cefed08a` this card was filed about) raises **no incident at all** today. S3 is task-scoped. Only S5a closes it — §5.4. |
| Can any of this be validated against live traffic? | **No.** Zero stubs ingested since S1 shipped (§2). Fixtures are the whole proof. |

---

## 1. What is already shipped, and what it leaves undone

| Commit | Slice | Card | State |
|---|---|---|---|
| `3c9728f` | S1 — `IsApiError` / `ApiErrorClass` / `ApiErrorStatus` across the runner boundary; `TranscriptKinds.IsApiErrorStub`; `ApiErrorClassifier` | 0072 | **Shipped** |
| `b4fda1a` | S2 — channel-reply guard | 0071 | Shipped |
| `254f2f9` | S3 — settlement guard + `ApiErrorTurnDied = 22` | 0071 | Shipped |
| `aedbc6b` | S8 — review-thread guard | 0071 | Shipped today |

`ApiErrorClassifier.Classify` (`server/Application/Services/ApiErrorClassifier.cs`) is complete and
pure: class first (`rate_limit`→Wall, `server_error`→Transient, `authentication_failed` /
`model_not_found`→NeedsHuman), HTTP status as the weaker fallback (429→Wall, ≥500→Transient),
otherwise Unknown. Its only consumer today is `AgentTaskReplyService.FailApiErrorTurnAsync`
(`:649`), which uses it to word a failure reason. **Nothing acts on the classification.**

So the pipeline currently ends a dead turn like this:

- **Delegate task session** → task `Failed`, delegate released, parent told, incident 22 raised.
  Correct as a terminal, and explicitly documented in that method as "the no-resume-coming half of
  §D6" — the arm that changes when S5 lands.
- **Orchestrator / always-on / channel-bound session with no task row** → the channel correlation
  stays owed (S2) and the CARD-0067 TTL eventually raises `ChannelReplyLost`, **but nothing else
  happens at all**: no incident, no retry, no record that the turn died. That is precisely the
  31-minute silence the card opens with, unchanged.

Nothing retries, in either case.

---

## 2. There is no live data, and there will not be before this ships

Measured today against the live database (71 076 transcript rows):

| Query | Result |
|---|---|
| `TranscriptEntries` where `IsApiError = true` | **0** |
| `TranscriptEntries` where `IsApiError IS NOT NULL` | **0** |
| `AgentIncidents` where `Kind = 22` | **0** |

Unchanged from CARD-0071's measurement 279 rows ago, and expected: `GetBool` returns null when the
JSONL property is absent, so only two shapes ever write a non-null value (a stub → `true`, the
benign `"No response requested."` synthetic → `false`) and neither has been ingested since S1
landed. Carriage itself is proven on the wire (CARD-0071 §1.1, live runner payload).

**Consequence for this card, and it is the testing stance**: S5a will ship without ever having seen
a real stub in this database. Fixtures are the proof — the same posture the spec's §3 already takes
("a headed canary that hits a real limit on purpose is neither reproducible nor affordable"). Do not
plan a live-verification step; plan a fixture that fails loudly if the shape drifts.

Two facts that condition the work:

- **The runner already carries S1.** pid 43424, started 2026-08-19 03:43, and its wire payload
  reports the three fields. No runner restart is required for S5a — it is server-side only. (The
  `Model` column is still null on all 71 076 rows because that field is CARD-0082's `2e8106c`,
  07:28, which the running runner predates. Different card, same deploy-lag rule.)
- **Text matching is still the wrong signal, and I re-demonstrated it by accident.** Querying
  `Text LIKE 'API Error%' OR Text LIKE '%hit your session limit%'` to look for recent stubs returned
  **this session's own transcript rows** — an agent reading this very card out loud. Spec §D1's
  rejection of text as the primary signal is not theoretical.

---

## 3. The mechanism: a third sweep, not a new queue and hosted service

The spec's §3 names `ApiErrorRetryQueue` / `ApiErrorRetryHostedService`, "the `AgentTaskCheckQueue`
scaffold shape". **Recommend building neither.** The spec was written 2026-08-17; CARD-0082's sweep
landed after it and is the better-fitting precedent now.

`AgentSupervisorHostedService` (`server/Infrastructure/Supervision/`) already runs a `PeriodicTimer`
tick with two piggy-backed one-minute sweeps: `ChannelReplyDispatcher.SweepStaleCorrelationsAsync`
(CARD-0067) and `ContextCompactionService.SweepAsync` (CARD-0082). A third `ApiErrorRecoveryService.SweepAsync`
is ~10 lines of wiring there and inherits the tick's error handling.

Four reasons the queue-and-worker shape is actively wrong here:

1. **`AgentTaskCheckQueue` is a hand-off channel, not a schedule.** Its durability lives in
   `AgentTask.NextCheckAt`, swept by `AgentTaskDispatcher`. Copying the queue copies the half that
   does not survive a restart.
2. **Restart-durability is the entire point of the card.** An in-memory schedule loses every pending
   resume when the server restarts — which happens routinely here (AppHost watchdog, rebuilds). A
   retry that evaporates on restart reproduces the card's own complaint. This is CARD-0067's lesson
   verbatim: two halves of one round trip, one durable and one not.
3. **The reason `AgentTaskCheckQueue` exists does not apply.** It exists so a *model call* never
   stalls the serial 5-second dispatcher tick. The retry action here is one `EnqueueAsync` — a DB
   insert.
4. **A sweep is backfill-proof and replay-proof for free.** It reads stored rows, so it needs no hook
   in `ObserveTranscriptAsync` *and* no second hook in the backfill path, and cannot miss a stub
   ingested while anything was down.

**Granularity**: a one-minute sweep against a one-minute first rung means the first retry lands 1–2
minutes after the stub. Against a 31-minute silence, acceptable; state it in the settings XML-doc so
nobody later "fixes" the ladder's first rung wondering why it is late.

**Cost**: the adopt pass queries `IsApiError = true`. Add a **partial index**
(`HasFilter("\"IsApiError\" = true\"")`) — without it that is a 71k-row scan every minute, growing.

---

## 4. Slices

### S5a-1 — durable state + the pure ladder *(sonnet)*

Shippable alone: rows get written and logged, nothing fires. Same "state + log only is still
shippable" posture the spec gives S4.

- **`server/Domain/Entities/ApiErrorRecovery.cs`** (new) + migration:
  `Id`, `AgentSessionId`, `StubSequence` (long), `StubUuid` (string?, forensics only),
  `Classification`, `ApiErrorClass`, `ApiErrorStatus`, `DetectedAt`, `AttemptCount`,
  `NextAttemptAt` (DateTime? — null means parked or resolved), `ResolvedAt`, `ResolvedReason`,
  `LastEnqueuedAt`.
  - **Unique index `(AgentSessionId, StubSequence)`** — this is the dedup key, and it deliberately
    departs from the spec's "keyed on the stub's transcript `Uuid`": `TranscriptEntry.Uuid` is
    **nullable**, and the normalizer stamps one JSONL line onto *two* rows (`AssistantText` +
    `TurnEnd`), so a uuid key is both null-prone and ambiguous. Sequence is unique per session by the
    existing index. Key on the **`TurnEnd`** row's sequence — the turn's end is what is being retried.
  - Index on `NextAttemptAt` for the due query.
- **`server/Application/Services/ApiErrorRetrySchedule.cs`** (new, pure — `CheckSchedule`'s shape: no
  clock, no DB): `Interval(attemptNumber)` → **1, 3, 5, 10, 30, 60 minutes, then 60 indefinitely**
  (operator-requested; §D3). Deliberately **not** `CheckSchedule` — its 5-minute rounding floor eats
  the 1- and 3-minute rungs, and it answers a different question (back off a delegate that might
  legitimately be busy vs. retry a session structurally proven dead-idle).
- **`ApiErrorRecoveryService.SweepAsync`** (new) — **adopt pass only** in this slice: find
  `TranscriptEntries` with `IsApiError = true`, `Kind = TurnEnd`, `CreatedAt` within
  `AdoptWindowMinutes`, with no `ApiErrorRecovery` row; classify via `ApiErrorClassifier`; insert the
  row with `NextAttemptAt = DetectedAt + Interval(1)` (or null for NeedsHuman); log. Wired into
  `AgentSupervisorHostedService` beside the other two sweeps.
- **`ApiErrorRecoverySettings`** in `server/Application/Settings/SupervisionSettings.cs` beside
  `DeliveryVerificationSettings`: `Enabled` (default true), `SweepPeriodSeconds`,
  `AdoptWindowMinutes`, `UnknownAttemptCap` (3), `DeadTimeWarningHours` (2), `TransientPrompt`,
  `WallPrompt`.

**Tests**: `ApiErrorRetryScheduleTests` (pure arithmetic — every rung, the hourly plateau, clamped
input); adopt-pass integration scoped strictly to its own rows (shared-Postgres rule) — a stub
adopts once, a re-sweep does not double-insert, a benign `IsApiError = false` synthetic is never
adopted, a stub older than the window is not adopted.

### S5a-2 — the resume *(opus — the hazardous slice)*

`ApiErrorRecoveryService` grows the **fire pass**. Order matters and each step has a named reason:

1. **Skip** unless the session is `Running` **and** in `_runtime.ListLiveSessions()` (§D4: this spec
   only ever continues a live idle session; `SessionRestartBoundary` owns relaunches).
2. **`await _runtime.CatchUpTranscriptAsync(sessionId, ct)` before deciding anything.** The standing
   rule — never act on "the transcript does not contain X" without pulling first (CARD-0055's
   `GraceConfirmAsync` lesson) — and `ContextCompactionService.ProcessSessionAsync` already does
   exactly this before its own enqueue.
3. **Resolve `Superseded`, do not fire**, if any `UserPrompt` with `Sequence > StubSequence` exists
   (a human or anything else already continued it — §D4's staleness rule), or if a **newer** stub row
   exists for the session (the ladder tracks the latest death, not an old one).
4. **Enqueue**: `_queue.EnqueueAsync(sessionId, prompt, MessageSendMode.WhenIdle, ct, origin: QueuedMessageOrigin.Supervision)`.
   **No new typing path** — every CARD-0055/0056 guard applies unchanged, which is the whole safety
   argument (§D4). `Supervision` rather than `System` is a deliberate deviation from
   `EnqueueResumeContinueAsync`: `Supervision`'s queue rule is *cancel, never strand / cancel, never
   park*, which is exactly right for a rung that the next sweep re-derives anyway — a `System`
   message left Pending would surface at an arbitrary later moment beside a live rung.
5. **Advance**: `AttemptCount++`, `NextAttemptAt = now + Interval(AttemptCount)`, `LastEnqueuedAt`.
6. **Give up per §D8**: Unknown capped at `UnknownAttemptCap` (3) then parked; Transient hourly
   indefinitely, but a **Warning** incident once `now - DetectedAt` crosses `DeadTimeWarningHours`,
   **Critical when the session's agent is channel-bound** (the az-care shape: 14.5h of silence at a
   real human). Same `ApiErrorTurnDied = 22` escalated, not a new kind.
7. **NeedsHuman**: never scheduled — row created already resolved, incident raised immediately,
   Critical (auth-expired is one shared account; the whole fleet is broken).

Incidents here are **session-scoped**, not task-scoped: look up the owner via
`Agents.PersistentSessionId == sessionId` and call `AgentSupervisorService.RecordIncidentAsync` in
its own scope — the exact `ChannelReplyDispatcher.ReportLostAsync` (`:445-495`) precedent, including
its "no agent owns this session ⇒ log at Error, never swallow" arm.

**Wall, until CARD-0022's S4 lands** — the one judgement call in this plan, and it is the caller's to
overrule. 18 of 23 measured stubs are Wall, so a CARD-0072 that skips them retries 9% of reality.
Recommend using the spec's **own** designed degrade rather than inventing anything: §D3 already says
a Wall whose reset text fails to parse "degrades to the Transient ladder **entering at the 30-minute
rung**, plus a Warning incident quoting the unparsed text". With no parser built, every Wall is
unparseable, so that path is correct by construction and self-cancels when S4 ships. Add §D8's wall
cap: **3 consecutive wall deaths on one session** parks it and escalates Critical, so a 30-minute
nudge at a five-hour quota wall costs at most three cheap deliveries instead of ten.

**Tests**: fires at the rung and not before; skips and resolves on a later `UserPrompt`; skips a
non-`Running` session; NeedsHuman never schedules; Unknown parks at 3; the dead-time incident's
Warning/Critical pair by channel-binding; **assert the resume goes through `EnqueueAsync` only** (no
new send path — the spec's explicit S5 test); a wall parks after 3 deaths.

### S5a-3 — activate S3's defer arm *(opus)*

`AgentTaskReplyService.FailApiErrorTurnAsync` (`:649`) is documented as the terminal for
"no resume coming". With S5a-2 landed, a **retryable** class must instead leave the task `Working`
with an `AgentTaskEvent` naming the class and the scheduled fire time (§D6), and settle normally when
the resumed turn reports.

Three things it must get right, all of which are easy to get wrong:

- **Do not call `ReleaseDelegateAsync`.** The delegate still owns the session for its resumed turn;
  releasing would free the agent and let something else be dispatched into it.
- **Do not deliver a failure to the parent** (`DeliverToParentAsync`) — the task has not failed.
- **Idempotency, or it loops** — see §5.1. Key the "already deferred" decision on the
  `ApiErrorRecovery` row from S5a-1, and write the `AgentTaskEvent` exactly once.

The terminal arm stays for NeedsHuman, Unknown-exhausted, and wall-parked.

**Tests**: a Transient stub on a task session leaves it `Working` with one event and no release; a
second `OnTurnEndAsync` on the same stub adds nothing; NeedsHuman still Fails through the old arm;
a parked recovery Fails the task with the reason naming exhaustion.

**Order**: S5a-1 → S5a-2 → S5a-3. CARD-0072 closes when all three land.

---

## 5. Hazards to name before anyone writes code

### 5.1 `SettleDeferredReportsAsync` stops being self-limiting

`AgentTaskDispatcher.SettleDeferredReportsAsync` (`:677`) scans **every** task in `Dispatched` or
`Working` with a session and re-calls `AgentTaskReplyService.OnTurnEndAsync`. Its own doc comment
says it "is self-limiting, because the settlement it triggers takes the task out of Dispatched and
out of this scan." **S5a-3 removes exactly that property**: a deferred task stays `Working`, so the
sweep re-triggers `OnTurnEndAsync` on the same dead turn every pass, forever. Without an explicit
idempotency marker that is a duplicated `AgentTaskEvent` per sweep and a re-entered defer arm per
sweep. The `ApiErrorRecovery` row is the marker; the test in S5a-3 that calls `OnTurnEndAsync` twice
is the pin.

### 5.2 The re-prompt itself is the *safe* part — do not over-guard it

Spec §D4 confronts this directly and the reasoning holds: unlike CARD-0055/0056, where a quiet
screen was used to *infer* death, here Claude Code wrote a persisted record declaring the turn over
and returned the composer to prompt. Every measured revival in the sweep was a human typing
`Continue` into precisely this state and it worked immediately. The positive evidence is the stub row
itself. Adding screen-reading on top would be re-deriving a fact already in the database.

### 5.3 A resume must never be the thing that starts a session

`ListLiveSessions()` + `Status == Running` is not a formality. `5aeb93ea` (the delegate worktree that
died on a 429 and was never resumed) is dead in a directory that may not exist any more; relaunching
is `SessionRestartBoundary`'s job and its guards, not this sweep's.

### 5.4 Task-less sessions are the card's own motivating case and nobody covers them

`cefed08a` — the orchestrator this card was filed about — has no `AgentTask` row, so S3's arm never
fires for it and **no incident is raised today**. `AgentTaskCheckHostedService` only ever fires for
tasks (the card says so in its §3 and the code agrees). The recovery sweep is keyed on the
*session*, so it covers orchestrators, always-on agents and channel-bound agents for the first time.
Say so on the card when it closes: this is not just retry, it is the first detection those sessions
have ever had.

### 5.5 Do not generalize to Grok or Codex here

CARD-0083's S1 survey (`docs/investigations/2026-08-19-provider-usage-limit-shapes.md`, today)
measured Grok's only error-shaped ACP row as `retry_state` — **Grok's own mid-turn ladder, cap 15**,
not a dead-turn stub — and records Grok's documented hook class `rate_limit` as covering 503/529,
which would map a *Transient* onto *Wall* if copied. Grok/Codex walls are Unknown. Nothing on Grok or
Codex stamps `IsApiError` today, so S5a is Claude-only by construction; CARD-0083's S4 makes the
detection seam provider-neutral, and this sweep inherits it for free because it reads the neutral
columns.

---

## 6. Deliberately NOT in scope

- **`UsageLimitResetParser` and `UsageLimitState`** (spec S4) — CARD-0022. S5a's wall handling is the
  spec's own parse-failure degrade, not a substitute parser.
- **Fleet dispatch pause and `AttentionKind.UsageLimitExhausted`** (spec S6) — CARD-0022.
- **Auto-salvage of a dirty shared checkout** — §D6 rejected it for v1 and §6.4 keeps it an open
  operator decision. The wall prompt's "commit any in-progress work to a branch first" sentence is
  the shipped answer, and it rides S5a-2's degraded wall arm.
- **S7 transcript rendering** — cosmetic, optional.
- **Retrying anything Claude Code already retries.** By the time a stub exists its internal budget is
  spent (measured 209s on the 529). This card starts after that point.

---

## 7. Verification

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c72/ \
  --treenode-filter "/*/*/ApiErrorRetryScheduleTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c72/ \
  --treenode-filter "/*/*/ApiErrorRecoveryServiceTests/*"
```

Then the suites the change can disturb:

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c72/ \
  --treenode-filter "/*/*/AgentTaskReplyIntegrationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c72/ \
  --treenode-filter "/*/*/ApiErrorClassifierTests/*"
```

Trailing **forward** slash on `OutputPath`; delete the `bin-c72/` directories afterwards
(`Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-c72 | Remove-Item -Recurse -Force`).

Migration via CLI only: `dotnet ef migrations add AddApiErrorRecovery --project server`, then
`dotnet ef migrations has-pending-model-changes --project server` (expect "no changes").

**No runner restart required** — S5a is server-side and the running runner already carries S1's
fields.

---

## 8. Recommended commit lines

```
feat(resilience): CARD-0072 - durable API-error recovery rows and the retry ladder
feat(resilience): CARD-0072 - resume a session whose turn was killed by a transient API error
fix(delegation): CARD-0072 - a retryable API-error death defers the task instead of failing it
```
