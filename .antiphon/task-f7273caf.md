# Failed-first seed order, round 3

A regrouped plan keeps failed-first order. Chunk ids are not binding. The class is. `seed-plan-regrouped` is gone. Tip before this report: `e0fca48698a4e0e17c646b69e28ebfc353a6b579`. Red tests: `9b7fae60b070caf75efd0bdcfd2e1c344264dc22`. Base: `d92701adb882bb15606e1d5ccffcae459d066891`.

Did not restart AppHost or a runner. Top-up 52 is the restart gate the caller already set.

## Outcome

`Resolve-NightlyStoppedSeedOrder` still checks `sourceSha` (`^[a-f0-9]{40}$`, else `seed-candidate-missing` / `seed-candidate-malformed`) and `sourceRunId` (else `seed-run-missing`). Duplicate suite+chunk stays `seed-schema` inside `Assert-NightlyStoppedSeedContent`. The throw that compared class sets on a shared chunk id is deleted.

State follows the class. Seed-only chunks are ignored. A class the plan has never seen is never-run. Every plan chunk appears once.

Priority is the rule the code already used, not a separate three-way leading partition:

- 0 when `entry.priorFailure` is set. `Resolve-NightlyPriorFailures` sets that from seed `failedTests`, so any failed class marks every plan chunk that contains it. A class split across two chunks marks both. A chunk that mixes a failed class and a passed class is priority 0.
- 2 when every class on the chunk was seen in the seed, every historical chunk for those classes completed, and none of those classes failed.
- 1 otherwise: never seen, unfinished, or torn. A missing timestamp sorts before a known one, so a never-started chunk runs before a timestamped unfinished chunk.

Sort is priority, then ticks, then canonical ordinal. `RealPackerRegroupKeepsFailedFirst` pins the hand plan as `mixed,split-a,split-b,never,unfinished,passed-only`. `old-zeta` (seed-only) is absent. `mixed`, `split-a`, and `split-b` are `priorFailure`. `unfinished`, `never`, and `passed-only` are not.

## A-8

Deviation, sort unchanged. Failed chunks first matches "failed first". There is no per-lane resume after the last passed chunk. Interrupted chunks that carry a timestamp are priority 1 and run after every never-started chunk, then all-passed chunks (priority 2) run oldest first. `FailedFirstMapping` already pins never-started before timestamped unfinished.

## Red, then green

At `9b7fae60b070caf75efd0bdcfd2e1c344264dc22`, before the product edit:

- `DigestGuardLeavesSeedOrder` regroup reason `seed-plan-regrouped`, first chunk `all-group-1`. The rest of that case already passed, including the forged normal record (`previous-run-order`, Priority first) and the fallbacks that now carry prior-failure members.
- `RealPackerRegroupKeepsFailedFirst`: `seed-plan-regrouped/all-group-1,all-group-2,never-added`, `moved=True`, method delta 151, Beta `all-group-1` to `all-group-2`. The split/mixed half was already green because those chunk ids are not shared with the seed.

After `e0fca48698a4e0e17c646b69e28ebfc353a6b579`: `DigestGuardLeavesSeedOrder`, `RealPackerRegroupKeepsFailedFirst`, `FailedFirstMapping`, and `InvalidSeedFallsBack` pass. The regrouped plan orders `operator-seed-order` with `held-red` first and `priorFailure`. The packer witness orders `operator-seed-order` with Beta's new chunk first. Adding 151 methods to Alpha (1700 to 1851) moves Beta off `all-group-1` (Alpha+Beta, 900s) onto `all-group-2` (Beta+Gamma). `seed-only` is absent. `never-added` is present. A second call matches.

## Mutants

Each mutant was one exact replacement, then restored. Both runners ended `RESTORE clean`. `git diff` was empty at `e0fca48698a4e0e17c646b69e28ebfc353a6b579`.

