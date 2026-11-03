# CARD-0824 S1c role allow-list for the slot canary: test design

TestDesign task 2bc285a9, 2026-11-03, server2 runner mirror (Linux, uid 1654), base master
`c2f1f83a30b324af81149918e8a6360565b95344`. Scope: slice S1c of the
[S4/S5 amendment](https://github.com/michal-ciechan/Antiphon/blob/ffc8f8fc1/docs/superpowers/plans/2026-11-03-card-0824-s4-s5-amendment.md)
(`origin/feat/card-task-d94b0c85` at `ffc8f8fc1`: §4.1 S1c, §4.2-§4.5 canary and rollback, A-D-3), over
the [S1 plan](2026-11-02-card-0824-per-slot-worktrees-plan.md) and the
[S1 test design](2026-11-02-card-0824-S1-test-design.md). No production or test source changed. The
expected results below are design, not executed evidence, except under "Measured facts".

This design keeps A-D-3's intent: a role-scoped canary, a soft rollback in which new leases stop while
releases continue, Mutation and SourceLanding never lease, and hot reload. It **corrects A-D-3's
representation and adds three requirements**, because the measured facts below show that the
amendment's `string[]` setting would invert the rollback (T-D-1..T-D-6). Each correction is a stated
default under the standing authority (the A-D defaults apply; the facts are not a preference). The caller
can override any of them before Code.

## Measured facts used by this design (server2, net9.0 runtime 9.0.22, one build-slot lease each)

Three scratch console probes ran under `scripts/build-slot.ps1` (waits 60-105 s, held 5-27 s, load 5-18).
The probes are outside the repo. They are `Microsoft.Extensions.Configuration` JSON and environment
providers, the binder, `AddOptions().Bind()`, `IValidateOptions` and `IOptionsMonitor` with
`reloadOnChange: true`: the same composition `WebApplication.CreateBuilder` gives `server/Program.cs:68`.

- **A `string[]?` setting cannot carry the soft rollback.**

  | Value | Result |
  |---|---|
  | `"WorkspaceSlotRoles": []` | binds to **`null`** |
  | `null` | `null` |
  | absent | `null` |
  | env `Delegation__WorkspaceSlotRoles=` (empty) | `null` |

  Under A-D-3, `null` means **every role**. A hot reload from `["Plan"]` to `[]` therefore gave `null`
  (changed=true). So A-D-3's soft rollback `[]` turns the canary into all roles.
- **Arrays merge by index across layered files.** A base `["Plan","Code"]` plus an override `[]` stays
  `["Plan","Code"]`. A base `["Plan","Code"]` plus an override `["Docs"]` gives `["Docs","Code"]`. So
  narrowing the list in a later file (`appsettings.Development.json` over `appsettings.json`) silently
  keeps the earlier entries.
- **A scalar `string` binds predictably.** The property defaults to `"*"`.

  | Value | Result |
  |---|---|
  | absent | `"*"` (the default) |
  | `"none"` | `"none"` |
  | `""` | `""` |
  | JSON `null` | `""` |
  | layered `"Plan,Code"` then `"none"` | `"none"` (whole value replaced) |
  | env `Delegation__WorkspaceSlotRoles=` | `""` (the variable is visible) |

  **But an array written into the string property is silently ignored.** `[]` and `["Plan"]` both bind
  to the default `"*"`.
- **What configuration shows for those shapes.** Each row is `Delegation`'s child `WorkspaceSlotRoles`.

  | JSON | `Value` | Grandchildren | `Exists()` |
  |---|---|---|---|
  | `[]` | `null` | 0 | **false** |
  | `{}` | `null` | 0 | false |
  | `["Plan"]` | `null` | 1 | true |
  | `null` | `""` | 0 | true |
  | `""` | `""` | 0 | true |
  | absent | (no child) | | |

  So only enumerating `GetSection("Delegation").GetChildren()` can refuse `[]` and `{}`.
  `GetSection(...).Exists()` reports `[]` as absent.
- **An invalid hot reload makes `IOptionsMonitor.CurrentValue` throw.** The thrown exception is
  `OptionsValidationException`, on every read until the file is fixed, and the `OnChange` listener does
  not fire. After the fix, the next read returns the new value.
  - `IOptions<T>.Value` (the singleton) keeps its startup value throughout.
  - So any invalid edit of **any** `Delegation:*` key in a reloadable file makes a monitor read throw,
    because `DelegationSettingsValidator` validates the whole object.
- **Where the dispatching server's configuration comes from.**
  - The desktop AppHost pins `ASPNETCORE_ENVIRONMENT=Development` (`Antiphon.AppHost/Program.cs:72`).
    `server/appsettings*.json` other than `appsettings.json` are gitignored (`.gitignore:5`). So
    `appsettings.Development.json` in the server content root is the operator's reloadable override.
  - Environment variables are not reloaded, and they override JSON (provider order). A value set
    through `Delegation__WorkspaceSlotRoles` therefore makes a file edit a silent no-op.
  - The Docker server sets `Delegation__*` through `docker-compose.yml:39-43`.
- **Code facts at the base** (read, not run):
  - The lease decision is `_workspaceSlots && binding is not null && runner advertises workspaceSlotV1`
    (`RemoteWorkspaceService.cs:216-221`). `_workspaceSlots` is read once from `IOptions` in the
    constructor (`:67`). `RemoteWorkspaceService` is scoped (`Program.cs:349`), and the preparer makes
    one scope per run (`RemoteWorkspacePreparer.cs:237-239`).
  - A lost reply is replayed by `PreparationId` (`RunnerWorkspaceSlotPool.cs:85-90`). A slot is set
    Leased only as the last step of `LeaseAsync`, so every 409 refusal leaves no Leased record for that
    preparation (`:91-147`).
  - The reconciler loop reads only `WorkspaceSlotsEnabled` (`WorkspaceSlotReleaseService.cs:275`).
  - SourceLanding never enters remote prep (`AgentTaskDispatcher.cs:6149`,
    `claimed.SourceLandingOperationId is null`). The existing witness is
    `DefaultRunnerPinTests/SourceLanding_mutation_reaches_runner_launch_with_binding`, which asserts
    `RequestCount(WorkspaceMirror) == 0` (`:310`).
  - Role names elsewhere in `DelegationSettingsValidator` are case-insensitive (`:1430-1435`).
  - `AgentTaskEventType` is stored as int, with no check constraint (`AppDbContext.cs:2498-2503`). Its
    last value is `DecisionQuestion = 40`.
  - The S1 test `C0824_SlotUnavailableHoldsWithoutFallback` asserts exactly 3 `Held` events and **zero
    `Warning`** events for a slot-held task (`C0824WorkspaceSlotServerTests.cs:145-150`).

## Decisions taken by this design (T-D-n; defaults, each overridable)

- **T-D-1 Representation: a single string, not `string[]`.**
  - `Delegation:WorkspaceSlotRoles` is a `string`, default `"*"`. The grammar is exactly one of:
    - `*`: every role except Mutation. This is S1's semantics plus A-D-3's Mutation exclusion.
    - `none`: no new leases.
    - A comma-separated list of `AgentTaskRole` names.
  - Names match `Enum.GetNames<AgentTaskRole>()` **ordinally, in canonical case, untrimmed**, as the
    brief asks ("exact match").
  - The validator refuses:
    - empty entries: `""`, `"Plan,,Docs"`, `"Plan,"`;
    - any whitespace;
    - other case (`plan`, `None`);
    - numbers (`1`);
    - unknown names (`Planner`, `all`);
    - `Mutation`;
    - `*` or `none` mixed with names.

    Duplicates are accepted.
  - The canary value becomes `"Plan,Investigate,TestDesign,Docs"` and the soft rollback becomes `"none"`.
    The amendment's §4.3 and §4.5 syntax is replaced, and the S1c docs edit says so.
  - This departs from the amendment's R-6 (case-insensitive, unknown only a warning). The brief wins
    here. Q-1 below offers case-insensitive matching.
- **T-D-2 Configuration-shape validator.** A second `IValidateOptions<DelegationSettings>` takes
  `IConfiguration` and refuses `Delegation:WorkspaceSlotRoles` when it is present as a child of
  `Delegation` and either has children or has a `null` value. It must enumerate the children, because
  `Exists()` misses `[]` (measured above).
  - Program's four registration lines (`Program.cs:183-186`) move into one extension,
    `AddDelegationSettings(this IServiceCollection, IConfiguration)`. Program and the tests call the same
    code.
- **T-D-3 The decision reads the monitor at decision time, and an invalid reload fails closed.**
  - A small `WorkspaceSlotRoleGate(IOptionsMonitor<DelegationSettings>, ILogger)` reads
    `CurrentValue.WorkspaceSlotRoles` on every `Evaluate(AgentTaskRole role, Guid? sourceLandingOperationId)`.
    It returns `(bool Lease, string Reason)`. The reasons are `all`, `listed`, `role_not_listed`,
    `roles_none`, `roles_invalid`, `mutation` and `source_landing`.
  - `OptionsValidationException` and an unparsable value both give
    `(false, roles_invalid)`, plus one `Warning` log naming `Delegation:WorkspaceSlotRoles`. The gate
    never throws.
  - `WorkspaceSlotsEnabled` stays on `IOptions`: restart-only, the hard rollback, unchanged.
  - The gate reads **only** the role value from the monitor. In a container where the monitor has no
    configuration bound, it resolves to defaults (`"*"`, flag false). Reading the flag there would turn
    S1 off.
- **T-D-4 The decision is sticky per preparation** (missing from the amendment). Here is the hazard
  at the base plus S1c:
  1. A lease reply is lost: a timeout, which is S-5's own stop signal.
  2. The operator soft-rolls back to `none`.
  3. The preparer resends the **same preparation** in the legacy shape. The runner creates
     `task-<id8>`, while its ledger keeps a slot Leased for that `(task, preparation)` that no server
     row names.
  4. The reconciler never releases it, because it selects only rows with `RemoteWorkspaceSlotId`. The
     slot is stranded, invisible until S4b.

  The reverse flip (a legacy reply lost, then the role is listed) leaves a `task-<id8>` beside a slot.

  The rule:
  - The first decision for a preparation is persisted before its first send, and reused for every
    resend of that preparation.
  - A **definitive runner refusal** of that send clears it, so the next send decides again. A
    definitive refusal is an Error frame with `StatusCode 409`; a 409 never leaves a Leased record.
  - A transport failure, a timeout or a malformed Result frame keeps it.
- **T-D-5 Persistence for T-D-4: one new event type, no migration.**
  - The new type is `AgentTaskEventType.WorkspaceSlotDecision = 41`, appended. Its `Detail` is
    `workspace_slot_decision: slot|legacy preparation=<D> reason=<reason>`, or
    `workspace_slot_decision: cleared preparation=<D> refusal=<code>`.
  - It is written in the preparer's DB scope and saved before the mirror request is sent.
  - `Warning` and `Held` are excluded: the S1 witness above counts both.
  - It is written only when the flag is on, the binding is present and the runner advertises
    `workspaceSlotV1`, so flag-off handling stays byte-identical.
  - The event is also the canary's per-task receipt. §4.3 already reads `workspace_slot_*` task events.
- **T-D-6 The SourceLanding belt.** The gate refuses `sourceLandingOperationId != null` whatever the
  value. This is independent of the dispatcher guard at `:6149`, which stays the primary D-8 guard.

Not decided here, by design: the flag-off hard rollback still strands a lease whose reply was lost
across a restart. That is S1's existing behaviour ("flag off still stops everything as today"); see
§Out of scope.

## Verification design

### Inspection

- **Read:**
  - `RemoteWorkspaceService.cs`: the constructor `:44-68`, `BeginMirrorPreparationAsync :140-177`,
    `MirrorAsync :184-224` and `ReleaseSlotAsync :227-229`.
  - `RemoteWorkspacePreparer.cs:232-360`.
  - `WorkspaceSlotReleaseService.cs:30-100` and `:255-292`.
  - `DelegationSettings.cs:697-703` and the validator `:1293-1440`.
  - `Program.cs:180-190`, `:236-238` and `:349-351`.
  - `AgentTaskDispatcher.cs:6135-6165`.
  - `RunnerWorkspaceService.cs:105-121` and `RunnerWorkspaceService.Slots.cs`.
  - `RunnerWorkspaceSlotPool.cs:80-147`.
  - `WorkspaceSlotContracts.cs:4-30`.
  - `AgentTaskEnums.cs` (roles and event types).
  - `AppDbContext.cs:2498-2513`.
  - Tests: `C0824SlotTransportWorld.cs` (all), `C0824WorkspaceSlotServerTests.cs` (all 14 methods),
    `RemoteWorkspacePreparerTests.Rig` `:1155-1415`, `RolePolicyConfigurationValidationTests.cs`,
    `DefaultRunnerPinTests.cs:262-312`, and the `C1677TempSweepServerTests.Monitor` stub pattern.
- **Boundaries:**

  | Boundary | IDs |
  |---|---|
  | config to bound value | V-5, V-6, R-3 |
  | bound value to validator | V-4, V-5 |
  | monitor to gate | V-7, V-8, V-14 |
  | gate to mirror request shape | V-10, V-11, V-17 |
  | request to runner ledger and tree | V-10, V-11, V-15 |
  | decision to durable event | V-15, V-16 |
  | ledger to reconciler release | V-12, V-13, V-18 |
  | dispatcher to SourceLanding exclusion | R-2 |
  | flag | V-17, V-18, R-1 |
  | runner `workspaceSlotV1` | V-17("runner-lacks-feature"), R-1 |

  Excluded: the runner side (unchanged by S1c) and the client (no new view).

### Seams (missing at the base; part of Code)

- **S-1** `WorkspaceSlotRoles` (a static parser: `TryParse(string?, out WorkspaceSlotRoleSet, out
  string error)`) and `WorkspaceSlotRoleSet.Admits(AgentTaskRole, Guid?)`. The validator and the gate
  both use them.
- **S-2** `AddDelegationSettings(IServiceCollection, IConfiguration)`. It holds the existing validator
  and the T-D-2 shape validator, and binds `Delegation` with `ValidateOnStart`. `Program.cs` calls it.
- **S-3** `WorkspaceSlotRoleGate` (T-D-3), registered as a singleton.
  `RemoteWorkspaceService` takes it through a new **optional trailing** constructor parameter. When it
  is absent, the service builds one over the container's `IOptionsMonitor<DelegationSettings>`, or
  over `"*"` with no monitor, so every existing positional constructor call compiles. It exposes
  `WorkspaceSlotDecision DecideSlot(AgentTask task)`, which covers the flag, the gate and
  SourceLanding; the runner capability check stays in `MirrorAsync`.
- **S-4** The decision record (T-D-4/T-D-5). The preparer calls
  `RemoteWorkspaceService.ResolveSlotDecisionAsync(db, task, expectation, ct)` after
  `BeginMirrorPreparationAsync` and before `MirrorAsync`. It returns the recorded decision for the
  preparation, or decides, saves the event and returns the new decision. `MirrorAsync` takes the
  decision as a parameter. On a 409 Error reply the preparer writes the `cleared` event in its existing
  failure branch.
- **S-5** Test seams:
  - `C0824SlotTransportWorld.StartAsync(Action<DelegationSettings>? tweak = null,
    Action<IServiceCollection>? configure = null)` passes both to `Rig.StartAsync`, keeping the current
    flag-on default.
  - A test `MutableMonitor<DelegationSettings>` (the `C1677TempSweepServerTests.Monitor` pattern) whose
    `CurrentValue` is settable and can be told to throw `OptionsValidationException`.
  - A recording `ILogger<WorkspaceSlotRoleGate>`.

  No member of the world or the `Rig` changes behaviour. `C1677RetentionTransportWorld` uses only the
  static `Git` helper.

### Fixtures and binding conventions

- **Class ST `C0824WorkspaceSlotRoleSettingsTests`** (`[Category("Unit")]`, no DB, no rig).
  - Pure parser and gate cases.
  - Binding cases build `ConfigurationBuilder().AddJsonFile(<temp file>, optional: false,
    reloadOnChange: <case>)` and call **S-2**. They resolve `IOptions` or `IOptionsMonitor` from the
    resulting provider.
  - Temp files go under `Path.GetTempPath()/c0824-s1c-<guid>/`, deleted in `finally`.
  - Reload waits poll `gate.Evaluate` every 100 ms, up to 10 s (the 250 ms reload debounce plus
    inotify latency).
- **Class RG `C0824WorkspaceSlotRoleGateTests`** (`[Category("Integration")]`, `[Timeout(35_000)]`, the
  C0824 server-class budget).
  - Each test builds its own world, and the class runs in parallel (the CARD-1660 R5 pattern).
  - Real-runner cases use `C0824SlotTransportWorld`, whose recipient evidence is the runner ledger
    (`world.Pool.Ledger()`) and the on-disk tree under `world.Work + "/worktrees"`.
  - Scripted cases use `Rig` with `SlotRunner()` capabilities.
  - The monitor is injected through `configure: s => s.AddSingleton<IOptionsMonitor<DelegationSettings>>(monitor)`
    (closed registrations win over the open generic).
- **"Legacy shape"** means the S1 CP-5 comparer: `PropertyNames(request).SetEquals(["branch","sha","name","repository","binding"])`.
  The request has no `slot` and no `cold` property, and `Name == MirrorName(taskId)`.
- **"Decision events"** are `AgentTaskEvents` with `Type == WorkspaceSlotDecision` for the task,
  ordered by `At`. **"Leased for"** a task is `world.Pool.Ledger().Slots.Where(s => s.State == Leased && s.TaskId == id)`.
- Seeding a role: `world.SeedAsync()` plus `ExecuteUpdateAsync(t => t.Role = role)` before the first
  tick, or `Rig.SeedAsync(t => t.Role = role)`. An "eligible terminal" task gets `Status = Succeeded`
  and `CompletedAt = now`, with no session and no debt, as `C0824_ReleaseRoundTripRealRunner` does.

### Delivery inventory

Each path is joined by its durable identity.

**P-A: the mirror lease decision and request** (changed). The durable identity is
`(TaskId, PreparationId)` → `(SlotId, Generation)`.

- **Producer:** `RemoteWorkspacePreparer.RunAsync` → `ResolveSlotDecisionAsync` (S-4) → `MirrorAsync`.
- **Destination:** the runner `WorkspaceMirror` op → `RunnerWorkspaceService.MirrorAsync`, which goes to
  `MirrorSlotAsync` / `pool.LeaseAsync` or to `MirrorBoundAsync`.
- **Persistence boundaries:**
  1. the `WorkspaceSlotDecision` event, saved **before** the send;
  2. the runner ledger `Leasing` intent, then `Leased(task, preparation, generation)`;
  3. the server row `RemoteWorktreePath`, `RemoteWorkspaceSlotId` and `RemoteWorkspaceSlotGeneration`.
- **Recovery:**
  - a server restart or retry resends the same preparation with the recorded decision;
  - a lost reply is replayed by `PreparationId`;
  - a definitive 409 refusal clears the decision.
- **Observable receipt:** the runner ledger, the on-disk `HEAD` and symbolic-ref (slot), or the
  `task-<id8>` tree (legacy), each agreeing with the server row.
- **Producer-to-recipient tests through the real phone-home socket:**
  - V-10 (listed);
  - V-11 (unlisted, `none`, `invalid`, Mutation);
  - V-15 (`busy-then-none`: a busy recipient replies after the flip; `slot-then-none`: crash or lost
    reply at the reply handoff; `legacy-then-listed`; `slot-refused-then-none`);
  - V-16 (the decision-saved-before-send handoff).

  Already-eligible case: a task that is Queued and eligible at the moment of the flip is V-14's second
  task.

**P-B: the slot release** (unchanged code, which must keep running). The durable identity is
`(SlotId, Generation, TaskId)`.

- **Producer:** `WorkspaceSlotReleaseHostedService` → `WorkspaceSlotReleaseService.ReconcileAsync`.
- **Destination:** the runner `WorkspaceSlotRelease` op.
- **Persistence:** the ledger `Releasing`, then `Free`; the server `RemoteWorkspaceSlotReleasedAt`.
- **Recovery:** the next pass resends (S1 V-43).
- **Receipt:** ledger `Free` with a detached `HEAD` at the published SHA, and the row stamped.
- **Tests:** V-12 (`none`: one already eligible, through both the hosted loop and a direct pass),
  V-13 (a role removed from the list), V-18 (flag off: no release frame).

**P-C: the configuration change** (new). The durable identity is the configuration value as read by
the next `Evaluate`.

- **Producer:** an operator edits the reloadable JSON file.
- **Destination:** `WorkspaceSlotRoleGate`.
- **Persistence:** the file.
- **Recovery:** an invalid edit fails closed until it is fixed (V-7).
- **Receipt:** the next task's `WorkspaceSlotDecision` event names the reason (`roles_none`,
  `roles_invalid`, ...).
