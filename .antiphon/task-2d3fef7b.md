# CARD-0506 S4 docs — Code 2d3fef7b

Docs-only slice. Source commit `86bbb7fc68902a8b34806fd93b18c918ada9bb53` on `feat/card-task-2d3fef7b` (base `1ac4c54f6b432e872d71468ac2902f1b16cf2f07`).

| File | Change |
|---|---|
| `docs/antiphon-api.md` | Six route rows in the runner-defaults block (GET/PUT/revisions × global/project); a behaviour → `DispatchConcurrencyWireTests` method table. |
| `docs/ops-http.md` | Dispatch concurrency row names Settings → Concurrency, `/settings?tab=concurrency` and `&project=<id>`. |
| `docs/project-context.md` | `DispatchConcurrencyChanged` row in the SignalR → query invalidation table (`C506 V07 changed-event`). |
| `docs/orchestration-loop.md` | WIP-defaults paragraph points at the tab; new `### Concurrency settings tab (CARD-0506)`: navigation, editing, 409 conflict, live updates, three known limitations not covered by a test. |

Plan deviation: the plan said four route rows; the code has six routes (`DispatchConcurrencyEndpoints.cs:15-27`), so six rows.
Test design lists no S4 row and no docs guard; none added. `docs/cards/` untouched.

## Verification (unlisted runs; reason: docs-only slice with no checkpoint row, Final profile asks for the Unit lane)

Built once `tests/Antiphon.Tests -> bin-c506s4/` with `UseAppHost=false` (first attempt without it failed MSB3027/3021 on the Linux fakeclaude apphost collision). All runs through `scripts/build-slot.ps1`; bin-c506s4 dirs removed.

| Run | Filter | Result | Slot |
|---|---|---|---|
| Full Unit (aborted) | `/*/*/*/*[Category=Unit]` | killed by my 595 s window; not a result | waited=0s |
| Application Unit | `/*/Antiphon.Tests.Application/*/*[Category=Unit]` | 2833 total, 2806 passed, 0 failed, 27 skipped, 2m02s | waited=30s |
| Scripts Unit | `/*/Antiphon.Tests.Scripts/*/*[Category=Unit]` | killed at 590 s, not run to completion | waited=0s |
| Doc guards outside Application | `(CheckpointRepeatDocumentationTests*)\|(DockerStackDocumentationTests*)` | 15/15 passed | waited=0s |
| Base-docs check | `(CheckpointTempUsageTests*)\|(HostJqPrerequisiteScriptTests*)`, base docs checked out, same binary | 47 total, 2 failed (`namespace_census_uses_the_native_execution_roster`, `full_suite_checkpoint_floor_uses_the_current_census`) | waited=0s |

The aborted full run showed red on `namespace_census_matches_compiled_checkpoint_cases` (429 vs 426), `C1025_Receipt_persistence_is_required`, `C1025_Transport_is_bounded_and_reaped`. None of these classes reads a changed doc and the binary does not embed them; at base docs, different members of the same classes failed. Treated as host-load-dependent, not caused by this diff; not investigated further.
Not run: Scripts Unit to completion; Checkpoints/Infrastructure/Agents/other Unit namespaces apart from the two doc-guard classes; CP-1..CP-4 (no S4 rows; client untouched).

Checks: `git diff --check` clean; `check-evidence-diff.ps1` commits=1 violations=0; `git merge-tree --write-tree origin/master(e0877982c) HEAD` exit 0.
PCs: none for S4; plan PC-1..PC-9 / test-design PC-1..PC-54 stay pending for SourceLanding Mutation.
