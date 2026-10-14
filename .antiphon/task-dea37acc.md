# CARD-0506 S4 repair round 2 — Code dea37acc

Outcome: Review 2d7196f7 findings F1–F5 repaired. The change is docs only. The 20 doc-guard classes pass 196/196.

Original Code task / landing owner: `2d3fef7b-c47b-415c-b6de-14115878e464`. This task: `dea37acc`.
Branch `feat/card-task-dea37acc`, worktree `/work/worktrees/task-dea37acc`.
Docs commit: `baa03f43b44167cd77d95d62f2f7bee1bae831c2`. The parent is `e83ec481f`; no rebase.

## Findings

| ID | Change |
|---|---|
| F1 | The "Save sends only the switched-on overrides" row is replaced by two rows. The first states the V09 exact body when no overrides are saved. The second states that S2F1 live/conflict mode-changed/added/removed carry r8's `mode` when present and omit it when absent. |
| F2 | The 422 row is narrowed to `roles.Code.maxParallel`, citing V13 field-error: the alert, `aria-invalid` and the accessible description. A new line says a 422 on the Reason field is not covered by a test. |
| F3 | The editor-state table is replaced with one row per test scenario: live-dirty, live-clean and inherited-change, each giving the exact revisions and values. A new line says other refetch cases, including occupancy-only refetches, are not covered by a test. |
| F4 | The no-write row now states its precondition: the store is already initialized by a global read. It lists the 19 bodies and what the fingerprint compares. A new line says a cold-store PUT with a rejected policy value is not covered by a test. |
| F5 | The `Human_reason_and_server_caller_are_audited` "publishes" row is removed. The discriminating witness is now `Revision_conflict_returns_current_revisions`. That test takes its baseline after initialization: three 409s plus two committed PUTs give a `MockEventBus` count of +2. Removing the post-PUT publish at `DispatchConcurrencySettingsService.cs:372` would make that count +0. The row states that event names are not checked and that the count is not a delivery witness. |

## Re-check of other statements: removed or narrowed

- **orchestration-loop intro:** removed "`scripts/dispatch-concurrency.ps1` changes it" as uncited prose. It is now a table row citing `DispatchConcurrencyScriptTests.Set_reads_then_sends_exact_snapshot`. Read-only mode now cites V08 rows-and-sources.
- **V05 row:** removed "revisions from the loaded DTO". The hook does not read the DTO; the test passes the revisions in. The row now states the exact five keys, the trimmed reason and the scope route.
- **V08 row:** "one row per DTO supportedRoles entry" is narrowed to the asserted counts: 15 rows, or 16 with FutureRole.
- **V10 row:** narrowed to the three tested fields and the exact Revert outcome.
- **V11 row:** added "keeps the draft and reason" for Cancel, which the test asserts. "Clear all overrides" is now backticked as the control label.
- **V14 row:** stated with the asserted revisions.
- **Conflict rows:** "Draft and reason kept" now applies to the project case only; the global test does not assert it. The refresh-fails row now states what the test asserts.
- **V07 live-updates sentence:** narrowed to the asserted keys (global and project scope and revisions, plus pipeline). Only the project scope's cached data is asserted unchanged; `['unrelated']` and `['runnerDefaults']` are asserted not invalidated.
- **Navigation rows:** restated as the asserted URL and request outcomes. "Keeps unrelated parameters" is narrowed to the `keep` parameter the tests use.
- **Limitations table:** "the invalidation is not asserted" is reworded; the rows are otherwise unchanged and remain labelled not covered by a test.
- **antiphon-api:** global and project round-trip rows are narrowed to the asserted revisions. The `/revisions` row adds the asserted 422 bounds and the cold-store GET writing no row. The conflict row adds the concurrent-PUT and `dispatch_concurrency_human` 409s.
- **ops-http UI pointer:** now cites `C506 V15 project-deeplink`.
- **project-context:** the event trigger now reads: committed PUT (event count in `Revision_conflict_returns_current_revisions`), and seed import also publishes (service code, no test).
- **Forbidden-word grep** over added lines: the only hit is the backticked `Clear all overrides` control label.

## Verification

- `git diff --stat 1ac4c54f6..HEAD`: docs plus the `.antiphon` reports only. `git diff --check`: clean. Nothing changed under `docs/cards/`, and there are no code or test edits.
- `check-evidence-diff.ps1 -BaseRef 1ac4c54f6 -HeadRef HEAD`: commits=3 entries=1 violations=0 at `baa03f43b`; with this report committed, commits=4 entries=2 violations=0.
- `validate-checkpoint-receipt.ps1 … -Rows CP-1`: CHECKPOINT SOURCE VALID source=baa03f43b… rows=1.
- `git merge-tree --write-tree origin/master HEAD`: exit 0 at master `88540babcef8413c662a34a07f2e19f7055ca5d9`, tree `416757e219a6517c9e5be55143f8eb37ea1fd0a4`.
- **Unlisted build:** the checkpoint-tool bootstrap, run through `build-slot.ps1` into `bin-code-dea37acc-tool/` (slot granted, waited 0s, succeeded). That output directory was then removed.
- **Checkpoint manifest:** Review 2d7196f7 guards.md copied to the scratchpad with the build name `bin-code-dea37acc`. Run `20261013-235906-16b0`, exit 0, the only run, with no repeats. The TRX has 20 distinct classes and 196 Passed.
- No whole-Unit lane was run. PCs are pending for SourceLanding Mutation; Mutation has not run.

```
CHECKPOINT CP-1 commit=baa03f43b44167cd77d95d62f2f7bee1bae831c2 build=ok filter=/*/*/(CheckpointRepeatDocumentationTests*)|(DockerStackDocumentationTests*)|(RepairSourceDocumentationTests*)|(PostLandMutationContractTests*)|(OrchestratorInstructionsGuidanceTests*)|(RunnerBranchContractDocumentationTests*)|(MechanicalPcContractTests*)|(StandingBootDocumentationTests*)|(PipelineDefinitionDocumentationTests*)|(CommitOnSettleDocumentationTests*)|(TaskPlatformGuidanceTests*)|(PostLandRetrospectiveContractTests*)|(InstructionBundleTests*)|(CheckpointManifestDocumentationTests*)|(BlockedTaskParkProjectionTests*)|(DelegationCapabilityContractTests*)|(SessionRunnerAbsenceEvidenceDocumentationTests*)|(PublishedRoutingDocumentationTests*)|(BootStallDocumentationTests*)|(StandingPipelinePolicyDocumentationTests*)/* executed=196 passed=196 failed=0 skipped=0 trx=/work/worktrees/task-dea37acc/.antiphon/checkpoints/20261013-235906-16b0/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=baa03f43b44167cd77d95d62f2f7bee1bae831c2 sourceState=clean buildSource=verified
```
