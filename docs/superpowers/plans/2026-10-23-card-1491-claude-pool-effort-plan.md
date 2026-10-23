# CARD-1491: match warm Claude delegates to their launch effort

Plan owner: task `f7c9db4a-51c7-4b46-ad07-469f629df1a0`. Source inspected:
`5073726ce95955a09cf0a40b64bfdd501cf99562`. This is a documentation deliverable;
implementation and test execution have not happened. Land this Plan under its own
task owner. TestDesign remains a separate stage: the brief requested checkpoints
but did not fold that stage into this dispatch.

The change makes a Shared Claude task reuse a pool process only when the process's
recorded launch effort equals the task's currently resolved effort. A High Review
requiring `high` must reject a High process launched for Code at `xhigh`. Conversely,
a task requiring `xhigh` may reuse a process already launched at `xhigh`, including
a free pinned delegate. Role identity itself is not a reuse key.

Inputs read: the full CARD-1491 card; the [parent plan](2026-10-22-claude-effort-by-role-plan.md)
and [placement repair](2026-10-22-claude-effort-by-role-repair.md); the complete
Final Review result for `0a75c217-c2bf-43b4-9ca5-ef089b491fa5`; the CARD-0167
[stamp repair](2026-10-22-card-0167-provider-endpoint-plan-s3-repair.md) and
[launch-path repair](2026-10-22-card-0167-provider-endpoint-plan-s3-repair2.md);
the [CARD-1495 reuse witnesses](2026-10-22-card-1495-reuse-witness-plan.md).
The parent Review's standing-seat escalation wording limitation is outside this card.

## Ground truth

| Card assumption or tempting shortcut | What the inspected code does | Consequence |
|---|---|---|
| An override already protects reuse in both directions. | `AgentTaskDispatcher.DeclinesReuseForClaudeEffort` examines only the incoming task's override versus its tier. A Review with no override passes it even if a candidate launched at `xhigh`. | Replace the task-wide decline with a candidate comparison. |
| A free pinned follow-up at the same overridden effort should reuse. | The same decline returns `SpawnFresh` whenever the task override differs from the tier, without inspecting the process. | Matching stamps must permit reuse in both pinned and unpinned paths. |
| Pool agents record launch effort already. | `Agent` records `ModelLevel`, `PoolProjectId`, inherited `LaunchEnvJson`, and `PoolProviderProjectionJson`; none identifies Claude effort. | Persist a separate launch fact. Neither current configuration nor the last task's role proves it. |
| Reusing `PoolProviderProjectionJson` avoids all migration cost. | `ProviderEndpointLayers.PoolFingerprint` returns an opaque, ordinally compared canonical JSON string containing endpoint env and argv tail. Null means *no endpoint*; `"unknown"` means unknown. Claude's endpoint argv tail is empty. The field is absent on the inspected rc tip. | An effort envelope changes the endpoint contract and adds an rc dependency. A nullable scalar is the smaller independent fix. |
| The claim transaction is the only stamp writer needed. | Cold dispatch and `RelaunchWedgedAsync` invalidate the provider stamp before launch; `BuildLaunchSpecAsync` then writes it from the spec's own inputs before enqueue. `AgentControlService` also starts pool rows and adopts Herdr panes. | Follow all these producer/invalidation paths, not just initial dispatch. |
| Control resume of a pool row can be exercised like standing resume. | `StandingSessionOwnership.RequireAsync` and `Refusal` explicitly reject `IsPoolDelegate`. CARD-0167's old resume test was removed for this reason. | Preserve refusal. Test admitted control start and the shared stamping block; do not manufacture a permissive pool-resume fixture. |
| The two matching call sites are equivalent enough for a pure helper test. | Pinned matching occurs after standing placement and the pool busy guard. Unpinned matching occurs while filtering candidates before ranking. CARD-1495 already found a call-site coverage gap for endpoint stamps. | Drive `TryReuseWarmAgentAsync` through both paths and test selection among multiple candidates. |
| Every pipeline Review is exposed. | `DispatchOneAsync` invokes warm reuse for Shared workspaces. Pipeline Reviews use Worktree; the disclosed failure affects ad-hoc Shared Review. | Keep workspace admission unchanged; regression must actually use Shared. |
| An empty map means all behavior must stay identical. | Empty-map argv is already pinned in `ClaudeEffortByRoleDispatchTests`; legacy process effort is still unknowable. | Preserve argv exactly, but explicitly accept the one-time transition in reuse eligibility for unstamped Claude rows. |
| Existing warm fixtures model launches. | Several helpers insert idle Claude rows directly with no launch metadata, notably `ClaudeEffortByRoleDispatchTests`, `AgentTaskPoolTests`, `LaunchEnvLayersIntegrationTests`, and `ModelAvailabilityDispatcherTests`. | Give fixtures that represent known launches an explicit effort. Keep intentional legacy/unknown fixtures null. |

