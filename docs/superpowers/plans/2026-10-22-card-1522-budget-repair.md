# CARD-1522 exact statement-budget repair

2026-10-22. Code/landing owner `1e08d3fe-d289-4eac-a91a-89db51795e74`.
The commissioning brief explicitly limits this Final round to the existing
14-instance budget method. No Unit lane, whole class or assembly run is required.
This amendment supersedes only the seven stale totals in the historical
CARD-1149/1150 D-6 and V-19 acceptance; historical measurements remain intact.
Diagnosis: [Debug report](../../investigations/2026-10-22-card-1522-query-budget-debug.md).

## Acceptance amendment and implementation order

S1 records this rationale before assertions change and captures the red witness
on fetched master in a detached diagnostic worktree. S2 changes the five RC-common
arguments and their adjacent comments. S3 changes the two CARD-0266-dependent
arguments and comments. S2/S3 form one committed verification group; their separate
commits permit selecting the RC-common changes independently.

| Action | Historical exact | Amended exact | Required command / introducing card |
|---|---:|---:|---|
| held-dispatched-tick | 18 | 19 | CARD-0097 registers routing recovery in the shared harness: one empty routing-blocked sweep SELECT |
| working-live-tick | 18 | 19 | Same CARD-0097 sweep, with no per-task reads |
| first-dead-session-observation | 18 | 19 | Same CARD-0097 sweep; first-observation grace still adds no recovery SQL |
| initial-refused-dispatch | 42 | 44 | CARD-0097 sweep plus CARD-1285 required TokenHash UPDATE before the refusing launch sink |
| repeated-blocked-tick | 16 | 17 | CARD-0097 sweep; no repeated recovery transition |
| running-recovery-missing-brief | 60 | 61 | CARD-0266 canceled-typing composer-safety SELECT before sending the next brief |
| watchdog-first-recovery | 60 | 61 | Same CARD-0266 safety SELECT before the awaited repair send |

Introducing commits: CARD-0097 `49d7884d9adcf74f90050777c653f27a65915d39`,
CARD-1285 `b602eac16a150d2acfe05d472aff500524168195`, CARD-0266
`96ee5df43209cba8d991ac5e8d11c2d65f90b6d1`. These are intentional commands,
not evidence of N+1 work. Preserve `total.ShouldBe(expected)`, all semantic/no-kill
assertions, clocks, timeouts, and fixture setup. Product code is outside scope.
The inside-grace scan stays 4; existing-brief recovery stays 65. Other controls
stay 5 (unknown close), 19/21 (due hold without/with parent), 6 (delivered discovery),
12 (readiness hold), and 5 with delta 0 (healthy scans).

RC `0714a9a89e4bff65aae7e0b4b94098694aeb931c` contains CARD-0097 and CARD-1285,
but lacks CARD-0266. Only S2's five arguments/comments apply there; S3 requires
CARD-0266 first. Check application against this exact RC tree without changing it.

## Verification design

- V-19: run `DelegationDispatchRecoveryBoundaryTests.C1149_C1150_Statement_budgets`
  once on fetched master, retaining the seven expected count failures and their
  SQL rosters; then once on the committed finished repair. All 14 argument results
  must execute, and the finished repair must have zero failures/skips.
- R-1: the same method's seven unaffected arguments must remain green, including
  the 4-command grace scan, 65-command existing-brief recovery, and exact 19/21
  parent/no-parent hold counts. No extra method selection is needed.
- R-2 (manual): inspect the full diff for only seven argument/comment changes,
  verify master merge-tree and RC applicability, run the evidence-history guard,
  validate the final checkpoint receipt against final committed HEAD, and inspect
  fresh TRX method identities, counts and action outcomes.

PC-18/held-tick and PC-18/inside-grace remain pending for method-scoped
SourceLanding Mutation. The amended held-tick variant must detect 20 != 19;
the grace variant still detects 5 != 4. No deliberate mutants run in Code.
Mutation owns missing-control discovery, including any additional budget variants.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-card1522-budget/` | exact-statement-budgets | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149_C1150_Statement_budgets` | V-19, R-1 | 14 executed, 0 failed/skipped on repaired source | 14 | 5 |

### Cost

One isolated test build and this exact method per source: red master witness,
then finished S2/S3 candidate. Estimated 5 minutes each plus slot waits. A single
leased checkpoint-tool bootstrap is an explicitly explained unlisted build,
needed to execute the manifest; it runs no tests. No repeats after green.
Commit the narrative report before the final run; append final receipts only to
ignored/external evidence so the final pushed tip is the tested source.
