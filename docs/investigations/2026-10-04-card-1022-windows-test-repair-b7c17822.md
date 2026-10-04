# CARD-1022 Windows test fixture repairs

Both reported defects reproduced on Linux through portable fixtures and passed after
test-only repairs. Native Windows qualification remains pending; this is not an overall
Final verification pass. No production code, assertion or timeout changed.

Landing owner: Code task `b7c17822-63b4-4626-b5bb-525cfccc0cea`, superseding
`e41a7005-3049-48f9-957a-162b4a4f9fbb` as instructed. Branch
`feat/card-task-b7c17822`; worktree `/work/worktrees/task-b7c17822`.
Task base: `4e865e05d288374c0d61d8599080a5c399db5f4e`; the linear branch carries
the previous implementation. No merge, rebase, reset or force push occurred.
Plan: [release A verification design](../superpowers/plans/2026-10-03-card-1022-modern-conpty-only-plan.md).
Prior evidence: [original Code note](2026-10-04-card-1022-code-e41a7005.md).
The requested `.antiphon/task-e41a7005.md` was absent in this mirror.

## Defects and fixes

1. `src/Antiphon.FakeClaude/Program.cs:804` serializes `message.content` as a string.
   `FakeClaudeContractTests.cs:646` already reads that string, and
   `ClaudeSubmitConfirmCanaryTests.cs:277` accepts string and array shapes. The freeze
   does not require changing FakeClaude's shape. `C1022TypedInputTests.cs:101` now
   accepts the string or concatenates text blocks; native typed/paste, clipping,
   whole-body, marker and separate-Enter assertions remain intact. Two portable
   arguments cover both shapes with Unicode, CRLF JSONL delimiters/body, a non-text
   block, an unrelated record and an unfinished trailing line. Before repair the
   string argument threw the exact reported Array/String exception; the array passed.
2. `SessionQueueReceiptPlumbingTests.cs:456` stops the pump before inserting the
   synthetic receipt. A stale full prompt at sequence 0 is below the initial TurnEnd
   at 1; a partial current prompt at 2 is above it. The SQL working-state predicate
   (`server/Infrastructure/Data/TranscriptWorkingStateQuery.cs:21`, `:39`) treats the
   latter as activity. `SessionMessageQueueService.cs:1639` therefore returns Nothing
   before recovery, leaving Sent. This is deterministic on Linux too, not Windows
   timing or a product platform difference. An offset clock cannot end that turn.
   Waiting for Pending would never help with the pump stopped and no background queue
   worker. The shared fixture now adds TurnEnd at 3 for the partial case before the
   idle-only flush (`SessionQueueReceiptPlumbingTests.cs:527`). The stale case is
   unchanged. This matches sibling `SessionMessageQueueDeliveryVerificationTests.cs:1491`.
   Recovery awaits truncation handling (`SessionMessageQueueService.cs:2593`), which
   persists Pending/Truncated before returning (`:4219`, `:4266`). The new portable
   real-queue test shares the native test's seed helper and reproduced the exact
   Pending-versus-Sent assertion before repair; after repair it verifies the cap,
   baseline, partial destination body and zero input writes.

Both original failing methods explicitly skip on Linux (`C1022TypedInputTests.cs:43`,
`SessionQueueReceiptPlumbingTests.cs:429`). Earlier Linux success did not execute
their native bodies. These portable reproductions establish the fixture causes;
they do not replace Windows recipient evidence.

## Commits and verification

Every slice was committed and pushed immediately:

| Commit | Change/outcome at commit time |
|---|---|
| `e2086c6cad31edf113d513e6b2d46b53926f9541` | Portable reproducers and shared seed extraction; subsequently reproduced both failures |
| `18c91049030484e230a0a1cd7d38938e2890921f` | String/block reader fix; green pending |
| `c552008b2e5564520906ddbcf18beccf18a18f46` | Receipt turn-boundary fix; green pending |
| `7e579dfda95b0945e201eb8c133243a208a19874` | Plan roster update; exact source of all repaired runs below |

The final reporting commit adds only this note. The caller's final SHA C is that
pushed HEAD; do not relabel the tested SHA above as C.

| Selection | Passed | Failed | Skipped | Evidence |
|---|---:|---:|---:|---|
| REPRO-TYPED | 1 | 1 | 0 | Exact String/Array exception at pre-fix reader |
| REPRO-RECEIPT | 0 | 1 | 0 | Exact Pending expected / Sent actual assertion |
| REPAIR-TYPED, whole class | 2 | 0 | 1 | Both portable argument rows; Windows peer pending |
| REPAIR-RECEIPT, whole class | 6 | 0 | 16 | Five pump cuts plus portable partial-receipt regression; native cases pending |
| CP-1 | 30 | 0 | 0 | Policy 8, DA1 parser 22 |
| CP-2 | 12 | 0 | 0 | Capability coherence 5, existing capabilities 7 |
| CP-9 | 18 | 0 | 0 | Unix argv 17, environment guard 1 |

Every fresh TRX's intended class/method/argument roster and nonzero counts were
inspected. All seven runs have equal clean start/end source snapshots and verified
build provenance. The checkpoint tool report passed `validate-checkpoint-receipt.ps1`
at its actual SHA for all three selected rows. The two repaired class selections
have known Windows skips and are not zero-skip qualification certificates.

The original manifest's CP-1/2/9 ran once via the required checkpoint tool (`run`,
then `wait` after each exit 75, terminal exit 0), serially. The tool DLL came from
the receipt project's build; no standalone bootstrap build was needed. The four
direct `run-checkpoint.ps1` selections outside the original nine rows were the
brief-authorized two portable reproductions and two full affected classes; all
used isolated outputs, prefix filters and committed full expected SHAs. Each new
case had one pre-fix execution and one post-fix execution; no loaded repetitions,
mutants or further green repeats ran. Every driver reported `slot=granted waited=0s`.
The final server-class build took 6m38s and the CP tool group 3m11s wall.