## Decisions

**D-1 — Store `Agent.PoolClaudeEffort : string?`, mapped to nullable PostgreSQL
`text` on `Agents`.** Values produced by Antiphon are canonical `low`, `medium`,
`high`, `xhigh`, or `max`. Null means the launch effort is unknown, including legacy
rows and invalidated/replaced processes. No default, index, backfill, API DTO,
user-editable setting, or enum migration is needed.

Rejected: extending `PoolProviderProjectionJson`. Its null and opaque-equality
semantics would need redesign, every endpoint producer/consumer would acquire
effort knowledge, and rc would need CARD-0167's schema first. Reuse that feature's
*stamp lifecycle pattern*, while leaving its bytes and predicate intact. Also reject
`LaunchEnvJson`: children inherit it and effort is an argv fact. A new general launch
fingerprint/session history framework would exceed this defect.

**D-2 — Stamp the effort present in the resolved launch argv, without changing
argv composition.** Add a small pure `ClaudeLaunchArgs.ReadLaunchEffort(args)`
helper. It reads explicit `--effort value` and `--effort=value` tokens before `--`,
canonicalizes through the existing catalog, and returns null for absent, missing,
invalid, or conflicting values. Repeated identical values may resolve to that value.
Its semantics should agree with `ClaudeEffortIntent.Read` in the Pty project, but
Application must not acquire a new Pty dependency. Do not tokenize a joined command
line, inspect prompt text inside an argv element, log argv, or change launch admission.

The helper describes explicit launch intent, not a later runtime observation. This
card does not detect manual `/effort` changes after launch. Reading the built spec
avoids a second role/configuration resolution and keeps standing/control launches
at the effort their own composer actually emitted. Rejected: infer the value from
the current tier, role, map, or mutable agent settings after launching.

**D-3 — Invalidate before replacement; stamp before enqueue.** Only pool rows are
written. At the cold claim and boot-wedge replacement points that invalidate the
provider stamp, also set `PoolClaudeEffort = null`. In the profile-less
`BuildLaunchSpecAsync` branch, after successfully building `spec`, assign the helper's
result when `spec.Kind == ClaudeCode`, otherwise null; save with the existing stamp
save before the launch can be enqueued. Pool delegates do not take the profile branch.

In `AgentControlService.StartInteractiveSessionAsync`, assign from the resolved
`spec` inside the existing pool-stamp block **after** the reservation's `ReloadAsync`.
Both existing session-save branches then carry the value; preserve all continuity
and resume refusals. In `AttachHerdrAsync`, clear a pool row's effort to null: an
externally launched pane has no trustworthy launch argv. Clear even if the row is
now another provider, so an old Claude value cannot survive a kind change. Never
stamp a standing row or move standing placement into pool matching.

Rejected: stamping only at cold launch, or before a reload that discards the write.
CARD-0167's two repairs show the concrete stale-stamp failures those approaches cause.
Launch construction/queue failure must not make an old process's stamp authoritative
for a replacement; the existing live-session checks remain required.

**D-4 — Compare candidates, not roles.** Replace `DeclinesReuseForClaudeEffort(task)`
with a private candidate-aware predicate, conceptually:

```text
non-Claude task -> true (all existing kind/environment gates still apply)
Claude task -> non-null agent.PoolClaudeEffort equals, Ordinal,
               ClaudeLaunchArgs.Effort(task.ModelLevel, task.Role, settings.ClaudeEffortByRole)
```

For a pinned pool row, call it at the old effort-decline location, after the busy
check; mismatch/unknown gives `SpawnFresh`, match permits normal reuse. Standing
placement remains earlier. For the unpinned shop, remove the early blanket decline
and filter each candidate before ranking/session selection. A newer mismatch must
not hide an older match. Leave tier, kind, project, inherited environment, provider
projection, runner, directory, reservation, and live/working-session gates intact.
Do not restamp on reuse, match on role equality, mutate effort in a live process,
or add a new tier policy for pinned agents. Update the nearby comments to describe
their own methods rather than retaining the misplaced parent comment.

