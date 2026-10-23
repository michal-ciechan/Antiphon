# CARD-1532: stream complete content for checkpoint source stamps

Plan date: 2026-10-23. Source inspected: `0643a3a721cfee6e9934e8fa0b01fddd405b9ce0`.
Read CARD-1532 and CARD-1531 in full through `scripts/card.ps1`. The
[stamp investigation](../../investigations/2026-10-23-checkpoint-stamp-64mib-debug.md)
was absent at this task's base and read in full from
`origin/feat/card-task-4f0126ba`, commit `e159b7327d618dcac7dd2a14a73efbbafe6a32ab`.
Also read [the starvation investigation](../../investigations/2026-10-23-build-slot-starvation-debug.md).
This is a documentation-only Plan: no builds, test execution, deployment or live
Windows qualification changes. Verification design is the closed proposal for a
**separate TestDesign stage**, not a claim of completed verification.

## Outcome and scope

Pre-existing regular files above 64 MiB and through 1 GiB can be source-stamped
without whole-file allocation. Every byte still contributes to SHA-256. The
64 MiB receipt limit, shape stamps, verified-handle boundary, source-clean gate,
and output deletion authority remain intact. Both the .NET checkpoint tool and
the direct PowerShell stamp path implement this contract.

## Ground truth

| Card assumption | Code at the inspected SHA | Planning consequence |
|---|---|---|
| A normal DLL can exceed the stamp reader's limit | `tools/Antiphon.Checkpoints/BuildConfinement.cs:Stamp` calls `ReceiptFile.ReadBytes` and hashes its byte array. The implementation moved to `src/Antiphon.HostCleanup/Checkpoints/ReceiptFile.cs`; its cap is 67,108,864 bytes. | Change the regular-file stamp caller, not global receipt admission. |
| A suitable bounded full digest already exists | `ReceiptFile.Digest` calls `CheckpointFileReader.Digest` with `ContentLimit = 1L << 30`, 256 KiB reads, an incremental hash, and the same inspected handle. `SafeWrite` already uses it. | Reuse this exact implementation; no new weaker hash or .NET content-bound setting. |
| Ignored artifacts may still be user-owned | `CaptureLedger` includes ignored/untracked paths except root `.antiphon/`; current write roots change capture timing, not coverage. Prior `bin-*` is outside new roots; `obj` may be inside. | Test both paths. Never exclude `bin*`/`obj` or claim that an ignored name authorizes deletion. |
| The second build can fail within one run | Scheduler captures ledger before acquire and verifies after release; current-root baseline is settled by the real driver. A large first-build DLL becomes the next build's baseline. | Use real confinement with a fake build driver for second-build/retry witnesses; no actual compiler in the fixture. |
| Stamp serialization must remain stable | Current regular stamp is `file|length|lowercase-sha256`; absent/directory/link target have separate shape stamps. `ReceiptFile.Digest` returns uppercase SHA. | Use returned long length and normalize only digest case. Preserve shape branches and refusal formatting. |
| PowerShell has the same cap | `scripts/run-checkpoint.ps1:Get-CheckpointContentStamp` calls `Read-CheckpointBytes`; `scripts/lib/checkpoint-file-reader.ps1` embeds a verified reader but only exposes buffered reads, capped at 67108864 in that wrapper. | Add a streaming digest to the embedded reader and route only regular stamps through it. |
| Reader parity includes EOF behavior | Shared .NET `Consume` re-inspects the retained handle at an EOF stall (CARD-1369). The embedded PowerShell reader currently accepts solely when bytes read equals the original inspected size. | Digest parity must include final-size reinspection; preserve this through a common embedded consumption loop and pin it with a witness. Do not copy the stale EOF shortcut into the new digest. |
| Windows PowerShell compatibility means executing the stamp there | Both affected scripts already declare `#requires -Version 7.0` and the embedded C# uses modern .NET APIs. Their PowerShell source is ASCII. | Preserve PS7 runtime requirement; prove **syntax parsing** under Windows PowerShell 5.1 without dot-sourcing/executing them there. Do not promise .NET Framework runtime support. |
| The tests have a small dedicated build | Checkpoint tests are in `tests/Antiphon.Tests`; receipt seams live in its Infrastructure fixtures, including `VirtualCheckpointIo` and `ControlledTimeProvider`. | Share one test-project build per committed red/green group; avoid new test projects and project-file edits. |

