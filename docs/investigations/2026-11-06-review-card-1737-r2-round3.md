# CARD-1737 R2 — Review round 3

B-2R is repaired. No new blocker was found in the reviewed diff. The carried B-1 / UNIT-FINAL gate remains incomplete; this report does not authorize landing.

Original Code/landing owner: `edd7cc0a-0c0d-4899-9dde-884c9f289474`.
Repair Code: `68422abc-6ea4-449d-8ef1-b343d8a38da8`.
Reviewed source: `5b2e7aab481832bccc256d2269a1a99fae10a88a`.
Round-3 range: `0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67..5b2e7aab481832bccc256d2269a1a99fae10a88a`.
Complete candidate base: `955145f83ab717e3467a06cd192401eb13618129`.
Review task: `1e6caa41-9cf3-47f2-834e-c7bcda4e94fc`, ordinary Worktree on `feat/card-task-1e6caa41`, not a SourceLanding mutation snapshot.

Only the three-file repair delta was substantively reviewed, with the touched test file read in full. Production is unchanged in both the repair delta and complete candidate. No product/test source was edited, no mutation applied, and no desktop, deployment, tracker, remote executor or subdelegate was used. The only tracked deliverable is this report. Work proceeded under the existing commissioning authority without an approval pause.

## B-2R disposition: repaired

At `tests/Antiphon.Tests/Application/C1708ReflogSalvageTests.cs:431`, TickAsync saves the actual primary exception before rethrowing. The cleanup catch at line 459 constructs an aggregate containing that exception and the cleanup failure. The cached join task carries that same aggregate into outer cleanup and DisposeAsync, which still refuses to dispose the world while joining has failed.

The new `C1737_NoncooperativeTickKeepsPrimaryFailureOnJoinTimeout` witness at line 33 resolves the real DesktopLegacyReleasePolicyService in an async DI scope over the existing Git/Postgres world. Its before:legacy-candidate hook deliberately waits for an independent rescue signal despite cancellation. After caller cancellation it checks that the returned aggregate contains exactly the original cancellation token/stack and a TimeoutException; the actual release task remains incomplete and the actual repository remains present. A subsequent DisposeAsync must return the same aggregate and retain the fixture. Its own finally releases the boundary, awaits the real task and the wrapper with independent ten-second bounds, then disposes the world. The final assertion requires the repository to be absent.

The ten-second join wait and forty-second test deadline are unchanged. The outer fifteen-second observation bounds the tested noncooperative path. These are observable outcomes, not a constant or self-comparison: ReferenceEquals compares failures from two independent calls, and the task/filesystem assertions inspect the real owner. Existing cooperative cancellation/assertion/exception/return witnesses and their preservation assertions remain intact. Synchronous cancellation-callback behavior is unchanged; its additional discovery/control work remains pending, not newly certified by this witness.

## Historical author evidence audited

The new method does not exist at `0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67`. Test-first commit `83a7aa839842c0a8cb9e0510d84d5ece04fc4d2a` inserts exactly the new witness while retaining the old helper. Its CP-0 run `20261106-213410-fd5b` executed one result, failed one, skipped zero, at the intended `primary:W-JOIN-TIMEOUT:caller-cancellation-and-join-timeout` assertion: expected System.AggregateException, actual System.TimeoutException. The result took 17.3311167 seconds; this was not a setup failure, discovery count of zero or process timeout. Source start/end fingerprints match, dirty=0 and buildSource=verified. Report Markdown hashes and the actual TRX were checked.

Author run `20261106-214508-3f13` had 5/7/4/110 passing raw results but ended RED exit 7, owner-unverified, wall 926.425s. It remains invalid qualification evidence. The author documented the owner-API failure and reran only that incomplete selection. Qualified run `20261106-220208-f4c6` has 5/7/4/110, zero failures/skips, wall 936.997s and passes the official source validator at the reviewed SHA. The repeated qualification is justified by invalid provenance, not a timing repetition. Historical red wall was 599.379s; both post-fix attempts together cost 31m03.422s. These costs/outcomes remain recorded.

CP-0 was audited as historical test-first evidence, not independently re-executed at a nonexistent base method or credited as a sourced PC. The requested replace-primary/remove-rescue/remove-join controls were not applied: the standing Review contract is read-only, including PCs. They require a separately commissioned method-scoped SourceLanding Mutation task. All control credit remains pending.

## Carried B-1: incomplete Final verification

Attribution: inherited verification obligation, not an introduced regression or a new round-3 product finding.

**Where:** the brief's Final profile, the committed repair plan's UNIT-FINAL deferral, and prior Review B-1.

**Failure:** whole-Unit execution at this source is absent. The brief expressly prohibits it here under CARD-1720 and caps this commission at Interim. Named guard classes and the affected integration class cannot replace the missing lane.

