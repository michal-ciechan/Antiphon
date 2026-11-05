# CARD-1718 B-1r2 Code repair — task 0c0e9de1

Fixed the ignored nested-repository secret bypass. Ordinary Linux verification produced
685 passes, zero assertion failures after the fix, and 11 native Windows skips. Historical
production produced seven intended assertion failures before the fix. Strict Final receipt
qualification remains incomplete; this report makes no Clean/Full or reviewedSourceClean claim.

## Source and ownership

- Branch: `feat/card-task-0c0e9de1`.
- Worktree: `/work/worktrees/task-0c0e9de1`.
- Original Code landing owner: `5e05fc80-930f-4f03-8c71-6c323d552d63`.
- Task base: `2d4e80a2169052fee591bd4fafde9f4102c828d6`.
- Red witness commit/tested SHA: `cfa3f897115aee23861531661968c9d99a1287ff`. Production
  bytes are unchanged from task base; the committed descendant adds the tests and plan.
- Production fix: `13e437d796605d8e165c0f0411bb3b097b8b411b`.
- Test limiter qualification fix and actual tested source:
  `7b9976ef7b520b8aa28c4becf41688340aa8becc`.
- Round 3 Review diff base: `40b176ce1225a40bd8290e77975633f17025c4d2`.
- Each slice was pushed to the assigned task ref. No merge, rebase, amendment, force push,
  landing or deployment occurred. The final publication SHA is in the completion report.

The runner now walks trailing-slash entries returned by Git's ignored-file listing. It checks
ancestors and descendants for reparse points, enumerates without following links or ignoring
inaccessible entries, and applies the shared secret floor to descendant files. Control names,
metadata/enumeration failures, regex failures and a ten-second scan deadline keep the tree.
The existing secret-free nested-repository removal test still passes. The helper is shared by
admission, locked recheck, phase B and residue recovery through ReadIgnoredSecretAsync.

Changes are one production file, two existing test classes, a checkpoint plan and a pending
Mutation candidate. The transport test carries the assembly-local process limiter. The native
permission fixture restores its exact original mode in finally. No assertion or existing test
timeout was loosened; no csproj/props input was changed.

Plan: `docs/superpowers/plans/2026-11-05-card-1718-nested-retention-checkpoints.md`.
Mutation candidate: `docs/superpowers/mutations/2026-11-05-card-1718-nested-retention-candidate.md`.

## Invariants and actual outcomes

| ID | Actual outcome |
|---|---|
| V-1 | Two engine parameter cases assertion-red at cfa3f8971, green at 7b9976ef7. Each has one secret and verifies DryRun/Execute, bytes, registration and no move/remove. |
| V-2 | One real server/WebSocket/dispatcher/runner/Git case assertion-red before fix (server path cleared), green after fix. Bytes, Git registration, server path and Held reason asserted. Green duration 2.584 seconds. |
| V-3 | Four cases (file link, directory link, actually unreadable directory, control-character name) assertion-red before fix, green after fix. Native Windows ACL/reparse qualification remains pending. |
| R-1 | Full seven-class runner selection: 241 passed, 0 failed, 1 native skip. |
| R-2 | Full eleven-class expanded server selection: 444 passed, 0 failed, 10 native skips. |
| R-3 | Ignored_nested_repo_goes_with_the_tree: 1 passed. Secret-free nested repositories still go with the tree. |
| V-B1 | Prior admission/locked/phase-B/recovery and real-transport/replay regressions green in the full selections; five appsettings boundary cases and nine independent transport/replay secret cases passed. |
| V-B2 | Prior shared-floor/override/completed-card transport coverage green in the full selections. |
| V-B3 | Prior secret-directory descendant/classifier/physical cleanup coverage green in the full selections. |
| V-Native | PENDING: 11 Windows-only skips, never counted as passes. |
| Whole Unit / prior performance V-2 | Deferred to CARD-1720 by the explicit repair brief; not run or claimed passed here. |

Git 2.47.3's actual collapsed listing is asserted before each of the two engine secret cases
and the one transport case. Each plants one secret independently. These real filesystem/Git
oracles go red on the historical production code; no constant or self-comparison supplies the
retention verdict. No live retention operation or production worktree was used.

## Fresh TRX census

All intended class/method identities were inspected from fresh TRX TestDefinitions and results.
The native-only class has eight reported skips and zero executed tests; it remains pending.

