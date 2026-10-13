# CARD-1210 S1 verification continuation — Code 48fbe811

Linux CP-5/6 passed 8/8 with strict clean-source qualification. The predecessor's aggregate refusal was caused only by the unexecuted CP-5/6 rows, not a source/provenance mismatch. No implementation repair was needed. Windows CP-14/15/16 remain pending for separate desktop Review; this report does not claim all-platform Final completion.

## Source and ownership

- Landing owner under this continuation brief: Code `48fbe811-6df9-4abb-93fe-803cc1459779`. Predecessor implementation task: `604cc024-353a-46d8-870d-e4d5a00b4695`; the continuation supersedes its landing-owner statement.
- Branch: `feat/card-task-48fbe811`; runner worktree: `/work/worktrees/task-48fbe811`; assigned desktop counterpart: `C:\Antiphon\worktrees\card-task-48fbe811` (not reachable here).
- Recorded task base and actual tested committed HEAD: `efee2c4057596a15a5ef0fdf5f8eca80f9a1472d`. The only continuation deliverable committed after execution is this report. Its final publication SHA is in the completion message; receipts remain attributed to the tested SHA.
- Plan: `docs/superpowers/plans/2026-10-13-card-1210-windows-unit-lane-plan.md`; frozen checkpoint/test design: `docs/superpowers/plans/2026-10-13-card-1210-test-design.md`.
- Scope is S1 CP-5/6 only, as expressly commissioned. No Unit lane, full assembly, other slice, Windows execution, deliberate mutation, operational restart, deployment or board write was performed. Restart: none.

## Ordinary evidence

| CP | Results | Provenance / disposition |
|---|---:|---|
| CP-1 | 39 passed, 0 failed/skipped | Inherited, tested `229bc00ee7a6335c4fc8c9857f54ce82caff75d7` |
| CP-2 | 18 passed, 0 failed/skipped | Inherited, same SHA; seven methods, exactly 12 Version argument results |
| CP-3 | 11 passed, 0 failed/skipped | Inherited, same SHA |
| CP-4 | 4 passed, 0 failed/skipped | Inherited, same SHA; prerequisites/healthy heal assertions reached |
| CP-5 | 4 passed, 0 failed/skipped | Fresh, strict certificate valid at `efee2c4057596a15a5ef0fdf5f8eca80f9a1472d` |
| CP-6 | 4 passed, 0 failed/skipped | Fresh, same strict certificate |
| CP-14 | Not run | Pending Windows desktop Review; 5 outer results, exact child skip roster |
| CP-15 | Not run | Pending Windows desktop Review; 4 portable results, zero skips |
| CP-16 | Not run | Pending Windows desktop Review; 6 outer results and both child audits |

Run `20261013-072345-60bf` ended GREEN exit 0 after 7m33s. Each selected row had its own isolated build, `UseAppHost=false`, forward-slash trailing OutputPath, exact frozen filter, and granted host build slot. The printed `builds: 25` counts definitions in the imported full table: only CP-5/6's two builds executed. One explicitly budgeted, unlisted setup build bootstrapped the checkpoint tool into `bin-c1210-tool/`: slot=granted waited=75s maxcpucount=6, zero errors, one pre-existing CS8602 warning. No other unlisted build/test driver ran. There were no red rows, repetitions, retries, timeout changes or weakened assertions in this continuation. Owner checks did not return exit 7; the brief's three-retry contingency was unused.

Fresh raw TRX was joined by testId to exactly one definition and the expected namespace/class/method, requiring exactly one Passed result for every intended method and counters 4 total/executed/passed, 0 failed/notExecuted for each row:

- CP-5 / `Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests`: `C1210_Guard_matches_native_platform`, `C1210_Native_linux_entries_guard_before_work`, `C1210_Roster_closure_matches_source_gates`, `C1210_Audit_rejects_empty_or_incomplete_outcomes`.
- CP-6 / `Antiphon.Tests.Scripts.C1008PlatformContractTests`: `C1050_Guard_matches_native_platform`, `C1050_Remote_entries_guard_before_work`, `C1050_Rolling_entries_guard_before_work`, `C1050_Audit_rejects_empty_or_incomplete_outcomes`.