**Why:** the standing Final Review contract includes earlier Interim deferrals and does not permit an Interim-to-land disposition. The narrowing of round-3 source inspection does not create Full execution evidence. Treating the missing lane as passed would leave an incomplete gate.

**Fix:** after CARD-1720's performance disposition, commission the exact missing Unit selection once at the eventual candidate source, with fresh nonzero result/argument identities and qualified build receipts. Do not rerun green targeted rows merely to relabel this report, and do not substitute nightly green or list-tests. Preserve the historical failed/time-limited Unit attempts. This is the final source-review round; surface the remaining gate to the operator rather than starting an automatic fourth repair/review loop. No new source delta remains unreviewed through the recorded tip.

## Scope, runtime and performance audit

The original CP-1/2/3 mapping is preserved: V-1/2/3 and R-1/2; V-4 through V-7 including every malformed/empty argument; and the four exact classification/policy/wrapper checks for R-3. CP-4 is the committed eleven-class union, including all four V-JOIN arguments and the new V-TIMEOUT. Its planned count is now 110, not the previous tip's 109. Every class OR operand has a trailing `*`. The focused rows overlap CP-4 because the closed table explicitly requires both; no unlisted broad assembly selection was introduced.

The release producer is the actual scoped policy. Storage/recovery oracles preserve exact task GUID/ref/OID identity, raw log custody, source snapshots, registration, successful final enumeration and real retirement state. No producer-to-recipient messaging path, queue, receipt schema or input wait changes in this repair. No queue/Sent/ack or screen signal is credited as transcript-confirmed UserPrompt acceptance. Wider delivery/native/manual obligations remain with their original scopes. Parking-disabled sessions have no automatic release deadline; this delta adds none.

No OS-selected production behavior, optional configuration state or startup path changes. The OS witness inventory/lint covers the unchanged Linux permission setup/restoration at base lines 226/282 and tip lines 281/337. Real DI service construction is retained. The Linux unreadable-log witness requires an actual unprivileged denial; native Windows N1 is not discharged by it. GET /api/runner-defaults and GET /api/session-runners were read through the configured API; no fleet address or runner/platform pin was embedded.

The existing assembly-shared Postgres template/model and Git seed remain shared; worlds use isolated stores. There is no new per-test container, migration or AppHost setup. New timing is one ordinary sample, not a measured p95. The plan explicitly labels the small sample and inherited ceilings provisional under CARD-1737: build 28m, each row 6m, admission 10m per command, total charged 52m, hard wall 102m, with its below-derived-total override. Per-result TRX and phase JSONL are retained. Unrecorded external-call/setup phases and unattributed remainder are unknown, not zero. CARD-1720 retains the whole-Unit performance obligation.

## Pending work

All PCs remain pending: PC-1 through PC-7 (including all three PC-5 variants and both PC-7 variants), PC-JOIN's four exit cases, PC-JOIN-TIMEOUT, replace-primary/remove-rescue/remove-join discovery, and the inherited S ledger with its prior candidate/unrun/masked distinctions. Callback-failure, cancel-only, join-only and ordering discovery are not certified. No source restoration cycle or mutation kill is claimed.

Native Windows N1, separately owned R3/R4, U8/O-1 and U10/O-2 and their required manual acceptance remain pending. This repair defines no new manual acceptance. No restart is required.

## Independent ordinary verification

One checkpoint-tool run, `20261106-222555-f9bd`, executed CP-1 through CP-4 at the exact reviewed source and reached GREEN, exit 0. There was one producing build; the unused CP-0 build definition is not a second build. All five actual build/test commands had separate granted host slots. No source changed while the run was active.

| Row | Executed / passed | Failed / skipped | Driver seconds | Slot wait seconds |
|---|---:|---:|---:|---:|
| CP-1 | 5 / 5 | 0 / 0 | 57.591 | 0 |
| CP-2 | 7 / 7 | 0 / 0 | 49.148 | 60 |
| CP-3 | 4 / 4 | 0 / 0 | 32.879 | 30 |
| CP-4 | 110 / 110 | 0 / 0 | 153.438 | 0 |

The fresh TRX audit validates four distinct TestRun IDs and 126 distinct execution IDs, with no overlap against any of the three author attempts. Exact CP-1/2/3 methods, all raw-log argument variants, all four cooperative join variants and the new timeout witness are present. Four join observations report completed-before-disposal=true, touched-disposed-fixture=false and exited=true. The unreadable-log test records actual UnauthorizedAccessException at effective UID 1654. The new timeout witness passed in 00:00:10.7550065. No green row was rerun.