| Assembly / class | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Runner / C1677RetentionDispatchTests | 11 | 0 | 0 |
| Runner / C1677RetentionEngineTests | 81 | 0 | 0 |
| Runner / C1677RetentionInventoryTests | 3 | 0 | 0 |
| Runner / C1677RetentionNativeProcessTests | 14 | 0 | 0 |
| Runner / CompletedCardWorkspaceRemovalTests | 106 | 0 | 1 |
| Runner / RunnerWorkspaceIgnoredContentFloorTests | 25 | 0 | 0 |
| Runner / TestClassificationGuardTests | 1 | 0 | 0 |
| Server / C0824DesktopLegacyReleaseNativeTests | 0 | 0 | 8 |
| Server / C0824DesktopLegacyReleasePolicyTests | 123 | 0 | 0 |
| Server / C0824DesktopLegacyReleaseSettingsTests | 32 | 0 | 0 |
| Server / C1677RetentionServerTests | 71 | 0 | 0 |
| Server / WorktreeCleanupSettingsValidatorTests | 3 | 0 | 0 |
| Server / WorktreeIgnoredContentClassifierTests | 117 | 0 | 0 |
| Server / SlowTestRegistryMergeTests | 5 | 0 | 0 |
| Server / WorktreeGuardedCleanupTests | 51 | 0 | 2 |
| Server / SlowTestTripwireTests | 2 | 0 | 0 |
| Server / TestClassificationPolicyTests | 39 | 0 | 0 |
| Server / TestClassificationGuardTests | 1 | 0 | 0 |

New engine methods expanded to 2 and 4 passed results respectively; the new transport method
expanded to 1 passed result. The existing clean nested-repository control expanded to 1 pass.

## Execution history, costs and qualification limits

Evidence roots are under `/work/worktrees/task-0c0e9de1/.antiphon/checkpoints/`.

| Run | Outcome / retained cost |
|---|---|
| 20261105-064344-a371 | Historical CP-1: 6 intended assertion failures; CP-2: 1 intended assertion failure. Both builds successful, clean source and verified provenance. Wall 2113.235 s; charged execution 582.850 s; admission-only 1530.385 s. |
| Source mismatch launch | Mistyped expected fix SHA rejected before any build/test; no result credited. Corrected immediately. |
| 20261105-072008-159d | Stopped and joined while queued after noticing the unqualified test limiter type. No build/test executed. Stop returned 0; wait returned 6 without a terminal receipt. Namespace correction is committed at 7b9976ef7. |
| 20261105-072103-537b | CP-3 build compiled successfully but confinement refused changed task-generated native obj files from the historical run. No tests executed. Queued sibling stopped and joined; partial state/build log retained. No tracked source changed. Removed the four owned native obj trees and refused output before rerunning. No receipt credited. |
| 20261105-073147-6648 | CP-3: 241 passed, 1 native skip, verified row provenance. CP-4 reached the unchanged combined 28-minute charged execution cap; no TRX, no server result credited. Wall 2101.165 s; execution 1681.034 s; admission-only 420.131 s; exit 5. |
| 20261105-080747-833b | Failure-driven rerun of CP-4 alone, same SHA/filter/18m row/28m execution/45m hard wall limits. 444 passed, 10 native skips. Wall 991.792 s; execution 871.778 s; admission-only 120.014 s. Tool exit 0. |

The one unlisted build was the necessary checkpoint-tool bootstrap through build-slot.ps1,
label c1718-checkpoint-bootstrap: slot granted, waited 241 s, held 6 s, compiler 4.64 s,
0 errors. Native default outputs, UseAppHost=false and TEMP=/tmp. All checkpoint builds and
test rows used their own host leases. No ungated/unlisted test was run. The tool's summary
counts all four manifest build entries, including unused entries; selections built only their
required projects and normal referenced/probe graph. No green selection was repeated.

There was one build-refusal-driven CP-3 retry and one budget-failure-driven CP-4 retry. The
initial queued cancellation is not test execution. Historical red rows were rerun as part of
the full green affected-class rows. No deliberate mutant was run in Code.

The 60-minute estimate was exceeded. Two combined runs exceeded 30 minutes. Caller must
commission the required performance Debug follow-up with these run IDs and costs (related
existing CARD-1720/V-2, CARD-1514/CARD-1531 instrumentation/admission work). No subdelegation
was performed under this delegate's contract. Build/admission versus host/test durations are
retained separately in tool receipts; per-test durations and exact membership are in TRX.
Uninstrumented container/migration/per-call filesystem/HTTP/CLI internal phases remain unknown,
not zero. Slow-class sums overlap and are not wall durations. No timeout was widened.

Strict tool validate results, preserved without promotion:

