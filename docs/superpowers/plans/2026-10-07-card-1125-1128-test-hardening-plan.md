# CARD-1125 / CARD-1128: mirror-removal fallback and Program sweep-wiring guards

Date: 2026-10-07. Plan task: `bd8d6f42-18cb-417d-9204-eaff2058e719`.
Board: Antiphon. CARD-1125: `dad48ae1-99a8-4fa7-a70b-f4b1258214fe`;
CARD-1128: `b9971246-fbc0-4772-865a-6cba7daa9f8d`.

Inspected master: **`056ca06eb8e538b34c27f0ee313407a18fc196d2`**, fetched from origin before
inspection. The assigned branch was fast-forwarded from
`b5e78700ae9a76430c13d75cc03399055dd82e59` to that SHA without rewriting history.
All source citations below refer to that master, not to an in-flight branch.

Status: complete design, including verification; **next: code**. No unresolved product decision.
This is the executable extraction of CARD-1128 from F4 and CARD-1125 from F5 in
[the CARD-1082 follow-up plan](2026-10-07-card-1082-followups-plan.md).
Its checkpoint list replaces those two cards' part of that bundle; it does not commission
CARD-1115 baseline gates, CARD-1136 recovery changes, CLI work, or their large final battery.

## Ground truth

| Card assumption | What master does (verified file:line) | Design consequence |
|---|---|---|
| CARD-1125 says Pending retirement is unreachable until S5. | This was historical. `server/Application/Services/AgentTaskReplyService.cs:1998` calls `SettlementSyncDebtPolicy.Classify`; `:1153` accepts Pending terminal settlements and `:1158` inserts their debt. `server/Application/Services/SettlementSyncDebtPolicy.cs:24` classifies the full owned-ref lease-busy observation as Pending. | The coverage gap is current and reachable; S5 is already landed. No investigation stage is needed. |
| Removal chooses confirmed, then Pending observed, then base. | `server/Application/Services/RemoteWorkspaceService.cs:802` reads stored evidence; `:805` selects `ConfirmedSha`, `:806` selects `ObservedSha` only for Pending, `:807` falls back to `WorktreeBaseSha`. | Three distinct inputs and three distinct expected wire values; preserve production selection. |
| Testing the helper alone is sufficient. | `RemoteWorkspaceService.cs:782` requires `WorkspacePublishV1`; `:783` respects an explicit override; `:784` sends the request and `:786` gates the SHA on full object-id length. | Exercise public `RemoveMirrorAsync` with no override, a capable peer and full 40-character SHAs. Assert the received request, including its path. |
| Existing removal coverage pins all arms. | `tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerTests.cs:183` tests Synchronized/ConfirmedSha and an unpublished-tip refusal. Its wire and residue assertions are at `:206`. No Pending sibling exists. `tests/Antiphon.Tests/Application/RemoteWorktreeMirrorTests.cs:291` throws on runner `Resolve`. | Reuse `RollingWorld`; retain the existing refusal test. Do not enlarge the unsuitable sync-only fixture. |
| The rolling peer needs new removal behavior. | `PhoneHomeRollingRunnerTests.cs:278` creates an isolated PostgreSQL store; `:297` advertises workspace publish capability; `:482` handles removal; `:497` returns `Removed=true, Residue=null` by default. | The three new cases require no peer/harness change. Each owns and disposes its world and scope. |
| Pending with ConfirmedSha is an ordinary producer shape. | `server/Application/Dtos/TaskProgressDtos.cs:92` writes ConfirmedSha only when `result.Confirmed`; `server/Application/Dtos/RemoteSettlementSyncDtos.cs:62` excludes Pending. Stored `RemoteSyncEvidence` nevertheless permits both fields (`TaskProgressDtos.cs:79`). | The third case is a deliberately constructed precedence witness for the stored-evidence reader, not a claim that normal settlement emits it. |
| Both sync sweeps can silently disappear from DI. | `server/Application/Services/AgentTaskDispatcher.cs:193` and `:194` default the two constructor parameters to null, assigned at `:197` and `:198`. `:7431` and `:7435` return zero when absent. | Merely asserting an empty sweep returns zero cannot detect missing injection. Inspect the resolved dispatcher's actual fields through an internal probe. |
| Program currently supplies both dependencies. | `server/Program.cs:412` registers `BlockedTaskSyncRecoveryService`, `:413` registers `SettlementSyncRecoveryService`, and `:470` registers the dispatcher by type. Its one public constructor starts at `AgentTaskDispatcher.cs:118`. | Preserve all registrations and constructor defaults. Add a real Program test that fails if either injected field becomes null. |
| A scope-resolved dispatcher represents the tick's sweep dispatcher. | The tick invokes both wrappers at `AgentTaskDispatcher.cs:339` and `:342`; `RunSweepAsync` creates its own scope at `:1812` and resolves `AgentTaskDispatcher` from it at `:1813`. | Resolve from factory scopes, never construct a dispatcher in the test or inspect only service descriptors. This is wiring proof, not a new debt-recovery behavior test. |
| A booted-Program fixture must be invented. | `tests/Antiphon.Tests/Application/DispatcherSweepLifetimeRegistrationTests.cs:14` is already NotInParallel and factory-backed; its sole method at `:23` checks singleton scope identity. `tests/Antiphon.Tests/TestHelpers/AntiphonWebAppFactory.cs:52` owns an isolated store; `:114` disables the runner, `:122` disables Hangfire workers, and `:134` installs the refusing runner client. | Add one method to the existing class. Preserve these guards and use no factory service overrides. |
| An internal probe would require widening assembly access. | `server/Antiphon.Server.csproj:12` already exposes internals to Antiphon.Tests; `AgentTaskDispatcher.cs:115` has the existing read-only `Db` test probe. | Add only a read-only boolean tuple beside `Db`; no public API or new friend assembly. |
| This work must revise settlement/recovery/approval contracts. | S2 retains advertised tips under lease contention (`RemoteWorkspaceService.cs:416`, `:465`); S3 evaluates Pending with local objects (`TaskCompletionProgressService.cs:187`); S4a binds settled sync to the recorded source (`RemoteWorkspaceService.cs:191`); S4b reads due debt (`SettlementSyncRecoveryService.cs:27`); S6 projects it (`AgentTaskService.cs:2571`, `SettlementSyncDebtAttention.cs:64`); S7 recovery still requires confirmed historical sync (`ReviewEvidenceRecoveryService.cs:61`). | These are dependencies inspected from the landed S1..S7 chain, not edit targets. Pending remains unconfirmed; evidence, verdicts, recovery, and approval remain governed by those existing contracts. |

