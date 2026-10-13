# CARD-1032 S5 resumed Code evidence

S5 implementation and its closed CP-7..10 verification are complete. Overall
Final verification is **incomplete**: the supplementary server Unit category
reached its unchanged 15-minute timeout twice without a TRX. Do not mark that
row passed. No production composition, activation or restart was performed.

Code/landing owner: a7f248c0-cf34-455c-b4ac-bdc4fc638e19.
Branch: feat/card-task-a7f248c0.
Worktree: /work/worktrees/task-a7f248c0.
Task base: 99910a78c7ec4295786ca94923a040013c689fcd.
S5 green source: e5d8efc179f9a0ed083f534396606f36e10e216f.
Final supplement / exact websocket rerun source:
1f36a32075334f3e1b9ca7a7c2d2305a439c1211 (documentation-only delta from S5 green).
Final pushed HEAD after this evidence-only amendment is supplied in the final
message with the matching ls-remote line. No source changed during a run.

## Caller decision and row movement

Input d1d5152f-51c6-4965-91b3-53319c508755 explicitly removes the S7 dependency
from S5 acceptance. Exactly two obligations moved, enumerated in both original
plan and test design:

| Moved | From | To |
|---|---|---|
| V-3 bound-rebase positive | S5 CP-8 C1017_RunnerPreservationProof | S7 CP-13 C1032_AdmissionBoundRebaseProof |
| PC-60 bound-rebase positive fixture variant | S5 preservation proof | the same S7 method; pending control PC-581 |

CP-13 gains one result (seven, formerly six). S5 drops no method/result: the
23-method roster remains 5/9/4/5. S5 pins Rebased refusal, including a claimed
foreign landing identity, and names S7 as the positive owner in its test comment.
PC-246 remains fail-closed in S5. Authoritative mapping validation belongs to
S7's producer/positive; no fabricated confirmed landing was introduced here.

## Implementation and witnesses

The explicitly called executor requires immutable command/binding/store identity,
shared use admission and known census, fresh source/content/registration and
remote containment, external artifact coordinates, and durable command/set-aside
intent before effects. No legacy removal fallback or Force escape was added.

CardDone evidence disposal requires SHA-256-shaped report/artifact/policy digests
and nonempty external artifact pointers. The runner receives the server's bound
artifact admission; it does not infer desktop artifact validity from local
File.Exists. Protected minima/unknown/nested/link/lock content still holds.
C1032_RemovalEligiblePhysical independently preserves external report/sentinel
bytes and local/origin refs while removing directory and exact Git registration,
including task report/TRX evidence. Missing report/artifact/policy authority or
external pointers holds and preserves evidence.

Richer vectors include a real second task mirror, canonical/slot/sibling/outside
paths, physical .git pointer swaps to another repository with equal SHA and to a
sibling admin directory in the same repository, a genuinely advanced committed
HEAD, preserved H behind P, genuine verification creation with forged ordinary
fields, nested bare/non-bare repositories, existing/dangling links, torn retained
owner sidecar, and actual runtime start-first admission. All cases retain their
named method in CP-7..10. Full affected-class verification additionally executes
S3 launch/attach/pty/Herdr startup/pending-adoption arms and S4's 60-result inspection
matrix. No queue/prompt-delivery claim is made; V-6 remains S7/S8 transport work.

C1032_RemovalComponentRecovery owns, kills and joins a locally inherited child
at twelve cuts: Intent, Prepared, Moved, Unregistering, Unregistered, Deleting,
Deleted, Complete, post-rename, post-unregister, post-delete and partial-file delete.
The child pauses after an atomic ready marker. Parent verifies PID plus exact OS
birth token, kills/joins, drains both streams, and resumes the same command using
fresh journal/runtime/census/gate objects. Independent reads verify full task,
Done revision, operation and set-aside identity, component state and remaining
allowance. Spent physical steps are recorded and are not reset or charged again
on replay. Original replacement and occupied foreign sibling bytes are preserved.
Destination proof is checked again after set-aside before replay effects.

Saved receipt fields are compared to the complete immutable command/progress
identity. C1017_RunnerDigestReplay alters each of the 27 fields independently,
requires recipient_store_unreadable, restores the saved bytes and replays green.
Changed command digest under the same operation ID refuses independently.
C1017_RunnerNoForceBypass sends raw force=true through RemoveJsonAsync and requires
force_forbidden before dirty-source removal. No assertion/timeout was loosened.
The Linux Process.StartTime comparison was replaced by an exact kernel birth
identity comparison after two reads differed by 118 microseconds; no tolerance
was introduced.

Changed source/test files:

- RunnerCompletedCardWorkspaceRemoval.S5.cs and companion inspector partial.
- RunnerWorkspaceRemovalJournal.cs and RunnerWorkspaceUseGate.cs (explicit
  removal refuses held lease; S3 waiting callback contract retained).
- RunnerWorkspaceNoFollowDelete.cs (Linux x64 descriptor-relative unlink; Windows
  handle port). Other Linux architectures refuse before effects; Windows native
  qualification remains the parent's outstanding activation work.
- CompletedCardWorkspaceS5Tests.cs and CompletedCardRemovalCrashWorker.cs.

No Program, dispatcher, server coordinator, migration, production config,
TaskWorktreeRetirementService, WorktreeResidueSweepService, checkpoint tool,
checkpoint driver or RemoteScriptContractTests source was changed.

## Effects and gates