**D-5 — Unknown Claude effort declines reuse even with an empty map.** Do not
backfill from `ModelLevel`: that would relabel overridden processes as tier launches
and preserve the Review defect. Unpinned unknown rows remain idle for the ordinary
pool retirement path; a free pinned unknown row takes the existing relaunch path.
An otherwise compatible busy pinned row still waits. This may increase cold starts
and temporarily retain unused idle seats during rollout; subsequent known matching
launches regain normal reuse. There is no forced pool flush and no fixed promise of
one extra start per row, since availability and reservations also affect selection.

**D-6 — Preserve provider and launch compatibility.** Codex/Grok bypass only this
new effort predicate; their existing eligibility checks and argv stay intact.
`ComposeDelegateArgs`, `AgentSessionLaunchComposer`, `AgentTaskService` escalation
text, role-map validation, launch env, and instruction text do not change. Empty-map
Claude argv must remain element-for-element identical, including prompt bytes and
argument order. A new column is metadata, never an additional CLI flag.

**D-7 — Keep verification portable and bounded.** Read `/api/runner-defaults` and
`/api/session-runners` immediately before each later dispatch. At planning time
(2026-10-23 UTC), defaults revision 2 selected an available eligible Linux runner;
an eligible Windows runner also existed. These observations are not a placement pin.
Omit `-Runner` and `-Platform`; no native Windows behavior changes. Every checkpoint
below uses the **portable .NET/PostgreSQL lane**, with fake launch sinks/adapters,
owned temporary directories, and isolated schemas for new integration tests.

**D-8 — Preserve the separate TestDesign gate.** The coverage and checkpoint list
below are the bounded planning proposal. TestDesign must finalize fixture selections
and the expected expanded roster before Code; any amendment must stay method-scoped
and state its cost. No human decision is needed for D-1 through D-7. This Plan does
not authorize a whole-suite run, production migration, pool flush, restart, or rc land.

## Migration and rollout safety

Fetched reference observations on 2026-10-23:

| Reference | Full SHA | Latest migration ID in that tree |
|---|---|---|
| `origin/master` | `5073726ce95955a09cf0a40b64bfdd501cf99562` | `20261022110953_AddPoolProviderProjection` |
| `origin/rc/2026-10-20` | `5a126f6fc5ae4e16d803b628716b8f0485a37174` | `20261022095749_AddWorktreeResidueCursorLastOutcome` |

Generate `AddPoolClaudeEffort` with the repository-pinned EF CLI (`dotnet tool
restore`, then `dotnet ef migrations add AddPoolClaudeEffort --project server`).
The generated timestamp ID must sort **after both tips' latest IDs**, rechecked
after fetching immediately before generation/porting. Do not reuse the historical
maxima as permanent values, hand-write a migration, or rename a published one.
Any EF build uses the host build-slot wrapper and an isolated output path according
to the testing owner; it is a declared tooling operation, not another test pass.

`Up` adds just nullable `Agents.PoolClaudeEffort` with no default or data rewrite;
`Down` drops just that column. Old rows remain null. Inspect generated migration,
designer, and `AppDbContextModelSnapshot` for unrelated model changes. PostgreSQL
DDL still takes a table lock: use the existing migration/deployment procedure, no
ad-hoc live database command from this task. Apply the additive schema before the
new binary reads it. Keeping the nullable column allows binary rollback, but an
old binary does not enforce the new reuse guarantee. Do not drop the column while
a binary that maps it is running.

The rc branch already contains CARD-1481's effort decline but lacks the provider
stamp. This plan therefore introduces no CARD-0167 dependency. A separately owned
rc port must adapt insertion context to its launch paths and generate/validate the
designer and snapshot against the **rc model**, never copy master's whole snapshot
or import unrelated migrations. Coordinate the migration identity across both
lines before publication; no rewrite of a migration already applied anywhere.
Check `HasPendingModelChanges()` and upgrade a pre-column database in that port's
own narrow verification. The Plan document remains master-only, consistent with
the parent rc policy. Master publication is not product activation.

## Slices