- **Tests:** V-14 (a file-backed monitor in the rig: edit → next lease) and V-7.

**Substitutes, and what each cannot prove:**
- The scripted peer (`Rig`) in V-17 and V-18 cannot prove the runner's behaviour. Those rows assert only
  the request shape and the absence of frames, which is all they claim.
- The `MutableMonitor` in V-8, V-11 and V-15 cannot prove the file → monitor path. V-7 and V-14 prove it
  with a real reloading JSON file.
- No test proves the environment-variable shadowing in production. It is a documentation obligation
  (§Out of scope).

There is no session input path: no transcript evidence applies.

### Proves it works now

ST = `C0824WorkspaceSlotRoleSettingsTests`. RG = `C0824WorkspaceSlotRoleGateTests`.

- **V-1** ST `Default_admits_every_role_except_mutation`. This is the canonical-default oracle.
  - `new DelegationSettings().WorkspaceSlotRoles == "*"`.
  - Over `Enum.GetValues<AgentTaskRole>()`, the admitted set under `"*"` equals all values minus
    `Mutation`.
  - The same holds when bound through S-2 from `{"Delegation":{}}`. Each refused role carries the reason
    `mutation`; each admitted role carries `all`.
- **V-2** ST `None_admits_no_role`. Under `"none"`, no `AgentTaskRole` is admitted, and the reason is
  `roles_none` for each.