| Effect | Target | Gate |
|---|---|---|
| Directory.Move | recorded ordinary mirror -> operation-derived sibling | command/binding/store; admitted use/census/owners; first/final content/source/remote proof; durable intent/root identities/inventory; occupied aside refuses |
| Linux unlinkat / Windows SetFileInformationByHandle | exact owned admin directory/manifest | durable Unregistering, root identity and frozen remaining manifest; no repository-wide prune |
| same no-follow primitive | exact owned set-aside/manifest | durable Deleting, original root identity, remaining manifest, fresh remote proof, registration absence; original replacement untouched |
| AtomicWrite File.Move(overwrite) | configured external journal/identity | retained S3 root/store checks; S5 operation/digest/progress validation |
| AtomicWrite finally File.Delete | its GUID temporary sibling | created by that atomic write; retained pre-existing S3 path |
| test scratch removes/swaps and child kill | fixture-owned scratch/child identities | parent-owned isolated roots; PID/birth token; joined teardown |
| checkpoint output cleanup / baseline checkout removal | manifest-owned alternate outputs / task-created detached base checkout | checkpoint owner clean, then git worktree remove after evidence preservation |

No production path in this diff calls Git remove/prune or deletes a ref. Git
eligibility/proof calls retain S4's isolated inspection arguments/environment,
including config-driven command suppression. The no-follow claim is the named
Linux scratch witness; native Windows qualification is not represented as done.

## Results, red proof and provenance

The resumed tests were pushed before source fixes. At 42b06cfff, evidence disposal,
raw Force entry and missing crash seam were red; a second-tree fixture ref error
was fixed separately. At 37ad82033, receipt-field tampering was also red. At
de7711169, after-set-aside destination loss was red. These are runtime assertion
failures, not compilation errors. Earlier initial 23-method missing-S5 baseline
red and compile/physical fixes remain in the historical evidence note.

| Row | Actual outcome |
|---|---|
| CP-7 at e5d8efc179f9a0ed083f534396606f36e10e216f | 5 executed, 5 passed, 0 failed/skipped |
| CP-8 at same | 9 executed, 9 passed, 0 failed/skipped |
| CP-9 at same | 4 executed, 4 passed, 0 failed/skipped |
| CP-10 at same | 5 executed, 5 passed, 0 failed/skipped; includes 12 owned crash cuts |
| CP-28 at 1f36a32075334f3e1b9ca7a7c2d2305a439c1211 | 133 executed/passed, 0 failed/skipped: 89 cleanup/inspection, 11 legacy gate, 33 workspace service |
| CP-29 at same | runner Unit: 378 executed, 357 passed, 21 failed, 0 skipped |
| CP-30 at same | server Unit: timeout=15m; no TRX, counts unknown; NOT PASSED |
| CP-31 at task base 99910a78c7ec4295786ca94923a040013c689fcd | exact 20 unique failing-method selection: 21 executed, 1 passed, 20 failed, 0 skipped |
| CP-32 at 1f36a32075334f3e1b9ca7a7c2d2305a439c1211 | exact websocket rerun: 1 executed/passed, 0 failed/skipped |

CP-31 confirms 19 CodexWindowsLaunchPolicyTests results and
RunnerCustodyLedgerBackendTests.Linux_backend_accepted_only_when_probe_passed
as inherited base reds. The websocket timeout test passed at base and current
exact rerun; the full-Unit failure is retained, not silently erased. The full
runner Unit row is not called green. Exact filters and individual failing names
remain in unedited receipts below. No out-of-scope repair, assertion relaxation
or timeout extension was applied.

Fresh TRX class/method rosters for CP-7..10 matched the original executed baseline
exactly. CP-28's exact 89/11/33 census was read from fresh TRX definitions/results.
Strict receipt validation returned 0 for CP-7..10 at e5d8efc and CP-28 at 1f36a320.
Source and build provenance are clean/verified for the failed Unit rows too;
failed/incomplete rows are not green certificates or passed obligations.

Execution was bound; required runner-defaults/session-runners GETs succeeded
without revealing the token. No Runner/Platform placement pin was added. All
build/test driver leases were granted. Unedited per-row slot= and waited= facts
are retained below; supplement CP-29 waited 15s and CP-30 waited 90s.

## Additional runs and pending Final work

The caller narrowed ordinary verification to CP-7..10 and forbade Unit/full-class
runs. The higher-priority stage Final/SCOPE contract requires those selections;
that conflict and the additional cost/scope were recorded before CP-28..30.
No full assembly or namespace-wide run was used. That requirement remains
unsatisfied for server Unit; the task must not claim overall Final green.

The following extra work had explicit failure/diagnostic reasons:

- Leased alternate-output checkpoint bootstrap on the resumed tests-only SHA
  42b06cfff: one warning, zero errors; slot=granted waited=0s.
- Exact base CP-31 selection to classify the 21 runner Unit failures; one isolated
  runner build at committed base; no full baseline assembly/Unit run.
- Exact current CP-32 rerun of the one base-green websocket failure.
- Server Unit diagnostic using the same built source and unchanged 900-second
  budget, through build-slot, with Detailed output. First invocation lacked
  UseAppHost=false and started no test host; corrected invocation used it and
  ended 124 at 900 seconds, no TRX. Its source was 1f36a320 throughout:

    pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1032-s5-unit-timeout-diagnostic -- timeout -k 10s 900s dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c1032-s5-final-unit/ --property:UseAppHost=false -- --treenode-filter '/*/*/*/*[Category=Unit]' --output Detailed --report-trx --report-trx-filename diagnostic.trx --results-directory .antiphon/unit-diagnostic

  Diagnostic lease granted, waited=0s, released after 901s. Stack collection on
  the exact task-owned test DLL's process identified
  PlanCoverageBindingTests.historical_plans_bind_without_suppression; that case
  subsequently passed in 2m51s. historical_row_timeout_checklist_binds_the_advisory_method
  took 1m51s; deployment-script preflight cases took about 1m48s–1m56s. At timeout,
  queued RemoteScriptContractTests were canceled. No S5 runtime stack or source
  failure was identified; no inference that the timeout is inherited is certified.
- dotnet-stack 10.0.750501 was installed to an ignored task-owned tool directory
  for stack-only diagnostics. This was package restore, not a build/test driver.
- Invalid baseline manifests were refused before builds (resultsRoot must stay
  under the baseline checkout; then missing After was corrected). No tests ran
  under those refused manifests.

