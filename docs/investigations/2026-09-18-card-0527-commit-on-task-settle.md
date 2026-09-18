# CARD-0527 Commit on task settle: already built and landed, never once succeeded live

Investigate, 2026-09-18, task `e28d4c3d`, worktree `card-task-e28d4c3d` at Antiphon `3a62074e`
(= `origin/master`). Read-only: code, the delegation database (`antiphon-postgres`), the live
server (`GET /api/version`), the server Serilog files under `C:\src\Antiphon\server\logs\`, and
timing runs of one read-only `git log` in three live checkouts. No fix was designed or built; one
fix idea is confined to the last section.

Supersedes nothing: the 2026-09-15 investigation
([2026-09-15-card-0527-commit-on-task-settle.md](2026-09-15-card-0527-commit-on-task-settle.md))
answered the brief's questions 1-5 for the code as it was then, and its answers led to the plan
and the code that is now on master. This document records what exists today, whether it works
live, and why the card is still in Backlog.

## Verdict

**Confirmed, from code, stored rows and a reproduced measurement.**

1. **The card's ask is built, landed and live.** `AgentTaskReplyService.TryCommitOnSettleAsync`
   (`server/Application/Services/AgentTaskReplyService.cs:3139`) runs at settle for every
   Succeeded Shared task, commits the task's footprint in-process through `GatedCommitService`
   (ignore gate, never pushes), and spawns a Commit-role child through the routing pin when it
   cannot attribute the dirty paths. 42 `CARD-0527` commits are on `origin/master`; the live
   server (`aa02c398`) contains all of them; `Delegation:CommitOnSettle` defaults to `true`.
2. **It has never produced a commit in production.** Zero `Committed` events (type 33), zero
   Commit-role children, zero `CommitRecoveryStarted` rows exist in the database. Since go-live
   (2026-09-17 07:18Z) the hook has run for 19 eligible Shared settles: 10 in `markdown-package`
   found a clean tree, and **all 9 in the Antiphon checkout threw** before reaching the gate.
3. **Mechanism of the throw:** the recovery search `git log --all --reflog --fixed-strings
   --all-match --grep=<task> --grep=<settlement> --format=%H`
   (`server/Application/Services/GitWorkspaceService.cs:965`) is killed by the 15 s git timeout
   (`server/Application/Settings/GitSettings.cs:6`) in `C:\src\Antiphon`, `existing.Succeeded`
   is false, `:3178` throws `settlement_recovery_unavailable`, `OnTurnEndLockedAsync` swallows it
   (`:205`), and the report sweep re-hands the same boundary every 60 s
   (`DelegationSettings.ReportSweepRehandSeconds`, `:521`) and throws again. 362 identical
   throws on 2026-09-17 across 9 sessions. Each of the 9 tasks (Plan/Investigate/TestDesign for
   CARD-0545/0547/0550/0552) sat unsettled for 27 min to 4 h 46 min and was then **Canceled**;
   none ever delivered a completion note. Reproduced today: the exact command takes 74-85 s in
   the main checkout and 135 s from a worktree; without `--reflog` it takes 1.1-1.3 s. The
   `--reflog` flag entered the search in `18ece90e` (F15/F16 repair, 2026-09-17 02:18) and went
   live at the 08:18 (+01:00) restart the same morning.
4. **The board is wrong about this card.** Revision 33 (2026-09-17 01:25) and cards CARD-0548 /
   CARD-0549 say the 2026-09-15..17 chain was an unrelated "recovery-identity-loss" fix and that
   CARD-0527 "has had zero design/investigation work done". The commits, the docs and the task
   briefs show the opposite: every one of those tasks was this card's own Investigate → Plan →
   TestDesign → Code S1-S6 → Review/repair rounds F8-F18, all editing `TryCommitOnSettleAsync`.
   CARD-0549 is a retroactive duplicate record of CARD-0527's own repair rounds.
5. **The worktree-default policy (2026-09-17 20:43Z, orchestrator memory, no code) hides rather
   than removes the problem.** It is why no Antiphon Shared settle has happened since 21:20Z and
   why the 09-18 log has zero throws. The Worktree path shares the same search after its own
   gated sweep (`GatedCommitService.cs:195` → `FindCommitOperationAsync`), so the first Worktree
   task that leaves a dirty tree is exposed to the same timeout (reasoned from code, not observed:
   no Worktree task has left a dirty tree since go-live).

## 1. What is on master, mapped to the card's acceptance

| Card acceptance | Code | Status |
|---|---|---|
| A dirty settle results in a commit without the orchestrator acting | `TryCommitOnSettleAsync` (`AgentTaskReplyService.cs:3139-3400`): tier 1 in-process footprint commit; tier 2 `SpawnCommitChildAsync` (`:3406`) → `AgentTaskService.CreateCommitTaskAsync` (`AgentTaskService.cs:2538`) with routing-pin resolution (live pin: `Commit` Required Codex/Low → ClaudeCode/Medium → Grok). Child commits through `POST /api/agent-tasks/{id}/commit` (`server/Api/Endpoints/AgentTaskEndpoints.cs:210`) via `scripts/task-commit.ps1`; its own settle audits every commit (`AuditCommitChildAsync`, `:3433`). | Built. **Not working live** (section 3). |
| Ignored paths provably never staged | `GatedCommitService.CommitHeldAsync`: refuses when any `.gitignore` is dirty (`GatedCommitService.cs:103`, `IgnoreRulesChanged`) and when `check-ignore` matches a candidate before or after staging (`:125`, `:161`, `IgnoredPathStaged`); index captured and restored on refusal; `commit --only <pathspec>` with an `antiphon-paths` manifest trailer (`:182`). Footprint for Shared = transcript edits since `DispatchedAt` ∪ report-named paths (`:3258-3266`), never the tree. `.antiphon/` excluded (`SettlementDirtyPaths`). Tests: `C527_deleted_gitignore_refuses_spawns_the_child_and_warns`, `C527_tracked_then_ignored_path_refuses_the_whole_footprint` (`tests/Antiphon.Tests/Application/AgentTaskReplyC527Tests.cs`). | Built. |
| Nothing pushed automatically | No push call in either tier; `RecordingGitWorkspaceService` test spy asserts no `push` verb (Code brief correction 4, `docs/investigations/2026-09-16-card-0544-evidence/task-rows.json:42`). | Built. |
| Per-project opt-out; defined `-NoCommit` interaction | `CommitOnSettlePolicyResolver.Resolve` (`server/Application/Services/CommitOnSettlePolicyResolver.cs`): task `Never/Always/Agent` > project `bool?` (`Projects.CommitOnSettle`, `scripts/project.ps1 set -CommitOnSettle On|Off|Inherit`) > global `Delegation:CommitOnSettle` (`DelegationSettings.cs:45`, default `true`). `delegate.ps1 -NoCommit` sends `Never` (`scripts/delegate.ps1:181`, `:873`); prose "do not commit" is a create-time advisory only (`AgentTaskService.cs:881-898`). Documented at `docs/orchestration-loop.md:266-283`. | Built. |
| "Cheap" tier honoured | `CreateCommitTaskAsync` calls `RoutingPinService.ResolveAsync` and stores `RoutingPinId`; `routing-pin.ps1 get -Role Commit` = `Codex/Low (gpt-5.6-luna) +2: ClaudeCode/Medium, Grok`. | Built. |
| Mutation stage (post-land PCs) | Never dispatched. `docs/investigations/2026-09-16-card-0527-f17-f18-repair.md:55-66` lists PC-1..87 pending; CARD-0552's debt table carries them under CARD-0549 (86). | **Owed.** |

Eligibility (`server/Application/Services/CommitOnSettleEligibility.cs`): `Succeeded` ∧ `Shared` ∧
`SourceLandingOperationId == null` ∧ role ∉ {Commit, Merge, Mutation, Check, Distill, Diagnose} ∧
`RepoPath` set. Worktree tasks take the separate gated sweep in
`DelegationWorktreeService.TryMergeBackAsync` (`server/Application/Services/DelegationWorktreeService.cs:514-549`).

Where the old warning went (brief question 1): `SettleAsync` calls the hook at `:752`; only when
it returns `null` (ineligible, non-git directory, or clean tree) does the pre-existing
`TryDescribeGitAsync` Shared arm (`:3518`, `:3527-3551`) run and emit
`Report names N file(s) still uncommitted in the shared checkout`. The Off policy arm inside the
hook (`:3225-3245`) reproduces the same warning with header `uncommitted:N (commit-on-settle off)`;
`-NoCommit` renders `uncommitted:N (no-commit)`; tier-2 renders `uncommitted:N → commit task
<id>`; the task/depth cap renders `uncommitted:N (commit task not spawned: run at task cap)` (`:3424`).

## 2. Landing and go-live timeline

| When | Event | Source |
|---|---|---|
| 09-15 09:27Z .. 10:45Z | Investigate `62040dbd`, Plan `35921275`, TestDesign `2139f2f3` landed (`e7868a49`, `e4bc7ae5`, `b97abfd8`) | card history rev 1-6; `Landed` events (type 21) on those tasks |
| 09-15 10:45Z .. 09-17 01:12Z | Code/Review rounds (tasks `b23d0fe2`→`2853f966`, `6d2a2060`→`360e223e`, `0d48a707`, `b23741b2`, `ca4eef9c`, `151b4e13`, `67a63a4f`; reviews `89bfbbdf`, `4d210064`, `32c1f938`, `7ef54b1c`, `80155ada`, `1d0539bc`, `c9a3d250`/`d17aff7f`) | card history rev 7-30 |
| 09-17 01:12Z | Code task `2853f966` land **refused** `reviewed_source_mismatch` | `LandRefused` event (type 22) |
| 09-17 01:17Z-01:22Z | Merge task `2c956bc5` resolves the rebase conflict, fast-forwards master to `e2a49f3a` | card history rev 31-32; CARD-0549 close reason |
| 09-17 01:18:54 (+01:00) | `18ece90e fix(CARD-0527): recover durable attempts across refs and reflogs` — introduces `--reflog` in `FindGatedCommitsAsync` | `git log -S"--reflog" -- server/Application/Services/GitWorkspaceService.cs` |
| 09-17 01:25Z | Revision 33 moves the card to Backlog as "zero work done" | card history rev 33 |
| 09-17 02:20:20 (+01:00) | Main checkout fast-forwards to `e2a49f3a` | `git -C C:\src\Antiphon reflog` |
| 09-17 08:18:51 (+01:00) | Server starts; `Applying migration '20260915113406_AddCommitOnSettlePolicy'`; **go-live** | `antiphon-20260917.log:8953` |
| 09-17 09:57:16 (+01:00) | First throw (task `c9140e5d`) | `antiphon-20260917.log:11578` |
| 09-17 19:52 (+01:00) | Restart at `aa02c398` (CARD-0547 land); throws continue 20:50-22:20 | log; sessions `8c2e7558`, `7b87e2d5`, `dee1a5b1` |
| 09-17 20:43Z | Operator: every dispatch `-Worktree` (memory `feedback-default-worktree-isolation`) | memory file |
| 09-17 21:20Z | Last Antiphon Shared settle attempt (task `fb65210d`, Canceled) | `AgentTasks` |
| 09-18 | Zero throws, zero Shared Succeeded settles | log; `AgentTasks` |

All five CARD-0527 landings (`e7868a49`, `e4bc7ae5`, `b97abfd8`, `b12e852e`, `197289d0`) and the
two repair tips (`e2a49f3a`, `00ed5d88`) are ancestors of the live SHA `aa02c398`
(`git merge-base --is-ancestor`). `origin/master` is 5 docs commits ahead of live.

## 3. Live behaviour since go-live (database, 2026-09-18)

Counts (whole table, all time): `Committed` 0, `CommitRecoveryStarted` 0,
`CommitRecoveryNotNeeded` 0, `CommitRecoveryAbandoned` 0; Commit-role tasks created since
09-15: 0 (manual or spawned); tasks with `CommitOnSettle = 'Never'` since 09-15: 1;
`Projects.CommitOnSettle` is NULL for every project row (inherit → global `true`).

`Report names % still uncommitted` warnings per day: 09-08 4, 09-09 12, 09-11 8, 09-12 3,
09-13 9, 09-14 3, **09-15 onward 0**.

Shared Succeeded settles since 09-15: 194 (Worktree 81). Eligible ones after go-live
(`CompletedAt >= 2026-09-17 07:18:56Z`, role not Commit/Merge/Mutation/Check/Distill/Diagnose,
no SourceLanding):

| Project | Tasks | Kind | Note header `git=` | Commit events |
|---|---|---|---|---|
| markdown-package | 10 (`d4a351f9`, `dcfc898d`, `62ab3611`, `4fe0436f`, `c1c4ef93`, `312049b2`, `15a67fc3`, `f992495d`, `eb8ed5fa`, `79b96667`) | ClaudeCode | `landed` (all) | 0 |
| Antiphon | 0 reached settlement | | | |

The 10 `markdown-package` rows are the only proof that the hook completes anywhere: a clean tree
returns `null` at `dirtyPaths.Length == 0` and the fallback renders `landed`. No Shared settle
with a dirty tree has occurred since go-live, so tier 1, tier 2 and the gate are unexercised in
production.

### The nine Antiphon tasks that could not settle

`antiphon-20260917.log`: 362 lines `[WRN] Failed to settle a delegated task for session
"<id>"` each immediately preceded by `[WRN] git log --all --reflog --fixed-strings --all-match
--grep=<task-guid> --grep=<settlement-digest> --format=%H timed out after "00:00:15" in
C:\src\Antiphon; child killed`, and each carrying
`ServiceUnavailableException: Settlement recovery requires one exact identity; inspect git
history.` at `TryCommitOnSettleAsync ... :line 3177` → `SettleAsync :752` →
`OnTurnEndLockedAsync :201` (line numbers of the checkout running at the time; `:3178`, `:752`,
`:205` at `3a62074e`). Per hour (+01:00): 09h 3, 10h 58, 11h 50, 12h 58, 13h 57, 14h 36, 15h 38,
16h 17, 17h 17, 20h 5, 21h 17, 22h 6 — one re-hand per minute per stuck task. No other exception
message occurs; 362 timeouts = 362 throws, all in `C:\src\Antiphon`.

| Task | Session | Role | Card / title | Created (Z) | Canceled (Z) | Throws | Stuck |
|---|---|---|---|---|---|---|---|
| `c9140e5d` | `93b80b28` | Plan | CARD-0545 plan nightly watchdog | 08:35 | 10:22 | 83 | 1 h 47 m |
| `b355c108` | `1a19cbba` | Investigate | CARD-0547 confirm gaps | 08:35 | 13:21 | 165 | 4 h 46 m |
| `117a4690` | `29129bb5` | Plan | CARD-0545 revise plan | 13:21 | 13:48 | 14 | 27 m |
| `4a2497b2` | `87095fd1` | TestDesign | CARD-0545 finalize G-545 | 13:49 | 14:59 | 38 | 1 h 10 m |
| `91773d94` | `93762b5c` | Plan | CARD-0547 plan recovery-consult | 13:50 | 15:24 | 12 | 1 h 34 m |
| `2b64d75a` | `49a4b333` | TestDesign | CARD-0547 finalize 21 guards | 15:24 | 16:16 | 22 | 52 m |
| `0d3ff398` | `8c2e7558` | Investigate | CARD-0552 investigate Mutation tracking | 19:37 | 19:54 | 5 | 17 m |
| `b5cd5ee0` | `7b87e2d5` | Plan | CARD-0552 plan companion-card | 19:54 | 20:21 | 17 | 27 m |
| `fb65210d` | `dee1a5b1` | Investigate | CARD-0550 bisect V26 | 20:11 | 21:20 | 6 | 1 h 09 m |

All nine: `Workspace = Shared`, `RepoPath = C:\src\Antiphon`, `Status = Canceled` (terminal
event detail `Canceled.`), no `SessionQueuedMessages` row with a `NoteHeader` (only check
interpreter digests, `INTERPRETER DOWN` / `SUPERSEDED`, and the caller's refinements "Your last
message was not recognized as a completion report"). The work itself reached master because the
delegates committed and pushed their own docs (`16f4268a`, `51c609a3`, `22f010a0`, `e3ebeab5`,
`6bda48d3`, `0d7765c1`, `77e080f3` in the main checkout reflog); only the settlement, note and
card transition were lost, and each was then re-dispatched. Nothing on the affected cards'
histories records why they were canceled; CARD-0547's own investigation
(`docs/investigations/2026-09-17-card-0547-commit-recovery-orphaned-and-audit-trailer-mismatch.md:21-35`)
had documented the swallow-and-re-hand loop as "pending until reachable again" without knowing
the search never completes in this checkout.

### Reproduction (2026-09-18, ~01:40 +01:00, CPU 64 %, 709 processes, one other git process)

| Checkout | Registered worktrees | Reflog files under `.git/logs` | `git log --all --reflog … --grep … --grep …` | Same without `--reflog` |
|---|---|---|---|---|
| `C:\src\Antiphon` (main) | 402 (`git worktree list` 403 lines; `prune --dry-run` 0) | 922 (5 541 lines) + 402 per-worktree `HEAD` logs (2 302 lines) | **74.4 s, 85.0 s** | 1.1 s |
| `C:\Antiphon\worktrees\card-task-e28d4c3d` | same store | same | **135.0 s** | 1.3 s |
| `C:\src\markdown-package` | 12 | 19 | 2.6 s | — |
| `C:\src\slides` | 1 | 4 | 0.35 s | — |

`git status --porcelain -z --untracked-files=all` in the main checkout: 0.17 s. Repository:
3 264 commits reachable from `--all --reflog`, 2 101 refs, 9 packs, git 2.50.1.windows.1. The
timeout is `GitSettings.TimeoutSeconds = 15` (`server/Application/Settings/GitSettings.cs:6`),
applied to every command by `GitWorkspaceService.RunAsync` (`:1048`, `:1069`). The search is
therefore 5-9x over budget in the only checkout where the Antiphon pipeline settles Shared tasks,
and within budget in the other two projects.

Why `--reflog` is that expensive on this checkout is not established here (the per-worktree
reflog count is the visible difference between the three checkouts, and the CPU was busy); the
measured differential is what matters for design.

## 4. The card-identifier mix-up, re-examined

Revision 33, CARD-0548 and CARD-0549 assert that tasks `d17aff7f/67a63a4f/c9a3d250/2c956bc5` etc.
were an unrelated bug fix misfiled under CARD-0527. Evidence against:

- The Code brief (`docs/investigations/2026-09-16-card-0544-evidence/task-rows.json:42,64`)
  reads "Build CARD-0527 S1-S6 per docs/superpowers/plans/2026-09-15-card-0527-commit-on-settle-plan.md".
- The repair docs are all about `TryCommitOnSettleAsync`'s recovery of *its own* gated commit
  (`docs/investigations/2026-09-15-card-0527-f8-f9-repair.md`, `…-f10-f12-…`, `…-f13-f14-…`,
  `2026-09-16-…-f15-f16-…`, `…-f17-f18-…`): a settle whose save fails after the commit must
  render `committed:` on re-hand and never double-commit (Code brief correction 3). "Recovery
  identity loss" is a defect class *inside* this card's feature, not another card.
- Every landed commit touches `AgentTaskReplyService.TryCommitOnSettleAsync`,
  `GatedCommitService`, `CommitOnSettle*`, `scripts/task-commit.ps1`, `project.ps1
  -CommitOnSettle`, `delegate.ps1 -NoCommit`, and the tests named `C527_*` /
  `CommitOnSettle*Tests` / `GatedCommit*Tests` (29 test files under `tests/Antiphon.Tests/Application/`).

So the card was correctly bound throughout; the correction itself was the mix-up (likely a
post-compaction orchestrator reading the repair-round titles without the plan). The card's real
state is: built, landed, live, **defective in production**, Mutation owed, and misrecorded.

## 5. Brief question 3: settlement hook point and the CARD-0552 "prior art"

- The settle hook point already exists and is in use: `SettleAsync` between `MergeBackAsync`
  (`:735-750`) and the CARD-0544 completion obligation (`:795-799`), at `:752`. The spawn shape
  is `CreateMergeTaskAsync`'s (same `AppDbContext`, child saved with the settlement by
  `PersistDeliverThenReleaseAsync`, child id rides in the note header).
- CARD-0552's land hook (`AgentTaskLandService.CompleteTerminalLockedAsync`,
  `server/Application/Services/AgentTaskLandService.cs:678`) and `PostLandVerificationCompanions`
  are **plan-only** (`docs/superpowers/plans/2026-09-17-card-0552-mutation-tracked-stage-plan.md`;
  `VerificationCardId` and `PostLandVerificationCompanion` occur in no `.cs` file). It is not
  prior art for this card; CARD-0527's `CreateCommitTaskAsync` is prior art for it.

## 6. Brief question 5: what the worktree-default policy changes

- Code default is unchanged: `AgentTaskService.ResolveWorkspace` (`AgentTaskService.cs:1527-1560`)
  returns `Shared` for every non-orchestrator request without an explicit workspace. CARD-0458
  is in Review with no code on any ref (no `DefaultWorkerWorkspace` symbol).
- The policy is the orchestrator memory `feedback-default-worktree-isolation` (2026-09-17
  20:43Z): pass `-Worktree` on every `delegate.ps1` dispatch. Its stated reason is shared-slot
  serialisation (CARD-0552 TestDesign queued behind CARD-0550 Investigate), not this defect; it
  was set two hours after the last stuck settle and is the reason the 09-18 log is quiet.
- Consequences for this card's shape:
  - Antiphon pipeline tasks now settle through `TryMergeBackAsync`, whose gated sweep commits
    the whole worktree (pathspec `null`) with the same `GatedCommitService`, then inspects the
    result with `FindCommitOperationAsync` — the same `--reflog` search. A dirty Worktree settle
    would commit, time out on inspection, throw `CommitInspectionPendingException`
    (`GatedCommitService.cs:195`, `InspectCommitAsync`), be caught by `TryMergeBackAsync`'s
    `catch (Exception)` and return `MergeResult.Failed "Committing the delegate's work failed"`,
    leaving a committed branch unmerged. Not observed: every Worktree settle since go-live had a
    clean tree (delegate-basics tells delegates to commit and push; log has zero `Committing the
    delegate's work failed` / `InspectionPending`).
  - Other boards still dispatch Shared (`markdown-package` 10 settles on 09-17, all ClaudeCode;
    `slides` none since 09-14). The card's motivating case (Custom-role Shared tasks on `slides`)
    is still Shared and still eligible; it has simply had no traffic since the feature went live.
  - Isolation and commit-on-settle remain coupled as the card said: a worktree cut from `HEAD`
    still sees none of a dirty shared checkout's untracked work.

