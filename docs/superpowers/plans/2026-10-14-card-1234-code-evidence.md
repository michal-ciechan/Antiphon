# CARD-1234 Code evidence (task 47b93e01)

Plan: `docs/superpowers/plans/2026-10-14-card-1234-role-policy-invalid-values.md`. Base `42974ae069440f0aee9d7b446b1b11f0f1dbf105`.
S1 tests-only `a21113e4e4e8dde93ac8af3a1a10ea9a82ba09b4`; S2/S3 fix `8e87bd3f87f8a5e6ae41aa313ff21020d35c066a` (tested SHA). Manifest imported from the plan table with `timeouts.rowMinutes: 40`, `totalMinutes: 120`.

## Red-first, run 20261014-045239-68c2 at a21113e4 (exit 1, intended)

```
CHECKPOINT CP-2 commit=a21113e4e4e8dde93ac8af3a1a10ea9a82ba09b4 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/delegations/DelegateModal.test.tsx src/features/settings/ProjectSetupModal.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=120s dirty=0 source=a21113e4e4e8dde93ac8af3a1a10ea9a82ba09b4 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=a21113e4e4e8dde93ac8af3a1a10ea9a82ba09b4 build=ok filter=/*/*/(RolePolicyConfigurationValidationTests*)|(RolePolicyInvalidValuesCreateTests*)/* executed=29 passed=14 failed=15 skipped=0 trx=/work/worktrees/task-47b93e01/.antiphon/checkpoints/20261014-045239-68c2/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=a21113e4e4e8dde93ac8af3a1a10ea9a82ba09b4 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=30s build=655.0855101s startup=134.1207435s testsWall=10.6381224s teardown=22.1790086s hostWall=166.9378805s
```

CP-1: 15 failed exactly as designed (V-1 8 rows, V-3, V-5 5 rows, V-7); 14 controls passed. On master a policy or default level of 999 answered 400 (not 422); the cause was not traced (probably the response body cannot serialise level 999, which the string-only enum converter refuses). Policy kinds Raw/OpenCode/999 answered 422 `validation_failed`. CP-2: `Tests 4 failed | 31 passed (35)` - V-8 (master showed the row's High) and V-9 undefined/null (`default High tier.`) and 999 (`default 999 tier.`); `CLIENT TESTS EXIT CODE: 1`.

## Green at 8e87bd3f

Server rows, run 20261014-055353-ba95 (all four green; run exit 2 `refused: unsafe-tree` at post-row cleanup):

```
CHECKPOINT CP-3 commit=8e87bd3f87f8a5e6ae41aa313ff21020d35c066a build=ok filter=/*/*/(RolePolicyConfigurationValidationTests*)|(RolePolicyInvalidValuesCreateTests*)/* executed=29 passed=29 failed=0 skipped=0 trx=/work/worktrees/task-47b93e01/.antiphon/checkpoints/20261014-055353-ba95/rows/CP-3/run.trx slot=granted waited=150s dirty=0 source=8e87bd3f87f8a5e6ae41aa313ff21020d35c066a sourceState=clean buildSource=verified
PHASES CP-3 slotWait=150s build=1416.7441949s startup=175.289134s testsWall=14.419888s teardown=22.7987695s hostWall=212.5077952s
CHECKPOINT CP-4 commit=8e87bd3f87f8a5e6ae41aa313ff21020d35c066a build=reused filter=/*/*/(RolePolicyEndpointTests*)|(RolePolicyReadServiceTests*)|(AgentTaskAgentKindTests*)|(RolePolicyToolBundleDispatchTests*)/* executed=59 passed=59 failed=0 skipped=0 trx=/work/worktrees/task-47b93e01/.antiphon/checkpoints/20261014-055353-ba95/rows/CP-4/run.trx slot=granted waited=180s dirty=0 source=8e87bd3f87f8a5e6ae41aa313ff21020d35c066a sourceState=clean buildSource=verified
PHASES CP-4 slotWait=180s build=0s startup=159.2711603s testsWall=28.4372408s teardown=16.2855175s hostWall=203.9939187s
CHECKPOINT CP-5 commit=8e87bd3f87f8a5e6ae41aa313ff21020d35c066a build=reused filter=/*/*/(DelegationLeaseSettingsTests*)|(WorkerWorkspaceDefaultStorageTests*)|(CheckCompactionContinuationSettingsTests*)|(SpecialistFailurePolicyTests*)|(AgentTaskServiceIntegrationTests*)/* executed=171 passed=171 failed=0 skipped=0 trx=/work/worktrees/task-47b93e01/.antiphon/checkpoints/20261014-055353-ba95/rows/CP-5/run.trx slot=granted waited=315s dirty=0 source=8e87bd3f87f8a5e6ae41aa313ff21020d35c066a sourceState=clean buildSource=verified
PHASES CP-5 slotWait=315s build=0s startup=52.466255s testsWall=19.0853114s teardown=12.296656s hostWall=83.8482221s
CHECKPOINT CP-6 commit=8e87bd3f87f8a5e6ae41aa313ff21020d35c066a build=reused filter=/*/*/(AgentTaskConcurrencyLimitTests*)|(DispatchHoldVisibilityTests*)|(AgentTaskLandMonitoringTests*)|(AgentTaskLandTargetRaceTests*)|(RunnerSettlementSyncTests*)/* executed=122 passed=122 failed=0 skipped=0 trx=/work/worktrees/task-47b93e01/.antiphon/checkpoints/20261014-055353-ba95/rows/CP-6/run.trx slot=granted waited=300s dirty=0 source=8e87bd3f87f8a5e6ae41aa313ff21020d35c066a sourceState=clean buildSource=verified
PHASES CP-6 slotWait=300s build=0s startup=51.051576s testsWall=80.2677578s teardown=3.3650815s hostWall=134.6844156s
```

Client row CP-7, run 20261014-050910-3200 (its server rows refused `tampered` before testing, see below): `Test Files 2 passed (2)`, `Tests 35 passed (35)`, `CLIENT TESTS EXIT CODE: 0`.

```
CHECKPOINT CP-7 commit=8e87bd3f87f8a5e6ae41aa313ff21020d35c066a build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/delegations/DelegateModal.test.tsx src/features/settings/ProjectSetupModal.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=270s dirty=0 source=8e87bd3f87f8a5e6ae41aa313ff21020d35c066a sourceState=clean buildSource=notApplicable
```

## Refused runs (no test executed in the refused rows)

- 20261014-050903-ebca: a duplicate `start` I launched with a shell `&` survived; it raced 20261014-050910-3200 for `bin-c1234-fix` and refused `pre-existing-output`. Its CP-7 also passed 35/35.
- 20261014-050910-3200: build 0 errors, then `refused: tampered` on ignored paths rewritten by the concurrent client rows (`client/node_modules/.vite/vitest/.../results.json`, `logs/client-tests.log`, two esbuild binaries).
- 20261014-052653-8a09 (CP-3..CP-6 only, no client row): build 0 errors, then `refused: tampered` naming only the hard-linked pair `client/node_modules/esbuild/bin/esbuild` and `client/node_modules/@esbuild/linux-x64/bin/esbuild` (inode 29383551, nlink 2, mtime 04:55:20 and ctime 04:55:30, unchanged). Likely a false positive of the ignored-file hashing (CARD-1241 area). I removed my own `client/node_modules` and reran: 20261014-055353-ba95.
