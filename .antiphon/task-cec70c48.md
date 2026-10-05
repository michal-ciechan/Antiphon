# CARD-1070 Code report

Fixed the specialist-role guard and the unsupported-response fixture regression;
all commissioned Final checks pass. The suspected force-release production defect
was a fixture defect: production HTTP/phone-home rejection already has no force
fallback. Existing assertions were preserved and HTTP 501 coverage was added.

Original Code task / landing owner: `cec70c48-9462-4b67-b32f-3177069f595a`.
Branch: `feat/card-task-cec70c48`.
Worktree: `/work/worktrees/task-cec70c48`.
Base: `d2fe587a55efcee7981aca2188d206d6d1eaadaa`.
Final implementation and actual tested SHA:
`cffeebefbd43dc5c77cfd98a125a371d9d4c1728`.
The subsequent report-only commit does not change implementation or tests.
Plan: `docs/superpowers/plans/2026-10-05-card-1070-seat-release-verification.md`.
Final receipt: `.antiphon/checkpoints/20261005-173435-6997/report.json`.

## Changes and regression attribution

- `server/Application/Services/TerminalRunnerSeatReleasePolicy.cs`: use
  `AgentTaskRoles.IsSpecialist(task.Role)`. This is exactly the sanctioned helper
  named by the guard, with the same Check/Distill/Diagnose predicate. No allowlist change.
- `tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs`: reject an
  unsupported request before the release-specific handler can manufacture a
  Released receipt. The supported behavior is unchanged. Unsupported status is
  selectable for the two production HTTP rejection codes.
- `tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs`: retain
  all existing assertions; parameterize the no-force test with HTTP 404 and 501.
  Both cases exercise the phone-home unsupported-operation peer too.

Source introduction: `9f1b274dfae8cc8d1845949f344d8487ca93b60e` (S3d test authoring).
Its fixture diff adds the `/release-terminal-seat` success branch before the
existing Unsupported check. The HTTP client then correctly decodes the fixture's
200/Released response. This is not an observed force command.

Executed a first-parent, relevant-path `git bisect` from the supplied S3a green
bound `7a3eabd3d6b740f65547ff25e6aa1e9a70ff65d1` to the initial task source.
The supplied S3a CP-12 11/11 receipt is the good bound; it was not rerun here.
Each historical run used only
`/*/*/TerminalRunnerSeatReleaseTests*/Unsupported_server_transport_never_falls_back_to_force*`.
Four historical builds executed one test each and reproduced the precise
Unsupported-versus-Released assertion:

| SHA | Executed / passed / failed |
|---|---|
| f68011ab5c4e789d9e766313d7cc4014dd239729 | 1 / 0 / 1 |
| b4bbc982cef42ac508ce2a22542540b3b46a3d17 | 1 / 0 / 1 |
| 664bf7009892803e047ee3e8d4608c0074da37d2 | 1 / 0 / 1 |
| ddc7ba3f1f0f2db5c5a1d13ec5357941fad8a221 | 1 / 0 / 1 |

Two commits were unbuildable and were skipped, not called behavioral red:
`33d2dd91be70dfc8d9e86ca1965f717d2ec0ef14` (CS1503 assertion overload),
`9f1b274dfae8cc8d1845949f344d8487ca93b60e` (CS1061 DateTime.Value in
RunnerSeatOrphanSweepTests). Git bisect alone therefore ended with candidates
9f1b274d/664bf700. The additional run at immediate successor
`ddc7ba3f1f0f2db5c5a1d13ec5357941fad8a221` resolves the production-versus-fixture
question: it only repairs that unrelated nullable compile error and already
reproduces the defect before 664bf700 adds production dispatch/recovery.
Attribution to 9f1b274d combines that runtime evidence and its exact source diff;
it is not a claim that unbuildable 9f1b274d executed successfully.

Historical receipts/logs remain under `.antiphon/checkpoints/bisect-<full-sha>/`;
the bisect log is `.antiphon/bisect-c1070-result.txt`. The owned detached worktree
was removed after every historical driver completed.

## Ordinary verification

