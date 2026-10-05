# CARD-0667 S4a Code evidence

S4a is implemented and ordinary verification is green. Automatic release remains dormant.

- Original Code task / landing owner: `c495f1d8-9c4d-49cf-a644-1ad59183c4d6`.
- Branch: `feat/card-task-c495f1d8`.
- Worktree: `/work/worktrees/task-c495f1d8` (assigned desktop mirror: `C:\Antiphon\worktrees\card-task-c495f1d8`, not accessed).
- Task base: `f41748999c285a6ea00a7b8bf486ec51ee92fa16`.
- Tested source: `4d54feef842fec6736b4d388139ff4e25d2582ef`. The subsequent evidence-only commit does not change production or test code; the final reply supplies its pushed SHA.
- Plan: `docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`, active S4a / CP-17 rows at the task base.
- Round: Final for the explicitly commissioned S4a scope. The brief explicitly excludes the whole Unit lane and later slices; this is not final qualification of the entire card.

## Implementation

`AttentionService.cs` and `AttentionService.Leaks.cs` project the durable ledger using the existing `SessionDisagreement` presentation and `runner-seat-release:<full release UUID>` condition key. No Agent or AgentSession join is required. Known task/card/board links remain; unknown seats receive no guessed board. Evidence contains runner/store/session/generation, task/attempt/status when known, bounded reason/outcome and confirmation. Only confirmed outcomes receive the “Runner seat released” headline. Successful notes expire after 24 hours, with the exact .NET tick boundary checked after the PostgreSQL candidate query; unresolved debt remains visible.

`TerminalRunnerSeatReleaseService.cs` publishes the existing attention-mapped `AgentChanged` event after committed release/hold processing. Publication failure leaves the same ledger identity recoverable. `ReconcileAttentionAsync` reconciles existing uncertain actions through the existing authoritative inventory path and republishes visible identities; it cannot discover, qualify or send a release. It also works with automatic release disabled. No publication-delivered stamp can advance ahead of the event, and no new alert sink, channel message or migration was introduced.

The three existing server test files now contain five S4a methods and an isolated HTTP attention fixture, log capture and an audit-write fault. The HTTP fixture maps the real endpoints behind a test credential gate; this is not a production authentication qualification. The existing AttentionApiTests additionally exercise production Program composition with its production-runner guard.

S4b still owns production scheduler/writer/dispatcher wiring. The S4a restart witnesses recreate DI and call the real recovery service boundary; they do not claim that the future Hangfire hookup has been implemented or tested. S4c and activation were not started. `AutomaticEnabled=false` remains unchanged. No force-release fallback or timeout/assertion relaxation was added.

## Verification outcomes

| Selection | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-17 (final) | 5 | 5 | 0 | 0 |
| Full TerminalRunnerSeatReleaseTests | 23 | 23 | 0 | 0 |
| Full RunnerSeatOrphanSweepTests | 16 | 16 | 0 | 0 |
| Full AttentionServiceTests | 164 | 164 | 0 | 0 |
| Full AttentionApiTests | 2 | 2 | 0 | 0 |
| Full runner TerminalSeatReleaseTests | 38 | 38 | 0 | 0 |

The final receipts total 248 executions, including the five CP-17 cases repeated in the required full-class run; 243 results belong to the full classes. The runner class contains three existing C519 results in addition to the plan's 35. Every fresh TRX was inspected for actual class/method names, nonzero counts and outcomes. All three final receipts validated with `validate-checkpoint-receipt.ps1`, exact tested SHA, `dirty=0`, `sourceState=clean`, `buildSource=verified`. Every build/test slot was granted with `waited=0s`; the first compile-failed CP row itself reports `slot=skipped`, while its build held a granted slot.

| Plan ID | Actual outcome in this commission |
|---|---|
| V-1 | PASS: required full Linux runner class, 38/38. No Windows claim. |
| V-2 | PASS for S4a: `Attention_contains_release_identity_and_reason`; full currently implemented lifecycle class 23/23. Later writer/delivery witnesses remain S4b/S4c work. |
| V-3 | PASS for S4a: the four CP-17 orphan/attention methods; full currently implemented orphan class 16/16. Actual scheduled discovery/job wiring remains S4b work. |
| R-1 | Not selected by the S4a brief; classifier/capacity qualification remains assigned to S4c CP-1/CP-5. |
| R-2 | Not selected by the S4a brief; endpoint/rules qualification remains assigned to S4c CP-2, after S4b integration. |
| R-3 | Not selected by the S4a brief; four local/Shared compatibility methods remain assigned to S4c CP-4. |
| R-4 | Not selected by the S4a brief; Windows qualification remains assigned to S4c CP-5/CP-6. |

