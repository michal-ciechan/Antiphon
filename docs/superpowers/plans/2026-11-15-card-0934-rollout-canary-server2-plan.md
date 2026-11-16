# CARD-0934 upgraded server2 Plan canary

## Decisions
D-1. Limit execution to a documentation canary: publish the requested note and this stage-required plan, with no builds, tests, merges, configuration changes or restarts. Reject expanding this into a rollout repair: only a bounded canary was commissioned.
D-2. Record the actual per-slot path, even though the brief requires a per-task path. Reject treating runner-only enablement as proven isolation: the observation contradicts that acceptance condition.
D-3. Finish this canary and hand the discrepancy to Investigate. Measure the effective server slot flag/roles at preparation, this task's WorkspaceSlotDecision event and preparation history, and its runner ledger binding before proposing a repair. Do not infer that roles changed from the path alone.

## Ground truth
| Brief assumption | Observed documentation/code/runtime |
|---|---|
| Upgraded runner is server2 at a71c45ab4d5703d480f4eee4c4e30adab45bc4f9 | Runner/build are caller-supplied rollout context; catalogue confirms server2 is available on Linux. Runner build was not independently probed. |
| Roles remain `none`, so this task MUST use a per-task directory | Actual cwd is `/work/worktrees/ws-01`, a per-slot path; effective roles and the historical decision are unverified. |
| Runner support alone does not select slots | `docs/docker-stack.md`, CARD-0824 S1, requires both server gates; `WorkspaceSlotRoleSet.Admits` returns false/`roles_none` for `none`. |
| Task checkout identifies runner build | Initial task HEAD is 52a90a3935f1f8ed4198200ce3a5e21ca8dab370; this is task source, not runner binary provenance. |

## Slices
One documentation slice: `docs/superpowers/plans/2026-11-15-rollout-canary-server2-note.md` (ten-line lease/rollback note) and this plan. Commit on `feat/card-task-80c30683`, push that exact ref, verify its advertised SHA, and report runtime identity plus the discrepancy. Existing tests affected: none; no migrations, public DTOs/routes, operation labels, remote script functions or statement budgets change; see [impacted classes, CARD-1782](../../testing-and-build.md#impacted-classes-card-1782).

### Checkpoints
Closed build/test list: empty, as expressly required by the canary brief. Manual checks use the assigned Linux runner lane: count note lines, inspect the staged diff/whitespace, and verify the pushed branch SHA. No class/method roster or suffix collisions apply. Read-only GET `/api/runner-defaults` returned global preference server2 (revision 2); GET `/api/session-runners` returned available desktop, server2 and server2-temp. Placement remains the assigned host; no new dispatch is planned and no fleet location is prescribed.

## Performance budget
Apply [performance standards](../../testing-and-build.md#performance-standards). No test selection, application/category batches, project graph, setup fixture or runtime behavior changes: this is documentation only and the brief prohibits builds/tests. Container/readiness, data preparation, host startup, migrations, snapshot/restore, test body, teardown, build and build-slot wait are all not applicable. Metadata reads share this existing session; API GETs were bounded at 20 seconds each, with the successful paired read taking 1.44 seconds wall time. This is one observation, not a measured p95; no test timeout or approximately-2x-p95 claim is made. No production timeout changes. Evidence is this plan, the note, command outcomes and the final report; manual review/publication is expected to take minutes, with no over-30-minute selection. No session waiting for input is added or changed.

## Handoff
Next: investigate the mismatch between the stipulated roles-none preparation and observed ws-01 path. No code stage or further verification spend is authorized by this canary; a separately commissioned implementation plan would proceed to TestDesign before Code.