## Decisions

### D-1. Keep the mirror guard test at the phone-home request boundary

Use three named `[Test]` methods in `PhoneHomeRollingRunnerTests`, instead of the earlier
plan's single three-argument method. Each behavior gets an exact checkpoint and an independent
mutation detector without argument-selection ambiguity. A small private helper in the same
file may build the task and capture the request, but it must take an explicit expected SHA;
it must not calculate the expectation using production's fallback expression.

Use distinct full values: base = 40 `a` characters, observed = 40 `b` characters,
confirmed = 40 `c` characters. Store evidence with `TaskProgressJson.SerializeEvidence`,
use the existing `RollingRunnerSettings` fixture identities, and omit the optional
`publishedSha` argument. Assert exactly one WorkspaceRemove on PeerA, none on PeerB,
the exact remote path, the expected PublishedSha, and null residue.

Rejected: exposing `PublishedShaForRemoval` solely for a unit test (misses serialization and
the capability/length gate); reflection; mocking the helper; testing identical SHAs; a real
runner deletion (the card asks what SHA the server sends, and the scripted boundary observes it).

### D-2. One necessary production edit: a read-only injection probe

Add beside `AgentTaskDispatcher.Db`:

```csharp
/// <summary>CARD-1128: reports the dependencies received by this dispatcher; tests only.</summary>
internal (bool BlockedTaskSync, bool SettlementSync) SyncDebtSweepsWired =>
    (_blockedTaskSync is not null, _settlementSync is not null);
```

