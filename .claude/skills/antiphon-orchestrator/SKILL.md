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
For AppHost and runner restarts or server2 rollouts without human confirmation, follow [orchestrator operational autonomy](../../../docs/orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout).
For a desktop runner restart use `pwsh -NoProfile -File scripts/restart-session-runner.ps1` (`-Hard` only for a planned supervisor refresh; `-KillSessions` is human-only); check `logs/apphost.restart.lock` and `logs/apphost.launch.lock` first, and inspect AppHost exit 3 before retrying.

- **Each pipeline stage at up to four; at most six tasks on server2 across all stages.** These are
  operator defaults. Before dispatching, read the effective concurrency limits and current occupancy
  from `GET /api/agent-tasks/pipeline` (stage and host limits/counts), `GET /api/session-runners`
  (seats and eligibility), and `GET /api/runner-defaults` (placement defaults). This three-route
  read is today's reality; CARD-0881 will replace it with a single effective-settings endpoint.
  Read each host's limit and in-flight count from `GET /api/hosts`; changing a host budget is an
  operator-only setting, never an orchestrator-initiated write. Antiphon's enforced
  limits are the ceiling; use the lower effective stage cap. Run stages in parallel, never more tasks
  in one stage than its cap. Prefer server2 (`-Runner server2`); use desktop/Windows only when work
  absolutely requires it, scoped to that piece.
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

## 2. The "branched from HEAD" warning is usually expected, not a bug

A fresh `-Worktree` dispatch for a non-Code role branches from current master HEAD, not the card's
actual kept sibling branch — this is by design (CARD-0538/CARD-0215), not something to fix per
dispatch. The fix is upstream: your goal text should already tell the agent which branch/commit to
fetch and checkout first ("fetch/checkout feat/card-task-XXXX before starting, it has N commits").
When you see the warning fire anyway, check whether your goal already covers it — if so, it's
harmless and self-corrects; verify by checking the task's later transcript shows it checked out
the right branch.

## 3. Checks: read the digest, act only when something changed

A `[check ...]` notification is a snapshot, not a live poll. Most checks need no reply at all —
just confirm progress looks sane (new commits, matching test counts, real investigation). Only
intervene when:

- **A genuinely new defect/finding appears** — file a card if it's structural (§0).
- **A stall is actually detected** (the harness's own `TaskProgressStalled` incident, or the
  transcript tail is byte-identical across 2+ checks with growing idle time and zero new tool
  calls). A stall is a detection state, never grounds for an automatic kill on its own — decide,
  don't reflexively cancel.
- **The "report-format stall" pattern** (see §6) — the task finished real work and committed it,
  but ended its turn with plain text instead of the structured completion block, so it never
  settles. This is common enough tonight (3+ times) that it's its own pattern, not a one-off.

## 4. When a report doesn't settle: nudge once, then read the artifact yourself

A recurring failure mode: a delegate finishes its actual work (writes a doc, commits, pushes),
narrates a plain-English summary, and ends its turn (`TurnEnd`) without the literal
`--- next stage ---` block the harness needs to mark the task `Succeeded`. The task sits
`Dispatched` indefinitely even though the deliverable is already safely on `origin/master`.

1. `-Refine` once, explicitly asking for the exact format: "end your turn with the required report
   — brief summary, then a literal '--- next stage ---' block with 'next: <role>' and a
   'handoff:' line."
2. If that also produces plain text with no structured block (check the task's status again after
   a few minutes — don't wait 20+ minutes hoping), **cancel the task** and **read the artifact
   file directly** (`git fetch origin master`, `git show origin/master:<path>`) to find its own
   `--- next stage ---` block, which the file itself usually does contain even when the chat
   report didn't. Dispatch the next stage from that. Don't keep waiting or re-refining past one
   attempt — it wastes real wall-clock time for no benefit once the pattern is confirmed.

This was frequent enough on `fable`-tier ClaudeCode dispatches to be worth flagging as its own
process-bug card if it recurs in a fresh session (see CARD-0551 in this project's history).

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

## 6. Landing: verify the actual branch holder, not just the "original owner"

A card can go through several fix/review rounds, each on its own fresh branch. The "original Code
owner" task recorded on the card is often stale — its own branch stops at an early round, while
the actually-reviewed, approved commit lives on a later round's branch. Before calling `-Land`:

1. `GET /api/agent-tasks/{candidate-task-id}` for both the recorded owner and the most recent
   round's task; compare `worktreeBranch` and whether it actually contains the reviewed SHA.
2. Land against whichever task's branch genuinely holds the tip, not just whichever id was
   originally recorded as "owner."
3. A `Conflicted` outcome (master moved since the branch's base) usually auto-spawns a `Merge`-role
   task to resolve it — check for one (`role=Merge`, `status=Dispatched`) before manually retrying;
   don't duplicate it.
4. After a confirmed `Landed` outcome: `git fetch origin master` in the canonical checkout to
   confirm the SHA, `restart-apphost.ps1 -ExpectedServerSha <sha>`, confirm healthy, **then**
   close the card with a reason summarizing the whole stage history (defects found, how fixed,
   final verified counts) — this is the last thing anyone reads about the card.
5. A stale `.git/index.lock` holds the land as `git_index_lock_stale` or
   `git_index_lock_held` (path and `Remove-Item` in the hold/`-Status` `Reason:`
   detail) instead of `target_advance_failed`. Confirm with `Get-Process git` that
   nothing older than the lock is running, then `Remove-Item` that path; the 5 s
   sweep resumes the land. Do not delete a lock a live git process still owns.

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
