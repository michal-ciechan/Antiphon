# CARD-1910 unbound Mutation candidates

Original Code/landing owner: 09095036-e6f5-4b5b-80bd-4c17f791495d.
Plan: `docs/superpowers/plans/2026-11-14-card-1910-floor-index-plan.md`.
Status: every candidate and variant below is PENDING for Mutation.
Binding required after Code, ordinary Review and confirmed publication. This is
neither a bound pack nor a mechanically eligible execution manifest. Mutation
owns binding to actual landed L, deliberate mutants, baseline/red/restore/green,
reachability and missing-control discovery. Code's ordinary pre-fix red is not
a Mutation cycle. No control has been executed or discharged by this candidate.

All detecting tests are in `tests/Antiphon.Tests`. Each precise method filter
below selects one TUnit result, with zero skips. Mutation must observe exactly
one intended assertion failure for red and one pass after exact restoration;
zero tests, build/fixture errors and unrelated failures are not the control.

| ID/variant | Suggested deliberate defect | Exact detecting filter | Required assertion red |
|---|---|---|---|
| PC-1/scan | Restore scan-based NewestBetween/NextSequence over constructor rows and the real predicate; retain classification instrumentation. | `/*/*/TaskReportFloorIndexTests/Floor_classifies_each_row_once_for_twenty_thousand_rows_and_two_thousand_boundaries` | classification count exceeds 20,000; the floor assertion still succeeds |
| PC-2/string-copy | Restore string TrimStart in IsLocalCommandRecord. | `/*/*/LocalCommandRecordTests/Record_check_allocates_zero_bytes` | allocated bytes exceed zero, with the matching-count assertion intact |
| PC-3/before-inclusive | Include a prompt at the exclusive before bound. | `/*/*/TaskReportFloorIndexTests/Floor_matches_original_scan_for_seeded_sessions` | old-oracle equality fails |
| PC-3/floor-inclusive | Admit a prompt whose sequence equals the exclusive floor. | `/*/*/TaskReportFloorIndexTests/Floor_matches_original_scan_for_seeded_sessions` | old-oracle equality fails |
| PC-3/first-tie | Return the first qualifying equal-sequence real row instead of the last. | `/*/*/TaskReportFloorIndexTests/Floor_matches_original_scan_for_seeded_sessions` | row equality against the linear oracle fails |
| PC-3/housekeeping | Include housekeeping rows in the real-prompt index. | `/*/*/TaskReportFloorIndexTests/Floor_matches_original_scan_for_seeded_sessions` | old-oracle equality fails |
| PC-3/omit-queued | Exclude QueuedUserPrompt from real-prompt ownership. | `/*/*/TaskReportFloorIndexTests/Floor_matches_original_scan_for_seeded_sessions` | old-oracle equality fails |
| PC-4/command-case | Make the command-name prefix comparison case-insensitive. | `/*/*/LocalCommandRecordTests/Record_check_matches_original_over_corpus` | mixed-case near-marker differs from the private old predicate |
| PC-4/stdout-case | Make the stdout prefix comparison case-insensitive. | `/*/*/LocalCommandRecordTests/Record_check_matches_original_over_corpus` | mixed-case stdout marker differs from the private old predicate |
| PC-4/trim | Remove leading-whitespace trimming. | `/*/*/LocalCommandRecordTests/Record_check_matches_original_over_corpus` | leading-whitespace record differs from the private old predicate |

PC-1/PC-3 touch `server/Application/Services/TaskReportTurnSelector.cs`;
PC-2/PC-4 touch `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`.
Each variant needs a separately resolved exact patch and assertion oracle at L.
The variants in each source file are not independent and must not be batched.
Run only inherited local children in the assigned SourceLanding snapshot. Keep
forced builds and exact method runs behind the host build-slot/checkpoint gate.
Retain source/build identity and fresh TRX for every phase in external evidence.
Restore byte-for-byte through Mutation's owner; never commit/push from that
snapshot. Do not widen timeouts, remove barriers or weaken any assertion.

Ordinary proof measured a zero-byte 60,000-call local-record loop after warmup,
and one classification per row for 20,000 rows/2,000 boundaries. These are outcome
oracles; no wall-clock performance assertion is a replacement. Budgets and exact
payload/preimage hashes remain unresolved until the landed pack is bound.
