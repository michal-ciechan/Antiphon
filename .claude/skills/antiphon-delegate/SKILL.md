---
name: antiphon-delegate
description: Hand a piece of work to another agent, at the model tier the work deserves. Use when work is separable and you don't need it in your own context — writing code, running tests, deploying, investigating, editing docs, or a whole chunk that needs its own decomposition.
---

# antiphon-delegate — hand work to another agent

**Run it exactly like this** — `pwsh -NoProfile -File`, then the script path, then the arguments.

> **The path is relative to your working directory — the repo root — NOT to this skill's folder.**
> It is `scripts/delegate.ps1`. There is no copy inside `.claude/skills/`, so
> `.claude/skills/antiphon-delegate/scripts/delegate.ps1` does not exist; pointing `-File` at it
> makes PowerShell print its usage banner instead of running anything.

The script is not directly executable, so a bare `./scripts/delegate.ps1` fails, and putting the
arguments before `-File` makes PowerShell take them as its own.

```powershell
# a worker: one piece of work, reports back to you
pwsh -NoProfile -File scripts/delegate.ps1 -Role Code -Title "add Fizz" -Goal "add Fizz(int) in Calc.cs, multiples of 3 -> 'Fizz'"

# a sub-orchestrator: owns a chunk, decomposes it, runs its own agents
pwsh -NoProfile -File scripts/delegate.ps1 -Orchestrator -Title "Postgres 18 upgrade" -Goal "get the Postgres 18 upgrade shipped"
```

Quote every value that contains a space. If `pwsh` is not on PATH, use `powershell` — the script is
ASCII-only and runs under either.

Two decisions, in this order.

## 1. Worker or sub-orchestrator?

**Worker** when you can state the deliverable in one sentence and one agent can finish it — a doc
change, a test run, a commit, one function.

**Sub-orchestrator** when the chunk needs its own decomposition: several steps, several tiers, or
you don't yet know the shape of it. It gets this same skill and runs its own delegates; its report
is a rollup of its whole subtree, so you read one summary instead of every leaf.

Unsure? Send a worker. A worker that comes back saying "this is bigger than it looked" is cheap, and
you can re-send it as a sub-orchestrator knowing more than you did.

## 2. Which role?

Pick by what the work IS. The role sets the model tier, and that is the cost decision.

**Stage vs helper (CARD-0146).** `Investigate`, `Plan`, `TestDesign`, `Code`, `Mutation`, `Review` are pipeline
**stages** — each launches with its own `server/Bundles/stage-<role>.md` standing-rules bundle
(composed automatically, nothing to type) and is expected to close its report with a
`--- next stage ---` block (below). Every other role is a **helper**: dispatched *inside* a stage
to answer one narrow question, carries no stage bundle, and the block is optional for it.

| Role | Use for | Tier |
|---|---|---|
| `Investigate` | confirm a card's root cause — evidence only, no fix design (stage) | opus, escalate fable |
| `Plan` | decompose, design, choose an approach (stage) | fable |
| `TestDesign` | write the `## Verification design` section for a landed plan — separate dispatch by default for `complexity:hard`/`medium`, folded into Plan for `easy` (stage) | fable |
| `Code` | write or change code; implements tests and runs ordinary V/R; commits/pushes then hands off ordinary Review (stage) | fable |
| `Mutation` | post-land PCs and missing-control discovery in a fresh SourceLanding snapshot (stage) | Frontier |
| `Review` | ordinary read-only pre-land review for every complexity; judges implementation, ordinary evidence and pending PC design (stage) | fable |
| `Debug` | find out why something is broken | opus |
| `Coverage` | check what a change missed | opus |
| `Merge` | resolve a conflict left by a worktree task (auto-spawned after TryMergeBackAsync fails; rarely by hand) | opus |
| `Docs` | prose, markdown, comments | sonnet |
| `Commit` | git add/commit/push/branch, PRs | sonnet |
| `Test` | run a suite or build and report what failed | haiku |
| `Deploy` | run a script, restart a service, check health | haiku |