| Slice | Deliverable and files | Verification |
|---|---|---|
| S0 (this task) | This plan only, committed and pushed on the assigned Plan branch. | Source/card/Review reads; Markdown/table checks; conflict-free `merge-tree` against fetched master. No build or product test. |
| S1 (Code red foundation) | Add `PoolClaudeEffort` in `server/Domain/Entities/Agent.cs`; CLI-generated migration/designer and `server/Migrations/AppDbContextModelSnapshot.cs`; compile-only `ReadLaunchEffort` stub returning null in `server/Application/Services/ClaudeLaunchArgs.cs`. Add `tests/Antiphon.Tests/Application/PoolClaudeEffortTests.cs` and `PoolClaudeEffortMigrationTests.cs`; new method each in `AgentSessionBackendTests.cs` and `AgentAttachHerdrTests.cs`. Existing dispatcher behavior and stamp writers remain unchanged. | Commit and push before CP-1/CP-2. Real assertion failures must demonstrate the missing writers and both reuse defects. The stub is temporary, never a completed implementation. |
| S2 (Code fix) | Implement helper; reset/stamp/compare in `server/Application/Services/AgentTaskDispatcher.cs`; reset/stamp in `server/Application/Services/AgentControlService.cs`; replace the residual paragraph in `docs/agent-kinds.md`. Update known-launch test seeds described below, preserving their existing outcome assertions. | Commit and push before CP-3 through CP-9. Run each listed group once at the same frozen SHA. |
| S3 (qualification/report) | Preserve exact checkpoint lines, full SHAs, counts, intentional red outcomes, tooling cost, migration ID and fresh master/rc observations in the stored report. Generated receipts stay ignored. No extra source/report-only commit after the qualified source unless necessary. | Validate green receipts for the exact SHA; run `scripts/check-evidence-diff.ps1` over base..HEAD; fetch master and report `git merge-tree --write-tree origin/master HEAD`. Push and confirm the task ref. Final Code handoff is Review. |

Fixture work is part of S1/S2, not a change to production defaults. Specifically:

- `ClaudeEffortByRoleDispatchTests.SeedAgentAsync` and
  `AgentTaskPoolTests.SeedWarmAgentAsync` must seed the declared launch effort for
  known Claude processes. The old overridden-role test continues to reject a
  tier-effort process; it no longer means that every overridden role rejects reuse.
- In `ApiKeys/LaunchEnvLayersIntegrationTests.cs`, the two direct env reuse tests
  and `AddWarmPoolAgentAsync` used by CARD-1495 need matching effort stamps, so the
  new guard cannot mask endpoint/environment regressions. Keep their unknown
  *provider* stamp distinct from unknown *effort*.
- Audit direct seeds in `GrokDelegateDispatchTests.cs`, `CodexDelegateDispatchTests.cs`
  (their Claude controls), `PinnedAgentKindTests.cs`, `ModelAvailabilityDispatcherTests.cs`
  and its consumers, `ComplexityDispatcherTests.cs`, `RoutingPinDispatcherTests.cs`,
  `RoutingPinCandidateDispatchTests.cs`, `AgentTaskCheckScheduleTests.cs`,
  `PhoneHomeTaskDispatchProjectionTests.cs`, `TaskPlatformDispatchTests.cs`, and
  `TestHelpers/DispatchConcurrencyTestHost.cs`. A `PoolIdleSince` assignment alone
  does not prove it exercises reuse. Inspect reachability, then change only known
  launch seeds whose assertions depend on reuse. A simulated tier-changing
  *relaunch* updates its stamp; a mere edit to an agent's tier must not do so.

TestDesign owns the exact affected-fixture roster and any corresponding additional
method rows before Code is dispatched. This explicit audit is necessary because
null is deliberately becoming an unknown launch, rather than silently preserving
incorrect fixture assumptions with a production fallback.

No new session waiting for human input is introduced or changed. Existing busy-pin
waiting remains; this card adds no release timer. With parking disabled there is
no automatic release deadline for a session waiting for input (CARD-1083).

## Verification design

This section supplies the requested narrow verification proposal for the separate
TestDesign handoff. New methods below are planned names, not claims they already
exist. Use literal expected effort values and persisted rows/captured launch specs;
never derive the oracle by calling the new helper twice. Test new dispatcher paths
through `TickAsync`/`RelaunchWedgedAsync` and a recording `IAgentTaskLaunchSink`, as
in `PoolProviderProjectionStampTests`, without depending on its endpoint schema.
Reuse tests drive `TryReuseWarmAgentAsync`, as in CARD-1495; `Pool` and `Pinned`
arguments must reach their distinct call sites. All untouched gates must admit
the tested candidate. Verify selected agent/session IDs, unchanged declined rows,
and absence of a second launch for a matching pin.

