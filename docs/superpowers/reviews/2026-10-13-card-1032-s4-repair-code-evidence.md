# CARD-1032 S4 fail-closed repair Code evidence

D1/D2/D3 are repaired. Required S4/S2/S3 rows and both full affected classes passed
216/216, zero skips. The required runner Unit supplement passed 358/378; all 20
failures independently reproduced at the task base. This is a scoped successful
repair with a RED inherited Unit row, not an all-green Final run. Next: Review.

Landing owner / current Code task: ebf0a2f0-49cb-4525-95b7-1d2dcffecd2f.
Predecessor Code owner: 8bcfa5da-1c58-477c-bf97-65f83a299509.
Branch: feat/card-task-ebf0a2f0.
Worktree: /work/worktrees/task-ebf0a2f0.
Desktop counterpart (not accessed): C:\Antiphon\worktrees\card-task-ebf0a2f0.
Task base: d7d27ae327a4397fe061cbb17626e249fc7d7190.
Tests-only commit: 653db0d4bfcd04f4703af4242d2cc0141a8af96c.
Actual tested repair SHA: cfe966b3756f164cb12a663db2fcaa8280cd2395.
A later report-only commit does not relabel these receipts.

## Changes and proving tests

- RunnerCompletedCardWorkspaceRemoval: conservative HOLD for lowercase/S index
  tags with NUL-delimited HeldPaths, admin/common recursive locks and gc.pid,
  operation markers, sparse and shallow state, unpopulated gitlinks, case twins;
  detached HEAD retains actual source SHA. C1032_InspectionHoldShapes checks 42
  hand-written real-repository arguments, verdict/reason, actual HEAD (where
  readable), unchanged file/index/ref/store digest and no retirement intent.
- SessionRunner.Contracts.RepositoryDirectoryShape: filesystem-only ordinary/bare
  markers used before disposable classification; bare and ignored-bare arguments
  initialize a real bare repository and push a retained ref. CARD-1211's bare fix
  is absent from fetched master; it should converge to this shared helper.
- C1032_InspectionReadOnlyGitCalls: exact read-only Git argument allowlist,
  GIT_OPTIONAL_LOCKS=0 for each captured call, mtime-only index preservation,
  successful fresh proof reads and absence of retirement state.
- The original three CP-6 methods remain intact. C1032_InspectionSourceProof
  checks advanced actual HEAD and H-behind-P containment. S2/S3 regressions retain
  their original counts and filters; CP-5 runs once. Cleanup remains inert: the
  inspection tests create no retirement state, and the manual base..repair diff
  of both Program files and PhoneHomeCommandDispatcher is empty.

Plan: docs/superpowers/plans/2026-10-11-card-1032-runner-mirror-cleanup-plan.md.
Frozen design: docs/superpowers/plans/2026-10-12-card-1032-test-design.md (CP-6 now 46).
Exact repair selection / HOLD table / new controls:
docs/superpowers/plans/2026-10-13-card-1032-s4-repair-verification.md.

## Negative proof and unchanged bytes

The tests-only commit changed no production source from d7d27ae32. CP-6 executed
46, passed 16, failed 30. All D1 index-flag arguments, both bare arguments, every
lock/gc marker and each operation argument failed the hand-written HOLD reason
assertion. The ignored bare fixture returned Eligible on the old production;
flagged tracked edits and lock/unfinished-operation markers also returned Eligible.
Three additional failures required precise diagnostic reasons (ordinary bare,
unpopulated gitlink, detached HEAD); they are not called fail-open observations.
Each HoldShapes digest assertion precedes its verdict assertion, so those 30
reason failures also passed the captured-byte comparison. These regression reds
are not deliberate mutants and discharge no PC.

Fresh TRX was read for every selected row's class/method/argument roster and
nonzero count. CP-6 has original three + 42 shape arguments + one Git-call method.
Full classes have 52 CompletedCardWorkspaceRemovalTests and 33
RunnerWorkspaceServiceTests results. Class-qualified roster audits, not counters
alone, are stored with each run. The manual HOLD table is independently written;
its 42 physical shapes were executed as table-driven tests. Parent native Windows
junction/sharing and delivery/deployment acceptance remain deferred, not passed.

## Ordinary V/R outcomes

| ID | Actual S4 round outcome | Deferred obligations (not passed) |
|---|---|---|
| V-1 | S2 identity/capture/join CP-3/4/17–24 passed | CP-1/13/15 schema/admission/recovery outside repair |
| V-2 | Read-only Git/no-intent assertions passed in CP-6 | CP-1/2/11/14/15 wire/removal receipt/composition outside repair |
| V-3 | CP-6 46/46; full affected classes 85/85 passed | CP-8/9/13 destructive/final preservation outside S4 |
| V-4 | CP-5 6/6 once; CP-25 2/2; CP-26 11/11; full classes passed | CP-7 S5 owner/removal witnesses |
| V-5 | Not run: outside S4 repair | CP-7/8/9/10/11/15 removal/component recovery |
| V-6 | Not run: S7/S8 coordinator/removal-receipt scope | CP-13/14/15 end-to-end endpoint recovery |
| R-1 | CP-24 3/3, CP-26 11/11, CP-27 33/33; full classes passed. Unit supplement has 20 confirmed inherited reds | CP-12 remaining legacy selection; CP-16 desktop sync outside S4 |
| R-2 | CP-3/23/24 legacy binding/dispatcher contracts and full classes passed | CP-11/15 S6/S8 default composition |
| R-3 | Not run: parent destructive witnesses outside S4 | CP-7/8/9/10/11/13/14/15 and native acceptance |

Deferred-to-later-slice IDs: CP-1/2, CP-7..16 and the listed outstanding V/R
sub-obligations; native Windows parent acceptance; every PC/variant below.
Nothing in that list is marked passed by the S4 green rows.

## Unit failures and scope exceptions

The brief excluded Unit; higher-priority Code Final instructions require the whole
Unit lane. S4-unit is therefore a disclosed supplement, checking existing runner
Unit behavior with inspector unconfigured, all Unit classes, estimated 1–3 minutes.
No whole-assembly or unrelated namespace run occurred. No Windows-only assertion,
timeout or platform skip was changed. Unit had 19 CodexWindowsLaunchPolicyTests
results (18 methods, Nonpositive_budget_is_refused has two arguments) and one
RunnerCustodyLedgerBackendTests.Linux_backend_accepted_only_when_probe_passed red.
The exact 20 class/method/argument identities and exception kinds match the base
rerun; the audit is inherited-red-audit.txt under the final run directory.
The formerly flaky websocket case passed in this Unit lane; no websocket rerun.

Declared extra builds: one checkpoint-tool bootstrap (UseAppHost=false,
bin-c1032-s4r-tool/, slot granted, waited 15s, one pre-existing warning); tests-only
negative runner build; final one isolated build per required project (runner and
server); one independent base runner build for exact inherited failure diagnosis.
Server regressions require Antiphon.Tests, so a runner-only build cannot execute
CP-4/20/21. All driver builds/tests took slots. No build ID was named tests.
One mistyped expected SHA was refused before a build/test; it was corrected.
No unchanged broad repeats, loaded repetitions or repetition-budget exception.
Source stayed frozen during each run. Every selected invocation supplied committed
expected SHA; clean source and verified build provenance are retained even for RED
rows, which are never described as green certificates.

GET /api/runner-defaults and GET /api/session-runners succeeded at dispatch.
The assigned Linux mirror supports CP-21/22. No host or platform pin was added.
Restart: none. No live worktree removal, session stop, activation or deployment.

## Per-checkpoint counts and slots

| Row | Executed | Passed | Failed | Skipped | Slot | Waited |
|---|---:|---:|---:|---:|---|---:|
| CP-6 | 46 | 46 | 0 | 0 | granted | 30s |
| CP-3 | 5 | 5 | 0 | 0 | granted | 30s |
| CP-5 | 6 | 6 | 0 | 0 | granted | 75s |
| CP-17 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-18 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-19 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-22 | 10 | 10 | 0 | 0 | granted | 0s |
| CP-23 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-24 | 3 | 3 | 0 | 0 | granted | 0s |
| CP-25 | 2 | 2 | 0 | 0 | granted | 0s |
| CP-26 | 11 | 11 | 0 | 0 | granted | 0s |
| CP-27 | 33 | 33 | 0 | 0 | granted | 0s |
| S4-classes | 85 | 85 | 0 | 0 | granted | 15s |
| S4-unit | 378 | 358 | 20 | 0 | granted | 15s |
| CP-4 | 4 | 4 | 0 | 0 | granted | 45s |
| CP-20 | 1 | 1 | 0 | 0 | granted | 0s |
| CP-21 | 6 | 6 | 0 | 0 | granted | 0s |

Final source: clean, build provenance: verified.
Build bin-c1032-s4r-runner: ok, slot=granted, waited=90s.
Build bin-c1032-s4r-server: ok, slot=granted, waited=30s.

## Unedited CHECKPOINT lines

