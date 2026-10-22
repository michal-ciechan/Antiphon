---
name: antiphon-orchestrator
description: Run the standard Antiphon pipeline (Investigate -> Plan -> TestDesign -> Code -> Review -> land -> Mutation) as an orchestrator - dispatch, monitor, nudge/cancel stalls, land, restart, close. Use when driving cards through the pipeline over an extended session, not for a single one-off delegate call (see antiphon-delegate for that).
---

# antiphon-orchestrator — run the standard pipeline

This is the operating loop, distilled from a long live session (2026-09-16/17) that drove several
cards end to end. `docs/orchestration-loop.md` is the authority for mechanics (CARD-0146 handoff
contract, landing, scopes); this skill is the judgment layer on top of it — what to actually do,
tick by tick, and the specific traps that cost real time when missed.

## 0. Policy defaults, unless the user says otherwise

Windows Review/Debug routing and the pending CARD-1011 release gates are owned by
[Windows Review and Debug routing](../../../docs/orchestration-loop.md#windows-review-and-debug-routing).
OS placement does not authorize a routing-pin bypass. Read effective pins and
defaults; omit kind/level for ordered fallback. The future Debug pair stays gated.

For runner-bound Worktree tasks, never ask the delegate to rebase, amend or force-push its task
branch. It must advance the owned branch by fast-forward pushes. Land performs the target rebase;
use a fresh task with `-StartRef` when a different base is needed. A confirmation-only step is a
Review task, not a Code task requiring new attributed progress (CARD-0779).

A live Blocked child keeps its runner seat; an accepted Reply resumes work and does not itself free the seat. Answer it within the session or surface the missing decision before leaving; do not leave one overnight. At capacity, read GET /api/session-runners/{id}/slots: orphan=true is not a count of free seats; a live Blocked owner reads orphan=false with its park field. BlockedTaskParking:Enabled and TerminalRunnerSeatRelease:AutomaticEnabled default to false; do not count a live Blocked seat free until its release is confirmed. An Exited or Failed status, or absence from a listed catalogue, drops that seat. Parking never stops a Working session (CARD-1083).

Use -Reply for a Blocked answer, -Continue only for a question with standing authority, and -Refine only for Queued, Dispatched or Working tasks. Follow docs/session-runtime-invariants.md for released-park admission and prerequisite publication.

The owner is `docs/orchestration-loop.md` §1, "Standing pipeline policy" (CARD-0533); this is the
short form.
Operator standing operating rules (failures get a Debug task, in-flight target on the landing host, no land from an Interim Review, minimal testing, master lands are not activation, reviewed rc ports): [standing operating rules](../../../docs/orchestration-loop.md#standing-operating-rules) (CARD-1469).
For AppHost and runner restarts or server2 rollouts without human confirmation, follow [orchestrator operational autonomy](../../../docs/orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout).
During a staged server2 rolling rollout, keep dispatching new work to `server2-temp` until the standing `server2` is fully ready (`redeploy-old` exit 0 and smoke/Both passed), then send new work to `server2` and start `drain-temp`; there must always be a server2 accepting work, and the orchestrator never drops below its in-flight target (6) for a rollout ([continuous dispatch](../../../docs/docker-stack.md#continuous-dispatch-during-rollout)).
For a desktop runner restart use `pwsh -NoProfile -File scripts/restart-session-runner.ps1` (`-Hard` only for a planned supervisor refresh; `-KillSessions` is human-only); check `logs/apphost.restart.lock` and `logs/apphost.launch.lock` first, and inspect AppHost exit 3 before retrying.

- **Each pipeline stage at up to four; at most six tasks on server2 across all stages.** These are
  operator defaults. Before dispatching, read the effective-settings route (CARD-0881):
  `GET /api/agent-tasks/pipeline?projectId=<id>` (or `?unscoped=true`). Its `concurrencyScopes`
  entry carries the absolute and per-stage effective limits with sources, open/queued counts and
  remaining slots; `hosts` carries each host's effective limit, source, in-flight count and
  `remaining`; `runners` carries seats and eligibility; `runnerDefaults` carries placement. Use its
  limits; changing a host budget is an operator-only setting, never an orchestrator-initiated
  write. Antiphon's enforced limits are the ceiling; use the lower effective stage cap. Run stages
  in parallel, never more tasks in one stage than its cap. Prefer server2 (`-Runner server2`); use
  desktop/Windows only when work absolutely requires it, scoped to that piece. Live operating
  settings: read the file at ANTIPHON_ORCHESTRATOR_INSTRUCTIONS (or GET /api/orchestrator-instructions)
  at session start, after compaction and when a settings-changed note arrives; it outranks the
  numbers in AGENTS.md.
- **`-Worktree` by default on every dispatch.** Shared checkout only when explicitly told to
  default to Shared (globally/per-project/per-invocation), or when a task must continue on a
  branch that's already checked out elsewhere (see §4).
- **One stage transition per completion.** Read `next=`/`handoff:` off the header and dispatch
  that stage (§1). Different cards may run in the same or different stages in parallel up to the
  effective caps; dispatch each transition from its own completion.
- **Code stage at its depth cap** (four unless Antiphon enforces less; in flight + queued + ready,
  read from `GET /api/agent-tasks/pipeline`). Below the cap, pull the next unstarted Backlog card,
  lowest rank first, and start it through Plan toward Code; at the cap, start no new Plan toward Code.
- **Same source area as an in-flight Code task: defer that card's Code**, even with a free slot —
  a worktree scope overlap only warns (CARD-0063), and the conflict lands on you at merge
  (CARD-0535/CARD-0537, 2026-09-19).
- **Land as soon as a stage's work is confirmed** (§6); don't hold landings to the end of a
  session.
- **Concurrency is per-project.** The absolute cap (`concurrency_limit` 409) and each stage's
  `recommendedInFlight` are both scoped to the project the board belongs to — unrelated boards
  (other projects) never count against it.
- **`-IgnoreConcurrencyLimit` only for `axis: absolute` with no same-stage occupant** in the 409's
  `open` list. `axis: role`, or a listed occupant in the role you are dispatching: defer. The
  absolute axis wins the report when both caps trip, so read the list.
- **File a card immediately** for any structural bug/defect found during Investigate or Review,
  before moving on. Don't let a real finding evaporate into a chat message.
- **A defect found live after land that a Clean Review approved gets a post-land retrospective**
  (companion card, Investigate, then a Low-tier instruction-gap pass; `docs/orchestration-loop.md`
  section 1, CARD-0811). A Review or Mutation catch before land is not a retrospective.

## 1. Dispatch the next stage from the completion header, never the report body

Every stage report ends with a `--- next stage ---` block naming `next:` and `handoff:`. Dispatch
exactly that stage next. Write the new goal file referencing the prior task's branch/commit and
the handoff text — don't re-derive it by reading the report prose.

Goal text always goes through a file (`Write` a scratchpad `.md`, then `Get-Content -Raw` into
`-Goal`), never inline through Bash — long text with quotes/backticks/newlines gets mangled
otherwise.

## 2. A delegate's base is chosen at dispatch; it stays on its own branch

A fresh `-Worktree` task works on its own recorded `WorktreeBranch`; git-common forbids switching
branches, and a branch under Review is frozen. Do not tell a delegate to fetch or check out a
sibling's branch. To continue a sibling's work, select the sibling's tip as the creation base:
`-StartRef <full SHA>` (owner: Continuing a sibling's work (CARD-0613) in
`docs/orchestration-loop.md`). Without `-StartRef`, dispatch selects the base from the card's kept
siblings; outcomes include continuing one, taking the target, refusing and holding. Tested cases are in the table
below; read a refusal's code and message before re-dispatching. When a brief names
a branch, copy the sibling task's recorded `WorktreeBranch` (`feat/card-<n>-<stage>-<id>` or
`feat/card-task-<id>`) rather than rebuilding it from the task id.

| Behaviour | Test (TUnit, Antiphon.Tests) | What it asserts |
|---|---|---|
| `-StartRef` cuts a new branch at the requested base | `WorktreeStartRefDispatchTests.C613_StartRefCreatesOwnBranchAndBaseline` | for a branch, tag or full SHA: HEAD is the source SHA, the branch is the new task's own `feat/card-task-<id>`, and the source checkout keeps its branch and HEAD |
| One clean same-card kept sibling, Review created without `-StartRef` | `AgentTaskDispatchBaseGuardTests.retained_real_create_continues_clean_same_card_tip` | the new checkout HEAD and `WorktreeBaseSha` equal the sibling SHA; master is unchanged |
| Two divergent same-card kept siblings | `AgentTaskDispatchBaseGuardTests.retained_divergent_create_refuses_and_both_recovery_modes_work` | create is refused `worktree_base_ambiguous`, naming both tasks, `-BaseTask` and `-FreshWorktree`, and no task is added; with `WorktreeBaseTask` it continues the named sibling; with `FreshWorktree` it takes the target |
| A kept sibling whose land is in flight | `AgentTaskDispatchBaseGuardTests.a_sibling_land_in_flight_holds_until_the_base_contains_it` | the task stays Queued with no checkout and one Held event naming the sibling; after the land completes it dispatches |
| Delegates receive the assigned-branch-only and frozen-Review rules | `InstructionBundleTests.C1213_GitContractPins` | Code, Review, Mutation and orchestrator compositions carry each git-common sentence once |

## 3. Checks: read the digest, act only when something changed

A `[check ...]` notification is a snapshot, not a live poll. Most checks need no reply at all —
just confirm progress looks sane (new commits, matching test counts, real investigation). Only
intervene when:

- **A genuinely new defect/finding appears** — file a card if it's structural (§0).
- **A stall is actually detected** (the harness's own `TaskProgressStalled` incident, or the
  transcript tail is byte-identical across 2+ checks with growing idle time and zero new tool
  calls). A stall is a detection state, never grounds for an automatic kill on its own — decide,
  don't reflexively cancel.
- **A report without its handoff block or token** (see §4): read the task status first; a
  report with its token has settled.

## 4. A report without its handoff block or report token

The report token (`[antiphon-report:<id> done|blocked|failed]`) settles a task; the
`--- next stage ---` block only sets `next=`. A report with the token and no block settles, and its
completion header says `next=unmarked`. A turn that ends with neither is handled by the delivery
watchdog: it nudges for the closing line, then settles the task or blocks it as unmarked waiting. Read the task's
status and stored report before acting; do not cancel a settled task. For `next=unmarked`, read the
report or its artifact and dispatch the next stage yourself.

| Behaviour | Test (TUnit, Antiphon.Tests) | What it asserts |
|---|---|---|
| A stage report with the token and no block settles | `AgentTaskReplyIntegrationTests.a_stage_role_without_the_block_gets_next_unmarked` | status Succeeded; the completion note contains `next=unmarked` |
| A missing block parses as not found | `PipelineHandoffParseTests.a_missing_block_is_not_found` | a `done` report with no block: `Found` false, header bit `unmarked` for a stage role |
| An untokened reply after a delivered nudge settles | `AgentTaskDeliveryWatchdogTests.an_unmarked_reply_after_a_delivered_nudge_settles_unmarked_after_nudge` | status Succeeded, evidence `UnmarkedAfterNudge`, the reply is the result |
| An untokened idle session past the waiting clock blocks | `AgentTaskDeliveryWatchdogTests.an_unmarked_idle_after_the_waiting_clock_blocks_unmarked_waiting` | status Blocked, evidence `UnmarkedWaiting`, the session stays Running |

## 5. API quirks to route around, not fight

- `GET /api/agent-tasks?boardId=Y` **is** a filter (CARD-0515). The body is always an envelope
  `{ scope, items, excluded }` — read `.items`, never treat the envelope as one row. An omitted
  `boardId`/`projectId` is the whole fleet (`scope` is null). Query a known task with
  `GET /api/agent-tasks/{id}` when you already have the id.
- `?status=X` works: case-insensitive, comma list unions, an unrecognised value is
  `422 validation_failed` (pinned by CARD-0546). What looked like "zero rows for a genuinely
  running task" was PowerShell, not the server: a bare `Invoke-RestMethod ... | Select-Object`
  emits the JSON array as one `Object[]` and prints a header plus one blank row for ANY array.
  Always parenthesise the call — `(Invoke-RestMethod ...) | Select-Object ...`, or
  `(Invoke-RestMethod ...).items | ...` for the list envelope — or assign it to a variable and pipe
  that. Inline `@(Invoke-RestMethod ...)` does NOT enumerate (verified live). For occupancy prefer
  `GET /api/agent-tasks/pipeline` (in-flight / queued / blocked / ready per stage) over a
  hand-filtered list.
- The response nests fields under a top-level `"summary"` key — `$response.summary.status`, not
  `$response.status`.
- Cancel needs the **full GUID**, not the short id the chat notifications use — `GET
  /api/agent-tasks/{shortid}` first to resolve it, then `POST /api/agent-tasks/{full-guid}/cancel`.

## 6. Landing: the original Code owner lands; the reviewed source is named separately

A card can go through several repair rounds, each on its own branch. The landing owner stays the
original Code/Worktree task; a newer reviewed tip on another task's branch does not replace it.
Owner: the Reviewed owner recovery table (CARD-0753) and
[RepairSource succeeded; owner Failed](../../../docs/orchestration-loop.md#repairsource-failed-owner-recovery)
in `docs/orchestration-loop.md`.

1. Ordinary: `-Land <owner> -ExpectedSourceSha <sha> -ReviewEvidenceId <evidence>`, the Review's
   `subjectTaskId` being the owner.
2. The reviewed tip is on another task's branch (a `-StartRef` repair): `-Land <owner> -FromTask <source>
   -ExpectedSourceSha <sha> -ReviewEvidenceId <evidence>`, the Review's `subjectTaskId` being the source.
3. The reviewed tip is on the owner's own branch and the owner settled Failed or Blocked: the
   ordinary flags plus `-RecoverReviewedSource`. Use `-FromTask` or `-RecoverReviewedSource`, not both.
4. A `-RepairSource` task is not a landing owner; its land is refused with
   `repair_source_landing_owner_required`, naming the owner recipe.
5. A `Conflicted` outcome (master moved since the branch's base) usually auto-spawns a `Merge`-role
   task to resolve it — check for one (`role=Merge`, `status=Dispatched`) before manually retrying;
   don't duplicate it.
6. After a confirmed `Landed` outcome, restart AppHost only when the landed commit is the
   `rc/YYYY-MM-DD` rc tip the hosts run. A master-only land does not restart AppHost or server2.
   When a restart is due, advance the checkout those hosts run to that tip (`git pull --rebase`
   there; `git fetch` alone moves no local HEAD), then `restart-apphost.ps1 -ExpectedServerSha <sha>`,
   which refuses with exit 3 when that SHA is not the checkout HEAD, and follow the Post-land
   server activation check (CARD-0495) in `docs/orchestration-loop.md`.
   **Then** close the card with a reason summarizing the whole stage history (defects found, how
   fixed, final verified counts) — this is the last thing anyone reads about the card.
7. A stale `.git/index.lock` holds the land as `git_index_lock_stale` or
   `git_index_lock_held` (path and `Remove-Item` in the hold/`-Status` `Reason:`
   detail) instead of `target_advance_failed`. Confirm with `Get-Process git` that
   nothing older than the lock is running, then `Remove-Item` that path; the 5 s
   sweep resumes the land. Do not delete a lock a live git process still owns.

| Behaviour | Test (TUnit, Antiphon.Tests) | What it asserts |
|---|---|---|
| A RepairSource task's land is refused and names the owner route | `RepairSourceLandRefusalTests.C603_RepairLandRefusalNamesReviewedOwnerRecovery` | 409 `repair_source_landing_owner_required`; the message names `-Land <owner>`, `subjectTaskId`, `-ExpectedSourceSha`, `-ReviewEvidenceId` and `-RecoverReviewedSource`; no land request is queued |
| Adoption requires a Review of the source | `AgentTaskLandApprovalRequestTests.C788_AdoptionSubjectMismatchNamesSourceAndFlag` | an owner-subject Review with `AdoptFromTaskId` is refused `review_evidence_subject_mismatch`, naming `-FromTask <source>` |
| A Failed owner lands its reviewed repaired tip | `RepairSourceRecoveryLandingTests.C603_FailedOwnerLandsReviewedRepairedTip` | with `RecoverReviewedSource` and an owner-subject Review the request queues and completes with recovery mode `OwnerReviewedSource`, recording owner status Failed |
| `-FromTask` with `-RecoverReviewedSource` is refused | not covered by a test | `AgentTaskLandService` refuses `recovery_source_ambiguous`; `delegate.ps1` refuses before the request |

## 7. Mutation is post-land, throttled, and (as of CARD-0552) trackable

Mutation batteries (PC/positive-control red-restore-green cycles) run against a landed card's
`SourceLandingOperationId`, not inline per round — this is deliberate (CARD-0478/CARD-0544): full
per-round verification was too slow. A Mutation task needs a companion card (title `Post-land
verification: <original identifier>`, label `post-land-verification`) distinct from the original
landed card, and a confirmed `AgentTaskLanding` row (a manual git-push land via a Merge-conflict
task usually has none — retry `-Land` once master already has the content to get one, or accept
the card can't be sourced-Mutation-verified until CARD-0552's backfill endpoint covers it).

## 8. Usage/provider ceilings

If a task's transcript shows a session/usage-limit message or the harness logs
`ApiErrorTurnDied`/`rate_limit` (HTTP 429), don't force-retry into the same wall — it usually
carries its own scheduled auto-resume time. Nudging once is harmless (costs little, sometimes
clears a session-local hiccup that isn't the account-wide limit), but stop dispatching *new* work
until either the resume window passes or the user says otherwise. Default posture (unless told
differently): keep the backlog queued rather than idle-waiting — the user may prefer cards sit in
Backlog and get worked whenever capacity allows, rather than blocking on strict human-in-the-loop
gating for every dispatch.