CP-17's five methods all failed against unchanged production code at test-only commit `d1e8b9da5c63769c90c368019b4b8e0f6515844a`: missing release rows in four HTTP GET assertions and missing recovery invalidation in the fifth. These were real guarded defects, not compile/fixture failures or deliberate mutants. The earlier test-only commit `e7c3686913cbd14bd7e74fc16361733fe2f4ea15` failed to compile because three string assertion calls used an unsupported overload; that run executed no tests and is not red-proof evidence. Repair 1 fixed those overloads and the board fixture setup. The first implementation run at `69fafcdc808eabb1f44b0c8e0bec50d38d3c931f` passed 4/5; the fifth hit reflection metadata serialization while inspecting framework logs. Repair 2 renders every structured property value, message and exception for the unchanged canary-exclusion assertions. The final CP-17 run passed 5/5. No further repair or repeated proof run was performed after green.

Manual/source acceptance: checked default-false configuration, unchanged force fallback policy, no migration, no production runner/activation calls, and retained the existing fleet-global attention contract. The isolated tests cover actual GET, fresh-context visibility before invalidation, rollback before audit commit, restart with automatic release disabled, repeated identity projection, rowless Working and Unknown custody over both transports, payload exclusions, and the 24-hour +/- one tick boundary.

## Pending Mutation controls

All card-wide PC-1 through PC-104 remain pending for method-scoped SourceLanding Mutation. Ordinary green and baseline failures do not discharge them. S4a owns these seven rows and variants:

| PC | Pending guarded variant |
|---|---|
| PC-51 | Audit write failure after runner execution; restart converges stopped row and one release note, without another command. |
| PC-75 | Rowless projection does not require AgentSession/Agent joins; both literal seat IDs survive. |
| PC-76 | Two null-agent seats keep two distinct release condition keys across restart/repeated GET and recovery. |
| PC-77 | Deferred-only discovery returns Released=0 and no released headline for the held seat. |
| PC-78 | Raw transcript, native path and answer canaries are excluded from ledger JSON, release GET items and captured structured diagnostics. |
| PC-79 | Commit precedes publication; a failed event followed by restart republishes the same release identity. |
| PC-88 | At 24 hours plus one tick, unresolved debt remains while old successful attention expires. |

Deferred to later card qualification, never marked passed here: S4b/CP-18, S4c/CP-1 through CP-6, their remaining V-2/V-3 writer/job cases, R-1 through R-4, and live activation acceptance. No required S4a ordinary case remains unresolved.

## Execution and evidence

Unlisted runs were necessary and explicitly bounded: one checkpoint-tool bootstrap build through `scripts/build-slot.ps1`; one server `-NoBuild` checkpoint for the brief's full release classes plus affected attention/service endpoint classes; one isolated runner checkpoint for the explicitly requested full runner class. No full assembly or whole Unit run occurred. The runner build/test overlapped only the already-built server test host; they used separate outputs and independent granted slots, with no concurrent builds or source edits.

