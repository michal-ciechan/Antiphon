# CARD-1149/1150 S1 repair (task 88ebd5b8)

Outcome: repaired. D1 and D2 are in production. D3 needed tests only. Ordinary checkpoint run 20261007-203839-04be is green at c46fda83d8db21dc464744ab44dfe98a5f42d6c6 (24 rows, 316 executed, 316 passed, 0 failed, 0 skipped).

Branch: feat/card-task-88ebd5b8. Worktree: /work/worktrees/task-88ebd5b8. Original S1 Code owner (landing owner): 1ac80818-3942-42fe-b81a-96a6beb474b0. Reviewed base: 740474af223c674b1b62dbf5f2c9df30e429e1aa. Repair commit: c46fda83d8db21dc464744ab44dfe98a5f42d6c6 `fix(CARD-1149): keep attempted launches off the absent-launch hold`. This report is a later docs commit. The branch was not rebased.

origin/master: 3eeea31cc261c7265e81b1b05c8bc7b40079886e. `git merge-tree --write-tree origin/master HEAD` at the repair commit exited 0 (tree b7dfc5f45694232937f81f4c6263fe41e13b0d22).

## What changed

- `server/Application/Services/AgentTaskDispatcher.cs` (43 insertions, 3 deletions). Open-task query is `OrderBy(t => t.Id)`. `DecideAbsentLaunchAsync` takes `nativeAttempt` (true when `unreportedJsonlPath` is set). After the Working / `IsWorking` withhold, and before `ReadBriefCustodyAsync`, a native attempt returns `NotThisShape`. On a failed hold, `DiscardUncommittedHoldAsync` detaches Added entities that were not tracked before the hold, then reloads the victim task (detaches it if reload throws). Cancellation is rethrown. Other failures still return Withheld. `ChangeTracker.Clear()` is not used.
- `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentLaunchRepair.cs` (new). D1 four identities, D2 two-task same-sweep failure, D3 eligible / busy / crash receipts.
- `tests/Antiphon.Tests/Application/DelegationDispatchRecoveryBoundaryTests.AbsentLaunch.cs`. Optional seed task id, existing parent, and brief flag (default still seeds a brief). `OpenSweep` optionally registers reply service and bind-refusal recovery. Existing callers keep today's behavior.
- `docs/superpowers/plans/2026-10-07-card-1149-1150-test-design.md`. First Checkpoints table gains CP-39..CP-56, all After S1, Build `CP-1`.

`AgentTaskService.cs` blob d61381a230fc9b57cf0f97fa4a2bead2537127f0 and `TaskDeadlinePolicy.cs` blob 9fa3abc9185740ae6366762fc34e56ecc0f2c34f match the reviewed base. No migration. No assertion was weakened or deleted. `TryFailBootStallAsync` on the reviewed file (lines 3162–3275, the line before `BootStallLedgerKey`) SHA-256 59f5909b7e5b80ddad0bace8bea4279771072d541d909186e09940113c18b711. The repair diff's last hunk ends before that method.

D3 production is unchanged. The three receipt cases were already green on 740474af after the test clock was split (a shared frozen `FakeTimeProvider` hangs inside `SessionMessageQueueService` confirmation `Task.Delay`). Sweep clock starts five minutes behind wall time. Both harnesses use the system clock.

## Red proof

Every new case failed on 740474af and again under a method-scoped mutation, then the mutation was reverted.

