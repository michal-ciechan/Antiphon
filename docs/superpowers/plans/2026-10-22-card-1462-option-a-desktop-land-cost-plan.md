# CARD-1462 option A plan: cut the desktop cost of a land without a rule or custody change

Plan stage, 2026-10-22, task 5fe83b6d (runner mirror, branch `feat/card-task-5fe83b6d`, base
`e0080cfc609100be004c570ec1beab307c081973`; master `cbb297f8f` adds only the CARD-1462 investigation).
Source: [why lands run on the desktop](../../investigations/2026-10-22-why-lands-run-on-the-desktop.md)
sections 4-6 (option A, slices S1-S3), the
[230-git-command inventory](../../investigations/2026-10-21-card-1432-desktop-repo-maintenance.md) (G1-G8)
and the [server2 remote-prep latency](../../investigations/2026-10-21-server2-remote-prep-latency.md)
ranked fixes 6-8. Verification design is folded in, so the next stage is Code (slice 1).

Operator premise (2026-10-22): AppHost and server2 run the rc tip, never master; a master land is not
an activation. This plan changes no operating rule, no custody rule (R is still the only mutation
custody; a window is the existing CARD-1379 exception, applied to two more phases), no receipt
contract and no session behaviour. No session in this plan waits for input, so the CARD-1083 parking
checklist does not apply.

## Numbers this plan is measured against (investigation §3)

| Measure | Value |
|---|---|
| Median land (op total) | 312-349 s, about 5.5 min: rebase 3 s, verify 170-240 s, push 30-47 s, canonical 1 s, cleanup 36-77 s (max 134 s) |
| R held per land | 165-543 s (everything except the verification window) |
| Git processes per land | 232-250, gitSeconds 164-331 (0.7-1.3 s per process before the 2026-10-21 gc; not re-measured after it) |
| Serial lane | one global reader; 2026-10-20 07:00Z burst of 37 requests: median queue 2.3 h, max 3.7 h; 44 of 73 lands that day over 15 min end to end |
| Cuts held behind a land-held R | 2026-10-20: 41 tasks, median 305 s, max 13,130 s; 2026-10-21: 53 tasks, median 167 s |
| Cut under R | 13 s idle, up to 2 min 13 s during a land; six queued cuts drained in 2 min 43 s |

## Ground truth (what the card or investigation assumes, what the code does)

| # | Assumption | Code at `e0080cfc6` | Consequence |
|---|---|---|---|
| T-1 | CARD-1429/1430 and CARD-1432 S3/S5 are "already planned" and partly built | **None of CARD-1429, 1430, 1431 has a commit on master** (`git log origin/master --grep`); the cards are Backlog with 0 revisions. CARD-1432 S1 (`74deabb48`), S2 (`6cd9317a0`, repairs `ee62ec013`, `705026da8`) and S4 (`74deabb48`) are on master; **S3 (maintenance job) and S5 (G1-G6) are not**. CARD-1427/1428 are on master (`0fcc1c1b2`, `c00e3d1da`, `be14b8b0d`). | This plan carries 1429, 1430, 1432 S3 and S5, G7/G8, and 1431 as an option. |
| T-2 | The hosts run these fixes | **No.** `origin/rc/2026-10-20` tip `ceb6e3d6b` carries none of CARD-1427/1428/1432 (27 master commits, `rc..master`). The S4 histogram the F-4 measurement needs is not in production. | Pick the chain onto rc first (section "rc applicability"); every new slice is picked after it. |
| T-3 | Cleanup "runs inside R" and could simply be moved after R | Cleanup runs inside the protocol (`AgentTaskLandingProtocol.CleanupAsync`, `:406-454`) under R; `GuardedWorktreeRemoval.AuthorityAsync` (`GuardedWorktreeRemoval.cs:321`) refuses `repository_lease_required` unless `leases.Owns(request.Lease, common)`. The land's terminal commit and the CARD-1432 S2 ref retention (`AgentTaskLandService.cs:515-531`) also require R. | Cleanup outside R needs a custody object the removal accepts (D-1) and a terminal reacquire (D-3). |
| T-4 | A second "window" is a new custody rule | The verification window already exists: R released, L kept, a marker file; only a compatible dispatch cut of an unrelated delegate branch may take R, and it is fenced from the window's own journaled children (`RepositoryVerificationWindows.cs:48-100`, `RepositoryMutationLease.cs:60-68`, `RepositoryChildJournal.FirstFencingAsync` window/task exemption). `AllowsPreflightTurn` keeps the turn FIFO (`RepositoryLeaseWaiters.cs:86-94`). | Reuse it for the publication and cleanup phases (D-1, D-2). No new custody kind. |
| T-5 | Push must run under R | The push is a CAS against origin (`ObserveAsync` before and after, `AgentTaskLandingProtocol.cs:333-357`). Its only local writes are observation pins under the land's own `refs/antiphon/land/<task>/<op>/` prefix and the journal record. Nothing a compatible cut does can move the source branch (the compatibility rule excludes it) or the remote. | Push may run under a window (D-2). |
| T-6 | Canonical advance can also leave R | The cut resolves `InitialSha` with `rev-parse` and then runs `worktree add ... <baseRef name>` (`WorktreeManager.cs:111-130`), so a `master` moved between the two would record a wrong base. `C688_PushPrecedesCanonicalAdvance` pins push before canonical. | Canonical advance stays under R, after the push window and before the cleanup window (D-2). Two short R holds per land. |
| T-7 | The cut needs R through baseline capture | R is acquired at `AgentTaskDispatcher.cs:5457` and disposed at `:5930`/`:6103`; `CreateForTaskAsync` (`:5786`) and `ValidateVerificationAsync` (`:5812`) need it. `CaptureProgressBaselineAsync` (`:7634-7684`) runs `rev-parse`, `remote get-url`, `ls-remote`, `cat-file`, `check-ref-format`, `show-ref`, `update-ref` of `refs/antiphon/progress/<task>/baseline-*`: no shared mutation. Its remote observation takes its **own** R only when the advertised object is not local (`TaskProgressGit.cs:130-147`); today that acquire fails under the dispatcher's R and reports `repository_lease_busy`. | Release R after `:5812` (D-4). A baseline `update-ref` is a journaled child; an acquirer that lands inside it is refused once and re-picked. |
| T-8 | G1-G8 "halve the processes" | G1-G6 are designed in the CARD-1432 plan (D-8, D-9, S5) with tests named; none is built. G7 (paired `merge-base`, `AgentTaskLandSourceResolver.cs:614-617`) and G8 (seven `worktree list` runs over about 1,150 registrations, 2.3 s each on the desktop) are follow-ups F-3. | S4-S7 below carry G1-G8, keeping D-9: every check stays, in fewer processes. |
| T-9 | The S4 histogram tells Code what each slice saved | True only on a host that runs S4. The desktop runs rc (T-2). The profile also skips processes run while R is released (`LandingGit.ExecuteAsync:60-66` records only inside a scope). | S1a adds `leaseHeldSeconds` and `windowSeconds` to the profile line and keeps a read scope open under a window (D-5). |
| T-10 | CARD-1446 milestone 2 conflicts with these slices | Task 36786bbc (Code, server2, in flight at plan time) builds S4/S5 of CARD-1446: `AgentTaskLandService.AbandonCleanupAsync`, `LandingEnums.cs` (`Abandoned = 4`), `WorktreeResidueSweepService.cs`, `WorktreeResidueSettings`, `Program.cs:464`, a migration. | Slices touching `AgentTaskLandService.cs` (S1b, S2) and `Program.cs` (S9a) wait for it to land. The dispatcher, `LandingGit`, the protocol and the new files do not collide. |

