# CARD-1969 slice 2 plan review, round 2 (diff-only)

Date: 2026-11-17. Review task ce86e086 (Claude Opus). Read-only review. No production code, test
or plan file was changed.

- Subject: `docs/superpowers/plans/2026-11-17-card-1969-prompt-span-plan.md` on
  `origin/feat/card-task-52484483`, tip **7622b7a7021ee651b5b74dc8791eb55e4daefb55**.
  Reviewed range `241f89463977945e43d7e8414edd5221501ba531..7622b7a70` (2 commits, that one file).
- Round 1: `docs/superpowers/reviews/2026-11-17-card-1969-prompt-span-plan-review.md` at
  `origin/feat/card-task-9dbbe5bf` `c9d2bcded61269cfcd33cb38029cfd3437d0f396` (B-1, B-2, A-1..A-6).
- Code re-read at `7622b7a70` (base `7901e9ba5`; master `57563753c` changes none of these):
  `TranscriptPromptSpan.cs:56-120`, `DispatchConcurrencySettingsService.cs:120-135`, the
  `UseNpgsql` sites (no `EnableRetryOnFailure` or execution strategy under `server/`), and the
  declaration, `[Arguments]` count and platform gates of every CP-5/CP-6 method.
- **Independence note (for the caller, not a plan defect):** the board shows the amendment task
  `52484483-27d0-4c68-99d9-89bbe5582315` as ClaudeCode (the plan header says "Claude Opus"), not
  Grok as the brief says. Only the original plan `2048f2d0` is Grok. This reviewer is also Claude,
  so under the standing different-companies rule this round's author and reviewer are the same
  company for the reviewed diff.

**Verdict: CLEAN.** B-1 and B-2 are resolved. A-1..A-6 are all taken. No blocker was introduced.
Three advisories below do not hold the land.

## B-1: resolved

- **Stored stamp derived from stored rows** (§3, D-11): every projection that fills the cache also
  returns `Id`. `Count`, `FirstId`, `MaxSequence` and `TopId` come from the held list (empty list is
  `(0, null, null, null)`). No second statement produces a stored stamp. A prompt that commits after
  the rows' statement is in neither the rows nor the derived stamp, so the next live stamp differs
  and the call misses. This closes the torn pair whatever the isolation level of the cold read.
- **Hit = one statement** (`-- prompt-span.stamp`): a count plus four one-row `LIMIT 1` laterals,
  first and top per kind (`Kind = …`, A-1 shape). One statement is one snapshot, and the call opens
  no transaction. The live stamp is compared with the derived cached stamp, so neither side can tear.
- **Miss path**: one explicit `BeginTransactionAsync(IsolationLevel.RepeatableRead)` (the precedent
  at `DispatchConcurrencySettingsService.cs:128` exists and has the same `CurrentTransaction`
  guard). Proof (stamp plus `count(<= m)` and the id at `m`) and the tail or full read share the
  first statement's snapshot. With no execution strategy configured, `BeginTransaction` is legal.
  A read-only RR transaction cannot raise a serialization failure.
- **Tail anchor**: `m` = the last cached row's sequence. The tail is `Sequence > m`, so it cannot
  duplicate a cached row. In the same snapshot, a row committed below `m` changes `count(<= m)` and
  forces a full read, so nothing can be skipped. The tail is accepted only when its count and last
  id match the in-transaction stamp. `cached ++ tail` then stores with a derived stamp.
- **Ambient transaction**: bypass the cache entirely, with no nested transaction.
- **W-7 is genuinely red at S2 for the stated reason.** In EF Core 9.0.21, `ToListAsync`
  consumes every row and then disposes the `RelationalDataReader`. That calls
  `DataReaderClosingAsync` before the close, so the interceptor's second-connection insert commits
  after the projection's snapshot. S2 then reads `max` in its own READ COMMITTED statement and
  sees the insert. It stores `n` rows under max `n+1`, and the second load hits and lacks the
  prompt: red on the content assertion. At base and S1 (inert seam) the second load is a full read
  that sees `n+1` rows: green. At S3 the derived stamp is the `n`-row stamp, so the live stamp
  misses, the proof holds and the tail returns the inserted row: green. The interceptor's match
  (`TranscriptEntries` + `"Text"` + `"Timestamp"`) skips the session-kind read, the
  `SessionQueuedMessages` read and S2's max statement. The "inserted exactly one" assertion stops a
  vacuous pass.

## B-2: resolved

- One `lock (_gate)` over the dictionary, the LRU list, `LastUsedMs` and the character total. Each
  `Entry` (stamp, `ImmutableArray` rows and ids, chars) is built outside the lock and swapped whole.
  The lock holds no I/O, no `await` and no row-proportional allocation. Last writer wins and is safe,
  because every entry's stamp is derived from its own rows. `PromptRow` is a record and `RawPrompts`
  is already `IReadOnlyList<PromptRow>` (`TranscriptPromptSpan.cs:75-78`), so sharing the immutable
  rows across callers changes no signature.