| Mutant | Killed by |
|---|---|
| M1 drop both marker checks | DigestGuard: missing/foreign become `seed-schema` |
| M2 drop `(-not $normal) -and` | Forged record is `operator-seed-order` with Ordinary first, not `previous-run-order` with Priority first. `forgedMissing` is `operator-seed-order`, not `previous-run-plan-digest-missing`. The forged record has two chunks and two failed tests. |
| M3 delete the foreign-kind return | Expected `operator-seed-kind-foreign`; foreign falls through and `Assert` throws `seed-schema` |
| M4a delete blank/malformed `sourceSha` throws | `operator-seed-order` instead of `seed-candidate-missing` |
| M4b delete blank `sourceRunId` throw | `operator-seed-order` instead of `seed-run-missing` |
| M5 drop `-not $old.completed -or` | `FailedFirstMapping`: torn sorted into the passed partition |
| M6a catch sorts `priorFailure` first | `InvalidSeedFallsBack`: reason `seed-plan-unmapped`, canonical sequence, prior-failure members present |
| M6b digest fallback sorts `priorFailure` first | `scripts/test-nightly-failed-first.ps1 -Case ActualLoopFollowsSchedule -SmallLoopProbe`: 17 passed, 1 failed (`chainDigestMissing`). DigestGuard's tiny plan has no priority members, so it does not kill this one. |
| M6c foreign-kind return sorts `priorFailure` first | DigestGuard `Test-Original`: reason stays `operator-seed-kind-foreign`, order is not canonical |
| DM1 delete the malformed-digest branch | `chainIngress` 17/18: short/long/nonhex become `previous-run-plan-mismatch` |
| DM2 delete the blank-liveness throw | `chainIngress`: actual `previous-run-liveness-unknown`, expected `previous-run-liveness-missing` |
| DM3 delete the blank-schema throw | `chainIngress`: blank schema became `previous-run-schema`, expected `previous-run-schema-missing` |
| DM4 drop `liveness='closed'` from the producer | `chainPreviousOrder` 17/18 |
| DM5 delete the blank-sha throw | `chainIngress` 17/18: blank sha fails the hex check as `previous-run-schema` |

## Property and shape

Property seed `20261031`, 1200 cases, 0 failures, re-run on this tip. No chunk dropped or duplicated. Two calls match. Every fallback is the exact canonical sequence, including when prior-failure members exist. `operator-seed-order` only for an exact marker on a non-normal record. Zero `seed-plan-regrouped`.

| Reason | Count |
|---|---:|
| operator-seed-kind-foreign | 362 |
| operator-seed-order | 217 |
| previous-run-plan-digest-missing | 247 |
| seed-candidate-malformed | 118 |
| seed-candidate-missing | 126 |
| seed-plan-unmapped | 1 |
| seed-run-missing | 129 |

End-to-end probe, synthetic files only, 123 planned chunks (antiphon 105, e2e 10, agents-pty 5, markdown-pdf 3). Export 10.042s, chunks 123, failed 5, completed 89. Reason `operator-seed-order`. Lead `antiphon/all-group-37`, `antiphon/all-group-52`, `antiphon/all-group-79`, `e2e/all-split-3`, `agents-pty/all-group-4`. Count 123, unique 123. Moving antiphon `all-group-30`'s second class onto `all-group-31` keeps that lead and that count. Seed SHA256 of this run is `3e7d061c52cb4f2d17c7c06b2da4f92c764e8d838bcfd0e9744f37f631f9925d` (the export stamps the clock, so the digest is not stable across runs). The order is.

## Checkpoints

Plan copy `.antiphon/task-f7273caf-plan.md` changes only `bin-d63-*` to `bin-f727-seed1`, `bin-f727-runner1`, `bin-f727-pty1`, `bin-f727-agents1`. Import wrote `.antiphon/checkpoints.yaml` (6 rows). The derived-row-timeout warning (120 minutes over the 45-minute advisory) was printed. `request.json` is `rowTimeoutMinutes` 90, `totalTimeoutMinutes` 180, `serial` true, `expectedSourceSha` `e0fca48698a4e0e17c646b69e28ebfc353a6b579`.

Run `20261031-223359-90cc`. Tool build was its own leased build (`f727-ckpt-tool`, waited 0s, `UseAppHost=false`, `bin-f727-ckpt/`), then `dotnet exec`. The run was not wrapped in an outer slot. Source clean, `buildSource=verified`. `validate` printed `CHECKPOINT SOURCE VALID source=e0fca48698a4e0e17c646b69e28ebfc353a6b579 rows=6`. Verdict GREEN, exit 0. Wall 41m47s. Sequential-equivalent 32m21s. Builds 4. Max concurrent builds 1. Unlisted: none. Evidence: `.antiphon/checkpoints/20261031-223359-90cc/report.md`.

