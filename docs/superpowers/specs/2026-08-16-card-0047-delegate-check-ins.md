# CARD-0047 — Check in on a delegate on a declared schedule

**Status:** Plan (not implemented). 2026-08-16.
**Card:** CARD-0047 "An orchestrator hears nothing until the report lands, and the report can be 90 minutes late - check in on a delegate on a declared schedule" (`5c568e5c-ad72-4dc6-9bf3-c3f9abf7e9ed`)
**Relates to:** CARD-0046 (settlement keys on the turn-ending response — a check must never be able to settle anything), CARD-0035 (the human diagnostic surface; this is the automatic probe behind it), CARD-0048 (the DA1 stall — why "quiet" is not evidence).
**Manual version:** `docs/orchestration-loop.md` §4 — this card automates exactly that section.

---

## 0. The finding that reframes the card: the notification pipeline is fast; the LAST HOP silently loses submissions

The card asks whether the notification delay is a separate defect. **It is, and it is now measured.**
Queried live (`SessionQueuedMessages` + `TranscriptEntries`, parent session
`cefed08a-fd4a-42a0-8c76-0fbf82cf6b20`):

- **All 24 delegation completion notes ever sent to that session were enqueued and typed into the
  pty within seconds of settlement** (worst observed lag enqueue→`SentAt`: 79 s, for a note that
  waited on WhenIdle; typical <1 s). The queue, the settlement path, and the WhenIdle machinery are
  **not** the defect.
- **`ea2feb92`**: note marked Sent 15:16:20Z — but it appears as a `UserPrompt` in the parent's
  transcript only at **17:00:09Z**, 104 minutes later (seq 548), at the exact moment the *next*
  note was delivered. The body sat in the composer; the next delivery's Enter submitted it.
- **`15c9150e`**: note marked Sent 17:00:08Z — its body **never appears in the transcript at all**.
  Its Enter is what submitted the stale ea2feb92 note; its own content was lost with the composer
  (next real prompt is the operator, seq 554, next morning: "why did we not hear anything").

Mechanism: `VerifiedPromptSubmitter.SubmitAsync` (`src/Antiphon.Agents.Pty/VerifiedPromptSubmitter.cs:78`)
verifies submission by **"output advanced after Enter"** — any redraw, spinner tick or cursor
repaint passes. It never confirms the prompt became a JSONL user record, which is the only ground
truth (`TranscriptEntries` has it within ~1 s via the tailer). So a delivery can be verified
"Delivered", marked Sent, and never have happened.

**Recommendation: file a separate card** — *"Delivery verification must confirm the prompt landed
in the transcript (a UserPrompt record within a window), not that Enter produced output; a
Sent-but-never-submitted delivery must retry the Enter and, failing that, revert to Pending and
raise an incident."* Suggested probe window: 30 s (measured tailer latency is ~1 s; a turn can
start slower under load). This affects EVERY queued delivery (channel replies, operator messages),
not just task notes.

**Consequence for CARD-0047's urgency:** with that card fixed, completion notification is
near-real-time and checking becomes a **safety net plus a progress channel**, not the primary
completion mechanism. CARD-0047 is still worth building — nothing else answers "is it working,
stuck, or finished?" *before* completion, and a safety net is exactly what a lost-submission bug
class needs — but its completion-detection role halves. Build both; the checker below deliberately
reports "has it settled" from the task row, so it also catches any future lost notification.

---

## 1. The six decisions

### 1.1 Where the timer lives: `AgentTaskDispatcher.TickAsync` — scheduling only, never execution

`AgentTaskDispatcherHostedService` ticks `TickAsync` every `PollIntervalSeconds` = **5 s**, and the
tick already runs four sweeps before its `queued.Count == 0` early return
(`AutoEscalateStalledAsync`, `FailNeverStartedAsync`, `RetireIdleWarmAgentsAsync`,
`SettleDeferredReportsAsync` — `AgentTaskDispatcher.cs:79-93`). Check-due times have minute
granularity, so a 5 s cadence is two orders of magnitude finer than needed. **Host the sweep
there** (`RunScheduledChecksAsync`, fifth in the list): it is where every other
"running-work-gone-quiet" clock already lives, and putting it in `SessionHealthHostedService`
would split task concerns across two services (the CARD-0046 spec already rejected that).

