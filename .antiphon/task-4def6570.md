CARD-0667 S3a is implemented and dormant. CP-12 is green: 11 executed, 11 passed, 0 failed, 0 skipped. Ordinary work for the explicitly commissioned slice is complete; next stage is Review.

Original Code / landing owner: 4def6570-2eaa-493f-8075-3b16f56a2ee3.
Branch: feat/card-task-4def6570.
Worktree: /work/worktrees/task-4def6570.
Task base: 58447830349d79ffaaeddf14e8721f8dfe6eb347 (S2c, predecessor owner 2cfabeca-fd02-465b-af8b-b8619c3d1ffa).
Actual green tested source SHA: da3c25b97f1c2c1b8f57b2e37b6b05028453f982.
This stored report is the only subsequent change; the final task Result gives the pushed report-commit SHA. Adoption must wait for S2c to land. Do not rebase this task branch manually.
Plan: docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md, S3a and CP-12.
Report: /work/worktrees/task-4def6570/.antiphon/task-4def6570.md.
Restart: none. Future server activation/restart belongs to the caller/orchestrator; the live server applies the additive migration on its next restart. No server/runner restart, deployment, provider launch, or production release occurred.

Implementation:

- Added RunnerSeatRelease and one CLI-generated migration, 20261005121558_AddRunnerSeatReleases, with the ledger, unique generation identity, revision fence, bounded diagnostic fields, and all six nullable AgentTask released-answer fields. The ledger deliberately has no task/session/agent foreign keys. No backfill or activation is included.
- Added TerminalRunnerSeatReleasePolicy and a real PostgreSQL coordinator. It reads committed attempts, registers debt idempotently, uses the existing recipient queue gate, checks both task-ownership keys, standing/AlwaysOn/board/specialist/warm/SourceLanding custody, settlement age, pending/uncertain delivery, committed Working state, and current runner identity/capability. A qualified observation reserves exactly one durable action using a conditional database update and actual affected-row count.
- Added observation/release wrappers to ISessionRunnerClient and HTTP, phone-home, routing and runner-scoped implementations. Unsupported peers do not fall back to force or generation kill.
- Program binds typed TerminalRunnerSeatReleaseOptions with AutomaticEnabled=false and registers the services. There is no production coordinator caller. S3a stops at reservation: no release send, confirmed-outcome application, sweep hook, answer transaction, or activation was implemented. S3d owns the send/recovery boundary.
- Added RunnerSeatReleaseFixture and the eight named TerminalRunnerSeatReleaseTests methods (11 expanded results), using migrated isolated PostgreSQL, real queue/state/DI and serialized fake runner transports. Corrected CP-12 method wildcards for TUnit discovery.

Verification scope and outcomes:

The brief explicitly requires CP-12 only and forbids a whole-Unit run. That specific scope controls this Final slice; no full assembly, Unit lane, unrelated checkpoint, or native provider test was run. V-2/S3a: PASS (11 results). The rest of V-2 is deferred to its named later slices. V-1, V-3, R-1, R-2, R-3, R-4 and CP-1 through CP-6 final Linux/Windows qualification are NOT executed or credited by this task; they remain assigned to the plan's later final qualification. CP-13 through CP-18 remain later-slice work, not passes. Required manual S3a migration/source inspection is complete.

Fresh TRX inspection for every run confirmed exactly the same intended eight method identities, with Completed_attempt_registers_release_debt expanded to four results and each other method to one. Final outcomes:

| Method | Passed |
|---|---:|
| Completed_attempt_registers_release_debt (Succeeded, Failed, Canceled, Blocked) | 4 |
| Incomplete_settlements_never_authorize_release | 1 |
| Unsettled_blocked_or_queued_owner_is_preserved | 1 |
| Standing_warm_and_verification_owners_are_preserved | 1 |
| Settlement_age_has_its_own_safety_margin | 1 |
| Pending_delivery_prevents_release | 1 |
| Concurrent_reservations_have_one_winner | 1 |
| Unsupported_server_transport_never_falls_back_to_force | 1 |

Run history (two fixture repair rounds; no assertion/timeout weakening):

