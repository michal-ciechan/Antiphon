# CARD-1307 Code report

S1 and S2 are implemented and their commissioned ordinary witnesses/checkpoints
are complete. S3 remains unstarted. The task stops at the S2 boundary within the
60-minute budget; two further isolated builds plus S3 implementation would not fit.
Next stage is Code for S3, followed by ordinary Review when its V-3/R-3 work is done.

Original Code task / landing owner:
`e207a2d4-9d81-40ae-be94-8b688e62b26f`.
Branch: `feat/card-task-e207a2d4`.
Worktree: `/work/worktrees/task-e207a2d4`.
Task base: `7a4e74e3f8a596d65b70347b4ec71b4306b24833`.
Target: master only. Restart: none. No landing or deployment was attempted.

Plan:
`/work/worktrees/task-e207a2d4/docs/superpowers/plans/2026-10-20-card-1307-cancellation-partial-removal-plan.md`.
Unbound candidates:
`/work/worktrees/task-e207a2d4/docs/superpowers/mutations/2026-10-20-card-1307-candidates.md`.

## Implementation and publication

Every source slice was committed and pushed to the assigned ref before its long run.

| Group | Full commit |
|---|---|
| S1 compiling witness, production unchanged; fix the plan's three method filters to carry trailing wildcards | `9396f448f68c41b9673eba6497ea6f1f11c7aafd` |
| S1 implementation and recovery documentation | `0fe7e0409e1fc7bfd82199626b1a6bda8259089f` |
| S2 compiling witness, command production unchanged | `85307b6468d0a8d463a1eb74c2755e5f8dd3eeee` |
| S2 implementation and joined timer-barrier cleanup in its test | `ee2e64e5eab5365d7fbda33ae34768b0d9438277` |

`ToolCopyCleanup` publishes `tool-removal-started.json` through ordinary
`CheckpointWrite` ownership under the cleanup gate, after dead-owner/full authority
admission and before tool deletion. A publication failure permits zero tool
unlinks. A sealed removal failure returns non-clean `Refused` with
`tool-removal-incomplete`, the original failure, tool/marker paths and manual
inspection guidance. A valid prior marker beside remaining tool files stops
re-admission/deletion; it grants no custody. Invalid marker paths refuse.
Successful removal retains the marker; verified manual residue removal leads to
`AlreadyAbsent`. The legacy current-process ledger branch remains unchanged.

`Program.RunAsync` normalizes its Runtime once and uses the caller token before
execution, on failures and after returned results to emit
`canceled: caller-requested` and return `ExitCodes.Canceled = 130`.
Unrequested deadlines/refusals retain their existing classification; 130 is not
added to row precedence. `WaitCommand` prints the cleanup receipt, then honors
cancellation before accessing an earlier report or printing a dead-executor fallback.

Changed production: `src/Antiphon.HostCleanup/Checkpoints/ToolCopyCleanup.cs`,
`tools/Antiphon.Checkpoints/Program.cs`, `Execution/ExitCodes.cs`, and
`Commands/WaitCommand.cs`. Added tests: `C1307ToolRemovalTests.cs` and
`C1307CancellationTests.cs` under `tests/Antiphon.Tests/Infrastructure/`.
The recovery guidance is in `docs/testing-and-build.md`.

`ToolCopyAuthority`, `SafeDelete`, receipt validation, scheduler kill logic,
`ExclusiveDirectory.cs` and `scripts/lib/checkpoint-owned-output.ps1` were not
changed. The last two protected standalone-compilation files have an empty
base-to-implementation diff. Their existing standalone witness was not rerun,
as explicitly specified by the plan.

## Verification scope and outcomes

Round: Final, with the brief's explicit closed-list restriction to each completed
slice's commissioned red and narrow green rows. No full Unit, assembly, native
signal or unrelated integration lane was run. This is not a claim that the whole
card's Final gate is complete: S3 and its ordinary V-3/R-3 remain required.
Virtual I/O proves the exercised ordering, bytes and custody decisions, not native
filesystem races or OS signal delivery.