`Test` and `Deploy` are cheap because they RUN things and report what happened. Interpreting a
failure is a separate `Debug` task — don't ask haiku to work out why the build broke.

## Stage recipes

One `delegate.ps1` line per stage — kind-free (no `-Kind`/`-Level` unless you have a reason to
state in `-Goal`; the server routes the kind from pins/chains/RolePolicy). Every stage dispatch's
brief must additionally carry: the **previous stage's `handoff:` line, verbatim** (the sentence the
next brief is built from), its `artifact:` path, and — for a Plan dispatch on a `complexity:easy`
card — the sentence "the test-design stage is folded into this dispatch; the `## Verification
design` section is required." `-ExpectAbout` for Code is the ordinary V/R floor from the plan's
`## Verification design` → `### Cost` block (`suites forced: …; verification floor ≈ N min`) plus
authoring time — not a guess (`feedback_estimate_as_verification_floor_plus_authoring`).

```powershell
# Investigate — root cause only, no fix design
pwsh -NoProfile -File scripts/delegate.ps1 -Role Investigate -Card CARD-nnnn -Title "investigate <card>" -Worktree -Goal "<what to measure/reproduce>"

# Plan — always dispatched; carries the prior Investigate's handoff verbatim if one ran
pwsh -NoProfile -File scripts/delegate.ps1 -Role Plan -Card CARD-nnnn -Title "plan <card>" -Worktree -Goal "<what to design>; handoff from investigate: '<verbatim>'; artifact: <path>"

# TestDesign — separate dispatch (hard/medium); read the landed plan doc first
pwsh -NoProfile -File scripts/delegate.ps1 -Role TestDesign -Card CARD-nnnn -Title "verification design <card>" -Worktree -Goal "write ## Verification design for <plan artifact path>"

# Code — ordinary V/R; -ExpectAbout is ordinary V/R floor + authoring
pwsh -NoProfile -File scripts/delegate.ps1 -Role Code -Card CARD-nnnn -Title "build <card>" -Worktree -ExpectAbout <floor+authoring> -Goal "execute <plan artifact path> and its verification section; handoff from test-design/plan: '<verbatim>'"

# Review -- ordinary pre-land review of the retained Code worktree
$reviewGoal = Get-Content -LiteralPath '<review-brief-file>' -Raw
pwsh -NoProfile -File scripts/delegate.ps1 -Role Review -Card <original-card-guid> -ReadOnly -Dir '<code-worktree>' -Title 'ordinary review' -Goal $reviewGoal
# Record linked companion, then land the original Code task and await confirmed O/L
pwsh -NoProfile -File scripts/delegate.ps1 -Land <original-code-task-id>
# Mutation -- after confirmed publication and required deployment
$mutationGoal = Get-Content -LiteralPath '<mutation-brief-file>' -Raw
pwsh -NoProfile -File scripts/delegate.ps1 -Role Mutation -Card <verification-card-guid> -Worktree -SourceLanding <operation-guid> -Title 'post-land mutation' -ExpectAbout <pc-floor-plus-analysis> -Goal $mutationGoal
```

**The handoff block (D2).** A stage-role report must close with, immediately above the
`[antiphon-report:<id> …]` token:

```
--- next stage ---
next: <investigate|plan|test-design|code|mutation|review|land|decide|none>
handoff: <one physical line, at most 400 chars>
artifact: docs/<repo-relative path>.md   (optional)
[antiphon-report:<id> done]
```

This is delivered automatically in the stage's `ReportingContract` — do not retype it in `-Goal`.
A missing block still settles, as `next=unmarked` on the completion header; that is the cue to send
the same delegate back (§0 of the loop doc), never to read the diff instead of it.

A clean fast-forward plus deploy is `Deploy` (and `Test` for the suites). `-Role Merge` is the
conflict specialist the server already spawns; sending it a clean merge pays opus for haiku work.

A sub-orchestrator defaults to `Plan` and never runs below opus.

## Options