## Decisions

- **D-1 Cleanup runs under a window, with R released and L kept.** After `ConfirmAsync`, the protocol
  advances the canonical ref and transitions to `CleanupStarted` under R as today, then returns
  `CleanupPending`. The service opens a window with the same marker and compatibility rule as
  verification (identity built from the operation: task, request, attempt, operation, candidate
  `VerifiedSourceSha`, land worktree, source worktree, source and target refs), releases R, signals the
  lease waiters, runs the removal with a `WindowCustody` (window id + live L handle) in
  `WorktreeRemovalRequest`, closes the window and reacquires R for the terminal commit and ref
  retention. `GuardedWorktreeRemoval.AuthorityAsync` accepts a window custody only when the window is
  live in this process, its marker's `SourcePath` is the worktree being removed and L is live;
  otherwise `repository_lease_required` as today. Every git child of the removal carries the window id
  so compatible cuts are not fenced by it. *Rejected:* widening `IRepositoryMutationLease.Owns` to
  windows (every other `Owns` caller would inherit the exception); deferring cleanup past the terminal
  event to the residue sweep (about 60 s less lane time per land, but every land becomes
  `Landed(cleanup pending)`: a receipt contract change the brief excludes; noted under "next"); releasing L
  before cleanup (the next land could start, and the window requires L).
- **D-2 Push runs under the same window kind; canonical advance stays under R.** Sequence per land
  after S2: R (admission, resolution, pins, rebase, prepare) → verification window → R (rechecks)
  → publication window (observe, push, re-observe, confirm) → R (canonical advance, `CleanupStarted`,
  about 2 s) → cleanup window → R (terminal commit, ref retention, about 1 s). Observation fetches and
  the push are journaled children tagged with the window id. *Rejected:* canonical advance under the
  window (T-6); one window spanning push and cleanup (needs T-6 fixed first; a later slice may merge
  them once the cut passes a SHA to `worktree add`).
- **D-3 Reacquire is the existing poll.** `LandRepositoryLeaseScope.ReacquireAsync` polls every 250 ms
  with no bound (`PreparedLandingVerification.cs:45-50`). Two more reacquires per land make the land
  race compatible cuts twice more. The profile's new `reacquireSeconds` (D-5) measures it; if a week of
  logs shows reacquire waits above 30 s, file a card for a bounded reacquire with a preflight-turn cap.
  Not changed here.
- **D-4 The cut releases R after `ValidateVerificationAsync`.** `AgentTaskDispatcher.DispatchOneAsync`
  disposes `repositoryLease` right after `:5812` and before the baseline capture; the `:5930` and
  `:6103` disposals stay and are idempotent (`OwnedLease.Disposed`). The claim transaction still commits
  after the capture; a capture failure still fails the claim for a Code task (`:5847-5852`). The
  baseline's own remote observation may now take R itself for a fetch when the advertised object is
  not local (T-7); that is the CARD-1082 D-3 path working as written. *Rejected:* moving the capture
  off the tick (CARD-0657 D-3 requires the identity before the claim commits).
- **D-5 Measure the hold, not the hope.** `LandingGitProfile` gains `leaseHeldSeconds` (sum of R
  intervals), `windowSeconds` and `reacquireSeconds`; the `Land git profile` line prints them. The lease
  scope opens a read scope while a window is open so processes under a window are counted and the
  registration cache stays stamp-validated (`LandingGit.RegistrationStampAsync`). Live reads are unchanged.
- **D-6 G1-G8 keep one check per boundary (CARD-1432 D-9).** Every refusal reason stays reachable and the
  batching tests assert the reason strings. G3's validator is a pure port checked against real
  `check-ref-format` in a 30-row oracle test.
- **D-7 CARD-1432 S3 is three Code tasks, not one.** The landed plan sized S3 at 2-3 h. S8 (service
  steps 1-3 under the lease, F-5 retained-operation rule), S9a (job, schedules, run entity, gc outside the
  lease) and S9b (thresholds, attention, ops route) each fit 30-60 min. The landed plan's D-6, D-7, D-10
  and D-11 stand unchanged.
- **D-8 Placement.** `GET /api/runner-defaults`: `globalRunnerId` is `server2` (operator 2026-09-26).
  `GET /api/session-runners`: server2 (linux, 10 seats, eligible), desktop (windows, 2 seats). Every
  checkpoint uses temp repositories or fakes and runs on Linux; no `-Runner`, no `-Platform`. Each
  checkpoint names its lane as server2 (default).
- **D-9 rc.** The hosts run rc; safe performance fixes go to `rc/2026-10-20` by cherry-pick after the
  prerequisite chain. Section "rc applicability" has the simulation and the pick order.

## Slices (ordered; each is one Code task)