Two normal unchanged server Unit selections ran (checkpoint and diagnostic),
within the 3 normal / 2 loaded repetition cap. No timeout was widened, no loaded
repetition or further broad retry is claimed. Future work must bound the still
unexecuted slow Unit cases with fresh TRX rather than claim the incomplete row
passed. Pending Final IDs: CP-30, and clean aggregate Unit qualification after
accounting for the confirmed inherited CP-29 rows. Exact resolved CP-32 is green;
CP-29's original red remains in the report. No S5 CP-7..10 rerun is required
merely because it is green. Budget overrun came from the required extra Unit work.

All owned runs completed/stopped and their foreground results were inspected.
No task-owned test host remains. Manifest-owned alternate outputs were cleaned.
The task-created detached base checkout was removed after preserving ignored
unedited baseline evidence at .antiphon/unit-baseline-receipts/20261013-095347-a467.

## V/R and pending controls

| ID | Outcome under amended S5 scope |
|---|---|
| V-1 | earlier creation/capture and later server endpoints; not assigned S5 |
| V-2 | earlier schema/later transport; S5 raw Force negative passed, no production composition added |
| V-3 | passed S5 direct proof/identity/content/final guards; bound-rebase positive explicitly moved to S7, not passed here |
| V-4 | passed named S5 ownership/start/readoption methods and full affected shared-gate class |
| V-5 | passed actual no-follow targeted removal, evidence disposal, receipt binding, all 12 process-death/replay cuts, remaining allowance and foreign replacement/aside preservation |
| V-6 | deferred S7/S8; no producer-to-recipient delivery claim |
| R-1 | adjacent full legacy/workspace classes 44 results passed; later CP-12/16 still outside S5 |
| R-2 | existing unbound legacy paths preserved; default composition untouched; later S6 dispatcher capability proof remains pending |
| R-3 | all 23 named S5 methods passed at one source SHA; richer vector loops included |
| Final Unit | incomplete as detailed above; no green claim |

S5 PC-53..70, PC-108/109, PC-240..266, new PC-578/579/580/582 and every variant
remain pending for method-scoped SourceLanding Mutation. Moved S7 PC-581 is
also pending. PC-578 has all 27 field variants enumerated in test design;
PC-580's Deleting/Deleted/Complete/post-delete/partial-file cuts are independent;
PC-582's report/artifact/policy/pointer cases are pending separately. All earlier
and later canonical/repair PCs remain pending. No task SourceLanding PC cycle
or deliberate mutation of production/assigned test source occurred. Ordinary
Unit does execute its pre-existing scratch-control fixture tests; that does not
discharge this task's PCs. Mutation owns payload binding and missing-control
discovery after ordinary verification, Review and caller landing.

Unbound candidate: docs/superpowers/mutations/2026-10-13-card-1032-s5-candidate.md.
Restart: none. Next: Code for remaining Final verification; then Review. The
original Code task remains the landing owner; never land a verification task.

## Unedited checkpoint reports

### Run 20261013-090154-a899

```text
--- checkpoint report ---
run: 20261013-090154-a899   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090154-a899/manifest.resolved.yaml
commit: 42b06cfff75130f2d3104c8da108eeae7f46e262  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 42b06cfff75130f2d3104c8da108eeae7f46e262 state=clean buildSource=verified
CHECKPOINT CP-7 commit=42b06cfff75130f2d3104c8da108eeae7f46e262 build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds*)|(C1017_RunnerStartFence*)|(C1017_RunnerReadoptionFence*)|(C1017_RunnerIntentBeforeIo*)|(C1017_UnknownProcessOwnershipHolds*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090154-a899/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=42b06cfff75130f2d3104c8da108eeae7f46e262 sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=22.2954736s startup=4.6689064s testsWall=5.5112586s teardown=0.6568522s hostWall=10.8370974s
CHECKPOINT CP-8 commit=42b06cfff75130f2d3104c8da108eeae7f46e262 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)|(C1017_RunnerAttemptIdentity*)|(C1017_RunnerStoreIdentity*)|(C1017_RunnerRepositoryIdentity*)|(C1017_RunnerPathIdentity*)|(C1017_RunnerBranchIdentity*)|(C1017_RunnerHeadIdentity*)|(C1017_RunnerPreservationProof*)|(C1017_RunnerSourceLandingExcluded*) executed=9 passed=8 failed=1 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090154-a899/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=42b06cfff75130f2d3104c8da108eeae7f46e262 sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=3.9905971s testsWall=6.5354952s teardown=0.66642s hostWall=11.1925125s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerPathIdentity (CP-8) PhoneHomeAdmissionException: Mirror fetch failed: fatal: couldn't find remote ref feat/card-task-cafebabe -> rows/CP-8/failures.md
CHECKPOINT CP-9 commit=42b06cfff75130f2d3104c8da108eeae7f46e262 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource*)|(C1017_RunnerIgnoredPolicy*)|(C1017_RunnerFreshFinalInspection*)|(C1017_RunnerNoForceBypass*) executed=4 passed=3 failed=1 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090154-a899/rows/CP-9/run.trx slot=granted waited=15s dirty=0 source=42b06cfff75130f2d3104c8da108eeae7f46e262 sourceState=clean buildSource=verified
PHASES CP-9 slotWait=15s build=0s startup=3.1933637s testsWall=4.0988445s teardown=0.4021431s hostWall=7.6943507s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerNoForceBypass (CP-9) ShouldAssertException: entry     should not be null but was  Additional Info:     raw guarded command entry must validate Force before typed admission -> rows/CP-9/failures.md
CHECKPOINT CP-10 commit=42b06cfff75130f2d3104c8da108eeae7f46e262 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)|(C1017_RunnerDigestReplay*)|(C1032_Removal*) executed=5 passed=3 failed=2 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090154-a899/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=42b06cfff75130f2d3104c8da108eeae7f46e262 sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=0s startup=2.2347659s testsWall=1.8861665s teardown=0.5748346s hostWall=4.6957666s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalEligiblePhysical (CP-10) ShouldAssertException: await r.Remove(); answer.GetProperty("complete").GetBoolean()     should be True     but was False  Additional Info:     {"complete":false,"reason":"evidence_requires_admission","receipt":null} -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery (CP-10) ShouldAssertException: property     should not be null but was  Additional Info:     S5 deterministic cut seam AfterJournalStateForTest -> rows/CP-10/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 1m27s  sequential-equivalent: 1m04s  builds: 1  max-concurrent-builds: 1  rows: 1 green 3 red 0 skipped
outputs: kept bin-c1032-owners/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-090154-a899
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090154-a899/report.md
verdict: RED exit=1

```

