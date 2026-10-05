# CARD-0667 S3e discovery Code evidence

S3e is implemented and its commissioned ordinary verification is green. Automatic release remains dormant (`AutomaticEnabled=false`). No S4a or later implementation, scheduler hookup, deployment, live provider launch or production seat release was performed.

Code task: `c74bc66c-84ee-4bd5-b6b0-07d7953620c2`. Branch: `feat/card-task-c74bc66c`. Worktree: `/work/worktrees/task-c74bc66c`. Base: `653eee75da86be7c71e71081500642d33b81a427`. Tested implementation: `1b48c9bdb7cecf891ed6a64c92dbfc81f1cbcd56`. The final completion message records the subsequent documentation-only pushed tip.

This is the current Code/landing task; its authenticated task detail has no `repairSourceTaskId`. The plan also records the earlier blocked S3e task `11f476f4-91b6-46e3-b940-65eb04735422`; this dispatch did not modify or land that branch. Caller owns publication after Review and the subsequent SourceLanding Mutation commission.

Plan: `docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`, S3e / CP-16. Round: Final within the explicit S3e brief. Its specific “no whole-Unit run” and “CP-16 only” instructions govern over the generic profile boilerplate. The separately requested complete runner class and seat-release server classes were also run. This report does not claim the later S4 qualification or the entire card is complete.

## Implementation

- `server/Application/Services/TerminalRunnerSeatReleaseService.cs`: bounded candidate processing, per-runner batches, continuation after failed candidates/runners, local and phone-home discovery, captured-evidence-only requests, durable rowless tuple upsert, reservation and existing conditional release/reconciliation. Ownership, queue, fresh evidence and generation checks remain in the release path. Missing server rows with terminal task references can use captured proof without manufacturing a session row. Known local conversations remain with their existing owner.
- `server/Application/Dtos/RunnerSlotDtos.cs`: bounded discovery results and an explicit continuation cursor. The caller carries that cursor between passes; it is scan position, never release authority. Losing it restarts traversal while the database retains generation/action identity. The existing full inventory RPC is fetched once per visited runner per pass; this is not a new paged wire API or a partial-inventory absence certificate.
- `tests/Antiphon.Tests/Application/RunnerSeatOrphanSweepTests.cs` and `RunnerSeatReleaseFixture.cs`: the four exact CP-16 methods, real runtime/tailer/backend-input evidence over local HTTP and phone-home, failed runner/candidate, page-size 2/budget 3/eight-candidate traversal, the 119.999/120-second boundary, duplicate concurrent discovery and recreated server services over the same migrated database. The local fixture now reproduces the catalogue's `local` versus descriptor's `desktop` identities and refuses phone-home resolution for a local seat.

No migration was needed. The existing model and snapshot both retain the unique `(RunnerId, RunnerStoreId, SessionId, AcceptedStartedAt)` index and unique action ID. No force/generation-kill fallback, assertion weakening, timeout widening, branch rebase, reset, amend or force-push was used.

## Committed slices and verification

| Commit | Outcome |
|---|---|
| `30673a1920f028395801e18446f8513e02c7462d` | Test-first discovery surface and four real outcome witnesses. CP-16 compiled; 4 executed, 0 passed, 4 named assertion failures, 0 skipped. Missing discovery caused the budget, later release, rowless discovery and durable-row assertions to fail. |
| `03212dadf6629134f4bca52c2e592e255f86d15f` | Discovery implementation. CP-16: 4/4 passed. |
| `1b48c9bdb7cecf891ed6a64c92dbfc81f1cbcd56` | Local-directory boundary correction and stronger local fixture. CP-16: 4/4 passed; full classes: 82/82 passed. |

Every slice was pushed before its test run. Source stayed frozen during each run. There were two CP-16 reruns after the initial assertion-red baseline, each on changed committed source; one corrective round addressed the local directory boundary. No loaded or flake repetitions were run. The mandatory full classes account for the additional green execution of the four discovery methods on the final implementation.