## Decisions

**D-1 — Reuse the complete .NET digest.** Change the regular branch of
`BuildConfinement.Stamp` to `ReceiptFile.Digest` and render
`file|<returned long Length>|<Sha256.ToLowerInvariant()>`. Replace the internal
byte-reader seam with a digest seam (or an explicitly typed context overload)
that reaches the same production path. Update the existing stamp arm in
`CheckpointEofStallTests` so it no longer exercises the retired byte-reader path.
Keep shape stamps and innermost one-line/200-character unreadable reasons. Do not
change `src/Antiphon.HostCleanup/Checkpoints/ReceiptFile.cs` or
`CheckpointFileReader.cs` merely to duplicate behavior already present.

**D-2 — Retain distinct bounds.** Regular-file content stamps accept through
1,073,741,824 bytes (1 GiB) inclusive; 1 GiB+1, including growth during reading,
refuses unreadable with the existing size-limit reason. Receipts still accept
through 67,108,864 bytes inclusive and refuse the next byte. Document/source
reader caps remain 4 MiB/1 MiB. Reject raising all `ReadBytes` limits, buffered
1 GiB reads, an unbounded digest, a knob that disables the limit, and automatic
retry/deletion as remedies for the deterministic cap failure.

**D-3 — Port the reader algorithm, not just its digest name.** In
`scripts/lib/checkpoint-file-reader.ps1`, factor the embedded reader's verified
open/inspect/consume loop so `ReadBytes` and new `Digest` share it. `Digest` uses
`IncrementalHash` and long byte counts; no `MemoryStream` proportional to file
length. Add `ContentLimit` and a `Get-CheckpointFileDigest` wrapper with explicit
length/hash members, then call it in `Get-CheckpointContentStamp`. Preserve
256 KiB chunks, the three-second progress-rearmed stall timer, caller cancellation,
one-byte-over-limit detection, single-link regular handle inspection and containment.
At an EOF stall, accept only when reinspection on the **same handle** proves its
size still equals the original and every originally inspected byte arrived. Growth,
shrink or failed final inspection refuses; cancellation never becomes successful
EOF. Keep the existing time/stream test seam for both consumers. This closes the
observed embedded-EOF gap as part of parity; it changes no native open policy.

**D-4 — Integrity argument and limitations.** For an admitted regular file the
old stamp hashed every byte from one verified handle; the new stamp hashes every
byte from that same kind of handle, only in bounded chunks. The tuple preserves
length and full SHA-256, so a one-byte edit at offset 69 MiB in a 70 MiB file
changes the stamp even if length and mtime are restored. Neither admission/stat
optimization nor `.gitignore` is used as a replacement for capture and settlement
content checks. Link-target/absence/directory stamps and hard-link/special-file
refusals remain distinct. No prefix digest, size+mtime comparison, output exclusion,
new persisted ownership authority or weaker source-clean check is allowed.
This preserves the existing boundary-based tamper evidence, not an atomic
filesystem snapshot against a writer changing and restoring bytes between checks.
Keep the documented Linux stat admission shortcut and its existing strict mode;
prove tail edits at both full settlement and strict admission.

