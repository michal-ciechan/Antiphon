# CARD-1369: EOF-stall append race and Stamp catch evidence

Date: 2026-10-20 (UTC). Task: `84630272-b437-40ab-b7fd-fd0a818c8cf5`.
Assigned branch: `feat/card-task-84630272`.
Investigated source: `359cc912235d840e9f94202592e86480d87db468`, the requested
CARD-1368 repair tip. All source line references below are at that commit.

## Verdict

**Confirmed by reconstruction from stored evidence and source, not by a new
execution.** The EOF-stall shortcut accepts the bytes already delivered when
their count equals a size inspected before reading. It does not establish that
the retained file still has that size. An append during the final stalled read
can therefore yield the same stamp as the captured original and a clean ledger
comparison even though the file has grown.

The precise event is a read requested at the **old EOF offset**, followed by
cancellation. No successful zero-byte read has established EOF. Calling this
"EOF was observed" would overstate the evidence.

The four CARD-1358 ledger witnesses bypass the real `Stamp` catch on their
failure path. There is no dedicated assertion for the catch's reason-formatting
contract in the inspected tests. The broader claim that no existing test can
ever enter the catch is too strong: an older Linux FIFO shape test can reach
the native refusal route, but does not assert its returned reason.

No builds, tests, timing probes, filesystem-race reproductions, or production
changes were performed. The observed evidence consists of stored card/review
records, committed source/test definitions, and Git history.

## Stored evidence and provenance

1. `scripts/card.ps1 get CARD-1369 -Board Antiphon` returned card
   `3739bce0-e977-4a5c-8318-d65bf075b15b`, board
   `8988ca03-7414-47ad-b0b6-51556c701703`, revision count `0`, token
   `7b6f2224-5654-4573-aee8-648c1330212e`, status `Backlog`. Its title is
   "CheckpointFileReader EOF-stall shortcut: re-check size after the stall
   (append race) and witness the real Stamp catch". Its description attributes
   both findings to Review `e2a6f1bf` of CARD-1358.
2. `scripts/delegate.ps1 -Status e2a6f1bf` and the read-only API resource
   `GET /api/agent-tasks/e2a6f1bf-f5db-403e-af3e-d395e301df8f` returned that
   stored Review result. `summary.id` is
   `e2a6f1bf-f5db-403e-af3e-d395e301df8f`, `summary.status` is `Succeeded`,
   `summary.createdAt` is `2026-10-19T19:54:02.888421Z`, and
   `summary.completedAt` is `2026-10-19T20:05:18.31277Z`.
3. Its `reviewEvidence` row is `d23bbcdc-8ed9-4b26-947c-530a46faee53`, subject
   `55a1921b-abe6-400c-8482-f5f52f4a81f2`, reviewed SHA
   `a653f881fe044acb9e75382deac13d10f9bece64`, outcome `Clean`, with
   `reviewedSourceClean=true`. Its **Result** explicitly discloses the append
   race and the injected-stamp coverage gap. These disclosures were findings
   from inspection, not reported executions of an append witness.
4. That Result reports run `20261019-195646-54f1`: CP-1 executed/passed/failed/
   skipped `2/2/0/0`, CP-4 `4/4/0/0`, source `a653f881f`, clean,
   `buildSource=verified`, slot granted, zero-second slot wait. These are
   historical claims from the retrieved record; this task did not rerun or
   independently validate its TRX. Their scope is the two reader tests and
   four ledger tests identified below, not the missing append scenario.

The API reads used the session's normal task-token header without printing or
persisting the token. No transcript absence or runtime incident is inferred
from these records.

## Mechanism at the assigned source

`src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs:16` sets the stall
window to three seconds; `:18` sets a chunk to 262,144 bytes. `Consume` creates
the clock-backed cancellation source at `:71`, opens the retained handle at
`:89`, and stores **one** `CheckpointHandleStat` value at `:90`. The value
type is declared in `src/Antiphon.HostCleanup/Checkpoints/ICheckpointCleanupIo.cs:8`.
The initial size-limit check uses this value at `CheckpointFileReader.cs:93`.

The loop at `CheckpointFileReader.cs:102` waits for a read with the linked
deadline token. Only delivered bytes reach the sink and increase `total`
(`:105-106`); progress re-arms the timer (`:107`). The timeout handler at
`:110-115` checks that the stall fired and the caller did not cancel, then
returns successfully when `total == (long)metadata.Size`. It neither inspects
the handle again nor requires a zero-byte read result. The one-byte-past-limit
request and limit guard (`:101-104`) cannot see an append that the stalled read
has not delivered.

This is not a pathname-substitution claim. The native stream uses the retained
handle (`src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs:382`). Its
`Inspect` implementation reads descriptor/handle metadata (`:384-403`), but
`Consume` calls it only before the loop. Linux opens have no exclusive-writer
lock here (`:365-369`); Windows opens use share flags `7` (`:373`).

### Reconstructed interleaving

The following concrete sizes instantiate the stored Review's scenario. They
are analytical values, not measurements from a newly executed test. Let `P`
be the captured 65,536-byte original and `H(P)` its SHA-256.