**But the tick is serial and a check takes seconds-to-a-minute.** A model call awaited inside
`TickAsync` would stall dispatching for its duration. So the sweep only **claims and hands off**:
it selects due tasks, advances `NextCheckAt` atomically (re-arm-before-run, so a crash mid-check
skips one check instead of looping), and enqueues the task id on an in-process
`System.Threading.Channels.Channel<Guid>` drained by a dedicated worker
(`AgentTaskCheckHostedService`) — the same shape as `AgentSessionLaunchQueue`. The tick never
blocks on a check.

### 1.2 Declaring expected duration: `-ExpectAbout <minutes>`, a hint that schedules the first check

- `scripts/delegate.ps1` gains `[int]$ExpectAbout` (Create set, range 1..1440) →
  `$body['expectedMinutes']`.
- `CreateAgentTaskRequest` gains `int? ExpectedMinutes = null`.
- `AgentTask` gains `ExpectedDurationMinutes` (int, non-null, stored resolved:
  request value or `DelegationSettings.DefaultExpectedMinutes` = **10**).
- At dispatch (`DispatchOneAsync`, alongside `DispatchedAt`):
  `NextCheckAt = DispatchedAt + ExpectedDurationMinutes`, but **only when
  `ReplyTo == Session`** — a check with no one to deliver to is dead weight; `NextCheckAt` stays
  null and the sweep never sees the task.
- It is a **hint, never a deadline**: no code path fails, escalates or kills a task off
  `ExpectedDurationMinutes` or `NextCheckAt`. The existing `AutoEscalateStalledAsync` /
  `FailNeverStartedAsync` clocks are untouched and independent. Enforced by the slice-1 test that
  drives a task far past its expected duration and asserts status is still Dispatched with no
  Failed/Escalated event.

### 1.3 Code gathers, a model interprets — the checker never runs probes

**Decision: a deterministic in-server probe builds a fact bundle; a tool-less haiku-tier call
turns it into 3–5 lines. The checker never gets tools, a shell, or a session.**

Argument, from the card's own measurements:

- Everything the card lists as a *working* probe is deterministic and the server can read it
  cheaper than any agent: task status/timestamps (`AgentTasks` row), the stored report
  (`Result`), commits on the branch (`git log`), files on disk (`git status`), session
  Running/Exited (`AgentSessions`), working/idle (`SessionMessageQueueService.IsWorkingAsync`,
  internal static — reusable), the transcript tail (`TranscriptEntries`). Zero model tokens.
- Every probe that *failed* on 2026-08-15/16 was an **improvised** one: inferring from silence,
  a process scan that matched its own scanning command, trusting prose over the repo, grepping
  `*.ansi.log`. An agent running its own probes is exactly the thing that improvises; a fact
  bundle is the limit case of the card's own lesson — "give the checker specific probes, not 'go
  and see how it's doing'" — with the probes promoted into reviewed, tested code. A checker
  pointed at logs produces noise; this one is pointed at the DB and two git reads, which is the
  endpoint-shaped end of that spectrum.
- Reliability compounds: the fact digest is deliverable **even when the model call fails**. The
  note then carries the raw digest alone — degraded, not absent. An agent that fails to boot
  (DA1, login, pool contention) delivers nothing.
- Cost/latency: the measured self-probing haiku agent was $0.123 / 61 s. A one-shot tool-less
  haiku call over a ~3 KB bundle is well under a cent and a few seconds. Both are affordable —
  the card is right — but 100× cheaper removes any temptation to check rarely.
- What is lost: an agent can follow a lead (read a file the transcript mentions). Answer: the
  check's job is **triage, not diagnosis**. Its contract is "doing / produced / looks stuck /
  settled"; when the facts are ambiguous it says so, and the orchestrator dispatches a real Debug
  agent through the machinery that already exists.

**The interpreter vehicle:** a headless `claude -p --model haiku` child process (JSON output for
usage/cost), via the same executable resolution sessions use (`AgentExecutableResolver`). A direct
`/v1/messages` call was rejected on a hard fact: **no API key is configured anywhere in this
deployment** — `Llm:Providers:*:ApiKey` empty in `server/appsettings.json`, all three `LlmProviders`
DB rows disabled with empty keys. All model access here is subscription-authed through the CLI.
The interpreter hides behind `IDelegateCheckInterpreter` so tests fake it and the fallback (route
the interpretation through the delegation pipeline as a haiku Worker — the measured $0.12 path) is
a swap, not a redesign, if headless `claude -p` proves unusable under the service account.