CP-4 class counts: MechanicalPcContractTests 11; C1729LayeringGuardTests 9; TaskPlatformGuidanceTests 5; SlowTestTripwireTests 2; NativeLinuxPolicyWitnessTests 3; TestLaneCategoryGuardTests 1; TestClassificationGuardTests 1; C1708ReflogSalvageTests 30; LiveLaneRunnerTests 4; SlowTestRegistryMergeTests 5; TestClassificationPolicyTests 39.

Official validation: CHECKPOINT SOURCE VALID source=5b2e7aab481832bccc256d2269a1a99fae10a88a rows=4. Start/end clean fingerprints are `6873895961fc041fd65e7e45c1a652f7426d03e06f70611a377e7a95df0e1c20`; every selected row has dirty=0, sourceState=clean, buildSource=verified. This certifies the executed selection's binding, not missing whole-Unit completion. Overall ordinary scope remains Interim.

Performance: final wall 1035.079s; charged execution 944.934s; admission-only 90.066s; finalization 63.943s. Producing-build receipt 560.816s, driver 567.055s, wall 577.284s. Compilation output reports 8m20.83s, 1835 warnings and zero errors. No selection, row or run in this Review exceeded 30 minutes. Per-test durations and exact membership are in review-roster.json; phases are in timing/. Unrecorded components stay unknown.

Two additional drivers were justified and gated: the native/default-output checkpoint bootstrap because this checkout lacked its binary (6.59s compile, lease held 7s, nine warnings and zero errors), and the brief-required mechanical harness (38 passed / 0 failed / 38 rows, lease held 56s). Mechanical fixtures use canned driver output and are not sourced mutants; no real nested dotnet build runs there. The wrapper's non-dotnet maxcpucount warning is retained. There were no ungated drivers or unexplained broad runs. The initial read-only OS lint invocation omitted its required Plan argument and refused; the corrected invocation passed. This launched no build/test and grants no duplicated test credit.

The execution manifest is `/work/review-evidence/task-1e6caa41/review-join.yaml`; an independent comparison matches every committed table filter and minimum count. The command, after the separately gated bootstrap, was:

`TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll run .antiphon/review-join.yaml --rows CP-1,CP-2,CP-3,CP-4 --serial --expected-source-sha 5b2e7aab481832bccc256d2269a1a99fae10a88a --total-timeout 52m --total-timeout-override-reason 'Same bounded build/row ceilings as Review; estimates include shared cold build once.' --hard-wall-timeout 102m --max-wait 50s`

Every wait was awaited, with further waits on exit 75, through terminal completion. This reproduction record is not a request to repeat green rows.

## Final checks and evidence

Fresh origin/master: `303c2476acc3c26538b98717e01776d2385ca450`. Read-only merge-tree against the reviewed source succeeded with tree `c0800bf412c3a1b0fa131c094217bc418d113d8f`. Complete candidate evidence history `955145f83ab717e3467a06cd192401eb13618129..5b2e7aab481832bccc256d2269a1a99fae10a88a` passes: eleven commits, zero evidence entries/violations. Whitespace checks pass. No merge, rebase or landing was performed.

Reviewed test SHA-256: `f1b3931b1b68d55a9ad9026837ec49ad7a4ffee399041534b494293ea0514539`. Unchanged production salvage SHA-256: `e3064c150e1c863107ea10acbe7d8052915c84d3a177e7c27cb740a8705b1a22`.

Durable external evidence: `/work/review-evidence/task-1e6caa41/`, including author-receipts/, checkpoint-receipts/, review-roster.json, source-validation.log, author-audit.log, mechanical.log, timing/, manifest audit, OS inventory/lint, source hashes and cleanup records. Generated evidence is not committed. The report is mirrored to `/work/worktrees/task-1e6caa41/.antiphon/task-1e6caa41.md`; only the requested docs/investigations report is committed on the Review branch.

Disposition: operator triage of the carried incomplete B-1 gate after the last source-review round. Preserve original landing owner `edd7cc0a-0c0d-4899-9dde-884c9f289474`; no replacement owner, no Final/Full claim and no landing approval. The exact missing execution belongs to Code after CARD-1720, not another Review merely to relabel existing evidence. All pending controls and separate native/manual obligations above remain pending.

Cleanup completed after all commands/executors were awaited. The checkpoint owner removed its cold output and shadow copy. The four task-created native bin/obj roots were verified as contained in this scratch worktree, owned, unlinked and without tracked files, then deleted with exact literal absolute paths using rm -r (no force, variables or globs). The completed mechanical fixtures were archived externally and their scratch directory removed after the same containment/ownership checks. No bin/obj/TestResults/compiler roots remain. No Docker volume or source file was removed; raw TRX, receipts, logs and timing evidence remain retained.

The first observation-audit pass validated all 126 identities/rosters but looked for per-test stdout in console.log; that lookup failed. Reading the result-bound TRX StdOut correctly yields all four join observations and the real denial. The corrected read-only audit passed without re-executing any test; both audit logs remain retained. The report assembly hash check initially compared uppercase/lowercase hexadecimal strings case-sensitively; normalizing hex case confirms the bytes match the original receipt. No receipt was changed.