Final scope follows the explicit narrow brief: no whole Unit run. No full
assembly or unbounded impact selection. The two full named integration classes
were each run once after repair. There are no deferred required V/R IDs in this
repair round; the broader CARD-0667 plan is not discharged by these checks.

| ID | Checkpoint | Actual outcome |
|---|---|---|
| V-1 | CP-1 role guard | 1 executed, 1 passed, 0 failed/skipped |
| V-2 | CP-2 HTTP 404/501 and phone-home no-force | 2 executed, 2 passed, 0 failed/skipped |
| R-1 | CP-3 full TerminalRunnerSeatReleaseTests | 22 executed, 22 passed, 0 failed/skipped; all 18 methods |
| R-2 | CP-4 full TerminalSeatReleaseTests | 38 executed, 38 passed, 0 failed/skipped; all 34 methods |

Fresh TRX counters, actual class names and every source test method were inspected;
both full-class method rosters match. The temporary inspection script initially
included a nested helper named CutCommit; restricting the source roster to the
actual class methods corrected that read-only audit. No tests were rerun for it.

All final rows have dirty=0, sourceState=clean, buildSource=verified,
slot=granted, waited=0s, and the exact expected SHA above. Receipt validator output:

```text
CHECKPOINT SOURCE VALID source=cffeebefbd43dc5c77cfd98a125a371d9d4c1728 rows=4
```

Initial diagnostic run at docs-only SHA
`512c5451aff987d985a10a8facd03888b377d352` reproduced the inherited role guard
(1 failed) and unsupported transport (1 failed). Test-authoring run at
`99620e36cdd55f7472c399466627c1bcf643808e` passed the role guard (1/1) and failed
both HTTP variants (0/2) at the intended Unsupported-versus-Released assertion.
Thus the new 501 variant went red before the fixture repair. Final repair used
no timeout increase, retry, assertion relaxation, or deliberate production mutant.
CP-1/CP-2 each have two subsequent executions across changed committed source;
CP-3/CP-4 have no repeats. No loaded repetitions.

Declared extra work: one checkpoint-tool bootstrap build through
`scripts/build-slot.ps1` (success, one existing CS8602 warning, slot=granted,
waited=0s), the inherited-red/test-authoring selections, and the six historical
single-method checkpoint builds described above. No unlisted full-suite run.
All test drivers were owned and awaited; all historical rows used the checkpoint
row tool's host gate. The final run took 6m08s. The tool deleted its server/runner
outputs, and 29 remaining owned alternate output directories were removed.

M-1 PASS: authenticated GET /api/runner-defaults and /api/session-runners read
before implementation; no runner/platform pin or configuration change.
M-2 PASS (source/card audit, not native test execution): both C1050 Windows audit
methods inherit Category Unit and immediately assert OperatingSystem.IsWindows.
The Linux Unit lane should exclude exactly those two methods (or a future
Windows-only category), preserving portable methods and running the audits on
native Windows. This guidance is saved on CARD-1070 revision 3 and verified by
fresh read. The card edit was accepted before the script's local C:-drive output
error. Neither native audit was changed or reported passed.
M-3 PASS: AutomaticEnabled remains false; no migration/configuration change.
`git diff --check` passed. The full base..HEAD evidence-history check passed before
this report and is rerun over the report commit before settlement.

## Pending Mutation and handoff

PC-1070-1: specialist predicate bypass (Check/Distill/Diagnose custody variants).
PC-1070-2: unsupported acceptance/force fallback (HTTP 404, HTTP 501, phone-home
unsupported-operation variants). All remain pending for method-scoped
SourceLanding Mutation; Mutation owns deliberate red/restore/green and missing
control discovery. All inherited CARD-0667 PC-1..PC-104 and every variant in
`docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`
also remain pending, not discharged here.

Restart: none for this task; caller owns any separately commissioned server
activation. Next: ordinary Review, retaining original Code task cec70c48 as
landing owner. Caller then lands that task and commissions SourceLanding Mutation.

## Unedited checkpoint lines

Run 20261005-171156-cf2c