Lanes that may run in parallel: **A** (dispatcher and new files), **B** (`LandingGit` and the
protocol's recheck helpers), **C** (the land service's lease flow; waits for CARD-1446 task 36786bbc).
Within a lane, slices are sequential. S1b/S2 (lane C) and S4 (lane B) both edit
`AgentTaskLandingProtocol.cs` in different regions (`:406-454` vs `:782-812`); run them one at a time.

| # | Card | Lane | What | Files | Tests (new) | Expect per land | Wait for | Code time |
|---|---|---|---|---|---|---|---|---|
| S1a | 1429 | A | Window custody for removal, profile hold accounting, read scope under a window (D-1, D-5) | `server/Application/Interfaces/IRepositoryMutationLease.cs` (`OpenCleanupWindowAsync`), `server/Infrastructure/Git/RepositoryMutationLease.cs`, `RepositoryVerificationWindows.cs` (identity from an operation; purpose field in the marker), `server/Infrastructure/Git/GuardedWorktreeRemoval.cs` (`AuthorityAsync`, `WindowCustody`), `server/Application/Dtos/LandingDtos.cs` (profile fields), `server/Application/Services/PreparedLandingVerification.cs` (scope accounting) | `tests/Antiphon.Tests/Infrastructure/LandCleanupWindowCustodyTests.cs`: `C1429_removal_accepts_a_live_window_custody_for_its_source_path` (red witness: `repository_lease_required`), `C1429_removal_refuses_a_closed_or_foreign_window_custody`, `C1429_profile_reports_lease_held_window_and_reacquire_seconds` | none yet (enables S1b) | nothing | 40 min |
| S1b | 1429 | C | Cleanup under the window (D-1, D-3) | `server/Application/Services/AgentTaskLandingProtocol.cs` (`CleanupAsync` split: canonical + `CleanupStarted` under R, removal under custody), `server/Application/Services/AgentTaskLandService.cs` (`RunLeasedAsync`: open window, release, remove, close, reacquire, terminal), child tagging for removal git | `tests/Antiphon.Tests/Application/LandCleanupWindowTests.cs` on `LandVerificationWindowHarness`: `C1429_cleanup_runs_with_R_released_and_a_compatible_cut_admitted` (red witness: the probe preflight acquire during removal returns null), `C1429_incompatible_acquirer_is_refused_during_cleanup`, `C1429_canonical_advance_and_cleanup_started_precede_the_release`, `C1429_terminal_commit_and_ref_retention_run_after_reacquire` | R held −36 to −77 s (−134 s max); lane time unchanged | 36786bbc landed | 50 min |
| S2 | 1429 | C | Push under the window (D-2) | `AgentTaskLandingProtocol.cs` (`Verified`/`PushStarted` arm returns `PublicationPending` after the post-verify rechecks; `PublishAsync(op, custody)` runs observe/push/observe/confirm), `AgentTaskLandService.cs` (publication window, reacquire for canonical), `LandingGit.cs` (`PushOwnedAsync`/`ObserveAsync` accept the child identity) | `LandCleanupWindowTests`: `C1429_push_runs_with_R_released` (red witness), `C1429_remote_changed_before_push_still_refuses_under_the_window`, `C1429_canonical_advance_runs_under_R_after_the_push` | R held −30 to −47 s | S1b | 40 min |
| S3 | 1430 | A | Release R before the baseline capture (D-4) | `server/Application/Services/AgentTaskDispatcher.cs` (`:5812-5845`) | `tests/Antiphon.Tests/Application/DispatcherLeaseSpanTests.cs` on `SiblingInspectionFixture` with a recording lease fake and a recording progress git: `C1430_lease_is_released_before_the_baseline_capture` (red witness: pin sequence precedes dispose), `C1430_source_landing_validation_runs_under_the_lease`, `C1430_baseline_failure_after_release_still_fails_a_code_claim` | per cut: R held 13 s → about 3 s idle, 2 min 13 s → about 10 s under a land; six queued cuts drain in about 20 s, not 2 min 43 s; a 90 s land yield admits about 25 cuts, not 6 | nothing | 35 min |
| S4 | 1432 S5 | B | G1 + G2: one `show-ref --verify` per recheck; pins by `update-ref --stdin create` | `AgentTaskLandingProtocol.cs` (`RecheckSourceIdentityAndPinsAsync`, `RecheckSourceAsync`), `LandingGit.cs` (`ReadRefsAsync`, `PinWithPrefixAsync`), `TaskProgressGit.PinBaselineAsync` (same 3 → 1) | `tests/Antiphon.Tests/Infrastructure/LandingGitBatchingTests.cs`: `C1432_recheck_reads_pins_and_branch_in_one_process` (red witness: histogram `show-ref --verify` > 1 per recheck), `C1432_missing_pin_reports_recovery_pin_changed`, `C1432_missing_branch_reports_source_changed`, `C1432_pin_create_refuses_existing_ref_with_other_sha` | −42 processes (−13 to −40 s) on both lane and R | nothing | 50 min |
| S5 | 1432 S5 | B | G5 + G4 + G7: fetch-only observation, endpoint bound once per scope and re-read before push, `rev-list --left-right --count` | `LandingGit.cs` (`ObserveWithPrefixAsync`, `ObserveSourceAsync`, `EndpointAsync`, `DestinationAsync`, `PushOwnedAsync`), `AgentTaskLandSourceResolver.ClassifyAsync` | `LandingGitBatchingTests`: `C1432_observe_one_round_trip_on_success` (red witness: remote round trips 2 → 1), `C1432_observe_missing_source_still_reports_missing`, `C1432_endpoint_reread_before_push`, `C1432_classify_uses_one_rev_list` | −20 processes and −6 remote round trips (−10 to −25 s) | S4 | 50 min |
| S6 | 1432 S5 | B | G3 + G6: pure ref-name validator; one `config --get-regexp` and one `ls-files --stage -v -z` per inspection | `server/Application/Services/GitRefName.cs` (new), `LandingGit.cs` (`check-ref-format` sites, inspection `:559-585`), `TaskProgressGit.cs` | `LandingGitBatchingTests`: `C1432_ref_name_validator_matches_git` (30 rows; red witness: skeleton returns false), `C1432_inspection_reads_config_once` | −16 processes (−5 to −15 s) | S5 | 40 min |
| S7 | 1432 F-3 (G8) | B | Removal-side registration reads: one live list, then file reads of `<common>/worktrees/<name>/{gitdir,HEAD,locked}` for the one registration; the two ticket lists (CARD-1384/1388) and the admission lists stay | `GuardedWorktreeRemoval.cs` (`RegistrationsAsync`, `UnregisterAsync`), `LandingGit.cs` (`RegistrationAsync(repository, path)`) | `tests/Antiphon.Tests/Infrastructure/LandingRegistrationReadTests.cs`: `C1432_removal_lists_registrations_once` (red witness: `worktree list` = 3 in the removal) , `C1432_single_registration_read_matches_worktree_list` | −2 to −4 lists of 2.3 s each on the desktop (−5 to −10 s) until CARD-1263 shrinks the registry | S6 | 45 min |
| S8 | 1432 S3 | A | Maintenance service steps 1-3 under the lease (stale-lock repair, ref sweep with the F-5 retained-operation rule, worktree prune), settings, waiter yield, busy skip | `server/Application/Settings/RepositoryMaintenanceSettings.cs` (+ validator), `server/Application/Services/RepositoryMaintenanceService.cs`, `RepositoryLeasePurposes.Maintenance` | `tests/Antiphon.Tests/Application/RepositoryMaintenanceTests.cs`: `C1432_stale_maintenance_lock_and_gc_pid_removed` (red witness), `C1432_ref_sweep_deletes_terminal_and_missing_tasks_only`, `C1432_sweep_keeps_latest_and_refused_operation_pins` (F-5), `C1432_locked_initializing_registration_without_directory_pruned`, `C1432_waiter_yields_before_step_two`, `C1432_busy_lease_skips` | keeps the post-gc per-process cost (for-each-ref 0.2 s, not 10.4 s); 0 new seconds per land today | nothing | 60 min |
| S9a | 1432 S3 | A | Hangfire job with `Cron`, `RefSweepCron`, `RetryCron`; `RepositoryMaintenanceRuns` entity + migration; step 4 (`maintenance run`, weekly gc) after the lease is released | `server/Infrastructure/Agents/RepositoryMaintenanceJob.cs`, `HangfireConfiguration.cs`, `Program.cs`, `server/Domain/Entities/RepositoryMaintenanceRun.cs`, migration | `RepositoryMaintenanceTests`: `C1432_gc_runs_after_lease_release` (red witness), `C1432_retry_runs_once_after_a_busy_night`, `C1432_refs_mode_runs_only_the_ref_sweep` | prevents the 2026-09-25 regression (auto gc silently off for four weeks) | S8; 36786bbc landed (`Program.cs`) | 60 min |
| S9b | 1432 S3 | A | Thresholds and attention items; `POST /api/agent-tasks/repository-maintenance/run?mode=full\|refs` | `RepositoryMaintenanceService.cs`, `server/Api/Endpoints/AgentTaskEndpoints.cs`, `docs/orchestration-loop.md` (maintenance paragraph), `docs/testing-and-build.md` | `RepositoryMaintenanceTests`: `C1432_thresholds_raise_attention` (red witness), `C1432_run_route_returns_the_run_id` | observability only | S9a | 40 min |
| S10 (optional) | 1431 | A | Dominant-class `held aged` ledger, `siblingInspection` queue reason, Information-level inspection timing | `AgentTaskDispatcher.cs` (ledger), `DispatchHoldDetails.cs`, `AgentTaskPipelineStatusService.cs`, `CardSiblingInspectionService.cs:105,124` | `tests/Antiphon.Tests/Application/DispatchHoldAttributionTests.cs`: `C1431_held_aged_names_the_dominant_class` (red witness), `C1431_inspection_pending_has_its_own_queue_reason` | measurement only; makes S3's effect visible in the pipeline view | S3 | 30 min |

