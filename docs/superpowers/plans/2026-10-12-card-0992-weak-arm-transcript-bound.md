# CARD-0992 — bound the weak-arm transcript read

CARD-0993 (two queries per session, and the candidate-stream drain) stays untouched. The strong-arm LIKE floor from CARD-0974 stays as it is.

## Decision

A caller input whose normalized body is shorter than `PromptSubmissionMatch.MinMatchChars` (12) is weak: `IsCompleteIn` is true for every non-null `UserPrompt`, and `TaskInputPromptPattern` returns `%`. That `%` makes the session floor the earliest non-null user prompt, so the range read materializes every later `UserPrompt` and `AssistantText`.

The loader no longer puts `%` on the session pattern list.

- Strong inputs keep today's LIKE candidate plus the range read from that floor.
- Weak inputs use a separate probe.

Weak-arm bound:

| Step | Request | Why this cannot hide a real item |
|---|---|---|
| Prompt probe | The earliest 2 non-null `UserPrompt` rows, sequence and timestamp only. `Text` is not projected. | The matcher accepts the earliest row. A second non-null user prompt is after any complaint in that episode, and the weak body itself makes `IsCompleteIn` true, so the episode is superseded. The item count matches a full read. A complaint after that second prompt is not an item. |
| Assistant window | When exactly one non-null user prompt exists, the next 8 `AssistantText` rows, full text. | `TaskInputReadFailure.Matches` sees each one. Row 8 is inside this window. |
| Phrase probe | When those 8 rows exist and any weak input is still unresolved, the next 8 assistant rows whose text contains a `TaskInputReadFailure` phrase, ordered by sequence. Each phrase row stays until every weak input is resolved or the page ends. | `Matches` returns false without one of those phrases, so a true complaint cannot fail the filter. A match for one input does not hide a later input's complaint. The row just past the window is still judged. |
| Full range | Only when that phrase page is 8 non-matches. | A later true complaint could still exist. Unknown keeps today's `Sequence >= receipt` read of both kinds. |

`LastDeliveryBaselineSequence` is not the floor. The queue stores the latest retry baseline, not the first attempt (the CARD-0974 retry note). A baseline floor would miss that first complaint. The two-prompt rule follows the matcher instead.

Mixed session: a second user prompt suppresses weak-input evaluation. Those inputs stay in the task-input list, so a later strong input's soleInput count is the original cardinality. One user prompt shares that receipt; the strong suffix already covers it when the strong body matched, and each weak input on that receipt is still judged.

No non-null user prompt: no receipt and no item, same as today.

## Files

- `server/Application/Services/AttentionService.TaskInputs.cs` — split the weak probe from the strong LIKE floor.
- `server/Application/Services/TaskInputReadFailure.cs` — expose the existing phrase list to the probe.
- `tests/Antiphon.Tests/Application/TaskInputAttentionCostTests.cs` — table below. The byte/row seam is the existing transcript interceptor, extended to sum UTF-8 bytes of the `Text` column it materializes.

## Tests

Hand-written outcomes. Each case also plants 2,000 trailing or historical rows so a full read is obvious. `ReadOperations` counts transcript reader reads, including the terminal read that returns no row. `TextBytes` sums UTF-8 bytes of every materialized string. The closed case is two prompt headers (the kind name only, 20 bytes) plus that terminal read, so the cap is 3 reads and 256 bytes.

| Case | Setup | Items | Max rows | Max text bytes | Mutation that goes red | Red on today's builder |
|---|---|---|---|---|---|---|
| `closed-by-later-prompt` | 2,000-row history, short body `go`, complaint at the late delivery | 0 | 3 | 256 | Put `%` back on the pattern list (PC-1) | Row and byte caps fail |
| `complaint-on-last-bounded-row` | One user prompt, complaint on assistant row 8, 2,000 trailing assistants | 1 | 16 | 8192 | Do not judge the 8-row window (PC-3) | Row and byte caps fail; the item is present |
| `complaint-just-outside-bound` | One user prompt, 8 benign assistants, complaint on row 9, 2,000 trailing assistants | 1 | 16 | 8192 | Stop at row 8 with no phrase probe (PC-2) | Row and byte caps fail; the item is present |

