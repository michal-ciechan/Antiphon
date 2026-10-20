# CARD-1370: preserve checkpoint results through state-write contention

Plan and verification design, 2026-10-20. Code budget: **45-60 minutes**, including
one expected-red witness and one narrow green run; no category, namespace or suite
runs. No builds or tests were run during Plan.

## Ground truth and root cause

Inspected base: `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`. The card records
Windows run `20261019-211021-551c`: TRX 2/2 passed, followed by
`receipt-write-failed` / access denied on state.json and a dead executor.
The original exception's native code and handle owner are not supplied; attributing
the incident specifically to antivirus, a reader, or a read-only attribute would
be speculation. The proven defect is the **single-attempt publication boundary
and loss of the completed in-memory result when it fails**.

| Card assumption | What the code does at the inspected base |
|---|---|
| State needs an atomic write. | Already writes a sibling GUID temporary file, rechecks ownership, then moves over the destination: `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:168-202`; native adapter uses `File.Move(source, destination, overwrite)` at `CheckpointNativeIo.cs:92`. There is no replacement retry. |
| A missing reader share flag might block replacement. | The native Windows receipt reader opens with share mask 7 (read/write/delete), `CheckpointNativeIo.cs:373`; the existing `RunStateStoreTests.cs:29` reader also shares delete. An external handle without delete sharing can still block the move. |
| A read-only Git object might explain this state file. | `RunStateStore.cs:23-28` generates JSON through the owned writer, not a Git-object copy. `tests/Shared/ReadOnlyDirectory.cs` addresses deleting fixture Git objects. No evidence connects that mechanism to this state file; do not clear attributes or ACLs. |
| The green row should survive a state-write error. | `RunScheduler.cs:225-237` records the result in memory, then calls Publish. `CheckpointApp.cs:156-165` throws on publication I/O; its outer catch at `:81-102` rereads stale state and reconstructs rows with only id/state/exit, losing counts, TRX and the CHECKPOINT line. Final Publish at `:454` can also turn a completed run into the same lossy fallback. |
| CARD-1368 may already fix this. | At repair tip `359cc912235d840e9f94202592e86480d87db468`, `SafeWrite.Describe` changes to streaming `ReceiptFile.Digest` and CARD-1358 changes bounded reading. `Text` still performs one move; `RunStateStore.Write` is unchanged. RowRunner also gains host-exit output adoption. This touches the state-write call path but does not add retry or result preservation. |

Line references above are base coordinates. The repair tip changes nearby line
numbers. Freshly fetched origin/master was still the inspected base during Plan.

## Decisions

- **D-1 — dependency:** implement after the reviewed CARD-1368/CARD-1358 repair
  is published. The Code checkout must contain that repair tip's changes (or its
  landed equivalent); preserve streaming stamps, stall handling and output
  adoption. The caller provisions that base. Do not rebase this Plan branch,
  merge master into a task branch, or edit the frozen repair branch. Rejected:
  independently changing the shared stamp reader while its repair is pending.
- **D-2 — smallest retry:** keep the existing atomic temporary-file/move writer.
  Retry only `write.Text(path, serializedState, ownership)` inside
  `RunStateStore.Write`, with the JSON serialized once and history appended once
  after success. Allow six attempts with delays 50/100/200/400/800 ms through the
  existing context clock (1.55 seconds of added waiting). Every attempt retains
  SafeWrite's ownership/link/Git checks and temporary cleanup. This is an attempt
  and delay bound, not a new bound on underlying filesystem/Git operations.
  Retry access-denied `UnauthorizedAccessException` and `IOException` whose
  HRESULT is Win32 access denied/sharing violation/lock violation (5/32/33);
  inspect an inner exception only when it carries the original typed failure,
  never parse error text. Other I/O failures and ownership refusal strings fail
  immediately. Explicit read-only destinations fail without attribute changes.
  Rejected: replacing File.Move with File.Replace (still needs delete sharing),
  retrying all exceptions, unbounded retry, in-place truncation, and broad changes
  to SafeWrite callers. A post-move stamp failure must never be silently accepted.