```text
CHECKPOINT CP-6 commit=653db0d4bfcd04f4703af4242d2cc0141a8af96c build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/C1032_Inspection* executed=46 passed=16 failed=30 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-032808-bcf5/rows/CP-6/run.trx slot=granted waited=15s dirty=0 source=653db0d4bfcd04f4703af4242d2cc0141a8af96c sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/C1032_Inspection* executed=46 passed=46 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-6/run.trx slot=granted waited=30s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_Binding* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-3/run.trx slot=granted waited=30s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/C1032_Fence* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-5/run.trx slot=granted waited=75s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_ForeignOwnerAdoptionRefused* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-18 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_AbsentMirrorReplayRefused* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-19 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_StoreMismatch* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentity* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-23 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/RunnerWorkspaceBindingTests/C1032_LegacyUnboundPathOnlyContract* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/PhoneHomeCommandDispatcherTests/Workspace_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-24/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-25 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/LegacyWorkspaceRemovalGateTests/(C1032_LegacyRemoveHeldLease*)|(C1032_LegacyPruneHeldLease*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-25/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-26 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/LegacyWorkspaceRemovalGateTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-26/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-27 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/RunnerWorkspaceServiceTests/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-27/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT S4-classes commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/(CompletedCardWorkspaceRemovalTests*)|(RunnerWorkspaceServiceTests*)/* executed=85 passed=85 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/S4-classes/run.trx slot=granted waited=15s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT S4-unit commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/*/*[Category=Unit] executed=378 passed=358 failed=20 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/S4-unit/run.trx slot=granted waited=15s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=ok filter=/*/*/(CardWorktreeMirrorBindingTests*)|(RemoteWorkspacePreparerTests*)/C1032_Capture* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-4/run.trx slot=granted waited=45s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-20 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/RemoteWorkspacePreparerTests/C1032_StoreReplacement* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=cfe966b3756f164cb12a663db2fcaa8280cd2395 build=reused filter=/*/*/PhoneHomeMirrorJoinTests/C1032_Join* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=cfe966b3756f164cb12a663db2fcaa8280cd2395 sourceState=clean buildSource=verified
CHECKPOINT S4-base-unit-reds commit=d7d27ae327a4397fe061cbb17626e249fc7d7190 build=ok filter=/*/*/(CodexWindowsLaunchPolicyTests*)|(RunnerCustodyLedgerBackendTests*)/(A_configured_budget_above_30000_does_not_raise_the_transport_ceiling*)|(A_customized_wrapper_is_not_rewritten_and_falls_under_the_batch_ceiling*)|(Explicit_cmd_launcher_is_capped_at_7000*)|(Explicit_node_codex_js_is_left_alone_but_still_budgeted*)|(Install_root_with_spaces_and_unicode_round_trips*)|(Linux_backend_accepted_only_when_probe_passed*)|(Missing_node_never_returns_the_batch_request*)|(Missing_sibling_and_PATH_node_is_codex_launcher_unavailable*)|(Native_hop_is_bounded_by_the_longest_installed_native_path*)|(Nonpositive_budget_is_refused*)|(Nul_in_any_argument_is_refused*)|(PATH_node_is_used_when_no_sibling*)|(Quote_expansion_is_counted_not_estimated*)|(Recognized_shim_without_a_vendored_native_package_is_codex_launcher_unsupported*)|(Serialized_hop1_at_budget_is_accepted_and_plus_one_is_refused*)|(Shim_without_codex_js_is_codex_launcher_unavailable*)|(Sibling_node_wins_over_PATH_node*)|(Standard_npm_shim_becomes_node_exe_plus_codex_js*)|(Surrogate_pairs_count_two_units*) executed=20 passed=0 failed=20 skipped=0 trx=/work/review-evidence/task-ebf0a2f0/base/.antiphon/checkpoints/20261013-034047-d88a/rows/S4-base-unit-reds/run.trx slot=granted waited=30s dirty=0 source=d7d27ae327a4397fe061cbb17626e249fc7d7190 sourceState=clean buildSource=verified
```

## Receipt paths and rerun