**D-5 — Keep the two Code tasks disjoint where possible.** CARD-1532 owns
`BuildConfinement.cs`, the two PowerShell production scripts, new card-specific
tests/scripts and the existing EOF test's digest-seam adaptation. CARD-1531 owns
scheduler, slot, manifest/request, budget and reporting files. Do not edit
`RunSchedulerTests.cs`, the test csproj or common fixture files in this card.
Both need `docs/testing-and-build.md`: this card edits its stamp-limit and reader
parity paragraphs **first**, CARD-1531 edits its timing paragraphs afterward from
the landed base. If orchestration scopes cannot admit disjoint paths, sequence
whole tasks CARD-1532 then CARD-1531. Parallel source work is possible; final
CARD-1531 qualification must use the reviewed, landed stamp fix to avoid this
defect blocking its retained-red-output scans. Do not broaden either card into
broker capacity, cache reuse or cleanup-diagnostics work. The recommended executable
schedule is **CARD-1532 Code/Review/land, then CARD-1531 Code**. TestDesign can
finalize the two independent manifests without simultaneous source edits.

**D-6 — Preserve failed evidence and custody.** Historical unreadable/timeout/
cleanup-refused receipts remain unchanged. A successful retry is a new attempt
from its actual source SHA. Same-run and retry fixtures assert old user files
are byte-identical, still present and not adopted for deletion. The S1 red output
is not disposable solely because its name contains `bin-`; retain its ignored
receipts and use the existing ownership/explicit inspected cleanup procedure for
outputs. Before S3 qualification bootstrap the **new** tool from the committed
source so it can stamp retained large outputs. Never use an older detached copy
and never patch its binaries or receipts. Bootstrap itself must not ask the broken
old stamp reader to qualify a fresh large test assembly; use the declared
slot-gated tool-only bootstrap, not another test build or an unleased workaround.

**D-7 — Lanes and portability.** Re-read `GET /api/runner-defaults` and
`GET /api/session-runners` at dispatch. Planning observed defaults revision 2
with an eligible Linux default and an eligible Windows runner; these are not
location pins. Main verification uses the **portable .NET/PS7 filesystem lane**
(its shape regression row is explicitly Linux). Omit `-Runner` and `-Platform`
for portable work. CP-7 requires Linux; if the resolved default is not Linux,
select `-Platform Linux` for the CP-1..CP-8 build-sharing group rather than
running the Linux-only row as a skipped test. The single parser-only row uses the **Windows PowerShell 5.1
syntax lane**, with `-Platform Windows` only for that separately scheduled piece,
no host pin. Never touch the desktop or supervisor pid 209072 in this dispatch;
later Windows parsing must use an authorized available Windows lane without
interfering with the live qualification. Missing 5.1 parser evidence is incomplete,
not a Linux success relabeled as Windows proof. No session input wait is added or
changed; parking disabled still means no automatic release deadline (CARD-1083).

## Slices

Each Code slice is 30-60 minutes. Commit and push the real failing-test slice
before the implementation; commit all source before each run and freeze it.

| Slice | Files and deliverable | Closure |
|---|---|---|
| S1 (45-60 min) | Add `tests/Antiphon.Tests/Checkpoints/CheckpointLargeStampTests.cs`, `CheckpointLargeStampScriptTests.cs`, and `scripts/test-checkpoint-large-stamp.ps1`. Real small/large regular files and AST-extracted production PowerShell stamp functions; local test-only fixture helpers. | CP-1/CP-2: real current stamp refuses >64 MiB. Red must be an assertion about that refusal, not an absent method, compile error or missing pwsh. |
| S2 (30-45 min) | Change only `tools/Antiphon.Checkpoints/BuildConfinement.cs`; add digest-oriented S1 seam if needed; adapt `tests/Antiphon.Tests/Checkpoints/CheckpointEofStallTests.cs` to digest. Complete stream-limit, cancellation, shape and custody controls in the new .NET test file. | Commit the .NET fix; no separate test-project rebuild before the shared S3 qualification. |
| S3 (45-60 min) | Implement embedded streaming parity in `scripts/lib/checkpoint-file-reader.ps1`, change the caller in `scripts/run-checkpoint.ps1`, finish PS7 harness/tests, add `scripts/test-checkpoint-stamp-ps51-parse.ps1`; update `docs/testing-and-build.md` stamp-cap/EOF paragraphs. | CP-3..CP-8 share one green build; CP-9 is parser-only Windows work. Production/test scripts stay ASCII. |
| S4 (30 min) | Validate exact source-bound green receipts and red failure identities; retain unedited CHECKPOINT lines, counts, tool-bootstrap cost and cleanup facts in the task report. | Run evidence-diff guard, fetch master and report conflict-only merge-tree; push/confirm exact tip, then ordinary Review. No incident-card rebuilds or historical evidence edits. |