1. f8c51e5e989c4ec2588e4d2634c87f6693424729: 0 passed / 11 failed. New fixture omitted RunnerCwd and hit CK_AgentSessions_RunnerBinding_AllOrNone. This is a setup failure, not behavioral red or an inherited defect. Repaired complete binding and fixture failure teardown; sourced custody now seeds real referenced rows.
2. 34fa6efd3e7ba95873123a812ce1b3adce359d7c: 4 passed / 7 failed. All seven failures are named behavioral assertions at the unfinished coordinator boundary: Reserved vs Waiting, owner refusal, age refusal, pending refusal, exactly-one reservation vs zero, and Unsupported vs Waiting. The four ledger/migration cases were already green and retain their Mutation obligations.
3. 3728d40f0a128b0478e9a27b826f376eb2323821: 10 passed / 1 failed. Board-owner fixture omitted its required ProjectId (FK_Boards_Projects_ProjectId). Production reservation and every other case passed. Repaired the fixture with a real project/board; no production change.
4. da3c25b97f1c2c1b8f57b2e37b6b05028453f982: 11 passed / 0 failed / 0 skipped. No repeat after green. Every rerun followed committed changed implementation/fixture source; none was a loaded or reassurance repetition of an unchanged proof.

Guard witnesses use the actual coordinator decision and persisted ActionId/reservation count, including an otherwise eligible companion case, so the dormant zero-command counter cannot by itself make a missing guard pass. The race uses independent scoped connections and the same pre-existing ledger row. No deliberate mutant was introduced: compiling incomplete S3a seams preceded implementation, as the plan requires. Physical command/exit witnesses belong to S3d and subsequent slices.

Unedited checkpoint receipts:

```text
CHECKPOINT CP-12 commit=f8c51e5e989c4ec2588e4d2634c87f6693424729 build=ok filter=/*/*/TerminalRunnerSeatReleaseTests*/(Completed_attempt_registers_release_debt*)|(Incomplete_settlements_never_authorize_release*)|(Unsettled_blocked_or_queued_owner_is_preserved*)|(Standing_warm_and_verification_owners_are_preserved*)|(Settlement_age_has_its_own_safety_margin*)|(Pending_delivery_prevents_release*)|(Concurrent_reservations_have_one_winner*)|(Unsupported_server_transport_never_falls_back_to_force*) executed=11 passed=0 failed=11 skipped=0 trx=/work/worktrees/task-4def6570/.antiphon/checkpoints/20261005-121703-5d3c/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=f8c51e5e989c4ec2588e4d2634c87f6693424729 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=34fa6efd3e7ba95873123a812ce1b3adce359d7c build=ok filter=/*/*/TerminalRunnerSeatReleaseTests*/(Completed_attempt_registers_release_debt*)|(Incomplete_settlements_never_authorize_release*)|(Unsettled_blocked_or_queued_owner_is_preserved*)|(Standing_warm_and_verification_owners_are_preserved*)|(Settlement_age_has_its_own_safety_margin*)|(Pending_delivery_prevents_release*)|(Concurrent_reservations_have_one_winner*)|(Unsupported_server_transport_never_falls_back_to_force*) executed=11 passed=4 failed=7 skipped=0 trx=/work/worktrees/task-4def6570/.antiphon/checkpoints/20261005-122224-dfc3/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=34fa6efd3e7ba95873123a812ce1b3adce359d7c sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=3728d40f0a128b0478e9a27b826f376eb2323821 build=ok filter=/*/*/TerminalRunnerSeatReleaseTests*/(Completed_attempt_registers_release_debt*)|(Incomplete_settlements_never_authorize_release*)|(Unsettled_blocked_or_queued_owner_is_preserved*)|(Standing_warm_and_verification_owners_are_preserved*)|(Settlement_age_has_its_own_safety_margin*)|(Pending_delivery_prevents_release*)|(Concurrent_reservations_have_one_winner*)|(Unsupported_server_transport_never_falls_back_to_force*) executed=11 passed=10 failed=1 skipped=0 trx=/work/worktrees/task-4def6570/.antiphon/checkpoints/20261005-122914-cab2/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=3728d40f0a128b0478e9a27b826f376eb2323821 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=da3c25b97f1c2c1b8f57b2e37b6b05028453f982 build=ok filter=/*/*/TerminalRunnerSeatReleaseTests*/(Completed_attempt_registers_release_debt*)|(Incomplete_settlements_never_authorize_release*)|(Unsettled_blocked_or_queued_owner_is_preserved*)|(Standing_warm_and_verification_owners_are_preserved*)|(Settlement_age_has_its_own_safety_margin*)|(Pending_delivery_prevents_release*)|(Concurrent_reservations_have_one_winner*)|(Unsupported_server_transport_never_falls_back_to_force*) executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-4def6570/.antiphon/checkpoints/20261005-123351-9a32/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=da3c25b97f1c2c1b8f57b2e37b6b05028453f982 sourceState=clean buildSource=verified
```

