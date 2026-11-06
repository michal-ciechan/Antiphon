# CARD-1737 R2 join repair — Review round 2

Found: the cooperative join path is repaired, but join failure can replace the original failure. Final Unit verification remains incomplete. No landing approval.

## Scope and source

Original Code/landing owner: `edd7cc0a-0c0d-4899-9dde-884c9f289474`.
Repair Code task: `ef4e959b-aa4a-4e8c-a8ee-5084e036c26f`.
Reviewed source: `0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67`.
Round-2 range: `90433a94f40bf13e0af25b5d8c76320111da669f..0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67`.
Complete candidate base: `955145f83ab717e3467a06cd192401eb13618129`.
Review branch: `feat/card-task-ebfed7ef`; assigned ordinary Worktree, not a SourceLanding mutation snapshot.

The substantive review is limited to the three-file repair delta: one test file and two plan/control documents. Production is unchanged. No source/test mutation, repair, branch switch, desktop execution, deployment, tracker write or subdelegation was performed. The requested packed controls remain pending under the higher-priority read-only Review rule, including PCs. Only the Review report may be committed on this ordinary Review branch.

The brief explicitly prohibits the whole Unit lane under CARD-1720 while its appended verification profile requires Final/Full, including prior Interim deferrals. The permitted targeted selection does not discharge that missing Final obligation. The stage contract forbids an Interim-to-land disposition. No approval was requested for work already commissioned.

## B-2R — Join timeout replaces the failure it was meant to preserve

Attribution: introduced by this repair; substantive test-lifecycle/adequacy blocker in the reviewed diff.

**Where:** `tests/Antiphon.Tests/Application/C1708ReflogSalvageTests.cs:374`–`:375`, `:391`–`:402`, `:515`–`:520`; new witness at `:43`–`:95`.

**Failure:** while the real tick remains incomplete, cancel the caller/test token. `ReleaseTick.WaitAsync(ct)` raises the original cancellation. The `finally` then awaits `JoinTickAsync`; if the tick ignores cancellation for ten seconds, `CancelAndJoinTickAsync` throws `TimeoutException` because the `ReleaseTick.IsCompleted` catch filter is false. C# finally unwinding replaces the original cancellation with that timeout. The outer cleanup finally and DisposeAsync await the same cached faulted `_join`, so neither retains the original exception nor its context. A throwing cancellation callback also escapes before the join try/catch. This is a control-flow finding, not a claimed executed noncooperative-tick reproduction.

**Why:** the commissioned repair explicitly requires bounded joining without swallowing the original failure. Retaining the world while a tick remains live is correct, and the ten-second wait is finite after Cancel returns; losing the original outcome is still incorrect. `CancellationTokenSource.Cancel()` itself runs callbacks synchronously before that bound. The new witness always uses cancellation-cooperative `release.Task.WaitAsync(token)`. Its assertion/exception variants capture the body exception before disposal and assert its type only after successful cleanup. No case exercises a join deadline or cleanup error while retaining a primary failure; consequently ordinary green cannot establish the promised failure preservation. This is not a constant/self-comparison stub: its real task and repository assertions are meaningful, but the exceptional cleanup path is missing.

**Fix:** in a separate Code task, retain the primary exception through bounded cleanup and report both primary and cleanup failures (with primary identity/stack preserved); keep the fixture intact until the real tick is complete. Add a bounded real-tick witness that deliberately withholds cancellation completion, checks the loud join failure and original failure together, then uses a guaranteed rescue to join and dispose. Cover cancellation-callback failure if supported by this owner. Preserve existing preservation assertions and ordinary deadlines. Re-run the exact affected selection at the repaired source; do not broaden or repeat green rows solely for relabeling.

## B-1 — Final Unit gate remains incomplete

Attribution: carried verification obligation, not a new product regression or a new round-2 source finding.

**Where:** the brief's Final verification profile; repair plan lines 28–31; preceding Review B-1.

**Failure:** no current-source whole-Unit completion exists. Code expressly carries UNIT-FINAL, and the commissioned host prohibition remains. Targeted guard counts, test discovery, exit zero, prior Interim results or nightly green cannot supply the missing methods/arguments.

**Why:** Final requires the complete ordinary selection, including earlier deferrals. The original missing lane and its over-30-minute cost do not disappear because the round-2 diff is small.

**Fix:** commission the exact missing Unit selection once on a suitable execution environment, after CARD-1720's performance disposition and at the eventual final source, with fresh nonzero method/argument receipts and native-exclusion accounting. Preserve historical failed/time-limited outcomes. Do not grant land through Interim or request another Review solely to relabel incomplete evidence.

## Source and adequacy audit

