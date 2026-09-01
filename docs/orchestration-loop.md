# The orchestration loop

How a card gets from the board to shipped, using delegates. Written from what actually worked
2026-08-13..16, including the parts that did not. The aim is that this is repeatable without
re-deriving it, and mechanical enough to automate later.

The orchestrator's job is to **decide, verify and record**. The reading, the writing and the running
are delegated. The orchestrator's context is the scarce resource: spend it on judgement, not on
archaeology - and verification is not archaeology's quieter cousin. It is trust, by default.

---

## 0. What the orchestrator may read, and what it must send out

1. **Trust the report.** The default, every time. A settled task's own report - what it changed,
   what it ran, what passed - is the evidence. Merge on it, close on it, move on. Re-reading a diff
   or re-running a named test "just to be sure" is not diligence here; it is spending the
   orchestrator's context on a question the delegate already answered.
2. **Ask the same delegate.** Real reason for concern - the report is vague, contradicts itself, or
   skips something the brief asked for - is answered by going back to the agent that did the work,
   not by reading its diff cold. Reply into the same task asking for the missing detail. It has the
   context; re-deriving that context from the code is the archaeology this whole doc exists to
   stop, just moved one stage later.
3. **Delegate the investigation - rare.** Only when the delegation pipeline itself is broken -
   unreachable agent, a stuck task, something wrong with the pipeline rather than the work - does
   direct reading become the answer, and even then it is a `Debug`/`Plan` delegate's job. "This
   one's quick" is the rationalisation that produced CARD-0246's inline fix (eight reads in ninety
   seconds, then an Edit, a build, a commit and a deploy, all in the orchestrator's own context) -
   the exact thing this ladder exists to make rare.

**Also delegated: the landing mechanics.** For a delegated Worktree task, the orchestrator orders
the landing with `delegate.ps1 -Land <id>` (optionally `-Verify <filter>`); the server fetches,
rebases, verifies when required, fast-forwards, pushes, and cleans up. The resulting
`Landed`/`LandRefused` outcome line is the confirmation. The orchestrator decides the order and
what a refusal means, but does none of those git operations itself.

Since CARD-0247, a `PreToolUse` hook in this repo nudges at the third consecutive cold source read
(it never blocks; `ANTIPHON_ORCHESTRATOR=0` silences it for a hacking session), and a server sweep
records each run as an `OrchestratorInvestigation` Warning on the attention feed. A row there is
not a fault to fix in the code - it is a habit to fix in the next brief.

---

## 1. The cycle

```
pick a card
  └─ is there a SOLID plan?  ── no ──▶ fable Plan agent ──▶ land the plan on master
  │                                                              │
  └─ yes ◀───────────────────────────────────────────────────────┘
       ▼
   opus Code agent, in a worktree, working the plan's slices
       ▼
   TRUST THE REPORT (§0) — ask the same delegate back if there's real concern
       ▼
   delegate.ps1 -Land <id>  ──▶  deploy  ──▶  close the card
```

Landing (fetch, rebase, verify, fast-forward, push, worktree removal, branch deletion) is the
`-Land` operation's job, not a manual step — see §5.

A finished Plan or Docs deliverable sitting in a worktree is invisible. Cherry-pick or copy it onto
master and push as soon as the task reports — do not wait for the task to formally settle. Two
2026-08-10 cases (the CARD-0002 design doc and the CARD-0001 fix) sat unmerged for 9 hours before
anyone noticed.

### Picking

P0 first, but prefer a card that **changes how everything else gets done** over one more feature.
Prefer a card whose plan already exists — but check properly, see below.

### "Is there a solid plan?"

Two places, and the second is the one people forget:

1. `docs/superpowers/specs/` — but **read the date**. A plan written before a big refactor may name
   files that no longer exist. CARD-0019's client section targeted a board page that CARD-0042 had
   replaced two days earlier; it had to be replanned.
2. **Unmerged branches.** Plans get stranded. Run this, not `git branch -r`:

   ```bash
   for b in $(git branch -r --list "origin/feat/*" | tr -d ' '); do
     git cherry master "$b" | grep -q '^+' && echo "UNAPPLIED: $b"
   done
   ```

   `git cherry` compares by patch-id, so it distinguishes "rebased and landed under a different
   hash" from "never landed". On 2026-08-16, 8 of 10 unmerged branches were already applied and
   exactly 2 held real work — including a 187-line plan for an open card.

A plan is **not** solid if it predates a refactor of the area, assumes an API that shipped
differently, or is under ~20 lines for a feature. Replanning costs ~$5; implementing the wrong thing
costs an hour and a merge.

---

## 2. Tiers

The role sets the model. Do not override without a reason stated in the goal.

| Role | For | Tier |
|---|---|---|
| `Plan` | decompose, design, choose an approach | fable |
| `Code` | write or change code | opus (override `-Level High`) |
| `Review` | judge whether logic is correct | fable |
| `Debug` | find out why something is broken | opus |
| `Merge` | resolving a conflict left behind by a worktree task (auto-spawned after TryMergeBackAsync fails, rarely dispatched by hand) | opus |
| `Docs` | prose, markdown, comments | sonnet |
| `Commit` | git plumbing, branches, PRs | sonnet |
| `Test` / `Deploy` | RUN a thing and report what happened | haiku |

**A `Test` agent runs and reports. It does not repair.** The boundary, stated so it is not a matter
of taste:

| Allowed at `Test` (haiku) | Escalate to `Debug` (opus) |
|---|---|
| Run a suite, report pass/fail counts and the failing names | Explain *why* something failed |
| Re-run a failure **in isolation** to establish flaky-vs-real | Change any production or test code |
| Re-run at a known-good commit (stash / worktree) to establish pre-existing-vs-caused | Widen a timeout, loosen an assertion, add a retry |
| Bisect by re-running | Decide a failure is "expected" and move on |

Isolating an error is narrowing *where* it lives; fixing it is deciding *what is wrong*. The first
is cheap and mechanical, the second is the expensive judgement this tiering exists to buy. A haiku
agent that starts editing a test to make it pass is the worst possible outcome — it is the exact
instinct that left a live 64 KB-truncation reproduction red for weeks by treating a real defect as a
flaky test.

Say this in the brief explicitly; do not assume the role name carries it.

The best single result of this period came from a `Debug` agent; the cheapest useful one was a haiku
check at **$0.12**.

### Launching an agent

Start a new project through `POST /api/projects/setup` (or `scripts/project.ps1 new -Dir ... -Orchestrator -Start`): it creates the project, board, and preset agent in one transaction and returns readiness. `POST /api/agents` remains for adding an agent to a project that already exists.

Create and start an agent through `POST /api/agents` + `POST /api/agents/{id}/start` (or the UI).
Never launch the `claude` CLI directly, and never a `launch-remote` script. When setting
`modelLevel`, send it as the string `"Frontier"` (or `"High"` / `"Medium"` / `"Low"`). A numeric
token (`0`, `1`, `99`) is a **400** — the wire is the member name, not the enum ordinal (CARD-0007).
Frontier maps to fable (Claude) by default, or `grok-4.6` when `Kind=Grok` is passed.
If `delegate.ps1` 409s `model_disabled`, pick an alias from `available` or wait until
`disabledUntil`; do not retry the same kind/tier.

### Reuse first

**Default: `delegate.ps1`.** Unrelated new work needs nothing special — the warm pool reuses an idle
agent in the same directory (compacted first) and spawns a fresh ephemeral delegate only when none
fits. Sequential follow-up that must keep context: `-OnAgent <taskId>` (already on the script and in
the skill). Parallelism on one model: let the pool spawn another, or pass `-Worktree`. That *is* the
"2–3 reusable workers per directory+model+tier, scale only for real parallelism" policy, implemented
by the pool rather than by named rows.

`POST /api/agents` is for a **standing identity**, not a unit of work: an orchestrator seat, a
channel-bound agent, the check interpreter, or a human-facing named worker that should outlive a
card. Pass an existing `BoardId` (the project's real board). A unique `workingDirectory` that is not
a real checkout is how 21 extra `gym-stat-*` projects appeared.

Work you need to hear finish is a task; creating an agent gives you an identity, not a report.
Pin a task onto an existing named standing agent with `delegate.ps1 -Agent <name|slug|guid>`
(CARD-0291): it queues while that agent is busy, delivers into the live session, and settles with
the normal `[task … done]` note. A raw session message to a child is for steering work you already
dispatched, never for handing over work — no completion note will ever arrive for it.

---

## 3. Writing a brief

**The standing rules are delivered automatically now (CARD-0058). Do not retype them.** Every
delegate launches with `server/Bundles/delegate-basics.md` composed into its
`--append-system-prompt` — foreground-only, no sub-delegation, commit-and-push each slice,
`OutputPath` with a forward slash, verify pre-existing red by stashing before blaming yourself. That
file is **canonical**; this section deliberately does not restate its contents, because a rule
restated in three places drifts in three places, and it drifted here first. Improve a rule by
editing that file in a PR: every future dispatch gets the better version, with nothing to
reconcile. A sub-orchestrator additionally launches with `server/Bundles/orchestrator.md`.

So a brief is now **the goal plus the state of today**, and the state is the part no bundle can
carry:

- **Name the known-failing tests** and say to verify by stashing. The *rule* is in the bundle; today's
  actual red list is not — it changes weekly, and a bundle naming last week's would be worse than
  silence.
- **Say what is already done** ("slices 1 and 5 are landed as X and Y, build on them, do not redo")
  and **what is out of scope**.
- **Say the warning count, the flaky suites, the ports in use** — anything true this afternoon and
  false next month.
- Give **outcomes, not procedures**. The delegate decides how.

The test of whether this worked is mechanical: briefs are stored on task rows, so sample the next
week's and grep for the six rule-paragraphs. If they are still being typed, the inversion did not
take and this section is still too gentle.

Bundles reach a delegate **at launch**, which has one bounded consequence worth knowing: a warm
pool delegate keeps the bundles it started with until it retires (60 minutes idle). Nothing types
bundles into a live session, deliberately — if a rule change matters urgently for work in flight,
say it in the brief for that dispatch.

---

## 4. Checking on a delegate

A `ParkedMessage` attention row on a finished task now clears itself within roughly 10 minutes: the
queue sweep discards the stale machine-origin message rather than retrying it. A parked row that
remains is deliberately one whose content may still need a human decision (for example a UI/channel
message, a completion note, or work on a session with an open task).

**Check-ins are automatic now (CARD-0047, slices 1-3).** Delegate with `-ExpectAbout <minutes>`
(1-1440, defaults to 10) and the server arms a schedule: a deterministic, read-only probe of the
task row, the delegate's session/transcript, its queue and its incidents — plus its git log for a
worktree task — lands in your session as a `[check <id> #n] ...` note, first around the minute mark
you declared, then backing off along a Fibonacci ramp fixed from a 5-minute base (5, 10, 15, 25, 40,
60, 60 …, capped at 60 minutes — CARD-0061) for up to 10 checks. Every gap is rounded to a
human-readable number on the way out — nearest 5 below 30 minutes, nearest 10 from 30 to 60 — a
separate step from the ramp itself, so it keeps the schedule legible even if the base or the ramp
change later; the shipped sequence above is already round, so this doesn't move it. The declared
duration schedules only the first check; it no longer scales the ramp. It costs no model call today
and cannot write to the delegate at all.
**A check note is a progress report, never a completion** — the delegate's own `[task <id> done]`
note still arrives separately, and a check note can never be mistaken for it or settle anything
(see `.claude/skills/antiphon-delegate/SKILL.md`).

**A Check-role task settles Succeeded when it has produced a reading (CARD-0302).** `LOOKS STUCK` /
`BLOCKED` in that reading is evidence on the **checked** task (its Check event / parent `[check …]`
note), never the Check row's own `Status`. The interpreter's job is the reading; finishing it is
`done`, not a question for the operator. Reply or Cancel on a Check row is unsafe — those verbs
enqueue into or kill the standing `antiphon-check-interpreter` session. Empty or `failed`
interpretations stay degraded (no reading on the checked task). Do not parse LOOKS STUCK as a
classifier input; PastExpectedIdle / ChecksSpent / DeadSession already surface actually-stuck
delegates and already attach the latest interpretation as evidence.

**A task that fails before it is ever dispatched still gets the ramp (CARD-0231).** CARD-0220 already
sends a one-shot `[task <id> failed]` note through `FailAndNotifyAsync`; that note can sit unsubmitted
or be lost, and the orchestrator bundle says not to poll. The same `NextCheckAt` / `CheckCount`
columns now arm on a never-dispatched failure (first look at the 5-minute ramp base, not
`-ExpectAbout`, because that number describes work that never started) and a sweep re-sends the note
only while nothing shows the caller has heard — note `Sent`, a human Drop, `delegate.ps1 -Status`, or
opening the task drawer. It is a reminder, not a check: no probe, no interpreter, no session. While
the reminder is armed the attention feed lists it as `FailureUnacknowledged` in the **Broken** group
(counted; not a push, not an alert). The ramp stopping after 10 reminders is not acknowledgement —
the row stays until something hears.

**Do not infer from silence** as general practice still holds, but the specific cause behind the
worst observed lags is now found and fixed, not just worked around. It was never a slow pipeline —
task `817682e9` traced the 90-minutes-late and never-arrived notifications to CARD-0055's root
cause: a queued completion note was marked Sent as soon as the screen merely redrew after Enter, so
a swallowed Enter left the note sitting unsubmitted in the composer (one sat there 104 minutes,
only reaching the transcript when a LATER delivery's Enter pushed it in), and a second note was
lost outright when its own Enter resubmitted the first note's stale body instead. CARD-0055
(shipped `4bb65fb`..`165da34`) fixes it at the source: a delivery is now Delivered only when a
matching `UserPrompt` transcript record appears, with Enter re-presses and a late-confirm brake
against double-submission — see the CLAUDE.md gotcha for the mechanism. The schedule above and the
manual probes below still earn their keep as a safety net and for a faster look, but they are no
longer standing in for this specific defect.

Cheapest first:

```bash
# has it settled?  (one request, no model)
curl -s localhost:17202/api/agent-tasks | python -c "..."   # filter by short id

# the stored report — available BEFORE the notification
pwsh -NoProfile -File scripts/delegate.ps1 -Status <id>

# the truest progress signal: commits exist before reports do
git log --oneline master..feat/card-task-<id>

# what it is doing right now
curl -s localhost:17202/api/sessions/<sessionId>/transcript
```

**Traps:**

- **Do not scan processes by command-line substring.** A scan for `*bin-c45*` matched *the scanning
  command itself*, "finding" a runaway agent that was the orchestrator's own session, and the
  follow-up kill terminated its own tool shell. Exclude self and ancestors, or key on session
  ownership.
- **Do not trust the agent's account of what it left behind.** One reported its spec "untracked, not
  committed" when the commit existed and was pushed. Check the repo.
- **`*.ansi.log` files are raw pty capture**, not structured logs. Grepping them yields escape
  sequences. Use `GET /capabilities` and the API instead.

---

## 5. Order the landings and read the outcome lines

**Landing order comes from the completion header.** A note whose header carries
`overlapping-running=<ids>` says those tasks were still running when this one settled and touched
the same areas. Land this branch first, or expect its rebase to replay onto their work
(CARD-0063).

For a succeeded Worktree task, make the ordered landing decision with one call:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land <id>
# Add -Verify "<treenode-filter>" when that narrow test is part of the landing decision.
```

The server performs the fetch, rebase, conditional build and optional named test, fast-forward,
push, worktree removal, and branch deletion. Read the resulting `Landed` or `LandRefused` event
and its outcome line. `Landed` reports the merged SHA, pushed remote ref, and verification result;
`LandRefused` leaves the branch and worktree in place and names why.

After `-Land`, the orchestrator's own git involvement is **zero**. Do not re-run `git show`,
`git diff`, `gh run view`, or tests to double-check a `Landed` outcome. This is the same
trust-the-report rule used for content review, extended to the server-measured land step itself.
If the outcome raises a real question, dispatch a follow-up or `Review` delegate rather than
inspecting the branch directly.

A refusal is a judgement call, not a silent gate: dispatch a follow-up to repair it, defer the
landing, or drop the branch.

---

## 6. Deploy

Deploy remains an orchestrator decision: batch and order restarts around the landings. Execute the
local deploy through one script:

```powershell
pwsh -NoProfile -File scripts/deploy-local.ps1
```

It restarts the AppHost, waits for health, runs the local stack verification without a browser
smoke, and checks the live EF migration history against `server/Migrations/`. Its final line is
the deploy result: `DEPLOY VERDICT: ok` or `DEPLOY VERDICT: failed <detail>`. Read that one line;
do not reconstruct the former multi-command deploy sequence by hand.

For the separately scoped `am-service` remote target, the same rule applies through
`pwsh -NoProfile -File scripts/deploy-am-service.ps1`. Once a Deploy-role brief explicitly
authorizes `-Deploy`, that Deploy-role delegate may run this script and report its final
`REMOTE DEPLOY VERDICT`; it may not reconstruct the SSH/tar/Compose sequence ad hoc. Its default
run is a read-only preflight, and its human traffic check remains the Antiphon-Family test group.

---

## 7. Close the card — orchestrator writes the verdict, haiku executes it

Split by what each part actually is:

- **The verdict is judgement and stays with the orchestrator.** It is synthesis across the whole run
  — what shipped, what was corrected, what was disproved, what is still open, which other cards it
  touches. A haiku agent cannot see that from the repo.
- **The move and the cleanup in §8 are mechanical — delegate them.** Hand the agent the verdict text
  and the card identifier; it runs `pwsh -File scripts/card.ps1 close CARD-nnnn -ReasonFile <path>`
  (or `-Reason` for something short) and reports the result.

- **A terminal move preserves its `reason`; use it as the verdict** — what shipped, what was
  corrected, what is still open, with commit hashes.
- **In Progress and Review are no longer yours to move by hand (CARD-0040).** A task bound to a card
  moves it: to In Progress when the task dispatches, to Review when the last open task settles
  `Succeeded`, within 60 s either way, with `card-transitions` as the actor on the revision. Bind it
  by leading the brief's title with `CARD-nnnn` or by passing `delegate.ps1 -Card CARD-nnnn`, which
  prints `- bound to CARD-nnnn` at dispatch so a mis-binding is visible immediately.
- **Done is still yours**, and so is any move you make on purpose: the sweep only acts on evidence
  NEWER than your last move, so dragging a card back with a reason is respected until the next
  dispatch. Nothing automates Review → Done.
- **A move into an active column used to spawn an agent silently — CARD-0051 made that opt-in.**
  Two dead sessions and a stray worktree came from one such PATCH before the fix. `card.ps1 move`
  (and the API underneath it) now only starts a session when `-Spawn` / `spawn: true` is passed, and
  says so when it suppressed one. Muscle memory from before the fix should assume nothing starts
  unless asked.
- Corrections to a card's text go through `card.ps1 edit` (`PATCH /api/cards/{id}/content`,
  CARD-0019), which records a revision with a reason. Before that shipped, a wrong card could only
  be corrected by filing another card.
- **`scripts/card.ps1` is the preferred way to touch a card from a shell now** — identifier
  addressing, the limits endpoint, and file-backed text are documented in its own header comment,
  which is canonical; see also the AGENTS.md synopsis. `server/Bundles/board-api.md` (attached to an
  agent that works the board directly) still documents the raw API for callers that can't shell out
  to the CLI — fix a wrong rule there, not here. Everything *around* the card — which agents exist,
  what they are running, a board's columns, a live session's transcript — is
  [ops-http.md](ops-http.md); read it rather than grepping the endpoint files for a route.
- Findings that outlive the card go in `docs/investigations/`. Agent scratch output lands in
  `.antiphon/`, which is **gitignored** — an 11 KB proven root-cause writeup was nearly lost that way.

---

## 8. Clean up

For delegated Worktree tasks, worktree removal and branch deletion are the `-Land` operation's own
job. Do not run them as a manual orchestrator step after a `Landed` outcome. A `LandRefused`
outcome deliberately keeps both so a follow-up delegate can work from the failure.

```powershell
Get-ChildItem C:\src\Antiphon -Recurse -Depth 3 -Directory |
  Where-Object { $_.Name -like 'bin-*' -or $_.Name -match '\s$' }
```

A directory whose name ends in a space must be deleted via the `\\?\` prefix; normal path APIs
cannot open it.

---

## 9. What to automate first

**The check-in timer (CARD-0047) and the card CLI (CARD-0051, `scripts/card.ps1`) have shipped** —
§4 and §7 now describe them running, not proposed. In rough order of payback for what's left:

1. **The unmerged-branch sweep** from §1 — a scheduled job that reports genuinely unapplied work.
2. **A post-merge deploy script** that reads the diff and decides which restarts §6 requires.


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

### Preserved Gotcha #4

- **`-Scope` is a list of AREA NAMES and/or path globs, and a hold is now a visible `Held` event — never a silent wait** (CARD-0063). The column is `Scope` (was `ScopeGlob`); each comma-separated element is compared independently, area names by EXACT match against [`antiphon.areas.json`](antiphon.areas.json) at the repo root, paths by the old literal-prefix rule. Before this the whole string was one glob compared by string prefix: in 623 live tasks it produced exactly ONE hold, a false one (`card-reopen-cli` held `card-reopen-client` because one label prefixes the other), and missed five genuine collisions where two running tasks' comma-lists shared an element outright. The policy is now per workspace PAIR, not per area: **Shared↔Shared serialises** (one checkout, one `git status`, one `bin/`), **anything with a Worktree only warns** (it collides at merge, and blocking it throws away the parallelism worktrees exist to give), **ReadOnly is outside the lease in both directions**, and an intersection only in a `weight: allow` area (`docs`, the only one) costs nothing. `Delegation:SerialiseSharedWriters` (default **true**) additionally holds a Shared task behind any running Shared task in the same repo *with no scope declared at all* — the skill doc has said since 2026-08-18 that disjoint scopes do not make two shared writers safe, and this is the server asking that question instead of the caller remembering to. An area name the map does not know is ACCEPTED as an opaque label plus a `Warning` event, never a rejected create: a bookkeeping field must not refuse a launch over a typo. Nothing enforces what a delegate actually writes — drift is RECORDED at settlement (`ObservedScope`, a `ScopeDrift` event, `drift=` in the completion header) and never blocks, holds, kills or re-types anything; a PreToolUse path hook was considered and rejected because it could only ever be armed in a worktree, where an out-of-area write is already harmless. `pwsh -File scripts/delegate.ps1 -ListAreas` prints the map; the create response prints what your scope just cost; the completion header's `overlapping-running=` names the still-running tasks whose areas this one touched, which is the whole of the merge-ordering deliverable. **Extend the map when two tasks collide in an area, naming it for the work, not the folder** — and never give a glob a leading wildcard in its file name (`Services/*Profile.cs`), because the literal prefix collapses to the directory and the area silently swallows everything in it (`AreaMapContractTests` fails the build on it).

### Preserved Gotcha #36

- **A pre-dispatch failure is reminded on the check ramp until the caller hears, and it counts in the attention feed while that reminder is armed** (CARD-0231; CARD-0220 sent the one-shot `[task … failed]` note via `FailAndNotifyAsync` but nothing ever looked again): `AgentTaskDispatcher.FailAndNotifyAsync` arms `NextCheckAt`/`CheckCount` when `DispatchedAt is null` (`ArmFailureReminder` — first look at the 5-minute ramp base, not `ExpectedDurationMinutes`, because that number describes work that never started). `RemindUnacknowledgedFailuresAsync` (registered in `TickAsync` right after scheduled checks) re-sends the note only while nothing shows the caller has heard — note `Sent`, a human Drop (`Canceled`), `LastPolledResultAt`, or `ReadAt`. A still-Pending note with attempts left is in flight: advance the schedule, send nothing. After 10 reminders the ramp stops; the attention row (`AttentionKind.FailureUnacknowledged`, Error / Broken) stays until acknowledgement. `RunScheduledChecksAsync` must keep filtering to `Dispatched`/`Working` — Failed + `NextCheckAt` is now a legal state and must never reach the check worker.

### Preserved Gotcha #37

- **A killed `git worktree add` leaves git's own `locked initializing` behind, and a locked registration with no directory failed every future dispatch of that task id** (CARD-0220): `WorktreeManager`'s 30 s per-command timeout killed the checkout under IO load (a quiet add of this repo is 5.4 s), the catch deleted the directory, and the timeout's `TaskCanceledException` escaped the dispatcher's per-task catch and killed the tick. `worktree add` now has its own budget (`Git:WorktreeAddTimeoutSeconds`, 180), a timeout is a `TimeoutException`, a failed add rolls back fully (directory → `remove --force --force` → `prune` → branch), `CreateAsync` heals a registered-but-missing worktree (re-attaching the branch, never deleting it), and a dispatch failure reaches the caller as a `[task … failed]` note through `FailAndNotifyAsync`. `git worktree remove --force --force` clears a locked+missing entry in one command; it does NOT clear one whose directory is partially present — delete the directory first.

### Preserved Gotcha #38

- **A restart can strand "working" forever — two distinct ways** (REQUIREMENT, live miss 2026-08-08, Antiphon-Opus badged Working for 30+ min while idle): (1) *Backfill reordering*: stored transcript sequences are ARRIVAL-ordered — `PersistTranscriptAsync` rebases entries past the session max, so a catch-up sync that lands entries missed during a server restart/stream gap puts stale pre-gap activity ABOVE the already-persisted TurnEnd, and the seq-only working rule read mid-turn forever. Both server `IsWorkingAsync` and client `isWorking()` now carry a timestamp override (record timestamps survive reordering; equal ts keeps the seq verdict); the runner's `TranscriptWorkingState` deliberately has NO override (its mirror is file-ordered). `SessionRunnerEventPump` also catches up ALL runner sessions on every (re)connect — never rely on the lazy GET-transcript sync — and `SyncTranscriptAsync` fires the turn-end queue flush for boundaries that only ever arrive via backfill (the live path dedups them as "seen" and stays silent). (2) *Dead mid-turn process*: a session relaunched after dying mid-turn (reboot/kill) has no TurnEnd coming, ever. The launch paths write a synthetic `SessionRestartBoundary` (a turn END in all working-rule implementations) and, on a genuine `--resume`, queue an auto-continue prompt (`AgentSessionSettings.ResumeAutoContinue`, WhenIdle so it serialises after the launch note). Pinned by `SessionMessageQueueServiceTests` (backfill/boundary cases), `AgentControlServiceIntegrationTests` resume-recovery pair, and the client `isWorking` tests.
<!-- CARD-0254 preserved source ends -->