This is the only planned production-file edit. It is needed to distinguish an injected
dependency from a service that is merely resolvable in the container. Reading booleans of
the existing fields has no runtime side effect, DB statement, scheduling, or public API cost.
The test must assert each member independently, with a message naming the missing service.
Resolve a dispatcher in each of two async factory scopes, assert both flags on each, and
resolve both sweep services in the same scopes **after** the flag assertions. Do not register
any of these services in the fixture. Do not run a whole tick or seed a debt row.

Rejected: registration-source string checks (a factory could ignore registered services);
private-field reflection (brittle runtime coupling); calling an empty sweep and asserting zero
(the missing-service fallback is also zero); a SQL interceptor/factory subclass to count empty
queries (more fixture wiring and startup-query discrimination than this injection guard needs);
making the optional arguments required (breaks existing harnesses and changes compatibility).

The probe directly reads the two fields; no cached constant or container lookup in its getter.
Code review must check this, and PCs must mutate production wiring, not the probe.

### D-3. Keep the scope to these two cards

No migration, schema/configuration changes, weakened assertions, timeout increases, retries,
new hosted jobs, or edits to the two sweep implementations. No session-wait behavior is added
or changed. The CARD-1083 release checklist is therefore not activated by these slices:
while parking is disabled, nothing automatically releases a session waiting for input and
there is no release deadline. A Reply itself does not release its seat. Preserve the policy
that CARD-0079 is the only automatic stop of a Working session.

This small plan does not certify the entire existing stop policy. The in-flight
CARD-1149/1150 plan (`20fdcc014`, D-7) separately records a boot-stall-policy conflict and
requires characterization before its Code work. Keep that prerequisite with its owner;
neither repair nor claim it tested in these two guards.

### D-4. Portable lane, live placement, two independent slices

Read `GET /api/runner-defaults` and `GET /api/session-runners` on 2026-10-07 at about 15:55 UTC
using the session API/token environment. Defaults revision was 2 with one global preference
and no kind overrides. The catalogue offered a non-draining, accepting Linux lane and a
Windows lane; another Linux entry was draining. This is an observation, not a pinned host.
Every checkpoint below names the **Linux / isolated PostgreSQL integration lane** in its Group.
The code is portable and no Windows-specific API is under test. Dispatch with no `-Runner`
and no `-Platform`; re-read live placement/capacity before dispatch. `-Platform Any` unpins
an existing OS constraint if needed; no fleet location is embedded in a command.

The two slices can land independently. S2 shares `AgentTaskDispatcher.cs` with in-flight
work and must wait for that file's active Code owner; do not dispatch colliding Code tasks.
The restart requirement is **none for either slice**: S1 is test-only; S2 adds an inert
test-observation member. The test build contains the probe; no live server activation is
needed to obtain this evidence. Normal later deployment can carry it.

### D-5. Fold verification design into this plan

The brief requests exact checkpoints, negative controls, and regression selection for these
small follow-ups. They are fully specified below, so the next stage is Code. Ordinary Code
runs the closed CP list; separate Review checks it; after confirmed land, a commissioned
SourceLanding Mutation task runs the five method-scoped PCs. Do not claim unexecuted PCs green.
This preserves the repository's Code -> Review -> Land -> Mutation order.

## Slices and collision map

| Slice | Authoring budget | Files to edit | Concrete result / tests | AppHost restart |
|---|---|---|---|---|
| S1: CARD-1125 fallback guards | 40 min | `tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerTests.cs` | Three named methods in V-1..V-3; retain all four existing tests; CP-1..CP-7. No RemoteWorkspaceService edit. | None |
| S2: CARD-1128 composition guard | 35 min | `server/Application/Services/AgentTaskDispatcher.cs` (probe beside `Db` only); `tests/Antiphon.Tests/Application/DispatcherSweepLifetimeRegistrationTests.cs` | Probe plus `Program_wires_both_sync_debt_sweeps_into_the_dispatcher`; existing singleton test retained; CP-8..CP-9. | None; inert probe only |

