# CARD-0478 application custody and workflow checkpoint

Date: 2026-09-11. Code task `cd1d39f7`. Base `08d2d111`.
Branch `feat/card-task-cd1d39f7`.
Worktree `C:\Antiphon\worktrees\card-task-cd1d39f7`.

**Incomplete Code: do not land or deploy this checkpoint.** Application admission,
custody import/cleanup and the active workflow reorder are implemented, with focused
ordinary evidence. S1-S5 and every required ordinary V/R are not complete. No external
dependency or operator decision was found that prevents further Code. The full
application delivery/crash matrix, native fault variants and guard/method census still
need implementation and execution before ordinary Review can approve the full feature.

All **230 planned PCs remain pending**. No deliberate source mutant or PC cycle was
executed. JSON receipt corruption and dirty fixture files below are ordinary tests.
Additional independently bypassable safeguards (notably the expected RunnerStoreId)
must be reconciled with the inventory before a final complete-Code handoff; this record
does not claim that the existing 230-row method/variant map is executable as written.

Authorities: [implementation plan](../superpowers/plans/2026-09-10-card-0478-mutation-after-land-plan.md)
and [custody amendment](../superpowers/plans/2026-09-10-card-0478-runner-custody-amendment.md).
The inherited [host/runner checkpoint](2026-09-11-card-0478-host-runner-custody-checkpoint.md)
contains 134 selected passing tests. Those tests were not rerun wholesale in this pass
and are not added to its fresh pass counts.

## Implementation

- Nullable immutable SourceLanding operation/SHA on AgentTask, restricted source FKs,
  append-only accepted execution identity, task execution revision/seal/removal facts.
  Migration `20260911073607_VerificationExecutionCustody` was generated with the EF CLI,
  including its Designer and model snapshot. It was exercised by disposable test schemas;
  the shared live database was not migrated.
- Source admission accepts fresh Worker/Mutation/Worktree only, checks structured
  HasPublication, same project/repository, a distinct same-board companion, canonical
  authorized roots, capability/backend/store identity and serialized same-O open-task
  exclusion including Blocked. Source metadata precedes provisioning.
- Exact-L creation uses the managed short task branch and existing external creation
  metadata under the real repository lease. Validation checks creation/common/git/CWD,
  HEAD, branch, tracked/index cleanliness and sequencers. Recorded partial/mismatched
  snapshots refuse rather than borrowing legacy healing or resetting source.
- Dispatcher reserves the immutable execution binding B with microsecond generation and
  expected stable RunnerStoreId in its transaction before launch enqueue. Central session
  launch preparation consumes retained B, records monotonic runner-call intent and refuses
  sealed/reused/wrong-generation/wrong-owner snapshots, including subdirectory CWDs.
  Full dispatch/relaunch/recovery race acceptance is still pending.
- Reply/worktree/explicit-land/protocol layers refuse Mutation publication before Git.
  Sourced tasks cannot join the warm pool. Unresolved release retains explicit ownership
  and residue; no custody assertion is manufactured from terminal status.
- Cleanup seals the terminal task's complete attempt set, obtains native seals/receipts
  before the Git lease, validates and imports exact bytes/digest/host identity, and retains
  every accepted attempt. Imported immutable receipts do not require another network call.
  Empty history is accepted only for the source contract's fenced NeverReserved path.
- A fresh independent DB evidence reader checks source/creation/seal/all attempts/receipt
  digest, live standing/pool/task/session paths, external restoration/report hash and
  original managed metadata. GuardedVerificationRemoval requires the genuine lease and
  clear child journal, inspects real Git/filesystem inventory, and removes only exact
  hashed owned output files and empty ancestors, then uses non-force worktree removal
  and expected-SHA branch CAS. Authority is reloaded at output/directory/final-ref cuts.
  Partial removal facts and residue remain separate from test/publication verdicts.
- Public CLI/API/client fields: optional `-SourceLanding`/sourceLandingOperationId,
  sourceLandingSha and verificationCleanupResidue; explicit `-CleanupVerification` and
  POST `/api/agent-tasks/{id}/cleanup-verification`. Full typed per-execution custody
  detail required by D-14 is still missing; CustodyReason is not yet populated uniformly.