- **V-3** ST `Listed_admits_exactly_the_listed_roles(string value, string expected)`, 3 cases:
  - `("Plan","Plan")`;
  - `("Plan,Investigate,TestDesign,Docs","Plan,Investigate,TestDesign,Docs")`;
  - `("Code,Code","Code")`.

  The admitted set over all enum values equals `expected` exactly, and every other role carries
  `role_not_listed`.
- **V-4** ST `Invalid_values_are_refused(string value)`, 14 cases:
  - empty entries: `""`, `"Plan,,Docs"`, `"Plan,"`;
  - case: `"plan"`, `"None"`;
  - whitespace: `" Plan"`, `"Plan "`, `"Plan, Docs"`;
  - unknown names and numbers: `"1"`, `"Planner"`, `"all"`;
  - `"Mutation"`;
  - mixed tokens: `"*,Plan"`, `"none,Plan"`.

  For each case:
  - `new DelegationSettingsValidator().Validate(null, settings)` fails with a message that contains
    `Delegation:WorkspaceSlotRoles` and the literal list of canonical names;
  - `WorkspaceSlotRoles.TryParse` returns false.

  Oracle: the T-D-1 grammar. Reachability: each value differs from a valid one by exactly the rule it
  breaks.
- **V-5** ST `Bound_through_the_production_registration(string json, string outcome)`, 7 cases, through
  S-2 and `IOptions<DelegationSettings>.Value`:

  | JSON (`Delegation:WorkspaceSlotRoles`) | Outcome |
  |---|---|
  | absent | `*` |
  | `"none"` | `none` |
  | `"Plan,Docs"` | `Plan,Docs` |
  | `[]` | refused |
  | `["Plan"]` | refused |
  | `null` | refused |
  | `""` | refused |

  A refused case throws `OptionsValidationException`, whose failures name `Delegation:WorkspaceSlotRoles`.
  The `[]` and `["Plan"]` refusals come from the shape validator; the `null` and `""` refusals come from
  the grammar. This is the soft-rollback-cannot-mean-all oracle: the measured `[]` → `"*"`.