- **D-3 — exhausted publication remains failed evidence:** use a typed state
  publication failure, preserve the completed `RowRunResult` objects, and report
  `receipt-write-failed` with exit 2. Keep the existing scheduler cancellation and
  join behavior before returning a partial result; include any rows that completed
  while draining. No further row/build may launch after the failure. Build the
  failed report from those results, not a reread of old state. Preserve counts,
  TRX path, source/build provenance and exact CHECKPOINT lines; retain artifacts
  and output custody. Do not run baseline comparison for this infrastructure
  failure. Handle failure of the final Finish publication the same way. Persist
  report.json/report.md where possible and emit row lines plus the failure to
  the existing log/output channel even if receipt storage remains unavailable.
  Never certify a green run without its required state receipt. Rejected:
  swallowing publication failure or synthesizing a successful terminal state.
- **D-4 — verification and placement:** one native Windows witness, with bounded
  injected failure arms in the same method. Only CP-1 requires Windows; authoring
  has no OS requirement. Read GET /api/runner-defaults and GET /api/session-runners
  before dispatch. Plan read both: defaults revision 2, with eligible Windows
  and Linux capacity advertised. Resolve live placement again; no host is pinned
  here. Omit `-Runner`; use `-Platform Windows` only for CP-1 execution. These are
  method-filter runs, not a Windows lane suite.

## Slices

| Slice | Files and concrete work | Validation |
|---|---|---|
| S1 — witness first | Add `tests/Antiphon.Tests/Checkpoints/CheckpointStatePublicationTests.cs`, using `CheckpointTestBase`, existing `FakeDriver`/TRX helpers and a local forwarding `ICheckpointCleanupIo` adapter. One method: `state_publication_survives_contention_and_preserves_completed_rows`. Use Integration/process limiter conventions for fixture Git calls. No production change. | Commit/push; run CP-1 once against the unrepaired publication path. Expect one failing test at the transient-write success assertion, not a build/fixture/zero-selection failure. |
| S2 — bounded publication and retained results | Change `tools/Antiphon.Checkpoints/State/RunStateStore.cs` for D-2; `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs` for a typed failure carrying the drained partial result; `tools/Antiphon.Checkpoints/CheckpointApp.cs` for D-3 report/final-Publish handling. A small exception type may live beside RunStateStore. Shared SafeWrite/reader and state/report schemas need no change. | Commit/push before the one CP-1 green rerun; all witness arms below execute. Report the two tested full SHAs separately. |

Authoring estimate: 30-40 minutes; two isolated build/filter invocations: about
12-20 minutes total. If implementation exposes a different operation or permanent
permission defect, retain its path/operation/HRESULT/attributes and the witness
failure; do not broaden retries or commission lane runs to hide it.

## Verification design

**V-1 — transient contention (the red-first witness).** In
`CheckpointStatePublicationTests.state_publication_survives_contention_and_preserves_completed_rows`,
seed an owned state.json in `TempDir()`, then retain a real native read handle
opened with `FileShare.ReadWrite` **without** `FileShare.Delete`. The forwarding
I/O adapter calls the real Move and records its first sharing/access-denied
failure before any release. Arrange release with an acknowledged barrier after
that failed attempt, before the next attempt; always dispose/join in finally.
Capture the Write outcome and assert success outside the adapter, so the old
single attempt fails an explicit assertion. Require a failed native move, a later
successful move, parseable new JSON, exactly one added history entry, and no
remaining temporary file. While held, old JSON must remain complete. Use a bounded
watchdog; no timing-only sleep as the proof of overlap. **Windows is required**:
Linux rename semantics would not exercise this denial. Off-Windows execution is
not acceptance and must not be reported as green/zero-skip coverage.