Commit and push each completed slice before its checkpoint run; keep source frozen until that
run finishes. If executed as separate tasks, each task names its subset of these After tokens.

| In-flight work named by caller | Shared files / dependency | Coordination |
|---|---|---|
| CARD-1082 F3d; Final Review `260e7059` | No planned edit overlap. Its `SettlementSyncRecoveryService.cs`, `SettlementSyncRecoveryTests.cs`, `PendingSettlementDeliveryTests.cs`, owner docs and `RunnerBranchContractDocumentationTests.cs` are outside both slices. The F3d footprint is visible in commit `8923c3b9d`; it also amends the older follow-up plan, which this task only reads. S2 resolves the recovery service but does not sweep it. | Keep the new plan separate. Preserve F3d's final semantics and evidence when it lands; do not copy the earlier plan's Held supersession design into this work. |
| CARD-1097 S4b (dispatcher warning telemetry) | **S2 shares `AgentTaskDispatcher.cs`.** The caller's `:6409` is in RemoteWarnAsync on inspected master; probe insertion is beside `:115`. S1's runtime subject also lives in RemoteWorkspaceService, which CARD-1097 may change, but S1 does not edit it. | Serialize S2 with active dispatcher Code. Preserve the queued-on-telemetry-failure fix visible at `3c9e10eff`; do not resolve conflicts by replacing the file. |
| CARD-1105 audit fix | `scripts/c590-remote.sh`: no overlap. | No ordering dependency. No rollout or shell-script edits here. |
| CARD-1149/1150 launch recovery plan | **S2 shares `AgentTaskDispatcher.cs`** with planned dead-session holds, brief production and watchdog arbitration. Plan commit `20fdcc014` also names AgentSessionService, SessionReconciliationService and queue files; neither slice edits those. | Serialize dispatcher Code and retain the two sync fields, registration and getter after integration. Run S2's rows against the integrated committed source if integration changes that file; keep D-3's safety prerequisite separate. |

The named task status comes from the dispatch brief, not a claim that it remained in flight at
settlement. Re-check live occupancy before Code. If an integration touches test names/rosters,
update this manifest explicitly rather than broadening a filter or quietly raising/lowering Min.

## Verification design

### Behaviors and independent oracles

| ID | Method in `tests/Antiphon.Tests/Application/` | Setup and observable assertion |
|---|---|---|
| V-1 | `PhoneHomeRollingRunnerTests.C1125_PendingRemovalUsesObservedSha` | Pending, ObservedSha=b, ConfirmedSha=null, base=a; the actual WorkspaceRemove request publishes b and successful removal has null residue. |
| V-2 | `PhoneHomeRollingRunnerTests.C1125_UnavailableRemovalUsesWorktreeBaseSha` | Unavailable with `RemoteSettlementSyncReasons.LeaseBusy`, ObservedSha=b, ConfirmedSha=null, base=a; publishes a. The reason is deliberately identical to Pending's reason, so only the state distinguishes them. |
| V-3 | `PhoneHomeRollingRunnerTests.C1125_PendingRemovalPrefersConfirmedSha` | Pending, ObservedSha=b, ConfirmedSha=c, base=a; publishes c. Full distinct SHAs make reordering visible. |
| V-4 | `DispatcherSweepLifetimeRegistrationTests.Program_wires_both_sync_debt_sweeps_into_the_dispatcher` | Real factory Program, two scopes, actual scoped dispatchers; each `BlockedTaskSync` and `SettlementSync` flag is true. Resolve each service after those assertions. No service replacement or manual dispatcher construction. |

