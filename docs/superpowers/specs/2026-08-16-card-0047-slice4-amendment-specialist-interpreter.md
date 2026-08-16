# CARD-0047 slice 4 AMENDMENT — the interpreter is a supervised Antiphon agent, not a CLI subprocess

**Status:** Plan (not implemented). 2026-08-16.
**Supersedes:** slice 4 of `2026-08-16-card-0047-delegate-check-ins.md` (spec 0ba6c2d) — the
`HeadlessClaudeCheckInterpreter` / `claude -p` design is **withdrawn**. Slices 1–3 and 5 are shipped
on master (4c22529, ede9b75, d187c62, a02eb2d) and are untouched by this amendment: the
deterministic probe, digest, sweep, envelope and delivery all stand exactly as landed.
**Card:** CARD-0047. **Relates to:** CARD-0055 (a queued delivery can be marked Sent without ever
being submitted — nothing here may trust queue status), CARD-0002/CARD-0031 (delegations board
readability), CARD-0046 (settlement marker gate — the machinery this amendment leans on instead of
rebuilding).

## 0. Why the headless CLI is out (taken as given)

A `claude -p` child process is invisible to Antiphon — no agent row, no supervision, no incident
trail, no cost accounting — which is precisely the blindness this product exists to replace. And
there is no API key configured anywhere in this deployment; CLI auth under the service account was
an unretired risk (original spec §4.2). The replacement is a **long-running, supervised Antiphon
agent that specialises in check-interpretation**: visible on every existing surface, restarted by
the machinery that restarts every AlwaysOn agent, warm so a check pays no cold start, and
accumulating exactly the context a bundle-reader benefits from.

The non-negotiable from the original design survives intact: **the deterministic digest from
slice 3 ships today and always delivers.** The specialist is garnish on top of it. Down, busy,
slow, absent, or disabled — the check note still goes out, digest body, degraded prefix. No slice
below introduces a hard dependency on the specialist, and `Delegation:CheckInterpreterEnabled=false`
returns the system to exactly today's behaviour.

## 1. The six decisions

### 1.1 How work reaches the specialist and how the answer comes back: **a pinned AgentTask**

Two options weighed:

**(a) Dispatch an AgentTask pinned to the specialist** (recommended). The check worker creates a
task (Role `Check`, pinned `AgentId`, `ReplyTo = None`), the dispatcher delivers the brief into the
specialist's live session, and the answer comes back as `task.Result` through the CARD-0046
settlement path — marker gate, transcript extraction, per-task token/cost accounting
(`AgentTaskReplyService.cs:245` stores `Result` before the `ReplyTo` check at line 656, so
`ReplyTo=None` still yields the text). Delivery confirmation is the **transcript**, not the queue:
settlement only fires on a marked turn that actually happened, which is the exact property
CARD-0055 proved the queue's `Sent` flag lacks. A lost brief is caught by the existing
`FailNeverStartedAsync` backstop; from the check's point of view it is just a timeout → degraded
digest. Incidents, the task timeline, and the board come free.
Cost of this option: one task row per interpreted check. Mitigated in §1.1.1.

**(b) Enqueue a session message and read the reply from the specialist's transcript.** Less
machinery on the surface, but every hard part of (a) must be rebuilt by hand: correlation needs a
home-made token in the prompt and a transcript scan for an answer carrying it — a private
re-implementation of `ExtractMarkedTurnAsync` and the marker gate, without their tests; delivery
confirmation cannot be the queue's `Sent` (CARD-0055), so the scan must also verify the prompt
became a `UserPrompt` record and re-drive Enter when it didn't — which is the unshipped fix of the
CARD-0055 card itself, smuggled in as a side effect; and the interpretation's spend lands nowhere —
session-level usage is not attributable to a specific check without inventing per-turn cost
attribution. Rejected: it re-derives settlement minus the guarantees.