### New witnesses

All V-1 through V-9 methods are in `PoolClaudeEffortTests`:

| ID | Method / expanded executions | Required observations and expected S1 result |
|---|---|---|
| V-1 | `C1491_resolved_argv_stamp` / 8 | Literal argv cases: separate `high`, equals/mixed-case `xhigh`, `max`, identical repeated `high`, absent, missing value, conflicting flags, invalid value. First four yield canonical literals; last four null. Include a `--` stop assertion in the absent case. S1: four failures, four controls pass. No argv mutation. |
| V-2 | `C1491_dispatch_launch_persists_effort` / 2 (cold, wedge) | Start from unknown/stale effort; High Code at `xhigh` produces literal `--effort xhigh` and persisted `xhigh` before sink enqueue. Settle that row idle and prove a Shared Review/High cannot reuse it, using the stored row without repairing its stamp in the fixture. A subsequent replacement after removing/changing the override records literal `high`, including a changed session ID. Wedge must actually execute `RelaunchWedgedAsync`. S1: both fail the persisted stamp. |
| V-3 | `C1491_review_rejects_overridden_process` / 2 (Pool, Pinned) | Seed a known High process with literal `xhigh` and a live session, then ask for Shared Review/High with no Review override. Desired `high`; return `SpawnFresh`, never bind the old session. This independent consumer witness must reach the matcher even before the writer is implemented; V-2 covers the production writer-to-consumer chain. S1: both fail the reuse decision. |
| V-4 | `C1491_matching_effort_reuses` / 4 (Pool/Pinned × high/xhigh) | Known `high` with empty map, and known `xhigh` under an override, reuse the same session; source and target roles differ. Exercise at least the pinned xhigh case through a dispatch tick with a recording sink, proving no second launch. S1: high controls pass, both xhigh cases fail the blanket decline. |
| V-5 | `C1491_unknown_effort_declines` / 4 (Pool/Pinned × null/invalid) | Empty-map High task cannot reuse a legacy/null or invalid stamp. Leave the candidate untouched on decline. S1: all fail. |
| V-6 | `C1491_search_selects_matching_candidate` / 1 | A newer `xhigh` process and older `high` process at the same tier otherwise qualify. Review must select the older matching session. S1: fails by choosing the newer process. |
| V-7 | `C1491_non_claude_ignores_stamp` / 2 (Codex, Grok) | Each otherwise eligible kind reuses under a nonempty Claude role map with null or a stale Claude stamp. Existing Grok rules gates must be satisfied. S1: controls pass. |
| V-8 | `C1491_non_claude_launch_clears_stamp` / 2 (Codex, Grok) | Relaunch a pool row carrying old `xhigh` for its new non-Claude kind. Persist null; captured argv retains that provider's own effort switches, with no Claude switch. S1: both fail the stale stamp assertion. |
| V-9 | `C1491_interrupted_launch_keeps_unknown` / 2 (cold, wedge) | Seed a stale known stamp and force spec construction to fail *after* replacement claim/commit (for example a deliberately invalid effort in a hand-built options fixture that bypasses startup validation). Confirm the seam was reached, no launch sink call, and persisted null rather than the old effort. S1: both fail the stamp assertion; fixture/admission errors are not the intended red. |

Additional producers and schema:

| ID | File and method / executions | Required observations |
|---|---|---|
| V-10 | `AgentSessionBackendTests.C1491_control_start_replaces_stale_effort_stamp` / 1 | Use the existing admitted pool-control-start harness; seed stale `xhigh`, start at High, capture literal `high` in argv and persisted `high` through a fresh DbContext. S1 fails. Keep the assignment after reload. Pool resume remains refused by `StandingSessionOwnership`, not a missing successful-resume test. |
| V-11 | `AgentAttachHerdrTests.C1491_adoption_clears_stale_claude_effort` / 1 | Extend the existing fake Herdr adoption shape with a stale effort. Adoption clears it. The existing Grok fixture suffices for kind-independent invalidation; do not claim native Claude attachment coverage. S1 fails. |
| V-12 | `PoolClaudeEffortMigrationTests.C1491_nullable_stamp_roundtrips_and_legacy_rows_stay_unknown` / 1 | Owned isolated schema only: migrate down to the immediately preceding ID, insert an Agent using SQL that does not reference the new property, upgrade, and verify old fields unchanged/null stamp. Round-trip `xhigh` via EF, verify model/snapshot agreement and no pending migrations. Down drops only this column and up restores null. This is schema verification, expected green once S1's generated migration exists. |