The scoped real DI-resolved DesktopLegacyReleasePolicyService now receives the linked token. TickAsync finally, caller cleanup and DisposeAsync all call the shared join before hooks, modes or the world are cleared. Cooperative cancellation and ordinary tick completion are the paths the new and existing tests exercise. Success, assertion/exception-triggered disposal and cancellation are covered at a real before:legacy-candidate boundary. The repository must exist when that boundary exits and be absent after disposal.

Round-2 changes no producer/recipient queue, receipt schema, recovery storage, durable task identity or session input-wait behavior. Existing V-1..V-7 and R-1/R-2 still drive the real release/Git graph and inspect durable salvage refs, exact commit OIDs, registration and filesystem custody. No queue/Sent/ack evidence is credited as a transcript-confirmed UserPrompt. Wider delivery/native obligations remain with their original scopes. Parking-disabled input waits have no automatic release deadline; this delta adds none.

OS-witness inspection/lint preserves the Linux mode-000 setup/restoration candidates at base lines 159/214 and head lines 226/282. They are test-only; production OS selection is unchanged. The real policy factory/scoped resolution is retained. Native Windows N1 remains pending and Linux evidence cannot discharge it. Runner defaults and session-runner catalogue were read through the configured API; no embedded fleet address or dispatch pin was introduced.

## Author evidence audited, not re-executed

Author red `20261106-204645-f2e4` binds test-first SHA `432c3cd103b6cb0d74fb4554c655cde045f3f4d7`, clean source and verified build. All four distinct argument results fail `joined:W-JOIN:release-tick-completed-before-fixture-dispose`: cancellation, assertion, exception, return. The prior reviewed SHA has no such test; the red SHA adds an equivalent scoped extraction retaining CancellationToken.None and no join. The decisive assertions are present at both red and green. Red has no `join-observation` instrumentation, added only at the fix, so no such red log is invented.

Author green `20261106-205420-5194` binds the reviewed tip and has CP-1/2/3/4 counts 5/7/4/109. Each of its four join observations says joinedBeforeDispose=true, touchedDisposedFixture=false, exited=true. The supplied red/green report and TRX identities and messages were read directly and preserved under external `author-receipts/`; the author audit is `author-join-audit.jsonl`. These are historical author observations, not an independent red run or sourced PC credit. The author used one gated bootstrap and one additional gated mechanical harness (38 rows) at the red SHA, with unchanged harness inputs at the tip; the Review reruns that harness at the tip.

## Pending controls and separate obligations

All formal PCs remain pending: PC-1..PC-7 (PC-5 three malformed variants, PC-7 both empty variants), PC-JOIN's four exit arguments, cancel-only/join-only/order/discovery controls, and the inherited S ledger with its existing candidate/unrun/masked distinctions. The requested fourth reviewer control is a primary failure plus noncooperative/failed cancellation cleanup; it is a proposed control only. No mutation was applied and no restoration/kill credit is claimed.

Windows N1 and separately owned R3/R4, U8/O-1 and U10/O-2 remain outside this repair and are not newly passed. Required manual work stays pending. No restart is required.

## Independent ordinary verification

One checkpoint-tool run, `20261106-211401-96b7`, selected the repair plan's CP-1..CP-4 at the exact reviewed SHA and ended GREEN, exit 0. There was one producing build; the report's `builds: 2` includes the unused CP-0/red build definition. No baseline/mutant build ran. CP-0 was audited as historical test-first evidence, not independently repeated. Each real build/test command acquired its own host build slot; all were granted with zero reported admission wait. No source files changed during the run.

| Row | Executed / passed | Failed / skipped | Execution seconds | Plan coverage |
|---|---:|---:|---:|---|
| CP-1 | 5 / 5 | 0 / 0 | 30.577 | V-1, V-2, V-3, R-1, R-2 |
| CP-2 | 7 / 7 | 0 / 0 | 30.487 | V-4..V-7, all three malformed and both empty arguments |
| CP-3 | 4 / 4 | 0 / 0 | 18.213 | Four exact R-3 classification/policy/wrapper pairs |
| CP-4 | 109 / 109 | 0 / 0 | 144.536 | Full affected SAL and all ten named guard classes, including all four V-JOIN arguments |

CP-4 class counts: SAL 29; MechanicalPcContract 11; TaskPlatformGuidance 5; C1729LayeringGuard 9; SlowTestRegistryMerge 5; LiveLaneRunner 4; SlowTestTripwire 2; TestClassificationPolicy 39; TestLaneCategoryGuard 1; NativeLinuxPolicyWitness 3; TestClassificationGuard 1. Every class OR operand has a trailing `*`. The earlier 16 selections overlap CP-4 intentionally because the committed closed table and Review contract require both the named rows and full affected classes. No green row was repeated for timing.