**Blocking wait:** `RunCheckAsync` creates the interpretation task, then polls its row every 2 s up
to `CheckInterpreterWaitSeconds` (default **60**). Settled with a `Result` → the note carries the
interpretation. Not settled in time → deliver the digest now with the degraded prefix; if the
interpretation task is still Queued, cancel it (`AgentTaskService.CancelAsync`); if already
Dispatched, let it finish and settle onto its own row (spend is committed; its late text is
recorded there and **never** delivered as a second note).

#### 1.1.1 The board does not drown

`AgentTaskRole` gains `Check = 11`. `AgentTaskService.ListAsync` excludes `Role == Check` unless
`includeChecks=true` — server-side default, so the delegations board (CARD-0002/0031) never sees
them without asking. Correlation is preserved both ways: the interpretation task's `Title` names
the checked task (`check #n on task <shortId>`), and the checked task's `Check` event detail names
the interpretation task's short id and cost.

#### 1.1.2 Interpretation tasks bypass `MaxConcurrentTasks`

The cap exists to bound concurrent Claude processes. A pinned task delivered into an
already-running always-on session spawns nothing, so `TickAsync` excludes `Role == Check` from the
`active` count and from the cap test. Without this, a system at the cap would starve every
interpretation and silently degrade all checks exactly when the operator most wants eyes on the
fleet. Their own backlog is bounded separately (§1.3).

### 1.2 Lifecycle and identity

- **Identity:** one standing agent, slug/name from `Delegation:CheckInterpreterAgentSlug`
  (default `antiphon-check-interpreter`). `AlwaysOn = true`, `ModelLevel = Low` (haiku),
  `RemoteControlEnabled = false`, `IsPoolDelegate = false` (the pool janitor
  `RetireIdleWarmAgentsAsync` filters on `IsPoolDelegate`, so it can never retire it).