| Verification ID | Actual outcome in this dispatch |
|---|---|
| V-1 | Full current `TerminalSeatReleaseTests`: 38 passed, 0 failed/skipped. Native provider/OS process-tree qualification is outside this fake-child scope; Windows remains future qualification. |
| V-2 | Full current `TerminalRunnerSeatReleaseTests`: 22 passed, 0 failed/skipped. Future S4a/S4b methods are not represented as complete. |
| V-3 | CP-16 exact four methods: 4 passed. Full current `RunnerSeatOrphanSweepTests`: 12 passed, 0 failed/skipped. Includes earlier S3d recovery and S3h evidence cases. |
| R-1 | Not selected by this S3e brief; classifier/capacity qualification stays with S4c / CP-1 and CP-5. |
| R-2 | Complete `RunnerSlotEndpointTests` (8) and `RunnerSlotRulesTests` (2): 10 passed, 0 failed/skipped. |
| R-3 | Not selected by this S3e brief; four reply compatibility cases remain S4c / CP-4. |
| R-4 | Not selected; Windows qualification remains S4c / CP-5 and CP-6. |
| Manual/static | Confirmed default false and no production discovery caller; checked existing model/snapshot identity indexes; inspected fresh TRX classes/methods and counts; validated green source receipts. No production activation acceptance was commissioned. |

Deferred to the card's final qualification: CP-1 through CP-6 and their remaining V/R obligations. S4a/CP-17 and S4b/CP-18 remain future implementation slices. Earlier preparatory checkpoint IDs were not rerun under their own IDs. No deferred row is marked passed here.

Final implementation results are 86 executions (4 CP-16 plus 82 full-class executions), covering 82 distinct argument-expanded results. The 38 runner results and server breakdown 22 + 12 + 8 + 2 were independently inspected in the fresh TRX. Counts reflect the actual landed classes, including their existing argument variants.

## Evidence, provenance and rerun commands

Evidence root: `/work/worktrees/task-c74bc66c/.antiphon/checkpoints/`.

- `20261005-181011-aaa3`: test-first CP-16 assertion red; clean source and verified build provenance, not a passing receipt.
- `20261005-181500-b225`: first implementation CP-16 green; receipt validator passed against `03212dadf6629134f4bca52c2e592e255f86d15f`.
- `20261005-182049-7004`: corrected CP-16 green; receipt validator passed against `1b48c9bdb7cecf891ed6a64c92dbfc81f1cbcd56`.
- `20261005-182358-484d`: FULL-RUNNER and FULL-SERVER green; receipt validator passed for both rows against that same final implementation SHA.

All selected builds and tests reported `slot=granted waited=0s`; clean green receipts have `dirty=0 sourceState=clean buildSource=verified`. The gated checkpoint-tool bootstrap also reported granted/0s, built successfully in about six seconds, and was the plan's declared setup build. The four checkpoint invocations took approximately 2m33s, 4m35s, 2m40s and 6m32s respectively. No full-assembly run was made and no unbounded affected class set was asserted.

The full-class run is outside the CP-16 manifest row, explicitly required by the brief. It used `.antiphon/c667-full-classes.yaml` through the same checkpoint tool, two serial rows with isolated outputs: `bin-c667-full-runner/` and `bin-c667-full-server/`. Its exact filters are preserved below. These protect runner release qualification and the server ownership/persistence/operator contracts after modifying their shared coordinator; the declared pre-run estimate was 13 minutes. There were no other build/test drivers. Two preflight CLI mistakes (a short expected SHA and malformed local YAML) were refused before any build or test; both were corrected without changing source.

To rerun CP-16, bootstrap the tool through `scripts/build-slot.ps1` if needed, then run:

```sh
dotnet tools/Antiphon.Checkpoints/bin-c667-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md --rows CP-16 --expected-source-sha <committed-full-SHA> --max-wait 50s
```

Continue `wait --run <id> --max-wait 50s` until the exit is not 75. The two full-class filters below can be run with `scripts/run-checkpoint.ps1` and their exact counts/expectations if the ignored local manifest is unavailable. All test drivers must retain the build-slot gate, alternate outputs, `TUNIT_MAX_PARALLEL_TESTS=1`, and the server row's `C804_ORPHAN_SWEEP_ROOT=c667-disabled`.