- **V-7** ST `Invalid_reload_fails_closed_then_recovers`. It uses a file-backed monitor
  (`reloadOnChange: true`) through S-2 and a gate with a recording logger.
  1. With `"Plan"`: `Evaluate(Plan)` is `(true, listed)`.
  2. Write `"plan"` and poll until the reason is `roles_invalid`. Then `Lease == false`,
     `Evaluate` does not throw, and exactly one `Warning` log contains `Delegation:WorkspaceSlotRoles`.
  3. Write `"none"` and poll: `(false, roles_none)`.
  4. Write `"Plan"` and poll: `(true, listed)`.

  Each poll ends within 10 s or the test fails with the last reason.
- **V-8** ST `Gate_reads_the_monitor_at_each_call`. One gate instance over a `MutableMonitor`:
  1. `"Plan"`: `Evaluate(Plan).Lease` is true.
  2. Set `"Docs"`: `Evaluate(Plan)` is `(false, role_not_listed)` and `Evaluate(Docs).Lease` is true.

  No new gate is constructed between the reads.
- **V-9** ST `Mutation_and_source_landing_are_never_admitted(string value)`, 2 cases: `"*"` and
  `"Plan,Docs"`.
  - `Evaluate(Mutation, null)` is `(false, mutation)`.
  - `Evaluate(Plan, Guid.NewGuid())` is `(false, source_landing)`, even though Plan is listed or
    covered by `*`.
- **V-10** RG `Listed_role_leases_a_slot_on_the_real_runner`. A world with roles `"Plan"` and a Plan
  task.
  1. Tick, then wait for the preparer to be idle.
  2. The request has `slot == true`.
  3. The ledger has exactly one slot Leased for the task, with
     `PreparationId == request.Binding.PreparationId`.
  4. `git rev-parse HEAD` in the slot equals `world.Base`, and `symbolic-ref HEAD` is the owned branch.
  5. The row has `RemoteWorkspaceSlotId == slot.Id` and `RemoteWorkspaceSlotGeneration == slot.LeaseGeneration`.
  6. There is one decision event, `slot preparation=<P> reason=listed`.
- **V-11** RG `Unlisted_role_gets_the_legacy_mirror_and_never_holds(string mode)`, 4 cases:

  | Mode | Roles | Task |
  |---|---|---|
  | `"role-not-listed"` | `"Plan"` | Code task, `WorkspaceColdReset = true` |
  | `"none"` | `"none"` | Plan task |
  | `"invalid"` | the monitor throws `OptionsValidationException` | Plan task |
  | `"mutation"` | `"*"` | Mutation task with no SourceLanding |

  Every case uses the real runner and asserts:
  - legacy shape, so `cold` is absent too;
  - `<Work>/worktrees/task-<id8>` exists on the owned branch at `world.Base`;
  - the ledger has **no** slot Leased for the task;
  - `RemoteWorktreePath` equals that path and `RemoteWorkspaceSlotId` is null;
  - status is not Queued-with-hold: zero `Held` events and zero events containing
    `workspace_slot_unavailable`;
  - one decision event `legacy ... reason=<role_not_listed|roles_none|roles_invalid|mutation>`.

  This is the "never blocks" oracle.
- **V-12** RG `None_stops_new_leases_while_releases_continue(string pass)`, 2 cases:
  `"reconcile-pass"` and `"hosted-loop"`. The world starts with roles `"Plan"`.
  1. Plan task A is leased (as V-10).
  2. Set roles `"none"`.
  3. Plan task B is ticked: legacy shape, `task-<id8B>` created, no new ledger slot.
  4. Make A an eligible terminal task.
  5. Release A:
     - `"reconcile-pass"`: `ReconcileAsync` in a rig scope returns 1;
     - `"hosted-loop"`: `new WorkspaceSlotReleaseHostedService(scopes,
       Options.Create(flag on, roles "none"), rig.Clock, logger)` is started. The test waits up to 10 s
       for A's `RemoteWorkspaceSlotReleasedAt`, then stops the service.
  6. Recipient: A's slot is `Free` in the ledger, `HEAD` is detached at the published SHA, A's row has
     `RemoteWorktreePath == null` and keeps its slot id and generation.
- **V-13** RG `Removed_role_slot_is_released_normally`.
  1. With roles `"Plan,Docs"`, a Plan task is leased.
  2. Set roles `"Docs"`.
  3. Make the task an eligible terminal task.
  4. `ReconcileAsync` returns 1: the ledger slot is Free and the row is stamped.