## Rollout, activation and rc

The compiled tool is produced from the checkout per invocation and shadow-copied
per run. Scripts are read from the checkout. **No daemon restart is needed**;
new invocations from the updated source activate the fix. Existing executors keep
their old code/evidence. Do not restart AppHost/session runners, terminate the
protected supervisor, change slots, or repeat the ongoing Windows qualification.

Both bugs were confirmed by source inspection on `origin/rc/2026-10-20` at
`5a126f6fc5ae4e16d803b628716b8f0485a37174`. This is an observation, not a permanent
rc target. **Recommend an rc pick**, before CARD-1531, because existing rc tooling
has the same regular-file caller. After independent Review, commission a port
from the freshly resolved active rc tip. Apply the reviewed stamp fix plus its
tests/docs, verify the referenced full-digest API exists there, and adapt context
without importing unrelated master work. Re-run the same narrow selections at
the port SHA, retain Windows parser evidence for those exact script bytes, and
obtain ordinary Review. Publish using the Merge-role target-tip workflow in
[release-gates.md](../../release-gates.md), then carry rc into master as required.
Do not move immutable tags or call a tooling smoke proof full RC qualification.

## Verification design

TestDesign finalizes the typed fixture interfaces/expanded roster before Code.
New methods below are planned; each contributes **one** TUnit execution, including
its internal matrix. Keep high-volume payloads in owned temporary directories;
delete fixture files after their use, not real checkout outputs. Produce files in
chunks and independently compute reference hashes with a test-side streaming
SHA-256, never compare the production digest to itself. Empty-file hash can use
the known SHA-256 constant. Sparse files may reduce disk writes but the digest
must still consume every logical byte. Real 70 MiB paths prove native admission;
virtual verified handles/streams prove the 1 GiB boundary without a gigabyte
allocation. Use short watchdogs, clock barriers and owned/joined children, no
real builds or live services in any fixture.

### Red witnesses first

| ID | Class.method | Oracle and pre-fix result |
|---|---|---|
| V-1 | `CheckpointLargeStampTests.C1532_regular_stamp_crosses_receipt_limit` | Real production `Stamp` on exact 64 MiB, 64 MiB+1 and 70 MiB files. Literal length and independent full lowercase SHA-256 must match. At base first passes, latter cases return unreadable instead of `file|...`; fail the stamp assertion. |
| V-2 | `CheckpointLargeStampTests.C1532_large_ignored_paths_remain_covered` | Small owned git repo with ignored `App/bin-prior/large.dll` and `App/obj/large.dll`, each 70 MiB. Real `CaptureLedger`/strict `AdmitLedger`/`VerifyLedger` for prior output; real Prepare/Settle for current write-root obj. Both unchanged paths admit/settle, remain present and byte-identical. Base refuses the former before build and the latter at settlement. |
| V-3 | `CheckpointLargeStampTests.C1532_tail_edit_with_restored_mtime_is_tampered` | Start with a valid 70 MiB baseline, flip exactly one byte at 69 MiB and restore original mtime without changing length. Strict admission before launch and full after-release verification identify the exact prior-output path as `tampered`; in-slot obj settlement also detects it. Assert file stamp exists before mutation, modified stamp differs, no repair write and no delete. At base large baseline assertion fails; after fix, a prefix-hash/exclusion mutation must pass admission incorrectly and turn this witness red. |
| V-4 | `CheckpointLargeStampTests.C1532_second_build_and_retry_admit_large_prior_output` | Fake `IConfinedDriver` runs real confinement on a tiny repo and writes a 70 MiB result for build A; build B in the same scheduler run starts and its row runs once. A second owned fixture simulates a retained red output from an earlier run, uses a fresh output ID and admits unchanged large prior output. Existing output ID still refuses. Assert real stamp traversal/driver counts, never fake a green stamp. Base fails second-build/retry admission. |
| V-7 | `CheckpointLargeStampScriptTests.C1532_script_stamp_matches_full_digest` | PS7 child invokes the real AST-extracted `Get-CheckpointContentStamp` and shared reader with real 64 MiB/64 MiB+1/70 MiB files, prior-bin and obj baselines, then the same 69 MiB byte flip/restored mtime. Assert literal length, independently expected digest, unchanged acceptance and tampered path/refusal. Base reports unreadable; parity must exercise the production caller, not only the new helper. |

