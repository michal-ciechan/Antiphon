# CARD-1032 S7 Code evidence

Historical round-1 evidence. Final Review e0b648d4 found report-loss gaps F1/F2
at cfd114a6d: uppercase report names and reports added after producer inspection
were not held. The repair and its new evidence are owned by Code task 60025819;
use [the repair selection](2026-10-13-card-1032-s7-repair-verification.md).
The historical CHECKPOINT lines below are retained unchanged.

Caller refinement d78b38f0-bde1-4a74-90c9-29f32859c508 arrived after the
whole-Unit row timed out: no Unit row remained active to interrupt, and no
further whole-Unit run was started. S7 implementation and scoped ordinary V/R
are complete; the Unit attempt remains disclosed, uncertified and not passed.

Original Code / landing owner: `f2b2ece7-37bf-4c83-9b74-cfb9e78c1eae`.
Branch: `feat/card-task-f2b2ece7`.
Worktree: `/work/worktrees/task-f2b2ece7`.
Task base: `63b2e70479c846cacd01af6b20190e845f8258e4`.
All meaningful slices/fixes were committed and pushed, without rebase, amend
or force push. The report's containing commit is documentation only.

Plan: `docs/superpowers/plans/2026-10-11-card-1032-runner-mirror-cleanup-plan.md`.
Frozen S7 design: `docs/superpowers/plans/2026-10-12-card-1032-test-design.md`,
CP-13/14, including the moved V-3 rebase positive and PC-60 positive variant.
Historical Final supplement and repair row:
`docs/superpowers/plans/2026-10-13-card-1032-s7-final-verification.md`.
Current Review selection excludes the historical CP-F1 whole-Unit row as
requested by the refinement. Do not run that table without explicit `--rows`.

The authoritative producer is `CardWorktreeMirrorCleanupService`. Discovery
creates endpoints keyed by durable mirror binding, including historical stores
and attempts. Admission rechecks Done generation, terminal/settled task,
consumer/recovery state, bound coordinates, actual mirror HEAD and external
artifact bytes. It commits immutable command bytes before phone-home send.
Receipt import and component completion use one SaveChanges, and compare all
27 receipt fields against the admitted command.

The new rebase certificate uses the configured phone-home shared secret and
binds the complete canonical command, including task, attempt, binding, store,
Done revision, exact H, mapped P, artifacts and policy. The service takes only
endpoint/run IDs and reads server landing records itself. Caller proof fields
are not producer input. No key appears in the command. Unconfigured inspectors
and transports retain their rebased-proof refusal; tests explicitly install the
keyed transport. Existing S5 runner gates/journal/identity checks still execute.

Production remains inert: no Program registration, sweep caller, cleanup route,
Execute/cohort configuration change, daemon restart, or live workspace deletion.
S8 scheduling and recovery-cut methods are not implemented by this task.
Artifact proof reads existing external regular files through
ChannelReplyAttachmentReader. The initial producer held lowercase task-report
names from its first inventory; it missed case variants and later arrivals
(Review F1/F2). Directory bundle pointers were held by the regular-file reader.
This historical run did not establish remote task-report preservation.

Files changed: discovery and the new producer in `server/Application/Services/`;
`server/Infrastructure/Files/CardDoneArtifactPreservation.cs`; the landing
certificate helper in SessionRunner.Contracts; removal/proof/transport support
in SessionRunner; the new mirror test class and fixture; the slow-class registry;
and the verification/evidence Markdown files. No EF migration or S8 dependency
was needed. The extra contract/recipient changes are required for S7's moved
real rebase positive, which S5's default explicitly refuses.

## Ordinary outcomes

Substantive code was tested at
`6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9`:

| Selection | Native results | Outcome |
|---|---:|---|
| CP-13 | 7 | 7 passed, zero failed/skipped |
| CP-14 | 7 | 7 passed, zero failed/skipped |
| CP-F2 | 56 | 56 passed, zero failed/skipped |
| CP-F3 | 144 | 144 passed, zero failed/skipped |
| CP-F1 whole Unit | unavailable | timeout at unchanged 15-minute bound; no TRX; not certified |