Both output build stamps matched the repository/project/output paths, `UseAppHost=false`, tested SHA and fingerprint `ae162f8d1d3619b8ed144781d2130a0b7616052650838ecbbf53a894478c1c73`; sourceState=clean. Both the tool validator and `scripts/validate-checkpoint-receipt.ps1` independently returned exit 0, `CHECKPOINT SOURCE VALID source=efee2c4057596a15a5ef0fdf5f8eca80f9a1472d rows=2`. Raw evidence remains unedited/ignored at `/work/worktrees/task-48fbe811/.antiphon/checkpoints/20261013-072345-60bf/`.

Historical CP-1–4 TRX was also inspected: 39/18/11/4 Passed results, 39/7/11/4 distinct identities, and no missing/duplicate testId definitions. These historical facts are not relabelled as execution at the continuation SHA.

## Why the old aggregate was refused

Original report: `/work/worktrees/task-604cc024/.antiphon/checkpoints/20261013-033923-f946/report.json`.

Its source start/end are both known, clean, dirtyFiles=0, SHA `229bc00ee7a6335c4fc8c9857f54ce82caff75d7`, fingerprint `d40e3de38110fd209adf6f6a6704c76b2d4668a26dcf804e37e96d10ec13afdc`. CP-1–4 have matching start/end observations and buildSource=verified. CP-5 is owner-unverified with unknown observations/build provenance; CP-6 is owner-unverified with clean observations but buildSource=unknown. Neither row built or tested.

`CheckpointApp.cs` computes aggregate buildSource=verified only when every selected test row is verified; the missing rows therefore make the heading unknown. `ReportValidator.Validate` rejects that heading before considering a selected row subset. Revalidation of CP-1–4 with the tool returned exit 2 `report_source_ineligible`; the PowerShell validator returned exit 2 `row_heading_disagreement` because verified rows differ from the unknown heading. These are consistent fail-closed consequences of missing CP-5/6, not SHA/dirty/fingerprint mismatches. No validator or evidence repair is appropriate.

The original aggregate remains invalid. The new two-row report is valid for CP-5/6 at its actual source. No single combined six-row certificate exists: the tool correctly forbids merging unknown reports or differing SHAs. Review must establish eligibility for its own exact reviewed HEAD rather than changing old receipts or treating cross-SHA arithmetic as a certificate.

The predecessor report retains its ordinary fail-first CP-5 receipts (missing reason, first statement, roster closure) and CP-4 prerequisite refusal assertions. This continuation adds no tests and performs no deliberate positive controls.

## Every V/R ID

| ID | Actual outcome |
|---|---|
| V-1 | Linux original behaviors: inherited 72 passes; new exact-reason/first-statement/source-shape proof passes. Native Windows behavior/portable proof pending CP-14/15/16. |
| V-2 | Linux closure and complete synthetic audit/launch/stamp negative vectors pass CP-5. Native Windows child receipt pending CP-14; S3 runtime/storage extension outside S1. |
| V-3 | Linux unchanged C1050 reason, entry and negative-audit checks pass CP-6. Windows child compatibility pending CP-16. |
| V-4 | Outside S1, deferred S2; not passed here. |
| V-5 | Outside S1, deferred S2; not passed here. |
| V-6 | Outside S1, deferred S3; not passed here. |
| V-7 | Outside S1, deferred S4; not passed here. |
| V-8 | Outside S1, deferred S4; not passed here. |
| V-9 | Outside S1, deferred S5; not passed here. |
| V-10 | Outside S1, deferred S6; not passed here. |
| R-1 | Outside S1, deferred S5; not passed here. |
| R-2 | Pending CP-16 native Windows child audits. |
| R-3 | Outside S1, deferred S6; not passed here. |
| R-4 | Outside S1, deferred S6; not passed here. |
| R-5 | Outside S1, deferred S3/S4; not passed here. |

Round remains Final with the brief's explicit continuation selection, not an Interim round. The commissioned Linux continuation is complete; all-platform S1 ordinary completion is withheld until Windows Review. No deferred ID is reported passed. There is no additional commissioned manual acceptance. The predecessor recorded successful native `/state` and unshare prerequisite inspection. No product delivery/landing/lease/persistence behavior was changed or claimed by these static/synthetic tests.

