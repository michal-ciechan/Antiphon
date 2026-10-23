# CARD-1531 repair verification admission

Continuation Code task: `8c3c378f`; original Code and landing owner: `6506e5e2`.
Branch: `feat/card-task-8c3c378f`; worktree: `/work/worktrees/task-8c3c378f`.
Start/base: `dd2ebff8a085082f71bd5207b0d78a0e46fff29c`.
Plan: `docs/superpowers/plans/2026-10-23-card-1531-checkpoint-admission-budget-plan.md`.
Final evidence will be recorded in `.antiphon/task-8c3c378f.md`; this committed
pre-run record claims no green result for the repaired source.

R-4 now records `tool-run: baseline git fetch <ref> skipped: baseline budget exhausted`
before an exhausted baseline returns. It starts no driver and creates no fresh
cleanup allowance. The existing assertion remains and additionally checks the
named diagnostic and an empty driver roster.

V-12 now exercises `ReceiptJson`, the shared production parser, and the actual
CLI validate route. Only newly added reference properties carry explicit optional
metadata: Attempts, row/build broker/terminal reasons, and row/build progress phase.
Omitted Attempts defaults to an empty typed list; explicit null/wrong type still
refuses. Omitted nullable fields remain unknown. Old required properties remain
required. Unknown JSON fields continue to be ignored, as before, and grant no
validation authority. Tests cover omitted/null/empty/value inputs, legacy schema
2/3 failed receipts, historical failed/refused attempts, and required-field refusal.
`executed=` remains the test count. No source, dirty, provenance or cleanup
validation condition was loosened. No production PowerShell script changed.

Red witness commit: `56384a240017084c392f3eb872b4c753bb01c4ac` (pushed before fix).
Run: `.antiphon/checkpoints/20261023-090315-6265/`.
CP-4: 4 executed, 3 passed, 1 failed, 0 skipped; the new production parser test
fails because legacy ReportBuild lacks brokerReason and terminalReason.
CP-10: 2 executed, 1 passed, 1 failed, 0 skipped; the baseline exhausted test
fails its unchanged missing git-fetch diagnostic assertion. Fresh TRX identities
were inspected for all six tests. Red cleanup refusal is preserved, not rewritten.
One shared test build, 174.05s compiler wall; measured build/settlement 210.953s.
All build and row slots granted, waited=0s. Tool bootstrap was a declared separate
slot-gated tooling build, 5.43s compile / 6s held, slot=granted waited=0s.
One mistaken expected-SHA invocation refused before a driver; it supplies no evidence.
Task-owned red outputs were removed after all work joined; original receipts stay intact.

The commissioned Final profile adds CP-11 whole Unit and CP-12 full
CheckpointSlotExecutorTests to the final closed list, CP-3..CP-12, with one shared
build. Unit covers the shared parser's copy/ownership users; the integration class
covers broker admission, distinct leases, releases, and persisted executor receipts.
No full assembly is required and no unbounded class is claimed. Estimate: original
focused rows plus 15 minutes Unit / 2 minutes integration, one build, admission.
Use total timeout 60m and existing derived local deadlines. No timeout widening,
assertion weakening, automatic retry, or deliberate mutation is authorized.

Placement was read from GET /api/runner-defaults (revision 2) and
GET /api/session-runners: default Linux runner eligible; Windows runner eligible.
No host/platform pin is embedded. All card-specific rows are portable; Windows
estimate selection is a portable test. No native Windows acceptance was added.
Do not access supervisor 209072 or live qualification. Restart: none (tool invocations
build/shadow-copy source; a running copy keeps its own version).

Every PC remains pending method-scoped SourceLanding Mutation, including all
PC-1a/b, PC-2, PC-3, PC-4a/b/c, PC-5, PC-6a/b and row/build variants,
PC-7a/b, PC-8a/b, PC-9a/b/c and input variants, PC-10a/b, PC-11a/b and
failed/refused variants, and added PC-12a/b optional/required field variants.
Candidate: `docs/superpowers/mutations/2026-10-23-card-1531-candidate.md`.
Mutation owns missing-control discovery and deliberate red/restore/green.

Full Review follows only when implementation and ordinary V-1..V-12/R-1..R-4,
Final selections, manual validators and clean receipt validation are complete.
The final source will be the exact pushed SHA supplied to the final run. Store
actual SHA, unedited CHECKPOINT lines, TRX roster, receipt validation, full
base..HEAD evidence guard, merge-tree, and RC ordering in the ignored final report.

## Original red receipt records

```text
commit: 56384a240017084c392f3eb872b4c753bb01c4ac  branch: feat/card-task-8c3c378f  worktree: /work/worktrees/task-8c3c378f  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 56384a240017084c392f3eb872b4c753bb01c4ac state=clean buildSource=verified
CHECKPOINT CP-4 commit=56384a240017084c392f3eb872b4c753bb01c4ac build=ok filter=/*/*/CheckpointAdmissionContractTests/C1531_* executed=4 passed=3 failed=1 skipped=0 trx=/work/worktrees/task-8c3c378f/.antiphon/checkpoints/20261023-090315-6265/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=56384a240017084c392f3eb872b4c753bb01c4ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=5.588s wall=7.143s
CHECKPOINT CP-10 commit=56384a240017084c392f3eb872b4c753bb01c4ac build=reused filter=/*/*/(CheckpointTaskOwnershipTests*)|(BaselineComparerTests*)/(windows_estimate_selects_row_and_total_deadlines*)|(baseline_stops_when_the_timeout_is_already_spent*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-8c3c378f/.antiphon/checkpoints/20261023-090315-6265/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=56384a240017084c392f3eb872b4c753bb01c4ac sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=0s executed=6.442s wall=8.424s
verdict: RED exit=1
```