214 scoped executions passed (including repeated mirror methods in the required
full-class row); this is not a count of unique cases or internal matrix rows.
Fresh TRX was inspected for every intended class/method. Full class counts:
CardWorktreeMirrorCleanupTests=14; CardWorktreeCleanupRecoveryTests=6;
CompletedCardWorktreeRemovalTests=36; CompletedCardWorkspaceRemovalTests=97;
CompletedCardWorkspaceContractTests=3; RunnerWorkspaceServiceTests=33;
LegacyWorkspaceRemovalGateTests=11. Matrices execute inside their native methods.

V-1: PASS in CP-13, distinct bound endpoints/current generation/attempt/store.
V-2: PASS in CP-14 and CP-F3, matching durable receipt and unsupported refusal.
V-3: PASS in CP-13, H behind P, real Git rebase/recipient removal, external
artifact refusals and signed-command tampering before any runner intent/effect.
V-4: adjacent PASS in CP-F3; no new native Windows qualification claimed.
V-5: adjacent PASS in CP-F3; S8 crash/delivery recovery remains deferred.
V-6: PASS in CP-13/14, independent writer during held inspection, commit before
send, reopen-first refusal/intent-first continuation and atomic receipt import.
R-1/R-2: adjacent PASS in CP-F2/3, local and legacy runner behaviors.
R-3: PASS in CP-13/14 and CP-F2, mirror admission/receipt behavior.
No session input or notification acceptance was added, so no UserPrompt manual
work was newly required. Manual source/DI/sweep/route diff inspection found no
activation. Restart: none.

The new slow-class entry initially lacked its required reason. Corrected and
verified at `4c6bec33b269c41206c4ea80544421d49d9271be`: CP-RG1, 1/1 passed,
zero failed/skipped. This exact metadata repair guard is not another Unit lane.
The timed-out Unit output also showed the unrelated checkpoint namespace census
426-versus-429 failure. CP-B1 reproduced it at exact base
`63b2e70479c846cacd01af6b20190e845f8258e4`: 1 executed, 1 failed, zero skipped.
It is inherited and unchanged here. Unit OS skips and canceled output are not
passed; the timeout provides no complete Unit census. No full Unit green claim
or Final/Full Unit qualification is made.

Before implementation, tests were pushed and run over unchanged production:
`27bc01d862690c443e6fd557a46f89e82cf367ab`, CP-13 and CP-14 each 0/7 passed,
all failing `S7 must discover independently bound mirror endpoints`.
This is ordinary pre-slice defect red, not deliberate Mutation.

Intermediate failures were owned and repaired: fixture constructor compile
failure at 350720539; producer namespace import build failure at 720198979;
f9b7520 CP-13 1 passed/6 failed due to the producer treating
`evidence_requires_admission` as a terminal refusal; a7fb4934 CP-13 5 passed/2
fixture failures (EF nontranslated Guid expression and a protected-file setup).
These runs were stopped before editing source. No assertion or timeout was
loosened. Subsequent CP-13/14 reruns passed at 6ccc25b8. The manifest-header
import refusal started no driver. No unchanged proof selection exceeds the
3-normal/2-loaded budget; no loaded repetitions were commissioned.

## Evidence and source provenance

All checkpoint runs were owner-bound (ANTIPHON_TASK_TOKEN present, never
printed). Placement GETs `/api/runner-defaults` and `/api/session-runners`
succeeded. No fleet location was embedded or pinned. Build/test slots were
granted; row waits are retained below. Builds used isolated forward-slash outputs
and UseAppHost=false. Generated TRX/JSON/logs remain ignored. The only setup
build outside a checkpoint row was the leased tool bootstrap, label
f2b2-checkpoint-bootstrap (slot granted, waited 0s); its existing CS8602 warning
was disclosed, no build error. CP-F2/F3 were declared before execution for the
higher-priority Final affected-class scope. CP-B1 was a method-only inherited
failure confirmation; CP-RG1 was the necessary classification repair proof.
The base probe was restarted to consider retaining evidence under the main
root; the tool refused an external resultsRoot before any driver, and the actual
base probe used its permitted default root. No unleased retry occurred.

Main scoped run:
`.antiphon/checkpoints/20261013-143244-460f/report.json`.
Registry repair:
`.antiphon/checkpoints/20261013-150450-1af4/report.json`.
Pre-slice red:
`.antiphon/checkpoints/20261013-140131-f9db/report.json`.
Inherited base probe:
`/tmp/antiphon-f2b2-base-census/.antiphon/checkpoints/20261013-145336-1180/report.json`.
The detached base checkout is retained only to keep its unedited receipts at
their recorded paths; its alternate build outputs have been removed.

