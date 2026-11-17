# CARD-1969 slice 2 plan review, round 2 (independent, Final)

Date: 2026-11-17. Review task b66f32eb (Grok). Read-only. The only commit is this
document. No production code, test, or plan file was changed.

- Subject: `docs/superpowers/plans/2026-11-17-card-1969-prompt-span-plan.md` on
  `origin/feat/card-task-52484483`, tip **7622b7a7021ee651b5b74dc8791eb55e4daefb55**.
  Amendment task `52484483-27d0-4c68-99d9-89bbe5582315`.
- Range: `241f89463977945e43d7e8414edd5221501ba531..7622b7a7021ee651b5b74dc8791eb55e4daefb55`
  (two commits, the plan file only: 390 insertions, 158 deletions).
- Round 1: `docs/superpowers/reviews/2026-11-17-card-1969-prompt-span-plan-review.md` at
  `c9d2bcded61269cfcd33cb38029cfd3437d0f396` (B-1, B-2, A-1..A-6).
- Code read at `57563753c7544c45661cc68619b203d87a2684ec`, which matches this worktree
  and fetched `origin/master` `e07e3efe8510fc18c7fa043eb85c6488f1fd1de6` for every cited
  file (`git diff --stat` empty for `TranscriptPromptSpan.cs`, `TaskReportTurnSelector.cs`,
  `AgentTaskDispatcher.cs`, `AgentTaskReplyService.cs`, `AgentSessionRuntime.cs`,
  `DataRetentionService.cs`, `AppDbContext.cs`, `DispatchConcurrencySettingsService.cs`,
  `docs/session-runtime-invariants.md`, `TranscriptPromptSpanTests.cs`). The brief's
  master pin `dcc7d9591e3a8b6aada60e9247c0ffd8bf916888` is an ancestor of that tip.

**Verdict: CLEAN.** B-1 and B-2 are closed. A-1..A-6 are taken in the amendment. No new
blocker. No regression in the diff. Advisories do not hold the land.

## B-1. A prompt cannot stay hidden

Closed. Cached rows carry `Id` in a parallel `Guid[]` (`PromptRow` stays the four-field
record at `TranscriptPromptSpan.cs:59`, which `TaskReportTurnSelectorTests` constructs).
The stored stamp is computed from those rows (`Count`, `Ids[0]`, `Rows[^1].Sequence`,
`Ids[^1]`; empty is `(0, null, null, null)`). It is never a second statement.

A hit is one statement: `count(*)` plus four `LIMIT 1` laterals, one ascending and one
descending per kind, on `IX_TranscriptEntries_AgentSessionId_Kind_Sequence`
(`AppDbContext.cs:1751-1755`). No transaction on a hit. The session-kind read and the
rules-id read stay outside that statement; they do not supply the stamp or the prompt
list. The C# filter still runs on every call.

A stamp mismatch opens one `BeginTransactionAsync(IsolationLevel.RepeatableRead)`.
The precedent is `DispatchConcurrencySettingsService.cs:128`. Postgres takes the
snapshot at that transaction's first statement. The proof (stamp, `count` of prompt
rows with `Sequence <= m`, id at `Sequence = m`) and the tail (`Sequence > m`) are
inside it. `m` is the last cached row's sequence. The unique
`(AgentSessionId, Sequence)` index (`AppDbContext.cs:1747-1749`) makes that anchor one
row. A tail cannot repeat a cached row and cannot skip a prompt that committed before
the snapshot. The stored entry is `cached ++ tail`, or today's full projection when
the prefix proof fails, and its stamp is derived from the rows stored.

Interleaving of two `LoadAsync` calls and one insert, for a later decision:

- An insert changes `Count` and, when it rebases above the stored max
  (`AgentSessionRuntime.cs:1481`), `MaxSequence` and `TopId`. The next stamp statement
  misses. The miss path's snapshot either contains the insert or does not. If it does,
  the tail or the full read returns it. If it does not, the derived stamp does not
  contain it either, so the following call misses and reads it.
- Last writer wins can put an older snapshot back into the dictionary. That entry's
  stamp is the older rows. The next stamp, which sees the insert, does not match.