Dependencies that are not in the table: S4-S7 all edit `LandingGit.cs` (one at a time). S2 needs S1b.
S9a edits `Program.cs`, which CARD-1446 S4a also edits.

## Expected savings (sum, with the assumptions stated)

Per-process cost after the gc is not measured (T-9). The table uses 0.3 s (post-gc, packed refs) to
1.0 s (the pre-gc measurement) per process.

| After | Processes per land | Lane time per land | R held per land | 37-request burst tail (2026-10-20 07:00Z, 3.7 h) | Cuts behind lands |
|---|---|---|---|---|---|
| Today | 232-250 | 5.5 min | 165-543 s | 3.7 h max, 2.3 h median | 41/day, median 305 s |
| S3 | same | same | same for lands; per cut 13 s → 3 s | same | each cut leaves R 10 s to 2 min sooner; the yield window admits 4× more cuts |
| S1a+S1b | same | same | −36 to −77 s (−134 s max) | same | the cleanup share of each hold (15-40 %) is open to cuts |
| S2 | same | same | a further −30 to −47 s; the land holds R for about 60-120 s total, in four short intervals | same | the push share is open to cuts |
| S4+S5+S6 | about 155-170 (−78) | −25 to −80 s (−8 to −24 %) | the same seconds off R | −15 to −50 min on the tail; 73 lands/day × 25-80 s = 30-97 lane-minutes/day of 568 | proportional |
| S7 | −2 to −4 lists | −5 to −10 s | the same off R | −3 to −6 min | proportional |
| S8-S9 | none today | keeps the gc'd cost; before the gc each process cost up to 10 s more (`for-each-ref`) | | prevents a return to the 10-20 numbers | |

What option A cannot touch: the 3-4 min verification build on the desktop and the CARD-1461 memory
floor (22-24 min walls). That is option B (section "next").

## Conflicts with in-flight Code tasks (at plan time, `GET /api/agent-tasks?projectId=...&status=...`)