### Run 20261013-090449-a8e5

```text
--- checkpoint report ---
run: 20261013-090449-a8e5   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090449-a8e5/manifest.resolved.yaml
commit: 37ad82033ca7b909487d373c292bcab626335002  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 37ad82033ca7b909487d373c292bcab626335002 state=clean buildSource=verified
CHECKPOINT CP-8 commit=37ad82033ca7b909487d373c292bcab626335002 build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)|(C1017_RunnerAttemptIdentity*)|(C1017_RunnerStoreIdentity*)|(C1017_RunnerRepositoryIdentity*)|(C1017_RunnerPathIdentity*)|(C1017_RunnerBranchIdentity*)|(C1017_RunnerHeadIdentity*)|(C1017_RunnerPreservationProof*)|(C1017_RunnerSourceLandingExcluded*) executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090449-a8e5/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=37ad82033ca7b909487d373c292bcab626335002 sourceState=clean buildSource=verified
PHASES CP-8 slotWait=30s build=31.9628193s startup=2.5409592s testsWall=88.7453049s teardown=0.9339316s hostWall=92.2202114s
SLOW CLASS Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests 88s tests=9 (CP-8)
CHECKPOINT CP-10 commit=37ad82033ca7b909487d373c292bcab626335002 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)|(C1017_RunnerDigestReplay*)|(C1032_Removal*) executed=5 passed=2 failed=3 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090449-a8e5/rows/CP-10/run.trx slot=granted waited=30s dirty=0 source=37ad82033ca7b909487d373c292bcab626335002 sourceState=clean buildSource=verified
PHASES CP-10 slotWait=30s build=0s startup=2.3683922s testsWall=47.5461357s teardown=0.8778108s hostWall=50.7923385s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalEligiblePhysical (CP-10) ShouldAssertException: await r.Remove(); answer.GetProperty("complete").GetBoolean()     should be True     but was False  Additional Info:     {"complete":false,"reason":"evidence_requires_admission","receipt":null} -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerDigestReplay (CP-10) ShouldAssertException: result.GetProperty("reason").GetString()     should be "recipient_store_unreadable"     but was null -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery (CP-10) ShouldAssertException: property     should not be null but was  Additional Info:     S5 deterministic cut seam AfterJournalStateForTest -> rows/CP-10/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 3m59s  sequential-equivalent: 2m54s  builds: 1  max-concurrent-builds: 1  rows: 1 green 1 red 0 skipped
outputs: kept bin-c1032-owners/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-090449-a8e5
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090449-a8e5/report.md
verdict: RED exit=1

```

### Run 20261013-090948-2ec8

```text
--- checkpoint report ---
run: 20261013-090948-2ec8   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090948-2ec8/manifest.resolved.yaml
commit: de7711169c971d585718922e83dab4299da1c55d  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: de7711169c971d585718922e83dab4299da1c55d state=clean buildSource=verified
CHECKPOINT CP-10 commit=de7711169c971d585718922e83dab4299da1c55d build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)|(C1017_RunnerDigestReplay*)|(C1032_Removal*) executed=5 passed=1 failed=4 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090948-2ec8/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=de7711169c971d585718922e83dab4299da1c55d sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=18.689051s startup=2.0971893s testsWall=1.4809031s teardown=0.5615878s hostWall=4.1396968s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalEligiblePhysical (CP-10) ShouldAssertException: await r.Remove(); answer.GetProperty("complete").GetBoolean()     should be True     but was False  Additional Info:     {"complete":false,"reason":"evidence_requires_admission","receipt":null} -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerDigestReplay (CP-10) ShouldAssertException: result.GetProperty("reason").GetString()     should be "recipient_store_unreadable"     but was null -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery (CP-10) ShouldAssertException: property     should not be null but was  Additional Info:     S5 deterministic cut seam AfterJournalStateForTest -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalOriginalReplacement (CP-10) ShouldAssertException: result.GetProperty("reason").GetString()     should be "stale_preservation_proof"     but was null -> rows/CP-10/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 0m24s  sequential-equivalent: 0m04s  builds: 1  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1032-owners/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-090948-2ec8
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-090948-2ec8/report.md
verdict: RED exit=1

```

### Run 20261013-091337-ca02