| | |
|---|---|
| `-Orchestrator` | make it a sub-orchestrator instead of a worker |
| `-OnAgent <taskId>` | follow-up on the SAME agent that ran that task — it keeps its context. Use the short id from its report |
| `-Agent <name>` | run this task on an existing standing agent by name, slug, or guid. The task queues while that agent is busy; you get the normal `[task … done]` note when it settles. Ambiguous or unknown references are refused 422, and so are pool delegates (that's `-OnAgent`'s job). Combined with `-OnAgent` is refused |
| `-Level <tier>` | override the role's tier — `Frontier`/`High`/`Medium`/`Low`. Say why in `-Goal` |
| `-Complexity Hard\|Medium\|Easy` | walk the (role, complexity) cell, falling back to the any-role chain (CARD-0090 / CARD-0332). Combined with `-Kind` or `-Level` is refused. Exhausted → Blocked for a human; **do not pick a kind yourself**. `-RefuseIfExhausted` 409s instead. `-Reroute <id> -Kind … -Level …` is the explicit human pick |
| `-Dir <path>` | run somewhere else — another repo, another checkout. Defaults to yours |
| `-Worktree` | isolate a worker in a fresh git worktree; sourced Mutation never merges back |
| `-SourceLanding <operation-guid>` | create only Worker/Mutation/Worktree at the confirmed operation's immutable L; distinct same-board companion and same project/repository required |
| `-CleanupVerification <task-id>` | explicitly seal and clean a terminal sourced snapshot after all-attempt native custody and restoration checks; never publishes or kills |
| `-Shared` | force the shared directory — opts a sub-orchestrator OUT of its worktree (warned) |
| `-ReadOnly` | shared directory, but the brief says don't write |
| `-AllowDirectEdits` | don't arm the deny hook in a sub-orchestrator's worktree (it needs to write a plan file itself) |
| `-Scope "<areas>"` | what this task owns: area names from `antiphon.areas.json` and/or path globs, comma-separated. Two **Shared** tasks whose scopes intersect are serialised — the waiting one gets a visible `Held` event; a worktree on either side runs anyway with a `Warning`. `-ListAreas` prints the names |
| `-Title "<text>"` | a 2–5 word label (max 80) for check headers, completion notes, and the board. Omitted, the Goal's first line is stored (clamped 300). Always pass it when `-Goal` is more than one short sentence |
| `-Card <id>` | which CARD this work is against — `CARD-0040`, `card-40`, `#40`, `40`, or the guid. Omitted, the server derives it: your own task's card, else the first `CARD-nnnn` in `-Title` |
| `-ExpectAbout <minutes>` | how long the work should honestly take (1-1440) — schedules the first automatic check-in. Defaults to 10 when omitted |
| `-NoInheritEnv` | do not forward this shell's `X_LLM_PROJECT` / `X_LLM_KEY`; server-side stored-env inheritance remains the fallback |
| `-Authority "<text>"` | the caller's own words for what this task is already authorised to do. Injected into the child's brief so it does not stop to ask for a go-ahead this already grants. Long text: `-AuthorityFile <path>` |
| `-Continue <taskId>` | replay that standing authority as the answer to a Blocked-on-question task. One action; the child resumes and reports back |
| `-Stage <name>` | which landing-step question this task answers: `Rebase`/`Verify`/`Cleanup`/`Review`/`FollowUp`/`Deploy` (CARD-0272). Omitted, the role maps: Review, Test→Verify, Merge→Rebase, Deploy; `-OnAgent` → FollowUp. Code and Plan never default. **Not** `-Role` (pipeline seat). A Debug titled "verify" is `-Stage Verify`. Unknown names 422 |
| `-Finding <id> -Stage … -Found "…" / -Clean` | orchestrator override of a stage finding (rare: the delegate said clean and you acted). Writes an Orchestrator row that supersedes the latest for that (task, stage) |

