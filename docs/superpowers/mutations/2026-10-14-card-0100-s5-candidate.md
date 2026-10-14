# CARD-0100 S5 unbound Mutation candidate

Status: pending, not commissioned. Separate Review and caller landing of original Code task
`a32e5d47-8756-49eb-8b72-a139962e6e7e` must precede SourceLanding Mutation.

Plan: `docs/superpowers/plans/2026-10-14-card-0100-card-relationships-plan.md`.
Binding test design: `docs/superpowers/plans/2026-10-14-card-0100-test-design.md`.

| Control | Exact test in `client/src/features/board/CardBlockers.test.tsx` | Mutation | Expected red | Status |
|---|---|---|---|---|
| PC-32 | `adds a blocker with the current token and shows it with its status` | Omit `concurrencyToken` from the add POST | Exact POST body lacks literal `token-1` | Pending |
| PC-33 | `hides add and remove on an archived card` | Render write controls regardless of `archivedAt` | Add/Remove/textbox absence assertions fail | Pending |

Run each with that single file and exact anchored Vitest `-t` name through the host build slot.
The tests exercise the real panel via CardModal, with MSW at the HTTP boundary. Bind payloads to
the landed source. Retain per-control red/restore/green evidence. Zero tests, fixture errors,
compilation failures and the ordinary red-first run do not discharge a control. Mutation owns
missing-control discovery, including the added refusal and inverse-list tests. Do not widen
timeouts or loosen assertions.

All other controls in the card's design (PC-1 through PC-31 and PC-34, with every listed variant)
also remain pending; they belong to S1-S4. This S5 candidate waives none. No deliberate mutation
was executed by Code. Restart after landing: server (to serve the rebuilt client); runner: none.