V-1 is that table. R-1 is `Results_are_byte_identical_to_original_builder` (the `short` argument included). R-2 is `No_input_reads_no_transcripts_despite_large_history` and `Input_reads_exclude_unrelated_history_before_first_delivery` (still ≤ 8 reads). R-3 is the adjacent roster: `AttentionServiceTests`, `AttentionFeedNoiseTests`, `TaskInputReadFailureTests`, `AgentTaskInputSpillTests`, and `AgentTaskInputFallbackTests`.

The saturated phrase page (8 non-matching phrase hits, real complaint later) is the full-range fallback. It has no separate cost row; the boundary comparison below covers it. Mutation may add that control.

V-2 is `Boundary_matches_the_base_outcome`. Each row writes one base item count. The production builder and a frozen copy of the f6d468e71 loader must both produce that count, and their item JSON must match. On 3a64f4e5 the four rows marked red below failed: production returned 1 where the base returned 2, or 1 where the base returned 0.

| Case | Items | Red on 3a64f4e5 |
|---|---:|---|
| `complaint-row-7` | 1 | no |
| `complaint-row-8` | 1 | no |
| `complaint-row-9` | 1 | no |
| `complaint-row-17` | 1 | no |
| `no-assistant` | 0 | no |
| `receipt-at-end` | 0 | no |
| `receipt-at-start` | 1 | no |
| `no-nonnull-user-prompt` | 0 | no |
| `complaint-before-second-prompt` | 0 | no |
| `complaint-after-second-prompt` | 0 | no |
| `null-and-system-prompts` | 1 | no |
| `saturated-phrase-page` | 1 | no |
| `two-inputs-window-shortcut` | 2 | production 1, reference 2 |
| `two-inputs-phrase-shortcut` | 2 | production 1, reference 2 |
| `mixed-weak-strong-implicit` | 0 | production 1, reference 0 |
| `weak-strong-shared-receipt` | 2 | no |
| `two-tasks-shared-session` | 2 | production 1, reference 2 |

## Positive controls (pending Mutation)

| PC | Mutation | Red assertion |
|---|---|---|
| PC-1 | Put `%` back into the session pattern list. | `closed-by-later-prompt` exceeds 3 reads or 256 text bytes. |
| PC-2 | Return after 8 assistant rows and skip the phrase probe. | `complaint-just-outside-bound` finds 0 items. |
| PC-3 | Ignore assistant rows inside the 8-row window and start the phrase probe after them. | `complaint-on-last-bounded-row` finds 0 items. |
| PC-4 | Return from the assistant window when any one weak input has matched. | `two-inputs-window-shortcut` finds 1 item. |
| PC-5 | Return from the phrase probe after the first matching row. | `two-inputs-phrase-shortcut` finds 1 item. |
| PC-6 | Remove closed weak inputs before counting soleInput. | `mixed-weak-strong-implicit` finds 1 item. |
| PC-7 | Treat the first task's weak input as enough to finish the window. | `two-tasks-shared-session` finds 1 item. |
| PC-8 | Skip weak-input evaluation whenever the session also has a strong input. | `weak-strong-shared-receipt` finds 1 item. |

Post-land Mutation was not run. PC-1 through PC-8 stay pending. No timeout or assertion is loosened.

## Checkpoints

One isolated build. Filters are tree-node filters with wildcards. Run `dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-12-card-0992-weak-arm-transcript-bound.md --after S1 --expected-source-sha <committed SHA>`, then `wait` until the exit is not 75. `ANTIPHON_TASK_TOKEN` is present, so the slot lease is bound; do not print the token.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0992/` | weak-arm-bound | `/*/*/TaskInputAttentionCostTests/Weak_arm_reads_a_bounded_window*` | V-1 | 3 executed, 0 failed | 3 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S1 | `CP-1` | task-input-oracle | `/*/*/TaskInputAttentionCostTests/(No_input_reads_no_transcripts_despite_large_history*)\|(Input_reads_exclude_unrelated_history_before_first_delivery*)\|(Results_are_byte_identical_to_original_builder*)` | R-1, R-2 | 19 executed, 0 failed | 19 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S1 | `CP-1` | weak-arm-boundaries | `/*/*/TaskInputWeakArmBoundaryTests/Boundary_matches_the_base_outcome*` | V-2 | 17 executed, 0 failed | 17 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S1 | `CP-1` | attention-adjacent | `/*/*/(AttentionServiceTests*)\|(AttentionFeedNoiseTests*)\|(TaskInputReadFailureTests*)\|(AgentTaskInputSpillTests*)\|(AgentTaskInputFallbackTests*)/*` | R-3 | 212 executed, 0 failed | 212 | 12 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
