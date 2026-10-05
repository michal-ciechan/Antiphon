# CARD-0667 S1 scope blocker

Code task / landing owner: `0c638c88` (caller owns the full task identity).
Worktree: `/work/worktrees/task-0c638c88`.
Branch: `feat/card-task-0c638c88`.
Task base and inspected source: `011a67134e53d51fd639581879441b9398fce5b4`.
Plan: `docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`.

S1 is unimplemented. Its full real-service red roster cannot be authored within the
six-file S1 footprint and the brief's prohibition on implementing later slices.
This is a scope decision, not a test failure or a claim that the proposed safety
design is incorrect. No source implementation, test, migration, or plan change was
made. This report is the only tracked deliverable.

## Admission observations

`git fetch origin master` succeeded. Both fetched `origin/master` and the task base
were `011a67134e53d51fd639581879441b9398fce5b4`. The named gates are present in that
master's ancestry:

| Gate | Representative full commit SHA | Subject |
|---|---|---|
| CARD-0826 | `f4665c4ca58214f9e23bdf40ca85e2bf2193aa94` | implement immutable receipt snapshot and durable try-only maintenance admission |
| CARD-0788 | `4380891cca92111cd6271a0bf0f3662965a7440d` | limit skewed no-movement settlement to cleanup |
| CARD-0883 | `cc4836d26c0456255da5fb33f30e1b03ddc922fb` | checkpoint recovery proof and writer schema slice |

This establishes presence in Git, not completion of every obligation of those
cards. No CARD-0881 effective-settings code was touched.

Authenticated, read-only `GET /api/runner-defaults` and
`GET /api/session-runners` succeeded. Defaults revision 2 selected `server2`.
The catalogue advertised an available Linux runner and an available Windows
desktop; the temporary runner was unavailable and draining. The Windows checkout
is unreachable from this mirror. No inventory mutation, session input, release,
stop, provider launch, runner restart, deployment, or settings write was made.

## Exact missing boundaries

The slice table at plan lines 122–125 assigns only the three test classes, their
server fixture, a contracts file and a pure server policy to S1. Its authoritative
verification section requires the real runtime/tailers, real migrated PostgreSQL
and actual services, queue, job and attention endpoint. At lines 343–351 it also
requires additional runner and service seams not present on the admitted source.

| Required assertion / test | Source observation | Scope needed before a compiling, real-service test can reach that assertion |
|---|---|---|
| `Fresh_tail_reads_each_provider` returns Working from each real fresh native read; generation, input and release races exercise the same runtime | `ITranscriptTailer` exposes cached `Snapshot()` and the Claude compaction-specific operation, but no generic fresh release observation. `SessionRunnerRuntime` exposes unconditional `ReleaseSlotAsync` and generation kill, but no terminal observation/token/conditional-release entry point. `BindChildForTest` binds only the child; it cannot bind a tailer. | Neutral callable observation/release surfaces and tailer binding/barrier seams in the S2 runtime/tailer files. A new DTO alone does not expose a runtime boundary. |
| `Completed_attempt_registers_release_debt` inspects one persisted ledger identity; `Concurrent_reservations_have_one_winner` exercises separate connections and conditional row updates | There is no `RunnerSeatRelease` entity, production EF mapping/table/migration, or `TerminalRunnerSeatReleaseService`. `AppDbContext` cannot query the proposed ledger through its production model. | S3 entity/model/migration and an inert production coordinator entry point. A fixture-local collection/table/context model would substitute for the production persistence contract. A missing-table/model exception is expressly not the required named red assertion. |
| `Existing_job_discovers_debt_without_settlement_callback`, reservation-before-wire and restart/lost-reply cases | The real `RunnerSlotReconcileJob.ExecuteAsync` only invokes `RunnerSlotService.ReconcilePendingReleasesAsync`. No discovery coordinator or persisted terminal release reservation exists. | S3 callable coordinator/discovery seams and actual job/dispatcher hooks, kept disabled until the safety path is implemented. Pure policy tests cannot exercise these integration boundaries. |
| Released-Blocked answer transaction, target attempt, retained complete answer, receipt-specific stop bypass and restart | `AgentTask` has no proposed released-answer fields or release identity. `AgentTaskService.RequeueAsync` is private and unconditionally enters the existing stop path. | S3 schema/answer entry seams and receipt-bound requeue interface, without implementing new continuation behavior yet. Fixture-generated answer records or attempts would mask the production defect. |

