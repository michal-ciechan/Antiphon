# CARD-1369: refuse growth during an EOF-position stall

Date: 2026-10-20. Source inspected: `359cc912235d840e9f94202592e86480d87db468`
(CARD-1368 repair, carrying CARD-1358). Code starts **after CARD-1368 lands**.
The brief folds verification design into this Plan by requesting executable
checkpoints and `next: code`. No builds or tests were run during planning.

Evidence: CARD-1369 on the Antiphon board and
`docs/investigations/2026-10-20-card-1369-eof-stall-append-race.md` at
`0adb0408e11ad2b22f3cd5abac95842c7de45a2b` on `feat/card-task-84630272`
(fetched and read with `git show`; the investigation is not copied here).

## Ground truth

| Card assumption | Code at the inspected source | Consequence |
|---|---|---|
| An append during the final stalled read can leave an unchanged stamp. | `CheckpointFileReader.Consume` accepts `total == metadata.Size`, using the single inspection before the loop. Cancellation at the old EOF offset has not established EOF. | Confirmed mechanism; re-inspect the retained handle before this success. |
| Size can be checked without reopening the path. | `ICheckpointReadHandle.Inspect()` supplies size; native implementations inspect the retained descriptor/handle. `CheckpointHandleStat` has no inode identity. | Use existing size observation; no new identity API or pathname check. |
| The repair affects stamping. | Both buffered reads and streaming digests use `Consume`; `BuildConfinement.Stamp` still uses `ReceiptFile.ReadBytes`. | Witness all three consumer paths. |
| W-3–W-6 bypass the real catch on failure. | Their failing branch injects a formatted stamp. `Stamp` catches IO/access/security exceptions and `Reason` selects and normalizes the innermost message. | Add a byte-read seam inside the existing try/catch. The older FIFO test may enter the catch but does not assert its reason. |
| The virtual fixture can model appending to an open file. | `Put` and `AppendText` replace the node/inode; `Held` retains the old node. Node fields and the controlled clock are already available. | Mutate the captured node in place inside `ReadAsync`; do not replace the path. |

## Decisions

- **D-1 — Refuse a changed final size, without retry.** In the existing stall-only
  catch, return only when both `total == (long)metadata.Size` and
  `handle.Inspect().Size == metadata.Size`. Short-circuit the second inspection
  when bytes are incomplete. Otherwise retain the existing `read-stalled`
  IOException; inspection failures propagate into the existing receipt wrapper.
  This also refuses shrinkage. Reject retry/reopen: they add budget and identity
  questions; reject unconditional timeout refusal: it removes CARD-1358's valid
  unchanged-file shortcut. Preserve caller cancellation, limits and timer rearming.
- **D-2 — Add a per-call byte-reader overload.** Preserve
  `Stamp(string full)` and delegate it to
  `Stamp(string full, Func<string, byte[]> readBytes)` using
  `path => ReceiptFile.ReadBytes(path)`. Move the existing body unchanged into
  that overload, replacing only the byte-read call inside its try block.
  Preserve method-group callers and the production formatter. Reject static
  hooks and preformatted-stamp injection: neither is needed for this boundary.
- **D-3 — One slice with two commits/runs.** The small catch correction and its
  byte-reader seam share a slice. Commit/push the tests and inert seam first;
  execute one red selection, then commit/push the reader correction and execute
  one narrow green selection. Formatting is already correct; its witness is
  additional coverage, not an invented formatting repair.

## Slices

**S1 — Reader correction and real-catch witnesses.**

Files:

- `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs`: D-1.
- `tools/Antiphon.Checkpoints/BuildConfinement.cs`: D-2.
- New `tests/Antiphon.Tests/Checkpoints/CheckpointEofStallTests.cs`:
  four Unit methods below and a local controlled stream/helper.

Reuse `HostCleanupCheckpointFixture` and `ControlledTimeProvider` without
changing their shared behavior. The test creates a tiny real regular file in an
owned temporary directory so real `Stamp` shape checks reach the read; mirror
that path in virtual IO for the controlled read. Clean up the owned directory
in `finally`. No processes, database or global hook are needed.