```text
CHECKPOINT CP-1 commit=512c5451aff987d985a10a8facd03888b377d352 build=ok filter=/*/*/SpecialistRoleContractTests*/no_Check_comparison_survives_outside_the_allowlist* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/20261005-171156-cf2c/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=512c5451aff987d985a10a8facd03888b377d352 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=512c5451aff987d985a10a8facd03888b377d352 build=reused filter=/*/*/TerminalRunnerSeatReleaseTests*/Unsupported_server_transport_never_falls_back_to_force* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/20261005-171156-cf2c/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=512c5451aff987d985a10a8facd03888b377d352 sourceState=clean buildSource=verified
```

Run 20261005-173128-2ed6

```text
CHECKPOINT CP-1 commit=99620e36cdd55f7472c399466627c1bcf643808e build=ok filter=/*/*/SpecialistRoleContractTests*/no_Check_comparison_survives_outside_the_allowlist* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/20261005-173128-2ed6/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=99620e36cdd55f7472c399466627c1bcf643808e sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=99620e36cdd55f7472c399466627c1bcf643808e build=reused filter=/*/*/TerminalRunnerSeatReleaseTests*/Unsupported_server_transport_never_falls_back_to_force* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/20261005-173128-2ed6/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=99620e36cdd55f7472c399466627c1bcf643808e sourceState=clean buildSource=verified
```

Run 20261005-173435-6997

```text
CHECKPOINT CP-1 commit=cffeebefbd43dc5c77cfd98a125a371d9d4c1728 build=ok filter=/*/*/SpecialistRoleContractTests*/no_Check_comparison_survives_outside_the_allowlist* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/20261005-173435-6997/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=cffeebefbd43dc5c77cfd98a125a371d9d4c1728 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=cffeebefbd43dc5c77cfd98a125a371d9d4c1728 build=reused filter=/*/*/TerminalRunnerSeatReleaseTests*/Unsupported_server_transport_never_falls_back_to_force* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/20261005-173435-6997/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=cffeebefbd43dc5c77cfd98a125a371d9d4c1728 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=cffeebefbd43dc5c77cfd98a125a371d9d4c1728 build=reused filter=/*/*/TerminalRunnerSeatReleaseTests*/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/20261005-173435-6997/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=cffeebefbd43dc5c77cfd98a125a371d9d4c1728 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=cffeebefbd43dc5c77cfd98a125a371d9d4c1728 build=ok filter=/*/*/TerminalSeatReleaseTests*/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/20261005-173435-6997/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=cffeebefbd43dc5c77cfd98a125a371d9d4c1728 sourceState=clean buildSource=verified
```

bisect-33d2dd91be70dfc8d9e86ca1965f717d2ec0ef14.log

```text
CHECKPOINT BISECT build=failed project=tests/Antiphon.Tests outputPath=bin-c1070-bisect/ exit=1 slot=granted waited=0s
CHECKPOINT BISECT EXIT CODE: 2
```

bisect-664bf7009892803e047ee3e8d4608c0074da37d2.log

```text
CHECKPOINT BISECT commit=664bf7009892803e047ee3e8d4608c0074da37d2 build=ok filter=/*/*/TerminalRunnerSeatReleaseTests*/Unsupported_server_transport_never_falls_back_to_force* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/bisect-664bf7009892803e047ee3e8d4608c0074da37d2/BISECT-20261005-172312-cdcb/run.trx slot=granted waited=0s dirty=0 source=664bf7009892803e047ee3e8d4608c0074da37d2 sourceState=clean buildSource=verified
CHECKPOINT BISECT EXIT CODE: 1
```

bisect-9f1b274dfae8cc8d1845949f344d8487ca93b60e.log

```text
CHECKPOINT BISECT build=failed project=tests/Antiphon.Tests outputPath=bin-c1070-bisect/ exit=1 slot=granted waited=0s
CHECKPOINT BISECT EXIT CODE: 2
```

bisect-b4bbc982cef42ac508ce2a22542540b3b46a3d17.log

```text
CHECKPOINT BISECT commit=b4bbc982cef42ac508ce2a22542540b3b46a3d17 build=ok filter=/*/*/TerminalRunnerSeatReleaseTests*/Unsupported_server_transport_never_falls_back_to_force* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/bisect-b4bbc982cef42ac508ce2a22542540b3b46a3d17/BISECT-20261005-172029-e8aa/run.trx slot=granted waited=0s dirty=0 source=b4bbc982cef42ac508ce2a22542540b3b46a3d17 sourceState=clean buildSource=verified
CHECKPOINT BISECT EXIT CODE: 1
```