No Unit or full-assembly rerun: the specific repair brief forbids it and supplies the
owner's baseline (4,030 passed / 15 inherited missing-jq failures / 53 skipped).
That baseline is historical, not new green evidence. The repair has no unbounded
shared production impact. No archives were made; generated receipts/TRX/logs remain
ignored. All owned alternate outputs were removed after their processes exited.
The final full-range evidence guards are run after committing this note, over both
the assigned task base and original release-A implementation base through final HEAD.

## Invariants and handoff

| ID | This dispatch's outcome |
|---|---|
| V-1 | Passed: CP-1 policy 8/8 |
| V-2 | Passed: CP-2 12/12 |
| V-3 | Not rerun here; caller's prior Windows CP-4 passed, final-SHA native qualification remains caller-owned |
| V-4 | Portable pump/recovery 6 passed; native queue and repaired negative case pending Windows CP-7 |
| R-1 | Parser 22 passed; native package/host/marker/DA1 requires Windows CP-3 |
| R-2 | Linux environment guard passed; Windows guard qualification pending at final SHA |
| R-3 | Unix argv 17 passed; caller's prior Windows shadow-pair CP-5 passed, no fresh native run here |
| R-4 | No changed production scope; caller's prior Windows CP-6 passed, not rerun here |
| R-5 | Caller prior modern C1011 passed; included again in pending CP-7 |
| R-6 | String/block regression 2 passed; native typed/paste receipt pending CP-3 |

The active nine-row plan now includes the three portable results: Windows CP-3
floor **20**, CP-7 floor **24** (its method filter explicitly includes the portable
receipt case), Windows group total **95**, overall **155**. Estimates and all old
assertions are unchanged. At final SHA C, caller reruns native Windows CP-3 and CP-7
with the updated plan and exact expected SHA, checks every method and zero skips,
then completes the remaining final-SHA qualification/ordinary Review obligations.
Land this original landing-owner Code task **b7c17822**, then commission SourceLanding
Mutation. All **PC-1 through PC-73**, every argument and internal boundary variant,
remain pending for Mutation, including missing-control discovery for added guards.
Neither portable red-first evidence nor ordinary green discharges any PC.

Read-only platform admission read both `/api/runner-defaults` (revision 2) and
`/api/session-runners`; Windows and Linux were eligible. No placement was embedded.
Restart: **none**. Activation owner: **caller**, after land and native qualification,
using the plan's Windows-runner-first procedure from the canonical checkout.

## Essential unedited checkpoint receipts

```text
CHECKPOINT REPRO-TYPED commit=e2086c6cad31edf113d513e6b2d46b53926f9541 build=ok filter=/*/*/C1022TypedInputTests/ReadPrompts_preserves_string_and_text_block_bodies* executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-b7c17822/.antiphon/checkpoints/REPRO-TYPED-20261004-060512-1aaf/run.trx slot=granted waited=0s dirty=0 source=e2086c6cad31edf113d513e6b2d46b53926f9541 sourceState=clean buildSource=verified
CHECKPOINT REPRO-RECEIPT commit=e2086c6cad31edf113d513e6b2d46b53926f9541 build=ok filter=/*/*/SessionQueueReceiptPlumbingTests/C1022_Partial_receipt_turn_parks_interrupted_attempt* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-b7c17822/.antiphon/checkpoints/REPRO-RECEIPT-20261004-060611-1336/run.trx slot=granted waited=0s dirty=0 source=e2086c6cad31edf113d513e6b2d46b53926f9541 sourceState=clean buildSource=verified
CHECKPOINT REPAIR-TYPED commit=7e579dfda95b0945e201eb8c133243a208a19874 build=ok filter=/*/*/C1022TypedInputTests/* executed=2 passed=2 failed=0 skipped=1 trx=/work/worktrees/task-b7c17822/.antiphon/checkpoints/REPAIR-TYPED-20261004-061229-df00/run.trx slot=granted waited=0s dirty=0 source=7e579dfda95b0945e201eb8c133243a208a19874 sourceState=clean buildSource=verified
CHECKPOINT REPAIR-RECEIPT commit=7e579dfda95b0945e201eb8c133243a208a19874 build=ok filter=/*/*/SessionQueueReceiptPlumbingTests/* executed=6 passed=6 failed=0 skipped=16 trx=/work/worktrees/task-b7c17822/.antiphon/checkpoints/REPAIR-RECEIPT-20261004-061307-6044/run.trx slot=granted waited=0s dirty=0 source=7e579dfda95b0945e201eb8c133243a208a19874 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=7e579dfda95b0945e201eb8c133243a208a19874 build=ok filter=/*/*/(PtyBackendPolicyTests*)|(Da1StartupResponderTests*)/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-b7c17822/.antiphon/checkpoints/20261004-062047-4776/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=7e579dfda95b0945e201eb8c133243a208a19874 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=7e579dfda95b0945e201eb8c133243a208a19874 build=ok filter=/*/*/(C1022BackendCapabilitiesTests*)|(RunnerCapabilitiesTests*)/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-b7c17822/.antiphon/checkpoints/20261004-062047-4776/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=7e579dfda95b0945e201eb8c133243a208a19874 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=7e579dfda95b0945e201eb8c133243a208a19874 build=ok filter=/*/*/(UnixPtyArgvTests*)|(PtyBackendEnvGuardTests*)/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-b7c17822/.antiphon/checkpoints/20261004-062047-4776/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=7e579dfda95b0945e201eb8c133243a208a19874 sourceState=clean buildSource=verified
```
