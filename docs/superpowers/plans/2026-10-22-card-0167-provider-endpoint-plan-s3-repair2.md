# CARD-0167 S3 repair 2: Final Review 6c08cd4b R1 (restamp the pool projection on every launch)

Amends [the landed plan](2026-10-22-card-0167-provider-endpoint-plan.md), [the S3 checkpoint amendment](2026-10-22-card-0167-provider-endpoint-plan-s3-amendment.md) and [the S3 repair](2026-10-22-card-0167-provider-endpoint-plan-s3-repair.md) for Code task 8c2a794e (descendant of 73a4fdd8 at `81937b7e5`). No earlier file is edited; this file carries the repair-2 slice and is the `--plan` the checkpoint tool runs. Target: master only.

## R1. The stamp was written at the cold launch only, from a separate map read

Repair 1 stamped `Agents.PoolProviderProjectionJson` inside the dispatch claim transaction from its own read of the project's endpoint map. Two gaps:

- `AgentTaskDispatcher.RelaunchWedgedAsync` starts a new process for the SAME pool row; its spec re-reads the map, but the row kept the wedged process's stamp. Sequence: launched with none (stamp null), boot wedges, operator sets F, relaunch starts WITH F (and F's resolved key), row stays null and goes warm, operator clears F, the next task matches null and runs through F with F's key.
- The claim-transaction read and the post-commit spec read (`BuildLaunchSpecAsync`) are two reads; an edit between them launched F' under a row stamped F.
- `AgentControlService` start/resume of a pool row (`StartInteractiveSessionAsync`, both arms) and a Herdr adopt of a pool row (`AttachHerdrAsync`) kept the old stamp.

### Design

- **One read**: `BuildLaunchSpecAsync`'s profile-less branch (the only branch a pool delegate takes: `ResolveDelegateProgramAsync` resolves a profile only for a standing agent) stamps a pool row with `PoolFingerprint(session.AgentKind, <the endpoints it just read for the spec>, agent mode, agent endpoint)` and saves before returning, so before the caller can enqueue the launch. Cold dispatch and boot-wedge relaunch both pass here (via `FinishDispatchedLaunchSpecAsync`).
- **Unknown until built**: the claim block and the relaunch commit set the stamp to `ProviderEndpointLayers.UnknownPoolProjection` (`"unknown"`, not JSON, so no fingerprint, a JSON document or null, can equal it). A launch that never reaches the spec build (unresolved program, refused binding, exception) leaves a row that matches no task, including one that projects nothing. Null is NOT used for this, because null matches a task that projects nothing.
- **Control start/resume**: `AgentLaunchResolution.ResolveForAgentAsync` returns the map its `AttachProjectContextAsync` read on the new `ResolvedAgentTuiLaunch.ProjectProviderEndpoints` (init-only, default null; both resolver branches resolve from that same `options` value), and `StartInteractiveSessionAsync` stamps a pool row from it for `spec.Kind`; both arms save it with the session row.
- **Herdr adopt**: the adopted process was launched outside Antiphon, so a pool row is stamped unknown (the map cannot describe it).
- Secrets: the stamp holds env names, the URL and the `{{key:NAME}}` placeholder only; every witness uses sentinel names and a sentinel key value and asserts the value never enters the stamp.

### Tests (red at `1d5a070e1`, one narrow build-slot run: 5 total, 5 failed on the guarded assertion)

| Test | Guards | Red message at `1d5a070e1` |
|---|---|---|
| `PoolProviderProjectionStampTests.C0167_cold_launch_stamps_the_map_its_launch_spec_read` | claim/spec race: a `LandDeliveryBoundary` flips the map F -> F' at `dispatch-warning-claim-committed`; spec carries F', stamp must be F' | stamp `...proxy-f-0167...` (F) instead of F' |
| `PoolProviderProjectionStampTests.C0167_boot_wedge_relaunch_restamps_after_the_endpoint_is_set_changed_and_cleared` | relaunch restamp: none -> set F -> F' -> cleared, three relaunches, spec and stamp agree each time | stamp `null` instead of fp(F) |
| `PoolProviderProjectionStampTests.C0167_none_wedge_set_relaunch_clear_then_the_next_task_does_not_match` | the review's end-to-end sequence, producer (TickAsync cold launch, RelaunchWedgedAsync) to recipient (`InheritedEnvMatchesPool` over the persisted row and the re-read map) | `should be False but was True` |
| `AgentSessionBackendTests.C0167_start_and_resume_restamp_a_pool_rows_provider_projection` | control start (new-session arm) and resume arm rewrite a stale stamp from the launch (no resolver: projects nothing, null) | `should be null but was "{...stale-0167...}"` |
| `AgentAttachHerdrTests.C0167_attaching_a_pool_delegate_marks_its_provider_projection_unknown` | Herdr adopt marks a pool row unknown, which matches no task | `should be "unknown" but was null` |

