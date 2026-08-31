# CARD-0258 — Who lands the work: git mechanics off the orchestrator

**Date:** 2026-08-31 · **Status:** plan (Plan pass; nothing built). Ground truth verified against
master `3f3ebd58` and the live delegation machinery (`AgentTaskService`, `AgentTaskReplyService`,
`DelegationWorktreeService`); the operational evidence is the orchestrator session of
2026-08-30/31 (~a dozen full manual merge pipelines, two real conflicts, one post-merge
regression, one remote-deploy failure) and CARD-0261's investigation (same night, plan
`2026-08-31-card-0261-shared-plan-commit-skip-investigation.md`).

## Verdict up front

**The deterministic git mechanics become a server operation (`land`), not a delegate typing git
commands; the judgment cases route to machinery that already exists — conflicts to the
auto-spawned Merge role, merge order and land/don't-land to the orchestrator. Deploy is a
separate category and stays orchestrator-triggered, shrunk to one script. Remote deploys are a
third category and get their own card. The settled-task follow-up gap is closed by making
`-OnAgent` degrade gracefully instead of 409ing.** Specifically:

1. **Neither of the card's two candidates is the answer as posed.** "The build delegate merges its
   own work" already exists — settlement merge-back (`TryMergeBackAsync`) IS the author landing its
   own branch — and it is right for the clean, immediate case but structurally wrong as the only
   mechanism (§2). "A dedicated Merge/Deploy role delegate lands it" is right for exactly one part
   — conflict resolution — and that delegate also already exists (auto-spawned, opus, briefed with
   the author's intent). What is actually missing is the piece between them: a way to land a
   finished branch **later**, after review latency and sibling landings have moved master, without
   the orchestrator running ten commands. That piece is deterministic (fetch, rebase-if-clean,
   verify, ff, push, cleanup) and deterministic work through a model is strictly worse than
   deterministic work in server code — the house already decided this once when merge-back became
   `DelegationWorktreeService` rather than a haiku brief (§3).
2. **Why the manual pipeline exists at all, mechanically:** an orchestrator dispatch gets
   `MergeTargetRef = null` — `AgentTaskService.CreateAsync:361` defaults it from
   `parent?.WorktreeBranch ?? parent?.MergeTargetRef`, both null for a Shared-workspace
   orchestrator, and `delegate.ps1` exposes no merge-target parameter. So every
   orchestrator-dispatched worktree task settles `LeftForHuman` ("branch kept — no merge target"),
   and even when a target IS set, `TryMergeBackAsync` never fetches origin, never pushes, and never
   re-verifies after rebase. The all-night manual pipeline was not a habit to break; it was the
   only path that existed.
3. **Landing writes the shared checkout, so self-landing build delegates would race.** The
   ff-advance falls back to `merge --ff-only` inside whatever checkout holds `master`
   (`AdvanceTargetAsync`) — the main repo, where Shared delegates and the check interpreter live.
   The one measured harm of merging master mid-Shared-task is a false check-interpreter verdict
   (CARD-0227). N build delegates each landing their own branch would contend on that checkout
   with each other and with running Shared work; serialization belongs server-side, where the
   Shared-writer lease (CARD-0063) already lives — one more reason landing is a server op, not a
   per-delegate final step.
4. **CARD-0261 killed the "just brief every build delegate to merge too" variant.** 0/4 Codex
   Shared delegates performed a git step their brief did not explicitly ask for; adding
   "fetch+rebase+merge+push before reporting" to every build brief adds a per-kind failure mode
   for the most safety-critical step in the pipeline. Instructions are the weakest layer here;
   the land step should not depend on them.
5. **"The orchestrator confirms" = reads the land outcome line, full stop.** The land op reports
   machine facts — the merged SHA, the pushed remote ref, the verification exit codes — not a
   model's claim about them. Trusting that line is *stronger* than trust-the-report. A refusal
   (conflict, red verification, held behind a Shared writer) is a decision point for the
   orchestrator — dispatch, defer, or drop — never a license to run the pipeline by hand (§5).
6. **Deploy is not "git operations" and stays orchestrator-triggered** — but shrinks to one script
   call with a single verdict line, folding in the migration check and health probes that tonight
   were five separate commands (§6). Remote/cross-machine deploys (am-service on server2) are a
   third category whose real defect is recipe location, not who runs it — own card (§7).
7. **CI status checking is out of scope for landing** and already has its house answer: detect and
   surface (CARD-0124's shape), react by dispatch. The land op cannot wait on an async 15-minute
   workflow, and it already ran the local build and the named tests (§8).
8. **The settled-task follow-up gap** (blocked twice on 2026-08-30/31, on two different kinds of
   question) is closed at `FollowUpOnTask`: when the prior agent is retired, degrade to a fresh
   task carrying an inherited context packet instead of 409 "delegate normally instead" — automate
   exactly what the refusal message tells the caller to do by hand (§9). Settled rows are never
   reopened.

## 1. Ground truth

### What exists (all verified in code today)

- **Settlement merge-back** (`AgentTaskReplyService.MergeBackAsync` →
  `DelegationWorktreeService.TryMergeBackAsync`): commit-all in the worktree → rebase onto
  `MergeTargetRef` → ff-advance the target (`git fetch . branch:target`, falling back to
  `merge --ff-only` in the checkout that holds it) → remove the worktree. Conflict aborts cleanly,
  flips the task Blocked, and auto-spawns a **Merge-role delegate (opus)** into the conflicted
  worktree, briefed with the conflict list and "resolve the way the TASK intended; its work is the
  newer change" (`AgentTaskService.CreateMergeTaskAsync`). A finished Merge task flips its parent
  back to Succeeded (`ResolveConflictedParentAsync`).
- **What it does not do:** no `git fetch origin` (rebase is onto the local ref), no `git push`
  (grep: zero pushes in `DelegationWorktreeService`), no post-rebase build/test, and — for every
  orchestrator dispatch — no target at all (point 2 above).
- **Follow-up verbs:** `-Reply` (Blocked task's question), `-Refine` (still-running steer; 409
  after settle), `-OnAgent` (`FollowUpOnTask`: new task pinned to the prior task's agent,
  inheriting directory/tier/kind; 409 when the pool janitor has retired the agent —
  `PoolIdleRetireMinutes` = 60).

### What the night measured (2026-08-30/31 orchestrator session)

- ~A dozen full manual pipelines (fetch / rebase / build / test / `merge --ff-only` / push /
  worktree-remove / branch-delete / AppHost-restart / migration-check) for CARD-0245, -0250,
  -0263, -0264, -0261 and more.
- **Two real conflicts needing judgment, both discovered at land time, not build time:**
  CARD-0250 and CARD-0245 independently took `AgentIncidentKind = 40` — the second branch to land
  hits the collision, after its author settled and (within the hour) retired.
  `InstructionBundleTests.cs` conflicted on adjacent unrelated lines — trivial, but still "read
  both sides".
- **One post-merge regression in the delegate's own new test** (a hard line-wrap in a bundle file
  broke a literal substring assertion) — caught only because the orchestrator ran the tests after
  rebasing. `-Refine` 409'd (settled), `-OnAgent` 409'd (agent retired); the orchestrator fixed it
  by hand. Same gap as CARD-0247 S4's "did the commit land", different question kind.
- **Migration verification never caused friction** — a direct `__EFMigrationsHistory` query after
  each restart, orchestrator-run every time.
- **The am-service remote deploy failed on a stale recipe** — a memory file naming an incomplete
  project list; caught by reading the remote build log.

## 2. The tradeoff the card asked to reason about, reasoned

**Author lands (build delegate merges its own work).** Steelman: zero extra dispatch, and the
author holds the intent that conflict resolution needs. Both true — and both already captured:
settlement merge-back runs at the author's settlement using the author's worktree, and the Merge
brief hands the intent forward ("read the conflicted task's goal"). What the author cannot do is
be present later: the conflicts that need judgment materialize when the *second* branch lands
(§1), after review latency, after the author's warm window. Extending author-lands to cover that
means either landing immediately at settlement (throws away the orchestrator's review window and
its merge-order judgment — the `overlapping-running=` header exists precisely so the orchestrator
sequences landings) or keeping authors alive speculatively (a session per finished branch, waiting
on nothing). Plus points 3 and 4 of the verdict: shared-checkout contention and the CARD-0261
instruction-following evidence.

**A dedicated landing delegate.** Steelman: fresh hands on a finished branch, uniform regardless
of who built it. But for the 90% clean case the work is fully deterministic, and a model typing
`git rebase` through a pty pays delivery ceilings, confirm loops, session cost and a nonzero
chance of improvisation — for work `TryMergeBackAsync` does in-process today. Where the fresh
hands genuinely earn their keep — conflicts — the delegate already exists and is already spawned
automatically with the right brief and the right tier.

**Resolution: split by nature, not by actor.** Deterministic mechanics → server code. Conflict
judgment → the existing auto-spawned Merge role. Order/whether/when → orchestrator. The only new
construct is the server-side land operation; no new role is added.

## 3. S1 — the `land` operation

A server-side continuation of an existing Worktree task's lifecycle, exposed as
`delegate.ps1 -Land <taskId>` (POST `/api/agent-tasks/{id}/land`). Runs as a background job on the
server; progress and outcome land as `AgentTaskEvents` on the task row, and the outcome line is
delivered to the caller's session the way a check-in note is. Pipeline:

1. **Hold behind Shared writers** — same repo, running Shared write-role task ⇒ queue the land
   behind it with a visible `Held` event (the CARD-0063 shape), never a silent wait and never an
   interleave. This encodes the "never merge master mid-Shared-task" rule (CARD-0227's false
   check-interpreter verdict) into the machine instead of the operator's memory file.
2. `git fetch origin`; target = `MergeTargetRef ?? master`. (Local master is canonical on this
   machine; the fetch is for the rare remote-ahead case, surfaced as a refusal, never auto-merged.)
3. Rebase the task branch onto the target **in the task's worktree** (reuse `TryMergeBackAsync`'s
   body). Conflict ⇒ abort, Blocked, spawn the existing Merge delegate — whose brief gains the
   remaining land steps (ff + push + cleanup), so a resolved conflict finishes the landing instead
   of handing half a pipeline back.