### Boundary and safety witnesses

| ID | Class.method | Required observation |
|---|---|---|
| V-5 | `CheckpointLargeStampTests.C1532_content_and_receipt_limits_are_distinct` | Digest/stamp seam on a verified virtual handle: exact 1 GiB consumed in <=256 KiB chunks and accepted; metadata 1 GiB+1 refuses before stream reads; initially admissible file growing past bound refuses on the extra byte. Receipt ReadBytes still accepts exact 64 MiB and refuses +1. No `byte[1GiB]`, no stamp cap inferred from the receipt cap. Also cover absent/empty/value below. Boundary controls using the new digest seam belong in S2 if not compilable in S1. |
| V-6 | `CheckpointLargeStampTests.C1532_digest_preserves_verified_read_refusals` | Digest path, not only legacy ReadBytes: held-handle hard link/nonregular/outside-root metadata refuses before bytes; progress over three virtual seconds succeeds, a stalled chunk refuses, cancellation stays cancellation; EOF grow/shrink/inspect failure refuses, unchanged EOF completes. Verify one retained handle and zero unauthorized writes/deletes. Reuse local fixture composition; no changes to shared fixtures required. |
| V-8 | `CheckpointLargeStampScriptTests.C1532_script_digest_limits_stalls_and_eof` | Embedded reader time/stream seams repeat V-5/V-6 bounds/progress/cancel/EOF cases through actual compiled Add-Type code; preserve byte-reader 64 MiB behavior. Tail growth during stalled EOF is refused, not accepted merely on initial size. Run native hard-link and nonregular checks in the owned Linux fixture; no real FIFO read may hang. Runtime stays PS7. |
| V-9 | `CheckpointLargeStampScriptTests.C1532_scripts_are_ascii_and_parse_in_pwsh` | Check every changed .ps1 byte <=127 and real PowerShell parser errors empty for the two production scripts and both new harnesses. Child harness fails nonzero for a deliberately malformed copied script, proving its oracle; do not mutate production files. This is PS7 syntax evidence only. |
| V-10 | `scripts/test-checkpoint-stamp-ps51-parse.ps1` | On Windows, actual `powershell.exe` 5.1 Desktop parses the four changed scripts with `Parser.ParseFile`, asserts zero parser errors and ASCII, plus the malformed-copy negative control. Print PS version/edition, per-file SHA-256 and assertion counts. Do not execute/dot-source scripts requiring PS7. Missing 5.1 executable is a failed prerequisite, not skip/pass. |

S1 selects only V-1..V-4 and V-7; S3 runs all new witnesses. The ELF/PE payload
does not need to be a loadable DLL: the real stamp admits regular bytes, while the
scheduler fixture records driver entry and writes a known fresh TRX. Do not run
the giant application test assembly merely to synthesize an oversized input.

### Absent/empty/value matrix and integrity regressions

