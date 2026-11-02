# task-d340e01f report: CARD-1640 review D-1 fix (stale tracking ref)

Branch feat/card-task-d340e01f (runner mirror /work/worktrees/task-d340e01f), continued from 0c71d13c5268883ac2fa83e41f16dbd14d742654 (feat/card-task-db2df8ea). Landing owner: db2df8ea. Review 13fb02d0 (ff4cbb000) D-1. No plan/Checkpoints table; no checkpoint-tool run.

Commits:
- 403b11be775a578348e1b69efea23d4d0134d20d test: D-1 witnesses (red)
- 8daad6a2b9bfaf2fc4802f7cf2afdd8e0ad01216 fix (tested source)

## Fix (the review's design)
Invariant: a refs/remotes/origin/<b> tip is never trusted as a sibling's current tip unless proven current.
- Resolver (AgentTaskWorktreeBaseResolver): Auto mode reads the tracking ref for a missing local branch, but uses it only with proof: (a) this create just fetched it (new optional `fetchedBranches` arg), (b) it equals the tip this task's create preview fetched (new `CardWorktreeBaseSelection.ProvenRemoteTips`, serialized in WorktreeBasePreviewJson), or (c) it equals the row's unpaid sync-debt SourceSha. Otherwise the branch stays missing and the result is `Unknown/source_unreadable` (dispatch blocks via SyncUnverified); the warning names `origin/<b> @ <sha> is not proven current`.
- Pending-land branches never read the tracking ref: no local branch keeps WaitForLand/pending_land_unknown.
- Create (AgentTaskService.PrepareWorktreeBaseAsync): an unproven tracking ref leaves the branch unreadable, so create fetches it (refreshing the stale ref or failing to source_unreadable), then re-resolves trusting only the fetched branches; the trusted tips are recorded in the preview so the no-network dispatch re-resolve continues from them.
- AgentTaskDispatcher.cs not touched (it calls the 3-arg overload; the new parameter is optional).

## Tests changed on purpose
- T0442_V08 remote_only: was Continue from the tracking ref (the D-1 behaviour); now Unknown/source_unreadable with the "not proven current" warning.
- New: AgentTaskWorktreeBaseResolverTests.C1640_D1_a_tracking_only_tip_is_used_only_when_proven_current x6 (stale_at_base, stale_pending_land, unproven_current, preview_other_sha, preview_proven, debt_proven).
- New shape: AgentTaskWorktreeBaseCreateTests.C1640_create_...(stale_tracking); on_origin and stale_tracking now also re-resolve the created row as dispatch would (no network) and expect Continue at the fetched sha.

## Runs (Linux server2, build-slot, native output, UseAppHost=false, TEMP=/tmp)
| run | source | filter | result | slot lease / waited |
|---|---|---|---|---|
| build | 403b11be (red tests, old server) | Antiphon.Tests | ok | ee2aca3c / 0s |
| red | 403b11be | /*/*/*/(C1640_*)|(T0442_V08_*) | 15 total, 6 failed as intended: D1 stale_at_base + stale_pending_land (Decision=Target), unproven_current + preview_other_sha (Continue), V08 remote_only (Continue), create stale_tracking (Target) | 4e679bce / 0s |
| build | 8daad6a2 | Antiphon.Tests | ok | 5f351348 / 0s |
| green (empty) | 8daad6a2 | (Name)|(Name) class grammar | 0 tests (known grammar quirk); rerun below | f6bf9fbb / 60s |
| **green** | **8daad6a2** | six classes: AgentTaskWorktreeBaseResolverTests, AgentTaskWorktreeBaseCreateTests, AgentTaskWorktreeBaseSyncDebtTests, AgentTaskDispatchBaseGuardTests, DispatchSiblingInspectionTests, StartRefAvailabilityTests | **253/253, skipped 0** (246 + 7 new) | a18f32d8 / 30s |
| M1 build+run | 8daad6a2 + proof forced non-null (tracking trusted unconditionally) | C1640_*/T0442_V08_* | 5/15 red: D1 stale_at_base, unproven_current, preview_other_sha, V08 remote_only, create stale_tracking; restored | 4524f691 / 45s, 10d9e891 / 45s |
| M2 build+run | 8daad6a2 + create skips the fetch when a tracking ref exists | same | 1/15 red: create stale_tracking (worktree_base_unreadable); restored | 2adedccc / 0s, 980e814a / 0s |
| land audit build | 8daad6a2 | tools/Antiphon.LandGraphAudit Release bin-land-audit (deleted after) | ok | d8b36d1d / 0s |

TRX (ignored): tests/Antiphon.Tests/bin/Debug/net9.0/TestResults/d340-{red,green,m1,m2}.trx. The test bin was last built with M2 applied (source restored; a fresh build is needed before reusing it). C508 was green in the class run.
Unlisted runs: the empty green run (grammar) and the two mutant pre-checks the brief asked for. The whole Unit lane was not run (minimal testing per the brief: the six review classes).

## Gates
- Land graph -Check: approved, count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE (no -Update).
- check-evidence-diff 4b945e6f..8daad6a2: commits=3 entries=1 violations=0; 3a49b601..8daad6a2: commits=6 entries=1 violations=0.
- merge-tree onto origin/master f5283ad08: clean; onto origin/rc/2026-11-01 e114313d3: clean. Master-only.

## Residuals / notes
- A tracking tip that moved after create (another create fetched a newer tip) no longer equals the preview, so dispatch blocks with source_unreadable and the remedy, rather than guessing. Safe, possibly noisy.
- Ready (paid) sync debts are not used as proof (the inventory reads only unpaid debts); such a branch blocks at dispatch and is fetched at the next create.
- Advisories A-1..A-3 of review 13fb02d0 unchanged (notes only per brief); A-4 dispatcher residual still a follow-up after CARD-0824 S1.

## Pending for Mutation (none discharged; M1/M2 were Code pre-checks)
- PC-a..PC-f from task-db2df8ea (PC-b now means the proof-gated remote read).
- PC-g: proof gate (`proof is null` -> unproven) — D1 stale_at_base/unproven_current/preview_other_sha, V08 remote_only.
- PC-h: pending-land exclusion from the remote read (`pendingBranches`) — D1 stale_pending_land.
- PC-i: create passes the fetched set to the re-resolve — create on_origin/stale_tracking.
- PC-j: preview proof (PreviewedRemoteTips, ProvenRemoteTips in Result) — D1 preview_proven, create atDispatch Continue.
- PC-k: sync-debt proof — D1 debt_proven.
- PC-l: create fetches an unproven tracking branch — create stale_tracking.