All SHA letters above stand for the full 40-character values in D-1. The V-1..V-3 oracles
are deserialized peer requests, independent of helper internals. V-4's oracle is the dispatcher
instance, independent of which DI registration style Program uses. It does not prove execution
of all tick sweeps, source readiness, delivery, or automatic release.

### Regression selection

The closed CP list executes the complete two affected classes exactly once per slice, split
by method instead of repeating a class after its focused cases:

| ID | Class / existing tests | Why / results at inspected master |
|---|---|---|
| R-1 | `PhoneHomeRollingRunnerTests.Retirement_mirror_remove_uses_confirmed_sha_and_keeps_unpublished_tip_as_residue` | Existing synchronized precedence plus retained unpublished residue, 1 result. |
| R-2 | `PhoneHomeRollingRunnerTests.Settlement_publishes_unpushed_mirror_through_operation_32_and_runner_dispatcher` | Shared rolling fixture still supports real Git publish, 1 result; retain its ProcessSpawnLimit. |
| R-3 | `PhoneHomeRollingRunnerTests.Runner_without_publish_feature_never_receives_operation_32` | Existing capability refusal in the shared fixture, 1 result; retain its ProcessSpawnLimit. |
| R-4 | `PhoneHomeRollingRunnerTests.Input_to_a_session_on_server2_reaches_server2_while_a_new_launch_goes_to_server2_temp` | Existing routing witness for the two scripted peers, 1 result. Names here refer to fixture identities, not deployment placement. |
| R-5 | `DispatcherSweepLifetimeRegistrationTests.Program_registers_the_sweep_in_flight_state_as_one_singleton` | Existing singleton identity across scopes, 1 result. Retain class-level NotInParallel. |

S1 roster becomes 7 (4 existing + 3 new); S2 roster becomes 2 (1 existing + 1 new).
No argument expansion is planned. Total ordinary executions: **9**, all passed, zero failed or
skipped. Recovery/progress/seat-release classes are not rerun by this plan because neither their
behavior nor fixtures change. A necessary edit outside this footprint requires a stated scope
and checkpoint amendment; it is not covered by these nine results.

### Guard inventory and negative mutation controls

Each row is a compiling production mutation with one exact detecting method. The new tests
must go red on the stated assertion and green after exact restoration. Do not change test
expectations, probe getters, fixture replies, or failure thresholds to manufacture red.

| PC | Guard / production mutation (master location) | Exact method filter | Expected assertion failure |
|---|---|---|---|
| PC-1 | V-1: remove the Pending observed fallback at `RemoteWorkspaceService.cs:806`, leaving `sync?.ConfirmedSha ?? task.WorktreeBaseSha`. | `/*/*/PhoneHomeRollingRunnerTests/C1125_PendingRemovalUsesObservedSha*` | Actual PublishedSha=a instead of b. |
| PC-2 | V-2: replace the guarded expression at `RemoteWorkspaceService.cs:806` with `sync?.ObservedSha`, keeping confirmed first and base last. | `/*/*/PhoneHomeRollingRunnerTests/C1125_UnavailableRemovalUsesWorktreeBaseSha*` | Actual PublishedSha=b instead of a for Unavailable/lease-busy. |
| PC-3 | V-3: move the Pending observed expression before `sync?.ConfirmedSha` at `RemoteWorkspaceService.cs:805`. | `/*/*/PhoneHomeRollingRunnerTests/C1125_PendingRemovalPrefersConfirmedSha*` | Actual PublishedSha=b instead of c. |
| PC-4 | V-4 blocked dependency: temporarily remove only `builder.Services.AddScoped<BlockedTaskSyncRecoveryService>();` at `Program.cs:412`; keep the dispatcher and settlement registrations. The optional parameter resolves null. | `/*/*/DispatcherSweepLifetimeRegistrationTests/Program_wires_both_sync_debt_sweeps_into_the_dispatcher*` | The resolved dispatcher's `BlockedTaskSync.ShouldBeTrue` fails. |
| PC-5 | V-4 settlement dependency: restore PC-4, then temporarily remove only `builder.Services.AddScoped<SettlementSyncRecoveryService>();` at `Program.cs:413`. | `/*/*/DispatcherSweepLifetimeRegistrationTests/Program_wires_both_sync_debt_sweeps_into_the_dispatcher*` | The resolved dispatcher's `SettlementSync.ShouldBeTrue` fails. |