| Boundary / real type | Absent | Empty | Value / invalid value |
|---|---|---|---|
| Filesystem entry supplied as a real `string` path | Nonexistent path remains `absent|0|`; deleted formerly present file is detected. | Existing zero-byte file is `file|0|<SHA256(empty)>`, not absent or unreadable. | Patterned 70 MiB file returns full hash; 1 GiB+1 refuses unreadable. No null byte-array surrogate for an absent file. |
| Shape observation (`FileInfo`/`DirectoryInfo`) | Missing LinkTarget means regular/absent/directory classification continues. | Empty directory retains `directory|0|`, never an empty-file stamp. | Symlink/dangling target preserves link-target stamp; hard-linked regular content and special files cannot gain accepted content stamps. |
| Reader cancellation (`CancellationToken`) | `None` permits an ordinary read. | Already-canceled token refuses before content acceptance. | Cancellation after a delivered chunk refuses without converting to read-stalled or successful EOF. |
| PowerShell digest result | Missing result or absent length/hash is not a valid empty file. | Length 0 plus valid empty digest is accepted; empty hash is not. | Actual result uses numeric Int64 length and a full hash string; no `.Length` on an array returned accidentally by PowerShell enumeration. Assert types in the harness. |

V-5/V-6 and V-7/V-8 own these matrix assertions. Do not introduce user-configurable
digest JSON or generic input parsing just to manufacture more cases.

- R-1: `CheckpointEofStallTests` retains all three consumers and formatting; the
  stamp arm must now inject the digest, not keep a buffered compatibility route.
- R-2: the existing receipt-size/deadline test pins the old 64 MiB cap, while new
  V-5/V-8 prove the intentionally different content limit.
- R-3: the existing tool/script source-shape tests preserve FIFO, symlink,
  directory, dangling and regular-file behavior. This is the Linux shape lane.
- R-4: existing output custody tests prove reading/digesting a file does not
  confer creation/deletion authority. The old disk ledger still cannot authorize
  generic deletion, and an existing directory still cannot gain custody.

Positive controls for later method-scoped SourceLanding Mutation: restore
`ReadBytes` at the .NET caller -> V-1; exclude ignored prior-bin or obj -> V-2/V-3;
hash a prefix only or compare size+mtime -> V-3 (must fail on the tail flip);
skip second-build baseline -> V-4 plus its tampered control; cap content at 64 MiB
or remove the 1 GiB/growth bound -> V-5; bypass handle checks, cancellation or
stall rearm -> V-6; leave the PowerShell stamp on ReadBytes -> V-7; omit EOF
reinspection or enlarge the receipt cap -> V-8; disable parser/ASCII validation
-> V-9/V-10 using a malformed/non-ASCII scratch copy. Full-file digest PCs must
demonstrate an assertion failure, not a missing API, build error or zero tests.
Run each exact method red then green; never mutate a whole suite or retain the
mutation in source. Preserve existing red-first evidence where applicable.

### Execution and cost

Two ordinary test-project builds total: S1 red and S3 green. All .NET and PS7
rows within each group reuse that group's build; the Windows parser command
needs no test-project build. Estimated total **28 minutes**, plus one explicitly
reported slot-gated isolated checkpoint-tool bootstrap per source group and
admission wait. The virtual 1 GiB read uses bounded buffers, not 1 GiB allocation;
do not claim it avoids all hash CPU. Serialize new large-file fixture activity
with the project's process limiter where children are spawned, and set the
script/shape rows Serial to avoid piling them on simultaneous large-file reads.

Use the checkpoint tool `run --plan <this-path> --rows <slice-row-ids>
--expected-source-sha <full-sha>` from the matching compiled bootstrap, then
own/await `wait` until exit is not 75. Every build/test driver takes its slot;
do not wrap an executor that acquires its own slots in another held lease.
Read the closed rows as separate lane selections: CP-1..CP-8 on the portable/Linux
qualification lane, CP-9 on the separately commissioned Windows parser lane at
the same source SHA. No native Windows process suite is added. The parser row
cannot run while this task is forbidden access to that lane; it is later Code/
Review work, never permission to touch the current desktop qualification.

