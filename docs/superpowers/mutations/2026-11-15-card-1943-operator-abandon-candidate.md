# CARD-1943 unbound Mutation candidate

Original Code / landing owner: 73e7dfdd-5bff-4cef-b23f-42dee4f7113c.
Plan: ../plans/2026-11-15-card-1943-operator-abandon.md.
Status: unbound; every PC and variant is pending SourceLanding Mutation.
The operator waived Code mutation checks on 2026-11-12. Ordinary defect-first
tests are recorded separately; no deliberate mutant was applied here.
Bind a confirmed landing before method-scoped red/restore/green execution.
Mutation owns missing-control discovery and additional variants.

| ID | Pending variants | Guarded defect | Exact ordinary method |
|---|---|---|---|
| PC-1 | field name; message; over-limit acceptance | drift the 422 reason validation contract | SessionQueueAbandonTests/Abandon_reason_problem_details_pin_the_exact_field_and_message |
| PC-2 | row cap; byte cap; cursor; truncation; ordering | unbounded or incomplete queue traversal | SessionQueueAbandonTests/Current_unconfirmed_listings_are_bounded_and_every_row_is_reachable |
| PC-3 | cap; offset; truncation; ordering; full census | unbounded or incomplete attention traversal or undercounted census | SessionQueueAbandonTests/Current_unconfirmed_listings_are_bounded_and_every_row_is_reachable |
| PC-4 | absent action; stale-generation item; resend/replacement; false delivery verdict | silently closed rules generation or canceled-row resurrection | SessionQueueAbandonTests/Abandoned_current_rules_keep_an_explicit_fresh_start_attention_until_generation_changes |

Use the exact `/*/*/SessionQueueAbandonTests/ExactTestMethod` per cycle, inspect
each native parameterized result and expected assertion, restore, rebuild and
run that same method green. Preserve the existing late-prompt and no-input
witnesses as adjacent restored-source smoke. No PC is passed by this document.