Fresh TRX inspection validates four unique TestRun IDs and 125 unique execution IDs, with no identity overlap against either audited author run. It validates exact CP-1/2/3 method/argument rosters and CP-4's eleven class counts. All four fresh join observations report joinedBeforeDispose=true, touchedDisposedFixture=false, exited=true, and actual repository deletion is asserted. The unreadable-log case records real `UnauthorizedAccessException:euid=1654`. Detailed per-result identities/durations/observations are preserved in external `review-roster.json`; its validation log is `roster-validation.log`.

Final receipt validation returned `CHECKPOINT SOURCE VALID source=0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 rows=4`, exit 0. Both clean source fingerprints are `001a660cc4c11c5c9eda826d3edf5e94bb7b1a8f4203100ef0cc75dd81f40d91`; every executed row has dirty=0, sourceState=clean, buildSource=verified. This certifies the source binding of the executed checkpoint selection. Overall ordinary completion remains Interim because UNIT-FINAL is absent; it is not Final/Full or land approval.

Performance: final wall 621.833s (10m21s), charged execution 621.764s, admission-only 0.004s, finalization 84.343s. Producing-build receipt: 288.068s, driver execution 303.495s, wall 312.259s. New join arguments measured 0.404–0.519s, one fresh sample per argument; these are not a stable p95. The inherited provisional ceilings remain 28m build, 6m per row, 10m admission, 52m charged and 102m hard wall, with the plan's explicit below-derived-total override. None of this review's rows/runs exceeded 30 minutes; the earlier Unit failure and CARD-1720 Debug obligation remain historical facts.

No new expensive per-test container, AppHost or migration was introduced. Existing assembly-shared PostgreSQL/template/model and Git seed ownership remains; worlds use isolated stores. Phase JSONL is retained under `/work/review-evidence/task-ebfed7ef/timing/`, linked to the review run metadata; exact row membership is in TRX. Host startup/body/teardown phases are preserved below. Unrecorded external-call/setup subphases and unattributed remainder stay unknown, and parallel/nested span sums are not wall time.

Additional drivers, all justified and gated: one native checkpoint bootstrap because this checkout had no tool binary (UseAppHost=false; 4.72s compile, lease held 5s, 9 warnings/0 errors), and the brief-required mechanical contract harness at the reviewed tip (38 passed / 0 failed / 38 rows; lease held 66s). That harness uses disposable repositories and canned driver output, not a sourced producer mutation. The wrapper's non-dotnet maxcpucount warning is retained; the harness performs no real dotnet build. One initial tool-launch invocation used the wrong framework directory, net10.0 instead of the project's net9.0; it exited 1 before a run/build/test existed. The corrected invocation created the sole checkpoint run. No ungated build/test driver or unlisted broad test selection ran.

Execution manifest: `/work/review-evidence/task-ebfed7ef/review-join.yaml`, derived from the committed repair table with identical filters/counts and explicit measured/provisional budgets. Reproduction, after a separately gated native checkpoint-tool bootstrap, is:

```sh
TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll run .antiphon/review-join.yaml --rows CP-1,CP-2,CP-3,CP-4 --serial --expected-source-sha 0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 --total-timeout 52m --total-timeout-override-reason 'Same bounded build/row ceilings as Review; estimates include shared cold build once.' --hard-wall-timeout 102m --max-wait 50s
```

This is a record, not a request to repeat green rows. The next Code task owns the exact repair and missing execution.

## Final checks, evidence and cleanup

After execution, fetched `origin/master` remains `d164e93ae47a79ed175beeb9367212f7a7d5d1d5`. Read-only merge-tree against the reviewed source succeeds with tree `045643c92cbe71abe31f162a1ec9e024a08ae477`. Remote repair source remains the reviewed full SHA. Complete candidate evidence-diff `955145f83ab717e3467a06cd192401eb13618129..0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67` passes: nine commits, zero evidence entries/violations. Diff whitespace checks pass. No merge/rebase/land occurred.

All commands and the checkpoint executor were awaited to terminal. The checkpoint owner deleted its cold output and, after observed death, its tool copy. Native bootstrap roots were independently checked for exact containment, no links and no tracked files; their birth times match this bootstrap. PowerShell reported an access/attribute error after deleting the first bin root; absence was verified, and the three remaining roots were re-resolved under the scratch root and removed using exact literal paths with `rm -r` (no variables/globs/force). No evidence directory or Docker volume was deleted. Cleanup records are external. Required TRX/receipts/logs remain ignored evidence.