| Task | What it touches | Effect on this plan |
|---|---|---|
| 36786bbc Code, CARD-1446 milestone 2 (S4a-S4c, S5) | `AgentTaskLandService.cs` (new `AbandonCleanupAsync`), `LandingEnums.cs`, `WorktreeResidueSweepService.cs`, `WorktreeResidueSettings`, `Program.cs:464`, `FileSystemEvidenceRetention.cs`, `WorktreeCleanupSettings.DefaultRetainedIgnored`, a migration | **S1b, S2 and S9a wait for it to land.** S1b's new terminal path must call nothing from `AbandonCleanupAsync`; the two are different lifecycle moments (abandon happens days later in the residue sweep). S1a, S3-S8 do not collide. |
| aecb6024, 26316601 Code, CARD-0167 | provider endpoint projection and persistence | none |
| 1490970e Plan, CARD-1461 option C | build-slot policy for land-verify | option B territory; S1-S10 do not touch `BuildSlotBroker` or `LandingVerifier` |
| 3d40a611, c64987ea Review; 77c15d47 Investigate (desktop) | CARD-1402, CARD-1409, RC qualification reds | none |

## rc applicability

`origin/rc/2026-10-20` (tip `ceb6e3d6b78471db6831258699c493b8eb2d619b`) carries none of the CARD-1432,
1427 and 1428 commits (T-2). A chain cherry-pick of the 27 `rc..master` commits that name those cards,
simulated in master order with `git merge-tree --write-tree --merge-base <c>^ <sim> <c>` and a dangling
`commit-tree` per step (no branch, no checkout), is **clean at every step**, plan docs included
(`4765fcb4f` … `be14b8b0d`, then `f4ba26383` CARD-1456 and `407f245e6` CARD-1446 S3, which touch the same
files). Two further master-only commits touch the land files and should be picked before any new slice:
`6ee09ad07` (CARD-1449/1447: ticketed resume, C475 lease probe, C688 source inventory) and `03686bed4`
(CARD-1384: reuse the post-reacquire worktree list). The rc line already has its own copies of
CARD-1379/1384/1436's earlier fixes under different SHAs (`20c38faa8`, `50e974a86`, `155af727d`,
`281c9349f`, `a80ec3d6c`, `f16284cff`).

| Slice | rc pick | Prerequisites on rc |
|---|---|---|
| S3, S8, S9a, S9b, S10 | yes, after the chain | the 1427/1428 commits (`AgentTaskDispatcher.cs`, `RepositoryLeaseWaiters.cs`); S9a needs `Program.cs` from CARD-1446 S1-S3 (`c3fe1fc26`) only if it lands first on master |
| S1a | yes | none beyond the chain (new interface member, new tests) |
| S1b, S2 | yes, after `6ee09ad07` and `03686bed4` | the S2 ref retention (`6cd9317a0`, `ee62ec013`) because the terminal path calls it; CARD-1446 S4c once landed |
| S4, S5, S6, S7 | yes | `74deabb48` (S4 histogram; the batching tests read it) and `6cd9317a0` (`UpdateRefsAsync`, `RunGitInputBytesAsync` reuse) |

Simulation script (run from the task mirror; it writes dangling objects only):

```bash
base=$(git rev-parse origin/rc/2026-10-20); cur=$base
for c in "$@"; do
  out=$(git merge-tree --write-tree --merge-base "$c^" "$cur" "$c" 2>&1); rc=$?
  tree=$(echo "$out" | head -1)
  [ $rc -eq 0 ] && echo "CLEAN $c" || echo "CONFL $c :: $(echo "$out" | grep ^CONFLICT | sed 's/.*in //' | sort -u | tr '\n' ' ')"
  cur=$(git commit-tree "$tree" -p "$cur" -m "sim $c")
done
```

Each Code task runs it with the chain plus its own commits and reports CLEAN/CONFL per commit. An rc
land is a Merge-role Worktree task that pushes `HEAD:refs/heads/rc/2026-10-20` (release-gates); it is
not this plan's work.

## Verification design

| V | Claim | Test | Red at |
|---|---|---|---|
| V-1 | A removal with a live window custody for its own source path proceeds; a closed or foreign window, or a window whose marker names another path, refuses `repository_lease_required` | `LandCleanupWindowCustodyTests.C1429_removal_accepts_a_live_window_custody_for_its_source_path`, `…refuses_a_closed_or_foreign_window_custody` | S1a test commit |
| V-2 | The profile line carries `leaseHeldSeconds`, `windowSeconds`, `reacquireSeconds`, and processes run under a window are counted | `LandCleanupWindowCustodyTests.C1429_profile_reports_lease_held_window_and_reacquire_seconds` | S1a test commit |
| V-3 | During the removal, R is free: a compatible preflight acquire succeeds and cuts; an untagged acquire and a second land are refused; canonical advance and `CleanupStarted` are committed before the release; the terminal event and ref retention run after the reacquire | `LandCleanupWindowTests.C1429_cleanup_runs_with_R_released_and_a_compatible_cut_admitted`, `…incompatible_acquirer_is_refused_during_cleanup`, `…canonical_advance_and_cleanup_started_precede_the_release`, `…terminal_commit_and_ref_retention_run_after_reacquire` | S1b test commit |
| V-4 | The push runs with R released; `remote_changed_before_push` still refuses; canonical advance runs under R after the push | `LandCleanupWindowTests.C1429_push_runs_with_R_released`, `…remote_changed_before_push_still_refuses_under_the_window`, `…canonical_advance_runs_under_R_after_the_push` | S2 test commit |
| V-5 | The dispatcher disposes R before the first baseline pin; SourceLanding validation still sees the lease; a Code-role capture failure after the release fails the claim | `DispatcherLeaseSpanTests.C1430_*` | S3 test commit |
| V-6 | One `show-ref --verify` per recheck; a missing pin gives `recovery_pin_changed`, a missing branch `source_changed`; a pin create on an existing ref with another SHA gives `recovery_ref_collision` | `LandingGitBatchingTests.C1432_recheck_*`, `…missing_pin_*`, `…missing_branch_*`, `…pin_create_*` | S4 test commit |
| V-7 | One remote round trip per successful observation; a missing source still reports `source_remote_missing`; the endpoint is re-read before push (`remote_configuration_changed`); classification runs one `rev-list` | `LandingGitBatchingTests.C1432_observe_*`, `…endpoint_reread_before_push`, `…classify_uses_one_rev_list` | S5 test commit |
| V-8 | The validator agrees with `check-ref-format` on 30 rows; an inspection reads config once and `ls-files` once | `LandingGitBatchingTests.C1432_ref_name_validator_matches_git`, `…inspection_reads_config_once` | S6 test commit |
| V-9 | The removal runs one live `worktree list`; the single-registration read matches the list | `LandingRegistrationReadTests.C1432_*` | S7 test commit |
| V-10 | Steps 1-3: stale locks repaired, terminal refs swept with the F-5 rule, stale registrations pruned, waiter yield, busy skip | `RepositoryMaintenanceTests.C1432_stale_*`, `…ref_sweep_*`, `…keeps_latest_*`, `…locked_initializing_*`, `…waiter_yields_*`, `…busy_lease_skips` | S8 test commit |
| V-11 | gc runs after the lease is released; the retry runs once after a busy night; refs mode runs only step 2 | `RepositoryMaintenanceTests.C1432_gc_*`, `…retry_*`, `…refs_mode_*` | S9a test commit |
| V-12 | Thresholds raise attention; the route returns a run id | `RepositoryMaintenanceTests.C1432_thresholds_*`, `…run_route_*` | S9b test commit |
| V-13 | No regression: verification windows, protocol harness, publication, cleanup safety, lease, land git, profile, ref retention, dispatcher cut and sibling scheduling classes stay green | the regression rows below | |