**Fact bundle contents** (all from the DB + two git reads; no HTTP, no process scans — the card's
self-matching process-scan trap is excluded by construction, not by care):

1. Task: status, title, role/tier, attempt, `DispatchedAt`, age vs `ExpectedDurationMinutes`,
   check number.
2. Session: exists / `Running` / `Exited`, working-or-idle verdict, age of last transcript entry.
3. Transcript tail: last ~10 entries as `kind` + first ~200 chars (AssistantText/ToolCall names) —
   structured rows, not logs.
4. Git, when the task has a repo: `git log --oneline <mergeTarget>..<branch>` (commit messages are
   the durable report in this repo) and `git status --porcelain` counts, both via
   `GitWorkspaceService` with `--no-optional-locks` (a bare `git status` refreshes the index — a
   write; the flag makes the probe honestly read-only).
5. Delegate-side queue: Pending messages on the delegate's session (a stranded WhenIdle delivery
   is a classic stall signature).
6. Open incidents on the delegate's session.

### 1.4 How the result reaches the orchestrator: the same queue, an unmistakable envelope, and it structurally cannot settle anything

Delivery reuses `SessionMessageQueueService.EnqueueAsync` to `ParentSessionId`, `WhenIdle`, with a
new `QueuedMessageOrigin.Check = 4` (int column — no migration) and conversation key
`check:<taskId:N>` (no batching branch added; checks are small and rare).

Note shape — first line always:

```
[check <delegateShortId> #<n>] <title> · <age> elapsed (expected <E>m) · session Running · working
```

then the interpretation (or the raw digest when the interpreter failed, prefixed
`(unverified digest — interpreter unavailable)`). It never begins with `[task`, never contains the
word `done`/`failed` in its header, and **never contains any task marker**.

Why it cannot be confused with a completion or settle anything, against CARD-0046's machinery:

- Settlement of the **delegate's** task reads the *delegate's* transcript
  (`ExtractMarkedTurnAsync` over `AgentSessionId`). The check note is delivered into the
  **parent's** session and the checker never touches the delegate's session or queue — there is no
  code path by which it can appear in the transcript settlement reads.
- Settlement of the **parent's own** task (when the parent is itself a delegate/orchestrator):
  the note arrives as a UserPrompt *without the parent's task marker*, so the marker gate
  (`AgentTaskReplyService.cs:766`) refuses the turn. This is the same shape completion notes
  already have today, and it is load-bearing: an unmarked note prompt is what shields a mid-flight
  orchestrator from settling on its reaction to a note. **Deliberately do NOT classify check notes
  (or completion notes) as housekeeping prompts** — if the walk-back skipped them back to the
  marked brief, the parent's reaction turn would pass the marker gate and settle the parent's task
  mid-flight. The existing once-per-session `DelegateReportUncorrelated` Warning incident on such
  parents is a known, already-present cost; checks add no new instances beyond the first note.
- Pinned by a slice-3 test: parent-as-orchestrator receives a check note, ends a turn reacting to
  it → parent task still Dispatched, no `Result`.

### 1.5 Re-arm and backoff

State on the task row: `NextCheckAt` (DateTime?), `CheckCount` (int). After each executed check:

```
interval(n) = clamp( max(CheckMinIntervalMinutes, ExpectedDurationMinutes / 2) * 2^(n-1),
                     ..., CheckMaxIntervalMinutes )
NextCheckAt = now + interval(CheckCount);  CheckCount++
```

Defaults: `CheckMinIntervalMinutes` = **5**, `CheckMaxIntervalMinutes` = **30**,
`CheckMaxCount` = **10**. A 10-minute task checks at ~10m, 15m, 25m, 45m…; a 3-hour task declared
`-ExpectAbout 180` first checks at 3 h. At $0.01/check the economics never bind; the cap exists so
a forgotten immortal task doesn't check forever.

Checking **stops** when: the task leaves Dispatched/Working (sweep filter — settlement needs no
bookkeeping); the parent session no longer exists or is Exited (set `NextCheckAt = null`, log —
nobody is listening); or `CheckCount` reaches `CheckMaxCount` (final note says checks are
exhausted). A check that finds the task already settled between claim and execution delivers
nothing. `CheckEnabled` (default true) turns the whole feature off; `-ExpectAbout 0` is rejected
by validation rather than meaning "never check" — opting out is not offered per-task until someone
needs it.

### 1.6 The read-only guarantee — enforced by construction, then by tests