At **S1-red**, commit the complete witnesses and D-2, keeping `Consume` unchanged,
then run CP-1. Require the intended outcome assertion failures, not compilation,
fixture or discovery failures. At **S1-green**, commit D-1, then run CP-2.

## Verification design

### Inspection

Read `CheckpointFileReader`, `ReceiptFile`, `ICheckpointCleanupIo`,
`CheckpointNativeIo.NativeHandle`, `BuildConfinement.Stamp/Reason`,
`HostCleanupCheckpointFixture`, `ControlledTimeProvider`,
`HostCleanupCheckpointReadDeadlineTests`, and the ledger W-3–W-6 definitions.
The checkpoint importer and testing/build owner establish the table format.
No asynchronous message-delivery path changes.

### Proves it works now

The append, unprovable-size and unchanged-file methods each have exactly three
TUnit argument cases: `receipt`, `digest`, `stamp`. Each calls the real reader;
stamp cases call the real `Stamp` overload with
`p => ReceiptFile.ReadBytes(context, p)`.

| ID | Method in `CheckpointEofStallTests` | Operation and decisive oracle |
|---|---|---|
| V-1 | `append_during_eof_stall_is_refused` (3 results) | Capture the unchanged original, then deliver exactly its 65,536 bytes. In the next `ReadAsync`, record offset and clock time, append one byte to the captured node's bytes and stat size while preserving device/inode, advance exactly `StallDeadline`, and honor the canceled token without delivering bytes. Receipt/digest must throw wrapped IO containing `read-stalled`; stamp must return exactly `unreadable\|read-stalled: 65536 of 65536 bytes; no progress within 3s`, differing from its captured file stamp. Assert one append, same node identity, final size 65,537, and a second inspection after the stall. On the base reader, assert outcomes before inspection counts: receipt/digest return successfully and stamp equals the original, causing three meaningful red results. |
| V-2 | `stamp_catch_formats_the_innermost_reason` (1 result) | Call real `Stamp` on a real regular file with a byte-reader callback that throws. Assert callback invocation and exact output for nested `IOException("outer", new IOException("  inner\r\nreason  "))` → `unreadable\|inner  reason`; nested whitespace-only message → `unreadable\|IOException`; nested 205 `x` characters → prefix plus exactly 200 `x` characters. Also assert access/security exceptions produce their normalized reasons. Expected strings are literal test oracles, not calls to `Reason`. |
| V-3 | `eof_stall_with_unprovable_final_size_is_refused` (3 results) | Fresh cases shrink the same node by one byte, or make only the second inspection throw `IOException("inspect failed")`, during the old-EOF read before advancing the clock. Receipt/digest refuse; stamp is unreadable with the corresponding innermost reason. Neither may return the original successful result. |

Measure virtual elapsed time **inside `ReadAsync`**, from entering the stalled
operation to advancing its clock; assert the timer fires there. No real sleep,
outer stopwatch bound, or scheduler-latency assertion. Complete every fake read
with bytes or cancellation; leave no pending task. Record the initial stamp
before arming the one-shot append, then reset per-read observation counters.

### Guards the regression

- **R-1:** `CheckpointEofStallTests.eof_stall_without_growth_still_completes`
  (3 results) uses the same old-EOF cancellation with unchanged node size.
  Assert exact original bytes, digest length/hash, or captured stamp, respectively;
  assert two retained-handle inspections and one stream.
- **R-2:** the two existing `HostCleanupCheckpointReadDeadlineTests` methods:
  `large_file_read_that_keeps_making_progress_completes_past_three_seconds` and
  `read_with_no_progress_for_the_stall_window_is_refused_as_stalled_and_a_caller_cancel_is_not`.
  Preserve their progress, stall and caller-cancellation assertions.

### Guard inventory and positive controls

These are post-land Mutation designs, pending execution and binding to Code's
actual lines. Each compiling defect is restored before its method-scoped green.
The detecting filter is `/*/*/CheckpointEofStallTests/<method>` with the exact
method below; Mutation binds the named argument case where relevant.