```
CHECKPOINT CP-1 commit=e0fca48698a4e0e17c646b69e28ebfc353a6b579 build=ok filter=/*/*/(RcStoppedRunSeedTests*)|(RcFailedFirstPlannerTests*)|(RcFailedFirstExecutionTests*)|(RcFailureHistoryTests*)|(RcFullWatchdogHarnessTests*)|(C1514LandingSplitBudgetTests*)|(RcRosterRepairTests*)|(ScriptHarnessProcessContractTests*)|(ProcessSpawnLimitTests*)|(SlowTestTripwireTests*)|(TestLaneCategoryGuardTests*)|(TestClassificationPolicyTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=130 passed=130 failed=0 skipped=0 trx=/work/worktrees/task-f7273caf/.antiphon/checkpoints/20261031-223359-90cc/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=e0fca48698a4e0e17c646b69e28ebfc353a6b579 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=1753.658s wall=1753.705s
CHECKPOINT CP-2 commit=e0fca48698a4e0e17c646b69e28ebfc353a6b579 build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogTests/CapFitsAdmittedPlannedCost executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f7273caf/.antiphon/checkpoints/20261031-223359-90cc/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=e0fca48698a4e0e17c646b69e28ebfc353a6b579 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=13.592s wall=13.638s
CHECKPOINT CP-3 commit=e0fca48698a4e0e17c646b69e28ebfc353a6b579 build=reused filter=/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/LedgerAndReceiptBytesAreUnchanged executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f7273caf/.antiphon/checkpoints/20261031-223359-90cc/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=e0fca48698a4e0e17c646b69e28ebfc353a6b579 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=107.583s wall=107.612s
CHECKPOINT CP-4 commit=e0fca48698a4e0e17c646b69e28ebfc353a6b579 build=ok filter=/*/*/(ProcessSpawnLimitTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-f7273caf/.antiphon/checkpoints/20261031-223359-90cc/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=e0fca48698a4e0e17c646b69e28ebfc353a6b579 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=9.279s wall=9.525s
CHECKPOINT CP-5 commit=e0fca48698a4e0e17c646b69e28ebfc353a6b579 build=ok filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-f7273caf/.antiphon/checkpoints/20261031-223359-90cc/rows/CP-5/run.trx slot=granted waited=45s dirty=0 source=e0fca48698a4e0e17c646b69e28ebfc353a6b579 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=45s executed=6.694s wall=51.823s
CHECKPOINT CP-6 commit=e0fca48698a4e0e17c646b69e28ebfc353a6b579 build=ok filter=/*/*/(ProcessSpawnLimitTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-f7273caf/.antiphon/checkpoints/20261031-223359-90cc/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=e0fca48698a4e0e17c646b69e28ebfc353a6b579 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=5.342s wall=5.402s
```

CP-1 is 130/130 (the new packer witness is the extra test; `RealPackerRegroupKeepsFailedFirst` and `DigestGuardLeavesSeedOrder` both Passed). Census cases `C1404_Group10MeasuredBudget`, `C1577_Group71MeasuredBudget`, `C1584_E2EExpandedBudget`, `C1410_MeasuredCostGroups`, and `G2865_Group28Group65MeasuredBudget` passed inside CP-1. `RcRosterRepairTests` is in that same filter. CP-2 and CP-3 reuse the CP-1 build. The tool deleted `bin-f727-seed1/`, `bin-f727-runner1/`, `bin-f727-pty1/`, and `bin-f727-agents1/`.

## Land, evidence, rc

`scripts/audit-land-verification-graph.ps1 -Check` only, after a leased Release build of `tools/Antiphon.LandGraphAudit` (`UseAppHost=false`, waited 75s). Approved, count 69, phase pass2, digest `E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806`. No `-Update`.

`scripts/check-evidence-diff.ps1 -BaseRef 4390c697e2a7255587264514260d9903fea8d719 -HeadRef e0fca48698a4e0e17c646b69e28ebfc353a6b579`: commits 5, entries 1, violations 0, exit 0. The one entry is the earlier report `.antiphon/task-91dd821a.md`.

`git fetch origin refs/heads/rc/2026-10-27` then `git merge-tree --write-tree origin/rc/2026-10-27 HEAD` at the code tip. RC is `e0d49d221513b467f8c853f47587e688e04255e2` and is an ancestor. Exit 0, tree `cb93679ae5e9e7bb8848a3a63c17ed5599245ce1`, equal to that commit's tree. No merge was made.

--- next stage ---
next: short Final review of D-1 only (real-packer regroup), then merge as rc top-up 52 with the tree-kill fix 0dbadd9ef
handoff: review this tip, then land onto rc/2026-10-27 as top-up 52
artifact: .antiphon/task-f7273caf.md