4. **Verify iff the base moved.** A rebase that was a fast-forward (base unmoved) skips this. A
   real replay runs, in the worktree pre-ff: `dotnet build --property:OutputPath=bin-land/`
   (alternate output path — the daemons hold `bin/`; delete after) plus an optional caller-named
   test filter (`-Land <id> -Verify "<treenode-filter>"` — default off; the full suite is ~28 min
   and is CARD-0131's job, not a landing gate). Red ⇒ refuse: `LandRefused` event naming the
   failing step and its output tail, Warning attention row, branch and worktree kept exactly as
   rebased so a follow-up delegate starts where it failed. **Never push red; never auto-fix** —
   this is the step that would have caught tonight's line-wrap regression before master.
5. ff-advance the target (existing `AdvanceTargetAsync`), `git push origin <target>`, remove the
   worktree, delete the branch. Push rejection (remote moved during the op) ⇒ refusal event, no
   retry loop.
6. **`Landed` event + outcome line:** `landed feat/card-task-<id> → master as <sha>, pushed
   (origin/master=<sha>), verify: build OK[, tests 14/14], worktree removed`. Machine facts only.

Refusals are loud actions-with-preconditions, not silent gates — consistent with the
detect-never-gate house style: nothing here blocks anyone *else's* work, and every refusal is an
event plus an attention row.

Settlement merge-back stays untouched for sub-orchestrator trees (where `MergeTargetRef` is the
parent's branch and immediate integration is the point). Orchestrator dispatches keep
`MergeTargetRef = null` deliberately: settlement leaves the branch for review, and `-Land` is the
orchestrator's explicit, ordered, one-call landing decision.

## 4. What stays orchestrator judgment

- **Whether and when to land**, and in what order (`overlapping-running=` header).
- **What to do with a refusal:** dispatch a Debug/follow-up delegate at the kept worktree, defer,
  or drop the branch. The refusal event carries the worktree path and failing step so the next
  brief writes itself.
- Nothing else. The orchestrator does not run `git fetch`, `rebase`, `merge`, `push`, `worktree
  remove` or `branch -d` for delegated work once `-Land` exists; §5 of `docs/orchestration-loop.md`
  is rewritten from "verify on master yourself" to "order the landings and read the outcome
  lines" (S4 below).

## 5. What "the orchestrator confirms" means now

The `Landed`/`LandRefused` line **is** the confirmation. It reports facts the server measured —
SHAs, exit codes, the pushed remote ref — so there is nothing for `git show`/`git diff`/`gh run
view` to add; running them anyway is the archaeology CARD-0247 exists to stop, one stage later.
Stated for the future reader the card asked this to be stated for: after `-Land`, the
orchestrator's own git involvement is **zero** — it does not check GitHub Actions, does not
re-run tests, does not inspect the merge. Doubt goes to a delegate (a `-OnAgent` follow-up, §9,
or a Review dispatch), the same ladder as report doubt.

## 6. Deploy: separate category, deliberately still orchestrator-triggered

Restarts are not git mechanics, and three properties keep them off the land pipeline:

- **A land op that restarts the AppHost restarts the server executing the land op** — it cannot
  report its own outcome. (A *delegate* could — delegate sessions live on the session-runner,
  which survives AppHost restarts, and report delivery just waits for the event pump reconnect —
  but see next point.)
- **Restarts batch and order.** Tonight's dozen landings needed a handful of restarts, placed
  between landings, not one per merge. Which landings force a restart, and when the stack can
  afford one against running Shared work, is judgment.
- **The watchdog contract** (locks, flap budget, exit 3 = refused) makes a retried restart
  actively harmful — exactly the kind of state-machine a model should not improvise against.

Decision: deploy stays an orchestrator action, shrunk to **one script** — a `scripts/deploy-local.ps1`
that wraps `restart-apphost.ps1`, waits for health, runs `verify-dev-stack.ps1 -SkipBrowser`,
checks `__EFMigrationsHistory` against `server/Migrations/` (the check that "never caused
friction" moves into the script because it is deploy's definition-of-done, not because it was
friction), and prints one unmissable `DEPLOY VERDICT: ok|failed <detail>` line (the
`test-client.ps1` pattern from CARD-0069). One tool call, one line read. Handing that single call
to a haiku Deploy delegate later is a cheap follow-up once the script exists; it is not this
card's core and buys little while the call is already one line of orchestrator context.

## 7. Remote deploys: third category, own card

The CARD-0265 am-service failure was not "who ran it" — it was a **stale recipe in a memory file**
(an incomplete project list) that no delegate would have gotten right either. The fix is recipe
location: the deploy procedure for each remote target lives in the repo next to what it deploys
(a `scripts/deploy-am-service.ps1` or a doc the compose file keeps honest — derive the project
list, don't enumerate it), and *then* it is safely delegable to a Deploy-role delegate with the
recipe as its brief. File as its own card; out of CARD-0258's build scope.

## 8. CI status: out of scope, and already has an answer

`gh run list`/`gh run view` polling is the same "orchestrator runs a CLI to confirm" shape, but
landing cannot gate on it (async, ~15 min, and the land op already ran the build and the named
tests locally). The house pattern for CI is CARD-0124's: detect and surface. If red CI on master
should reach the attention feed, that is a small sweep card of its own; reacting to it is a Debug
dispatch. The orchestrator never loops on `gh run view`; nothing in this plan reads GitHub state.

## 9. S2 — asking a settled task a follow-up

Two 409s blocked this twice in one night. The fix is one arm in `AgentTaskService.CreateAsync`'s
`FollowUpOnTask` block: when the prior agent row is gone (or the task never ran on an agent),
**degrade instead of refuse** — create the task as a normal fresh dispatch whose brief is
prefixed with an inherited context packet: the prior task's Goal, its full Result, its completion
header (git facts, drift, overlapping-running), its `WorktreePath`/`WorktreeBranch` if the
directory still exists, `RepoPath`, and the card binding. That is a mechanical transcription of
what the current 409 message ("delegate normally instead; the report is still on the task") tells
the caller to assemble by hand. Kind/tier follow the normal role policy on the degraded arm (the
prior session's constraints no longer bind). The response says which arm ran: `follow-up on the
live agent` vs `agent retired — fresh delegate with inherited context`.

Deliberately NOT done: reopening the settled row (settlement finality is what card transitions,
warm release, and `ResolveConflictedParentAsync` all lean on), and keeping delegates warm longer
speculatively (a session per settled task, waiting on questions that usually never come). Note the
*mechanical* half of tonight's questions needs no delegate at all once the rest of this plan and
CARD-0261 land: "did the commit land" is CARD-0261 S1's `git=uncommitted` header; "did the merge
land" is the `Landed` event.

## 10. Slices

- **S1** — the `land` operation: endpoint + background job + `delegate.ps1 -Land` (with
  `-Verify <filter>`), Shared-writer hold, conflict → Merge spawn with extended brief,
  `Landed`/`LandRefused` events + attention rows + caller outcome line. Reuses
  `TryMergeBackAsync`'s body; the new surface is fetch, verify, push, and the hold.
- **S2** — `FollowUpOnTask` graceful degrade with the inherited context packet.
- **S3** — `scripts/deploy-local.ps1` (restart + health + migration check + `DEPLOY VERDICT`
  line).
- **S4** — `docs/orchestration-loop.md`: rewrite §5 ("verify before merging" → "order and land"),
  §6 (one-script deploy), §8 (cleanup is the land op's job), and close §0's "being designed
  separately (CARD-0258)" note.
- **New cards, not slices:** remote-deploy recipes into the repo (§7); CI-red attention sweep
  (§8) if wanted.

S1 is the payback (ten commands → one call, and the post-rebase verification that catches the
regression class tonight hit); S2 is independent and small; S3/S4 are cheap. All independently
landable.