The two services have no other production constructor consumer on inspected master. Thus
PC-4/PC-5 allow Program to boot and dispatcher resolution to succeed with one null optional
dependency. The probe assertions run before direct GetRequiredService calls so a missing-service
exception is not the claimed red. If later DI changes prevent startup, report that as an invalid
PC and revise the compiling mutation to omit that argument in Program's dispatcher factory;
a boot/build/fixture failure never satisfies the guard.

Run each PC serially (PC-1..3 share a method/file; PC-4..5 share Program). Every baseline/red/green
phase uses the listed method filter with trailing wildcard, Min=1, and a fresh isolated output
and results path. Use the unchanged `scripts/run-checkpoint.ps1` driver, which takes a build
slot itself; use the SourceLanding external driver/evidence procedure in
`docs/testing-and-build.md` (Mutation-stage positive-control execution). Baseline and restored
green require exit 0 with one passed result; intended red requires exit 1 and that exact assertion.
Zero tests, skipped tests, fixture errors and timeouts are not red. Rebuild after restoration
with refreshed timestamps; inspect fresh TRX and build/run exit receipts. SourceLanding never
commits the mutation or evidence. Keep all five per-PC records and exact restoration evidence.

### Docs sentences with pins

No owner-doc sentence changes: these cards add coverage of existing contracts. The new plan
is the only documentation deliverable. The one XML comment beside the internal probe names
CARD-1128; V-4 pins its behavior, not the wording. No text-only test or doc assertion is needed.
Leave the older follow-up plan to its current owners, especially F3d.

### Checkpoints

Each row is one behavior and one exact method filter. Lane for every row: Linux, isolated
PostgreSQL integration. All rows are Serial=true, including the real Program class. Build
reuse is only within the same After token and certified committed source. CP-1 and CP-8 create
separate isolated outputs; each subsequent row reuses its slice's build. No whole-Unit,
namespace, or assembly run. A method's trailing `*` must select only that method's native
result; verify the executed roster, not just Min. The two CARD-1128 rows together supply the
card's requested class roster of two results.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1125-s1/` | linux-pg-pending-observed | `/*/*/PhoneHomeRollingRunnerTests/C1125_PendingRemovalUsesObservedSha*` | V-1 | exact 1 result, 0 failed/skipped | 1 | 6 | true |
| CP-2 | S1 | CP-1 | linux-pg-unavailable-base | `/*/*/PhoneHomeRollingRunnerTests/C1125_UnavailableRemovalUsesWorktreeBaseSha*` | V-2 | exact 1 result, 0 failed/skipped | 1 | 1 | true |
| CP-3 | S1 | CP-1 | linux-pg-confirmed-first | `/*/*/PhoneHomeRollingRunnerTests/C1125_PendingRemovalPrefersConfirmedSha*` | V-3 | exact 1 result, 0 failed/skipped | 1 | 1 | true |
| CP-4 | S1 | CP-1 | linux-pg-removal-residue | `/*/*/PhoneHomeRollingRunnerTests/Retirement_mirror_remove_uses_confirmed_sha_and_keeps_unpublished_tip_as_residue*` | R-1 | exact 1 result, 0 failed/skipped | 1 | 1 | true |
| CP-5 | S1 | CP-1 | linux-pg-mirror-publish | `/*/*/PhoneHomeRollingRunnerTests/Settlement_publishes_unpushed_mirror_through_operation_32_and_runner_dispatcher*` | R-2 | exact 1 result, 0 failed/skipped | 1 | 2 | true |
| CP-6 | S1 | CP-1 | linux-pg-publish-capability | `/*/*/PhoneHomeRollingRunnerTests/Runner_without_publish_feature_never_receives_operation_32*` | R-3 | exact 1 result, 0 failed/skipped | 1 | 2 | true |
| CP-7 | S1 | CP-1 | linux-pg-peer-routing | `/*/*/PhoneHomeRollingRunnerTests/Input_to_a_session_on_server2_reaches_server2_while_a_new_launch_goes_to_server2_temp*` | R-4 | exact 1 result, 0 failed/skipped | 1 | 2 | true |
| CP-8 | S2 | `tests/Antiphon.Tests -> bin-c1128-s2/` | linux-pg-sweep-injection | `/*/*/DispatcherSweepLifetimeRegistrationTests/Program_wires_both_sync_debt_sweeps_into_the_dispatcher*` | V-4 | exact 1 result, 0 failed/skipped | 1 | 6 | true |
| CP-9 | S2 | CP-8 | linux-pg-sweep-singleton | `/*/*/DispatcherSweepLifetimeRegistrationTests/Program_registers_the_sweep_in_flight_state_as_one_singleton*` | R-5 | exact 1 result, 0 failed/skipped | 1 | 2 | true |