- **The model has no tools.** The interpreter is `claude -p` with tool use disabled
  (`--disallowedTools` covering all tools; exact flag verified against the installed CLI in
  slice 4, with `--output-format json` asserting zero tool use per call), cwd set to the
  scratchpad, prompt = the bundle. It is handed no session id, no URL, no path into the repo — it
  *cannot* type, kill or commit, rather than being told not to.
- **The gatherer cannot write.** `DelegateCheckProbe`'s constructor takes `AppDbContext` (all
  queries `AsNoTracking`), `GitWorkspaceService` (only `log`/`status`/`rev-parse`, with
  `--no-optional-locks`), and `TimeProvider`. It does not depend on `SessionMessageQueueService`,
  `IDelegateSessionStopper`, any runner client, or anything else with a write surface — a reviewer
  can read the constructor and see the guarantee. Only the sweep (which owns scheduling state) and
  the note delivery (parent's queue only) write anything.
- **Tests pin it end to end** (slice 3): run a full check cycle against a seeded delegate; assert
  the delegate's session gained zero `SessionQueuedMessages`, zero `TranscriptEntries`, no status
  change, and the fake runner recorded zero writes to it. The card's process-scan trap is covered
  by absence: there is no process probe to get wrong.

---

## 2. Slices

Each slice lands green and independently revertable. 2 is independent of 1; 3 needs 1+2; 4 needs 3.

### Slice 1 — Declare and schedule (schema + plumbing)

**Files:** `server/Domain/Entities/AgentTask.cs` (`ExpectedDurationMinutes`, `NextCheckAt`,
`CheckCount`), migration `server/Migrations/2026xxxx_AddAgentTaskCheckSchedule.cs`,
`server/Application/Dtos/AgentTaskDtos.cs` (`ExpectedMinutes` on create; expose the three fields on
`AgentTaskSummaryDto`), `server/Application/Services/AgentTaskService.cs` (validate 1..1440, store,
default from settings), `server/Application/Services/AgentTaskDispatcher.cs` (`DispatchOneAsync`
sets `NextCheckAt` when `ReplyTo == Session`), `server/Application/Settings/DelegationSettings.cs`
(`CheckEnabled`, `DefaultExpectedMinutes`, `CheckMinIntervalMinutes`, `CheckMaxIntervalMinutes`,
`CheckMaxCount`), `scripts/delegate.ps1` (`-ExpectAbout`).

**Tests:** `AgentTaskServiceIntegrationTests` — explicit value stored; absent → 10; out-of-range
rejected. `DelegationUnitTests` — backoff arithmetic (pure function `CheckSchedule.NextInterval`).
Dispatch test — `NextCheckAt` set for `ReplyTo=Session`, null otherwise. The hint-not-deadline
test: advance a `FakeTimeProvider` far past expected → task untouched by every existing sweep.
`delegate.ps1` has no test harness; verify by one foreground `-ExpectAbout 5` run and read the row.

### Slice 2 — The probe (facts, no model, no writes)

**Files:** new `server/Application/Services/DelegateCheckProbe.cs` (`CheckFacts` record +
`GatherAsync(AgentTask, ct)` + `RenderDigest(CheckFacts)`), reusing
`SessionMessageQueueService.IsWorkingAsync` and `GitWorkspaceService` (add a read-only
`LogOnelineAsync(dir, from, to)` / ensure `--no-optional-locks` on the status call it exposes).

**Tests:** new `tests/Antiphon.Tests/Application/DelegateCheckProbeTests.cs` — seeded
task+session+transcript yields the right facts (running/exited, working/idle via real transcript
shapes, tail truncation); git facts against a temp repo (commits present/absent, no `.git` at all
→ git section absent, git failure → section says so rather than throwing); digest renders every
section; **read-only**: `SaveChanges` never called (probe context uses a save-throwing interceptor
in the test), index mtime unchanged across a status probe.

### Slice 3 — Sweep, execute, deliver (digest-only checks work end to end)

**Files:** `AgentTaskDispatcher.cs` (`RunScheduledChecksAsync` — claim due rows, re-arm via
§1.5, hand ids to the channel), new
`server/Infrastructure/Orchestration/AgentTaskCheckHostedService.cs` + small
`AgentTaskCheckQueue` (the `Channel<Guid>`), new
`server/Application/Services/AgentTaskCheckService.cs` (`RunCheckAsync`: re-read status → probe →
interpret (slice 4; until then digest-only) → build note → enqueue to parent, origin Check →
`AgentTaskEvent` type `Check = 13` recording digest head + interpreter cost),
`server/Domain/Enums/QueuedMessageOrigin.cs` (`Check = 4`),
`server/Domain/Enums/AgentTaskEnums.cs` (`Check = 13`).

**Tests:** new `tests/Antiphon.Tests/Application/AgentTaskCheckSweepTests.cs` — due/not-due
selection; re-arm-before-run (a throwing check still advances `NextCheckAt`); backoff progression
and `CheckMaxCount` stop; dead-parent stop; settled-between-claim-and-run delivers nothing; note
header shape (`[check `, never `[task `, contains no `TaskMarker` of any task); **the
cannot-settle test** (§1.4): parent-as-orchestrator ends a turn after a check note → parent task
unsettled; **the read-only test** (§1.6). Reuse `AgentTaskDeliveryWatchdogTests`' harness
(`FakeTimeProvider`-style seeding is already the house pattern in these suites).

### Slice 4 — The interpreter (haiku reads the bundle)

**Files:** new `server/Application/Interfaces/IDelegateCheckInterpreter.cs`, new
`server/Infrastructure/Agents/HeadlessClaudeCheckInterpreter.cs` (`claude -p --model haiku
--output-format json`, tools disabled, 90 s timeout, cwd = a scratch dir; returns text + cost;
any failure → null, logged), wire into `AgentTaskCheckService` (null → digest-only note with the
degraded prefix). Interpreter cost goes in the `Check` event detail, **not** into `CostUsd` — the
task's cost stays the delegate's own work, and the per-root ceiling keeps meaning what it means.

**Tests:** service tests with a fake interpreter (success shapes the note; failure degrades to
digest; timeout respected). One `[Explicit]` canary `HeadlessClaudeCheckCanaryTests` (pattern:
`ClaudeLocalCommandCanaryTests`) proving the real CLI answers a bundle headless under this
account and that the tool-restriction flags hold — this is also where the `claude -p`
open question (§4.2) gets its answer before the slice merges.

### Slice 5 — Docs

`.claude/skills/antiphon-delegate/SKILL.md`: document `-ExpectAbout`, what a `[check …]` note is
(and that it is never a completion), and that callers should declare honest durations rather than
padding. `docs/orchestration-loop.md` §4: note the manual forensics are now automated, keep the
manual commands as the fallback. No tests.

### Not a slice — file the separate card from §0

The submission-verification defect (transcript-confirmed submission in
`VerifiedPromptSubmitter`, revert-to-Pending + incident on failure) is its own card with §0's
evidence pasted in. It is not part of CARD-0047's implementation and must not ride these slices.

---

## 3. Landing order

1 → 2 → 3 → 4 → 5. After slice 3 the feature is already useful (deterministic digests answer
"settled? committed? running? idle?" — most of the manual §4 checklist); slice 4 adds the
three-line reading on top.

---

## 4. What I could not determine, and what would settle it

1. **Why the Enter did not submit at 15:16:20 on 2026-08-15.** The strand and its mechanism-of-
   recovery are measured (§0); the composer's state at that moment (paste-placeholder mode? a
   transient dialog? the modern backend's single-write rule violated by body+CR pacing?) is not.
   Evidence: the session-runner's log and the pty ansi capture for session `cefed08a` around
   15:16:20Z, read once, before writing the separate card's fix. This does not block CARD-0047.
2. **Whether headless `claude -p` works under the server's service account** (subscription auth,
   MSIX pathing, and the exact tool-disable flags of the installed CLI). Settled by the slice-4
   canary run in the foreground before the slice merges. Fallback is designed in: swap
   `IDelegateCheckInterpreter` to dispatch a haiku Worker through the existing pipeline — the
   card's own measured $0.123/61 s datapoint — with no change to slices 1–3.
3. **Whether check notes should also reach channel-bound orchestrators** (Telegram). Out of scope
   here; the origin enum and note shape don't preclude a later `ChannelAlertRouter` hookup.
4. **Whether `ExpectedDurationMinutes` should feed `AutoEscalateStalledAsync`** (a declared-slow
   task escalating later than a declared-fast one). Deliberately not done — the card says hint,
   never deadline — but the column makes the experiment cheap if stall data ever argues for it.
5. **The interpreter's transcript-tail privacy.** The bundle contains delegate transcript text and
   is sent to the model endpoint the CLI is already authed to — the same place the transcript
   already went when it was generated. No new exposure identified; noted for completeness.