- The key is `(GetConnectionString(), sessionId)`, held in memory only (A-6).
- Bounds: 4,000,000 chars per entry (larger lists are not stored and keep today's read), 32,000,000
  chars in total, 256 entries, and a 120 s idle expiry on `Environment.TickCount64`. All are
  labelled UNOBSERVED/chosen. Eviction, expiry and the ceiling fail closed to the cold full read.
  W-9's four cases pin each one (red at S2 because S2 ignores bounds and clock, green at S1 and S3).
- Task end and session reload: the idle expiry replaces a terminal-writer hook (rejected with a
  reason). Re-ingest, delete and rebase are stamp changes that replace the entry whole.
- M-7 adds a process-memory measurement after deploy, with a 64 MB text bound as the finding line.
- W-8 is a concurrency guard at the tip and does not claim a deterministic red.

## Advisories A-1..A-6: all taken

A-1 per-kind `LIMIT 1` laterals. A-2 widened D-4/D-8/F-1 to `Kind`/`Timestamp`, and CP-5/CP-6
run the nine in-place-edit classes. A-3 CP-4 = 13. A-4 W-4 is by sequence and text. A-5 header
corrected. A-6 S3 doc paragraph (D-14) plus the connection-string key. The amendment also fixed a
defect of its own: S1 witnesses called a seam that did not exist at base. S1 now adds an inert seam.

## Checkpoints and import

- CP-5/CP-6 counts checked in source: C1156 2, C768 2, `Boundary_matches_the_base_outcome` 17,
  `Single_query_boundary_matches_frozen_builder` 11, `Results_are_byte_identical_to_original_builder`
  17, `Weak_arm_reads_a_bounded_window` 3. The other six methods are single cases, each declared
  once in `tests/`. `PermissionWaitTests` is the partial class in `PermissionWaitDeliveryTests.cs`.
  No method or class carries a platform gate or skip. CP-5 = 10 and CP-6 = 48 match.
- `C1969s2_*` excludes W-8's `C1969s2c_` name. CP-1/CP-2 = 13 (3 + 6 + 4), CP-3 = 9 + 13 + 1 = 23.
- Re-import at `7622b7a70` (`ANTIPHON_LINUX_COMPILER_POLICY=0`, `TEMP=/tmp`, load 6.08, slot
  waited 0 s, held 11 s):
  `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1969-s2-r2-import -- dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan docs/superpowers/plans/2026-11-17-card-1969-prompt-span-plan.md --out .antiphon/c1969-s2-r2-import.yaml`
  The import exited 0 and printed "imported 6 rows", with 0 import warnings (only existing CS/CA
  compiler warnings in `Antiphon.Checkpoints`/`Antiphon.HostCleanup`). The first row's
  EstimatedMinutes is 15. Ids are CP-1..CP-6. There are three builds
  (`bin-c1969-s2-red`, `-stamp`, `-green`), and CP-4..CP-6 reuse green. `minExecuted` is
  13/13/23/13/10/48 and `timeoutMinutes` 45/45/45/24/45/45. Filters are verbatim. This matches the
  plan's Import section.

## Mechanics

- `git merge-tree --write-tree origin/master(57563753c) 7622b7a70`: tree `643bb7e3d`, no conflict.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 241f89463 -HeadRef 7622b7a70`:
  commits=2 entries=0 violations=0, exit 0. Over `7901e9ba5..7622b7a70`: commits=3, 0 violations.

## Advisories (round 2; do not hold the land)

- **R2-A1. W-8 connection fan-out.** 16 readers plus a writer, each on its own context, hold about
  17 connections on the shared test Postgres while CP-3's other cases run. Under load this risks
  53300 `too_many_clients`. If that appears, mark W-8 `[NotInParallel]` or cap its readers; do not
  widen timeouts.
- **R2-A2. Miss-path wording.** Step 2 should say outright that the tail-acceptance stamp is the
  in-transaction proof stamp, not the pre-transaction hit-check stamp. It should also say that the
  transaction is `await using` (rolled back on cancel or exception). Both are implied, and Code
  should write them that way.
- **R2-A3. Thrash signal.** If real sessions sit near the 4M-char ceiling, the 32M total holds
  about 8 of them. An LRU over more open sessions then goes all-cold: the stamp rate falls and
  `prompt-span.read` stays near today's statement-3 rate. M-6's "read rate near stamp rate" test
  does not catch that. M-6 should also compare `prompt-span.read` calls/min with the old 56/min.

## Tests

None run, by design: the subject changes no code and no test (one plan file). The touched-class
and guard batch reruns do not apply. All PCs stay pending.