- A stamp statement that sees the insert, followed by a snapshot that does not, would
  require the row to disappear. The only production delete of `TranscriptEntries` is
  the whole-session `ExecuteDeleteAsync` at `DataRetentionService.cs:372`. That fails
  the prefix proof and full-reads. Omitting a row that was deleted before the snapshot
  is the snapshot's answer, not a hidden live prompt.
- The three decision reads are one `LoadAsync` each. The watchdog uses
  `TurnPrompts` (`AgentTaskDispatcher.cs:2511-2520`, `DeliveryFailTimeoutMinutes` 10
  at `DelegationSettings.cs:475`). The deferred-report sweep uses `RawPrompts` through
  `SelectAsync` (`AgentTaskDispatcher.cs:484`, `:4940`; `TaskReportTurnSelector.cs:64-65`).
  Settlement's notification read is a second `LoadAsync`
  (`AgentTaskReplyService.cs:3412`, `:3588`, `:3623-3625`). A prompt that commits
  between two of those calls is invisible to the earlier snapshot and visible to the
  later one, which is also true of today's two full reads. It is not retained as a hit.

The accepted D-4 hole is unchanged: an in-place `Text` or `Timestamp` edit, or a
`UserPrompt` / `QueuedUserPrompt` swap, keeps the stamp. No production writer does
that. A same-sequence replace of a middle row would also keep the four-tuple, and it
needs a single-row delete, which production does not have. Round 1 accepted that.
The amendment does not widen it.

W-7 is red at S2 and green at S3, against the code the plan cites.

- `TranscriptPromptSpanTests` builds a context with `CreateContext()`
  (`TranscriptPromptSpanTests.cs:269`) and does not use `TransactionalTestBase`, so a
  second connection's commit is visible to the next statement under read committed.
- Today's `LoadAsync` (`:80-120`) issues the prompt projection after the session-kind
  query. The interceptor arms on the first command whose text contains
  `TranscriptEntries`, `"Text"`, and `"Timestamp"`, which is that projection, not the
  session-kind query and not the `max(Sequence)` statement.
- S2 stores `max(Sequence)` from its own statement after the rows. The insert commits
  in that window, the stored max includes it, and the stored rows do not. The second
  load's max matches and returns the cache without the prompt. That is the red.
- S3's cold path derives the stamp from the `n` rows already read. The second load's
  live stamp is `n+1`, so it misses, and the tail reads the prompt. That is the green.
- W-7 requires the interceptor to report exactly one insert, so a disarmed or failed
  insert cannot pass.

## B-2. One lock, whole entries, bounded memory

Closed.

- One `lock` covers the dictionary, the LRU list, last-used times, and the character
  total. Entries are immutable (`Entry` of `ImmutableArray`s) and replaced whole.
  `PromptRow` is an init-only record, so a caller cannot mutate cached text.
  `RawPrompts` returns that array. No database I/O and no `await` under the lock.
- The key is `(GetConnectionString(), sessionId)`. The string is not logged.
- Bounds, labelled UNOBSERVED: 4,000,000 chars per entry, 32,000,000 total, 256
  entries, 120 s idle. A list over the per-entry ceiling is returned and not stored.
  Eviction, expiry, and the ceiling take the next call down the cold full read.
- W-9 has four cases: `entry-ceiling`, `total-bound`, `entry-cap`, `idle`. S2 ignores
  the bounds, so the last load hits and the prompt-text count is 0 (red). S3
  full-reads (green).
- M-7 records the server working set the day before and the day after the restart,
  plus one sample after 30 minutes of dispatch. A rise above the 64 MB text bound
  plus ordinary variance is a card, not an in-place retune.

## Advisories A-1..A-6

All six are taken. None declined.

