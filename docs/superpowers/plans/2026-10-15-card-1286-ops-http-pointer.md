# CARD-1286: restore the routing owner pointer

Original Code task / landing owner: `e17a0f89-75cd-4ed3-8776-5a10d5129723`.
Task base: `8ffe3633357e6383b7e408bd078465acf9f5fd79`.

The operator explicitly commissions minimal Final verification: no whole Unit lane.
Preserve the routing cross-reference assertion and current CARD-1250 API guidance.
Restore the missing owner pointer only after clean historical execution attributes
the failure. No runtime or test behavior changes are intended. Restart: none.

## Attribution and implementation

S0: run CP-1 at fetched origin/master, then at the parent of the change removing
the pointer. These are separate clean detached historical checkouts, with their
own isolated builds. Record exact SHAs, fresh TRX identities and the removal diff.
The existing failing regression supplies red-first evidence; do not add a duplicate.

S1: restore the create-time routing pointer to the existing matrix-owner heading
in the Role policy row of `docs/ops-http.md`. Commit and push before verification.

## Verification design

- V-1: the exact cross-reference method executes both document arguments; the
  ops-http argument fails before repair and both pass after repair.
- V-2: manually resolve the linked anchor and bold heading and confirm the
  current CARD-1250 behavior/limitations remain in the Role policy row.
- R-1: run every full class reading ops-http, plus adjacent bundle/stamp consumers.
- R-2: run every full OrchestratorInstructions generator/refresh/endpoint class.
  This includes persistence and file/HTTP publication coverage.

No new tests or deliberate mutants. PC inventory: zero; missing-control discovery
remains pending for SourceLanding Mutation after ordinary Review and landing.
Do not infer a Mutation pass from ordinary green.

### Cost

Final ordinary floor: 7 minutes (one isolated build reused across three rows).
Historical attribution: two additional CP-1 builds/runs, about 4 minutes each.
One gated checkpoint-tool bootstrap build is needed to execute the manifest.
Authoring/inspection budget: the remaining portion of the operator's 30 minutes.
No namespace or full-assembly runs; all affected consumers are bounded below.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1286/` | routing-pointer | `/*/*/OrchestratorInstructionsGuidanceTests/Routing_cross_references_resolve_to_an_existing_owner_heading` | V-1 | all 2 argument cases, 0 failed after repair; historical red explicitly expected on current master | 2 | 4 |
| CP-2 | S1 | CP-1 | document-consumers | `/*/*/(OrchestratorInstructionsGuidanceTests*)\|(DockerStackDocumentationTests*)\|(RepairSourceDocumentationTests*)\|(RunnerBranchContractDocumentationTests*)\|(SessionRunnerAbsenceEvidenceDocumentationTests*)\|(DelegationCapabilityContractTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(BlockedTaskParkProjectionTests*)\|(PipelineDefinitionDocumentationTests*)\|(InstructionBundleTests*)\|(InstructionFileStampTests*)/*` | R-1 | all listed classes, 0 failed/skipped | 11 | 1 |
| CP-3 | S1 | CP-1 | instruction-generator | `/*/*/(OrchestratorInstructionsRendererTests*)\|(OrchestratorInstructionsRefreshTests*)\|(OrchestratorInstructionsEndpointTests*)\|(OrchestratorInstructionsDeltaTests*)\|(OrchestratorInstructionsPathTests*)\|(OrchestratorInstructionsRecipientTests*)\|(OrchestratorInstructionsMigrationShapeTests*)/*` | R-2 | all listed classes, 0 failed/skipped | 7 | 2 |
