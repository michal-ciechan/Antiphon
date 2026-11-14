# CARD-1914 unbound Mutation candidates

Original Code/landing owner: 33a55e39.
Plan: `docs/superpowers/plans/2026-11-14-card-1914-trim-span-plan.md`.
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
| PC-1/notification-copy | Restore `string.TrimStart` in `IsTaskNotificationPrompt`. | `/*/*/LocalCommandRecordTests/Notification_check_allocates_zero_bytes` | allocated bytes exceed zero, hit count unchanged |
| PC-2/compaction-copy | Restore `string.TrimStart` in `IsCompactionContinuationPrompt`. | `/*/*/LocalCommandRecordTests/Compaction_check_allocates_zero_bytes` | allocated bytes exceed zero, hit count unchanged |
| PC-3/name-miss-copy | Restore `string.TrimStart` in `TryReadLocalCommandName`. | `/*/*/LocalCommandRecordTests/Command_name_miss_allocates_zero_bytes` | allocated bytes exceed zero on a non-matching prompt |
| PC-3/name-return-copy | Same string copy, observed on a matching wrapper. | `/*/*/LocalCommandRecordTests/Command_name_return_allocates_only_the_name` | bytes beyond the returned name exceed zero |
| PC-4/echo-copy | Restore `string.TrimStart` in `IsRawLocalCommandEcho`. | `/*/*/LocalCommandRecordTests/Raw_echo_check_allocates_zero_bytes` | allocated bytes exceed zero for the HashSet and List paths |
| PC-5/notification-case | Compare the notification prefix case-insensitively. | `/*/*/LocalCommandRecordTests/Notification_check_matches_original_over_corpus` | mixed-case marker differs from the private string oracle |
| PC-5/command-case | Compare the command-name tag case-insensitively. | `/*/*/LocalCommandRecordTests/Command_name_matches_original_over_corpus` | mixed-case tag differs from the private string oracle |
| PC-5/trim | Stop trimming leading whitespace in one of the four methods. | `/*/*/LocalCommandRecordTests/Notification_check_matches_original_over_corpus` | a whitespace-prefixed marker differs from the private string oracle |

Every variant touches `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`.
The variants are not independent and must not be batched. Run only inherited
local children in the assigned SourceLanding snapshot. Keep forced builds and
exact method runs behind the host build-slot/checkpoint gate. Retain
source/build identity and fresh TRX for every phase in external evidence.
Restore byte-for-byte through Mutation's owner; never commit/push from that
snapshot. Do not widen timeouts or weaken any assertion.