## Unedited checkpoint evidence

--- checkpoint report ---
run: 20261106-222555-f9bd   manifest: /work/worktrees/task-1e6caa41/.antiphon/checkpoints/20261106-222555-f9bd/manifest.resolved.yaml
commit: 5b2e7aab481832bccc256d2269a1a99fae10a88a  branch: feat/card-task-1e6caa41  worktree: /work/worktrees/task-1e6caa41  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 5b2e7aab481832bccc256d2269a1a99fae10a88a state=clean buildSource=verified
CHECKPOINT CP-1 commit=5b2e7aab481832bccc256d2269a1a99fae10a88a build=ok filter=/*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_HeadReflogOnlyCommitSurvivesRemoval*)|(C1737_BranchReflogOnlyCommitSurvivesRemoval*)|(C1737_MissingVerifiedSalvageRefKeepsTree*)|(Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref*)|(Commit_kept_only_as_a_reflog_old_id_gets_a_salvage_ref*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-1e6caa41/.antiphon/checkpoints/20261106-222555-f9bd/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=5b2e7aab481832bccc256d2269a1a99fae10a88a sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=57.591s wall=57.636s
PHASES CP-1 slotWait=0s build=560.8159257s startup=31.3245775s testsWall=16.0054847s teardown=8.427933s hostWall=55.7685931s
CHECKPOINT CP-2 commit=5b2e7aab481832bccc256d2269a1a99fae10a88a build=reused filter=/*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_UnreadableRawReflogKeepsTree*)|(C1737_MalformedRawReflogKeepsTree*)|(C1737_MissingRawReflogKeepsTree*)|(C1737_EmptyRawReflogKeepsTree*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-1e6caa41/.antiphon/checkpoints/20261106-222555-f9bd/rows/CP-2/run.trx slot=granted waited=60s dirty=0 source=5b2e7aab481832bccc256d2269a1a99fae10a88a sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=60s executed=49.148s wall=109.319s
PHASES CP-2 slotWait=60s build=0s startup=12.9555161s testsWall=27.9382791s teardown=7.1226609s hostWall=48.0164559s
SLOW CLASS Antiphon.Tests.Application.C1708ReflogSalvageTests 108s tests=7 (CP-2)
CHECKPOINT CP-3 commit=5b2e7aab481832bccc256d2269a1a99fae10a88a build=reused filter=/*/*/(TestClassificationGuardTests*)|(TestLaneCategoryGuardTests*)|(NativeLinuxPolicyWitnessTests*)|(LiveLaneRunnerTests*)/(Registry_matches_compiled_metadata*)|(every_test_class_is_tagged_unit_xor_integration*)|(Policy_rows_require_pinned_attribute*)|(WrapperOnlyPaidManifest*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-1e6caa41/.antiphon/checkpoints/20261106-222555-f9bd/rows/CP-3/run.trx slot=granted waited=30s dirty=0 source=5b2e7aab481832bccc256d2269a1a99fae10a88a sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=30s executed=32.879s wall=63.030s
PHASES CP-3 slotWait=30s build=0s startup=8.5454002s testsWall=23.0132432s teardown=0.55128s hostWall=32.1099232s
CHECKPOINT CP-4 commit=5b2e7aab481832bccc256d2269a1a99fae10a88a build=reused filter=/*/*/(C1708ReflogSalvageTests*)|(LiveLaneRunnerTests*)|(SlowTestRegistryMergeTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=110 passed=110 failed=0 skipped=0 trx=/work/worktrees/task-1e6caa41/.antiphon/checkpoints/20261106-222555-f9bd/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=5b2e7aab481832bccc256d2269a1a99fae10a88a sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=153.438s wall=153.556s
PHASES CP-4 slotWait=0s build=0s startup=29.1678357s testsWall=120.9619713s teardown=1.8604061s hostWall=151.9902159s
SLOW CLASS Antiphon.Tests.TestHelpers.TestClassificationPolicyTests 90s tests=39 (CP-4)
SLOW CLASS Antiphon.Tests.Application.C1708ReflogSalvageTests 85s tests=30 (CP-4)
BUDGET wall=1034.999s execution=944.934s admissionOnly=90.066s
unlisted: none (the tool ran no other build or test command)
wall: 17m15s  sequential-equivalent: 6m23s  builds: 2  max-concurrent-builds: 1  rows: 4 green 0 red 0 skipped
outputs: deleted bin-c1737-r3-review/
evidence: /work/worktrees/task-1e6caa41/.antiphon/checkpoints/20261106-222555-f9bd/report.md
verdict: GREEN exit=0