## Handoff, publication and cleanup

Read-only platform discovery succeeded at task start: GET `/api/runner-defaults` revision 2; GET `/api/session-runners` showed eligible Linux and Windows lanes. Caller should read both again before dispatch and commission separate Windows desktop Review with `-Platform Windows`, omitting `-Runner`. Retain the default Windows apphost and run CP-14/15/16 from the frozen test-design table. This task does not dispatch another delegate.

Fetched origin/master is `7f8448798955a24427bc2279138bf9fb64bc4195`. Pre-report merge-tree exited 0 with tree `45efa1999954b835678d6c8ab1e78392a7c1587e`. Final merge-tree and full task-base..published-HEAD evidence-policy validation are recorded in the completion message. Report publication is authorized on this task ref only; standing authority was sufficient and no further approval was requested.

Cleanup used a narrow ignored manifest containing only bin-c1210-cp5, bin-c1210-cp6 and bin-c1210-tool. Every one of 57 candidate paths was independently matched to its producer build log or the single bootstrap path before deletion. All 57 were deleted; a fresh dry run returned zero owned outputs. No unrelated outputs, bin/ or obj/ were selected. Raw receipts/TRX/logs and the ignored cleaner copy remain retained. All foreground commands and checkpoint waits were joined to completion.

## Mutation disposition

Post-land SourceLanding Mutation was not run. PCs executed=0/46. Every PC and every variant remains pending, including PC-15's direct and allowed-helper-edge removal variants. Mutation owns deliberate mutants, red/restore/green and missing-control discovery; ordinary green and fail-first receipts do not discharge PCs. Unbound candidates: `docs/superpowers/mutations/`.

PC-1 pending; PC-2 pending; PC-3 pending; PC-4 pending; PC-5 pending; PC-6 pending; PC-7 pending; PC-8 pending; PC-9 pending; PC-10 pending; PC-11 pending; PC-12 pending; PC-13 pending; PC-14 pending; PC-15 pending; PC-16 pending; PC-17 pending; PC-18 pending; PC-19 pending; PC-20 pending; PC-21 pending; PC-22 pending; PC-23 pending; PC-24 pending; PC-25 pending; PC-26 pending; PC-27 pending; PC-28 pending; PC-29 pending; PC-30 pending; PC-31 pending; PC-32 pending; PC-33 pending; PC-34 pending; PC-35 pending; PC-36 pending; PC-37 pending; PC-38 pending; PC-39 pending; PC-40 pending; PC-41 pending; PC-42 pending; PC-43 pending; PC-44 pending; PC-45 pending; PC-46 pending.

## Unedited checkpoint evidence

### Fresh CP-5/6