Red witness rule (git-common): each slice commits its failing test first and reports the stated failure
before the fix commit. A build error or zero tests is not red. Inherited reds are proven at the base
commit by method, never by assembly; the CARD-1432 S2 amendment's exclusion list (`C475_LandingKeepsLeaseThroughProtocol`,
`C688_LandNeverRunsGitInTheSourceWorktree`, eight `RR_V*` cases) predates `6ee09ad07` and `f4ba26383`,
so Code re-proves it at its base and excludes by method only what is still red there.
`C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance` self-skips off Windows and stays excluded
(skipped > 0 is `row_failed`).

### Checkpoints

One isolated build and one exact filter per row. Each Code task runs only its slice's rows
(`--rows`), as one checkpoint-tool run per committed slice group; red rows run at the slice's test
commit. Builds are never reused across `After` groups. Lane: server2 (default) for every row.
`Min` is a floor; confirm `executed=` in each CHECKPOINT line.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1a test | `tests/Antiphon.Tests -> bin-c1429-s1ared/` | s1a-red | `/*/Antiphon.Tests.Infrastructure/LandCleanupWindowCustodyTests/C1429_removal_accepts_a_live_window_custody_for_its_source_path` | V-1 witness | 1 executed, 1 failed: `repository_lease_required` | 1 | 6 |
| CP-2 | S1a | `tests/Antiphon.Tests -> bin-c1429-s1a/` | s1a | `/*/Antiphon.Tests.Infrastructure/(LandCleanupWindowCustodyTests*)\|(LandVerificationWindowTests*)\|(LandVerificationWindowNativeTests*)\|(RepositoryMutationLeaseTests*)\|(LandingGitProfileTests*)/*` | V-1, V-2, V-13 | all passed (3 new + 1 + 3 + 13 + 9 existing), 0 failed/skipped | 29 | 10 |
| CP-3 | S1b test | `tests/Antiphon.Tests -> bin-c1429-s1bred/` | s1b-red | `/*/Antiphon.Tests.Application/LandCleanupWindowTests/C1429_cleanup_runs_with_R_released_and_a_compatible_cut_admitted` | V-3 witness | 1 executed, 1 failed: probe acquire returned null during removal | 1 | 7 |
| CP-4 | S1b | `tests/Antiphon.Tests -> bin-c1429-s1b/` | s1b | `/*/Antiphon.Tests.Application/(LandCleanupWindowTests*)\|(LandVerificationLeaseWindowTests*)\|(LandingProtocolHarnessTests*)\|(AgentTaskLandCleanupSafetyTests*)\|(LandRefRetentionTests*)/*` | V-3, V-13 | all passed (4 new + 6 + 9 + 4 + 6 existing, with rows), 0 failed/skipped; exclude by method only cases proven red at base | 29 | 14 |
| CP-5 | S1b | CP-4 (-NoBuild) | s1b-publication | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C494_*)\|(C448_*)\|(C665_*)\|(C642_*)\|(C688_RebaseAndVerifyRunInTheLandWorktree*)\|(C688_PushPrecedesCanonicalAdvance*)\|(C688_DirtyCanonicalCheckoutLandsWithResidue*)\|(C688_TargetCheckedOutElsewhereIsResidueNotRefusal*)\|(C688_Repair_AlreadyCanonicalRecordsLocalIdentity*)\|(C688_LocalMasterBehindOriginRebasesOntoOrigin*)\|(C688_LocalMasterAheadOfOriginRefuses*)\|(C688_LandNeverRunsGitInTheSourceWorktree*)` | V-13 (CARD-0688 ordering, CARD-0448 pins) | all passed (43-44), 0 failed/skipped; drop `C688_LandNeverRunsGitInTheSourceWorktree` from the filter only if red at base | 43 | 12 |
| CP-6 | S2 test | `tests/Antiphon.Tests -> bin-c1429-s2red/` | s2-red | `/*/Antiphon.Tests.Application/LandCleanupWindowTests/C1429_push_runs_with_R_released` | V-4 witness | 1 executed, 1 failed: R held during push | 1 | 7 |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-c1429-s2/` | s2 | `/*/Antiphon.Tests.Application/(LandCleanupWindowTests*)\|(AgentTaskLandTargetRaceTests*)\|(AgentTaskLandRefusedRetryTests*)\|(LandingProtocolHarnessTests*)/*` | V-4, V-13 (CARD-0711 target race, refused retry) | all passed (7 new + 9 + 12 + 9 existing, with rows), 0 failed/skipped; exclude by method only cases proven red at base | 37 | 14 |
| CP-8 | S2 | CP-7 (-NoBuild) | s2-publication | `/*/Antiphon.Tests.Application/AgentTaskLandPublicationTests/(C494_*)\|(C448_*)\|(C665_*)\|(C642_*)\|(C688_RebaseAndVerifyRunInTheLandWorktree*)\|(C688_PushPrecedesCanonicalAdvance*)\|(C688_DirtyCanonicalCheckoutLandsWithResidue*)\|(C688_TargetCheckedOutElsewhereIsResidueNotRefusal*)\|(C688_Repair_AlreadyCanonicalRecordsLocalIdentity*)\|(C688_LocalMasterBehindOriginRebasesOntoOrigin*)\|(C688_LocalMasterAheadOfOriginRefuses*)\|(C688_LandNeverRunsGitInTheSourceWorktree*)` | V-13 (CARD-0688 ordering, CARD-0448 pins) | as CP-5 | 43 | 12 |
| CP-9 | S3 test | `tests/Antiphon.Tests -> bin-c1430-red/` | s3-red | `/*/Antiphon.Tests.Application/DispatcherLeaseSpanTests/C1430_lease_is_released_before_the_baseline_capture` | V-5 witness | 1 executed, 1 failed: baseline pin recorded before the lease dispose | 1 | 7 |
| CP-10 | S3 | `tests/Antiphon.Tests -> bin-c1430/` | s3 | `/*/Antiphon.Tests.Application/(DispatcherLeaseSpanTests*)\|(AgentTaskWorktreeBaseCreateTests*)\|(WorktreeStartRefDispatchTests*)\|(DispatcherSiblingInspectionSchedulingTests*)\|(TaskProgressGitTests*)\|(LeaseTurnScenarioTests*)/*` | V-5, V-13 | all passed (3 new + 5 + 10 + 6 + 8 + 1 existing, with rows), 0 failed/skipped | 33 | 14 |
| CP-11 | S4 test | `tests/Antiphon.Tests -> bin-c1432-s5ared/` | s4-red | `/*/Antiphon.Tests.Infrastructure/LandingGitBatchingTests/C1432_recheck_reads_pins_and_branch_in_one_process` | V-6 witness | 1 executed, 1 failed: `show-ref --verify` count > 1 per recheck | 1 | 6 |
| CP-12 | S4 | `tests/Antiphon.Tests -> bin-c1432-s5a/` | s4 | `/*/Antiphon.Tests.Infrastructure/(LandingGitBatchingTests*)\|(LandingGitTests*)\|(LandingGitProfileTests*)/*` | V-6, V-13 | all passed (4 new + 25 + 9 existing, with rows), 0 failed/skipped | 38 | 12 |
| CP-13 | S4 | CP-12 (-NoBuild) | s4-protocol | `/*/Antiphon.Tests.Application/(LandingProtocolGuardTests*)\|(LandingProtocolHarnessTests*)\|(LandRefRetentionTests*)\|(TaskProgressGitTests*)/*` | V-6 reason strings through the protocol | all passed (17 + 9 + 6 + 8, with rows), 0 failed/skipped; exclude by method only cases proven red at base | 40 | 12 |
| CP-14 | S5 test | `tests/Antiphon.Tests -> bin-c1432-s5bred/` | s5-red | `/*/Antiphon.Tests.Infrastructure/LandingGitBatchingTests/C1432_observe_one_round_trip_on_success` | V-7 witness | 1 executed, 1 failed: 2 remote round trips | 1 | 6 |
| CP-15 | S5 | `tests/Antiphon.Tests -> bin-c1432-s5b/` | s5 | `/*/Antiphon.Tests.Infrastructure/(LandingGitBatchingTests*)\|(LandingGitTests*)\|(LandingGitProfileTests*)/*` | V-7, V-13 | all passed (8 new + 25 + 9 existing, with rows), 0 failed/skipped | 42 | 12 |
| CP-16 | S5 | CP-15 (-NoBuild) | s5-resolver | `/*/Antiphon.Tests.Application/(AgentTaskLandSourceFreshnessTests*)\|(AgentTaskLandRemoteSourceRetentionATests*)\|(AgentTaskLandRemoteSourceRetentionBTests*)\|(LandingProtocolGuardTests*)/*` | V-7 (resolver and remote retention reasons) | all passed, 0 failed/skipped; exclude by method only cases proven red at base | 20 | 12 |
| CP-17 | S6 test | `tests/Antiphon.Tests -> bin-c1432-s5cred/` | s6-red | `/*/Antiphon.Tests.Infrastructure/LandingGitBatchingTests/C1432_ref_name_validator_matches_git` | V-8 witness | 30 executed, 30 failed: skeleton returns false | 30 | 6 |
| CP-18 | S6 | `tests/Antiphon.Tests -> bin-c1432-s5c/` | s6 | `/*/Antiphon.Tests.Infrastructure/(LandingGitBatchingTests*)\|(LandingGitTests*)\|(LandingGitProfileTests*)/*` | V-8, V-13 | all passed (10 new + 30 rows + 25 + 9 existing), 0 failed/skipped | 72 | 12 |
| CP-19 | S7 test | `tests/Antiphon.Tests -> bin-c1432-g8red/` | s7-red | `/*/Antiphon.Tests.Infrastructure/LandingRegistrationReadTests/C1432_removal_lists_registrations_once` | V-9 witness | 1 executed, 1 failed: `worktree list` = 3 | 1 | 6 |
| CP-20 | S7 | `tests/Antiphon.Tests -> bin-c1432-g8/` | s7 | `/*/Antiphon.Tests.Infrastructure/(LandingRegistrationReadTests*)\|(LandingGitTests*)\|(LandingRegistrationFreshnessTests*)\|(LandingRemovalControlTests*)/*` | V-9, V-13 | all passed, 0 failed/skipped | 30 | 12 |
| CP-21 | S7 | CP-20 (-NoBuild) | s7-removal | `/*/Antiphon.Tests.Application/(AgentTaskLandRemovalMatrixTests*)\|(AgentTaskLandCleanupSafetyTests*)\|(CardWorktreeCleanupRecoveryTests*)/*` | V-13 (removal matrix) | all passed (3 + 4 + 6, with 76 rows), 0 failed/skipped | 13 | 12 |
| CP-22 | S8 test | `tests/Antiphon.Tests -> bin-c1432-s3ared/` | s8-red | `/*/Antiphon.Tests.Application/RepositoryMaintenanceTests/C1432_stale_maintenance_lock_and_gc_pid_removed` | V-10 witness | 1 executed, 1 failed: `maintenance.lock` still present | 1 | 6 |
| CP-23 | S8 | `tests/Antiphon.Tests -> bin-c1432-s3a/` | s8 | `/*/Antiphon.Tests.Application/(RepositoryMaintenanceTests*)\|(LandRefRetentionTests*)/*` | V-10, V-13 | all passed (6 new + 6 existing, with rows), 0 failed/skipped | 12 | 10 |
| CP-24 | S9a test | `tests/Antiphon.Tests -> bin-c1432-s3bred/` | s9a-red | `/*/Antiphon.Tests.Application/RepositoryMaintenanceTests/C1432_gc_runs_after_lease_release` | V-11 witness | 1 executed, 1 failed: `maintenance run` recorded before the release | 1 | 6 |
| CP-25 | S9a | `tests/Antiphon.Tests -> bin-c1432-s3b/` | s9a | `/*/Antiphon.Tests.Application/RepositoryMaintenanceTests/*` | V-10, V-11 | all passed (9), 0 failed/skipped | 9 | 9 |
| CP-26 | S9b test | `tests/Antiphon.Tests -> bin-c1432-s3cred/` | s9b-red | `/*/Antiphon.Tests.Application/RepositoryMaintenanceTests/C1432_thresholds_raise_attention` | V-12 witness | 1 executed, 1 failed: no attention item | 1 | 6 |
| CP-27 | S9b | `tests/Antiphon.Tests -> bin-c1432-s3c/` | s9b | `/*/Antiphon.Tests.Application/RepositoryMaintenanceTests/*` | V-10, V-11, V-12 | all passed (11), 0 failed/skipped | 11 | 9 |
| CP-28 | S10 test | `tests/Antiphon.Tests -> bin-c1431-red/` | s10-red | `/*/Antiphon.Tests.Application/DispatchHoldAttributionTests/C1431_held_aged_names_the_dominant_class` | S10 witness | 1 executed, 1 failed: latest sentence reported | 1 | 6 |
| CP-29 | S10 | `tests/Antiphon.Tests -> bin-c1431/` | s10 | `/*/Antiphon.Tests.Application/(DispatchHoldAttributionTests*)\|(DispatcherSiblingInspectionSchedulingTests*)/*` | S10, V-13 | all passed (2 new + 6 existing, with rows), 0 failed/skipped | 8 | 12 |

Cost: 29 rows, 20 builds, about 285 estimated minutes on server2 across eleven Code tasks. Anything
beyond a slice's rows needs a stated reason.

### Measurement after each land (orchestrator, not a checkpoint)

After S1a is on the running host, read `Land git profile` lines from the desktop server log:
`leaseHeldSeconds` is the number S1b, S2 and S3 move; `processes` and `histogram` (CARD-1432 S4, once
the chain is on rc) are the numbers S4-S7 move. One week after S4-S6, re-run the investigation's §3
queries (landings by phase, lane minutes, cuts held) and compare with the "Today" row above. Only
then decide option B.

## Risks

| Risk | Mitigation |
|---|---|
| A compatible cut running during cleanup changes the registration set the removal compares | The compatibility rule excludes the land worktree, the source worktree and any nesting path; the removal looks up its own path and nested paths only (`GuardedWorktreeRemoval.cs:104-112`). V-3 has a cut registered during the removal. |
| A removal child fences compatible cuts | Children carry the window id, as the verifier's do (`FirstFencingAsync` exemption). V-3 asserts the cut is admitted while a child is live. |
| More reacquires per land starve the land behind a stream of cuts | D-3: measured by `reacquireSeconds`; the preflight turn is FIFO and capped at 90 s per waiter; each cut leaves R in about 3 s after S3. A bounded reacquire is a follow-up card if a week of logs shows waits above 30 s. |
| Releasing R before the baseline lets a land start before the claim commits | The land's writer census is the DB; the uncommitted claim is invisible, so the land sees the same world it sees today when the tick runs 1 s later. The cut's worktree and branch exist and are visible to `LiveRegistrationsAsync`; nothing in a land writes them. V-5 covers the SourceLanding case, which keeps the lease. |
| G5 drops the `ls-remote` consistency check | The pin is the observation; `CommitAsync(pin)` and the ancestry check stay (CARD-1432 risk table). |
| The validator differs from git | 30-row oracle against real `check-ref-format`; inputs are GUIDs and fixed suffixes. |
| The ref sweep deletes a pin a retry needs | F-5: the sweep applies `LandRefRetention.IsDeletable` with `RetainedOperationsAsync` (latest and Refused operations keep their pins); old-value checks on every delete. |
| S1b/S2 land after CARD-1446 S4c and the two terminal paths diverge | S1b's terminal path keeps `Cleanup=Refused` semantics byte for byte; `AbandonCleanupAsync` is a later, separate transition. CP-4 includes `LandRefRetentionTests`; Code adds `WorktreeLandingCleanupRetryTests/C1446*` to CP-4's filter if S4c has landed. |

## Not doing, and next (option B, kept out of this plan)

- **Deferring cleanup past the terminal event** (about 60 s off every land's lane time, 37 min off a
  37-land burst) is the one change in this area with a lane-time payoff beyond G1-G8, and it changes the
  receipt contract (`Landed` vs `LandedWithResidue`, CARD-0641/0665). Operator decision, not planned.
- **Option B (verify on server2)** needs, from investigation §6: (1) whether a Linux `dotnet build` is
  acceptable land verification with Windows coverage left to the RC qualification; (2) the build-slot
  policy for `land-verify` (CARD-1461; Plan task 1490970e is designing option C of that card); (3) an
  operator-owned `Landing:VerificationRunner` setting and the desktop fallback rule; (4) whether the
  desktop may push the prepared SHA to a temporary `refs/antiphon/land/...` ref on origin for the runner
  to fetch. None of these is needed for S1-S10.
- **Option C (server2 land lane)** needs the local-`master` custody and the CARD-0604 D-15 decisions. Not planned.
- **CARD-1263** (terminal worktree directories, 1,134 of them) is in Review; when it removes them, S7's
  saving shrinks to a few hundred milliseconds and S7 may be skipped.
- **CARD-1432 F-1/F-2** (runner git guard, stale transient refs) stay follow-ups.

## Checklist (sessions that wait for input)

None. No slice adds or changes a session that waits for input; the dispatcher hold and the land queue
positions keep their CARD-0672/1427 semantics. With parking disabled there is no automatic release
deadline (CARD-1083); nothing here depends on one.
