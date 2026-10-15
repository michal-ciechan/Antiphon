# CARD-1136 F5 Code evidence

Owner: 039bb1d0-287d-4398-83ce-57540438dbb1. Branch: feat/card-task-039bb1d0.
Worktree: /work/worktrees/task-039bb1d0.
Base: 8ffe3633357e6383b7e408bd078465acf9f5fd79.
Plan: docs/superpowers/plans/2026-10-15-card-1136-cli-evidence-plan.md.

Tests are committed before the documentation fix. The initial run is expected
to fail V-16 because the chained-operator warning is absent. V-15 pins existing
CLI behavior; its PC-11 defect proof stays pending for SourceLanding Mutation.
No production change, migration or restart is required by this slice.

Platform read: runner-defaults revision 2; global default server2. Session-runner
inventory observed Linux and Windows eligible hosts. No runner or OS pin used.

All build/test invocations will be recorded here or in the final stored report,
including failed invocations, tool bootstrap and any baseline attribution run.
Generated checkpoint evidence stays ignored. Parent PC-1 through PC-11, including
all variants, remain pending. CARD-1148 retains the Held-clearance work.

Initial checkpoint 20261015-162851-033b at fc5a188817d6e4694f60e63540a4cdeaf810bf64: CP-9 passed 3/3. CP-10 selected zero tests because the combined class filter omitted the required trailing wildcard. This is a selection defect, not the expected V-16 red. Fix only the filter, then rerun CP-10 before adding the missing doc sentence. Build 225.2674655s, total wall 4m10s. Bootstrap build succeeded, 2 existing nullable warnings, slot=granted waited=0s; lease 1904e4d5-46b3-44b0-bb3a-1ee5241528e4, held 5s. No other build/test drivers.

CHECKPOINT CP-9 commit=fc5a188817d6e4694f60e63540a4cdeaf810bf64 build=ok filter=/*/*/DelegateScriptLandStatusTests/C1136_StatusPrintsSyncDebtAndPendingLines executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-039bb1d0/.antiphon/checkpoints/20261015-162851-033b/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=fc5a188817d6e4694f60e63540a4cdeaf810bf64 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=fc5a188817d6e4694f60e63540a4cdeaf810bf64 build=reused filter=/*/*/(CheckpointManifestDocumentationTests)|(RunnerBranchContractDocumentationTests)/* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-039bb1d0/.antiphon/checkpoints/20261015-162851-033b/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=fc5a188817d6e4694f60e63540a4cdeaf810bf64 sourceState=clean buildSource=verified

All source captures were clean with verified build provenance; the zero-count row is not acceptance evidence.

Corrected red run 20261015-163425-c3d5 at 103a3a5fba0eba5435f81f10f847ef3dc2165b6a: CP-10 executed 16, passed 15, failed 1, skipped 0. Fresh TRX contains exactly 8 CheckpointManifestDocumentationTests and 8 RunnerBranchContractDocumentationTests. The sole failure is the_chained_operator_pitfall_is_documented: the required sentence is missing from Build slots. This is the guarded defect. Build 297.9140566s; wall 5m30s; build and test slots granted with zero wait.

CHECKPOINT CP-10 commit=103a3a5fba0eba5435f81f10f847ef3dc2165b6a build=ok filter=/*/*/(CheckpointManifestDocumentationTests*)|(RunnerBranchContractDocumentationTests*)/* executed=16 passed=15 failed=1 skipped=0 trx=/work/worktrees/task-039bb1d0/.antiphon/checkpoints/20261015-163425-c3d5/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=103a3a5fba0eba5435f81f10f847ef3dc2165b6a sourceState=clean buildSource=verified
FAILED Antiphon.Tests.Application.CheckpointManifestDocumentationTests.the_chained_operator_pitfall_is_documented (CP-10) ShouldAssertException: section     should contain "Never chain a second driver after the gated command with a shell operator: the lease ends when build-slot.ps1 returns, so `-- dotnet build ... && dotnet exec ...` runs the exec unleased and Review flags it."     but was actually "### Build slots (CARD-0589) Nothing else bounds how many `dotnet build` / `dotnet run --project test..."  Additional Info:     CARD-1136 V-16 -> rows/CP-10/failures.md

A tool launch immediately before that run was refused source_mismatch before any driver: the supplied SHA was mistyped. It produced no acceptance evidence. The corrected launch used the exact HEAD above.

The documentation fix now adds the required sentence. Final qualification will run CP-9, CP-10 and CP-EXTRA against the committed fix. If an affected existing class fails, the checkpoint tool is allowed --baseline 8ffe3633357e6383b7e408bd078465acf9f5fd79 for an isolated base build and only the failing methods, to establish attribution. Those diagnostic runs are additional to the three ordinary rows and must be disclosed. This evidence file is frozen before that qualification; the final stored task report carries its fresh receipts.

No whole Unit or assembly run, deliberate mutant, assertion relaxation or timeout increase. Parent V-15/V-16 and the CARD-1136 subset of R-10 are this slice; V-7 is an adjacent doc guard. R-12 is the required affected-file regression row. All other parent V/R remain outside this scoped task, not passed by implication. Parent PC-1..PC-11 and every variant stay pending for Mutation.