| Guard / PC | Deliberate defect | Detecting method / expected assertion failure |
|---|---|---|
| G-1 / PC-1 | Remove the fresh size equality from the success shortcut. | V-1 method, receipt case: required IOException is absent. |
| G-2 / PC-2 | Turn failure of the second inspection into success. | V-3 method, receipt case: inspect-failed refusal is absent. |
| G-3 / PC-3 | Return `unreadable` without its reason from the real catch. | V-2 method: exact nested-message stamp differs. |
| G-4 / PC-4 | Format the outer exception instead of the innermost. | V-2 method: expected `inner  reason` differs. |
| G-5 / PC-5 | Remove CR replacement. | V-2 method: exact single-line stamp differs. |
| G-6 / PC-6 | Remove LF replacement. | V-2 method: exact single-line stamp differs. |
| G-7 / PC-7 | Remove trimming. | V-2 method: expected trimmed stamp differs. |
| G-8 / PC-8 | Remove empty-message type fallback. | V-2 method: expected `IOException` differs. |
| G-9 / PC-9 | Remove the 200-character cap. | V-2 method: exact bounded stamp differs. |

Nine guards, nine distinct controls, no missing mappings. These PCs do not
authorize Code mutation runs. The existing progress/cancellation guards remain
covered by R-2; their implementations are unchanged.

### Out of scope

Same-size rewrites, append-then-truncate, writes after the final inspection,
native timing/frequency qualification, metadata-operation deadlines, the
PowerShell reader, and ledger classification redesign remain outside this fix.
The controlled stream proves the interleaving and real catch, not a native disk
stall. The fresh inspection is a point-in-time size observation.

### Cost

Estimated Code budget: **40–55 minutes**: 16 minutes for the two isolated
build/checkpoint rows, 20–25 minutes authoring, 4–14 minutes for slot wait,
receipt inspection and publication. No full suite or repeated green is planned.
Review runs CP-2 against the final candidate; CP-1 remains historical red evidence.
Post-land PCs are separately budgeted: nine method-scoped red/restore/green
cycles, approximately 60–90 minutes including isolated rebuilds; no PCs ran here.

### Checkpoints

Lane for both rows: **portable `Antiphon.Tests` Unit**, controlled IO/clock plus
the test-owned regular-file shape. CP-2 selects 12 TUnit results:
V-1 3 + V-2 1 + V-3 3 + R-1 3 + R-2 2. Internal vector assertions are not results.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1369-red/` | portable-unit-append-red | `/*/*/CheckpointEofStallTests*/append_during_eof_stall_is_refused*` | V-1 red | 3 executed, 3 expected assertion failures, 0 skipped; exit 1 | 3 | 8 |
| CP-2 | S1-green | `tests/Antiphon.Tests -> bin-c1369-green/` | portable-unit-reader-stamp-green | `/*/*/(CheckpointEofStallTests*)\|(HostCleanupCheckpointReadDeadlineTests*)/*` | V-1, V-2, V-3, R-1, R-2 | 12 executed, 0 failed/skipped; exit 0 | 12 | 8 |

## Execution and handoff

Use the checkpoint tool through `scripts/build-slot.ps1`, with this plan,
`--rows CP-1` or `--rows CP-2` at the corresponding committed SHA and
`--expected-source-sha <full-sha>`. Keep waiting on exit 75 until terminal.
Retain unedited CHECKPOINT lines, fresh TRX and source receipts; CP-1 is an
intentional failed diagnostic, never a green certificate. Validate CP-2's clean
source/build provenance. Remove only owned alternate outputs after completion.

`GET /api/runner-defaults` and `GET /api/session-runners` were read on
2026-10-20: defaults revision 2, no kind overrides, eligible Linux and Windows
lanes; the configured global runner was stale/draining at observation. Re-read
placement at dispatch. Omit `-Runner` and `-Platform`; no host or OS pin is needed.

Next: **code after CARD-1368 lands**, then ordinary Review. The Code brief binds
this committed plan's `### Checkpoints` section. No implementation or test
execution is claimed by this planning artifact.