- CP-3 selected from 20261105-073147-6648: exit 2, report_failed (its parent run's CP-4 timed out).
- CP-4 from 20261105-080747-833b: exit 2, row_failed (10 native skips prevent strict qualification).

Thus actual ordinary Linux passes and clean/verified row tokens are reported, but strict Final
source qualification remains incomplete. A passed skip or reviewedSourceClean=true is not
claimed. Review must retain the incomplete gate and the explicitly deferred native/Unit work.

## Pending Mutation and activation

Every PC/variant remains PENDING for method-scoped SourceLanding Mutation, after Review and
the caller's original-Code landing. New IDs: PC-N1/drop-walk; PC-N2/follow-file-link and
follow-directory-link; PC-N3/unreadable-as-clean; PC-N4/control-name; PC-N5/recipient-drop-walk.
Inherited pending IDs: historical M1/M2, PEM-removal, protected-after-disposable; PC-D3-1/admission,
PC-D3-2/locked, PC-D3-3/phase-B, PC-D3-4/recovery-DryRun and recovery-Execute,
PC-D3-5/unreadable-list, PC-D3-6/runner-floor, PC-D3-7/credentials-descendants,
PC-D3-8/PEM-descendants, PC-D3-9/appsettings, PC-D3-10/initial-transport and final-transport,
PC-D3-11/active-recipient-replay. The separately owned 68 S4a controls and extra mutants retain
their previous pending obligations. Mutation owns deliberate edits, red/restore/green and
missing-control discovery; this is an unbound candidate, not a bound pack.

GET /api/runner-defaults and GET /api/session-runners were read. Portable work stayed on the
assigned Linux checkout with no runner/platform pin. OS-witness inventory lint passed for the
full task base to tested SHA; the unchanged retiring-root Windows check was dispositioned as
unchanged behavior. No startup path/factory changed. Evidence JSON:
`/work/worktrees/task-0c0e9de1/.antiphon/task-0c0e9de1-os-witnesses.json`.

Restart: runner for eventual activation after caller landing; none performed here. No desktop,
AppHost or runner restart occurred. No production port, volume or live session was changed.

Task-owned compiler/bin/obj outputs are removed after execution. Ignored TRX, logs and JSON
receipts remain as required evidence. No generated payload is committed. The Markdown report
alone is an allowed individual report artifact. Full-task evidence-diff and merge-tree checks
are rerun at the published report tip; their final results are in the completion message.

## Unedited checkpoint lines

```
CHECKPOINT CP-1 commit=cfa3f897115aee23861531661968c9d99a1287ff build=ok filter=/*/*/C1677RetentionEngineTests*/(Ignored_nested_repository_secret_keeps_bytes_and_registration*)|(Doubt_inside_collapsed_ignored_repository_keeps_the_tree*) executed=6 passed=0 failed=6 skipped=0 trx=/work/worktrees/task-0c0e9de1/.antiphon/checkpoints/20261105-064344-a371/rows/CP-1/run.trx slot=granted waited=150s dirty=0 source=cfa3f897115aee23861531661968c9d99a1287ff sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=cfa3f897115aee23861531661968c9d99a1287ff build=ok filter=/*/*/C1677RetentionServerTests*/Ignored_nested_repository_secret_survives_real_transport* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-0c0e9de1/.antiphon/checkpoints/20261105-064344-a371/rows/CP-2/run.trx slot=granted waited=330s dirty=0 source=cfa3f897115aee23861531661968c9d99a1287ff sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=7b9976ef7b520b8aa28c4becf41688340aa8becc build=ok filter=/*/*/(RunnerWorkspaceIgnoredContentFloorTests*)|(C1677RetentionEngineTests*)|(C1677RetentionDispatchTests*)|(C1677RetentionInventoryTests*)|(C1677RetentionNativeProcessTests*)|(CompletedCardWorkspaceRemovalTests*)|(TestClassificationGuardTests*)/* executed=241 passed=241 failed=0 skipped=1 trx=/work/worktrees/task-0c0e9de1/.antiphon/checkpoints/20261105-073147-6648/rows/CP-3/run.trx slot=granted waited=135s dirty=0 source=7b9976ef7b520b8aa28c4becf41688340aa8becc sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=7b9976ef7b520b8aa28c4becf41688340aa8becc build=n/a filter=/*/*/(C1677RetentionServerTests*)|(WorktreeIgnoredContentClassifierTests*)|(WorktreeCleanupSettingsValidatorTests*)|(WorktreeGuardedCleanupTests*)|(C0824DesktopLegacyRelease*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=granted waited=90s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-4 commit=7b9976ef7b520b8aa28c4becf41688340aa8becc build=ok filter=/*/*/(C1677RetentionServerTests*)|(WorktreeIgnoredContentClassifierTests*)|(WorktreeCleanupSettingsValidatorTests*)|(WorktreeGuardedCleanupTests*)|(C0824DesktopLegacyRelease*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)/* executed=444 passed=444 failed=0 skipped=10 trx=/work/worktrees/task-0c0e9de1/.antiphon/checkpoints/20261105-080747-833b/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=7b9976ef7b520b8aa28c4becf41688340aa8becc sourceState=clean buildSource=verified
```