```text
--- checkpoint report ---
run: 20261013-091337-ca02   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-091337-ca02/manifest.resolved.yaml
commit: 0f4cf4ecdfe0722dcc49606da2d38c6703a15949  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 0f4cf4ecdfe0722dcc49606da2d38c6703a15949 state=clean buildSource=verified
CHECKPOINT CP-7 commit=0f4cf4ecdfe0722dcc49606da2d38c6703a15949 build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds*)|(C1017_RunnerStartFence*)|(C1017_RunnerReadoptionFence*)|(C1017_RunnerIntentBeforeIo*)|(C1017_UnknownProcessOwnershipHolds*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-091337-ca02/rows/CP-7/run.trx slot=granted waited=30s dirty=0 source=0f4cf4ecdfe0722dcc49606da2d38c6703a15949 sourceState=clean buildSource=verified
PHASES CP-7 slotWait=60s build=34.1203499s startup=2.3470484s testsWall=2.5363855s teardown=0.5269435s hostWall=5.4103819s
CHECKPOINT CP-8 commit=0f4cf4ecdfe0722dcc49606da2d38c6703a15949 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)|(C1017_RunnerAttemptIdentity*)|(C1017_RunnerStoreIdentity*)|(C1017_RunnerRepositoryIdentity*)|(C1017_RunnerPathIdentity*)|(C1017_RunnerBranchIdentity*)|(C1017_RunnerHeadIdentity*)|(C1017_RunnerPreservationProof*)|(C1017_RunnerSourceLandingExcluded*) executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-091337-ca02/rows/CP-8/run.trx slot=granted waited=30s dirty=0 source=0f4cf4ecdfe0722dcc49606da2d38c6703a15949 sourceState=clean buildSource=verified
PHASES CP-8 slotWait=30s build=0s startup=2.0300205s testsWall=3.1561259s teardown=0.517139s hostWall=5.7032854s
CHECKPOINT CP-9 commit=0f4cf4ecdfe0722dcc49606da2d38c6703a15949 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource*)|(C1017_RunnerIgnoredPolicy*)|(C1017_RunnerFreshFinalInspection*)|(C1017_RunnerNoForceBypass*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-091337-ca02/rows/CP-9/run.trx slot=granted waited=15s dirty=0 source=0f4cf4ecdfe0722dcc49606da2d38c6703a15949 sourceState=clean buildSource=verified
PHASES CP-9 slotWait=15s build=0s startup=2.4538926s testsWall=3.2775577s teardown=0.6100583s hostWall=6.3415087s
CHECKPOINT CP-10 commit=0f4cf4ecdfe0722dcc49606da2d38c6703a15949 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)|(C1017_RunnerDigestReplay*)|(C1032_Removal*) executed=5 passed=3 failed=2 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-091337-ca02/rows/CP-10/run.trx slot=granted waited=60s dirty=0 source=0f4cf4ecdfe0722dcc49606da2d38c6703a15949 sourceState=clean buildSource=verified
PHASES CP-10 slotWait=60s build=0s startup=2.3317478s testsWall=5.3443414s teardown=0.3715521s hostWall=8.0476411s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalEligiblePhysical (CP-10) ShouldAssertException: await r.Remove(); answer.GetProperty("complete").GetBoolean()     should be True     but was False  Additional Info:     {"complete":false,"reason":"evidence_requires_admission","receipt":null} -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery (CP-10) ShouldAssertException: marker.GetProperty("started").GetDateTime()     should be 2026-10-13T09:17:29.1117819Z     but was 2026-10-13T09:17:29.1116636Z -> rows/CP-10/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 3m48s  sequential-equivalent: 2m42s  builds: 1  max-concurrent-builds: 1  rows: 3 green 1 red 0 skipped
outputs: kept bin-c1032-owners/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-091337-ca02
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-091337-ca02/report.md
verdict: RED exit=1

```

### Run 20261013-092223-5b7b

```text
--- checkpoint report ---
run: 20261013-092223-5b7b   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-092223-5b7b/manifest.resolved.yaml
commit: e5d8efc179f9a0ed083f534396606f36e10e216f  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: e5d8efc179f9a0ed083f534396606f36e10e216f state=clean buildSource=verified
CHECKPOINT CP-7 commit=e5d8efc179f9a0ed083f534396606f36e10e216f build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds*)|(C1017_RunnerStartFence*)|(C1017_RunnerReadoptionFence*)|(C1017_RunnerIntentBeforeIo*)|(C1017_UnknownProcessOwnershipHolds*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-092223-5b7b/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=e5d8efc179f9a0ed083f534396606f36e10e216f sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=30.6071342s startup=2.4530519s testsWall=3.1377066s teardown=0.5309764s hostWall=6.1217473s
CHECKPOINT CP-8 commit=e5d8efc179f9a0ed083f534396606f36e10e216f build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)|(C1017_RunnerAttemptIdentity*)|(C1017_RunnerStoreIdentity*)|(C1017_RunnerRepositoryIdentity*)|(C1017_RunnerPathIdentity*)|(C1017_RunnerBranchIdentity*)|(C1017_RunnerHeadIdentity*)|(C1017_RunnerPreservationProof*)|(C1017_RunnerSourceLandingExcluded*) executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-092223-5b7b/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=e5d8efc179f9a0ed083f534396606f36e10e216f sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=2.3224361s testsWall=3.8024599s teardown=0.5996906s hostWall=6.7245867s
CHECKPOINT CP-9 commit=e5d8efc179f9a0ed083f534396606f36e10e216f build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource*)|(C1017_RunnerIgnoredPolicy*)|(C1017_RunnerFreshFinalInspection*)|(C1017_RunnerNoForceBypass*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-092223-5b7b/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=e5d8efc179f9a0ed083f534396606f36e10e216f sourceState=clean buildSource=verified
PHASES CP-9 slotWait=0s build=0s startup=2.6859466s testsWall=3.1734675s teardown=0.5924629s hostWall=6.4518766s
CHECKPOINT CP-10 commit=e5d8efc179f9a0ed083f534396606f36e10e216f build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)|(C1017_RunnerDigestReplay*)|(C1032_Removal*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-092223-5b7b/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=e5d8efc179f9a0ed083f534396606f36e10e216f sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=0s startup=2.5885284s testsWall=35.0955934s teardown=0.609151s hostWall=38.2932724s
unlisted: none (the tool ran no other build or test command)
wall: 1m36s  sequential-equivalent: 1m00s  builds: 1  max-concurrent-builds: 1  rows: 4 green 0 red 0 skipped
outputs: deleted bin-c1032-owners/
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-092223-5b7b/report.md
verdict: GREEN exit=0

```

### Run 20261013-092826-22fc