`scripts/validate-checkpoint-receipt.ps1` returned exit 0 for selected green
CP-13, CP-14, CP-F2, CP-F3 at 6ccc25b8, and for CP-RG1 at 4c6bec33.
These receipts have dirty=0, sourceState=clean, buildSource=verified. The overall
main run is RED exit 5 because of the disclosed Unit timeout; selected green
qualification does not change that verdict. Base CP-B1 is RED exit 1.
Actual tested SHAs above are not relabeled to the documentation-only report tip.

Unedited CHECKPOINT lines follow.

```text
CHECKPOINT CP-13 commit=27bc01d862690c443e6fd557a46f89e82cf367ab build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip)|(C1017_ReopenBetweenEndpoints)|(C1017_MultipleMirrorsRemainDistinct)|(C1032_Admission*) executed=7 passed=0 failed=7 skipped=0 trx=/work/worktrees/task-f2b2ece7/.antiphon/checkpoints/20261013-140131-f9db/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=27bc01d862690c443e6fd557a46f89e82cf367ab sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=27bc01d862690c443e6fd557a46f89e82cf367ab build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatchOperation)|(C1017_ReceiptMustMatchEndpoint)|(C1017_ReceiptMustMatchStore)|(C1017_ReceiptMustMatchTaskAttempt)|(C1017_AckIsNotRemoval)|(C1017_UnsupportedNeverFallsBack)|(C1032_Receipt*) executed=7 passed=0 failed=7 skipped=0 trx=/work/worktrees/task-f2b2ece7/.antiphon/checkpoints/20261013-140131-f9db/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=27bc01d862690c443e6fd557a46f89e82cf367ab sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9 build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_MirrorBehindPushedTip)|(C1017_ReopenBetweenEndpoints)|(C1017_MultipleMirrorsRemainDistinct)|(C1032_Admission*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-f2b2ece7/.antiphon/checkpoints/20261013-143244-460f/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9 build=ok filter=/*/*/CardWorktreeMirrorCleanupTests/(C1017_ReceiptMustMatchOperation)|(C1017_ReceiptMustMatchEndpoint)|(C1017_ReceiptMustMatchStore)|(C1017_ReceiptMustMatchTaskAttempt)|(C1017_AckIsNotRemoval)|(C1017_UnsupportedNeverFallsBack)|(C1032_Receipt*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-f2b2ece7/.antiphon/checkpoints/20261013-143244-460f/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9 sourceState=clean buildSource=verified
CHECKPOINT CP-F1 commit=6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9 build=reused filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=15m slot=granted waited=0s dirty=0 source=6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9 sourceState=clean buildSource=verified
CHECKPOINT CP-F2 commit=6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9 build=reused filter=/*/*/(CardWorktreeCleanupRecoveryTests*)|(CompletedCardWorktreeRemovalTests*)|(CardWorktreeMirrorCleanupTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-f2b2ece7/.antiphon/checkpoints/20261013-143244-460f/rows/CP-F2/run.trx slot=granted waited=45s dirty=0 source=6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9 sourceState=clean buildSource=verified
CHECKPOINT CP-F3 commit=6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9 build=ok filter=/*/*/(CompletedCardWorkspaceRemovalTests*)|(CompletedCardWorkspaceContractTests*)|(RunnerWorkspaceServiceTests*)|(LegacyWorkspaceRemovalGateTests*)/* executed=144 passed=144 failed=0 skipped=0 trx=/work/worktrees/task-f2b2ece7/.antiphon/checkpoints/20261013-143244-460f/rows/CP-F3/run.trx slot=granted waited=0s dirty=0 source=6ccc25b8f5bb38b0f55ed6684a7f460f20ca5da9 sourceState=clean buildSource=verified
CHECKPOINT CP-RG1 commit=4c6bec33b269c41206c4ea80544421d49d9271be build=ok filter=/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f2b2ece7/.antiphon/checkpoints/20261013-150450-1af4/rows/CP-RG1/run.trx slot=granted waited=15s dirty=0 source=4c6bec33b269c41206c4ea80544421d49d9271be sourceState=clean buildSource=verified
CHECKPOINT CP-B1 commit=63b2e70479c846cacd01af6b20190e845f8258e4 build=ok filter=/*/*/CheckpointNamespaceCensusUsageTests/namespace_census_matches_compiled_checkpoint_cases* executed=1 passed=0 failed=1 skipped=0 trx=/tmp/antiphon-f2b2-base-census/.antiphon/checkpoints/20261013-145336-1180/rows/CP-B1/run.trx slot=granted waited=30s dirty=0 source=63b2e70479c846cacd01af6b20190e845f8258e4 sourceState=clean buildSource=verified
```