Rerun commands (resolve current committed HEAD for a new verification, rather than reusing these historical receipts):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c667-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c667-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md --rows CP-17 --expected-source-sha <HEAD> --keep-outputs --max-wait 50s
# Call wait with the returned run ID until completion; exit 75 is still running.
C804_ORPHAN_SWEEP_ROOT=c667-disabled TUNIT_MAX_PARALLEL_TESTS=1 pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name S4a-server-full -Project tests/Antiphon.Tests -OutputPath bin-c667-s4a/ -NoBuild -Filter '/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)|(AttentionServiceTests*)|(AttentionApiTests*)/*' -MinExecuted 172 -Expect TerminalRunnerSeatReleaseTests,RunnerSeatOrphanSweepTests,AttentionServiceTests,AttentionApiTests -ExpectedSourceSha <HEAD> -ResultsRoot .antiphon/c667-s4a-full
TUNIT_MAX_PARALLEL_TESTS=1 pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name S4a-runner-full -Project tests/Antiphon.SessionRunner.Tests -OutputPath bin-c667-s4a-runner/ -Filter '/*/*/TerminalSeatReleaseTests/*' -MinExecuted 35 -Expect TerminalSeatReleaseTests -ExpectedSourceSha <HEAD> -ResultsRoot .antiphon/c667-s4a-full
```

Final evidence roots, under `/work/worktrees/task-c495f1d8/`:

- `.antiphon/checkpoints/20261005-191312-8c95/` (CP-17 report.json/report.md, fresh rows/CP-17/run.trx).
- `.antiphon/c667-s4a-full/S4a-server-full-20261005-191731-e014/` (source.json, run.trx).
- `.antiphon/c667-s4a-full/S4a-runner-full-20261005-191942-14fb/` (source.json, run.trx).
- Earlier compile, baseline red and first implementation reports: `.antiphon/checkpoints/20261005-190053-d6e1/`, `20261005-190310-8888/`, `20261005-190841-eef7/` respectively.

Generated TRX/receipts/logs remain ignored. This Markdown report preserves essential unedited checkpoint lines below. The full-range evidence guard is run over `f41748999c285a6ea00a7b8bf486ec51ee92fa16..HEAD`; implementation range result was four commits, zero evidence entries, zero violations, and the final documentation commit is checked again before settlement. Task-owned alternate outputs are removed before settlement.

Read-only live routing reads succeeded for `/api/runner-defaults` (revision 2) and `/api/session-runners` (eligible Linux/Windows entries and one unavailable draining entry). No fleet host or platform pin was added.

Tooling finding CARD-1071 was filed and read back: a wait briefly returned exit 6, claiming executor death after its log already reported done exit=0. The durable state/report were complete and green; the next wait returned exit 0, with no test rerun, and receipt validation succeeded. The suspected completion/read race remains an inference for the tooling owner. The card command's post-write Windows-path formatting failure is already CARD-0793; the new card's successful creation was independently verified. Neither tool was changed.

Restart: server, owned by the caller/canonical deployment owner after landing when activation of this source is wanted. No server or runner was restarted here; no runner restart is required by these server-only changes. Automatic release must remain disabled through the separately commissioned later slices.

Next: ordinary Review of S4a. Preserve the original Code task as landing owner; PCs stay pending for SourceLanding Mutation.

## Unedited checkpoint lines

```text
CHECKPOINT CP-17 commit=e7c3686913cbd14bd7e74fc16361733fe2f4ea15 build=failed filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/(Attention_contains_release_identity_and_reason*)|(Failed_audit_commit_recovers_stopped_row_and_attention*)|(Attention_recovery_deduplicates_rowless_seats_without_leaking_payload*)|(Attention_recovers_a_missed_invalidation_after_commit*)|(Unknown_server_session_with_live_work_is_preserved*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=e7c3686913cbd14bd7e74fc16361733fe2f4ea15 sourceState=clean buildSource=unknown
CHECKPOINT CP-17 commit=d1e8b9da5c63769c90c368019b4b8e0f6515844a build=ok filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/(Attention_contains_release_identity_and_reason*)|(Failed_audit_commit_recovers_stopped_row_and_attention*)|(Attention_recovery_deduplicates_rowless_seats_without_leaking_payload*)|(Attention_recovers_a_missed_invalidation_after_commit*)|(Unknown_server_session_with_live_work_is_preserved*) executed=5 passed=0 failed=5 skipped=0 trx=/work/worktrees/task-c495f1d8/.antiphon/checkpoints/20261005-190310-8888/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=d1e8b9da5c63769c90c368019b4b8e0f6515844a sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=69fafcdc808eabb1f44b0c8e0bec50d38d3c931f build=ok filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/(Attention_contains_release_identity_and_reason*)|(Failed_audit_commit_recovers_stopped_row_and_attention*)|(Attention_recovery_deduplicates_rowless_seats_without_leaking_payload*)|(Attention_recovers_a_missed_invalidation_after_commit*)|(Unknown_server_session_with_live_work_is_preserved*) executed=5 passed=4 failed=1 skipped=0 trx=/work/worktrees/task-c495f1d8/.antiphon/checkpoints/20261005-190841-eef7/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=69fafcdc808eabb1f44b0c8e0bec50d38d3c931f sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=4d54feef842fec6736b4d388139ff4e25d2582ef build=ok filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)/(Attention_contains_release_identity_and_reason*)|(Failed_audit_commit_recovers_stopped_row_and_attention*)|(Attention_recovery_deduplicates_rowless_seats_without_leaking_payload*)|(Attention_recovers_a_missed_invalidation_after_commit*)|(Unknown_server_session_with_live_work_is_preserved*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-c495f1d8/.antiphon/checkpoints/20261005-191312-8c95/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=4d54feef842fec6736b4d388139ff4e25d2582ef sourceState=clean buildSource=verified
CHECKPOINT S4a-server-full commit=4d54feef842fec6736b4d388139ff4e25d2582ef build=reused filter=/*/*/(TerminalRunnerSeatReleaseTests*)|(RunnerSeatOrphanSweepTests*)|(AttentionServiceTests*)|(AttentionApiTests*)/* executed=205 passed=205 failed=0 skipped=0 trx=/work/worktrees/task-c495f1d8/.antiphon/c667-s4a-full/S4a-server-full-20261005-191731-e014/run.trx slot=granted waited=0s dirty=0 source=4d54feef842fec6736b4d388139ff4e25d2582ef sourceState=clean buildSource=verified
CHECKPOINT S4a-runner-full commit=4d54feef842fec6736b4d388139ff4e25d2582ef build=ok filter=/*/*/TerminalSeatReleaseTests/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-c495f1d8/.antiphon/c667-s4a-full/S4a-runner-full-20261005-191942-14fb/run.trx slot=granted waited=0s dirty=0 source=4d54feef842fec6736b4d388139ff4e25d2582ef sourceState=clean buildSource=verified
```