Bootstrap the corrected S3 tool before its green selection (D-6), retain ignored
red/green receipts and exact SHA/provenance, then remove only owned alternate
outputs. Run evidence-diff guard over base..HEAD and validate each final green
receipt. Missing/zero tests, skipped required rows, cleanup refusal, missing PS5.1
evidence and slot timeout are incomplete verification, not green. Any inherited
failure claim requires its exact base-method reproduction; no suite rerun.

### Checkpoints

Closed proposal; TestDesign finalizes it before Code. Each row has one literal
filter/command and minutes separate from executed-result count. S1 rows are
intentionally red; S3 rows must all pass on their named lanes.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1532-red/` | large-stamp-red | `/*/*/CheckpointLargeStampTests/(C1532_regular_stamp_crosses_receipt_limit)\|(C1532_large_ignored_paths_remain_covered)\|(C1532_tail_edit_with_restored_mtime_is_tampered)\|(C1532_second_build_and_retry_admit_large_prior_output)` | V-1..V-4 | all 4 executed, intended oversized-stamp assertion reds, 0 skipped | 4 | 9 | true |
| CP-2 | S1 | CP-1 | script-stamp-red | `/*/*/CheckpointLargeStampScriptTests/C1532_script_stamp_matches_full_digest` | V-7 | 1 executed, intended oversized-stamp assertion red, 0 skipped | 1 | 2 | true |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1532-green/` | large-stamp-green | `/*/*/CheckpointLargeStampTests/C1532_*` | V-1..V-6 | all 6 passed, 0 failed/skipped | 6 | 9 | true |
| CP-4 | S3 | CP-3 | script-parity-green | `/*/*/CheckpointLargeStampScriptTests/C1532_*` | V-7..V-9 | all 3 passed, 0 failed/skipped | 3 | 3 | true |
| CP-5 | S3 | CP-3 | eof-regression | `/*/*/CheckpointEofStallTests/*` | R-1 | all 10 passed, 0 failed/skipped | 10 | 1 | false |
| CP-6 | S3 | CP-3 | receipt-cap-regression | `/*/*/HostCleanupCheckpointReceiptTests/Receipt_size_and_deadline_are_bounded` | R-2 | 1 passed, 0 failed/skipped | 1 | 1 | false |
| CP-7 | S3 | CP-3 | linux-shape-regression | `/*/*/CheckpointStampSafetyTests/tracked_stamp_shapes_are_bounded_and_leave_results_in_place` | R-3 | all 10 passed on Linux, 0 failed/skipped | 10 | 1 | true |
| CP-8 | S3 | CP-3 | custody-regression | `/*/*/HostCleanupCheckpointCustodyTests/(Existing_directory_cannot_gain_creation_authority)\|(Disk_ledger_cannot_authorize_generic_delete)` | R-4 | all 2 passed, 0 failed/skipped | 2 | 1 | false |
| CP-9 | S3 | n/a | windows-ps51-parse | `powershell.exe -NoProfile -NonInteractive -File scripts/test-checkpoint-stamp-ps51-parse.ps1` | V-10 | exit 0, PS 5.1 Desktop, 4 ASCII + 4 parse checks and malformed-copy negative control pass | n/a | 1 | true |

--- next stage ---
next: test-design
handoff: Finalize CARD-1532's full-digest red witnesses, native large-path fixture and embedded-reader boundary/EOF parity; pin the Windows 5.1 parser-only row. Keep file scopes disjoint from CARD-1531 and sequence shared docs/stamp-first qualification. No builds in TestDesign.
artifact: docs/superpowers/plans/2026-10-23-card-1532-streaming-source-stamps-plan.md
