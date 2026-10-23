# CARD-1518 unbound SourceLanding Mutation candidate

Original Code/landing owner: `9d2b827a-8368-454d-90dc-35c019db4359`.
Plan: [permission wait classification](../plans/2026-10-23-card-1518-permission-wait-classification-plan.md).
This is an inventory, not a bound pack or execution receipt. Every PC and variant
below is pending. SourceLanding Mutation owns deliberate mutants, reached red,
restore/green and missing-control discovery. Review and landing precede binding.

Use exact method selection `/*/*/PermissionWaitTests/<method>` from the plan's V
mapping. A variant must reach its own behavior assertion; a build/fixture error,
zero count or timeout is not a positive control. Do not widen any timeout.

| PC/variants (all pending) | Guard / method |
|---|---|
| PC-1.early | Remove early pass; V-1 opening Warning/note before stall. |
| PC-2.absent, empty-contract, state-unknown, state-invalid, input-history, negative-output, negative-input, empty-epoch, wrong-session | Admit one invalid actual observation; V-2 must reject that labeled case. Zero counters remain valid. |
| PC-3.missing-generation, default-generation, older-generation, newer-generation, attempt, accepted-generation-race, runner-binding-race | Bypass each individual identity fence; V-2/V-3. |
| PC-4.serial-order, epoch-replacement | Allow an older blocked read to replace the later clear; V-3. |
| PC-5.refresh-on-access, age-90-inclusive, expired, negative-age, failed-refresh | Change the completed-read freshness rule independently; V-3 (stale supplied policy facts also V-5). |
| PC-6.phase | Leave current ToolCall LocalExecution; V-4 exact permission summary. |
| PC-7.blocked, unknown, clear, ceiling, model, disarmed | Pause/change the original clock; V-4/V-5. |
| PC-8.concurrent, restart, output-animation, unknown-resolution, clear-reappearance | Defeat episode cardinality independently; V-6. |
| PC-9.atomic | Split warning and note persistence across commits; V-6 injected failure before save leaves neither opening nor note. |
| PC-10.kind, completion-stamp, destination-none, destination-unavailable | Change early note identity/routing; V-7. |
| PC-11.keyed-insert, lost-link, lost-wakeup | Duplicate or lose authoritative queue custody; V-8. |
| PC-12.queued, head-only, missing-generation, wrong-generation, missing-floor, sent-only, wrong-destination, debt-generation | Admit each false receipt independently; V-8 (real complete delivery V-7). |
| PC-13.Check, attention, stall, stale-history, generic-kind | Drop/mislabel one consumer; V-9. |
| PC-14.terminal-refine, operator-authority, missing-session-release | Resurrect Failed or bypass operator gate; V-10, R-4. |
| PC-15.cadence, due-at-60, concurrent-sharing, four-read-limit, peer-isolation | Remove each bound independently; V-11. |
| PC-16.input, launch, stop, source, release | Introduce an automatic terminal side effect after a positive diagnosis; V-12; V-1/V-4 also retain custody. Explicit authorized fixture release in V-10 is not automatic release. |
| PC-17.mapping, rail, modal-action | Lose the kind/rail mapping or introduce an unauthorized button; V-13 real AttentionPanel render. |

Further optional-field, callback-race or missing-controls found by Mutation remain
its discovery work; this inventory does not assert exhaustiveness or PC-clean.