- Active Code/Review/Mutation/TestDesign/basics/orchestrator bundles, delegate skill and
  orchestration/card/testing/ops owners now describe ordinary Review before publication,
  durable same-board companion tracking, exact-L post-land Mutation, external evidence,
  finding triage and independent guarded cleanup. Source snapshots never commit/push
  amendments or use external executors. Role ordinals/parser vocabulary are unchanged.

Meaningful commits: `045146dc` application foundation; `fc2a5834` fixture checkout policy
and receipt identity tests; `32998e92` canonical/subdirectory ownership and native orphan
acceptance; `848e3f16` workflow contracts and final cleanup authority.
The requested source branch was occupied in another worktree, so this clean task branch
was based on its fetched `08d2d111` without modifying the other checkout.

## Fresh ordinary evidence

Windows 10.0.19045 x64, .NET 9.0.16. Real local Git/bare remote and disposable PostgreSQL
schemas. Native cases use modern ConPTY/PtyHost and a compiled named-event descendant
fixture; no paid provider, external messaging broker or production runner 17204.

- At `fc2a5834`: focused custody/receipt policy 49/49 passed. Earlier fixture setup failed
  because production Git inherited global autocrlf while FixtureGit isolated it; explicitly
  pinning the owned fixture repository's core.autocrlf=false made both observers agree.
  Production source-cleanliness guards were not loosened. The failed run was stopped by
  its exact owned executable/PID before editing; its incomplete output is not pass evidence.
- At `32998e92`: focused custody 54/54, zero failures/skips, 10m05s.
  `.antiphon/c478-cd1d-custody-3/custody.trx`: application custody 29, receipt policy 25.
  Real V-17 variants Succeeded/ordinary descendants, Failed/detached and Canceled/new-console
  descendants refuse cleanup while the grandchild survives root exit, then import the
  original receipt and remove the exact snapshot after natural exit/service recreation.
  External evidence and terminal task disposition survive. These are native custody and
  importer/removal tests, not transcript-confirmed worker/caller delivery tests.
- At `848e3f16`: Unit 2,081 passed, zero failures, one skipped, 1m04s (2,082 total).
  Skip: existing key-file symlink test lacks Windows file-symlink privilege. Junction
  authorization coverage did execute in the native/application suite.
  `.antiphon/c478-cd1d-unit-4/unit.trx` includes InstructionBundleTests 53,
  PipelineHandoffParseTests 42, MutationRoleContractTests 2, new composed/owner contract
  tests 3 and receipt-policy tests 25. Counts here overlap the overall Unit total.
- Client `npm ci --no-audit --no-fund` and `npm run build` passed TypeScript/Vite;
  only the chunk-size warning was reported. Source detail/request parity is compile evidence,
  not deployed acceptance. Server build passed with zero errors and two warnings in unchanged
  AgentService/CapacityRecoveryService. Test builds also report existing test warnings.

The duration tripwire reports 29 new application custody cases at 18-26 seconds each,
dominated by real repository initialization/landing and per-world schema setup. This cost
is disclosed; no allowlist, timeouts or assertions were widened. Unit receipt validators
remain fast. Future matrix expansion should budget these real fixture costs explicitly.

## Remaining acceptance, without relabeling partial evidence