| Step | File and reader state | Source establishing the consequence |
|---|---|---|
| Capture | The original stamp is `file\|65536\|H(P)`; the file remains unchanged until this verification read. | `tools/Antiphon.Checkpoints/BuildConfinement.cs:123-125`, `:441-442`, `:456-457` |
| Inspect | The verification handle sees size 65,536. Its saved `metadata.Size` remains 65,536. | `CheckpointFileReader.cs:89-93` |
| Deliver prefix | Reads deliver exactly `P`; `total` becomes 65,536, and the stall window is re-armed. | `CheckpointFileReader.cs:102-107` |
| Append and stall | The next read is at offset 65,536. A one-byte append makes the same retained file 65,537 bytes before the deadline; that read delivers no result within the three-second window. The caller token remains uncancelled. | The event ordering is the stored Review scenario; the await and catch are `CheckpointFileReader.cs:102`, `:110`. |
| Shortcut | The catch compares `65536 == 65536` and returns success, although current file size is 65,537. | `CheckpointFileReader.cs:113-114` |
| Stamp | `ReadBytes` returns only `P`; `Stamp` produces the original length and hash again. | `CheckpointFileReader.cs:48-50`; `BuildConfinement.cs:441-442` |
| Compare | `VerifyLedger` compares that identical stamp; this entry contributes neither a changed nor an unreadable path. With no other findings, settlement is clean. | `BuildConfinement.cs:179-190`, `:194-199`, `:337-340` |

If the append bytes are delivered, the returned stamp differs. If the file
already differed from its captured contents before this read, hashing that
different prefix need not compare equal. If fewer than the initially inspected
bytes arrive, the handler throws `read-stalled`; if the caller cancels, its
exception does not match this catch filter. Those conditions explain why the
reported false equality needs this particular ordering.

### Introduction and CARD-1368 interaction

Git history locates the success shortcut in
`20bfd0abf0e7a3d937b724f5f5f3509d0e004441` (CARD-1358 S1). Its parent loop had
no catch converting cancellation into success. The pre-S1 reader therefore
refused a canceled read in this ordering; this conclusion follows from the
diff, not a base-commit run in this investigation.

`462288aeafe4123b068c8fb280a1fc381326e0c7` (CARD-1368 S2) moved the same
condition into shared `Consume` and preserved it. At the assigned tip both
`ReadBytes` (`CheckpointFileReader.cs:45-50`) and streaming `Digest` (`:53-63`)
share that branch. `BuildConfinement.Stamp` still uses `ReceiptFile.ReadBytes`,
with its 64 MiB cap (`ReceiptFile.cs:8-24`); it was not converted to streaming
by CARD-1368.

The streaming consumer is also relevant to scope: `CheckpointWrite.Describe`
uses `ReceiptFile.Digest` (`src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:89-93`)
and `MatchesCreation` compares that description (`:121-125`). Adoption has an
additional before/after stat check (`:107-110`). This report does not equate
every consumer's end-to-end outcome with the demonstrated ledger comparison;
the shared low-level shortcut is the confirmed common mechanism.

## The real Stamp catch and the coverage boundary

The catch is `tools/Antiphon.Checkpoints/BuildConfinement.cs:444-447`, inside
`Stamp(string full)` (`:427`). It catches `IOException`,
`UnauthorizedAccessException`, and `SecurityException`, returning
`"unreadable|" + Reason(ex)`. Regular-file content reaches
`ReceiptFile.ReadBytes(full)` at `:441`. That wrapper turns reader IO,
invalid-data, and cancellation errors into an `IOException` retaining the
inner exception (`src/Antiphon.HostCleanup/Checkpoints/ReceiptFile.cs:23-24`).

`Reason(Exception)` at `BuildConfinement.cs:416-424` walks to the innermost
exception, replaces CR and LF with spaces, trims, falls back to the exception
type name if empty, and truncates to 200 characters. Consequently a normal
reader stall is formatted from the inner `read-stalled: ...` message, not the
outer path-bearing `receipt-read-refused: ...` wrapper.

The W-3..W-6 mapping is explicit in the CARD-1358 plan at
`docs/superpowers/plans/2026-10-19-card-1358-ledger-read-deadline-plan.md:118-121`.
Their implementations are all in
`tests/Antiphon.Tests/Checkpoints/CheckpointConfinementLedgerTests.cs`:

| Existing witness | Injected failure path |
|---|---|
| W-3, `ledger_entry_unreadable_after_release_is_refused_unreadable_not_tampered` (`:376`) | Capture receives `StalledStamp` at `:382`; the flag changes at `:383`. |
| W-4, `tampered_entry_beside_an_unreadable_entry_is_still_refused_tampered` (`:398`) | Same seam at `:403-404`; a separate real file change drives the changed-path comparison. |
| W-5, `in_slot_baseline_entry_unreadable_at_settlement_is_refused_unreadable` (`:422`) | `Settle` receives `StalledStamp` at `:429`. |
| W-6, `scheduler_reports_an_unreadable_ledger_entry_as_unreadable` (`:439`) | The driver receives `StalledStamp` at `:446`. |