The full-range evidence guard passed before this report: `scripts/check-evidence-diff.ps1 -BaseRef 653eee75da86be7c71e71081500642d33b81a427 -HeadRef HEAD`, 3 commits, 0 entries, 0 violations. It is run again through the documentation commit before completion; the final result and pushed SHA are in the completion message. Generated TRX, receipts, logs and manifests remain ignored. This Markdown preserves only the essential unedited checkpoint lines.

## Mutation and activation

Pending SourceLanding Mutation: PC-52 (budget/fair continuation), PC-53 (candidate failure isolation), PC-55 (local and phone-home rowless variants), and PC-56 (repeat, concurrent and server-restart variants on both transports). All PC-1 through PC-104 remain pending with their owning slices; ordinary assertion-red/green evidence does not discharge any deliberate mutation control. Mutation owns the deliberate mutants, red/restore/green and missing-control discovery.

Test limits: native-format records follow actual fake-child input; they establish parser/protocol/runtime custody and persisted database behavior, not live-provider acceptance or OS process-tree termination. Server restart means recreated services/transports over the same database with the runner still alive. No production setting or runner was restarted.

Restart: `none`. Owner: caller/orchestrator for eventual server/runner activation after the remaining dormant slices, independent Review and publication. Next: Review this Code task and implementation. Caller retains Code landing ownership and commissions SourceLanding Mutation after confirmed publication.

## Unedited checkpoint lines

CHECKPOINT CP-16 commit=30673a1920f028395801e18446f8513e02c7462d build=ok filter=/*/*/RunnerSeatOrphanSweepTests*/(Sweep_budget_is_bounded_and_resumes_fairly*)|(One_runner_failure_does_not_hide_other_candidates*)|(Unknown_server_session_with_idle_runner_is_released*)|(Discovery_is_idempotent_across_restart*) executed=4 passed=0 failed=4 skipped=0 trx=/work/worktrees/task-c74bc66c/.antiphon/checkpoints/20261005-181011-aaa3/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=30673a1920f028395801e18446f8513e02c7462d sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=03212dadf6629134f4bca52c2e592e255f86d15f build=ok filter=/*/*/RunnerSeatOrphanSweepTests*/(Sweep_budget_is_bounded_and_resumes_fairly*)|(One_runner_failure_does_not_hide_other_candidates*)|(Unknown_server_session_with_idle_runner_is_released*)|(Discovery_is_idempotent_across_restart*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-c74bc66c/.antiphon/checkpoints/20261005-181500-b225/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=03212dadf6629134f4bca52c2e592e255f86d15f sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=1b48c9bdb7cecf891ed6a64c92dbfc81f1cbcd56 build=ok filter=/*/*/RunnerSeatOrphanSweepTests*/(Sweep_budget_is_bounded_and_resumes_fairly*)|(One_runner_failure_does_not_hide_other_candidates*)|(Unknown_server_session_with_idle_runner_is_released*)|(Discovery_is_idempotent_across_restart*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-c74bc66c/.antiphon/checkpoints/20261005-182049-7004/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=1b48c9bdb7cecf891ed6a64c92dbfc81f1cbcd56 sourceState=clean buildSource=verified
CHECKPOINT FULL-RUNNER commit=1b48c9bdb7cecf891ed6a64c92dbfc81f1cbcd56 build=ok filter=/*/*/TerminalSeatReleaseTests*/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-c74bc66c/.antiphon/checkpoints/20261005-182358-484d/rows/FULL-RUNNER/run.trx slot=granted waited=0s dirty=0 source=1b48c9bdb7cecf891ed6a64c92dbfc81f1cbcd56 sourceState=clean buildSource=verified
CHECKPOINT FULL-SERVER commit=1b48c9bdb7cecf891ed6a64c92dbfc81f1cbcd56 build=ok filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)|(RunnerSlotEndpointTests*)|(RunnerSlotRulesTests*)/* executed=44 passed=44 failed=0 skipped=0 trx=/work/worktrees/task-c74bc66c/.antiphon/checkpoints/20261005-182358-484d/rows/FULL-SERVER/run.trx slot=granted waited=0s dirty=0 source=1b48c9bdb7cecf891ed6a64c92dbfc81f1cbcd56 sourceState=clean buildSource=verified