- **Working directory:** its own scratch cwd, default
  `<first Delegation:AllowedRoots entry>\.antiphon\check-interpreter\` (knob:
  `CheckInterpreterWorkingDirectory`), created by the provisioner. A distinct cwd gives it a
  distinct Claude transcript project dir — it never shares `C:/src/Antiphon`'s transcript root with
  the operator (the CARD-0006 binding hazard is avoided by construction, not just by the C1–C4
  rules).
- **Creation/self-heal:** a new `CheckInterpreterProvisioner` (`EnsureAsync`) — idempotent
  find-by-slug-or-create — is called once at `AgentTaskCheckHostedService` startup and again on any
  check that finds the agent missing. If the agent is **deleted**, the next check recreates it (that
  check degrades; the next one is warm). If its **session dies**, `AgentSupervisorService` restarts
  it — its sweep already "ensures every AlwaysOn agent that is not user-suspended has a live
  session" (`AgentSupervisorService.cs:15,82`) — so no new supervision code is written. The
  provisioner also calls `AgentControlService.StartAsync` on first creation so the first checks
  don't wait for a supervision tick.
- **Every failure mode degrades, never blocks:** agent missing (this check), session down
  (supervisor's problem; this check times out), dispatcher not delivering, brief lost — all end in
  the same place: digest with degraded prefix, reason named.

### 1.3 Concurrency: serialise on the agent, bound the backlog, degrade past the bound

There is one specialist and many delegates can come due at once.

- **Serialisation is the dispatcher's, not new code:** a pinned task whose agent already has an
  active task waits Queued (the standing-agent dispatch gate in slice 4A is
  "any Dispatched/Working task with this `AgentId` → `WaitForAgent`"). Briefs therefore never
  interleave into a mid-flight turn — the same invariant the warm pool already holds.
- **Depth policy:** before creating an interpretation task, the check worker counts
  Queued/Dispatched/Working `Role == Check` tasks pinned to the specialist. At or above
  `CheckInterpreterMaxBacklog` (default **2**) it skips creation entirely and delivers the digest
  with prefix `(unverified digest — interpreter busy)`. So the queue can never grow beyond ~2 and
  no check waits behind a pile.
- **A check that waits longer than its budget degrades:** the 60 s wait is well under
  `CheckMinIntervalMinutes` (5 m), so a check can never still be waiting when its successor comes
  due. The check worker stays a single serial drainer (as shipped); its worst-case stall per check
  is the wait budget, and check due-times have minute granularity.

### 1.4 Specialisation: the contract is code, the agent row is a projection of it

The standing instructions live in `Agent.SystemPromptAppend` — already rendered into
`--append-system-prompt` on **every** launch, fresh and resume, surviving compaction
(`AgentControlService.cs:155`). Its text is a versioned constant, `CheckInterpretation.Contract`
(new `server/Application/Services/CheckInterpretation.cs`), and `EnsureAsync` **reconciles** the
agent row against the constant on every call — edit the constant in a PR, the agent updates itself;
hand-edits in the UI are overwritten. Content: you are the check interpreter; each brief is a fact
bundle about someone else's running delegate; triage, not diagnosis (doing / produced / looks
stuck / settled / ambiguous — say which and why in 3–5 lines); never claim completion; never
investigate beyond the bundle; use no tools.

Hard enforcement on top of instructions: the provisioner writes a deny-all `PreToolUse` hook into
the scratch cwd's `.claude/settings.json` (same mechanism as `ArmDenyHookAsync`, wider match) — the
specialist needs zero tools, so it gets zero. The per-check brief carries only the bundle (already
`ScrubTaskMarkers`-safe rules apply: the bundle handed over must contain no live task marker — the
brief's OWN marker is the interpretation task's, as for any delegate) plus a one-line output-format
reminder.

### 1.5 Fallback (non-negotiable, restated as the contract of slice 4C)

`RunCheckAsync`'s success path is the ONLY new path; every other outcome is today's shipped
behaviour plus a prefix. Digest-only note with
`(unverified digest — interpreter unavailable: <reason>)` when: `CheckInterpreterEnabled=false`;
provisioning fails; backlog ≥ cap; task creation throws; wait times out; settled task has empty
`Result`; settled task Failed. The probe, `RenderDigest`, `BuildNote`'s envelope, marker scrubbing,
and pty fitting are not modified — the interpretation is passed in as an optional body override.

### 1.6 Cost accounting: the original decision holds, and gets stronger

The interpretation task is **its own root** (`RootTaskId = Id`, `ParentTaskId = null`,
`Depth = 0`), so its `CostUsd` sums into no caller's tree and `RootIsOverBudgetAsync` over the
delegation run is untouched — the per-root ceiling keeps meaning "what the delegated work cost."
Unlike the CLI design, the spend is no longer only a number in an event detail: it is fully
accounted on the interpretation task's own row (tokens split, pricing version, rollups) — but the
`Check` event on the CHECKED task still records it (`interpreter: task <shortId>, $<cost>` ahead of
the digest head) so the timeline answers "what did watching this cost" without a join. Nesting the
check task under the checked task was considered and rejected: it would need a role carve-out
inside the budget query to keep decision 6 true, and a carve-out inside a spending ceiling is the
kind of exception that rots.

## 2. Slices

Each lands green and independently revertable. 4A and 4B are independent of each other; 4C needs
both; 4D needs 4C.

### Slice 4A — Dispatch a pinned task into a standing agent's live session

Today `TryReuseWarmAgentAsync` sends any pinned non-pool agent to `SpawnFresh`
(`AgentTaskDispatcher.cs:956`), which creates a **second session** and overwrites
`Agent.PersistentSessionId` (`:662`) — for an AlwaysOn agent that fights the supervisor. This slice
is the general capability, useful beyond checks ("run this on my standing agent").

**Files:** `server/Application/Services/AgentTaskDispatcher.cs` — in the pinned branch of
`TryReuseWarmAgentAsync`: a pinned agent that is NOT a pool delegate but HAS a live session
(`LiveSessionIdOfAsync`) is reused when no Dispatched/Working task holds its `AgentId`
(→ `WaitForAgent` otherwise). Reuse sets `AgentSessionId`/`Dispatched`/`DispatchedAt`/event
`Dispatched: "Delivered into standing agent '<name>'s live session"`, delivers the brief via
`DeliverReuseMessagesAsync` **without** the unrelated-root `/compact` prepend for `Role == Check`
(homogeneous work; accumulated bundle-reading context is the point — gate the compact on
`task.Role != AgentTaskRole.Check` once 4C adds the enum; in this slice, on a new
`SkipReuseCompact` internal flag or simply leave compact behaviour as-is for non-check roles).
It must NOT touch `PersistentSessionId`, `Status`, or any Pool* field. A pinned AlwaysOn agent with
NO live session stays Queued (the supervisor will bring the session back); a pinned non-AlwaysOn
standing agent keeps today's `SpawnFresh` behaviour.

**Tests:** new `tests/Antiphon.Tests/Application/AgentTaskStandingAgentDispatchTests.cs` —
live standing agent: task delivered into its existing session, no new `AgentSession` row,
`PersistentSessionId` unchanged; busy standing agent: task stays Queued and dispatches after the
first settles (serialisation); AlwaysOn with dead session: stays Queued, no second session spawned;
non-AlwaysOn with dead session: SpawnFresh as today (regression pin); settlement end-to-end: a
marked turn on the standing session settles the pinned task and stores `Result`, agent row
untouched afterwards (no pool handshake fires — `AgentTaskReplyService.cs:611` already filters on
`IsPoolDelegate`; the test pins that).

### Slice 4B — The specialist exists, supervised, with a versioned contract

**Files:** new `server/Application/Services/CheckInterpretation.cs` (the `Contract` constant + the
brief/output-format fragments), new `server/Application/Services/CheckInterpreterProvisioner.cs`
(`EnsureAsync(ct)` → `Agent`), `server/Application/Settings/DelegationSettings.cs`
(`CheckInterpreterEnabled` = true, `CheckInterpreterAgentSlug` = "antiphon-check-interpreter",
`CheckInterpreterWorkingDirectory` = null → derived from first `AllowedRoots` entry +
`\.antiphon\check-interpreter`, `CheckInterpreterWaitSeconds` = 60,
`CheckInterpreterMaxBacklog` = 2), `server/Program.cs` (DI),
`server/Infrastructure/Orchestration/AgentTaskCheckHostedService.cs` (call `EnsureAsync` once at
startup when both `CheckEnabled` and `CheckInterpreterEnabled`). Provisioner behaviour: find by
slug; create if missing (AlwaysOn, Low tier, no remote control, scratch cwd created, deny-all
PreToolUse hook file written, `StartAsync` called); reconcile `SystemPromptAppend` to the constant
when drifted; never touch a running session.

**Tests:** new `tests/Antiphon.Tests/Application/CheckInterpreterProvisionerTests.cs` — creates on
first call with exactly the intended row shape; idempotent (second call: zero writes); recreates
after deletion; reconciles a hand-edited `SystemPromptAppend` back to the constant; hook file
content pinned; disabled setting → no agent created. Supervision is NOT re-tested here — the
existing AlwaysOn machinery owns it; one test pins only that the created row has `AlwaysOn = true`.

### Slice 4C — Wire-in: the one-line plug point becomes create → wait → note

**Files:** `server/Domain/Enums/AgentTaskEnums.cs` (`AgentTaskRole.Check = 11`),
`server/Application/Services/AgentTaskCheckService.cs` (replace the digest-only comment block at
`:106` — build the interpretation task row directly (`Role = Check`, own root, pinned to the
provisioned agent, `ReplyTo = None`, `Ephemeral = false`, `ModelLevel = Low`, `Workspace = Shared`,
`WorkingDirectory` = specialist cwd, Goal = bundle + output contract), backlog gate, bounded poll,
cancel-if-still-Queued, note body = interpretation or degraded digest, `Check` event gains the
interpreter line), `server/Application/Services/AgentTaskDispatcher.cs` (`TickAsync`: exclude
`Role == Check` from the `active` count and the cap test; `DeliverReuseMessagesAsync`: no compact
for `Role == Check`), `server/Application/Services/AgentTaskService.cs` (`ListAsync` excludes
`Role == Check` unless `includeChecks`), the list endpoint in `server/Api/Endpoints/`,
`client/src/api/agentTasks.ts` (role value + query param).

**Tests:** extend `tests/Antiphon.Tests/Application/AgentTaskCheckSweepTests.cs` (or a sibling
`AgentTaskCheckInterpreterTests.cs`): success — settle the interpretation task in-harness (write
`Result` + status on the row the service is polling; `FakeTimeProvider` advances the poll) → note
carries the interpretation, still opens `[check `, still marker-scrubbed, `Check` event names task
short id and cost; timeout → degraded digest note, still-Queued task cancelled; backlog at cap →
degraded, no task created; interpreter disabled / provisioner failure → degraded; settled-but-empty
`Result` and settled-Failed → degraded; recursion pins — the interpretation task never arms
`NextCheckAt` (`ReplyTo = None` → `ArmFirstCheck` skips; asserted, not assumed) and the check sweep
never selects it; cap-bypass — a system at `MaxConcurrentTasks` still dispatches a `Check` task and
still refuses a normal one; `ListAsync` default hides `Check` rows, `includeChecks` shows them; the
shipped cannot-settle and read-only-against-the-delegate tests must pass UNCHANGED — the delegate's
session is still never touched (the specialist reads a bundle, not the delegate).

### Slice 4D — Live verification and the docs that changed shape

Foreground, against the dev stack: create a delegate with `-ExpectAbout 1`, watch one full check
interpret (provisioner creates the specialist, dispatcher delivers into it, note lands with an
interpretation); kill the specialist's session mid-window and watch the next check degrade while
the supervisor restarts it; delete the agent and watch it recreated. Record all three in
`docs/superpowers/findings/` (the CARD-0047 findings log seeded by a02eb2d). Docs:
`.claude/skills/antiphon-delegate/SKILL.md` + `docs/orchestration-loop.md` §4 — a `[check …]` note
may now carry a specialist's reading; the digest-only form with the degraded prefix is the
guaranteed floor; name the `antiphon-check-interpreter` agent so an operator seeing it on the
agents page knows it is furniture, not a stray. Mark the original spec's slice 4 as superseded by
this file. No new server code.

## 3. Landing order

4A → 4B → 4C → 4D (4A/4B in either order). After 4A+4B the system is unchanged in behaviour;
after 4C checks interpret; 4D proves it against the real stack.

## 4. What I could not determine, and what would settle it

1. **Warm turnaround of a haiku always-on session over a ~3 KB bundle** — sets whether 60 s is the
   right `CheckInterpreterWaitSeconds`. Settled by one timed foreground run in 4D; the knob makes
   miscalibration a config change.
2. **Whether a deny-all PreToolUse hook in the scratch cwd interferes with anything the specialist
   legitimately does** (slash commands are not tool calls, so /compact should be unaffected — but
   this is asserted from the hook model, not measured). Settled in 4D by watching one auto/manual
   compaction under the hook; fallback is narrowing the hook to Edit/Write/Bash.
3. **Whether `AgentTaskReplyService.OnTurnEndAsync` fires for standing agents' sessions in every
   path it fires for spawned delegates** (it is keyed on session turn-ends and task→session
   mapping, so it should; the 4A settlement test is the evidence, and it must go through the real
   turn-end trigger, not call `OnTurnEndAsync` directly).
4. **Interaction with CARD-0002/0031's board redesign** — the `includeChecks` server-side default
   is deliberately the smallest possible board footprint so those cards stay free to decide
   presentation; if they instead want check rows grouped under the checked task, that is a client
   concern the own-root data model does not block (the `Check` event carries the link).
5. **Whether the specialist should also serve OTHER interpretation-shaped work** (incident
   digests, watchdog summaries). Nothing here precludes it — pinned dispatch is generic — but the
   contract constant is deliberately check-specific until a second consumer exists.