| Row | Run ID | Executed / passed / failed / skipped | Actual outcome |
|---|---|---|---|
| CP-1 | `20261020-025521-2c4a` | 1 / 0 / 1 / 0 | Expected assertion red, exit 1: missing attempt marker after one actual tool unlink and cancellation |
| CP-2 first | `20261020-030309-3c1d` | 5 / 5 / 0 / 0 | Tests passed; overall exit 2, cleanup `still-present` because the manifest also found the retained CP-1 red output |
| CP-2 permitted rerun | `20261020-031142-fe75` | 5 / 5 / 0 / 0 | GREEN exit 0 after deleting the completed task-owned red output; source certificate validated |
| CP-3 | `20261020-032248-71fa` | 1 / 0 / 1 / 0 | Expected assertion red, exit 1: null result because `OperationCanceledException` escaped instead of returning 130 |
| CP-4 | `20261020-033040-b0f4` | 5 / 5 / 0 / 0 | GREEN exit 0; source certificate validated |
| CP-5 / CP-6 | Not run | No counts claimed | S3 deferred to the next Code task |

Every fresh TRX was inspected for its intended names and nonzero counts. CP-2
contained all five planned S1 methods, and CP-4 all five planned S2 methods.
The red assertions were verified directly in their TRX; neither was a build,
fixture, timeout or zero-selection failure. No red witness was rerun on repaired
source. There were no unexpected assertion failures or skipped tests.

V-1 and R-1: passed (CP-1 intended red, final CP-2 green), including publication
ordering, deadline/cancellation residue, marker and custody refusal, fresh-context
zero-unlink retry, success and manual recovery.
V-2 and R-2: passed (CP-3 intended red, CP-4 green), including raw/wrapped/swallowed
and post-result cancellation, cleanup-before-canceled reporting, non-caller deadline
and OCE controls, and live-executor/receipt non-interference.
V-3 and R-3: NOT RUN; deferred, never marked passed.
Required standalone manual work: protected-file comparison complete; native rerun
excluded by the plan. No other manual acceptance was commissioned for S1/S2.

The one allowed receipt-refusal rerun was used for CP-2, with identical source and
selection after removing leftover CP-1 output. Both historical outcomes remain
above and below. No further receipt-refusal retries were made. CP-2's resumed
`wait` after exit 75 observed the same executor/run and did not rerun tests.
Its two logged owner HTTP 502s recovered under existing policy without a bypass.
No timeout, assertion or exclusion was relaxed.

All five selected builds and all five row drivers report `slot=granted waited=0s`.
Build phase durations were 326.415, 336.526, 441.623, 378.906 and 324.807 seconds
(about 30m08s total). Report wall seconds were 363.601, 372.348, 478.259, 406.306
and 347.547 respectively; report wall does not include all terminal cleanup time.
Each row ran one isolated build; the report's `builds: 6` lists the six manifest
build definitions, not six executed builds per selection.

