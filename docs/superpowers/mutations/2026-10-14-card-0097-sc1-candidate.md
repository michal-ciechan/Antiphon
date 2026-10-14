# CARD-0097 S-C1 unbound Mutation candidate

Status: pending, not commissioned. Ordinary Review and caller landing of original Code task
`6812413b-f769-4554-9d4a-5b73c6a11afa` must precede SourceLanding Mutation.

Plan: `docs/superpowers/plans/2026-10-14-card-0097-matrix-plan.md`.
Test design: `docs/superpowers/plans/2026-10-14-card-0097-matrix-test-design.md`.

| Control | Exact Vitest title | File under client/src/features/settings | Mutation | Expected red | Status |
|---|---|---|---|---|---|
| PC-24 | C097M V-C1 role-policy | routingSettingsModel.test.ts | Ignore effective data and return the former unset label | Literal RolePolicy target label differs from Unset — dispatch blocks | Pending |
| PC-25 | C097M V-C4 pin-overlay-first | RoutingSettingsTab.test.tsx | Render the stored rule before the effective overlay | First-text and document-order assertions fail | Pending |
| PC-26 | C097M V-C5 default-column-and-fallback | RoutingSettingsTab.test.tsx | Omit the default column suffix | Named default columnheader is absent | Pending |

Select one file and the exact anchored Vitest title through the host build-slot gate for each
red/restore/green cycle. Bind mutations to the landed snapshot. Zero tests, fixture errors,
compilation errors and Code's ordinary tests-first run are not a PC red result.

Mutation also owns missing-control discovery for the 11 degraded/agent DOM combinations and
the model table's diagnostic cases. No new PC IDs or successful controls are claimed here.
Other plan controls remain with their assigned slices; this candidate waives none.
No deliberate mutant was executed by Code. Do not widen timeouts or loosen assertions.
Restart: none.