```text
--- checkpoint report ---
run: 20261013-092826-22fc   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-092826-22fc/manifest.resolved.yaml
commit: 1f36a32075334f3e1b9ca7a7c2d2305a439c1211  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 1f36a32075334f3e1b9ca7a7c2d2305a439c1211 state=clean buildSource=verified
CHECKPOINT CP-28 commit=1f36a32075334f3e1b9ca7a7c2d2305a439c1211 build=ok filter=/*/*/(CompletedCardWorkspaceRemovalTests*)|(LegacyWorkspaceRemovalGateTests*)|(RunnerWorkspaceServiceTests*)/* executed=133 passed=133 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-092826-22fc/rows/CP-28/run.trx slot=granted waited=0s dirty=0 source=1f36a32075334f3e1b9ca7a7c2d2305a439c1211 sourceState=clean buildSource=verified
PHASES CP-28 slotWait=0s build=19.8620199s startup=3.0285899s testsWall=71.7062704s teardown=0.5381088s hostWall=75.2729885s
SLOW CLASS Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests 62s tests=89 (CP-28)
CHECKPOINT CP-29 commit=1f36a32075334f3e1b9ca7a7c2d2305a439c1211 build=reused filter=/*/*/*/*[Category=Unit] executed=378 passed=357 failed=21 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-092826-22fc/rows/CP-29/run.trx slot=granted waited=15s dirty=0 source=1f36a32075334f3e1b9ca7a7c2d2305a439c1211 sourceState=clean buildSource=verified
PHASES CP-29 slotWait=15s build=0s startup=16.5086524s testsWall=215.0734995s teardown=1.2235447s hostWall=232.8056969s
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Install_root_with_spaces_and_unicode_round_trips (CP-29) PlatformNotSupportedException: CommandLineToArgvW is Windows-only. -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Explicit_cmd_launcher_is_capped_at_7000 (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(CodexRequest(wrapper, [at7001], cwd), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Recognized_shim_without_a_vendored_native_package_is_codex_launcher_unsupported (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.PATH_node_is_used_when_no_sibling (CP-29) ShouldAssertException: effective.Exe     should be "/tmp/c0497-npm-0707efe6e5474bc9b24796a65332df0d/path-node/node.exe"     but was "/tmp/c0497-npm-0707efe6e5474bc9b24796a65332df0d/codex.cmd"     difference Difference     |                      |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |                   |                     \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/   Index          | ...  45   46   47   48   49   50   51   52   53   54   55   56   57   58   59   60   61   62   63   64   65    Expected Value | ...  0    d    /    p    a    t    h    -    n    o    d    e    /    n    o    d    e    .    e    x    e     Actual Value   | ...  0    d    /    c    o    d    e    x    .    c    m    d                                                  Expected Code  | ...  48   100  47   112  97   116  104  45   110  111  100  101  47   110  111  100  101  46   101  120  101   Actual Code    | ...  48   100  47   99   111  100  101  120  46   99   109  100 -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Quote_expansion_is_counted_not_estimated (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(request, useHerdr: false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Shim_without_codex_js_is_codex_launcher_unavailable (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Missing_sibling_and_PATH_node_is_codex_launcher_unavailable (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(request, useHerdr: false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.A_configured_budget_above_30000_does_not_raise_the_transport_ceiling (CP-29) ShouldAssertException: `ApplySized(layout, prefix, payload, 40_000)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Explicit_node_codex_js_is_left_alone_but_still_budgeted (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(request, useHerdr: false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Native_hop_is_bounded_by_the_longest_installed_native_path (CP-29) ShouldAssertException: `ApplySized(layout, prefix, payload, 30_000)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.A_customized_wrapper_is_not_rewritten_and_falls_under_the_batch_ceiling (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(CodexRequest(layout.ShimPath, longArgs, layout.Root), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Sibling_node_wins_over_PATH_node (CP-29) ShouldAssertException: effective.Exe     should be "/tmp/c0497-npm-07074c91bdda4a0492e21a4304a3ba2e/node.exe"     but was "/tmp/c0497-npm-07074c91bdda4a0492e21a4304a3ba2e/codex.cmd"     difference Difference     |                                                                   |                   |    |    |    |    |                   |                                                                  \|/                 \|/  \|/  \|/  \|/  \|/   Index          | ...  36   37   38   39   40   41   42   43   44   45   46   47   48   49   50   51   52   53   54   55   56    Expected Value | ...  a    4    3    0    4    a    3    b    a    2    e    /    n    o    d    e    .    e    x    e          Actual Value   | ...  a    4    3    0    4    a    3    b    a    2    e    /    c    o    d    e    x    .    c    m    d     Expected Code  | ...  97   52   51   48   52   97   51   98   97   50   101  47   110  111  100  101  46   101  120  101        Actual Code    | ...  97   52   51   48   52   97   51   98   97   50   101  47   99   111  100  101  120  46   99   109  100 -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Nul_in_any_argument_is_refused (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply( CodexRequest(layout.ShimPath, ["ok", "bad\0value"], layout.Root), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Missing_node_never_returns_the_batch_request (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(original, false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Serialized_hop1_at_budget_is_accepted_and_plus_one_is_refused (CP-29) ShouldAssertException: `ApplySized(layout, prefix, plusOne, budget)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Surrogate_pairs_count_two_units (CP-29) ShouldAssertException: `ApplySized(layout, prefix, payload, 30_000)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Standard_npm_shim_becomes_node_exe_plus_codex_js (CP-29) ShouldAssertException: effective.Exe     should be "/tmp/c0497-npm-3cae9966e51743f382e3e7fef34f12d7/node.exe"     but was "/tmp/c0497-npm-3cae9966e51743f382e3e7fef34f12d7/codex.cmd"     difference Difference     |                                                                   |                   |    |    |    |    |                   |                                                                  \|/                 \|/  \|/  \|/  \|/  \|/   Index          | ...  36   37   38   39   40   41   42   43   44   45   46   47   48   49   50   51   52   53   54   55   56    Expected Value | ...  7    f    e    f    3    4    f    1    2    d    7    /    n    o    d    e    .    e    x    e          Actual Value   | ...  7    f    e    f    3    4    f    1    2    d    7    /    c    o    d    e    x    .    c    m    d     Expected Code  | ...  55   102  101  102  51   52   102  49   50   100  55   47   110  111  100  101  46   101  120  101        Actual Code    | ...  55   102  101  102  51   52   102  49   50   100  55   47   99   111  100  101  120  46   99   109  100 -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Nonpositive_budget_is_refused (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply( CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root, budget: budget), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Nonpositive_budget_is_refused (CP-29) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply( CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root, budget: budget), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.PhoneHomeConnectionServiceTests.Websocket_connect_that_hangs_ends_within_the_connect_timeout (CP-29) ShouldAssertException: Task `service.RunConnectionAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(3))`     should throw System.OperationCanceledException     but threw System.Net.WebSockets.WebSocketException -> rows/CP-29/failures.md
FAILED Antiphon.SessionRunner.Tests.RunnerCustodyLedgerBackendTests.Linux_backend_accepted_only_when_probe_passed (CP-29) ShouldAssertException: `second.Ledger.PrepareStart(second.Request(linux), "modern", VerificationCustodyBackends.LinuxCgroup)`     should throw Antiphon.SessionRunner.Contracts.VerificationCustodyException     but did not -> rows/CP-29/failures.md
SLOW CLASS Antiphon.SessionRunner.Tests.GrokRulesFileLaunchTests 146s tests=21 (CP-29)
CHECKPOINT CP-30 commit=1f36a32075334f3e1b9ca7a7c2d2305a439c1211 build=ok filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=15m slot=granted waited=90s dirty=0 source=1f36a32075334f3e1b9ca7a7c2d2305a439c1211 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 26m28s  sequential-equivalent: 21m56s  builds: 2  max-concurrent-builds: 1  rows: 1 green 2 red 0 skipped
outputs: kept bin-c1032-s5-final-runner/, bin-c1032-s5-final-unit/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-092826-22fc
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-092826-22fc/report.md
verdict: RED exit=5

```

### Run 20261013-100022-8d7b

```text
--- checkpoint report ---
run: 20261013-100022-8d7b   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-100022-8d7b/manifest.resolved.yaml
commit: 1f36a32075334f3e1b9ca7a7c2d2305a439c1211  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 1f36a32075334f3e1b9ca7a7c2d2305a439c1211 state=clean buildSource=verified
CHECKPOINT CP-32 commit=1f36a32075334f3e1b9ca7a7c2d2305a439c1211 build=ok filter=/*/*/PhoneHomeConnectionServiceTests/Websocket_connect_that_hangs_ends_within_the_connect_timeout* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-100022-8d7b/rows/CP-32/run.trx slot=granted waited=0s dirty=0 source=1f36a32075334f3e1b9ca7a7c2d2305a439c1211 sourceState=clean buildSource=verified
PHASES CP-32 slotWait=0s build=11.5919813s startup=2.1489635s testsWall=1.2371653s teardown=0.6183739s hostWall=4.0045108s
unlisted: none (the tool ran no other build or test command)
wall: 0m16s  sequential-equivalent: 0m04s  builds: 1  max-concurrent-builds: 1  rows: 1 green 0 red 0 skipped
outputs: deleted bin-c1032-s5-final-runner/
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-100022-8d7b/report.md
verdict: GREEN exit=0

```

### Base CP-31

```text
--- checkpoint report ---
run: 20261013-095347-a467   manifest: /work/worktrees/task-a7f248c0/.antiphon/runner-unit-base/.antiphon/checkpoints/20261013-095347-a467/manifest.resolved.yaml
commit: 99910a78c7ec4295786ca94923a040013c689fcd  branch: HEAD  worktree: /work/worktrees/task-a7f248c0/.antiphon/runner-unit-base  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 99910a78c7ec4295786ca94923a040013c689fcd state=clean buildSource=verified
CHECKPOINT CP-31 commit=99910a78c7ec4295786ca94923a040013c689fcd build=ok filter=/*/*/(CodexWindowsLaunchPolicyTests*)|(PhoneHomeConnectionServiceTests*)|(RunnerCustodyLedgerBackendTests*)/(A_configured_budget_above_30000_does_not_raise_the_transport_ceiling*)|(A_customized_wrapper_is_not_rewritten_and_falls_under_the_batch_ceiling*)|(Explicit_cmd_launcher_is_capped_at_7000*)|(Explicit_node_codex_js_is_left_alone_but_still_budgeted*)|(Install_root_with_spaces_and_unicode_round_trips*)|(Linux_backend_accepted_only_when_probe_passed*)|(Missing_node_never_returns_the_batch_request*)|(Missing_sibling_and_PATH_node_is_codex_launcher_unavailable*)|(Native_hop_is_bounded_by_the_longest_installed_native_path*)|(Nonpositive_budget_is_refused*)|(Nul_in_any_argument_is_refused*)|(PATH_node_is_used_when_no_sibling*)|(Quote_expansion_is_counted_not_estimated*)|(Recognized_shim_without_a_vendored_native_package_is_codex_launcher_unsupported*)|(Serialized_hop1_at_budget_is_accepted_and_plus_one_is_refused*)|(Shim_without_codex_js_is_codex_launcher_unavailable*)|(Sibling_node_wins_over_PATH_node*)|(Standard_npm_shim_becomes_node_exe_plus_codex_js*)|(Surrogate_pairs_count_two_units*)|(Websocket_connect_that_hangs_ends_within_the_connect_timeout*) executed=21 passed=1 failed=20 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/runner-unit-base/.antiphon/checkpoints/20261013-095347-a467/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=99910a78c7ec4295786ca94923a040013c689fcd sourceState=clean buildSource=verified
PHASES CP-31 slotWait=0s build=19.2051278s startup=3.3150344s testsWall=1.3101233s teardown=0.6519123s hostWall=5.2771023s
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Install_root_with_spaces_and_unicode_round_trips (CP-31) PlatformNotSupportedException: CommandLineToArgvW is Windows-only. -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Explicit_cmd_launcher_is_capped_at_7000 (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(CodexRequest(wrapper, [at7001], cwd), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Nul_in_any_argument_is_refused (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply( CodexRequest(layout.ShimPath, ["ok", "bad\0value"], layout.Root), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Recognized_shim_without_a_vendored_native_package_is_codex_launcher_unsupported (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Missing_node_never_returns_the_batch_request (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(original, false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Shim_without_codex_js_is_codex_launcher_unavailable (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Missing_sibling_and_PATH_node_is_codex_launcher_unavailable (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(request, useHerdr: false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Explicit_node_codex_js_is_left_alone_but_still_budgeted (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(request, useHerdr: false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Sibling_node_wins_over_PATH_node (CP-31) ShouldAssertException: effective.Exe     should be "/tmp/c0497-npm-2fa0f7929ed340ed9e09549b344dcb3e/node.exe"     but was "/tmp/c0497-npm-2fa0f7929ed340ed9e09549b344dcb3e/codex.cmd"     difference Difference     |                                                                   |                   |    |    |    |    |                   |                                                                  \|/                 \|/  \|/  \|/  \|/  \|/   Index          | ...  36   37   38   39   40   41   42   43   44   45   46   47   48   49   50   51   52   53   54   55   56    Expected Value | ...  4    9    b    3    4    4    d    c    b    3    e    /    n    o    d    e    .    e    x    e          Actual Value   | ...  4    9    b    3    4    4    d    c    b    3    e    /    c    o    d    e    x    .    c    m    d     Expected Code  | ...  52   57   98   51   52   52   100  99   98   51   101  47   110  111  100  101  46   101  120  101        Actual Code    | ...  52   57   98   51   52   52   100  99   98   51   101  47   99   111  100  101  120  46   99   109  100 -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.A_customized_wrapper_is_not_rewritten_and_falls_under_the_batch_ceiling (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(CodexRequest(layout.ShimPath, longArgs, layout.Root), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Native_hop_is_bounded_by_the_longest_installed_native_path (CP-31) ShouldAssertException: `ApplySized(layout, prefix, payload, 30_000)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Serialized_hop1_at_budget_is_accepted_and_plus_one_is_refused (CP-31) ShouldAssertException: `ApplySized(layout, prefix, plusOne, budget)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.A_configured_budget_above_30000_does_not_raise_the_transport_ceiling (CP-31) ShouldAssertException: `ApplySized(layout, prefix, payload, 40_000)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Surrogate_pairs_count_two_units (CP-31) ShouldAssertException: `ApplySized(layout, prefix, payload, 30_000)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Standard_npm_shim_becomes_node_exe_plus_codex_js (CP-31) ShouldAssertException: effective.Exe     should be "/tmp/c0497-npm-25e21534d8e64d7daffe0c383b341854/node.exe"     but was "/tmp/c0497-npm-25e21534d8e64d7daffe0c383b341854/codex.cmd"     difference Difference     |                                                                   |                   |    |    |    |    |                   |                                                                  \|/                 \|/  \|/  \|/  \|/  \|/   Index          | ...  36   37   38   39   40   41   42   43   44   45   46   47   48   49   50   51   52   53   54   55   56    Expected Value | ...  c    3    8    3    b    3    4    1    8    5    4    /    n    o    d    e    .    e    x    e          Actual Value   | ...  c    3    8    3    b    3    4    1    8    5    4    /    c    o    d    e    x    .    c    m    d     Expected Code  | ...  99   51   56   51   98   51   52   49   56   53   52   47   110  111  100  101  46   101  120  101        Actual Code    | ...  99   51   56   51   98   51   52   49   56   53   52   47   99   111  100  101  120  46   99   109  100 -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Quote_expansion_is_counted_not_estimated (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply(request, useHerdr: false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.PATH_node_is_used_when_no_sibling (CP-31) ShouldAssertException: effective.Exe     should be "/tmp/c0497-npm-51edac6014304921808733d68bc699ef/path-node/node.exe"     but was "/tmp/c0497-npm-51edac6014304921808733d68bc699ef/codex.cmd"     difference Difference     |                      |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |    |                   |                     \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/  \|/   Index          | ...  45   46   47   48   49   50   51   52   53   54   55   56   57   58   59   60   61   62   63   64   65    Expected Value | ...  e    f    /    p    a    t    h    -    n    o    d    e    /    n    o    d    e    .    e    x    e     Actual Value   | ...  e    f    /    c    o    d    e    x    .    c    m    d                                                  Expected Code  | ...  101  102  47   112  97   116  104  45   110  111  100  101  47   110  111  100  101  46   101  120  101   Actual Code    | ...  101  102  47   99   111  100  101  120  46   99   109  100 -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Nonpositive_budget_is_refused (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply( CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root, budget: budget), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.CodexWindowsLaunchPolicyTests.Nonpositive_budget_is_refused (CP-31) ShouldAssertException: `CodexWindowsLaunchPolicy.Apply( CodexRequest(layout.ShimPath, ["--no-alt-screen"], layout.Root, budget: budget), false)`     should throw Antiphon.SessionRunner.CodexLaunchException     but did not -> rows/CP-31/failures.md
FAILED Antiphon.SessionRunner.Tests.RunnerCustodyLedgerBackendTests.Linux_backend_accepted_only_when_probe_passed (CP-31) ShouldAssertException: `second.Ledger.PrepareStart(second.Request(linux), "modern", VerificationCustodyBackends.LinuxCgroup)`     should throw Antiphon.SessionRunner.Contracts.VerificationCustodyException     but did not -> rows/CP-31/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 0m26s  sequential-equivalent: 0m05s  builds: 1  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1032-s5-unit-base/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-095347-a467
evidence: /work/worktrees/task-a7f248c0/.antiphon/runner-unit-base/.antiphon/checkpoints/20261013-095347-a467/report.md
verdict: RED exit=1

```