| V/R group | Current evidence and remaining work |
|---|---|
| V-1 / R-1 | Migration runs on disposable schemas and source service admission passes; historical pre-change upgrade, actual HTTP/PowerShell payload parity, complete source/FK/authorization/capacity matrix still owed. |
| V-2 / R-2 | Exact confirmed source snapshot and later-target advance covered; pairwise-distinct rebased C/L/R after authorized original-source removal, residue/AlreadyPresent and every publication predicate arm still owed. |
| V-3 / R-3 | Managed metadata replay/validation and source identity covered partially; actual accepted-create/provisioning restart/missing-L matrix still owed. |
| V-4 / R-3 | Existing unsourced dispatch/capacity compatibility is separate evidence; actual sourced provider compositions, immutable queue reservation and concurrent next-card Code scenarios still owed. |
| V-5 / R-4 | Lower worktree/autosave/land fences and dirty fixtures covered; real sourced OnTurnEnd/repeat/catch-up/no-progress/failure/cancel/release matrix still owed. |
| V-6 / R-5,R-6 | Positive native cleanup, exact outputs, unknown/ignored/empty/dirty/escape/evidence refusals and a final-ref authority cut covered; full first-forbidden-command matrix, all interrupted-removal cuts and alias/race cases still owed. |
| V-7,V-8,V-10 / R-7 | Contracts now specify independent companion/triage/resumption. New explicit card/task workflow-driver scenarios are not implemented; existing card transitions do not substitute for them. |
| V-9 / R-8,R-9 | Full land->caller, sourced dispatch->worker and settlement->caller producer/queue/crash matrix is not implemented. Require matching complete UserPrompt in correct destination after baseline, busy/idle arms, durable full Result and repeat-sweep no-retype proof. No native session delivery claim here. |
| V-11 / R-10 | Composed/active contract checks and legacy enum/parser compatibility passed; new actual Code pending/zero-PC -> Review and Review -> original Code land settlement/companion flow still owed. |
| V-12 / R-11 | Client compilation passed. Loaded selector, bundle hashes and actual server/runner/host capability probes remain caller-owned after complete Code+Review, land and canonical deployment. |
| V-13 / R-12 | Real service transaction reserves B before runner and retained B/late-seal refusal covered; full dispatcher/queue producer path, retry generations, seal/reservation races and all recovery paths still owed. |
| V-14,V-15,V-16 / R-13,R-14 | Inherited selected native/host/runner evidence remains partial. Complete first-instruction fault matrix, actual breakaway-denied plain-detached intermediary, crash/timeout/corruption/backend/no-start variants from the amendment. |
| V-17 / R-15 | Required real orphan-to-import/removal terminal variants pass with service recreation. Complete all importer persistence/crash cuts and independent consumer/frozen-history guard variants before claiming the entire R-15 group. |

Additional implementation audit before final Review: finish typed execution/custody reason
detail; ensure cancellation/unavailable import paths preserve actionable residue; audit
all source retry/relaunch ownership paths; examine canonical alias equivalence and every
first deletion boundary; reconcile all new guard/method names against the 230-row PC map,
expanding it for independently bypassable added safeguards. A reserved B that never reached
a host currently seals conservatively as Unknown rather than receiving manufactured no-start
proof; distinguish this retained residue from the proven NeverReserved empty-history path.

In particular, the `killSession:false` sourced release branch preserves the terminal task's
ephemeral agent and unresolved custody instead of pooling or automatically killing it. Its
eventual owner/recovery path has not been accepted against the repository release invariant;
do not treat this retained uncertainty as completed release. Finish that policy and its real
recovery tests, returning to Plan if an additional ownership mechanism is required.

## Bootstrap boundary

Continue Code from this branch; finish all application/native ordinary V/R and the executable
guard census. Do not commission any PC in this checkpoint. Once complete, return next: review
with the full pending inventory and the actual original Code landing owner.

After ordinary Review, the caller records the companion obligation, lands the original Code
task and deploys the complete server/runner/host/contracts through the canonical main checkout.
Directly verify loaded SourceLanding selection/composition and native tracking capability;
health alone is insufficient. Only then commission all pending PCs in a fresh SourceLanding
Mutation Worktree at confirmed O.VerifiedSourceSha. Never land/deploy a partial/prose-only
subset, use a retained Shared fallback or relabel C evidence as rebased L.

No live card, dispatch, stack, port, landing or deployment change was performed in this pass.

## Final integration result and reproducible commands

Implementation commit `848e3f16057c4cdce27f4c54d331429a754e1b22`:
**284 integration cases: 282 passed, 2 failed, 0 skipped; 16m09s.**
Both failures reproduced separately at base `08d2d111`, with the identical assertions:

- `AgentTaskReplyIntegrationTests.a_parked_recovery_fails_the_task_naming_exhaustion`:
  expected ResolvedReason `WallParked`, actual null.
- `AgentTaskReplyIntegrationTests.a_merge_conflict_blocks_the_task_and_spawns_a_merge_delegate`:
  expected task Blocked, actual Succeeded.

Base reports: `.antiphon/c478-cd1d-base-6/parked.trx` and
`.antiphon/c478-cd1d-base-7/conflict.trx`, each one executed/failed case.
An attempted combined method filter produced zero tests in base-5; that is explicitly
excluded from evidence. Source was clean before the temporary detached base checkout,
and the task branch was restored afterwards. The base build used its own output directory.
No inherited assertion was changed or weakened.

Fresh final report: `.antiphon/c478-cd1d-integration-4/integration.trx`.

| Executed integration class | Cases | Failed |
|---|---:|---:|
| AgentTaskCardBindingTests | 19 | 0 |
| AgentTaskLandRequestTests | 15 | 0 |
| AgentTaskReplyIntegrationTests | 121 | 2 (base reproduced) |
| CardWorkTransitionServiceTests | 12 | 0 |
| DataRetentionServiceTests | 22 | 0 |
| DelegationWorktreeTests | 30 | 0 |
| MutationAdmissionTests | 22 | 0 |
| MutationDispatchTests | 5 | 0 |
| MutationPipelineTests | 8 | 0 |
| PostLandMutationCustodyTests | 30 | 0 |

The 30 final custody cases include all 29 earlier cases plus
`C478_FinalBranchBoundaryReloadsCommittedAuthority`: a committed seal/revision change
at final symbolic-ref inspection permits no branch delete, retaining that exact ref and
recording partial directory-removal facts. Together with 25 receipt-policy cases in the
final Unit run, all **55 new focused custody/policy cases passed at 848e3f16**.
The 3 new composed/owner instruction cases also passed. These counts overlap the broader
Unit/integration totals; they are not extra suite executions.

Rerun from this task worktree, using fresh result directories and a producer-owned output:

```powershell
$env:MSBUILDDISABLENODEREUSE='1'
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c478-rerun/ --property:UseSharedCompilation=false
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c478-rerun/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c478-rerun-unit
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c478-rerun/ -- --treenode-filter '/*/*/(PostLandMutationCustodyTests*)|(MutationAdmissionTests*)|(MutationDispatchTests*)|(MutationPipelineTests*)|(DelegationWorktreeTests*)|(DataRetentionServiceTests*)|(AgentTaskCardBindingTests*)|(CardWorkTransitionServiceTests*)|(AgentTaskLandRequestTests*)|(AgentTaskReplyIntegrationTests*)/*' --report-trx --report-trx-filename integration.trx --results-directory .antiphon/c478-rerun-integration
```

For the base comparison, use a clean checkout at 08d2d111 with its own build output and
run the two exact methods separately, not the invalid combined method expression:

```text
/*/*/AgentTaskReplyIntegrationTests/a_parked_recovery_fails_the_task_naming_exhaustion
/*/*/AgentTaskReplyIntegrationTests/a_merge_conflict_blocks_the_task_and_spawns_a_merge_delegate
```

This is a tested, pushed checkpoint, **not complete ordinary acceptance or permission to land**.

Final housekeeping: all owned commands finished. Process inspection found zero executables
running from this task checkout and zero `c478-app-*` custody helpers. The cleanup script
verified all resolved targets stayed under this checkout and contained no reparse points,
then removed 29 exact task-owned final/base output directories. TRX/HTML evidence remains
under the paths above; 16 completed command/duration logs are retained under
`.antiphon/c478-cd1d/logs/`. The final duration scan flagged 40 unlisted integration rows
(30 new custody rows, 10 existing) and 2 existing Unit rows; new contract/receipt-policy
Unit cases had no slow rows. No duration allowlist was changed. `git diff --check` passed.
The final documentation commit changes this record only; application/test/bundle source
remains the ordinary-tested `848e3f16` state. Rebuild isolated outputs before rerunning.