### Compatibility and controls

- **R-1:** Existing `ClaudeEffortByRoleDispatchTests` argv tests keep the literal
  empty-map sequence and compare the entire Review argv across maps. Retain the
  busy-pin `WaitForAgent`, standing Check placement, mismatching tier-effort warm
  rejection, and free-pin mismatch assertions; give known pool fixtures stamps.
- **R-2:** CARD-1495's four methods (two paths each), and the two existing direct
  env-reuse methods in `LaunchEnvLayersIntegrationTests`, keep endpoint/inherited
  environment decisions observable with effort already matching.
- **R-3:** Four precise `AgentTaskPoolTests` methods retain initial reuse, bearer
  token transfer, same-run context, and exclusion of mid-turn sessions.
- **R-4:** One existing Codex argv pin and one Grok argv pin, combined with V-7/V-8,
  cover no cross-provider changes. No full provider suites or Windows-only real CLI
  argv suite: no terminal, command encoding, or native launch code changes.

Positive controls remain pending for later SourceLanding Mutation. Bind each to
one exact method, using a precise method filter, not a whole class: bypass each
effort comparison -> V-3's corresponding path; restore the blanket override decline
-> V-4; treat null as the tier -> V-5; put effort filtering after `FirstOrDefault`
-> V-6; omit each writer/invalidation -> its V-2/V-8/V-9/V-10/V-11 method; parse only
the tier or ignore equals/conflicts -> V-1; apply the Claude guard to every kind ->
V-7. Omit the column or make it non-null/defaulted -> V-12. Do not rerun controls
already witnessed red merely for ceremony; retain exact assertion evidence.

### Execution and cost

S1 commits the failing tests and passive schema first. CP-1/CP-2 intentionally
produce non-green tool results: expect V-1..V-6/V-8/V-9 failures and V-7 controls,
plus both producer failures in CP-2. The enumerated proposal is 27 executions in
CP-1 (19 fail, 8 pass) and two failures in CP-2. Confirm actual TRX identities;
zero tests, compile errors, missing Docker, or failures before the guarded assertion
do not count. S2 runs the same witnesses green once, plus the named compatibility
and schema rows. Never represent the red receipt as a clean qualification.

The two slice groups each have one isolated test build; within a group all rows
reuse it. Estimated checkpoint floor is **25 minutes** including red and green
builds, excluding slot wait, EF generation, and checkpoint-tool bootstrap. After
an actual fix rerun only the affected listed rows, with fresh build provenance;
report reruns. No full assembly, namespace, Unit lane, repeated green round, or
live provider call is authorized by this table.

