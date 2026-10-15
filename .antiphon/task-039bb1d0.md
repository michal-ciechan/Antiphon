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