Negative evidence: /work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-032808-bcf5/report.json (expected RED).
Final evidence: /work/worktrees/task-ebf0a2f0/.antiphon/checkpoints/20261013-033204-84d4/report.json (16 green rows plus RED inherited Unit).
Base confirmation: /work/review-evidence/task-ebf0a2f0/base/.antiphon/checkpoints/20261013-034047-d88a/report.json (expected RED; exact 20-case roster).
Validation of the 16 green selected rows excludes S4-unit; that exclusion is explicit, not a green relabel.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1032-s4-repair-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1032-s4r-tool/ --property:UseAppHost=false -nodeReuse:false
dotnet exec tools/Antiphon.Checkpoints/bin-c1032-s4r-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-13-card-1032-s4-repair-verification.md --expected-source-sha <committed-head> --keep-outputs --serial --max-wait 45s
```
Await every exit-75 run with wait <run-id> --max-wait 45s until terminal.

## Exact inherited Unit failure identities

Confirmed against d7d27ae327a4397fe061cbb17626e249fc7d7190; all 20 remain RED.

- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.A_configured_budget_above_30000_does_not_raise_the_transport_ceiling|A_configured_budget_above_30000_does_not_raise_the_transport_ceiling`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.A_customized_wrapper_is_not_rewritten_and_falls_under_the_batch_ceiling|A_customized_wrapper_is_not_rewritten_and_falls_under_the_batch_ceiling`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Explicit_cmd_launcher_is_capped_at_7000|Explicit_cmd_launcher_is_capped_at_7000`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Explicit_node_codex_js_is_left_alone_but_still_budgeted|Explicit_node_codex_js_is_left_alone_but_still_budgeted`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Install_root_with_spaces_and_unicode_round_trips|Install_root_with_spaces_and_unicode_round_trips`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Missing_node_never_returns_the_batch_request|Missing_node_never_returns_the_batch_request`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Missing_sibling_and_PATH_node_is_codex_launcher_unavailable|Missing_sibling_and_PATH_node_is_codex_launcher_unavailable`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Native_hop_is_bounded_by_the_longest_installed_native_path|Native_hop_is_bounded_by_the_longest_installed_native_path`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Nonpositive_budget_is_refused|Nonpositive_budget_is_refused(-1)`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Nonpositive_budget_is_refused|Nonpositive_budget_is_refused(0)`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Nul_in_any_argument_is_refused|Nul_in_any_argument_is_refused`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.PATH_node_is_used_when_no_sibling|PATH_node_is_used_when_no_sibling`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Quote_expansion_is_counted_not_estimated|Quote_expansion_is_counted_not_estimated`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Recognized_shim_without_a_vendored_native_package_is_codex_launcher_unsupported|Recognized_shim_without_a_vendored_native_package_is_codex_launcher_unsupported`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Serialized_hop1_at_budget_is_accepted_and_plus_one_is_refused|Serialized_hop1_at_budget_is_accepted_and_plus_one_is_refused`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Shim_without_codex_js_is_codex_launcher_unavailable|Shim_without_codex_js_is_codex_launcher_unavailable`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Sibling_node_wins_over_PATH_node|Sibling_node_wins_over_PATH_node`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Standard_npm_shim_becomes_node_exe_plus_codex_js|Standard_npm_shim_becomes_node_exe_plus_codex_js`
- `Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Surrogate_pairs_count_two_units|Surrogate_pairs_count_two_units`
- `Antiphon.SessionRunner.Tests.RunnerCustodyLedgerBackendTests.Linux_backend_accepted_only_when_probe_passed|Linux_backend_accepted_only_when_probe_passed`

## Pending SourceLanding Mutation inventory

All 314 PC IDs remain pending, including every named variant. No deliberate mutant or post-land Mutation ran. The prior 302 rows are retained verbatim below; PC-561..572 add the S4 repair controls. Mutation owns deliberate red/restore/green and missing-control discovery after Review and caller landing of this Code task.

### Prior 302 IDs / variants (all pending)

| PC | Lane | Perturbation / named variants | Detecting filter | Expected red | Status |
|---|---|---|---|---|---|
| PC-52 | RUN | eligible command through dispatcher -> break G-52: route it to legacy RemoveAsync. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_GuardedOperationIsDistinct` | `legacyRemovalCalls.ShouldBe(0)` | pending |
| PC-53 | RUN | different full TaskId with equal short id and valid path -> break G-53: omit TaskId comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerTaskIdentity` | `treeExists.ShouldBeTrue()` | pending |
| PC-54 | RUN | same task and binding coordinates, foreign attempt -> break G-54: omit attempt comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerAttemptIdentity` | `treeExists.ShouldBeTrue()` | pending |
| PC-55 | RUN | new store at same path, otherwise matching journal -> break G-55: omit RunnerStoreId comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerStoreIdentity` | `treeExists.ShouldBeTrue()` | pending |
| PC-56 | RUN | real foreign repository with the same commit -> break G-56: omit repository identity comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerRepositoryIdentity` | `foreignBytes.ShouldBe(originalBytes)` | pending |
| PC-57 | RUN | valid second task tree, sibling-prefix and outside-root variants -> break G-57: omit exact recorded-path comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPathIdentity` | `otherTreeExists.ShouldBeTrue()` | pending |
| PC-58 | RUN | different symbolic branch at equal SHA; detached variant -> break G-58: omit symbolic-ref comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerBranchIdentity` | `identityRefused.ShouldBeTrue()` | pending |
| PC-59 | RUN | unique committed HEAD changed after request -> break G-59: omit expected HEAD comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerHeadIdentity` | `preservationProofChecks.ShouldBe(0)` | pending |
| PC-60 | RUN | bare SHA with no proof; exact contained and bound-rebase positive variants -> break G-60: accept missing preservation proof. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `treeExists.ShouldBeTrue()` | pending |
| PC-61 | RUN | staged and unstaged tracked edits; separate untracked variant -> break G-61: accept nonempty tracked status. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDirtySource` | `finalInspectionCalls.ShouldBe(0)` | pending |
| PC-62 | RUN | bin/.env fixture overlaps disposable rule -> break G-62: remove protected-name precedence. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `protectedBytes.ShouldBe(originalBytes)` | pending |
| PC-63 | RUN | swap enumerated directory for outside link after final inspection -> break G-63: enumerate through the substituted link. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerNoFollow` | `outsideSentinel.ShouldBe(originalBytes)` | pending |
| PC-64 | RUN | terminal session row with observed live owned child -> break G-64: use terminal status as sufficient no-owner proof. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerProcessOwnerHolds` | `treeExists.ShouldBeTrue()` | pending |
| PC-65 | RUN | intent-first and start-first with different session IDs at same workspace -> break G-65: skip workspace fence in StartAsync. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerStartFence` | `startsAtRetiringPath.ShouldBe(0)` | pending |
| PC-66 | RUN | restart at durable intent, then attempt admission through adoption -> break G-66: skip restored workspace fence on pty adoption. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerReadoptionFence` | `adoptionsAtRetiringPath.ShouldBe(0)` | pending |
| PC-67 | RUN | pause before first physical rename and read journal independently -> break G-67: move intent persistence after first mutation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIntentBeforeIo` | `intentVisibleAtFirstMutation.ShouldBeTrue()` | pending |
| PC-68 | RUN | same ID with changed valid digest versus byte-identical replay -> break G-68: return saved success for changed digest. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDigestReplay` | `changedRequestAccepted.ShouldBeFalse()` | pending |
| PC-69 | RUN | late tracked file after successful first read -> break G-69: omit final tracked-source validation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateTreeBytes.ShouldBe(originalBytes)` | pending |
| PC-70 | RUN | raw force=true with otherwise valid request and dirty source -> break G-70: add compiling raw-JSON force arm routing to forced legacy removal. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerNoForceBypass` | `dirtyBytes.ShouldBe(originalBytes)` | pending |
| PC-71 | MIR | local Complete and mirror Pending, fresh coordinator -> break G-71: filter out targets with completed local endpoint. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MirrorCompletesIndependently` | `mirrorDirectoryGone.ShouldBeTrue()` | pending |
| PC-72 | MIR | offline mirror with eligible local tree -> break G-72: require online mirror before local execution. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_LocalProgressSurvivesOffline` | `localDirectoryGone.ShouldBeTrue()` | pending |
| PC-73 | MIR | real H ancestor of P, and changed-H refusal -> break G-73: require H==P despite exact H containment proof. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MirrorBehindPushedTip` | `mirrorDirectoryGone.ShouldBeTrue()` | pending |
| PC-74 | MIR | valid foreign-operation receipt, other fields equal -> break G-74: omit receipt operation-ID comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchOperation` | `mirrorComplete.ShouldBeFalse()` | pending |
| PC-75 | MIR | valid receipt with only endpoint changed -> break G-75: omit endpoint-ID comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchEndpoint` | `mirrorComplete.ShouldBeFalse()` | pending |
| PC-76 | MIR | valid receipt from replacement store -> break G-76: omit store-ID comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchStore` | `mirrorComplete.ShouldBeFalse()` | pending |
| PC-77 | MIR | valid receipt with only attempt changed -> break G-77: omit attempt comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReceiptMustMatchTaskAttempt` | `mirrorComplete.ShouldBeFalse()` | pending |
| PC-78 | MIR | ack, empty payload, null path, timeout and component-unknown variants -> break G-78: mark Complete on successful RPC return. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_AckIsNotRemoval` | `mirrorComplete.ShouldBeFalse()` | pending |
| PC-79 | MIR | missing capability or UnsupportedOperation response -> break G-79: retry using WorkspaceRemove. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_UnsupportedNeverFallsBack` | `legacyRemovalCalls.ShouldBe(0)` | pending |
| PC-80 | MIR | reopen commits after local result before mirror intent -> break G-80: reuse local generation admission for mirror. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_ReopenBetweenEndpoints` | `remoteCommandsIssued.ShouldBe(0)` | pending |
| PC-81 | MIR | two stores, first Complete, second offline -> break G-81: replace normalized bindings with current scalar path. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_MultipleMirrorsRemainDistinct` | `persistedEndpointCount.ShouldBe(2)` | pending |
| PC-82 | MIR | real removal, dropped reply, server restart and reconnect -> break G-82: allocate new operation on retry. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_LostAckReusesOperation` | `distinctRemovalOperationIds.ShouldBe(1)` | pending |
| PC-108 | RUN | genuine verification creation with forged ordinary fields -> break G-108: accept verification creation as ordinary binding. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerSourceLandingExcluded` | `verificationTreeExists.ShouldBeTrue()` | pending |
| PC-109 | RUN | unavailable census and torn owned-child record -> break G-109: treat unknown ownership as empty. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_UnknownProcessOwnershipHolds` | `treeExists.ShouldBeTrue()` | pending |
| PC-200 | DB | concurrent inserts with same PreparationId -> break G-200: drop preparation unique index in mapping and generated migration. | `/*/*/CardWorktreeMirrorBindingTests/C1032_SchemaUniqueBindings` | `preparationRows.ShouldBe(1)` | pending |
| PC-201 | DB | same task/attempt/store/binding under different preparation IDs -> break G-201: drop tuple unique index in mapping and generated migration. | `/*/*/CardWorktreeMirrorBindingTests/C1032_SchemaUniqueBindings` | `duplicateBindingRejected.ShouldBeTrue()` | pending |
| PC-202 | DB | upgrade predecessor DB with active, Done, canceled and scalar-only mirror rows -> break G-202: add synthetic binding backfill for RemoteWorktreePath. | `/*/*/CardWorktreeMirrorBindingTests/C1032_SchemaLegacyUpgrade` | `legacyBindingCount.ShouldBe(0)` | pending |
| PC-203 | DB | save intent, change task coordinates, reopen DbContext -> break G-203: omit immutable-request property from EF persistence mapping. | `/*/*/CardWorktreeMirrorBindingTests/C1032_SchemaRequestAndReceiptPersistence` | `persistedRequestBytes.ShouldBe(originalRequestBytes)` | pending |
| PC-204 | DB | save typed receipt with component facts then fresh context -> break G-204: omit receipt payload column write. | `/*/*/CardWorktreeMirrorBindingTests/C1032_SchemaRequestAndReceiptPersistence` | `persistedReceiptBytes.ShouldBe(originalReceiptBytes)` | pending |
| PC-205 | WIRE | round-trip inspect/remove plus old operation 15 literal -> break G-205: reuse legacy enum value for guarded remove. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRoundTrip` | `guardedOperationValue.ShouldNotBe(15)` | pending |
| PC-206 | WIRE | unknown/zero schema against otherwise valid JSON -> break G-206: accept unknown version as current. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `versionAccepted.ShouldBeFalse()` | pending |
| PC-207 | WIRE | equivalent object property order and artifact order normalization -> break G-207: digest uncanonicalized property insertion order. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `equivalentDigest.ShouldBe(originalDigest)` | pending |
| PC-208 | WIRE | duplicate operationId with two values -> break G-208: accept last duplicate field. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `duplicateAccepted.ShouldBeFalse()` | pending |
| PC-209 | RB | different full IDs share task-deadbeef leaf and equal source -> break G-209: lookup binding by short name alone. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingFullIdentityCollision` | `foreignBindingAccepted.ShouldBeFalse()` | pending |
| PC-210 | RB | cut after preparation journal, after worktree add, before completion, after completion -> break G-210: write creation intent only after worktree add. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingCreationRecovery` | `creationIntentAtGitAdd.ShouldBeTrue()` | pending |
| PC-211 | RB | legacy equal-SHA tree exists before incomplete preparation -> break G-211: mark pre-existing tree as completed bound creation. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingCreationRecovery` | `legacyTreeEnrolled.ShouldBeFalse()` | pending |
| PC-212 | RB | new store object over same state after reply loss -> break G-212: generate new BindingId for exact PreparationId replay. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingExactReplay` | `replayedBindingId.ShouldBe(originalBindingId)` | pending |
| PC-213 | RB | same preparation with changed attempt/branch/baseline -> break G-213: overwrite existing preparation record on mismatch. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingExactReplay` | `changedPreparationAccepted.ShouldBeFalse()` | pending |
| PC-214 | RB | payload asks state path inside mirror and outside allowed state root -> break G-214: use payload-supplied state root. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingStateRootIsolation` | `bindingStoredAtConfiguredRoot.ShouldBeTrue()` | pending |
| PC-215 | RB | torn, unreadable or missing record after interrupted write -> break G-215: recover failed record read as a completed binding. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingStateRootIsolation` | `bindingAccepted.ShouldBeFalse()` | pending |
| PC-216 | RB | old request without binding extension on configured ordinary service -> break G-216: require binding fields for legacy mirror requests. | `/*/*/RunnerWorkspaceBindingTests/C1032_BindingLegacyCompatibility` | `legacyMirrorExists.ShouldBeTrue()` | pending |
| PC-217 | DB | attempt/store A then B; reload with current scalar pointing only at B -> break G-217: upsert binding by TaskId only. | `/*/*/CardWorktreeMirrorBindingTests/C1032_CaptureDistinctBindings` | `capturedBindingCount.ShouldBe(2)` | pending |
| PC-218 | CAP | hold reply, reassign runner, then return valid binding -> break G-218: return before persisting historical binding when assignment differs. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLateReassignment` | `oldRunnerBindingPersisted.ShouldBeTrue()` | pending |
| PC-219 | CAP | current runner B already has a path when A replies -> break G-219: update scalar path without runner compare. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLateReassignment` | `currentPath.ShouldBe(runnerBPath)` | pending |
| PC-220 | CAP | same runner, increment attempt before reply -> break G-220: drop attempt predicate on scalar update. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureAttemptAndCancellation` | `currentPath.ShouldBe(newAttemptPath)` | pending |
| PC-221 | CAP | cancel originating attempt before reply -> break G-221: skip binding persistence for terminal task. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureAttemptAndCancellation` | `canceledAttemptBindingPersisted.ShouldBeTrue()` | pending |
| PC-222 | CAP | server dies after request send then preparation rearmed -> break G-222: allocate fresh PreparationId on recovery. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `recoveryPreparationId.ShouldBe(firstPreparationId)` | pending |
| PC-223 | RUN | different session IDs and normalized aliases of one workspace -> break G-223: key gate by raw path or session ID. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceStartOrders` | `simultaneousAdmissions.ShouldBe(1)` | pending |
| PC-224 | RUN | real AttachHerdrAsync after intent, then inverse order -> break G-224: omit workspace admission in explicit attach. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceAttachOrders` | `attachEffectsAtRetiringPath.ShouldBe(0)` | pending |
| PC-225 | RUN | hold manifest census at restart with retained live child -> break G-225: mark census ready before manifests inspected. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders` | `cleanupAdmittedBeforeCensus.ShouldBeFalse()` | pending |
| PC-226 | RUN | live sidecar at fenced path -> break G-226: omit ownership conflict on startup Herdr adoption. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceHerdrReadoptionOrders` | `ownershipConflictVisible.ShouldBeTrue()` | pending |
| PC-227 | RUN | unreachable sidecar then reconnect via SweepVanishedSessionsAsync -> RetryPendingHerdrAsync -> break G-227: omit workspace guard on pending retry arm. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceHerdrReadoptionOrders` | `cleanupAdmittedDuringPendingAdoption.ShouldBeFalse()` | pending |
| PC-228 | RUN | MirrorAsync competing with retiring gate, both race orders -> break G-228: omit workspace admission from mirror creation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceCreationOrders` | `mirrorCreatesAtRetiringPath.ShouldBe(0)` | pending |
| PC-229 | RUN | missing/torn/unreadable retirement journal after restart -> break G-229: restore malformed fence as open. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceUnknownJournal` | `cleanupAdmitted.ShouldBeFalse()` | pending |
| PC-230 | RUN | hold workspace, observe request at gate before session gate acquired -> break G-230: acquire session lock before workspace lock. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceStartOrders` | `sessionGateHeldBeforeWorkspace.ShouldBeFalse()` | pending |
| PC-231 | RUN | live surviving child contradicts persisted retiring intent -> break G-231: call KillPidBestEffort on the cleanup conflict arm. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders` | `ownedChildStillAlive.ShouldBeTrue()` | pending |
| PC-232 | RUN | same contradictory manifest/sidecar, fresh runtime observation -> break G-232: discard retained owner before recording conflict. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders` | `retainedOwnerVisible.ShouldBeTrue()` | pending |
| PC-233 | RUN | known bound clean mirror inspection -> break G-233: invoke removal/registration cleanup from inspect. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionReadOnlyBound` | `repositoryAndTreeDigestAfter.ShouldBe(before)` | pending |
| PC-234 | RUN | unbound valid legacy tree with full caller-supplied identity -> break G-234: create binding while inspecting unknown path. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionReadOnlyBound` | `bindingCountAfter.ShouldBe(bindingCountBefore)` | pending |
| PC-235 | RUN | unknown ignored synthetic file -> break G-235: default unknown classification to Disposable. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionPolicyParity` | `unknownClassification.ShouldBe(Protected)` | pending |
| PC-236 | RUN | known checkpoint evidence overlaps custom disposable rule -> break G-236: classify Disposable before Evidence. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionPolicyParity` | `evidenceClassification.ShouldBe(Evidence)` | pending |
| PC-237 | RUN | request policy removes .env protection -> break G-237: replace protected minima with request policy. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionPolicyParity` | `minimumClassification.ShouldBe(Protected)` | pending |
| PC-238 | RUN | bin/.antiphon/unknown under broad bin rule -> break G-238: apply ancestor disposable glob to .antiphon subtree. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionPolicyParity` | `nestedAntiphonClassification.ShouldBe(Protected)` | pending |
| PC-239 | RUN | H behind P and advanced H2; clean fixture -> break G-239: return request or creation baseline as observed HEAD. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionSourceProof` | `observedHead.ShouldBe(actualHead)` | pending |
| PC-240 | RUN | foreign real common directory with equal commit -> break G-240: omit common-directory comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerRepositoryIdentity` | `commonIdentityRefused.ShouldBeTrue()` | pending |
| PC-241 | RUN | foreign valid worktree admin directory -> break G-241: omit admin-directory comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerRepositoryIdentity` | `adminIdentityRefused.ShouldBeTrue()` | pending |
| PC-242 | RUN | same task/attempt/path but replaced BindingId -> break G-242: omit BindingId comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerTaskIdentity` | `bindingIdentityRefused.ShouldBeTrue()` | pending |
| PC-243 | RUN | canonical, reusable slot, shared or borrowed tree with crafted ordinary fields -> break G-243: accept unsupported workspace kind. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPathIdentity` | `ordinaryKindRefused.ShouldBeTrue()` | pending |
| PC-244 | RUN | valid proof for different destination -> break G-244: omit destination-fingerprint comparison. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `proofEndpointRefused.ShouldBeTrue()` | pending |
| PC-245 | RUN | H exists only on unique pushed task branch -> break G-245: use task ref instead of configured destination ref. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `unlandedTreeExists.ShouldBeTrue()` | pending |
| PC-246 | RUN | confirmed unrelated landing with equal resulting tree bytes -> break G-246: accept mapping for other task/source operation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `mappingRefused.ShouldBeTrue()` | pending |
| PC-247 | RUN | remote target rewound after proof or read fails -> break G-247: trust stale observed target without fresh remote check. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerPreservationProof` | `freshProofRefused.ShouldBeTrue()` | pending |
| PC-248 | RUN | nonignored untracked source only -> break G-248: omit untracked inventory from initial status. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerDirtySource` | `untrackedInitialRefused.ShouldBeTrue()` | pending |
| PC-249 | RUN | nested .git repository under otherwise disposable tree -> break G-249: ignore nested repository detection. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `nestedTreeExists.ShouldBeTrue()` | pending |
| PC-250 | RUN | real git worktree lock on otherwise eligible mirror -> break G-250: ignore locked registration. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `lockedTreeExists.ShouldBeTrue()` | pending |
| PC-251 | RUN | known evidence with non-CardDone disposition -> break G-251: allow evidence disposal for ordinary/unknown disposition. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `evidenceBytes.ShouldBe(originalBytes)` | pending |
| PC-252 | RUN | existing link/dangling link to outside owned sentinel -> break G-252: ignore links in ignored-content inventory. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `initialLinkRefused.ShouldBeTrue()` | pending |
| PC-253 | RUN | pooled process at mirror with terminal task row -> break G-253: exclude pooled owners from use census. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerProcessOwnerHolds` | `pooledOwnerTreeExists.ShouldBeTrue()` | pending |
| PC-254 | RUN | new owner appears after initial ownership read -> break G-254: omit final owner validation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateOwnerTreeExists.ShouldBeTrue()` | pending |
| PC-255 | RUN | protected ignored file appears after initial inventory -> break G-255: reuse initial ignored inventory at deletion. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateProtectedBytes.ShouldBe(originalBytes)` | pending |
| PC-256 | RUN | swap registration/admin identity after initial inspection -> break G-256: reuse initial registration at deletion. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateRegistrationRefused.ShouldBeTrue()` | pending |
| PC-257 | RUN | remote containment disappears before mutation -> break G-257: omit final preservation validation. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateProofRefused.ShouldBeTrue()` | pending |
| PC-258 | RUN | real owned clean redundant mirror, CardDone evidence -> break G-258: return success receipt without executing no-follow delete. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical` | `directoryGone.ShouldBeTrue()` | pending |
| PC-259 | RUN | owned mirror and unrelated stale registration -> break G-259: replace targeted unregister with repository-wide prune. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical` | `foreignRegistrationExists.ShouldBeTrue()` | pending |
| PC-260 | RUN | snapshot local and origin refs before successful mirror removal -> break G-260: delete owned task ref after remove. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalEligiblePhysical` | `refsAfter.ShouldBe(refsBefore)` | pending |
| PC-261 | RUN | observer paused before rename reads external record -> break G-261: persist set-aside path only after rename. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalComponentRecovery` | `setAsideIdentityVisibleBeforeRename.ShouldBeTrue()` | pending |
| PC-262 | RUN | crash after rename, unregister, partial delete, receipt write; each component pair -> break G-262: treat directoryGone alone as completed removal. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalComponentRecovery` | `completedWithRegistrationPresent.ShouldBeFalse()` | pending |
| PC-263 | RUN | restart with partial receipt and spent allowance -> break G-263: reset allowance while resuming journal. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalComponentRecovery` | `commandAllowanceAfter.ShouldBe(commandAllowanceBefore)` | pending |
| PC-264 | RUN | original absent, no journal or foreign journal -> break G-264: return Complete on Directory.Exists=false. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalOriginalReplacement` | `absenceWithoutReceiptComplete.ShouldBeFalse()` | pending |
| PC-265 | RUN | matching set-aside journal plus newly created original path -> break G-265: delete original pathname while resuming set-aside. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalOriginalReplacement` | `replacementBytes.ShouldBe(originalReplacementBytes)` | pending |
| PC-266 | RUN | recorded sibling name occupied by foreign content -> break G-266: delete pre-existing sibling to make room. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalOriginalReplacement` | `foreignAsideBytes.ShouldBe(originalForeignAsideBytes)` | pending |
| PC-267 | RUN | default runtime/dispatcher with Execute=true equivalent fixture settings -> break G-267: auto-construct guarded remover in dispatcher. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportDefaultInert` | `defaultRemovalEffects.ShouldBe(0)` | pending |
| PC-268 | RUN | capability query and registration on unconfigured then configured runtime -> break G-268: advertise guarded capability unconditionally. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportDefaultInert` | `defaultAdvertisesGuarded.ShouldBeFalse()` | pending |
| PC-269 | RUN | one missing binding payload sent via real guarded frame -> break G-269: default malformed guarded body into a valid command. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportRequiredPayload` | `malformedDispatchAccepted.ShouldBeFalse()` | pending |
| PC-270 | RUN | two distinct transport RequestIds carrying same removal operation -> break G-270: return a newly allocated receipt on second transport request. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportDispatchReplay` | `replayedReceiptBytes.ShouldBe(firstReceiptBytes)` | pending |
| PC-271 | MIR | Queued/Working/Blocked with remaining facts valid -> break G-271: omit terminal-task predicate. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility` | `remoteCommandsIssued.ShouldBe(0)` | pending |
| PC-272 | MIR | just below/at configured settling duration using fake clock -> break G-272: omit settling-floor predicate. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility` | `beforeFloorCommands.ShouldBe(0)` | pending |
| PC-273 | MIR | change attempt or binding after inspection before admission -> break G-273: admit stale captured attempt/binding. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility` | `staleAdmissionCommands.ShouldBe(0)` | pending |
| PC-274 | MIR | open repair/continuation using mirror with terminal owner -> break G-274: ignore active workspace consumer. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility` | `consumerHeldCommands.ShouldBe(0)` | pending |
| PC-275 | MIR | pending land/repair/recovery request -> break G-275: omit outstanding-recovery predicate. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionCurrentEligibility` | `recoveryDebtCommands.ShouldBe(0)` | pending |
| PC-276 | MIR | missing/digest-changed canonical Result or path inside deletion root -> break G-276: accept existing report path without verifying external bytes. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `unpreservedReportCommands.ShouldBe(0)` | pending |
| PC-277 | MIR | deliverable only below local/mirror/set-aside root; missing external variant -> break G-277: accept File.Exists without external artifact proof. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `unpreservedDeliverableCommands.ShouldBe(0)` | pending |
| PC-278 | MIR | change Result/deliverable pointers after initial artifact verification -> break G-278: reuse old artifact digest at intent. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `changedArtifactCommands.ShouldBe(0)` | pending |
| PC-279 | MIR | pause outbound send and observe from independent DB connection -> break G-279: send before intent transaction commits. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionIntentOrdering` | `intentCommittedAtSend.ShouldBeTrue()` | pending |
| PC-280 | MIR | intent commits, then reopen, then reconnect replay -> break G-280: revoke already-admitted exact mirror on reopen. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionIntentOrdering` | `admittedMirrorDirectoryGone.ShouldBeTrue()` | pending |
| PC-281 | MIR | hold runner inspection/send and execute independent card writer -> break G-281: hold admission row lock across network await. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionIntentOrdering` | `cardWriteCompletedWhileIoHeld.ShouldBeTrue()` | pending |
| PC-282 | MIR | matching IDs with different request digest -> break G-282: omit receipt digest comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `digestMismatchComplete.ShouldBeFalse()` | pending |
| PC-283 | MIR | receipt-save fault before state update -> break G-283: mark Complete in separate earlier save. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `completeWithoutImportedReceipt.ShouldBeFalse()` | pending |
| PC-284 | MIR | observer reloads DB when invalidation received -> break G-284: emit invalidation before saving component facts. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `durableOutcomeVisibleAtEvent.ShouldBeTrue()` | pending |
| PC-285 | MIR | hold runner mutation turn with owned earlier work, then release; ready variant -> break G-285: drop guarded work queued while mutation turn is busy. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryQueuesBusyAndReady` | `busyRecipientDirectoryGone.ShouldBeTrue()` | pending |
| PC-286 | MIR | pre-send request-limit/write failure and queued disconnect -> break G-286: clear intent/preparation debt when transport did not send. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `recoveredRecipientReceiptExists.ShouldBeTrue()` | pending |
| PC-287 | MIR | real completed receipt then server save fails and restarts -> break G-287: erase operation identity after save failure. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `postRestartOperationId.ShouldBe(originalOperationId)` | pending |
| PC-288 | MIR | reconnect same runner name with different persistent store -> break G-288: retarget old intent to new store. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `oldEndpointComplete.ShouldBeFalse()` | pending |
| PC-289 | MIR | due-1 tick then due tick after fresh coordinator -> break G-289: ignore persisted NextAttemptAt on retry. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `callsBeforeDue.ShouldBe(0)` | pending |
| PC-290 | MIR | real Program fixture with Execute=true and populated eligible endpoints -> break G-290: register coordinator/executor into production services. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryInertServerComposition` | `productionCoordinatorResolved.ShouldBeFalse()` | pending |
| PC-291 | MIR | invoke existing sweep on populated eligible fixture with Execute=true -> break G-291: add mirror cleanup call to WorktreeResidueSweepService. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryInertServerComposition` | `automaticMirrorCommands.ShouldBe(0)` | pending |
| PC-292 | MIR | enumerate default endpoint data sources and probe candidate route -> break G-292: map public completed-card mirror cleanup endpoint. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryInertServerComposition` | `publicGuardedCleanupRoutes.ShouldBeEmpty()` | pending |
| PC-293 | MIR | synthetic credential URL/process-env/secret-content canaries in failed inputs -> break G-293: log raw guarded payload in refusal detail. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `diagnosticsContainCanary.ShouldBeFalse()` | pending |
| PC-300 | WIRE | required.CardId: remove only CardId from valid bound JSON -> break G-300: remove the CardId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.CardId")` | pending |
| PC-301 | WIRE | required.DoneRevisionId: remove only DoneRevisionId from valid bound JSON -> break G-301: remove the DoneRevisionId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.DoneRevisionId")` | pending |
| PC-302 | WIRE | required.TargetId: remove only TargetId from valid bound JSON -> break G-302: remove the TargetId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.TargetId")` | pending |
| PC-303 | WIRE | required.EndpointId: remove only EndpointId from valid bound JSON -> break G-303: remove the EndpointId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.EndpointId")` | pending |
| PC-304 | WIRE | required.TaskId: remove only TaskId from valid bound JSON -> break G-304: remove the TaskId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.TaskId")` | pending |
| PC-305 | WIRE | required.Attempt: remove only Attempt from valid bound JSON -> break G-305: remove the Attempt required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.Attempt")` | pending |
| PC-306 | WIRE | required.BindingId: remove only BindingId from valid bound JSON -> break G-306: remove the BindingId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.BindingId")` | pending |
| PC-307 | WIRE | required.PreparationId: remove only PreparationId from valid bound JSON -> break G-307: remove the PreparationId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.PreparationId")` | pending |
| PC-308 | WIRE | required.RunnerId: remove only RunnerId from valid bound JSON -> break G-308: remove the RunnerId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.RunnerId")` | pending |
| PC-309 | WIRE | required.RunnerStoreId: remove only RunnerStoreId from valid bound JSON -> break G-309: remove the RunnerStoreId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.RunnerStoreId")` | pending |
| PC-310 | WIRE | required.RepositoryIdentity: remove only RepositoryIdentity from valid bound JSON -> break G-310: remove the RepositoryIdentity required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.RepositoryIdentity")` | pending |
| PC-311 | WIRE | required.CommonDirectory: remove only CommonDirectory from valid bound JSON -> break G-311: remove the CommonDirectory required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.CommonDirectory")` | pending |
| PC-312 | WIRE | required.GitDirectory: remove only GitDirectory from valid bound JSON -> break G-312: remove the GitDirectory required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.GitDirectory")` | pending |
| PC-313 | WIRE | required.WorktreePath: remove only WorktreePath from valid bound JSON -> break G-313: remove the WorktreePath required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.WorktreePath")` | pending |
| PC-314 | WIRE | required.SourceFullRef: remove only SourceFullRef from valid bound JSON -> break G-314: remove the SourceFullRef required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.SourceFullRef")` | pending |
| PC-315 | WIRE | required.CreationBaselineSha: remove only CreationBaselineSha from valid bound JSON -> break G-315: remove the CreationBaselineSha required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.CreationBaselineSha")` | pending |
| PC-316 | WIRE | required.OperationId: remove only OperationId from valid bound JSON -> break G-316: remove the OperationId required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.OperationId")` | pending |
| PC-317 | WIRE | required.ExpectedHeadSha: remove only ExpectedHeadSha from valid bound JSON -> break G-317: remove the ExpectedHeadSha required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ExpectedHeadSha")` | pending |
| PC-318 | WIRE | required.DestinationFingerprint: remove only DestinationFingerprint from valid bound JSON -> break G-318: remove the DestinationFingerprint required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.DestinationFingerprint")` | pending |
| PC-319 | WIRE | required.DestinationRef: remove only DestinationRef from valid bound JSON -> break G-319: remove the DestinationRef required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.DestinationRef")` | pending |
| PC-320 | WIRE | required.ObservedTargetSha: remove only ObservedTargetSha from valid bound JSON -> break G-320: remove the ObservedTargetSha required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ObservedTargetSha")` | pending |
| PC-321 | WIRE | required.ProofKind: remove only ProofKind from valid bound JSON -> break G-321: remove the ProofKind required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ProofKind")` | pending |
| PC-322 | WIRE | required.ProofIdentity: remove only ProofIdentity from valid bound JSON -> break G-322: remove the ProofIdentity required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ProofIdentity")` | pending |
| PC-323 | WIRE | required.PreservedSourceSha: remove only PreservedSourceSha from valid bound JSON -> break G-323: remove the PreservedSourceSha required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.PreservedSourceSha")` | pending |
| PC-324 | WIRE | required.ReportDigest: remove only ReportDigest from valid bound JSON -> break G-324: remove the ReportDigest required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ReportDigest")` | pending |
| PC-325 | WIRE | required.ArtifactProofDigest: remove only ArtifactProofDigest from valid bound JSON -> break G-325: remove the ArtifactProofDigest required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.ArtifactProofDigest")` | pending |
| PC-326 | WIRE | required.DisposalPolicyDigest: remove only DisposalPolicyDigest from valid bound JSON -> break G-326: remove the DisposalPolicyDigest required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.DisposalPolicyDigest")` | pending |
| PC-327 | WIRE | required.Disposition: remove only Disposition from valid bound JSON -> break G-327: remove the Disposition required-value validation. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireRequiredFieldsAndVersion` | `validation.IsValid.ShouldBeFalse("required.Disposition")` | pending |
| PC-340 | WIRE | digest.SchemaVersion: replace only SchemaVersion with a second valid value -> break G-340: exclude SchemaVersion from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.SchemaVersion")` | pending |
| PC-341 | WIRE | digest.CardId: replace only CardId with a second valid value -> break G-341: exclude CardId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.CardId")` | pending |
| PC-342 | WIRE | digest.DoneRevisionId: replace only DoneRevisionId with a second valid value -> break G-342: exclude DoneRevisionId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.DoneRevisionId")` | pending |
| PC-343 | WIRE | digest.TargetId: replace only TargetId with a second valid value -> break G-343: exclude TargetId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.TargetId")` | pending |
| PC-344 | WIRE | digest.EndpointId: replace only EndpointId with a second valid value -> break G-344: exclude EndpointId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.EndpointId")` | pending |
| PC-345 | WIRE | digest.TaskId: replace only TaskId with a second valid value -> break G-345: exclude TaskId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.TaskId")` | pending |
| PC-346 | WIRE | digest.Attempt: replace only Attempt with a second valid value -> break G-346: exclude Attempt from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.Attempt")` | pending |
| PC-347 | WIRE | digest.BindingId: replace only BindingId with a second valid value -> break G-347: exclude BindingId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.BindingId")` | pending |
| PC-348 | WIRE | digest.PreparationId: replace only PreparationId with a second valid value -> break G-348: exclude PreparationId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.PreparationId")` | pending |
| PC-349 | WIRE | digest.RunnerId: replace only RunnerId with a second valid value -> break G-349: exclude RunnerId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.RunnerId")` | pending |
| PC-350 | WIRE | digest.RunnerStoreId: replace only RunnerStoreId with a second valid value -> break G-350: exclude RunnerStoreId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.RunnerStoreId")` | pending |
| PC-351 | WIRE | digest.RepositoryIdentity: replace only RepositoryIdentity with a second valid value -> break G-351: exclude RepositoryIdentity from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.RepositoryIdentity")` | pending |
| PC-352 | WIRE | digest.CommonDirectory: replace only CommonDirectory with a second valid value -> break G-352: exclude CommonDirectory from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.CommonDirectory")` | pending |
| PC-353 | WIRE | digest.GitDirectory: replace only GitDirectory with a second valid value -> break G-353: exclude GitDirectory from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.GitDirectory")` | pending |
| PC-354 | WIRE | digest.WorktreePath: replace only WorktreePath with a second valid value -> break G-354: exclude WorktreePath from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.WorktreePath")` | pending |
| PC-355 | WIRE | digest.SourceFullRef: replace only SourceFullRef with a second valid value -> break G-355: exclude SourceFullRef from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.SourceFullRef")` | pending |
| PC-356 | WIRE | digest.CreationBaselineSha: replace only CreationBaselineSha with a second valid value -> break G-356: exclude CreationBaselineSha from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.CreationBaselineSha")` | pending |
| PC-357 | WIRE | digest.OperationId: replace only OperationId with a second valid value -> break G-357: exclude OperationId from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.OperationId")` | pending |
| PC-358 | WIRE | digest.ExpectedHeadSha: replace only ExpectedHeadSha with a second valid value -> break G-358: exclude ExpectedHeadSha from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ExpectedHeadSha")` | pending |
| PC-359 | WIRE | digest.DestinationFingerprint: replace only DestinationFingerprint with a second valid value -> break G-359: exclude DestinationFingerprint from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.DestinationFingerprint")` | pending |
| PC-360 | WIRE | digest.DestinationRef: replace only DestinationRef with a second valid value -> break G-360: exclude DestinationRef from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.DestinationRef")` | pending |
| PC-361 | WIRE | digest.ObservedTargetSha: replace only ObservedTargetSha with a second valid value -> break G-361: exclude ObservedTargetSha from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ObservedTargetSha")` | pending |
| PC-362 | WIRE | digest.ProofKind: replace only ProofKind with a second valid value -> break G-362: exclude ProofKind from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ProofKind")` | pending |
| PC-363 | WIRE | digest.ProofIdentity: replace only ProofIdentity with a second valid value -> break G-363: exclude ProofIdentity from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ProofIdentity")` | pending |
| PC-364 | WIRE | digest.PreservedSourceSha: replace only PreservedSourceSha with a second valid value -> break G-364: exclude PreservedSourceSha from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.PreservedSourceSha")` | pending |
| PC-365 | WIRE | digest.ReportDigest: replace only ReportDigest with a second valid value -> break G-365: exclude ReportDigest from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ReportDigest")` | pending |
| PC-366 | WIRE | digest.ArtifactProofDigest: replace only ArtifactProofDigest with a second valid value -> break G-366: exclude ArtifactProofDigest from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ArtifactProofDigest")` | pending |
| PC-367 | WIRE | digest.DisposalPolicyDigest: replace only DisposalPolicyDigest with a second valid value -> break G-367: exclude DisposalPolicyDigest from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.DisposalPolicyDigest")` | pending |
| PC-368 | WIRE | digest.Disposition: replace only Disposition with a second valid value -> break G-368: exclude Disposition from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.Disposition")` | pending |
| PC-369 | WIRE | digest.RebasedSourceSha: replace only RebasedSourceSha with a second valid value -> break G-369: exclude RebasedSourceSha from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.RebasedSourceSha")` | pending |
| PC-370 | WIRE | digest.ArtifactPointers: replace only ArtifactPointers with a second valid value -> break G-370: exclude ArtifactPointers from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.ArtifactPointers")` | pending |
| PC-371 | WIRE | digest.PolicyPatterns: replace only PolicyPatterns with a second valid value -> break G-371: exclude PolicyPatterns from canonical digest input. | `/*/*/CompletedCardWorkspaceContractTests/C1032_WireDigestFieldCoverage` | `changedDigest.ShouldNotBe(originalDigest, "digest.PolicyPatterns")` | pending |
| PC-380 | MIR | receipt.SchemaVersion: replace only SchemaVersion on genuine recipient receipt; missing/null/unknown arm -> break G-380: omit receipt SchemaVersion comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.SchemaVersion")` | pending |
| PC-381 | MIR | receipt.CardId: replace only CardId on genuine recipient receipt; missing/null/unknown arm -> break G-381: omit receipt CardId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.CardId")` | pending |
| PC-382 | MIR | receipt.DoneRevisionId: replace only DoneRevisionId on genuine recipient receipt; missing/null/unknown arm -> break G-382: omit receipt DoneRevisionId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.DoneRevisionId")` | pending |
| PC-383 | MIR | receipt.TargetId: replace only TargetId on genuine recipient receipt; missing/null/unknown arm -> break G-383: omit receipt TargetId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.TargetId")` | pending |
| PC-384 | MIR | receipt.TaskId: replace only TaskId on genuine recipient receipt; missing/null/unknown arm -> break G-384: omit receipt TaskId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.TaskId")` | pending |
| PC-385 | MIR | receipt.BindingId: replace only BindingId on genuine recipient receipt; missing/null/unknown arm -> break G-385: omit receipt BindingId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.BindingId")` | pending |
| PC-386 | MIR | receipt.PreparationId: replace only PreparationId on genuine recipient receipt; missing/null/unknown arm -> break G-386: omit receipt PreparationId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.PreparationId")` | pending |
| PC-387 | MIR | receipt.RunnerId: replace only RunnerId on genuine recipient receipt; missing/null/unknown arm -> break G-387: omit receipt RunnerId comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.RunnerId")` | pending |
| PC-388 | MIR | receipt.RepositoryIdentity: replace only RepositoryIdentity on genuine recipient receipt; missing/null/unknown arm -> break G-388: omit receipt RepositoryIdentity comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.RepositoryIdentity")` | pending |
| PC-389 | MIR | receipt.CommonDirectory: replace only CommonDirectory on genuine recipient receipt; missing/null/unknown arm -> break G-389: omit receipt CommonDirectory comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.CommonDirectory")` | pending |
| PC-390 | MIR | receipt.GitDirectory: replace only GitDirectory on genuine recipient receipt; missing/null/unknown arm -> break G-390: omit receipt GitDirectory comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.GitDirectory")` | pending |
| PC-391 | MIR | receipt.WorktreePath: replace only WorktreePath on genuine recipient receipt; missing/null/unknown arm -> break G-391: omit receipt WorktreePath comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.WorktreePath")` | pending |
| PC-392 | MIR | receipt.SourceFullRef: replace only SourceFullRef on genuine recipient receipt; missing/null/unknown arm -> break G-392: omit receipt SourceFullRef comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.SourceFullRef")` | pending |
| PC-393 | MIR | receipt.CreationBaselineSha: replace only CreationBaselineSha on genuine recipient receipt; missing/null/unknown arm -> break G-393: omit receipt CreationBaselineSha comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.CreationBaselineSha")` | pending |
| PC-394 | MIR | receipt.ExpectedHeadSha: replace only ExpectedHeadSha on genuine recipient receipt; missing/null/unknown arm -> break G-394: omit receipt ExpectedHeadSha comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.ExpectedHeadSha")` | pending |
| PC-395 | MIR | receipt.PreservationProofDigest: replace only PreservationProofDigest on genuine recipient receipt; missing/null/unknown arm -> break G-395: omit receipt PreservationProofDigest comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.PreservationProofDigest")` | pending |
| PC-396 | MIR | receipt.ArtifactProofDigest: replace only ArtifactProofDigest on genuine recipient receipt; missing/null/unknown arm -> break G-396: omit receipt ArtifactProofDigest comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.ArtifactProofDigest")` | pending |
| PC-397 | MIR | receipt.DisposalPolicyDigest: replace only DisposalPolicyDigest on genuine recipient receipt; missing/null/unknown arm -> break G-397: omit receipt DisposalPolicyDigest comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.DisposalPolicyDigest")` | pending |
| PC-398 | MIR | receipt.Disposition: replace only Disposition on genuine recipient receipt; missing/null/unknown arm -> break G-398: omit receipt Disposition comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.Disposition")` | pending |
| PC-399 | MIR | receipt.SetAsideIdentity: replace only SetAsideIdentity on genuine recipient receipt; missing/null/unknown arm -> break G-399: omit receipt SetAsideIdentity comparison. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_ReceiptCompleteBoundReceipt` | `mirrorComplete.ShouldBeFalse("receipt.SetAsideIdentity")` | pending |
| PC-400 | LEG-R | forward child commit; expected published tip -> break G-400: omit push invocation. | `/*/*/RunnerWorkspaceServiceTests/Publish_pushes_only_own_fast_forward_branch` | `originTip.ShouldBe(tip)` | pending |
| PC-401 | LEG-R | diverged tip -> break G-401: classify diverged as descends. | `/*/*/RunnerWorkspaceServiceTests/Publish_refuses_a_rebased_tip_without_pushing` | `answer.Pushed.ShouldBeFalse()` | pending |
| PC-402 | LEG-R | equal tip with untracked source -> break G-402: hardcode Dirty=false. | `/*/*/RunnerWorkspaceServiceTests/Publish_equal_tip_is_not_pushed_and_reports_dirty_tree` | `answer.Dirty.ShouldBeTrue()` | pending |
| PC-403 | LEG-R | foreign path and branch -> break G-403: remove target validation before Git. | `/*/*/RunnerWorkspaceServiceTests/Publish_refuses_foreign_path_and_branch_before_git` | `starts.ShouldBeEmpty()` | pending |
| PC-404 | LEG-R | detached worktree -> break G-404: accept detached symbolic head. | `/*/*/RunnerWorkspaceServiceTests/Publish_refuses_detached_head` | `answer.Refusal.ShouldBe("not_on_branch")` | pending |
| PC-405 | LEG-R | MERGE_HEAD exists -> break G-405: omit sequencer check. | `/*/*/RunnerWorkspaceServiceTests/Publish_refuses_an_active_sequencer_before_push` | `answer.Refusal.ShouldBe("sequencer_active")` | pending |
| PC-406 | LEG-R | non-FF remote rejection -> break G-406: report push as successful regardless of exit. | `/*/*/RunnerWorkspaceServiceTests/Publish_reports_rejected_non_fast_forward_without_exposing_origin` | `answer.Pushed.ShouldBeFalse()` | pending |
| PC-407 | LEG-R | ancestor tip behind remote -> break G-407: map behind to equal. | `/*/*/RunnerWorkspaceServiceTests/Publish_classifies_a_tip_behind_the_advertised_remote` | `answer.Relation.ShouldBe("behind")` | pending |
| PC-408 | LEG-R | unpublished commit without Force, then legacy Force positive -> break G-408: omit legacy unpublished-work guard. | `/*/*/RunnerWorkspaceServiceTests/Remove_refuses_committed_unpublished_tip_unless_forced` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` | pending |
| PC-409 | LEG-R | equal published SHA -> break G-409: refuse exact published tip. | `/*/*/RunnerWorkspaceServiceTests/Remove_allows_a_published_tip` | `removed.Removed.ShouldBeTrue()` | pending |
| PC-410 | LEG-R | mirror behind published SHA -> break G-410: require equality in legacy removal. | `/*/*/RunnerWorkspaceServiceTests/Remove_allows_a_tip_which_is_ancestor_of_published_sha` | `removed.Removed.ShouldBeTrue()` | pending |
| PC-411 | LEG-R | secondary clone -> break G-411: run remove in primary checkout. | `/*/*/RunnerWorkspaceServiceTests/Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout` | `starts.ShouldContain(matchingSecondaryRemove)` | pending |
| PC-412 | LEG-R | unadmitted repository -> break G-412: skip clone-source admission. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_a_repository_outside_the_allowed_clone_sources` | `starts.ShouldBeEmpty()` | pending |
| PC-413 | LEG-R | synthetic credential URL -> break G-413: echo unredacted invalid request URL in exception. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_credentialed_request_without_echoing_credentials` | `refused.Message.ShouldNotContain(secret)` | pending |
| PC-414 | LEG-R | secondary path has other origin -> break G-414: skip origin identity check. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_an_existing_checkout_whose_origin_is_another_repository` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` | pending |
| PC-415 | LEG-R | credentialed existing origin -> break G-415: include raw origin in mismatch text. | `/*/*/RunnerWorkspaceServiceTests/Mirror_mismatch_does_not_echo_credentialed_origin` | `refused.Message.ShouldNotContain(secret)` | pending |
| PC-416 | LEG-R | failing dry-run probe -> break G-416: ignore failed push probe. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` | pending |
| PC-417 | LEG-R | successful dry-run -> break G-417: remove --dry-run from push probe. | `/*/*/RunnerWorkspaceServiceTests/Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds` | `start.ArgumentList.Take(6).ShouldBe(expectedProbeArgv)` | pending |
| PC-418 | LEG-R | normal mirror -> break G-418: create detached mirror. | `/*/*/RunnerWorkspaceServiceTests/Mirror_creates_worktree_on_branch_at_sha` | `actualBranch.ShouldBe(Scratch.Branch)` | pending |
| PC-419 | LEG-R | known but wrong fetched SHA -> break G-419: omit fetched-tip equality. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_sha_mismatch` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` | pending |
| PC-420 | LEG-R | absent and populated destination variants -> break G-420: refuse absent repository rather than provisioning. | `/*/*/RunnerWorkspaceServiceTests/Mirror_clones_repository_when_absent` | `mirrorHead.ShouldBe(scratch.Sha)` | pending |
| PC-421 | LEG-R | child-start exception and caller cancellation -> break G-421: let start IOException escape without typed mapping. | `/*/*/RunnerWorkspaceServiceTests/Mirror_failure_is_admission_error_not_crash` | `refused.Code.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget)` | pending |
| PC-422 | LEG-R | invalid name with valid branch/SHA -> break G-422: skip mirror-name validation. | `/*/*/RunnerWorkspaceServiceTests/Mirror_refuses_a_name_or_sha_it_did_not_shape` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` | pending |
| PC-423 | LEG-R | untracked unique source -> break G-423: omit dirty-status guard. | `/*/*/RunnerWorkspaceServiceTests/Remove_refuses_dirty_tree` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` | pending |
| PC-424 | LEG-R | root/outside path -> break G-424: skip managed-root validation. | `/*/*/RunnerWorkspaceServiceTests/Remove_refuses_a_path_outside_the_worktree_root` | `Should.ThrowAsync<PhoneHomeAdmissionException>(productionCall)` | pending |
| PC-425 | LEG-R | /etc removal -> break G-425: allow foreign Cwd into workspace operation. | `/*/*/PhoneHomeCommandDispatcherTests/Workspace_ops_are_admitted_only_under_allowed_cwd` | `removeOutside.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget)` | pending |
| PC-426 | LEG-R | foreign repository request -> break G-426: skip repository admission. | `/*/*/PhoneHomeCommandDispatcherTests/Workspace_mirror_naming_an_unadmitted_repository_is_refused_before_any_git_runs` | `reply.ErrorCode.ShouldBe(PhoneHomeProblemTypes.RepositoryNotAdmitted)` | pending |
| PC-427 | LEG-R | foreign publish path -> break G-427: skip allowed-Cwd routing guard. | `/*/*/PhoneHomeCommandDispatcherTests/Workspace_publish_route_rejects_a_foreign_target_before_git` | `reply.ErrorCode.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget)` | pending |
| PC-428 | LEG-S | SSH/HTTPS origin normalization -> break G-428: drop repository identity from successful push result. | `/*/*/RemoteWorktreeMirrorTests/Push_reads_the_desktop_origin_and_the_mirror_request_carries_its_https_identity` | `push.Repository.ShouldBe(expectedHttpsIdentity)` | pending |
| PC-429 | LEG-S | remote lookup fails -> break G-429: continue push on failed origin read. | `/*/*/RemoteWorktreeMirrorTests/Push_refuses_when_the_desktop_origin_cannot_be_read` | `push.Pushed.ShouldBeFalse()` | pending |
| PC-430 | LEG-S | baseline ancestor of observed SHA -> break G-430: return Synchronized without merge. | `/*/*/RemoteWorktreeMirrorTests/Sync_fast_forwards_desktop_worktree` | `git.Commands.ShouldContain(expectedFfCommand)` | pending |
| PC-431 | LEG-S | diverged desktop -> break G-431: permit merge after failed ancestry check. | `/*/*/RemoteWorktreeMirrorTests/Non_fast_forward_sync_is_refused` | `result.State.ShouldBe(RemoteSettlementSyncState.Refused)` | pending |
| PC-432 | LEG-S | dirty tracked desktop -> break G-432: omit dirty check. | `/*/*/RemoteWorktreeMirrorTests/Dirty_desktop_tree_is_never_fast_forwarded_over` | `progress.Observations.ShouldBe(0)` | pending |
| PC-433 | LEG-S | remote push exit128 -> break G-433: mark push successful regardless of exit. | `/*/*/RemoteWorktreeMirrorTests/Push_failure_reports_and_does_not_invent_a_sha` | `result.Pushed.ShouldBeFalse()` | pending |
| PC-434 | LEG-S | successful prep push -> break G-434: omit configured push options/task tag. | `/*/*/RemoteWorktreeMirrorTests/Push_reports_the_head_it_pushed` | `git.LastPushOptions.ShouldNotBeNull()` | pending |
| PC-435 | LEG-S | known full task id -> break G-435: use nine characters in mirror name. | `/*/*/RemoteWorktreeMirrorTests/Mirror_name_is_the_dispatchers_own_short_form` | `mirrorName.ShouldBe("task-deadbeef")` | pending |
| PC-500 | RUN | valid complete payload with one stale digest -> break G-500: omit recomputed digest check before journal admission. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportRequiredPayload` | `payloadDigestAccepted.ShouldBeFalse()` | pending |
| PC-501 | RUN | commit unique change after initial read -> break G-501: omit final HEAD equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateHeadRefused.ShouldBeTrue()` | pending |
| PC-502 | RUN | switch branch at same HEAD after initial read -> break G-502: omit final symbolic-ref equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateBranchRefused.ShouldBeTrue()` | pending |
| PC-503 | RUN | late nonignored untracked file -> break G-503: omit final untracked status. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateUntrackedBytes.ShouldBe(originalBytes)` | pending |
| PC-504 | RUN | durable intent changed after initial read -> break G-504: skip final journal identity read. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateIntentRefused.ShouldBeTrue()` | pending |
| PC-505 | RUN | nested registered worktree without a nested repository sentinel masking the check -> break G-505: omit nested-registration check. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerIgnoredPolicy` | `nestedRegistrationPreserved.ShouldBeTrue()` | pending |
| PC-506 | MIR | matching report bytes/digest only inside mirror or set-aside -> break G-506: omit report-root exclusion. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `internalReportCommands.ShouldBe(0)` | pending |
| PC-507 | MIR | external file exists with truncated Result -> break G-507: accept external report based on existence only. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `truncatedReportCommands.ShouldBe(0)` | pending |
| PC-508 | MIR | same-looking desktop path exists but runner artifact does not -> break G-508: use desktop File.Exists for remote artifact proof. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_AdmissionArtifactSurvival` | `guardedRemovalCommands.ShouldBe(0)` | pending |
| PC-509 | MIR | genuine receipt registration gone, directory remains -> break G-509: treat registration success as complete. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_AckIsNotRemoval` | `mirrorComplete.ShouldBeFalse("directory-remains")` | pending |
| PC-510 | MIR | genuine receipt directory gone, registration remains -> break G-510: treat directory success as complete. | `/*/*/CardWorktreeMirrorCleanupTests/C1017_AckIsNotRemoval` | `mirrorComplete.ShouldBeFalse("registration-remains")` | pending |
| PC-511 | MIR | task/path/proof/policy changes after persisted intent -> break G-511: rebuild replay command from current task. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `replayedRequestBytes.ShouldBe(firstRequestBytes)` | pending |
| PC-512 | MIR | move wall clock backward with unchanged monotonic deadline budget -> break G-512: compute timeout from wall-clock deadline. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryCrashHandoffs` | `elapsedBudgetExceeded.ShouldBeFalse()` | pending |
| PC-513 | MIR | default options from production configuration -> break G-513: set default WorktreeResidueSettings.Execute=true. | `/*/*/CardWorktreeMirrorCleanupTests/C1032_RecoveryInertServerComposition` | `defaultExecute.ShouldBeFalse()` | pending |
| PC-514 | RUN | replace admin directory after initial read preserving other facts -> break G-514: omit final admin equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateAdminRefused.ShouldBeTrue()` | pending |
| PC-515 | RUN | common identity changes after initial read -> break G-515: omit final common equality. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateCommonRefused.ShouldBeTrue()` | pending |
| PC-516 | RUN | late worktree lock and separate late nested registration -> break G-516: omit final structural inventory read. | `/*/*/CompletedCardWorkspaceRemovalTests/C1017_RunnerFreshFinalInspection` | `lateStructuralRefused.ShouldBeTrue()` | pending |
| PC-517 | RUN | pause at reply writer and independently reopen runner journal -> break G-517: send success before durable receipt commit. | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportDispatchReplay` | `receiptVisibleBeforeReply.ShouldBeTrue()` | pending |
| PC-520 | CAP | capture.TaskId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-520: omit returned TaskId validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.TaskId")` | pending |
| PC-521 | CAP | capture.Attempt: one mismatch/missing member in otherwise valid runner creation receipt -> break G-521: omit returned Attempt validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.Attempt")` | pending |
| PC-522 | CAP | capture.PreparationId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-522: omit returned PreparationId validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.PreparationId")` | pending |
| PC-523 | CAP | capture.RunnerId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-523: omit returned RunnerId validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.RunnerId")` | pending |
| PC-524 | CAP | capture.RunnerStoreId: one mismatch/missing member in otherwise valid runner creation receipt -> break G-524: omit returned RunnerStoreId validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.RunnerStoreId")` | pending |
| PC-525 | CAP | capture.BindingId: empty ID, or a changed ID on exact repeated preparation receipt; a first nonempty runner-issued ID is accepted -> break G-525: omit returned BindingId validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.BindingId")` | pending |
| PC-526 | CAP | capture.RepositoryIdentity: one mismatch/missing member in otherwise valid runner creation receipt -> break G-526: omit returned RepositoryIdentity validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.RepositoryIdentity")` | pending |
| PC-527 | CAP | capture.WorktreePath: empty path, or changed path on repeated known binding; no desktop filesystem probe -> break G-527: omit returned WorktreePath validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.WorktreePath")` | pending |
| PC-528 | CAP | capture.SourceFullRef: one mismatch/missing member in otherwise valid runner creation receipt -> break G-528: omit returned SourceFullRef validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.SourceFullRef")` | pending |
| PC-529 | CAP | capture.CreationBaselineSha: one mismatch/missing member in otherwise valid runner creation receipt -> break G-529: omit returned CreationBaselineSha validation. | `/*/*/RemoteWorkspacePreparerTests/C1032_CaptureLostReplyRecovery` | `invalidBindingPersisted.ShouldBeFalse("capture.CreationBaselineSha")` | pending |
| PC-530 | RB | A writes creation intent and is cancelled; B with the same short id creates the mirror; A retries -> break G-530: adopt a directory when only presence and HEAD match. | `/*/*/RunnerWorkspaceBindingTests/C1032_ForeignOwnerAdoptionRefused` | `foreignAdopted.ShouldBeFalse()` | pending |
| PC-531 | RB | exact replay after ordinary worktree removal -> break G-531: return the completed binding when that generation's directory is absent. | `/*/*/RunnerWorkspaceBindingTests/C1032_AbsentMirrorReplayRefused` | `staleSuccess.ShouldBeFalse()` | pending |
| PC-532 | RB | request names another store or runner -> break G-532: copy the request store into a completed binding. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreMismatchRefused` | `wrongStoreCompleted.ShouldBeFalse()` | pending |
| PC-533 | CAP | store replaced between preparation and retry -> break G-533: reuse the old-store preparation. | `/*/*/RemoteWorkspacePreparerTests/C1032_StoreReplacementKeepsDebt` | `reusedOldStore.ShouldBeFalse()` | pending |
| PC-534 | JOIN | idle registered recipient -> break G-534: skip the owner-stamp write and still return success. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinIdleRecipient` | `stampMatches.ShouldBeTrue()` | pending |
| PC-535 | JOIN | second mirror while the mutation lock is held -> break G-535: skip MutationLock.WaitAsync. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinBusyRecipient` | `partialBindingLeft.ShouldBeFalse()` | pending |
| PC-536 | JOIN | server drops the waiter after enqueue -> break G-536: mint a new PreparationId on retry. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinCrashRetry` | `recoveredPreparationId.ShouldBe(firstPreparationId)` | pending |
| PC-537 | JOIN | colliding short id through the live queue -> break G-537: adopt when the owner stamp does not match. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinForeignOwner` | `foreignAdopted.ShouldBeFalse()` | pending |
| PC-538 | JOIN | exact replay after removal through the live queue -> break G-538: return success when the generation directory is absent. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinStaleBinding` | `staleCompleted.ShouldBeFalse()` | pending |
| PC-539 | JOIN | registration store file replaced before the request -> break G-539: bind the requested store and skip the mismatch throw. | `/*/*/PhoneHomeMirrorJoinTests/C1032_JoinStoreMismatch` | `wrongStoreCompleted.ShouldBeFalse()` | pending |
| PC-540 | RB | missing registration file, request names the nonempty custody guid -> break G-540: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("missing")` | pending |
| PC-541 | RB | empty registration file, request names the custody guid -> break G-541: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("empty")` | pending |
| PC-542 | RB | whitespace registration file, request names the custody guid -> break G-542: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("whitespace")` | pending |
| PC-543 | RB | garbled registration file, request names the custody guid -> break G-543: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("garbled")` | pending |
| PC-544 | RB | wrong-length registration guid, request names the custody guid -> break G-544: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("wrong-length")` | pending |
| PC-545 | RB | directory at the registration path, request names the custody guid -> break G-545: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("directory")` | pending |
| PC-546 | RB | mode-denied registration file, request names the custody guid -> break G-546: substitute RunnerStoreId and accept the bound mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `acceptedCustody.ShouldBeFalse("unreadable")` | pending |
| PC-547 | RB | readable registration guid differs and the request names custody -> break G-547: compare the request with RunnerStoreId instead of the file. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `custodyAccepted.ShouldBeFalse()` | pending |
| PC-548 | RB | readable registration guid matches the request and differs from custody -> break G-548: bind RunnerStoreId and refuse the registration guid. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `registeredStoreCompleted.ShouldBeTrue()` | pending |
| PC-549 | RB | unbound request and missing registration file -> break G-549: require a parsed registration guid before the legacy mirror. | `/*/*/RunnerWorkspaceBindingTests/C1032_StoreIdentityRefused*` | `legacyMirrorExists.ShouldBeTrue()` | pending |
| PC-550 | RB | omitted binding and missing registration file -> break G-550: fabricate a binding row from the unbound mirror path. | `/*/*/RunnerWorkspaceBindingTests/C1032_LegacyUnboundPathOnlyContract*` | `Directory.Exists(ledgerRoot).ShouldBeFalse()` | pending |