Each run built only bin-c667-s3a and executed only CP-12. The tool report lists 18 manifest build definitions; the other 17 were not executed. Every selected build and row had slot=granted and waited=0s. Each receipt states dirty=0, sourceState=clean, buildSource=verified. Raw report.json, run.trx, build log, console log and failure detail remain gitignored under /work/worktrees/task-4def6570/.antiphon/checkpoints/<run-id>/.

Green receipt qualification command and result:

```text
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261005-123351-9a32/report.json -ExpectedSourceSha da3c25b97f1c2c1b8f57b2e37b6b05028453f982 -Rows CP-12
CHECKPOINT SOURCE VALID source=da3c25b97f1c2c1b8f57b2e37b6b05028453f982 rows=1
```

Declared setup outside CP-12:

- Isolated server build for EF generation: scripts/build-slot.ps1 -Label c667-schema-build -- dotnet build server --property:OutputPath=bin-c667-ef/ --property:UseAppHost=false --nologo. Passed, 0 errors; slot=granted waited=105s, maxcpucount=6.
- dotnet tool restore restored the pinned dotnet-ef 9.0.20. An EF help read executed no build/test.
- Gated dotnet ef migrations add AddRunnerSeatReleases --project server --no-build: first setup attempt exited 129 because EF metadata appended net9.0 to the alternate output path; it generated no migration and ran no test. Retry with process-local OutputPath=bin-c667-ef/ and AppendTargetFrameworkToOutputPath=false succeeded. Both slot=granted waited=0s; no database update command was run.
- Gated checkpoint-tool bootstrap to bin-c667-tool/: passed, 0 errors, slot=granted waited=0s, maxcpucount=6. These are the plan's declared tool/schema setup builds, not additional proof selections.

Immediately before migration generation, origin/master was fetched at 22373b509c7837de3f83ee7fe8fedc1bdb7d687d; its AppDbContext and migration tree matched this branch, with latest migration 20261004231302_ExtendChannelOutboundRecovery. The CLI-generated migration changes only the new table/indexes and nullable answer columns. PostgreSQL tests apply the real migration, verify no pending migrations, round-trip 5,001 Unicode characters and retain ledger rows after deleting task/session rows. AutomaticEnabled remains false and no runtime hook was added.

All task-owned bin-c667-s3a outputs were removed by the green checkpoint. The six bin-c667-ef project outputs and bin-c667-tool bootstrap output were then removed; the source tree has no remaining bin-c667-* payloads. No daemon bin directory was touched.

Evidence history check over the full source range passed:

```text
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 58447830349d79ffaaeddf14e8721f8dfe6eb347 -HeadRef HEAD
EVIDENCE result commits=5 entries=0 violations=0 base=58447830349d79ffaaeddf14e8721f8dfe6eb347 head=da3c25b97f1c2c1b8f57b2e37b6b05028453f982
```

The same full-base..HEAD command is required again after committing this report; the final task Result records that result. Generated TRX/JSON/logs are not staged. Only this individually named Markdown report is committed as evidence.

Every S3a positive control remains PENDING for method-scoped post-land SourceLanding Mutation:

| PC | Pending variants |
|---|---|
| PC-1 | Failed, Canceled, completed Blocked terminal predicate (Succeeded companion) |
| PC-2 | Uncommitted settlement and null CompletedAt |
| PC-3 | Routing hold / absent completed report |
| PC-4 | Session-key owner: Queued, Dispatched, Working, unsettled Blocked |
| PC-5 | Agent-key owner: Queued, Dispatched, Working, unsettled Blocked |
| PC-8 | Ordinary standing non-pool owner |
| PC-9 | Otherwise eligible AlwaysOn pool owner |
| PC-10 | Otherwise eligible board-owned seat |
| PC-11 | Specialist agent role, specialist owner identity, task role |
| PC-12 | Deliberate Shared warm custody |
| PC-13 | SourceLanding custody |
| PC-30 | Settlement ages 0, 119.999, exactly 120, 120.001 seconds; independent +24h runner clock |
| PC-34 | Brief, answer, channel, mention, completion, continuation, recovery; each pending/attempted/held |
| PC-45 | Two independent contexts, one existing-row reservation winner |
| PC-80 | Same pending-input variants retain original body/status/attempt evidence |
| PC-86 | Old HTTP and phone-home peers; routing/scoped wrappers; zero force/generation fallback |

No PC is discharged by ordinary green. All other plan PCs are outside this slice and remain with their owning slices. Mutation owns deliberate defects, red/restore/green cycles, and missing-control discovery. Caller commissions Review now, then adopts/lands this original Code task after S2c lands and commissions SourceLanding Mutation; do not start S3d or activation from this report.