`StalledStamp` at `:590-595` returns the literal
`unreadable|receipt-read-refused: read-stalled: 0 of 7864320 bytes; no progress within 3s`
for the selected failing entry. It calls the real `BuildConfinement.Stamp`
only in its other branch. These witnesses establish classification and report
propagation after a stamp exists; they do not establish exception capture,
innermost-message selection, CR/LF handling, empty-message fallback, or truncation.

The two existing `HostCleanupCheckpointReadDeadlineTests` exercise
`ReceiptFile.ReadBytes` with the controlled fixture, not `BuildConfinement.Stamp`
(`tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointReadDeadlineTests.cs:28`,
`:46`, `:58`). The progress case advances one second on every read (`:26`);
the stall case stops on read three after 131,072 of 7,864,320 bytes (`:44-48`).
Neither combines delivery of the original full size, an append to the held
file, and a stalled final read. The repaired receipt test likewise blocks
before delivering the payload (`HostCleanupCheckpointReceiptTests.cs:107-135`).

An important qualification: `CheckpointStampSafetyTests.cs:15,25-43` includes
the Linux `tool/fifo` case. `scripts/test-checkpoint-stamp.ps1:38` creates a
FIFO and `:50` calls real `BuildConfinement.Settle`. A FIFO that reaches
`ReceiptFile.ReadBytes` is refused by `CheckpointFileReader.cs:77-79` and then
the real catch. However, the script asserts only `tampered` and a named path
(`:52`), after changing the tracked filesystem shape. It does not inspect the
reason; an `absent` stamp or a different unreadable reason is not distinguished
by that assertion. No execution/coverage receipt for that case was retrieved.
Thus "no dedicated catch/formatting witness" is supported; "zero catch
executions anywhere" is not established.

## Existing observation facilities

The reader already accepts `CheckpointCleanupContext`, and the fixture owns
`ControlledTimeProvider` (`HostCleanupCheckpointFixture.cs:10-27`). Its virtual
IO has `HandleMetadata`, `StreamFactory`, and `InspectFails` (`:175-177`), and
the held handle records inspection events (`:296-308`). Existing `PacedStream`
advances virtual time inside `ReadAsync`
(`HostCleanupCheckpointReadDeadlineTests.cs:74-82`); the clock's `Advance` and
timer observations are at `tests/Antiphon.Tests/TestHelpers/ControlledTimeProvider.cs:41,59-68`.

There is a material distinction for any later evidence: virtual `Put` replaces
the node and assigns a new inode (`HostCleanupCheckpointFixture.cs:193-197`),
whereas `Held` retains the originally opened node (`:296`). A pathname
replacement is therefore not evidence that the retained file grew.
`BuildConfinement.Stamp` takes only a path and uses the immutable default
native context through `ReceiptFile.ReadBytes`; the default is defined at
`src/Antiphon.HostCleanup/Checkpoints/CheckpointCleanupDefaults.cs:6-9`.
These are existing capabilities and limits, not a proposed test implementation.

## Remaining uncertainties and scope

- No native filesystem timing incident or frequency is established. The
  required three-second silence is stipulated by the stored finding; its
  mechanism is reconstructed exactly from the source. An executed deterministic
  witness would add runtime evidence; it is outside this reading-only brief.
- No fresh evidence proves the `Stamp` formatting branches, nor that an older
  shape test entered a particular catch. Branch-sensitive execution evidence
  would settle that narrower coverage question.
- Same-size rewrites, append-then-truncate, later writes after a final check,
  and the timing of synchronous stream disposal are not resolved by this
  reconstruction. It establishes the reported size-growth ordering only.
- CARD-1368 landing/activation status was not established by this investigation;
  the requested source tip was checked directly and remained the working base.
  The caller's dependency on CARD-1368 landing remains in the handoff.

## Stage boundary

The supplied brief requests fix design, a red-first witness, a Code-ready
`### Checkpoints` table, a 30-60 minute Code estimate, and `next: code`.
The governing `stage-investigate` instruction instead requires evidence only,
forbids fix design, and selects `next: plan` after confirmation; the repository
role table agrees (`docs/orchestration-loop.md:762`). Accordingly this artifact
does not invent a checkpoint manifest or claim that budget has been validated.
Plan owns those deliverables. No implementation work is blocked on an answer
needed to establish the mechanism.

## Not done, noted

The card's proposed retained-handle size recheck after an EOF stall, with deterministic append/catch witnesses, is deferred to Plan; no fix, test, or checkpoint design was implemented or executed here.

--- next stage ---
next: plan
handoff: Root cause confirmed at 359cc912235d840e9f94202592e86480d87db468. After CARD-1368 lands, plan CARD-1369 from this evidence, including deterministic append and real Stamp-catch verification, an exact checkpoint manifest, and the requested 30-60 minute Code budget; qualify the older FIFO test's coverage.
artifact: docs/investigations/2026-10-20-card-1369-eof-stall-append-race.md