| Id | Where the amendment answers it |
|---|---|
| A-1 | §3 stamp: one `Kind = … ORDER BY Sequence LIMIT 1` lateral per direction, not `Kind IN` plus `ORDER BY Sequence`. §6 and §8 keep `count(*)` as O(prompt index entries). |
| A-2 | D-4 and D-8 name `Text`, `Timestamp`, and a `UserPrompt` / `QueuedUserPrompt` swap. CP-5 and CP-6 run the nine edit-bearing classes. |
| A-3 | CP-4 `minExecuted` 13. `TaskReportHousekeepingTests` is 10 methods and 11 cases (`C714_grok_shape_on_other_provider_is_real_prompt` has two `[Arguments]`). `TaskReportTurnSelectorTests` is 2. |
| A-4 | W-4 asserts sequence and text. `PromptRow` still has no `Id`. |
| A-5 | Header cites the slice 1 review at `57d7a637e5f2273ff746f7a0dcd083a0b1a70bb7`. |
| A-6 | D-14 adds `### Prompt-span cache (CARD-1969 S2)` after the CARD-0714 heading, which is `docs/session-runtime-invariants.md:1608`. Slice 1's heading is `:343`. D-12 puts the connection string in the key. |

## Checkpoints

Six rows, CP-1..CP-6. First row `EstimatedMinutes` 15. `minExecuted` 13/13/23/13/10/48.
Counted from the tree: existing span methods 9; new `C1969s2_*` cases 13 (W-1 ×3, W-2,
W-3, W-4, W-5, W-6, W-7, W-9 ×4); W-8 is `C1969s2c_*`, which `C1969s2_*` does not match,
because the pattern requires the underscore immediately after `C1969s2`. CP-3 is
9+13+1 = 23. CP-5 methods are one case each except `C1156` (2) and `C768` (2), total 10.
CP-6 is 17+11+17+3 = 48. Each CP-5/CP-6 method name occurs once.

`C1969s2_*` on CP-2 does not pull W-8 in. W-8 stays a tip guard on CP-3. The committed
reds are W-3, W-4, W-5, W-7, and W-9 (8 cases).

Read-only import at `7622b7a70` (`ANTIPHON_LINUX_COMPILER_POLICY=0`, `TEMP=/tmp`, load
4.72, slot lease `984c0ea1-0ec6-422e-a95f-1ab9c733ac81` waited 0 s, held 7 s):

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1969-s2-r2-grok-import -- dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan docs/superpowers/plans/2026-11-17-card-1969-prompt-span-plan.md --out .antiphon/c1969-s2-r2-grok-import.yaml`

Exit 0, `imported 6 rows`. No import warning. The CS/CA warnings are the existing
`Antiphon.HostCleanup` and `Antiphon.Checkpoints` build warnings and name no row.
Three builds (`bin-c1969-s2-red/`, `bin-c1969-s2-stamp/`, `bin-c1969-s2-green/`);
CP-4, CP-5, and CP-6 reuse green. `timeoutMinutes` 45/45/45/24/45/45. Filters verbatim.
`timeouts.totalMinutes` 90 is the legacy field. The importer did not set
`ExplicitTotalMinutes`, so a run that omits `--total-timeout` derives `2*sum+10`
(`CheckpointApp.cs:168-170`, `RowTimeout.DeriveMinimumTotalMinutes`). CP-3..CP-6 derive
116 minutes. The plan says to pass that when a total is passed.

## Mechanics

- `git merge-tree --write-tree origin/master HEAD` against fetched
  `e07e3efe8510fc18c7fa043eb85c6488f1fd1de6`: tree `3b9815c75c13afc114af1b8e51c792fd8d1556b5`,
  exit 0, no conflict.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 241f89463977945e43d7e8414edd5221501ba531 -HeadRef 7622b7a7021ee651b5b74dc8791eb55e4daefb55`:
  commits=2 entries=0 violations=0, exit 0.
  Same script over `7901e9ba537b757740c2913aa2211d513b0b31f2..7622b7a70`: commits=3,
  entries=0, violations=0, exit 0.
- `scripts/impacted-classes.ps1 -MergeSubset -Range 7901e9ba5..7622b7a70 -Json`:
  `migrations=false`. The downgrade selector is not selected. Migration-impacted row
  count is 0.
- No OS-specific branch changes. No waiting-session change. PCs stay pending; this
  plan commissions none, and this round discharges none.

## Tests

None run. The subject changes one plan file. The brief runs the touched classes and
the guard batch only when the subject changes code or tests.
