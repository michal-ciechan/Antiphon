# CARD-1603: stop capacity grants cycling after their consumer disappears

Plan task `2cebeca1`. Source inspected at
`f8bddb6efda7b0e8f50f3b45744087e52188bc66` on 2026-10-29.
This first slice records the verified premise; the implementation and verification
design are being completed in the next plan slice. No product code is changed.

## Inputs and dependency

- CARD-1603 on the Antiphon board, read with `scripts/card.ps1 get`.
- CARD-1593 S3 plan, Risks 8 and 9:
  `docs/superpowers/plans/2026-10-28-card-1593-s3-atomic-consume-plan.md`
  on `origin/feat/card-task-f264f937`, card reference `733f413a`.
- Its supporting investigation:
  `docs/investigations/2026-10-28-card-1593-capacity-leaks.md`
  on `origin/feat/card-task-bcb5bc45`.
- The S1 lock contract in
  `docs/superpowers/plans/2026-10-27-card-1593-capacity-admission-lock-order-plan.md`
  on `origin/feat/card-task-d94003bc`.

Code is commissioned only after S3 round A lands on master. This base still uses
the pre-start `RedeemAsync` in the supervisor. A plan may be reviewed before that
dependency lands; it is not evidence that the dependency is already present.

## Ground truth

All paths below are relative to `server/Application/Services/` unless stated otherwise.

| Card assumption | Verified behavior at the inspected source |
|---|---|
| A canceled selected message leaves a reusable grant | `SessionMessageQueueService.cs:5567-5573` stamps `SelectedMessageId` before delivery. `:2204-2222` cancels an expired brief without ending its wait; `:2231-2243` also handles an expiry after the first Sent claim. |
| The orphan sweep catches that cancellation | `CapacityRecoveryService.cs:665-680` checks terminal/missing session and task owners only. It does not inspect `SelectedMessageId` or queued-message status. A live session with a canceled selected message passes through. |
| Expiry eventually exhausts the leak | `CapacityRecoveryPolicy.cs:92-96` treats the selected message as an execution receipt. `CapacityRecoveryService.cs:1347-1355` requests revalidation; `:1208-1222` preserves the admission count and schedules another grant after expiry. The 120-second default bounds a grant, not the number of grants. |
| A human start progresses a standing wait | `AgentControlService.cs:833-863` commits the accepted session pointer and resets human-start supervision state. The wait moves only in the capacity-specific branch at `:804-816`. Transcript progress looks up the wait by `SessionId` (`CapacityRecoveryService.cs:885-890`). |
| A running agent will eventually consume the old standing grant | `AgentSupervisorService.cs:175-207` handles the live session and returns before the capacity branch at `:213`. `OrphanedWaits` excludes `StandingStart` entirely. |
| Every standing wait includes the old session generation | False: `AgentSupervisorService.cs:393-404` creates model-hold waits with `AgentId` and `BlockedAt`, but no `SessionId` or `SessionStartedAt`. The design must handle that production shape, not just a fully stamped test row. |
| Canceling a capacity wait releases a process seat | False. These are provider-admission rows. Finishing one must not kill, park, or detach a session. |

## Decisions

- **D-1:** Preserve S1's provider-leaf lock order and S3's atomic start/consume
  contract. Do not solve this by charging dummy admissions or shrinking expiry:
  neither establishes that a consumer still exists.
- **D-2:** Prefer extending the existing bounded orphan reconciliation over adding
  a scheduler. Recheck durable evidence under the provider lock, finish the wait,
  and clear only its own grant. The final slice will specify the exact predicates
  and the human-start acceptance boundary.
- **D-3:** Preserve admission counts, action identity and historical receipts when
  ending an obsolete wait. A human start or canceled brief is not proof of a
  successful provider turn, so `Progressed` is the wrong outcome.
- **D-4:** Review this plan with a different company's model before Code. Include
  executable verification design in the completed plan.

## Slices

The remaining design will name exact production/test files, RED-first witnesses,
and a closed checkpoint roster. Plan-only slices commit and push this document;
they run no product build or tests.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards` and
`docs/test-performance-standards.md`. This Plan task runs no tests or builds.
The Code budget will use shared Postgres/template setup, isolated test databases,
fake time for grant expiry, and explicit build/slot/fixture/body/teardown timings.
The S3 plan reports 1.2-11.4-second individual capacity methods and 217-399-second
builds, but those ranges are not measured p95 for this change. New budgets must
remain provisional until the bounded Code sample supplies comparable timings.

## Placement

Read `GET /api/runner-defaults` and `GET /api/session-runners` on 2026-10-29.
Defaults revision 2 has no per-kind override; an eligible Linux lane is available.
Use current defaults rather than embedding a fleet location; omit `-Runner` and
`-Platform` for portable work. Checkpoints will name the Linux integration lane.
No desktop files or native Windows runs are needed for this plan.