- **V-14** RG `Hot_reload_applies_to_the_next_lease_without_restart`. The world's monitor is
  **file-backed**: S-2 over a temp JSON with `reloadOnChange: true`, registered through `configure`.
  1. With `"Plan"`: Plan task 1 gets a slot (`slot == true`, ledger Leased).
  2. Write `"none"`, and poll `world.Rig.Provider.GetRequiredService<WorkspaceSlotRoleGate>()` until
     the reason is `roles_none`. Plan task 2 gets the legacy shape and a `task-<id8>`.
  3. Write `"Plan"` and poll. Plan task 3 gets a slot.

  It is one provider and one process: no rig restart.
- **V-15** RG `Retry_keeps_the_first_decision_of_its_preparation(string flip)`, 4 cases. Each uses the
  real runner and the V-42 interceptor pattern, and ends with `AdvanceAsync(1 h)`, a tick and waiting
  for the preparer to be idle.
  - **`"slot-then-none"`.** Roles `"Plan"`, a Plan task.
    - The first mirror frame is delivered to the runner, so the lease is taken; the reply is replaced
      by `{}` (lost).
    - Set roles `"none"`, then retry. The resend has `slot == true` and the same `PreparationId`.
    - End state: exactly one slot Leased for the task; the row's `RemoteWorkspaceSlotId` equals it;
      **no** `task-<id8>` directory; exactly one decision event (`slot`).
  - **`"legacy-then-listed"`.** Roles `"Docs"`, a Plan task.
    - The first legacy frame is delivered and its reply lost.
    - Set roles `"Plan"`, then retry. The resend has the legacy shape.
    - End state: no slot Leased for the task; the row path is `task-<id8>`; one decision event (`legacy`).
  - **`"busy-then-none"`.** Roles `"Plan"`, a Plan task.
    - `SilentFor(WorkspaceMirror)`. Tick, and capture the request.
    - Set roles `"none"`. Tick twice: no second request.
    - Deliver the real runner reply.
    - End state: the row is bound to the ledger's Leased slot, and the ledger has one slot.
  - **`"slot-refused-then-none"`.** Roles `"Plan"`, a Plan task.
    - The first slot frame is answered with the S1 V-13 Error frame (`StatusCode 409`,
      `UnavailableDetail("overflow_cap")`).
    - Set roles `"none"`, then retry. The resend has the legacy shape.
    - End state: the task has a `task-<id8>` mirror and is not Queued-held. The decision events, in
      order, are `slot`, `cleared ... refusal=workspace_slot_unavailable`, then `legacy reason=roles_none`.
  - Oracle for every case: the server row and the runner ledger agree, and no task holds both a slot and
    a `task-<id8>`.
- **V-16** RG `Decision_is_recorded_before_the_mirror_is_sent`.
  - World with roles `"Plan"`. The `Intercept` on the first `WorkspaceMirror` frame reads the DB
    synchronously through `world.Rig.NewDb()`.
  - At that moment a `WorkspaceSlotDecision` event exists for the task, and its `preparation=` equals
    the frame's `Binding.PreparationId`.
  - The frame is then passed on, and the task ends leased.
- **V-17** RG `Flag_off_or_no_feature_ignores_the_list(string mode)`, 4 cases, on the scripted `Rig`:
  - `"flag-off:Plan"`, `"flag-off:none"` and `"flag-off:*"`: `WorkspaceSlotsEnabled = false`, a
    slot-capable runner, a Plan task;
  - `"runner-lacks-feature"`: flag on, roles `"Plan"`, a runner without `workspaceSlotV1`.

  Each case asserts: legacy shape; `RemoteWorkspaceSlotId` null after the S1 V-12 reply; **zero**
  decision events. This proves byte-identical flag-off handling.
- **V-18** RG `Flag_off_hosted_loop_sends_no_release`. On the scripted `Rig` with the flag off and roles
  `"*"`, seed a terminal eligible task with `RemoteWorkspaceSlotId = "ws-01"`, generation 1, and a
  path. Start `WorkspaceSlotReleaseHostedService` (flag off), wait 2 s of real time with no change
  required, and stop it. The result is `Peer.RequestCount(WorkspaceSlotRelease) == 0` and
  `RemoteWorkspaceSlotReleasedAt` null.

### Guards the regression

- **R-1** S1 server behaviour is unchanged (CP-22): `C0824WorkspaceSlotServerTests` (24 executions),
  including `C0824_FlagOffMirrorRequestUnchanged` (2), `C0824_SlotUnavailableHoldsWithoutFallback`
  (zero `Warning`, 3 `Held`), and `C0824_LeaseRecoveryAtEachHandoff` and
  `C0824_ReleaseRoundTripRealRunner` (the real runner). All run under the default `"*"`, through the
  open-generic monitor.
- **R-2** D-8 dispatcher exclusion: `DefaultRunnerPinTests/SourceLanding_mutation_reaches_runner_launch_with_binding`
  (`RequestCount(WorkspaceMirror) == 0`, `:310`), as part of `DefaultRunnerPinTests` (6).
- **R-3** Layered files: ST `Layered_files_replace_the_whole_value` (V-6, 1 case). A base
  `"Plan,Code"` with an override `"none"` gives `"none"`; with an override `"Docs"` it gives `"Docs"`.
  This pins T-D-1 against a revert to `string[]`, which merges by index.
- **R-4** The preparer and its rig: `RemoteWorkspacePreparerTests` (27). The rig gains only the
  `configure`/monitor path, and the preparer gains S-4.
- **R-5** The validator: `RolePolicyConfigurationValidationTests` (4), the existing users of
  `new DelegationSettingsValidator()` and its registration.

### Masking and reachability

- **V-11 `"mutation"`.** If the dispatcher refuses or reroutes a bare Mutation seed before remote prep
  (not established at the base), Code records that in its report. The case then moves to ST V-9 alone,
  and PC-4 still has V-9. This is a known reachability risk, not a guess at the outcome.
- **V-12 `"hosted-loop"`.** The hosted service gets `Options.Create` with roles `"none"` **and** the
  rig's monitor at `"none"`. So a mutant that gates the loop on either source is reachable.
- **V-15 `"slot-then-none"`.** Without T-D-4, the resend is legacy. The runner then creates
  `task-<id8>`, and the row's `RemoteWorkspaceSlotId` stays null while the ledger keeps a Leased slot.
  Both assertions go red. Git does not mask this, because a legacy mirror succeeds.
- **V-5 `[]`.** A shape validator built on `GetSection(...).Exists()` passes `[]` as absent (measured),
  and the value is `"*"`. This is M-4. Only the children enumeration refuses it.
- **V-17.** The runner is scripted, so the request shape is the whole oracle. The flag-off runner path
  is S1's `C0824_FlagOffRunnerKeepsTaskMirror`, which is unchanged and out of this scope.
- **V-7 and V-14 timing.** The tests poll on the gate's reason, never on `OnChange`: an invalid reload
  fires no `OnChange` (measured).

### Guard inventory