bisect-ddc7ba3f1f0f2db5c5a1d13ec5357941fad8a221.log

```text
CHECKPOINT BISECT commit=ddc7ba3f1f0f2db5c5a1d13ec5357941fad8a221 build=ok filter=/*/*/TerminalRunnerSeatReleaseTests*/Unsupported_server_transport_never_falls_back_to_force* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/bisect-ddc7ba3f1f0f2db5c5a1d13ec5357941fad8a221/BISECT-20261005-172755-9bf9/run.trx slot=granted waited=0s dirty=0 source=ddc7ba3f1f0f2db5c5a1d13ec5357941fad8a221 sourceState=clean buildSource=verified
CHECKPOINT BISECT EXIT CODE: 1
```

bisect-f68011ab5c4e789d9e766313d7cc4014dd239729.log

```text
CHECKPOINT BISECT commit=f68011ab5c4e789d9e766313d7cc4014dd239729 build=ok filter=/*/*/TerminalRunnerSeatReleaseTests*/Unsupported_server_transport_never_falls_back_to_force* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-cec70c48/.antiphon/checkpoints/bisect-f68011ab5c4e789d9e766313d7cc4014dd239729/BISECT-20261005-171527-ba67/run.trx slot=granted waited=0s dirty=0 source=f68011ab5c4e789d9e766313d7cc4014dd239729 sourceState=clean buildSource=verified
CHECKPOINT BISECT EXIT CODE: 1
```

Git bisect record:

```text
# bad: [512c5451aff987d985a10a8facd03888b377d352] docs(card-1070): declare narrow repair checkpoints and historical bisect budget
# good: [7a3eabd3d6b740f65547ff25e6aa1e9a70ff65d1] CARD-0667 S3a: record CP-12 11/11 green and pending Mutation controls
git bisect start '--first-parent' '512c5451aff987d985a10a8facd03888b377d352' '7a3eabd3d6b740f65547ff25e6aa1e9a70ff65d1' '--' 'tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs' 'tests/Antiphon.Tests/Application/TerminalRunnerSeatReleaseTests.cs' 'server/Application/Services/TerminalRunnerSeatReleaseService.cs' 'server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs' 'server/Infrastructure/Agents/SessionRunner/RoutingSessionRunnerClient.cs' 'server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs'
# bad: [f68011ab5c4e789d9e766313d7cc4014dd239729] test(card-0667): repair S3c restart and clock fixture after incomplete red run
git bisect bad f68011ab5c4e789d9e766313d7cc4014dd239729
# skip: [33d2dd91be70dfc8d9e86ca1965f717d2ec0ef14] test(card-0667): add S3b answer transaction witnesses; verification pending
git bisect skip 33d2dd91be70dfc8d9e86ca1965f717d2ec0ef14
# bad: [b4bbc982cef42ac508ce2a22542540b3b46a3d17] test(card-0667): fix assertion overload after CP-13 compile failure
git bisect bad b4bbc982cef42ac508ce2a22542540b3b46a3d17
# bad: [664bf7009892803e047ee3e8d4608c0074da37d2] feat(card-0667): persist release receipts and reconcile ambiguous actions; CP-15 verification pending
git bisect bad 664bf7009892803e047ee3e8d4608c0074da37d2
# skip: [9f1b274dfae8cc8d1845949f344d8487ca93b60e] test(card-0667): specify S3d receipt recovery; CP-15 red pending
git bisect skip 9f1b274dfae8cc8d1845949f344d8487ca93b60e
# only skipped commits left to test
# possible first bad commit: [664bf7009892803e047ee3e8d4608c0074da37d2] feat(card-0667): persist release receipts and reconcile ambiguous actions; CP-15 verification pending
# possible first bad commit: [ddc7ba3f1f0f2db5c5a1d13ec5357941fad8a221] test(card-0667): use nonnullable session generation in response witness
# possible first bad commit: [9f1b274dfae8cc8d1845949f344d8487ca93b60e] test(card-0667): specify S3d receipt recovery; CP-15 red pending
```