**LLM project routing follows the caller by default.** `delegate.ps1` forwards the live shell's
`X_LLM_PROJECT` and `X_LLM_KEY` into the child's inherited routing layer, because the server cannot
see process-only values. An explicit `-EnvOverride` for either name wins and is not duplicated in
that snapshot. Use `-NoInheritEnv` only when the child must not follow the current shell project;
server-side reconstruction from stored agent/task env remains available when nothing is forwarded.

**Bind the card, and the card moves itself (CARD-0040).** A bound task drags its card to In Progress
when it dispatches and to Review when it settles `Succeeded` with nothing else open — within 60 s,
with `card-transitions` on the revision. Leading `-Title` with `CARD-nnnn` is enough; `-Card` is the
explicit form and is refused 422 if it names no card. Creation echoes `- bound to CARD-nnnn`, so
check that line rather than discovering a mis-binding on the board a week later. A `Failed` task moves
the card nowhere, and a move you make by hand is never overridden — the sweep only acts on evidence
newer than your last move. **Review → Done is still yours.**

**Workers default to shared** — the delegate runs right in the directory, like you would yourself.
That default is only safe when it is the only write-capable worker in there. Decide explicitly, every
dispatch, don't just accept the default:

- **Nothing else is currently active in the shared directory** → shared is fine.
- **Anything else (worker or sub-orchestrator) is still running there** → pass `-Worktree`, even if
  the file scopes look unrelated. The collision isn't only "two agents touch the same file" — it's
  shared commit boundaries (`git add -A` from one agent can sweep up another's uncommitted work),
  shared build/test output, shared `git status`. Two Code workers editing disjoint files in the same
  checkout at the same time is still a collision risk.
- **You want the change reviewable before it lands, or several delegates will genuinely touch the
  same files** → `-Worktree` regardless of the above.

(Live miss 2026-08-18: CARD-0054 slice 1 landed in the shared directory; a second, unrelated Code
worker was then queued while it was still running, without stopping to ask this question first. Caught
before either wrote anything wrong — fixed by giving the second task `-Worktree`. The checklist above
is the fix, so the next dispatch doesn't rely on catching it by luck. Since CARD-0063 the server asks
the same question itself: a second Shared task in a repo that already has one running is **held**,
scope or no scope, and the `Held` event says so. `Delegation:SerialiseSharedWriters` turns that off
for an operator who knows the pair is safe.)

**Declare areas, not file lists.** `-Scope` takes a comma-separated list; each element is either an
**area name** from [`antiphon.areas.json`](../../../antiphon.areas.json) at the repo root
(`delivery`, `schema`, `client`, `ops`, …) or a **path glob** (`server/Migrations/**`,
`docs/setup.md`). Elements are compared one at a time, area names by exact match — so
`card-reopen-cli` and `card-reopen-client` are two different things, and `delivery,docs/x.md`
collides with `AGENTS.md,delivery`. Run `pwsh -File scripts/delegate.ps1 -ListAreas` to see the
names, and read the response the create prints: `will wait behind 3f2a1c…` means this task is queued
behind that one, `overlaps 3f2a1c…` means it starts now and somebody owes a rebase. A name the map
does not know is still accepted (it becomes a label that matches only itself) and earns a `Warning`
event — it never refuses the launch. **An area is added when two tasks collide in it, named for the
work, not the folder.**

**A sub-orchestrator defaults to its own worktree** (or just its own `-Dir` when you point it
elsewhere) — it fans out writers, so it must own something. Its workers land on ITS branch and it
merges one level up when the subtree is done. Forcing `-Shared` is allowed but the server will warn:
its delegates and its caller can overwrite each other. Inside its worktree a PreToolUse hook refuses
direct Edit/Write ("delegate this instead") — pass `-AllowDirectEdits` if it genuinely must write.

## Working across repos

The directory is a property of the task, so one orchestrator can drive several repos — an agent per
repo, each reporting back to you:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Dir C:\src\am-service -Role Deploy -Goal "roll out the gateway build and confirm health"
pwsh -NoProfile -File scripts/delegate.ps1 -Dir C:\src\antiphon -Orchestrator -Goal "make the client speak the new contract"
```

A directory outside the configured allowed roots is refused — that is a guard, not a bug. Ask for
the root to be added rather than working around it.

## Follow-up work: same agent, same context

A delegate stays WARM after it reports — its session, and everything it just read, is still alive.
For the first five minutes it is reserved for YOUR run; after that it serves any work in its
directory, until it retires after an hour idle. Work that builds on a task's result should go back
to that agent:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -OnAgent 7f3a2b91 -Goal "now add the edge-case tests for what you just wrote"
```

Unrelated new work needs nothing special — the pool handles it: an idle warm agent in the same
directory is reused automatically (compacted first, focused on the new task), and a fresh one is
spawned only when none fits.

## Named standing children

A named agent is an **identity**, not a unit of work. Create it once (`POST /api/agents`), then
dispatch every piece of work to it as a task:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Agent gym-stat-dupmachine-plan -Role Plan -Card CARD-0029 -Goal "design the duplicate-machine detection pass"
```

Why the task, and never a raw session message: work handed to a child over
`POST /api/sessions/{id}/messages` **reports to nobody** — no `[task … done]` note, no check ramp,
no card movement (CARD-0291). The pinned task queues while the agent is busy, delivers into its
live session, and settles with the normal report; message a child's session directly only to steer
work you already dispatched.

Two pinned same-directory tasks are Shared writers and therefore serialised — the documented
collision default, not a bug. For a standing child whose value is its cwd and context, sequential
Shared dispatch is usually what you want; buy real parallelism explicitly:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Agent gym-stat-setupmockups -Role Code -Worktree -Goal "build the setup mockups"
```

noting that a `-Worktree` task pinned to a standing agent runs in the worktree, not the agent's own
directory.

## Check-ins while it runs

A dispatched task with `-ExpectAbout` (or the 10-minute default) is checked on automatically. The
first check lands around the minute mark you declared; later ones back off along a Fibonacci ramp
fixed from a 5-minute base — 5, 10, 15, 25, 40, 60, 60 … minutes, capped at 60 — for up to 10 checks,
then it stops with a note saying so. Gaps are rounded to a human-readable number (nearest 5 below 30
minutes, nearest 10 from 30 to 60) as a separate step from the ramp itself; the shipped sequence
above is already round. The declared duration only schedules the first check; it does
not change the ramp. Each check is a deterministic, read-only probe (task row, the
delegate's session and transcript tail, its pending queue, its incidents, and — for a worktree
task — its git log): it costs no model call, and it cannot type into, kill or commit for the
delegate it is inspecting.

It shows up in your session as a `[check <id> #n] ...` line, for example:

```
[check 7f3a2b91 #2] add Fizz(int) in Calc.cs · 18m elapsed (expected 10m) · session Running · working
```

**This is a progress report about the delegate, never its result, and never something to act on as
if the task had finished.** The delegate's own report still arrives separately as
`[task <id> done] ...` when the work actually completes — a check note is never that, never begins
with `[task `, and never uses completion language. If a note says the check budget is spent, the
task is still running, just no longer being watched on a schedule; ask `-Status <id>` if you want
to know where it stands.

`-ExpectAbout` is a hint that schedules the first check, never a deadline — nothing about the task
fails, escalates or gets killed off it. Declare the honest duration: padding it just delays the
first check, and it doesn't buy the delegate more time to run.

## Rules

These are rules for YOU, the caller. The rules the *delegate* works under are delivered to it
automatically — see "What the delegate is told" below; don't type them into `-Goal`.

- **One task, one deliverable.** Don't delegate what you could finish in two tool calls.
- **Always pass `-Title`.** It is a 2–5 word label (max 80) for check headers, completion notes, and the board — not a second Goal.
- **Write `-Goal` as an outcome, not a procedure.** The delegate decides how.
- **Put the state of today in `-Goal`, not the standing rules.** Known-red tests, what already
  landed, what is out of scope, which ports are taken — that is what only you know. The harness
  rules it already has.
- **Don't poll — this rule is for the CALLER, not the delegate.** As the caller, the report is
  delivered into your session as `[task <id> done] ...` when it lands; end your turn and it will
  reach you. The same applies to `-Land`: its outcome auto-delivers to the caller session via the
  same `WhenIdle` mechanism, so wait for it rather than running a manual `GET /api/agent-tasks/{id}`
  poll loop, which only duplicates the delivery (CARD-0386). The delegate's side of this is the opposite and it is already in
  `server/Bundles/delegate-basics.md`: it settles when its turn ends, so it finishes the work in the
  foreground and never waits to be re-invoked.

  > Historical note, because a wrong theory here cost real debugging time on 2026-08-13/14: when
  > six delegates appeared to "end their turns early" and return only preamble, this bullet was
  > blamed. It was not the cause. The cause was a settlement race — Claude Code splits one API
  > response into a signature-only `thinking` record and then the `text` record, both stamped with
  > the response's `stop_reason`, so a bare `TurnEnd` arrived up to 1.2 s before the report and
  > settlement fired on it. See `docs/superpowers/specs/2026-08-14-card-0046-settlement-final-message.md`
  > and CARD-0046. The guidance above is still worth scoping, but it was never the bug.
- **A delegate that asks a question comes back blocked.** The `[task … blocked]` note carries
  `reason:` / `asks:` / `authority:` / `next:`. If `authority:` names something, `-Continue` is
  the one action; otherwise answer it — don't take the work back:
  ```powershell
  pwsh -NoProfile -File scripts/delegate.ps1 -Continue <taskId>
  pwsh -NoProfile -File scripts/delegate.ps1 -Reply <taskId> "yes, accept negatives"
  ```
- **A running delegate can be steered without cancelling it** (CARD-0062). A spec that sharpens
  mid-flight — a test failure you already diagnosed elsewhere, a file another agent owns, "skip
  slice 3" — is one sentence, not a cancel-and-redispatch:
  ```powershell
  pwsh -NoProfile -File scripts/delegate.ps1 -Refine <taskId> "the CARD-0050 failures are known-red; do not chase them"
  ```
  It lands between the delegate's turns (never mid-tool-call), its report will open by noting the
  refinement arrived, and the task's timeline records what you said. A still-queued task gets the
  message folded into its brief instead; a Blocked one needs `-Reply`, not this.
- **Trust the full report.** A `[task … done]` note may be a distillation (CARD-0330) with a
  one-line pointer at the task. The evidence is still the settled task's own `Result` —
  `pwsh -NoProfile -File scripts/delegate.ps1 -Status <taskId>` or the drawer Report section —
  never the distilled bullets alone. Flag a bad summary with
  `delegate.ps1 -Flag <id> -Verdict Lost|Noisy|Good [-Note]`.
- **Need a task's full text later?** `pwsh -NoProfile -File scripts/delegate.ps1 -Status <taskId>`
- **A stage run self-reports a finding line.** Review/Test/Merge/Deploy/`-OnAgent` get a stage by default; pass `-Stage` for a Debug/Docs/Custom dispatch that is actually a verify, cleanup, etc. The brief asks for `[antiphon-finding:<id> found\|clean]` on the line before the report token. If you judged differently from the delegate:
  ```powershell
  pwsh -NoProfile -File scripts/delegate.ps1 -Finding <taskId> -Stage Review -Found "hole in X; dispatched a fix"
  pwsh -NoProfile -File scripts/delegate.ps1 -Finding <taskId> -Stage Review -Clean
  ```

## What the delegate is told

Two things reach every delegate without you writing them, so writing them again only costs the
delegate's attention:

**The reporting contract**, in its brief — lead with the outcome, give only what the caller needs in
order to act, skip preamble and narration, and spill anything past the ceiling to a file and point
at it.

**The standing harness rules**, composed into its `--append-system-prompt` at launch from
`server/Bundles/delegate-basics.md` (CARD-0058) — foreground-only, no sub-delegation, commit and push
each slice, `--property:OutputPath=bin-<name>/` with a forward slash, verify pre-existing red by
stashing first. A sub-orchestrator also gets `server/Bundles/orchestrator.md`, the contract that
makes it decompose rather than do the work.

**That directory is canonical, and this file deliberately does not restate what is in it.** A rule
copied here would be a second version of the truth, and the copy is the one that goes stale — which
is exactly what happened to the list this section replaced. To change how every future delegate
behaves, edit the bundle in a PR; a running delegate keeps what it launched with, and a warm pool
agent until it retires (60 minutes idle).

What is NOT in a bundle, and is therefore yours to say in `-Goal`: today's known-red tests, what has
already landed, what is out of scope, and anything else that is true this afternoon and false next
month.

### Code, ordinary Review, Land, then Mutation (CARD-0478)

Code implements tests, runs every ordinary V/R, commits/pushes and returns `next: review`,
even with zero PCs. Review is separate for every complexity and judges the diff, ordinary
evidence and pending guard/PC inventory read-only. It does not require executed PCs before
land. Clean Review returns `next: land`, defects `next: code`; preserve the original Code
task ID as landing owner. Code's cost is authoring plus ordinary V/R; Mutation's is every
PC/variant plus discovery and reporting. TestDesign retains both numeric floors and their sum.

Before implementation land, create or discover one ordinary same-board Backlog companion:
`Post-land verification: <original identifier>`, label `post-land-verification`, stable key
`post-land-verification:<original-code-task-guid>`. Record full board/card GUIDs, Code/Review
task IDs, reviewed C, plan, commissioning project (including null), pending PC inventory and
`publication pending`. Append the reverse link through a content revision; preserve existing
human description and metadata. Serialize commissioning, inspect linked threads/tasks after
interruption and reconcile conflicting duplicates before dispatch. Missing acknowledgements
never authorize duplicate cards/tasks. The stable text key is discoverable, not DB uniqueness.
Exclude these cards from fresh feature picking; never Spawn a card session for a companion.

Order `delegate.ps1 -Land <original-code-task-id>`. Its correlated publication outcome starts
the continuation, not a synthetic next-stage report. Only structured HasPublication for
Landed, AlreadyPresent or LandedWithResidue qualifies. Queued land, task success, push exit,
local advance, event prose and LandRefused do not. Record O (operation), L=VerifiedSourceSha
and R=ObservedRemoteTargetSha alongside ordinary-reviewed C; rebase can make C differ from L.
Mutation tests L, even after master advances or reverts. Changed implementation needs fresh
ordinary V/R and Review; never relabel C evidence as L.

Update the same companion with O/L/R. Use a file-backed brief containing the Review handoff
verbatim, full reports/plan, all PC IDs/variants, both card GUIDs, Code/Review task IDs,
C/O/L/R, restart target for context and the persistent evidence root:

```powershell
$reviewGoal = Get-Content -LiteralPath '<review-brief-file>' -Raw
pwsh -NoProfile -File scripts/delegate.ps1 -Role Review -Card <original-card-guid> -ReadOnly -Dir '<code-worktree>' -Title 'ordinary review' -Goal $reviewGoal
# After Review and companion recording:
pwsh -NoProfile -File scripts/delegate.ps1 -Land <original-code-task-id>
# After confirmed publication and required deployment:
$mutationGoal = Get-Content -LiteralPath '<mutation-brief-file>' -Raw
pwsh -NoProfile -File scripts/delegate.ps1 -Role Mutation -Card <verification-card-guid> -Worktree -SourceLanding <operation-guid> -Title 'post-land mutation checks' -ExpectAbout <pc-floor-plus-analysis> -Goal $mutationGoal
# After settlement, restoration and durable evidence:
pwsh -NoProfile -File scripts/delegate.ps1 -CleanupVerification <mutation-task-id>
```

Use the same commissioning project and authorized repository. SourceLanding accepts only a
fresh Worker/Mutation/Worktree, no standing pin, OnAgent, Shared, ReadOnly or merge target.
Record the accepted task ID in a companion revision. Admission serializes same-O open tasks
including Blocked; a 409 names the existing task or refusal. Preserve the pending obligation
after quota/sign-in/capacity refusal; no silent provider fallback. A new explicit same-O attempt
requires prior terminal evidence/restoration assessment. Normal task queues own running-state
visibility; no tick creates cards or spends quota. Next-card Code may use its own Worktree.

Before mutation check exact managed creation, HEAD=L and clean tracked source/index; inventory
outputs. Await all commands, use exact-method green/compiling-defect/intended-red/restore/fresh-
build/green cycles and retain nonzero counts/assertions per variant. Run discovery even with
zero PCs. Missing tests or repairs become findings. Do not reset, substitute latest master,
commit/push even evidence amendments, merge, land or deploy from the snapshot. Use local
inherited execution only; never give snapshot access to an external executor, broker, remote
service or pre-existing process. Preserve full evidence/restoration outside the worktree at
the assigned common-Git verification root. Interrupted work retains contaminated paths.

Original Done means shipped: ordinary V/R and Review passed, publication confirmed, companion
recorded and other explicit deployment/acceptance conditions met. Close with C/O/L, Review ID
and `post-land Mutation pending: <companion>`, never PC-clean. Companion activity cannot reopen
the original automatically. Existing card statuses and explicit tracker actions are unchanged.
All PCs/variants and discovery clean after restoration: Mutation `done`, `next: none`; caller
closes the companion with L, counts, evidence and restoration. Findings: `done`, `next: decide`;
caller reads the full report, creates linked remediation and keeps verification open. Incomplete,
interrupted, unavailable or unknown evidence is failed/blocked, not clean. Explicit abandonment
uses Canceled with reason/successor, never Done/Clean.

Mutation's `next: decide` is a triage continuation, not automatically AskUserQuestion. Distinguish
coverage survivor/missing detection from reproducible unmutated-L product regression, invalid/
noncompiling/equivalent control and infrastructure failure. Report Where/Failure/Why/Fix,
severity, guard/PC and C/O/L evidence. Check the companion thread before creating each actionable
remediation; group one root cause. Forward fix at current target through ordinary V/R, Review
and a new land. Record O2/L2 and explicitly commission a new snapshot on this companion; old
Found evidence remains at L. Never automatically reland the original, revert a surviving mutant,
reset or force-push master. A justified revert is a new reviewed commit and deliberate landing.
Only real operator choices belong on a NeedsDecision move/reopen revision and attention feed;
do not create an alert-sink message or new status for a finding.

Cleanup is separate from both publication and test verdict. CleanupVerification irreversibly
seals this task's complete launch set, imports exact native receipts and reads fresh committed
authority under the genuine repository lease before deletion. A started attempt requires the
original container's descendant-zero observation and drained output; dead root, terminal row,
kill success or worker restoration alone never proves custody. Never-reserved evidence is a
separate closed path. Every attempt, exact L/creation/registration/ref, no owners/sequencer,
restoration and hashed task-owned outputs must match. Unknown files, dirty/replaced trees or
missing/unsupported custody remain actionable residue. No force or recursive deletion, and
cleanup never kills. A restored Failed/Canceled battery may clean without changing its verdict.
See docs/testing-and-build.md for the restoration schema and docs/ops-http.md for inspection.

Ship source admission, launch custody, no-autosave/no-land fences, cleanup and these contracts
together. Complete ordinary Code and Review before landing/deploying the feature. The caller
uses the canonical main-checkout runbook and verifies loaded SourceLanding behavior, composed
Code/Review/Mutation hashes and runner/host tracking directly; health alone is insufficient.
Only then commission the feature's own pending PCs. Never use a prose-only, Shared or Debug
fallback. Preserve active PC commands: await and restore through their owner before adopting
the new order, keep evidence at its tested SHA and rerun affected controls at the actual L.
No pins/holds/quotas, historical reports, role ordinals or parser tokens change; `verify` still
aliases Review and optional `-Stage Verify` remains outcome accounting.