| G | Plan ref + safety-critical guard | PC |
|---|---|---|
| G-1 | A-D-3/T-D-1 `*` admits every role except Mutation (S1 semantics kept) | PC-1 |
| G-2 | A-D-3/T-D-1 `none` admits no role (soft rollback stops new leases) | PC-2 |
| G-3 | T-D-1 a list admits only its listed roles | PC-3 |
| G-4 | A-D-3 Mutation is never admitted, whatever the value | PC-4 |
| G-5 | T-D-6 gate belt: SourceLanding is never admitted | PC-5 |
| G-6 | plan D-8 dispatcher: SourceLanding never enters remote prep (`AgentTaskDispatcher.cs:6149`) | PC-6 |
| G-7 | T-D-1 names match ordinally in canonical case, untrimmed | PC-7 |
| G-8 | T-D-1 names are names: no numeric `Enum.TryParse` acceptance | PC-8 |
| G-9 | T-D-1 `""`, empty entries and JSON `null` are refused, not read as none or all | PC-9 |
| G-10 | T-D-1 `Mutation` cannot be listed | PC-10 |
| G-11 | T-D-1 `*` and `none` are exclusive tokens | PC-11 |
| G-12 | T-D-2 the shape check refuses an array or object value (children enumeration) | PC-12 |
| G-13 | T-D-2 the shape validator is registered by S-2 (Program's path) | PC-13 |
| G-14 | T-D-3 the gate reads the monitor at each decision (hot reload) | PC-14 |
| G-15 | T-D-3 an invalid value or reload fails closed (no lease), never open | PC-15 |
| G-16 | A-D-3 the hosted release loop does not depend on the role value | PC-16 |
| G-17 | A-D-3 `ReconcileAsync` does not depend on the role value (`none`) | PC-17 |
| G-18 | brief: a slot whose role was removed from the list is still released | PC-18 |
| G-19 | S1 the flag gates the lease (flag off: legacy whatever the list) | PC-19 |
| G-20 | S1 the flag gates the hosted release loop | PC-20 |
| G-21 | S1 the runner must advertise `workspaceSlotV1` | PC-21 |
| G-22 | T-D-4 a resend reuses a recorded `slot` decision | PC-22 |
| G-23 | T-D-4 a resend reuses a recorded `legacy` decision | PC-23 |
| G-24 | T-D-4 a definitive 409 refusal clears the decision | PC-24 |
| G-25 | T-D-4 a transport, timeout or malformed reply does not clear it | PC-25 |
| G-26 | T-D-5 the decision is saved before the request is sent | PC-26 |
| G-27 | T-D-5 no decision event unless flag, binding and feature all hold (flag-off byte identity) | PC-27 |
| G-28 | A-D-3/R-2 the unlisted request is the exact legacy shape (no `slot:false`, no `cold`) | PC-28 |

Guards in no witness: none. G-6, G-19, G-20 and G-21 are S1 guards that S1c's edit sits beside. They are
listed because S1c rewrites the code around them.

### Positive controls

Each PC breaks its guard with a compiling defect and expects the named method red at the named
assertion. Mutation runs break/red/restore/green after the land; Code runs the V/R rows; Review judges
before the land.

Filters are method-scoped: `--treenode-filter "/*/*/<Class>/<Method>"`. A method with `[Arguments]`
runs all its cases, and the named case must go red. Zero tests, a build error or a fixture error is not
red.

Files: `Par` is the S-1 parser/`WorkspaceSlotRoleSet`, `Gate` is `WorkspaceSlotRoleGate.cs`, `Val` is
`DelegationSettings.cs` (validators) plus the S-2 extension, `Svc` is `RemoteWorkspaceService.cs`, `Prep`
is `RemoteWorkspacePreparer.cs`, `Rel` is `WorkspaceSlotReleaseService.cs`, and `Disp` is
`AgentTaskDispatcher.cs`.

| PC | Break | Expect red | Reachability |
|---|---|---|---|
| PC-1 | `Par`: `*` admits no role | ST/`Default_admits_every_role_except_mutation` at the admitted set | default value, no config |
| PC-2 | `Par`: `none` is parsed as `*` | ST/`None_admits_no_role` at Admits; RG/`None_stops_new_leases_while_releases_continue("reconcile-pass")` at B's legacy shape | `none` is valid |
| PC-3 | `Par`: a list admits every non-Mutation role | ST/`Listed_admits_exactly_the_listed_roles("Plan","Plan")` at the set; RG/`Unlisted_role_gets_the_legacy_mirror_and_never_holds("role-not-listed")` at legacy shape | Code task, list `Plan` |
| PC-4 | `Par`: drop the Mutation exclusion | ST/`Mutation_and_source_landing_are_never_admitted("*")` at `(false, mutation)` | `*` would admit it |
| PC-5 | `Gate`/`Par`: drop the SourceLanding check | same method `("Plan,Docs")` at `(false, source_landing)` | Plan is listed |
| PC-6 | `Disp`: drop `claimed.SourceLandingOperationId is null` at `:6149` | `DefaultRunnerPinTests/SourceLanding_mutation_reaches_runner_launch_with_binding` at `Status == Dispatched` / `RequestCount(WorkspaceMirror) == 0` | the existing world |
| PC-7 | `Par`: compare with `OrdinalIgnoreCase` after `Trim()` | ST/`Invalid_values_are_refused("plan")` and `(" Plan")` at `Validate(...).Failed` | each is canonical under the mutant |
| PC-8 | `Par`: accept an entry when `Enum.TryParse<AgentTaskRole>(entry, false, out _)` | same `("1")` at Failed | `"1"` parses as `Plan` |
| PC-9 | `Par`: split with `RemoveEmptyEntries` and treat zero names as `none` | same `("")`, `("Plan,,Docs")` and `("Plan,")` at Failed; ST/`Bound_through_the_production_registration(null)` at the exception | JSON `null` → `""` (measured) |
| PC-10 | `Par`: allow `Mutation` in a list | ST/`Invalid_values_are_refused("Mutation")` at Failed | Mutation is a defined name |
| PC-11 | `Par`: `*,Plan` parses as `*` | same `("*,Plan")` at Failed | |
| PC-12 | `Val`: the shape check uses `GetSection("Delegation:WorkspaceSlotRoles").Exists()` and `GetChildren()` on that section (M-4 form) | ST/`Bound_through_the_production_registration("[]")` at the expected exception (the value would be `*`) | measured: `[]` has `Exists() == false` |
| PC-13 | `Val`: S-2 omits the shape validator registration | same `("[\"Plan\"]")` and `("[]")` at the exception | S-2 is the only registration path the test uses |
| PC-14 | `Gate`: capture `CurrentValue.WorkspaceSlotRoles` in the constructor | ST/`Gate_reads_the_monitor_at_each_call` at `Evaluate(Plan).Lease == false` after `"Docs"` | one instance |
| PC-15 | `Gate`: on `OptionsValidationException`, return `(true, all)` (fail open) | ST/`Invalid_reload_fails_closed_then_recovers` at `Lease == false`; RG/`Unlisted_role_gets_the_legacy_mirror_and_never_holds("invalid")` at legacy shape | throwing monitor |
| PC-16 | `Rel` hosted loop: `if (enabled && settings.Value.WorkspaceSlotRoles != "none")` | RG/`None_stops_new_leases_while_releases_continue("hosted-loop")` at A's `RemoteWorkspaceSlotReleasedAt` | `Options.Create` carries `none` |
| PC-17 | `Rel`: `ReconcileAsync` returns 0 when the gate's value is `none` | same `("reconcile-pass")` at `ReconcileAsync == 1` | A is eligible |
| PC-18 | `Rel`: `EligibleAsync` returns false when the task's role is not admitted now | RG/`Removed_role_slot_is_released_normally` at `== 1` | Plan removed |
| PC-19 | `Svc`: drop `_workspaceSlots &&` from the decision | RG/`Flag_off_or_no_feature_ignores_the_list("flag-off:Plan")` at legacy shape; `C0824_FlagOffMirrorRequestUnchanged("flag-off")` at the property set | slot-capable runner |
| PC-20 | `Rel` hosted loop: drop the flag check | RG/`Flag_off_hosted_loop_sends_no_release` at `RequestCount == 0` | an eligible slot-bound row |
| PC-21 | `Svc`: drop the `workspaceSlotV1` check | RG/`Flag_off_or_no_feature_ignores_the_list("runner-lacks-feature")` at legacy shape; `C0824_FlagOffMirrorRequestUnchanged("runner-lacks-WorkspaceSlotV1")` | flag on, Plan listed |
| PC-22 | `Svc` S-4: ignore a recorded `slot` decision (always decide anew) | RG/`Retry_keeps_the_first_decision_of_its_preparation("slot-then-none")` at the row's slot id equal to the ledger's Leased slot | the lease was taken on the first send |
| PC-23 | `Svc` S-4: reuse only `slot` decisions | same `("legacy-then-listed")` at "no slot Leased for the task" | Plan listed after the first send |
| PC-24 | `Prep`: never write `cleared` on a 409 | same `("slot-refused-then-none")` at the resend's legacy shape (the mutant resends `slot` and is held again) | real 409 Error frame |
| PC-25 | `Prep`: write `cleared` on any mirror failure | same `("slot-then-none")` at the resend `slot == true` | the `{}` reply is a malformed Result, not a 409 |
| PC-26 | `Svc` S-4: save the decision event after `MirrorAsync` returns | RG/`Decision_is_recorded_before_the_mirror_is_sent` at "an event exists inside the interceptor" | the interceptor runs during the send |
| PC-27 | `Svc` S-4: write the decision event before the flag and feature checks | RG/`Flag_off_or_no_feature_ignores_the_list("flag-off:Plan")` at zero decision events | |
| PC-28 | `Svc`: for a legacy decision send `request with { Slot = false, Cold = task.WorkspaceColdReset }` | RG/`Unlisted_role_gets_the_legacy_mirror_and_never_holds("role-not-listed")` at the property set (`cold` and `slot` present) | the Code task has `WorkspaceColdReset = true` |

**Mutant list** (other mutants that Mutation also runs, on the witness named; none needs its own guard):

- **M-1** `Svc`: inject `IOptions<DelegationSettings>` into the gate path instead of the monitor →
  RG/`Hot_reload_applies_to_the_next_lease_without_restart`, at task 2's legacy shape.
- **M-2** `Gate`: rethrow `OptionsValidationException` → ST/`Invalid_reload_fails_closed_then_recovers`
  at "does not throw"; RG `("invalid")` at "never holds" (the task stays Queued with a Warning).
- **M-3** `Svc`: read `WorkspaceSlotsEnabled` from the monitor (the rig's open-generic monitor has the
  default, false) → R-1, `C0824_LeaseRoundTripRealRunner`, at `slot == true`.
- **M-4** the `Exists()` shape check (PC-12's form).
- **M-5** `Par`: case-insensitive tokens (`None` as `none`) → ST/`Invalid_values_are_refused("None")`.
- **M-6** `Prep`: write the decision only after a successful reply →
  RG/`Retry_keeps_the_first_decision_of_its_preparation("slot-then-none")`.
- **M-7** `Svc`: the legacy decision keeps `Cold` → PC-28's assertion.
- **M-8** `Rel`: the reconciler selects only tasks that the current list admits → V-13 (the same assertion
  as PC-18, reached through the query rather than `EligibleAsync`).

### Out of scope

- **The runner side.** S1c changes no runner file, and the runner pool, replay and release are S1's
  (the S1 test design and its CP rows).
- **The flag-off hard rollback across a lost reply.** A lease whose reply was lost, followed by a server
  restart with `Delegation:WorkspaceSlotsEnabled=false`, resends in the legacy shape and strands that
  slot. This is S1 behaviour ("flag off still stops everything as today"), and the amendment §4.5 already
  limits hard rollback to S-1 and S-2. It is recorded so that Review does not read it as an S1c gap. A
  fix would be the same T-D-4 rule applied under flag off (a runner-side replay across shapes), which is
  a follow-up card if wanted.
- **Environment-variable shadowing and the reload location.** No test. S1c's docs edit to
  `docs/session-runtime-invariants.md` (CARD-0824 paragraph) and `docs/docker-stack.md` (§ Runner
  workspace slots) must say:
  - `WorkspaceSlotRoles` is changed only in the server's reloadable JSON (`appsettings.Development.json`
    under the desktop AppHost);
  - never through `Delegation__WorkspaceSlotRoles`, which is not reloaded and shadows the file;
  - the Docker server has no reloadable file, so its soft rollback is a restart;
  - the operator confirms a rollback by the next task's `WorkspaceSlotDecision` event
    (`reason=roles_none`);
  - the grammar (`*`, `none`, exact names), `"none"` instead of `[]`, and that an array value is refused
    at startup and fails closed on reload.
- **The canary read-only inventory hook.** The amendment puts it in S4b (§3.5, `WorkspaceSlotInventory`),
  not S1c. For the canary, a server2 delegate reads the ledger directly (§4.3). No S1c row.
- **The client.** No view change. The new event type renders through the existing generic event row.
  Adding `'WorkspaceSlotDecision'` to the `client/src/api/agentTasks.ts` union is optional: the union
  already omits `CommitRecoveryStarted`.
- **Program composition beyond S-2.** `Program.cs` calls S-2. No WebApplication host test is added:
  V-5 and V-14 run the same S-2 registration over real JSON providers.

### Implementation steps (red first, then green)

1. Commit ST and RG with S-5 and stubs for S-1..S-4 that compile and keep today's behaviour: the
   property is a string with default `"*"`, `Admits` returns true, there is no shape validator, the gate
   reads once, and there is no decision record. The commit is red as expected: V-2..V-9, V-11..V-17 and
   R-3 mostly red; V-1, V-10 and V-18 may be green.
2. Implement S-1, S-2 and the validators (V-1..V-6 green), then S-3 (V-7..V-9 and V-11 green), then S-4
   and T-D-5 (V-15 and V-16 green), and check V-12, V-13, V-17 and V-18.
3. Docs: the two docs paragraphs (§Out of scope) and an `appsettings.json` comment-free default. Do not
   add the key to `server/appsettings.json`: absent means `"*"`, which is the S1 default.
4. Run CP-21, then CP-22.

## Performance budget

Owner: [test-performance-standards.md](../../test-performance-standards.md).

- **Measured inputs.**
  - `tests/Antiphon.Tests` build: 143 s warm on server2 (CARD-1677 TD, `b477d7ccf`), and p50 326 s and
    p95 579 s loaded (n=117, the plan's cf8b6982 receipts).
  - C0824 server class per-test p95: 10.4 s at load ~10 and 17.2 s at load 37 (CARD-1660). The class
    runs in parallel: 24 cases in about 58 s with three sibling classes.
  - Configuration probes (this task): binding and reload are sub-second, and a reload takes effect in
    under 8 s (the probe's bound; the expected debounce is 250 ms).
- **Per-test bodies (estimated).**
  - ST: parser and binding cases under 50 ms; reload cases V-7 and V-14 (the file part) under 2 s
    typical, with a 10 s poll bound.
  - RG: a transport world plus one or two leases, about 12 s typical and 20 s loaded. V-12 and V-15 run
    two leases or retries each, about 25 s loaded.
- **Timeouts.**
  - ST `[Timeout(15_000)]`: the 10 s poll plus setup.
  - RG `[Timeout(45_000)]`: 2 × the 20-25 s loaded figure, the CARD-1677 transport-world value. The
    plain C0824 class's 35 s is too tight for the two-lease cases.
  - Code resets both from the first green TRX at 2 × the measured p95.
- **Shared setup.** None across tests: one world or rig per RG test, as the C0824 class does, and one
  temp directory per ST reload test.
- **Phases.** Build, fixture setup, bodies, teardown, with slot wait reported separately. Each RG test
  wraps its ticks in the existing `TestTiming` phase when the rig exposes it.
- No row is near 30 minutes. CP-21 including its build is the largest, at about 12 minutes.

### Checkpoints

One checkpoint run, After group S1c, one build. Both rows are server2 (Linux). These rows replace the
amendment's CP-21 and CP-22 (same numbers, so CP-23 and up stay with S4a and later). `Min` is the TUnit
executed floor (`-MinExecuted`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-21 | S1c | tests/Antiphon.Tests -> bin-c0824-s1c/ | portable-slot-role-gate | /*/*/(C0824WorkspaceSlotRoleSettingsTests*)\|(C0824WorkspaceSlotRoleGateTests*)/* | V-1..V-18, R-3 | 2 classes, 0 failed/skipped | 50 | 12 | true |
| CP-22 | S1c | CP-21 | portable-slot-role-regression | /*/*/(C0824WorkspaceSlotServerTests*)\|(RemoteWorkspacePreparerTests*)\|(DefaultRunnerPinTests*)\|(RolePolicyConfigurationValidationTests*)/* | R-1, R-2, R-4, R-5 | 4 existing classes, 0 failed/skipped | 61 | 6 | true |

**How Min is counted (executions).**
- CP-21 = ST 31 + RG 19 = **50**.
  - ST has nine methods: V-1..V-5, V-7..V-9 and R-3's `Layered_files_replace_the_whole_value` (V-6).
    The cases are V-1 1, V-2 1, V-3 3, V-4 14, V-5 7, V-6 1, V-7 1, V-8 1 and V-9 2, for **31**.
  - RG has nine methods: V-10 1, V-11 4, V-12 2, V-13 1, V-14 1, V-15 4, V-16 1, V-17 4 and V-18 1, for
    **19**.
- CP-22: `C0824WorkspaceSlotServerTests` 24, `RemoteWorkspacePreparerTests` 27, `DefaultRunnerPinTests` 6
  and `RolePolicyConfigurationValidationTests` 4 = **61**. These are counted from source at the base by
  `[Test]` plus `[Arguments]` (24 matches the CARD-1677 TD measurement). They are floors: a method added
  later keeps them valid.

**Roster and prefix check** (grep `class <name>` in `tests/` at the base):
- None of `C0824WorkspaceSlotRoleSettingsTests` or `C0824WorkspaceSlotRoleGateTests` exists, and
  neither name is a prefix of the other or of any existing `C0824*` class (the existing
  `C0824WorkspaceSlotRehomeTests` shares only `C0824WorkspaceSlotR`).
- `RemoteWorkspacePreparerTests`, `DefaultRunnerPinTests`, `RolePolicyConfigurationValidationTests` and
  `C0824WorkspaceSlotServerTests` each match exactly one class, with no partial files and no longer
  class names sharing the prefix.
- The `Rig` and `C0824SlotTransportWorld` have no `[Test]`.

**Import verdict (measured, server2, 2026-11-03, one build-slot lease, waited 301 s, held 7 s):**
`dotnet run --project tools/Antiphon.Checkpoints -- import --plan
docs/superpowers/plans/2026-11-03-card-0824-s1c-test-design.md --out .antiphon/c0824-s1c-td-manifest.yaml`
gave exit 0 and `imported 2 rows`, with **no importer warnings** (only compiler warnings from the tool's
own build).
- The manifest has one build, `bin-c0824-s1c` (`outputPath: bin-c0824-s1c/`).
- CP-21: `minExecuted: 50`, `timeoutMinutes: 36`, `serial: true`.
- CP-22: `minExecuted: 61`, `timeoutMinutes: 18`, `serial: true`.
- The escaped `\|` filters parse to the `(A*)|(B*)` grammar.

### Cost

Labels: **measured** where stated, otherwise estimated. Slot wait (60-105 s per lease today) is reported
separately.

- **Ordinary V/R floor (Code)** = the sum of `EstimatedMinutes` = CP-21 12 + CP-22 6 = **18 min**. This is
  one build (143 s warm measured, 579 s loaded p95). It matches the amendment's 16 min plus 2 for the
  larger regression row: R-4 and R-5 are added because the rig and the validator change.
- **Positive-control floor (Mutation)**, method-scoped, one incremental `Antiphon.Tests` build per
  mutant and per restore (about 90 s each, measured full 143 s):
  - PCs on ST only (PC-1, -4, -5, -7..-14: 12): 2 builds + 2 runs of under 30 s ≈ **3.5 min each = 42 min**.
  - PCs on RG or existing rig classes (PC-2, -3, -6, -15..-28: 16; the ST half of PC-2, PC-3 and PC-15
    runs in the same filter): 2 builds + 2 runs of 30-60 s loaded ≈ **5 min each = 80 min**.
  - Mutants M-1..M-8 on the same witnesses: 8 × about 4.5 min = **36 min**.
  - Setup: one cold build = **about 10 min** loaded.
- **Total** = setup 10 + V/R 18 + PC 122 + M 36 = **about 186 min estimated, serial**.
- **Cheaper option:** the env-gated batch recipe (all 36 mutants compiled behind `On("PC-n")` in one
  build, selected per run, one lease per run) gives about 3 builds + 36 runs of about 1 min ≈ **50 min**,
  saving about 135 min. PC-6 (`Disp`) and PC-13 (`Val` registration) are the same pattern. No mutant
  touches source that a witness reads as data.

### Completion and handoff

- guards=28, mapped=28, missing=0, duplicate PC maps=0. All 28 PCs and the 8 extra mutants run on
  server2. Cost is numeric (estimated, with measured inputs marked).
- Seams S-1..S-5 are missing at the base and are part of Code.
- T-D-1..T-D-6 are design corrections backed by the measured facts. They are not open choices, but each
  is overridable (Q-1..Q-3).

## Open questions (each with a default)

- **Q-1 Case-insensitive names?** Default: exact canonical case (the brief). The alternative is the
  repo's `ClaudeEffortByRole` convention (case-insensitive, trimmed). It would drop V-4's `"plan"`,
  `" Plan"` and `"Plan "` cases and PC-7.
- **Q-2 The new event type vs. an existing one.** Default: append `WorkspaceSlotDecision = 41`
  (T-D-5). No existing type fits: `Warning` and `Held` break the S1 witness and alarm the feed. The
  alternative is a column on `TaskWorktreeMirrorBindings`, which needs a migration.
- **Q-3 Should Mutation leasing under `*` differ from S1?** Default: excluded (A-D-3). The flag is off in
  production, so no live behaviour changes.
