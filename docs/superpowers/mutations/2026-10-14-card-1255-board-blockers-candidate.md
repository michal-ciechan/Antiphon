# CARD-1255 unbound SourceLanding Mutation candidate

All controls are pending. Code executed no deliberate mutants. Bind to landed source only
after ordinary Review and caller landing of original Code task
`dc2740ed-3d7c-42a1-9f45-359b810a068d`.

Plan: `docs/superpowers/plans/2026-10-14-card-1255-board-blockers-plan.md`.
Evidence: `.antiphon/task-dc2740ed.md` (actual tested SHAs and unedited checkpoint receipts).

| ID | Exact method / client case | Mutation and variants | Required red | Status |
|---|---|---|---|---|
| PC-1 | `CardRelationshipApiTests.Board_views_return_blockers_using_live_resolution_state` | Omit board enrichment; full and summary variants | OpenBlockerCount 0 versus literal 2 | Pending |
| PC-2 | Same method as PC-1 | Count all summaries, or remove Done / Canceled / archived resolution from CardBlockers (each variant independently); full and summary | Literal count 2 or 0 and exact five resolved flags fail | Pending |
| PC-3 | `CardRelationshipApiTests.Board_loads_all_blocker_summaries_in_one_query` | Resolve an unknown status; 1/40 cards and full/summary variants | OpenBlockerCount 0 versus 1; literal status 999 summary must be unresolved | Pending |
| PC-4 | Same method as PC-3 | Load summaries per card; full/summary 40-card variants | Relationship command count exceeds 1 | Pending |
| PC-5 | `CardRelationshipApiTests.Board_without_relationships_omits_blocker_fields` | Emit an empty blockers array or serialize zero openBlockerCount; each variant, full/summary | Raw JSON member omission assertion fails | Pending |
| PC-6 | `BoardPage.test.tsx`: `shows the Blocked chip from the board summary response on /boards` and `shows the Blocked chip from the board summary response on /boards/board-1?state=backlog` | Omit the badge / include resolved identifiers in its tooltip; each variant and route independently | Missing Blocked by CARD-0049 title or forbidden CARD-0048 tooltip | Pending |

Use method-scoped `/*/*/CardRelationshipApiTests/ExactMethod` selectors for .NET controls,
and the single client file with an exact anchored, regex-escaped `-t` name for each client case.
All red/restore/green cycles and missing-control discovery belong to Mutation. A zero-test,
build, fixture or receipt failure is not a control. No timeout widening or weaker assertions.
Existing CARD-0100 controls remain pending under that card's own plan; this work discharges none.

Restart: server after caller landing for board API activation; runner: none.