**V-2 — classification and budget, same method.** Use the adapter to inject the
specific D-2 HRESULTs and UnauthorizedAccessException, then allow success; assert
the new state and single history append. For permanent sharing denial, assert six
attempts, the five specified delays, unchanged complete old JSON, and no fabricated
history entry. An unrelated IOException, explicit read-only destination and an
ownership refusal must not loop or mutate the protected file. Drive delays with
the supplied clock where possible; do not wait out wall-clock backoffs in every
injected arm. Every arm uses independent owned paths.

**V-3 — preserve completed work, same method.** Through ExecuteAsync and the
existing fake driver, produce a row with a real fixture TRX containing two passing
results. Inject permanent failure only when the state update contains that completed
row; report paths stay writable. Assert exit 2 / RED / receipt-write-failed,
executed=2, passed=2, failed=0, skipped=0, unchanged TRX path and row line, retained
source/build identity, no second row launch, and joined in-flight work. Assert the
report is ineligible for clean receipt validation. Repeat this boundary at final
Finish publication and preserve the previously built row report. In a separate
transient arm, release contention within the retry budget and require terminal
state plus report to succeed without changing the row counts. Release all held
handles before the temp-root scope teardown. These are internal scenarios in one
TUnit method, **one executed test**, not a synthetic Min count of two TRX results.

Execution: CP-1 is the sole checkpoint row and has one intentional red/green cycle:
run it after the S1 commit for the expected red, then once after S2 for green.
The `After` column names its final closing slice group; this explicitly listed S1
red run is the only pre-fix invocation. Use the checkpoint tool through
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1370 --`
(`dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1370-tool/ -- run
--plan <this-path> --rows CP-1 --expected-source-sha <exact-committed-sha>`), one
run per commit, and its wait command until exit is not 75. Keep source frozen
during each invocation. Use `--clean-on-red` for the expected-red invocation so
owned isolated outputs can be reclaimed; keep its TRX/log/receipt evidence ignored.
No automatic baseline or known-flaky rerun, no extra test selection. Record the
expected red as red; only the S2 run may qualify green. Preserve unedited CHECKPOINT
lines, actual counts, build/source provenance and the failure assertion in the
stored report. Remove task-owned `bin-c1370/` and `bin-c1370-tool/` outputs using the guarded cleanup
path; a refusal remains visible. Code also runs the required evidence-diff guard.

## Code commissioning adjustment (2026-10-20)

The Code brief overrides execution to CP-1 expected red and CP-2 narrow green,
with --row-timeout 40m, trailing method wildcards, no whole-Unit run and a 45-minute
budget. The Linux task runs injected failures; V-1 native Windows acceptance stays
pending for a separate Windows task. No placement pin is used here.
Live defaults revision 2 and both eligible OS runners were read before execution.

Current implementation base is 0c5c3ec3d03c5d10ba927f856733c5c79b1775c3, including
CARD-1368/1358 and CARD-1369. Current references: RunStateStore.cs:27 (single write),
RunScheduler.cs:236 (completed row publication), CheckpointApp.cs:156 (Publish),
CheckpointApp.cs:467 (Finish), SafeWrite.cs:168 (atomic writer). Streaming stamps
and host-exit adoption are retained. ReportValidator also needs to reject an
infrastructure-failed report whose completed rows are individually green.

V-1 injected replacement failure runs here; the native handle arm executes only
on Windows and is not claimed covered by Linux. V-2 and V-3 run in the same method.
No R-n or PC-n was supplied by Plan. Mutation must discover missing controls;
zero planned PCs does not discharge SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1370/` | state-publication-red | `/*/*/CheckpointStatePublicationTests/state_publication_survives_contention_and_preserves_completed_rows*` | V-1 injected | 1 expected assertion failure | 1 | 8 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1370/` | state-publication-green | `/*/*/CheckpointStatePublicationTests/state_publication_survives_contention_and_preserves_completed_rows*` | V-1 injected, V-2, V-3 | 1 passed, 0 failed/skipped; native Windows pending | 1 | 8 | true |