| Case | On 740474af | Mutation | Red |
|---|---|---|---|
| D1 `C1149_Native_attempt_keeps_the_failure_path` (user, queued-command, legacy-file, pending-brief) | 4 failed / 4. Each expected Failed and was Blocked. 46s 444ms. Lease 9a5533c4-f0fa-40a5-b00f-d060732e5155 waited=0s. Build before it: lease 6b943996-efca-4a12-8bad-1d5241274961 waited=0s held=170s, 00:02:49.40, 0 errors. | Delete `if (nativeAttempt) return AbsentLaunchDecision.NotThisShape` after the Working guard. | 4 failed / 4, 44s 501ms, ROW_EXIT 2. Same Blocked result. Lease ea62e850-81e1-4a6c-91b1-e76174bc0f19 waited=0s held=157s. |
| D2 `C1149_Failed_hold_does_not_persist_on_a_later_save` | 1 failed / 1. Victim expected Dispatched and was Blocked. 45s 922ms. Same red lease as D1. | Delete `await DiscardUncommittedHoldAsync(task, addedBefore)`. | 1 failed / 1, 45s 243ms, ROW_EXIT 2. Victim Blocked. Build 00:01:47.44, 0 errors. Lease dc928909-6530-4674-8342-c1bbe163db0f waited=0s held=155s. |
| D3 `C1149_Caller_note_has_one_complete_user_prompt` (eligible, busy, crash) | Already green: 3 passed / 3, 50s 572ms. Lease 2c663144-9851-4b02-a970-98689ad761da waited=0s held=52s. The first attempt hung on the frozen clock and was killed; that hang is a test defect, not a production gap. Rebuild of the clock fix against the reviewed dispatcher: lease f78efed7-e064-4c72-80df-734982b64c96 waited=0s held=131s, 00:02:10.26. | Skip the `EnqueueBlockedParentNoteAsync` call inside the hold. | 3 failed / 3, 47s 754ms, ROW_EXIT 2. Each mode had 0 caller notes (eligible 7s 790ms, busy 568ms, crash 543ms). Build 00:00:43.82, 0 errors. Lease 3e192cea-cffc-444e-a4ab-f82fd687e74c waited=0s held=93s. |

Green on the fix before the checkpoint, same source that was committed: D1+D2+D3+budgets 11/11 in 54s 469ms (lease 5cac8802-7481-477d-90b4-b3eb4c3e52dc waited=0s held=56s). V-1..V-5 13/13 in 54s 802ms (lease 49494a4a-54e3-4039-b756-a48a9212e91d waited=0s held=56s). Fix build lease 74d90cc2-14b6-4605-8114-cb3abeabd7fb waited=0s held=109s, 00:01:47.70, 0 errors. Those runs are the red/mutation proofs and the pre-commit check. They are outside the checkpoint and are listed for that reason.

## Ordinary checkpoint

Run 20261007-203839-04be. `--rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-37,CP-39,CP-40,CP-41,CP-42,CP-43,CP-44,CP-45,CP-46,CP-47,CP-48,CP-49,CP-50,CP-51,CP-52,CP-53,CP-54,CP-55,CP-56 --serial --expected-source-sha c46fda83d8db21dc464744ab44dfe98a5f42d6c6 --total-timeout 90m`. Exit 0. Wall 21m23s. Sequential-equivalent 19m25s. Source clean, buildSource verified. The tool reported `unlisted: none` for that run. Report `builds: 4` counts unused plan build ids (`bin-c1150-recovery`, `bin-c1149-sboot`, `bin-c1149-s5`) as well as the one build that compiled.

One isolated build: `bin-c1149-s1`, UseAppHost=false (the tool adds it off Windows), build 116.5s, slot lease c5c09821-3abd-4b29-9ca8-dd6348f7c8d4 waited=0s maxcpucount=6, released after about 117s. Every later row reused it (`build=0s`) with its own slot, each waited=0s. Tool bootstrap (not a second lease around the run): lease 887b0c97-c397-4c91-be5b-071beb80a52e waited=0s held=5s, 00:00:04.09, 0 errors.