The only unlisted build was the necessary coordinator bootstrap:

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1307-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1307-tool/ --property:UseAppHost=false --nologo
```

It was built at the tests-first SHA `9396f448f68c41b9673eba6497ea6f1f11c7aafd`:
exit 0, four existing nullable warnings, zero errors, build 9.07s;
`slot=granted waited=15s`, held 10s, lease
`bacc7606-3cba-4cd6-92ed-7a6a0c8275d2`. The unchanged coordinator drove later target
builds at the exact committed expected SHAs recorded in each CHECKPOINT line.
There were no other unlisted builds or tests.

Each selected row used:

```text
dotnet tools/Antiphon.Checkpoints/bin-c1307-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-20-card-1307-cancellation-partial-removal-plan.md --rows <CP-n> --expected-source-sha <committed-group-SHA> --row-timeout 40m --max-wait 570s
```

The same CLI's read-only `validate --evidence <run>/report.json
--expected-source-sha <SHA> --rows <CP-n>` returned:

```text
CHECKPOINT SOURCE VALID source=0fe7e0409e1fc7bfd82199626b1a6bda8259089f rows=1
CHECKPOINT SOURCE VALID source=ee2e64e5eab5365d7fbda33ae34768b0d9438277 rows=1
```

## Custody, evidence and follow-up

All checkpoint commands/executors were awaited to terminal results. Both expected
red output roots were removed only after their run ended, using exact allowlisted
paths, containment/tracked/link checks, individual file unlinks and nonrecursive
directory removal. Each held 2247 files and 369 directories. Original build stamps
were copied byte-for-byte to ignored `.antiphon/c1307-provenance/` before removal.
Successful green runs removed their own output roots. The two task-owned bootstrap
output roots were removed at closure (37 files/14 directories and 3 files/1 directory).
No foreign output or daemon bin directory was part of this cleanup.

Original ignored evidence remains under
`/work/worktrees/task-e207a2d4/.antiphon/checkpoints/<run-id>/`, including TRX,
JSON reports, logs, state and custody receipts. No generated payload directory is
committed. This individual Markdown report is the permitted retained report.
The source-history guard over full task base..implementation HEAD passed:

```text
EVIDENCE result commits=4 entries=0 violations=0 base=7a4e74e3f8a596d65b70347b4ec71b4306b24833 head=ee2e64e5eab5365d7fbda33ae34768b0d9438277
```

The guard and merge checks are repeated at closure against the final report tip.
`origin/master` was fetched as `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`;
the additional required compatibility target is
`359cc912235d840e9f94202592e86480d87db468`.
Final full-tip outcomes and publication SHA are in the caller summary.

GET `/api/runner-defaults` and GET `/api/session-runners` were read. Defaults
revision 2 named an unavailable default; eligible Linux and Windows placements
were present. No Runner or Platform pin or fleet location was embedded.

The plan has zero PC-n rows. All unbound C-1..C-8 candidate variants in the linked
Mutation document are PENDING; no deliberate mutants or SourceLanding cycle ran.
Mutation must bind the controls and discover missing ones, including with a
zero-PC plan. Ordinary baseline-red witnesses do not discharge Mutation.

Continue S3: console handler lifetime/helper exclusion, Row's probe/acquire/driver
tokens, executor cleanup and terminal 130 persistence, and all five S3 methods.
Run CP-5 at its pushed compiling witness SHA, then CP-6 at its pushed fix SHA, with
the commissioned wildcard and 40m row timeout. V-3/R-3 remain open. After ordinary
verification is complete, commission Review; the caller then lands the original
Code task and commissions SourceLanding Mutation.

## Unedited checkpoint receipts and phase costs


Run 20261020-025521-2c4a

```text
CHECKPOINT CP-1 commit=9396f448f68c41b9673eba6497ea6f1f11c7aafd build=ok filter=/*/*/C1307ToolRemovalTests/Mid_removal_cancellation_leaves_marker* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-e207a2d4/.antiphon/checkpoints/20261020-025521-2c4a/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=9396f448f68c41b9673eba6497ea6f1f11c7aafd sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=326.4146075s startup=5.6485462s testsWall=0.9829182s teardown=0.8400747s hostWall=7.475803s
```

Run 20261020-030309-3c1d

```text
CHECKPOINT CP-2 commit=0fe7e0409e1fc7bfd82199626b1a6bda8259089f build=ok filter=/*/*/C1307ToolRemovalTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-e207a2d4/.antiphon/checkpoints/20261020-030309-3c1d/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=0fe7e0409e1fc7bfd82199626b1a6bda8259089f sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=336.5255044s startup=6.0413215s testsWall=0.4103251s teardown=0.516544s hostWall=6.9685509s
```

Run 20261020-031142-fe75

```text
CHECKPOINT CP-2 commit=0fe7e0409e1fc7bfd82199626b1a6bda8259089f build=ok filter=/*/*/C1307ToolRemovalTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-e207a2d4/.antiphon/checkpoints/20261020-031142-fe75/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=0fe7e0409e1fc7bfd82199626b1a6bda8259089f sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=441.6234419s startup=7.9706562s testsWall=0.6391342s teardown=0.8387548s hostWall=9.4484169s
```

Run 20261020-032248-71fa

```text
CHECKPOINT CP-3 commit=85307b6468d0a8d463a1eb74c2755e5f8dd3eeee build=ok filter=/*/*/C1307CancellationTests/Canceled_wait_returns_130* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-e207a2d4/.antiphon/checkpoints/20261020-032248-71fa/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=85307b6468d0a8d463a1eb74c2755e5f8dd3eeee sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=378.9061919s startup=4.5065233s testsWall=0.4781327s teardown=0.3993932s hostWall=5.3841855s
```

Run 20261020-033040-b0f4

```text
CHECKPOINT CP-4 commit=ee2e64e5eab5365d7fbda33ae34768b0d9438277 build=ok filter=/*/*/C1307CancellationTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-e207a2d4/.antiphon/checkpoints/20261020-033040-b0f4/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=ee2e64e5eab5365d7fbda33ae34768b0d9438277 sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=324.8071646s startup=5.6892774s testsWall=1.0088389s teardown=0.621165s hostWall=7.3193513s
```