```text
--- checkpoint report ---
run: 20261013-072345-60bf   manifest: /work/worktrees/task-48fbe811/.antiphon/checkpoints/20261013-072345-60bf/manifest.resolved.yaml
commit: efee2c4057596a15a5ef0fdf5f8eca80f9a1472d  branch: feat/card-task-48fbe811  worktree: /work/worktrees/task-48fbe811  host: Debian GNU/Linux 12 (bookworm) cores=24
source: efee2c4057596a15a5ef0fdf5f8eca80f9a1472d state=clean buildSource=verified
CHECKPOINT CP-5 commit=efee2c4057596a15a5ef0fdf5f8eca80f9a1472d build=ok filter=/*/*/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)|(C1210_Native_linux_entries_guard_before_work*)|(C1210_Roster_closure_matches_source_gates*)|(C1210_Audit_rejects_empty_or_incomplete_outcomes*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-48fbe811/.antiphon/checkpoints/20261013-072345-60bf/rows/CP-5/run.trx slot=granted waited=60s dirty=0 source=efee2c4057596a15a5ef0fdf5f8eca80f9a1472d sourceState=clean buildSource=verified
PHASES CP-5 slotWait=75s build=181.1845997s startup=2.8418404s testsWall=17.5734205s teardown=0.4635666s hostWall=20.8788443s
CHECKPOINT CP-6 commit=efee2c4057596a15a5ef0fdf5f8eca80f9a1472d build=ok filter=/*/*/C1008PlatformContractTests/(C1050_Guard_matches_native_platform*)|(C1050_Remote_entries_guard_before_work*)|(C1050_Rolling_entries_guard_before_work*)|(C1050_Audit_rejects_empty_or_incomplete_outcomes*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-48fbe811/.antiphon/checkpoints/20261013-072345-60bf/rows/CP-6/run.trx slot=granted waited=15s dirty=0 source=efee2c4057596a15a5ef0fdf5f8eca80f9a1472d sourceState=clean buildSource=verified
PHASES CP-6 slotWait=45s build=120.384273s startup=2.9234946s testsWall=0.603682s teardown=0.728056s hostWall=4.2552325s
unlisted: none (the tool ran no other build or test command)
wall: 7m33s  sequential-equivalent: 1m45s  builds: 25  max-concurrent-builds: 1  rows: 2 green 0 red 0 skipped
outputs: kept bin-c1210-cp1/, bin-c1210-cp2/, bin-c1210-cp3/, bin-c1210-cp4/, bin-c1210-cp5/, bin-c1210-cp6/, bin-c1210-cp7/, bin-c1210-cp8/, bin-c1210-cp9/, bin-c1210-cp10/, bin-c1210-cp11/, bin-c1210-cp12/, bin-c1210-cp13/, bin-c1210-cp14/, bin-c1210-cp15/, bin-c1210-cp16/, bin-c1210-cp17/, bin-c1210-cp18/, bin-c1210-cp19/, bin-c1210-cp20/, bin-c1210-cp21/, bin-c1210-cp22/, bin-c1210-cp23/, bin-c1210-cp24/, bin-c1210-cp25/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-072345-60bf
evidence: /work/worktrees/task-48fbe811/.antiphon/checkpoints/20261013-072345-60bf/report.md
verdict: GREEN exit=0
```

### Historical CP-1–4 (old aggregate heading remains unknown)

```text
commit: 229bc00ee7a6335c4fc8c9857f54ce82caff75d7  branch: feat/card-task-604cc024  worktree: /work/worktrees/task-604cc024  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 229bc00ee7a6335c4fc8c9857f54ce82caff75d7 state=clean buildSource=unknown
CHECKPOINT CP-1 commit=229bc00ee7a6335c4fc8c9857f54ce82caff75d7 build=ok filter=/*/*/HostJqPrerequisiteScriptTests/* executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-604cc024/.antiphon/checkpoints/20261013-033923-f946/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=229bc00ee7a6335c4fc8c9857f54ce82caff75d7 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=45s build=189.3014607s startup=6.6449993s testsWall=449.3712462s teardown=0.6592114s hostWall=456.6757416s
CHECKPOINT CP-2 commit=229bc00ee7a6335c4fc8c9857f54ce82caff75d7 build=ok filter=/*/*/JqRunnerImageContractTests/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-604cc024/.antiphon/checkpoints/20261013-033923-f946/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=229bc00ee7a6335c4fc8c9857f54ce82caff75d7 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=154.1333392s startup=4.1756916s testsWall=0.6046697s teardown=0.5354805s hostWall=5.3158416s
CHECKPOINT CP-3 commit=229bc00ee7a6335c4fc8c9857f54ce82caff75d7 build=ok filter=/*/*/GrokSha256PinTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-604cc024/.antiphon/checkpoints/20261013-033923-f946/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=229bc00ee7a6335c4fc8c9857f54ce82caff75d7 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=116.8528517s startup=2.9979329s testsWall=0.1074335s teardown=0.6247831s hostWall=3.7301495s
CHECKPOINT CP-4 commit=229bc00ee7a6335c4fc8c9857f54ce82caff75d7 build=ok filter=/*/*/CodexRootExecContractTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-604cc024/.antiphon/checkpoints/20261013-033923-f946/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=229bc00ee7a6335c4fc8c9857f54ce82caff75d7 sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=140.4162459s startup=3.3364906s testsWall=8.5351039s teardown=0.3770378s hostWall=12.2486321s
```