| CP | executed | passed | failed | skipped | host wall | slot wait |
|---|---:|---:|---:|---:|---:|---:|
| CP-1 | 1 | 1 | 0 | 0 | 49.3s | 0s |
| CP-2 | 1 | 1 | 0 | 0 | 49.9s | 0s |
| CP-3 | 5 | 5 | 0 | 0 | 47.0s | 0s |
| CP-4 | 3 | 3 | 0 | 0 | 49.4s | 0s |
| CP-5 | 3 | 3 | 0 | 0 | 46.2s | 0s |
| CP-37 | 3 | 3 | 0 | 0 | 50.1s | 0s |
| CP-39 | 4 | 4 | 0 | 0 | 48.7s | 0s |
| CP-40 | 1 | 1 | 0 | 0 | 47.3s | 0s |
| CP-41 | 3 | 3 | 0 | 0 | 53.9s | 0s |
| CP-42 | 24 | 24 | 0 | 0 | 64.8s | 0s |
| CP-43 | 3 | 3 | 0 | 0 | 55.8s | 0s |
| CP-44 | 25 | 25 | 0 | 0 | 56.3s | 0s |
| CP-45 | 85 | 85 | 0 | 0 | 79.6s | 0s |
| CP-46 | 8 | 8 | 0 | 0 | 59.1s | 0s |
| CP-47 | 67 | 67 | 0 | 0 | 55.1s | 0s |
| CP-48 | 7 | 7 | 0 | 0 | 68.4s | 0s |
| CP-49 | 15 | 15 | 0 | 0 | 63.4s | 0s |
| CP-50 | 25 | 25 | 0 | 0 | 53.2s | 0s |
| CP-51 | 17 | 17 | 0 | 0 | 3.8s | 0s |
| CP-52 | 3 | 3 | 0 | 0 | 50.6s | 0s |
| CP-53 | 1 | 1 | 0 | 0 | 49.0s | 0s |
| CP-54 | 3 | 3 | 0 | 0 | 7.0s | 0s |
| CP-55 | 3 | 3 | 0 | 0 | 42.7s | 0s |
| CP-56 | 6 | 6 | 0 | 0 | 4.0s | 0s |

CP-42's 24 includes V-1..V-5 (13), D1..D3 (8), and the three `C1149_C1150_Statement_budgets` arguments. Those budget assertions passed, so the pinned totals remain 18 / 18 / 4. `OrderBy` adds no SQL command. Recovery is unwired on the budget paths, so `nativeAttempt` stays false there.

Not run, and why: the whole Unit lane is outside this brief's ordinary scope (the brief's definition wins over the verification-profile footer) and AGENTS.md forbids it. CP-19's floor is 14 and only three budget arguments exist. CP-6..CP-18, CP-20..CP-36, and CP-38 belong to later slices or the boot prerequisite and would compile a second isolated build. Queue classes CP-31..CP-33 and the CP-36 converter are not in the brief's S1 parenthetical, so they were not added as repair rows.

## Evidence and platform

`scripts/check-evidence-diff.ps1 -BaseRef 3eeea31cc261c7265e81b1b05c8bc7b40079886e -HeadRef c46fda83d8db21dc464744ab44dfe98a5f42d6c6` exited 0: commits=4 entries=2 violations=0. The docs commit that adds this file is checked on the same base after it is committed; that result is in the caller message.

GET /api/runner-defaults returned revision 2, an empty kindDefaults array, and supported kinds Grok, ClaudeCode, Codex. GET /api/session-runners returned three runners, all available and dispatch-eligible. Occupied counts were 0, 0, and 4. Omit -Runner. Omit -Platform.

## Pending for Mutation

PC-1 through PC-19 in `docs/superpowers/plans/2026-10-07-card-1149-1150-dispatch-recovery-plan.md` stay pending for method-scoped SourceLanding Mutation. The D1..D3 red/revert cycles above are this Code round's proofs. They do not discharge those PCs.

Restart after land: server (dispatcher code). Runner: none. This task did not restart anything. Owner of the restart is the landing owner.

Generated checkpoint evidence stays gitignored at `.antiphon/checkpoints/20261007-203839-04be/`.