## 7. Remaining uncertainties

- Whether the `--reflog` cost is the 402 per-worktree `HEAD` reflogs, the 922 ref reflogs, or
  git enumerating `.git/worktrees/*` on every reflog walk; the three-checkout differential is
  measured, the internal cause is not. It also depends on machine load (19 other git commands
  timed out on 09-17: `status`, `rev-parse`, `log -50`).
- Whether the orchestrator's nine cancellations were prompted by the stuck settles alone; the
  card histories carry no reason, and the caller refinements ("not recognized as a completion
  report") are consistent with it.
- The Worktree-path exposure (section 6) is reasoned from code, not reproduced.
- Test suite status at `3a62074e` was not re-run here; the last recorded state is 101 V/R
  passing with six inherited reds (`e2a49f3a`), and CARD-0547's round 3 at `aa02c398`.

## Not done, noted

Fix idea, one line: bound or replace the reflog-wide recovery search (record the gated commit's
SHA in the `CommitRecoveryStarted` row / trailer lookup on `--all` only, or give
`FindGatedCommitsAsync` its own timeout and a non-throwing "unknown" outcome) so a slow checkout
degrades to today's warning instead of an unsettleable task; then correct CARD-0548/0549 and
the card's revision-33 note, and dispatch the owed Mutation stage.