## Execution, cost, and delivery

Ordinary Code V/R floor: **23 minutes** (6+1+1+1+2+2+2+6+2).
Authoring: 40+35=75 minutes; Code dispatch estimates: S1 about 55 minutes and S2 about
43 minutes, plus any separately reported tool bootstrap/slot wait. Post-land PCs: about
30-45 minutes for five short method-scoped baseline/red/green cycles; not part of ordinary V/R.

Read the testing/build owner before execution. Bootstrap the checkpoint driver once through
the build-slot gate, using a producer-owned alternate output path; this tool build is the
declared driver bootstrap, not an extra application verification run:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1125-c1128-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1125-driver/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1125-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-07-card-1125-1128-test-hardening-plan.md --after S1 --expected-source-sha "$(git rev-parse HEAD)" --max-wait 45s
```

Run once per committed slice group, substituting `--after S2` when S2 is committed. `S1` and
`S2` are literal After tokens; use `--rows CP-8,CP-9` for S2-only verification if needed.
On exit 75 continue `wait --run <run-id> --max-wait 45s` until the executor has a terminal
result; never settle a still-running checkpoint. Exit 4 is a slot timeout: report not run,
never bypass the gate. No source edits during a run. A failure-driven rerun names its CP,
reason and new committed SHA. Verify inherited failures with the exact failing method at
the recorded base, not a broad suite; do not weaken the assertion or add a retry.

Store unedited CHECKPOINT lines, commit/build provenance, counts and any failures in the task
report. Require clean strict-source receipts for ordinary green. Code and Review run
`scripts/check-evidence-diff.ps1` over their full task range. Keep generated receipts, logs,
TRX and imports ignored. Remove only these runs' producer-owned alternate outputs (including
driver outputs in referenced projects); the checkpoint tool cleans green row outputs.

This Plan task validates the Markdown checkpoint import without running future tests or PCs.
Its only assigned file is this plan. Commit and push to `feat/card-task-bd8d6f42`, then compare
`git ls-remote origin refs/heads/feat/card-task-bd8d6f42` to HEAD before reporting completion.
No rebase, amend, reset, or force-push of this task branch. If push reports
`fatal error in commit_refs`, retry at most five times, two minutes apart, keeping the turn
alive with bounded foreground waits. A publication failure is reported explicitly.

--- next stage ---
next: code
handoff: Implement S1 and S2 separately using CP-1..CP-9 as the closed verification list; serialize S2 with dispatcher Code owners, preserve all existing assertions, and leave PC-1..PC-5 for commissioned post-land Mutation.
artifact: docs/superpowers/plans/2026-10-07-card-1125-1128-test-hardening-plan.md
