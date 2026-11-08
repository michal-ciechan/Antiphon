# Review 7fe35bc3: CARD-1772 done-receipt validation before the terminal gate

Verdict: CLEAN. No blockers. One advisory (A-1).

Subject: origin/feat/card-task-8161c466 tip `42023b0e1fc0b687c99abe9a44eb10b746562cff` (Grok Debug 8161c466), base `6ccf8896e61a121bb2b34f1d19bcdf344dc51dfa`, master at review `d0a3ee89e495baf364e8260abeab326db9c98f76`.
Diff: 3 files, 22 insertions, 7 deletions (`tools/Antiphon.Checkpoints/Program.cs`, `tools/Antiphon.Checkpoints/Commands/WaitCommand.cs`, `docs/testing-and-build.md`).
Landable: `git merge-tree --write-tree origin/master HEAD` exit 0, tree `a5b5cc748da6c099533f4ef4e2dc2c7f70996ea1`.
Evidence diff: `scripts/check-evidence-diff.ps1` commits=1 entries=0 violations=0.

## (1) Concurrency: pre-gate validation of an already-done receipt

Writers of the run receipts and their ordering:

- Executor (`CheckpointApp.ExecuteAsync`): every provisional and final `ReportWriter.WriteFiles` (report.md, then report.json) happens before `Finish`, which sets `phase=done` in memory and publishes `state.json` as its last write. Every file is published through `CheckpointWrite.Text` -> temp file -> `ReplacementPublication.Publish` (rename), so a reader never sees a torn file, and `phase=done` in `state.json` is the commit point after which the executor writes nothing more.
- Gated publishers (`stop`, `wait` through `TerminalReceipt.Publish`): run only when the state re-read UNDER the gate is not `done`; they write report.md, report.json, then `state.json` with `phase=done` last. Same atomic replace per file.
- The executor never takes `TerminalPublicationGate`; it did not before CARD-1734 either. The gate only ever serialized stop/wait callers against each other.

Consequences for the new ordering:

- Spurious refusal of a healthy run: impossible for a gated publisher, because the pre-gate block runs only when `phase=done` was read, and a publisher's `done` write is its last write; a reader that sees `done` sees the complete report pair. For the executor, `wait` adds the `observed.Verdict == Dead` condition (WaitCommand.cs:77), so the executor cannot be writing; `stop` validates on `phase=done` regardless of liveness, but a live executor that has published `done` has nothing left to write. `stop` behaved the same way before: its old gated check (Program.cs, CARD-1734 ordering) already validated the state snapshot read before the gate; only the report-file reads moved outside the lock.
- Accept-then-publish a conflicting receipt: impossible. Both verbs re-read `state.json` under the gate and re-run the full receipt validation there (`stop` Program.cs:390-404, `wait` WaitCommand.cs:105-115). Publication (`TerminalReceipt.Publish`) is reached only from a not-`done` re-read; `phase` never moves from `done` back to another value, so a pre-gate `done` read cannot lead to a publication.
- CARD-1734 B-1 invariant (a real waiter must not return between executor death and the stopper's publication) is intact: `stop` still takes the gate before killing the executor (Program.cs:360 before :366), so a waiter that observes death reads a not-`done` state, skips the pre-gate block, finds the gate busy and loops. `C1734_Stop_publishes_before_the_executor_waiter_returns` passed in the rerun (CP-2).

Step-by-step comparison with the previous ordering (`ceda26b31`, CARD-1734):
- stop before: read state -> SHA check -> TryEnter gate -> done? validate (gate held, lock file created) -> AliveSame stop -> re-read -> publish or re-validate -> Remove.
- stop after: read state -> SHA check -> done? validate (no lock) -> TryEnter gate -> AliveSame stop -> re-read -> publish or re-validate -> Remove. Only the gate moved; the double check under the gate is retained.
- wait before: read state -> Dead? TryEnter gate (lock created) -> re-read -> done? validate -> Remove/print.
- wait after: read state -> Dead && done? validate (no lock) -> Dead? TryEnter gate -> re-read -> done? validate -> Remove/print. The pre-gate block is additive and read-only.
- Both are the pre-CARD-1734 refusal position (validation before any write) with the CARD-1734 gate kept for the publication path.

## (2) Refusal semantics and writes

- Other verbs (`status`, `report`, `report --merge`, `clean`, `validate`) are untouched by the diff.
- Non-done phases: the pre-gate block is conditioned on `phase == "done"` in both verbs; every other path is byte-identical to the base.
- No new write before refusal: the pre-gate block only calls `ReportWriter.RequiredReceiptRefusal` (reads report.md, report.json, optional `checkpoint-build-source.json`) and the same `receipt-source-SHA-mismatching` string check; exit `ExitCodes.Invalid` (2) and the `refused: <reason>` line are the same strings the gated path emits.
- Minor behavioural difference, acceptable: `wait` on a dead executor whose `done` receipt is invalid now refuses immediately even when another caller holds the gate; previously it looped on `terminal-publication-in-progress` until the holder released. A holder of a `done` run never rewrites report files, so the refusal is truthful.

## (3) Coverage and red-at-base

Witness: `CheckpointReceiptSafetyTests.receipt_row_and_build_identities_refuse_blank_without_writing` with arguments `""`, `" \t\r\n"`, `null`, `"CP-fixture"`; for each argument the matrix is {state.json, report.json} x {rows, builds} x 9 verbs (validate skipped for state.json). The inventory assertion (lines 164-165, `file inventory changed`) is separate from the exit/message assertions (154-161) and the bytes assertion (166-168). The test file is unchanged by the subject commit, and the CP-fixture healthy case asserts exit 0 for every verb except `clean`.

Fresh red-at-base probe (this review): detached worktree of `6ccf8896e6` at `/tmp/claude-1654/-work-worktrees-task-7fe35bc3/76031618-e444-4532-ab87-ae2822fd46a0/scratchpad/c1772base`, `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` under lease `f8ae6872` (252 s), then the single method under lease `c208bf43`:

| case | outcome |
|---|---|
| `""` | Failed |
| `" \t\r\n"` | Failed |
| `null` | Failed |
| `CP-fixture` | Passed |

Every failure item in the TRX is `<case>: file inventory changed` for `report.json/rows/stop`, `report.json/rows/wait`, `report.json/builds/stop`, `report.json/builds/wait`; zero items about a missing refusal, wrong exit or changed bytes. TRX: `.../scratchpad/base-trx/base.trx` (scratch, not committed). This matches the Code report and the card.

Why state.json blanks were already clean: `RunStateStore.TryRead` refuses `receipt-identity:` on a blank state identity, which `wait` returns at WaitCommand.cs:71-73 before observing the executor and `stop` returns as `state-receipt-unreadable` before the gate.

## (4) Docs

`docs/testing-and-build.md` line 741 adds: when `phase` is already `done`, `stop` and `wait` validate that receipt, including nonempty `report.json` row and build identities, before opening the gate; a refusal returns exit 2 and does not create the lock (CARD-1772). Accurate: both verbs open the gate only after that validation (for `wait` the gate is opened only on a dead observation, and the pre-gate validation precedes it on that path), the identity check is `CheckpointReceiptJson.Validate` through `ReceiptJson.Read<ReportModel>`, and exit 2 is `ExitCodes.Invalid`. The later `wait` paragraph (same file, ~line 700) remains correct.

## Checkpoint rerun at the reviewed tip (checkpoint tool, one run)

Run `20261108-043017-042c`, plan `.antiphon/c1772-review-plan.md` (gitignored review table), `TEMP=/tmp`, Linux server2, build `bin-c1772-rv` 469 s slot=granted, `buildSource=verified`, `sourceState=clean`, commit `42023b0e1fc0b687c99abe9a44eb10b746562cff`, verdict GREEN exit 0, unlisted: none.

| row | filter | executed | passed | failed | skipped | wall |
|---|---|---:|---:|---:|---:|---:|
| CP-1 | `/*/*/CheckpointReceiptSafetyTests/*` | 8 | 8 | 0 | 0 | 201 s |
| CP-2 | `/*/*/CheckpointTerminalReceiptTests/*` | 10 | 10 | 0 | 0 | 218 s |
| CP-3 | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/*` | 47 | 47 | 0 | 0 | 120 s |
| CP-4 | `/*/*/C1217ConditionalSkipContractTests/*` | 8 | 8 | 0 | 0 | 9 s |

CP-1 TRX names include all four `receipt_row_and_build_identities_refuse_blank_without_writing` arguments (Passed). CP-2 TRX names: all ten C1734 cases including `C1734_Stop_publishes_before_the_executor_waiter_returns`.
Receipt validation: `Antiphon.Checkpoints.dll validate --evidence .antiphon/checkpoints/20261108-043017-042c/report.json --expected-source-sha 42023b0e1fc0b687c99abe9a44eb10b746562cff` -> `CHECKPOINT SOURCE VALID rows=4`; `scripts/validate-checkpoint-receipt.ps1` -> same. The run directory retains `.terminal-publication.lock` after the healthy `wait`, as documented.
Counts match the Code report (8/8, 10/10, 47/47, 8/8). Tool build for the run: `scripts/build-slot.ps1 -Label c1772-review-tool -- dotnet build tools/Antiphon.Checkpoints -p:UseAppHost=false` (lease f3350bba, 8 s). Base tool build in the detached worktree: lease ba40071e.

Scope note: the brief's Final selection is the two checkpoint classes, the guard batch and the C1217 class the Code report claimed; the whole Unit lane is owned by nightly and rc qualification (docs/testing-and-build.md, Unit lane checkpoint shards), not by per-task Final. PCs: none defined for this Debug task; none discharged here.

## Advisory

A-1 (WaitCommand.cs:79-81 and :109-110): the SHA-mismatch-or-`RequiredReceiptRefusal` expression is now duplicated inside `WaitAsync`; `Program.RequiredRunReceiptRefusal` already expresses it. A shared helper would keep the pre-gate and gated checks from drifting. Not a blocker; no Review rerun needed for this correction.

## Cleanup

Detached base worktree removed with `git worktree remove --force` on its literal scratchpad path. Native tool output under `tools/Antiphon.Checkpoints/bin|obj` left in place (ignored, incremental). Checkpoint evidence stays gitignored under `.antiphon/checkpoints/20261108-043017-042c/`.