The plan explicitly says: “If implementation cannot reach a named production
boundary in those files, return next: plan with that exact seam; do not replace it
with a helper-only test.” The brief explicitly says to implement S1 only and not
later slices. The general allowance for neutral compiling seams does not specify
moving the migrated ledger and answer persistence contract into S1. That ordering
needs an explicit correction before writing the entire S1 roster.

## Concrete decision for the caller

Amend the slice ordering to put inert production API seams and the migrated ledger
and nullable answer schema in S1. Use the existing S2/S3 paths already listed in
the plan; keep all automatic release/discovery/answer activation disabled until
the later safety implementation. Name these additions explicitly in S1, then
commission Code to author its complete frozen roster and run CP-1/2/3.

Alternatively, keep S1's six-file footprint and split test authoring/checkpoint
selection by the slice that introduces each real production boundary. That option
requires an explicit manifest/roster amendment; the current 97-result preparatory
selection cannot be reported complete. No lower counts, fixture-only substitute,
constant assertion, relaxed timeout or loosened assertion is proposed.

## Actual verification and pending obligations

No build or test driver was run. There is no TRX, checkpoint receipt or CHECKPOINT
line to report. Checkpoint slots: `slot=not-run`, `waited=0s`; no leases requested.
No repeat or repair round was consumed. No unlisted build/test occurred.

| ID | Actual outcome |
|---|---|
| V-1 | Not run; runtime/tailer conditional-release seams and 26 new results absent. |
| V-2 | Not run; production ledger/coordinator/answer seams and 29 new results absent. |
| V-3 | Not run; real discovery/reservation/recovery seams and 14 new results absent. |
| R-1 | Not run; CP-1 is a closed 44-result selection requiring the absent new class. |
| R-2 | Not run; CP-2 is a closed 39-result selection requiring the absent new class. |
| R-3 | Deferred to full S1–S4 verification, CP-4; outside this S1 red group. |
| R-4 | Deferred to separately commissioned Windows CP-5/6 at the final source SHA. |
| CP-1 | Not run; expected 44, actual 0. |
| CP-2 | Not run; expected 39, actual 0. |
| CP-3 | Not run; expected 14, actual 0. |
| CP-4 | Deferred to final; expected 4, actual 0. |
| CP-5 | Deferred to Windows final; expected 29, actual 0. |
| CP-6 | Deferred to Windows final; expected 4, actual 0. |

All `PC-1` through `PC-90`, including each provider variant of PC-21/22/23 and all
other internal scenario variants, remain pending for post-land SourceLanding
Mutation. No deliberate mutant was introduced. No manual acceptance was performed.
No whole Unit lane or full assembly run was attempted; the explicit S1 scope wins
over the generic Final profile for this dispatch.

After the scope correction, the plan's preparatory command is the checkpoint
tool's `run --plan docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md
--rows CP-1,CP-2,CP-3 --expected-source-sha <committed-HEAD> --max-wait 50s`.
Use the declared gated tool bootstrap only if needed and wait for completion.
The declared estimate is 26 minutes for the red selection plus up to 10 minutes
setup, excluding queue time; the brief budgets 30–60 minutes. No full assembly
selection is needed.

Restart performed: none; restart owner: caller. The eventual server implementation
requires a caller-owned AppHost activation after landing. This documentation-only
report requires no restart. The original Code task remains the landing owner.

The final task report records the report commit, pushed ref and full-range
`scripts/check-evidence-diff.ps1` result after publication. Ordinary implementation
and verification remain, so this task is blocked for the slice-ordering decision;
it is not ready for ordinary Review or landing.