## Pending controls and deferred work

Post-land SourceLanding Mutation was NOT run. Code's stage contract assigns
all deliberate mutants and missing-control discovery to Mutation, so the
caller-requested throw-away family scratch mutations were not run in Code.
Ordinary malformed/tampered inputs are not deliberate source mutants.

Every plan PC and named variant remains pending. S7 specifically carries
PC-60's moved bound-rebase positive, PC-73..81, PC-271..283, PC-293,
PC-380..399 (field-specific altered/missing/null/unknown variants),
PC-506..510, and PC-581. No prior S2-S6 PC is discharged. S8 PCs/cuts remain
pending on that slice, including PC-71/72, PC-82 and PC-284..292.
New unbound candidates PC-588 certificate bypass (the nine tampered command
variants in C1032_AdmissionBoundRebaseProof), PC-589 missing-receipt fields
(each of the 27 fields), and PC-590 safe external regular-file reads remain
pending for binding/discovery. PC-590 needs a concrete producer special-file
witness/control; it is not claimed as completed ordinary special-file coverage.
Candidate refinement reports belong under `docs/superpowers/mutations/`.
Mutation must bind separate predicates/variants even when they share a method.

Deferred-to-native-Final IDs W-S5-file-identity, W-S5-file-sharing/content,
W-S5-root-identity, W-artifact-reader-junction/sharing remain pending, not passed:
`/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileIdentityRefuses*`;
`/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateFileContentRefuses*`;
`/*/*/CompletedCardWorkspaceRemovalTests/C1032_RemovalLateRootIdentityRefuses*`;
`/*/*/ChannelReplyAttachmentReaderWindowsTests/*`.
Linux passes do not qualify Windows handle/share/reparse behavior.
U-Final-whole-Unit remains disclosed/uncertified following the caller stop
refinement, not deferred as a falsely passed row. S8 CP-15/16 remain out of scope.

Review commands: bootstrap the checkpoint tool under build-slot if needed, then
run the frozen design with `--rows CP-13,CP-14 --expected-source-sha <Review HEAD>`.
For the named adjacent classes use the supplement with `--rows CP-F2,CP-F3`;
CP-RG1 is the exact metadata guard if required. Await every run/exit-75 wait.
Do not select the historical whole-Unit row. Review precedes landing the original
Code task; caller then commissions SourceLanding Mutation on the confirmed land.

## Closing checks

Fetched origin/master `c6bbb1bb115c28b16529750c06424c89562056f6`; merge-tree
against the code/metadata tip 4c6bec33 returned exit 0, tree
`51d185ad4776ad0c2f3184af2b801d7579163ce8`. The final documentation-only
closure is checked again before reporting. Source worktree clean before closure;
tracked file count before this evidence file: 7,564 (7,565 including this file).
`git diff --check` passed. Full task-range check-evidence-diff at 4c6bec33:
9 commits, zero generated-evidence entries, zero violations; repeated over the
final base..HEAD range before reporting. Alternate test outputs were removed by
the checkpoint owner; the leased tool bootstrap output is removed at closure.
Only ignored reports/logs/receipts remain. No build/test process is left running.
The branch push is checked against `git ls-remote` and the final full SHA is in
the caller report. No desktop checkout or production service was changed.

--- next stage ---
next: review
handoff: Review S7 producer/certificate/receipt changes and the frozen CP-13/14 plus recorded full affected-class evidence; keep production inert and all PC/native-Windows obligations pending. Follow the caller's Unit-stop refinement. Landing owner is original Code task f2b2ece7-37bf-4c83-9b74-cfb9e78c1eae; caller lands only after Review and then commissions SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-13-card-1032-s7-code-evidence.md