### Prior PC-551..560 (all pending)

| PC | Perturbation | Detecting filter | Expected red | Status |
|---|---|---|---|---|
| PC-551 | Omit present legacy removal admission | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemoveHeldLease*` | expected PhoneHomeAdmissionException is missing | pending |
| PC-552 | Move admission after missing-path prune | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyPruneHeldLease*` | expected PhoneHomeAdmissionException is missing | pending |
| PC-553 | Keep admission held after disposal | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemoveReleasedLease*` | actual removal refuses workspace_in_use | pending |
| PC-554 | Release removal admission before Git removal/prune | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemovalCreationOrders*` | creation must wait through removal and prune | pending |
| PC-555 | Require a configured gate on legacy remove | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyUnconfiguredRemoval*` | legacy real removal must succeed | pending |
| PC-556 | Leak removal admission on exception/cancellation | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemovalReleaseOnFailure*` | next admission/removal must succeed; bind exception and cancellation variants separately | pending |
| PC-557 | Ignore retained owners | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemovalRetainedOwner*` | expected PhoneHomeAdmissionException is missing; same-path and absent-sibling prune variants | pending |
| PC-558 | Omit verification removal admission | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_VerificationRemovalHeldLease*` | expected PhoneHomeAdmissionException is missing; bind present and absent variants separately | pending |
| PC-559 | Omit verification creation admission | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_VerificationCreationAdmission*` | GitStarts increases while lease held | pending |
| PC-560 | Omit journal/census hold checks | `/*/*/LegacyWorkspaceRemovalGateTests/C1032_LegacyRemovalJournalFence*` | expected PhoneHomeAdmissionException is missing; retiring, torn and incomplete-census variants | pending |

### Added 12 IDs / variants (all pending)

| PC / variants | Deliberate defect | Detecting filter | Expected red | State |
|---|---|---|---|---|
| PC-561: assume-clean, assume-dirty | Ignore lowercase ls-files tags | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | tracked_index_flags reason differs | pending |
| PC-562: skip-clean, skip-dirty, both-flags, flagged-newline-path | Ignore S/s or split filenames on whitespace | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | reason or HeldPaths differs | pending |
| PC-563: bare, ignored-bare | Omit HEAD/objects/refs recognition | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | nested_repository reason differs | pending |
| PC-564: every admin/common *.lock argument (7) | Omit recursive lock inventory | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | repository_locked reason differs | pending |
| PC-565: admin-gc.pid, common-gc.pid | Ignore gc.pid | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | repository_locked reason differs | pending |
| PC-566: merge, common-MERGE_HEAD, rebase-merge, rebase-apply, CHERRY_PICK_HEAD, REVERT_HEAD, BISECT_LOG, sequencer | Omit each operation marker separately | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | unfinished_operation reason differs | pending |
| PC-567: sparse | Omit sparse-checkout config hold | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | sparse_checkout reason differs | pending |
| PC-568: shallow | Omit shallow-repository hold | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | shallow_repository reason differs | pending |
| PC-569: gitlink | Ignore committed unpopulated gitlink | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | submodule_content reason differs | pending |
| PC-570: case-twin | Omit case-insensitive sibling check | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionHoldShapes*` | case_ambiguous_content reason differs | pending |
| PC-571 | Omit GIT_OPTIONAL_LOCKS=0 | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionReadOnlyGitCalls*` | environment assertion fails | pending |
| PC-572 | Add a mutating Git invocation / index refresh | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_InspectionReadOnlyGitCalls*` | allowlist/digest assertion fails | pending |

Mutation executes precise method filters without the ordinary discovery wildcard. Native Windows and S5–S8 remain separate obligations.

## Publication and cleanup

Tests and fix were committed and pushed separately to the authorized fast-forward-only task ref. The caller-facing final report supplies the report-only full SHA, remote-ref equality, fresh fetched-master merge-tree result, full task-base..HEAD evidence-policy check and clean worktree. Tested source remains cfe966b3756f164cb12a663db2fcaa8280cd2395. Cleanup records retain the exact task-owned alternate outputs; generated TRX/JSON/logs remain ignored.

Validation exited 0: CHECKPOINT SOURCE VALID source=cfe966b3756f164cb12a663db2fcaa8280cd2395 rows=16.
Checkpoint-owner cleanup removed 37 final project outputs and 9 base project outputs; the negative output name was the same owned runner output and is now absent. The detached base remains only for receipt custody outside the assigned checkout. Bootstrap output cleanup is recorded separately. No other outputs were deleted.