### Checkpoints

One build serves every row (all rows read one test assembly) and all rows are one slice group, so the reuse is admitted. CP-1..CP-4 are the repair-1 rows unchanged; CP-5 is the new witnesses with their full affected classes; CP-6 is the dispatcher integration classes whose launch path changed (warm pool and boot-wedge relaunch).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S3R2 | `tests/Antiphon.Tests -> bin-c0167r2/` | projection | `/*/*/(ProviderEndpointProjectionTests*)\|(FakeLlmApiSelfTests*)/(C0167_*)\|(ForClaude_sets*)\|(ForGrok_requires*)\|(ForCodex_emits*)` | D-1, D-3, D-5, D-6, carried S1 review gaps, D2 parity | 37 passed, 0 failed, 0 skipped | 37 | 12 |
| CP-2 | S3R2 | CP-1 | persistence | `/*/*/(ProviderEndpointApiTests*)\|(ApiKeyApiTests*)/*` | D-2, D-8, D-10 regression, D2's save-time 422s | 12 passed, 0 failed, 0 skipped | 12 | 14 |
| CP-3 | S3R2 | CP-1 | resolver-unit | `/*/*/(AgentTuiLaunchResolverTests*)\|(AgentLaunchEnvTests*)\|(PhoneHomeTaskRoutingTests*)/*` | D-4, D-5, D-11, redaction, byte-identical, pool predicate witnesses | 62 passed, 0 failed, 0 skipped | 62 | 12 |
| CP-4 | S3R2 | CP-1 | resolver-layers | `/*/*/LaunchEnvLayersIntegrationTests/*` | D-4 on the legacy registry path, rank order across both resolvers | 25 passed, 0 failed, 0 skipped | 25 | 10 |
| CP-5 | S3R2 | CP-1 | stamp-producers | `/*/*/(PoolProviderProjectionStampTests*)\|(AgentSessionBackendTests*)\|(AgentAttachHerdrTests*)/*` | R1: the five new witnesses plus their full classes (control start/resume, Herdr attach) | all listed, 0 failed, 0 skipped | 35 | 15 |
| CP-6 | S3R2 | CP-1 | dispatch-launch | `/*/*/(AgentTaskPoolTests*)\|(BootWedgeRelaunchParityTests*)/*` | cold dispatch and relaunch spec path, warm-pool reuse unchanged | all listed, 0 failed, 0 skipped | 46 | 20 |

**CP-6 is a non-receipt diagnostic row.** At `8c285b514` it ran 50: 28 passed, 22 failed (`AgentTaskPoolTests`: the janitor/release-sweep/C691 cohort on `CardSiblingInspectionQueue only implements IAsyncDisposable`, plus `unrelated_work_compacts...`, `the_hold_retraces...`, `a_busy_standing_agent...`). The same build-slot run at base `81937b7e5` (detached worktree) is 50: 28 passed, 22 failed with the identical 22 names, so all 22 are inherited, and both `BootWedgeRelaunchParityTests` cases pass at the tip. The receipt run is `--rows CP-1,CP-2,CP-3,CP-4,CP-5`; nothing after `8c285b514` touches the dispatcher (only `AgentControlService` and one test file).

**CP-5 repair.** At `8c285b514` CP-5 was 43: 41 passed, 2 failed (both control witnesses). `StartInteractiveSessionAsync` reloads the agent inside its reservation, which discarded a stamp set before it, so the stamp moved after that reload (`79604e7b9`). The attach witness had seeded the pool flag through a second context while the harness scope still tracked the row (a test artifact: production gives each request its own scope), so it now seeds through the harness scope's context.

No V-n/R-n IDs and no PC table exist in the plan. The Unit lane is not a row: the brief for 8c2a794e asks for minimal testing with no whole-suite runs, and every changed production path is covered by CP-3..CP-6. The plan's two operator acceptance items are unchanged and remain operator-run after land.