Run with the checkpoint tool's `run --plan <this-path> --rows <slice-row-ids>
--expected-source-sha <full-sha>`, then `wait` for the same run until exit is not
75. Own and await the run before settlement. The tool takes build slots; other
build/tool drivers use `scripts/build-slot.ps1`. Budget the one EF generation
build and one isolated checkpoint-tool bootstrap explicitly in Code's report,
without adding ad-hoc test runs. Use forward-slash `bin-.../` paths; delete owned
alternate outputs after evidence is retained. Preserve unedited CHECKPOINT lines,
fresh TRX counts and exact source SHA. Validate CP-3..CP-9 receipts as clean green
evidence. A red not attributable to this card must be rerun at base with its exact
method, never with the assembly.

### Checkpoints

Code amendment (task d15bbee2): TestDesign was explicitly skipped by the dispatch brief.
CP-2 initially selected zero tests because TUnit's source-generated hint extractor treats
OR operands without wildcards as literals. Trailing wildcards below correct that syntax;
the intended methods and counts are unchanged and are checked against fresh TRX identities.
CP-2 was rerun through the checkpoint tool's no-build row verb against the same S1 build,
with the corrected filter, before implementation. Cost: one additional two-case test host,
no additional build. The original zero-count receipt remains invalid evidence.

Closed list for this Plan revision; all rows use the **portable .NET/PostgreSQL
lane** under effective runner defaults. TestDesign must publish any fixture-roster
amendment before Code; the table is not permission for discretionary broad runs.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1491-red/` | effort-red | `/*/*/PoolClaudeEffortTests/C1491_*` | V-1..V-9 | intentional red: 19 failed assertions, 8 passed controls, 0 skipped | 27 | 9 |
| CP-2 | S1 | CP-1 | producer-red | `/*/*/(AgentSessionBackendTests*)\|(AgentAttachHerdrTests*)/(C1491_control_start_replaces_stale_effort_stamp*)\|(C1491_adoption_clears_stale_claude_effort*)` | V-10, V-11 | intentional red: 2 failed stamp assertions, 0 skipped | 2 | 1 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1491-green/` | effort-green | `/*/*/PoolClaudeEffortTests/C1491_*` | V-1..V-9 | all 27 passed, 0 failed, 0 skipped | 27 | 9 |
| CP-4 | S2 | CP-3 | producer-green | `/*/*/(AgentSessionBackendTests*)\|(AgentAttachHerdrTests*)/(C1491_control_start_replaces_stale_effort_stamp*)\|(C1491_adoption_clears_stale_claude_effort*)` | V-10, V-11 | all 2 passed, 0 failed, 0 skipped | 2 | 1 |
| CP-5 | S2 | CP-3 | migration | `/*/*/PoolClaudeEffortMigrationTests/C1491_nullable_stamp_roundtrips_and_legacy_rows_stay_unknown` | V-12 | 1 passed, 0 failed, 0 skipped | 1 | 1 |
| CP-6 | S2 | CP-3 | claude-compatibility | `/*/*/ClaudeEffortByRoleDispatchTests/(a_code_task_launches_at_the_overridden_effort_while_review_keeps_the_tier*)\|(an_empty_map_launches_todays_argv*)\|(an_override_changes_only_its_own_roles_effort_element*)\|(a_check_task_pinned_to_its_standing_seat_keeps_the_seats_placement_under_an_override*)\|(a_task_pinned_to_a_busy_pool_delegate_still_waits_for_it_under_an_override*)\|(a_role_override_that_differs_from_the_tier_declines_warm_reuse*)\|(a_task_pinned_to_a_free_pool_delegate_launched_at_another_effort_relaunches*)` | R-1 | all 8 passed, 0 failed, 0 skipped | 8 | 1 |
| CP-7 | S2 | CP-3 | environment-compatibility | `/*/*/LaunchEnvLayersIntegrationTests/(C1495_*)\|(TryReuseWarmAgentAsync_declines_a_non_empty_override_and_not_an_empty_one*)\|(TryReuseWarmAgentAsync_declines_when_inherited_env_projections_differ*)` | R-2 | all 10 passed, 0 failed, 0 skipped | 10 | 1 |
| CP-8 | S2 | CP-3 | pool-compatibility | `/*/*/AgentTaskPoolTests/(a_queued_task_takes_over_a_warm_agent_in_its_directory*)\|(the_reused_sessions_bearer_token_now_resolves_to_the_new_task*)\|(a_follow_up_in_the_same_run_keeps_the_context_uncompacted*)\|(a_mid_turn_warm_agent_is_not_reused_by_the_unpinned_shop*)` | R-3 | all 4 passed, 0 failed, 0 skipped | 4 | 1 |
| CP-9 | S2 | CP-3 | provider-argv | `/*/*/(CodexDelegateDispatchTests*)\|(GrokDelegateDispatchTests*)/(a_codex_delegate_launches_the_codex_definition_with_a_slug_an_effort_and_developer_instructions*)\|(a_grok_delegate_launches_the_grok_definition_with_a_grok_model_when_nothing_composes*)` | R-4 | all 2 passed, 0 failed, 0 skipped | 2 | 1 |

--- next stage ---
next: test-design
handoff: Finalize CARD-1491's narrow witnesses and affected warm-fixture roster, retaining the separate nullable effort stamp, fail-closed legacy policy, both reuse paths, and master/rc migration ordering. Publish the executable checkpoint amendment before Code; no whole-suite runs.
artifact: docs/superpowers/plans/2026-10-23-card-1491-claude-pool-effort-plan.md