Reviewed test SHA-256 remains `6807de9f51303ec2cb046d12ed79691875603234fe068e0c081ee242c1199d92`; unchanged production salvage source remains `e3064c150e1c863107ea10acbe7d8052915c84d3a177e7c27cb740a8705b1a22`. No mutation/restoration was performed.

Durable evidence root: `/work/review-evidence/task-ebfed7ef/`; raw finalized checkpoint receipts also remain at `/work/worktrees/task-ebfed7ef/.antiphon/checkpoints/20261106-211401-96b7/`. The external copy is `checkpoint-receipts/`. Full review report is also mirrored to `.antiphon/task-ebfed7ef.md`. Generated receipts are not committed. Only `docs/investigations/2026-11-06-review-card-1737-r2-round2.md` is the Review-branch deliverable.

Disposition: next Code for B-2R and the exact missing B-1 execution. Preserve original landing owner `edd7cc0a-0c0d-4899-9dde-884c9f289474`; repair task ef4e959b does not replace that owner. Round 3 is blockers-only; any blocker remaining after it goes to the operator. All PCs and separate native/manual obligations above remain pending. No advisory-only rerun is requested.

## Unedited checkpoint evidence
--- checkpoint report ---
run: 20261106-211401-96b7   manifest: /work/worktrees/task-ebfed7ef/.antiphon/checkpoints/20261106-211401-96b7/manifest.resolved.yaml
commit: 0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67  branch: feat/card-task-ebfed7ef  worktree: /work/worktrees/task-ebfed7ef  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 state=clean buildSource=verified
CHECKPOINT CP-1 commit=0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 build=ok filter=/*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_HeadReflogOnlyCommitSurvivesRemoval*)|(C1737_BranchReflogOnlyCommitSurvivesRemoval*)|(C1737_MissingVerifiedSalvageRefKeepsTree*)|(Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref*)|(Commit_kept_only_as_a_reflog_old_id_gets_a_salvage_ref*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-ebfed7ef/.antiphon/checkpoints/20261106-211401-96b7/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=30.577s wall=30.625s
PHASES CP-1 slotWait=0s build=288.0681751s startup=15.5395707s testsWall=8.1732755s teardown=5.9405477s hostWall=29.6537703s
CHECKPOINT CP-2 commit=0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 build=reused filter=/*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_UnreadableRawReflogKeepsTree*)|(C1737_MalformedRawReflogKeepsTree*)|(C1737_MissingRawReflogKeepsTree*)|(C1737_EmptyRawReflogKeepsTree*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-ebfed7ef/.antiphon/checkpoints/20261106-211401-96b7/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=30.487s wall=30.528s
PHASES CP-2 slotWait=0s build=0s startup=7.1940494s testsWall=16.7560955s teardown=5.7953613s hostWall=29.7455063s
SLOW CLASS Antiphon.Tests.Application.C1708ReflogSalvageTests 65s tests=7 (CP-2)
CHECKPOINT CP-3 commit=0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 build=reused filter=/*/*/(TestClassificationGuardTests*)|(TestLaneCategoryGuardTests*)|(NativeLinuxPolicyWitnessTests*)|(LiveLaneRunnerTests*)/(Registry_matches_compiled_metadata*)|(every_test_class_is_tagged_unit_xor_integration*)|(Policy_rows_require_pinned_attribute*)|(WrapperOnlyPaidManifest*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-ebfed7ef/.antiphon/checkpoints/20261106-211401-96b7/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=18.213s wall=18.250s
PHASES CP-3 slotWait=0s build=0s startup=7.30386s testsWall=9.5376191s teardown=0.6644715s hostWall=17.5059505s
CHECKPOINT CP-4 commit=0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 build=reused filter=/*/*/(C1708ReflogSalvageTests*)|(LiveLaneRunnerTests*)|(SlowTestRegistryMergeTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=109 passed=109 failed=0 skipped=0 trx=/work/worktrees/task-ebfed7ef/.antiphon/checkpoints/20261106-211401-96b7/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=144.536s wall=144.577s
PHASES CP-4 slotWait=0s build=0s startup=25.1843218s testsWall=116.1947448s teardown=1.5905666s hostWall=142.969636s
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 99s tests=39 (CP-4)
BUDGET wall=621.768s execution=621.764s admissionOnly=0.004s
unlisted: none (the tool ran no other build or test command)
wall: 10m21s  sequential-equivalent: 3m43s  builds: 2  max-concurrent-builds: 1  rows: 4 green 0 red 0 skipped
outputs: deleted bin-c1737-r2-review/
evidence: /work/worktrees/task-ebfed7ef/.antiphon/checkpoints/20261106-211401-96b7/report.md
verdict: GREEN exit=0
