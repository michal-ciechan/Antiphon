# CARD-0881 re-plan: the effective-settings read is the scoped pipeline route

Date: 2026-10-10. Stage: **Plan with verification folded in; next Code.** Baseline `176c8a86ca39bd2ffbea0f5d9eaf27d7c3e52bd5` (origin/master and `feat/card-task-efb7c741` are identical at this commit). Supersedes the 2026-10-01 plan `2026-10-01-card-0881-effective-settings-endpoint-plan.md`, which was written before CARD-0505 and CARD-0822 landed and whose 40-test, four-slice design no longer matches what the code does. This task edits only this file. Post-land Mutation is paused: it was **NOT** run and is not part of this plan's Code budget.

## Outcome and scope

CARD-0505 (`a646a004d`) and CARD-0822 (`176c8a86c`) already deliver most of what CARD-0881 asked for. What remains is small:

1. Put the two facts the scoped pipeline route still lacks, **runner seats/eligibility** and **runner defaults**, on that route, and add a `remaining` slot count to each host row, so that one GET answers "how many more tasks may I dispatch in each stage, and on server2, right now".
2. Replace the "three-route read; CARD-0881 will replace it" wording in the delivered orchestrator bundle, AGENTS.md, `docs/orchestration-loop.md` §1, the skill mirror and the two API docs with "read the effective-settings route and use its limits", carrying the operator defaults as the fallback, and update the tests that pin that wording and the bundle bytes.

No limit value, enforcement predicate, queue behaviour, host budget, new route, new table, migration, UI or settings writer. Code budget: 30-60 minutes including the two checkpoint rows.

## Ground truth: what the card assumes against what master does

| Card assumption (written 2026-10-01) | What `176c8a86c` does | Consequence |
|---|---|---|
| The absolute limit and open count are exposed by no route; the orchestrator only learns them from the 409 body. | `GET /api/agent-tasks/pipeline?projectId=<guid>` returns `concurrencyScopes[0].effective.maxParallel` (+ `maxParallelSource`) and `occupancy.open` / `parallelRemaining` for that project; the fleet read lists every represented scope plus the null bucket (`AgentTaskPipelineStatusService.ProjectScopes`, `DispatchConcurrencySettingsService.ToOccupancy`). | Ask (a) is satisfied under CARD-0505's names. No duplicate top-level `absoluteLimit`/`openCount` is added (D-1). |
| Role limits are startup config with no runtime API; `recommendationsAreAdvisory` while the gate enforces. | Limits live in the revisioned `DispatchConcurrencySettings` store; `GET/PUT /api/dispatch-concurrency` and the project route; the pipeline's `recommendedInFlight` is the effective role value of the scope; sources are `default` (imported seed), `global`, `project`. | Per-stage effective limit, counts, free slots and source are already on the route. |
| Per-host effective limit, in-flight, budget vs declared vs effective with source are only on `GET /api/hosts`. | The pipeline's `hosts[]` already carries `inFlight`, `effectiveLimit`, `configured`, `declared`, `source` (`budget` / `config` / `runner`), `scope: fleet`. | Only a `remaining` count is missing (S1). |
| Runner capacity/eligibility and runner defaults need `GET /api/session-runners` and `GET /api/runner-defaults`. | Still true: the pipeline response has no `runners` or `runnerDefaults` section. `SessionRunnerCatalogue.ListAsync` and `RunnerDefaultSettingsService` are the existing projections. | S1 adds both sections to the pipeline DTO, reusing those projections unchanged. |
| CARD-0822 may generate instruction text whose numbers must come from the same effective-settings object. | `OrchestratorInstructionsSnapshotBuilder` reads `DispatchConcurrencySettingsService.ReadEffectiveAsync`, `HostBudgetService`, `SessionRunnerCatalogue` and `RunnerDefaultSettingsService`: the same stores the pipeline projects. Its file deliberately carries no occupancy and says "Live counts: GET /api/agent-tasks/pipeline". Its comment still says "the only policy seam CARD-0881 replaces". | Reconciliation is structural and already true; S2 corrects the stale comment only. No builder refactor (D-4). |
| The policy text lists three GETs and says CARD-0881 will replace them. | `server/Bundles/orchestrator.md`, `AGENTS.md:81`, `docs/orchestration-loop.md` §1 and the WIP-defaults paragraph (~line 800), `.claude/skills/antiphon-orchestrator/SKILL.md` all still say so; `StandingPipelinePolicyDocumentationTests.Phrases` and `OrchestratorInstructionsGuidanceTests.Phrases` pin `CARD-0881`, `GET /api/hosts` and `effective concurrency limits`; `CheckpointRepeatDocumentationTests` pins the bundle's SHA-256 (`80b0c0be…f56a`); `InstructionBundleTests` caps the LF bundle at 14,310 chars (now 13,391). | S2 is the S-last slice: text, pins and byte pin together. |
| CARD-0505 is unimplemented; land this after it. | Landed; live `/api/version` reports `176c8a86c` with `land-v2`. Live seed: LegacyOpen, maxParallel 6, Code 5 / Review 5 / Plan 3 / Investigate 3, others 1, `seed.origin: startupConfiguration`. | No sequencing constraint remains. |

Live reads at 2026-10-10 (task token, nothing printed): `/api/runner-defaults` revision 2, global `server2`, no kind overrides; `/api/session-runners` desktop windows 0/2 `delegatedTasks`, server2 linux 4/10 `sessions` eligible, server2-temp stale/draining; `/api/hosts` local 0/2 `config`, server2 4/10 `runner`; fleet pipeline 51,495 bytes with `concurrencyScopes` present. These are observations, not reservations.

## Decisions

### D-1: extend the pipeline route; no new route, no alias fields

The card allows "a new GET /api/settings/dispatch, or an extended pipeline response". The pipeline route already carries limits with sources, occupancy with remaining slots, scoped selectors (`?projectId=`, `?unscoped=true`), 404/422 validation and the fleet `hosts` block, all tested by `DispatchConcurrencyPipelineTests`. Adding `runners` and `runnerDefaults` (two existing DTO types) and `hosts[].remaining` makes it the one read. Rejected: a new aggregate route (a second place to keep in sync with the pipeline's host block, new DTOs, more tests, over budget); duplicate top-level `absoluteLimit`/`openCount` (CARD-0505 already names them per scope; aliases would be two names for one number). Both new sections are fleet facts on every read, like `hosts`; `taskScope` still names the task scope.

### D-2: reuse the two existing projections verbatim

`runners` is exactly `SessionRunnerCatalogue.ListAsync(directory, db, settings, prep, ct)`, the body of `GET /api/session-runners`. `runnerDefaults` is exactly the `RunnerDefaultsDto` of `GET /api/runner-defaults`, read through a new non-initialising `RunnerDefaultSettingsService.ReadAsync` (null before the first import). The pipeline is a read: it must not seed the runner-defaults row the way `GetAsync` does (`EnsureInitializedAsync`). Rejected: calling `GetAsync` (a write inside a GET); re-implementing either projection (contract drift).

### D-3: keep CARD-0505's source vocabulary

Sources stay `default` (the imported seed), `global`, `project` for concurrency; `budget`, `config`, `runner` for hosts; `lastProvenance` for runner defaults. `default` does not distinguish a code default from a bound configuration leaf; `GET /api/dispatch-concurrency` `seed` shows the imported values, which is how an operator sees that the live seed (Code 5) differs from the shipped default (Code 2). Adding seed-origin metadata is a CARD-0505 contract amendment outside this budget; if the operator wants it, file a Backlog card "Dispatch-concurrency seed origin: code default vs configuration leaf" (text in Follow-ups). Rejected: inferring origin by comparing values with `new DelegationSettings()`.

### D-4: CARD-0822 stays as landed

The generated instructions file is built from the same four stores the pipeline projects, carries no occupancy by design, and already tells orchestrators to read the pipeline for live counts. Refactoring the builder to consume the pipeline would pull task lists and occupancy into a file whose hash must not change on every task transition. S2 only corrects the stale "only policy seam CARD-0881 replaces" comment. Rejected: a shared "effective settings service" consumed by both (the prior plan's S2), which is more code than the card's remaining ask.

### D-5: S-last policy text, net-shorter bundle, pins updated in the same slice

The bundle gets the exact block in S2 below; LF length must end strictly below the current 13,391 (projected 13,379 with the Platform sentence change) and under the 14,310 pin. `CheckpointRepeatDocumentationTests` re-pins the SHA-256 of the new LF bytes (`server/Bundles/*.md` is `eol=lf`, so `sha256sum` of the checked-out file equals the pinned value on every OS). Both `Phrases` lists change identically; negative pins stop the three-route wording returning. Stage bundles (`stage-plan.md`, `stage-code.md`, `stage-review.md`, `stage-mutation.md`) keep their `Platform:` lines: they are the delegate placement contract (CARD-0710) and not the orchestrator's limits read. Rejected: leaving the bundle stale and editing docs only; hand-editing a version hash (the runtime computes it from the embedded text).

### D-6: minimal verification, no Windows row, no whole-Unit run

Two checkpoint rows, one per slice; class filters only; one red-first run of CP-1 before the S1 production edit. Four method-scoped positive controls are listed for the paused post-land Mutation stage and are not run in Code. Lane: server2/Linux, platform unpinned (Any); no row needs Windows.

### Waiting-input checklist

Not applicable: this plan adds or changes no session that waits for input.

## Slices

### S1: `runners`, `runnerDefaults`, `hosts[].remaining` on the pipeline route

Files:

- `server/Application/Dtos/AgentTaskPipelineDtos.cs`: on `AgentTaskPipelineDto` add
  `public IReadOnlyList<SessionRunnerCatalogueEntryDto> Runners { get; init; } = [];` (CARD-0881; the `GET /api/session-runners` rows, fleet-wide on every read; empty when no directory is registered) and
  `public RunnerDefaultsDto? RunnerDefaults { get; init; }` (the `GET /api/runner-defaults` body; null before the first import or when no service is registered). On `HostLimitSummaryDto` add `public int? Remaining => EffectiveLimit is int limit ? Math.Max(0, limit - InFlight) : null;`.
- `server/Application/Services/RunnerDefaultSettingsService.cs`: add `public async Task<RunnerDefaultsDto?> ReadAsync(CancellationToken ct)` returning null when `ReadSnapshotAsync` is null, else the existing private `ProjectAsync(ct)`. No initialisation, no SaveChanges.
- `server/Application/Services/AgentTaskPipelineStatusService.cs`: two optional constructor parameters appended, `PhoneHomeRunnerDirectory? directory = null, RunnerDefaultSettingsService? runnerDefaults = null` (Program's `AddScoped<AgentTaskPipelineStatusService>()` resolves both; existing test constructions are unchanged). In `GetAsync`, after the host limits are read: `runners = _directory is null ? [] : await SessionRunnerCatalogue.ListAsync(_directory, _db, _settings, _remotePrep, ct)` and `defaults = _runnerDefaults is null ? null : await _runnerDefaults.ReadAsync(ct)`, set on the returned DTO beside `Hosts`. Cancellation token reaches both reads.
- **new** `tests/Antiphon.Tests/Application/EffectiveSettingsPipelineTests.cs` (V-1..V-3 below).

No change to `Program.cs`, `AgentTaskEndpoints.cs`, the client (additive JSON), `DelegationOpenGate`, `DispatchConcurrencySettingsService` or `HostBudgetService`.

### S2 (S-last, mandatory): policy text, docs, pins

Files: `server/Bundles/orchestrator.md`; `AGENTS.md`; `docs/orchestration-loop.md`; `.claude/skills/antiphon-orchestrator/SKILL.md`; `docs/ops-http.md`; `docs/antiphon-api.md`; `server/Application/Services/OrchestratorInstructionsSnapshotBuilder.cs` (one comment); `tests/Antiphon.Tests/Application/StandingPipelinePolicyDocumentationTests.cs`; `tests/Antiphon.Tests/Application/OrchestratorInstructionsGuidanceTests.cs`; `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatDocumentationTests.cs`. `CLAUDE.md` is an import pointer and is not edited; `docs/cards/` is generated and is not edited; historical plans and investigations keep their wording.

**Bundle.** Replace the text from `When you are working a board through its pipeline` up to, not including, `Model-tier names are` with exactly:

```text
When you are working a board through its pipeline, this is the standing policy unless the user
says otherwise this session. Before dispatch read the effective-settings route (CARD-0881),
GET /api/agent-tasks/pipeline?projectId=<id> (or ?unscoped=true): concurrencyScopes (absolute and
per-stage limits with sources, open/queued counts, remaining slots), hosts (effective limit,
source, in-flight, remaining), runners (seats, eligibility), runnerDefaults (placement). Use its
limits. Operator defaults where Antiphon sets none: every stage at up to four; at most six tasks
on server2 across stages; use the lower effective stage cap. Antiphon's limits are the ceiling.
Host budget writes need an operator request. Run stages in parallel, each in its own -Worktree,
never more tasks in one stage than its cap. Prefer server2 (-Runner server2); use desktop/Windows
only when work absolutely requires it, scoped to that piece.
On every completion dispatch the named next stage. Land a stage's work as soon as
it is confirmed. Keep Code at its depth cap (four unless Antiphon enforces less), counting
in flight, queued and ready in that snapshot. Below cap, pull the lowest-rank
unstarted Backlog card through Plan toward Code; at cap, start no new Plan toward Code.
Defer Code touching an in-flight Code task's same source area until it lands, even with a free slot.
File a Backlog card the moment
Investigate or Review finds a structural defect; never batch them. A defect a Clean Review approved that is found only in the running system after land
gets the post-land retrospective companion (`Post-land retrospective: <identifier>`,
label `post-land-retrospective`) with its Investigate task and Low-tier Docs pass from
docs/orchestration-loop.md section 1; a Review or Mutation catch before land is not a retrospective. A 409 `concurrency_limit`
carries `axis`, `population`, `canOverride` and the occupants: re-send with `-IgnoreConcurrencyLimit`
only when `population` is `open`, `canOverride` is true, the axis is `absolute`, and no occupant is in the stage you are dispatching; when it is
`role`, `population` is `queued`, `canOverride` is false, or a same-stage occupant is listed, defer. Other projects' work never counts against
yours.

Live operating settings (caps, runners, holds, pins, levels, standing lines) are generated into
ANTIPHON_ORCHESTRATOR_INSTRUCTIONS, also GET /api/orchestrator-instructions. Read it at session
start, after compaction and on a settings-changed note; it outranks numbers here and in the docs.
Never edit it.

Follow docs/orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout for autonomous AppHost and runner restarts and server2 rollouts.

```

In the final paragraph replace the sentence `Platform: read GET /api/runner-defaults and GET /api/session-runners.` with `Platform: the pipeline read's runnerDefaults and runners equal GET /api/runner-defaults and GET /api/session-runners.`; the rest of that paragraph is unchanged (its `Do not embed a fleet location.`, `Normally omit -Runner; the runtime default places the task.`, `pass -Platform Any explicitly`, `OS-only probe` pins stay). Everything before the policy block is unchanged. Measured on this baseline: old block 2,759 chars, new block 2,699; old Platform sentence 69, new 117; projected LF bundle length 13,379 (current 13,391; pin 14,310). Code records the actual LF length, the computed bundle version (`InstructionBundles` SHA-256 prefix of the trimmed LF text; today `884bd8a4`) and the new `sha256sum` of the file.

**AGENTS.md line 81** (the single line starting `- An orchestrator working a board`), replace with exactly:

```text
- An orchestrator working a board reads the effective-settings route before dispatching: `GET /api/agent-tasks/pipeline?projectId=<id>` (or `?unscoped=true`; CARD-0881). Its `concurrencyScopes` entry carries the absolute and per-stage effective limits with their sources, open/queued counts and remaining slots; `hosts` carries each host's effective limit, source, in-flight count and `remaining`; `runners` carries seats and eligibility; `runnerDefaults` carries placement. Use its limits. Changing a host budget is an operator-only setting. Operator defaults where Antiphon sets none: each pipeline stage at up to four concurrent tasks and at most six tasks on server2 across all stages; Antiphon's enforced limits are the ceiling, so use the lower effective stage cap. Run stages in parallel, each task in its own `-Worktree`, never more tasks in one stage than its cap. Prefer server2 (`-Runner server2`); use the desktop or Windows machine only for work that absolutely requires it, scoped to that piece. Keep the Code stage at its depth cap (four unless Antiphon enforces less, counting in flight, queued and ready) by pulling the next Backlog card when below it. Land as soon as a stage is confirmed; file a card for every structural defect the moment it is found. On a 409 defer; pass `-IgnoreConcurrencyLimit` only for `axis: absolute` with no same-stage occupant. A same-stage collision or a Code task touching the same source area as one already in flight defers. Owner: [docs/orchestration-loop.md](docs/orchestration-loop.md) §1 (CARD-0533). Live operating settings: read the file at ANTIPHON_ORCHESTRATOR_INSTRUCTIONS (or GET /api/orchestrator-instructions) at session start, after compaction and when a settings-changed note arrives; it outranks the numbers in AGENTS.md.
```

**docs/orchestration-loop.md §1 item 1**, replace the item body (from `**Each pipeline stage at up to four` to the end of the item, before `2. **On every completion`) with:

```text
1. **Each pipeline stage at up to four; at most six tasks on server2 across all stages.** These
   are the operator's defaults. Before dispatching, read the effective-settings route
   (CARD-0881): `GET /api/agent-tasks/pipeline?projectId=<id>` (or `?unscoped=true` for the null
   bucket). Its single `concurrencyScopes` entry carries the absolute and each stage's effective
   limit with its source (`default` is the imported seed; `global` and `project` are runtime
   overrides), the `open`/`parallel`/`queued` counts and the nonnegative `parallelRemaining` and
   `queuedRemaining` slots; `hosts` carries each host's `effectiveLimit`, `configured`, `declared`,
   `source` (`budget`, `config` or `runner`), `inFlight` and `remaining`; `runners` carries
   capacity, occupied seats, `dispatchEligible` and `acceptingNewWork`; `runnerDefaults` carries
   the global and per-kind runner defaults (there is no platform default). Use its limits:
   Antiphon's enforced limits are the ceiling, and the lower of four and the enforced stage limit
   applies. Run Investigate, Plan, TestDesign, Code, Review and Mutation in parallel, each task in
   its own `-Worktree`, never more tasks in one stage than its cap. Prefer server2
   (`-Runner server2`) while it is eligible; shield the desktop/Windows machine by using it only
   when work absolutely requires it (a Windows-only test row, native ConPTY or desktop-only
   behaviour), scoped to that piece, as in CARD-0778 CP-3 and CARD-0801 W-1. A
   `PUT /api/hosts/server2/budget` with `{ "maxInFlight": <n>, "reason": "<why>" }` can
   hold new dispatch, but is an operator-only setting; the orchestrator does not write it on its
   own initiative. The orchestrator still keeps its six-task server2 default when no lower host
   budget is enforced. Live operating settings: read the file at ANTIPHON_ORCHESTRATOR_INSTRUCTIONS
   (or GET /api/orchestrator-instructions) at session start, after compaction and when a
   settings-changed note arrives; it outranks the numbers in AGENTS.md.
```

Items 2-7 and the "Create-time per-project and per-role gates" paragraph are unchanged. In the WIP-defaults paragraph near line 800 (`The role gate uses the effective dispatch-concurrency policy (CARD-0505)…`), replace the span from `Read effective values` through `` to read each host's `effectiveLimit` and `inFlight`. `` with:

```text
Read effective values, occupancy, hosts, runners and runner defaults from the
effective-settings route, `GET /api/agent-tasks/pipeline?projectId=<id>` (CARD-0881), before
dispatching; the lower of that role limit and the operator's four-task default applies. Its
`hosts` block reports each host's `effectiveLimit`, `inFlight` and `remaining`; `runners` reports
capacity, occupied seats and eligibility; `runnerDefaults` reports the global and per-kind runner
defaults, with no platform default. CARD-0505 stores the dispatch limits at runtime.
There is no enforced server2 six-task cap beyond its seats unless a host budget is set.
```

The following `An operator can set `PUT /api/hosts/server2/budget` …` sentence stays. The "Runner defaults are not model pins" paragraph (~line 840) is the placement contract and stays.

**SKILL.md**, replace the first bullet of the policy block (`- **Each pipeline stage at up to four; …` through `…outranks the numbers in AGENTS.md.`) with:

```text
- **Each pipeline stage at up to four; at most six tasks on server2 across all stages.** These are
  operator defaults. Before dispatching, read the effective-settings route (CARD-0881):
  `GET /api/agent-tasks/pipeline?projectId=<id>` (or `?unscoped=true`). Its `concurrencyScopes`
  entry carries the absolute and per-stage effective limits with sources, open/queued counts and
  remaining slots; `hosts` carries each host's effective limit, source, in-flight count and
  `remaining`; `runners` carries seats and eligibility; `runnerDefaults` carries placement. Use its
  limits; changing a host budget is an operator-only setting, never an orchestrator-initiated
  write. Antiphon's enforced limits are the ceiling; use the lower effective stage cap. Run stages
  in parallel, never more tasks in one stage than its cap. Prefer server2 (`-Runner server2`); use
  desktop/Windows only when work absolutely requires it, scoped to that piece. Live operating
  settings: read the file at ANTIPHON_ORCHESTRATOR_INSTRUCTIONS (or GET /api/orchestrator-instructions)
  at session start, after compaction and when a settings-changed note arrives; it outranks the
  numbers in AGENTS.md.
```

**docs/ops-http.md.** In "The jobs you have", add a row directly after "Dispatch concurrency (CARD-0505)":

```text
| Effective dispatch settings (CARD-0881) | GET | `/api/agent-tasks/pipeline?projectId=<guid>` (or `?unscoped=true`) is the one read before dispatch. `concurrencyScopes[0]` is that scope's effective policy with sources (`default` = imported seed, `global`/`project` = runtime overrides) and `occupancy` (`open`, `parallel`, `queued`, `parallelRemaining`, `queuedRemaining` per scope and per role); `hosts[]` carries `effectiveLimit`, `configured`, `declared`, `source` (`budget`/`config`/`runner`), `inFlight` and `remaining` (max(0, effectiveLimit − inFlight), null when unknown); `runners` is the `GET /api/session-runners` body; `runnerDefaults` is the `GET /api/runner-defaults` body (null before the first import). `hosts`, `runners` and `runnerDefaults` are fleet facts on every read. A snapshot, not a reservation: a later create can still 409. |
```

In the "Delegated work" row change `` `/pipeline` without a query stays fleet-wide and adds `concurrencyScopes`. `` to `` `/pipeline` without a query stays fleet-wide and adds `concurrencyScopes`, `runners` and `runnerDefaults` (CARD-0881). ``. In the example block, after the `Invoke-RestMethod "$api/api/agent-tasks/pipeline" -Headers $h` line, add the comment `# CARD-0881: with ?projectId=<id> this is the one effective-settings read: limits with sources, remaining slots, hosts, runners, runnerDefaults`.

**docs/antiphon-api.md.** Append to the `GET /api/agent-tasks/pipeline` row: `CARD-0881: the response also carries `runners` (the `GET /api/session-runners` rows, fleet-wide on every read) and `runnerDefaults` (the `GET /api/runner-defaults` body; null before the first import), and each `hosts[]` row adds `remaining` = max(0, effectiveLimit − inFlight), null when the effective limit is unknown. With `?projectId=<guid>` this is the one effective-settings read before dispatch; it is a snapshot, not a reservation.`

**OrchestratorInstructionsSnapshotBuilder.cs** comment line 16: replace with `/// <see cref="ReadPipelinePolicy"/> reads the same stores as the CARD-0881 effective-settings read (GET /api/agent-tasks/pipeline?projectId=); occupancy stays out of the file.`

**Pins.** In both `StandingPipelinePolicyDocumentationTests.Phrases` and `OrchestratorInstructionsGuidanceTests.Phrases` replace `"effective concurrency limits"` with `"effective-settings route"`, replace `"GET /api/hosts"` with `"/api/agent-tasks/pipeline?projectId="`, keep every other entry (`CARD-0881` stays and is satisfied by the new text). In `StandingPipelinePolicyDocumentationTests.the_policy_phrases_are_pinned_in_every_copy` add, after the positive loop, `foreach (var stale in new[] { "three-route", "GET /api/session-runners", "GET /api/runner-defaults" }) text.ShouldNotContain(stale, Case.Insensitive, relative);` (the policy slices only; the bundle's Platform paragraph and the loop doc's placement paragraph are outside every slice). Add one test `the_effective_settings_route_is_documented` asserting `docs/ops-http.md` and `docs/antiphon-api.md` each contain `/api/agent-tasks/pipeline?projectId=`, `runnerDefaults`, `runners`, `remaining` and `CARD-0881`, and that `AGENTS.md` contains `Use its limits`. In `CheckpointRepeatDocumentationTests` replace the pinned SHA-256 with the new file's `sha256sum` and extend the comment with `CARD-0881 swaps the three-route policy for the effective-settings route.`

## Verification design

Frozen at `176c8a86ca39bd2ffbea0f5d9eaf27d7c3e52bd5`. Test-first order: write V-1..V-3, run CP-1 red (JSON properties `runners`, `runnerDefaults`, `remaining` absent), make S1, run CP-1 green; make S2 text and pin edits, run CP-2 (V-4 and V-5 cannot pass on the old text: `three-route` is present and `effective-settings route` is absent). All rows run through the checkpoint tool under the build-slot gate on server2.

### Fixture for V-1..V-3

`PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString, configureServices: …, mapEndpoints: app => app.MapGet("/api/agent-tasks/pipeline", AgentTaskEndpoints.ReadPipelineAsync))`, registering `ISessionRunnerDirectory => PhoneHomeRunnerDirectory`, `IOptions<DelegationSettings>` (`MaxConcurrentTasks = 2`, `MaxOpenTasks = 6`, `DefaultRunnerId = "grok-linux"`), `HostBudgetService`, `DispatchConcurrencySettingsService`, `RunnerDefaultSettingsService`, `AreaMapLoader`, `AgentTaskPipelineStatusService`, and `ConfigureHttpJsonOptions` (camelCase + `JsonStringEnumConverter`, as `DispatchConcurrencyWireHost` does). The host already maps `GET /api/session-runners` and registers `PhoneHomeRunnerDirectory` with `grok-linux` configured but never registered, so `hosts` has `local` and `grok-linux` and `runners` has `desktop` and `grok-linux`. Seed a Project row and AgentTask rows the way `DispatchConcurrencyPipelineTests.SeedTaskAsync` does; initialise the concurrency store with `DispatchConcurrencySettingsService.EnsureInitializedAsync` in a scope. Class attributes `[Category("Integration")]`, `[NotInParallel("card-0505-advisory-lock")]`.

### Named tests

| ID | Test | Asserts |
|---|---|---|
| V-1 | `EffectiveSettingsPipelineTests.Scoped_read_carries_runners_and_defaults_matching_their_routes` | After `RunnerDefaultSettingsService.EnsureInitializedAsync`: `GET /api/agent-tasks/pipeline?projectId=P` returns `runners` whose JSON property names and values equal `GET /api/session-runners` row-for-row, ignoring only `capacityObservedAt`, `platformObservedAt` and `codexCliVersionCheckedAtUtc`; `runners[desktop].capacityKind == "delegatedTasks"`, `capacity == 2`, `occupied == 1` (one seeded local Working Code task; a seeded `CapacityWaitRetained` task is excluded); `runners[grok-linux].dispatchEligible == false`, `capacity == null`. `runnerDefaults.revision == 1`, `globalRunnerId == "grok-linux"`, `kindDefaults` empty, `supportedKinds` has 3 entries. The fleet read carries the same `runners` and `runnerDefaults` as the scoped read (minus the timestamps above). |
| V-2 | `EffectiveSettingsPipelineTests.Uninitialised_defaults_read_null_and_seed_nothing` | With the concurrency store initialised and no runner-defaults row: the GET returns `runnerDefaults` as JSON null and `RunnerRoutingSettings` row count is 0 after the read. After `EnsureInitializedAsync`: `runnerDefaults.revision == 1`; two further GETs leave `RunnerRoutingSettings` revision 1 and `RunnerRoutingRevisions` count unchanged (the read never writes). |
| V-3 | `EffectiveSettingsPipelineTests.Host_remaining_clamps_and_open_count_excludes_blocked_and_specialists` | Seeded in project P: Working Code (local), Queued Plan, Blocked Review, Working Check. `hosts[local]`: `inFlight 1`, `effectiveLimit 2`, `remaining 1`, `source "config"`; `hosts[grok-linux]`: `effectiveLimit null`, `remaining null`. After `HostBudgetService.UpsertAsync("local", 0, "drain")`: `effectiveLimit 0`, `inFlight 1`, `remaining 0` (clamped, not −1), `source "budget"`. `concurrencyScopes.Single()`: `projectId P`, `effective.maxParallel 6`, `maxParallelSource "default"`, `occupancy.open 2`, `parallel 1`, `queued 1`, `parallelRemaining 4`; role `Code` `open 1`, `parallelRemaining 1` (seed 2); role `Review` `open 0`; `taskScope "project"`, `hosts[].scope "fleet"`. Expected literals are written by hand, not computed from the production calculator. |
| V-4 | `StandingPipelinePolicyDocumentationTests.the_policy_phrases_are_pinned_in_every_copy` (4 arguments, updated) and `OrchestratorInstructionsGuidanceTests.The_policy_copies_keep_every_pinned_phrase_and_each_names_the_file` (updated) | Every policy copy contains the new `Phrases` (including `effective-settings route`, `/api/agent-tasks/pipeline?projectId=`, `CARD-0881`, `up to four`, `at most six`, `use the lower effective stage cap`, `-Runner server2`, `absolutely requires`, `depth cap`, `-IgnoreConcurrencyLimit`, `axis`) and none of `three-route`, `GET /api/session-runners`, `GET /api/runner-defaults`. |
| V-5 | `StandingPipelinePolicyDocumentationTests.the_effective_settings_route_is_documented` (new) | ops-http and antiphon-api name the route with `runnerDefaults`, `runners`, `remaining` and `CARD-0881`; AGENTS.md contains `Use its limits`. |
| R-1 | `DispatchConcurrencyPipelineTests` (5), `AgentTaskPipelineStatusTests` (39 results), `HostEndpointTests` (4) | The scoped/fleet contract, the null-directory construction path and the host/pipeline local-limit agreement are unchanged by the optional constructor parameters. |
| R-2 | `InstructionBundleTests` (74 results, includes the 14,310 cap and the worst-case argv composition), `TaskPlatformGuidanceTests` + `RunnerDefaultGuidanceTests` (9), `CheckpointRepeatDocumentationTests` (1, re-pinned bytes), `OrchestratorInstructionsGuidanceTests` (4) | The Platform paragraph keeps `GET /api/runner-defaults` and `GET /api/session-runners`; the bundle stays under budget; the byte pin matches the committed LF file. |

### Positive controls (post-land Mutation; paused, NOT run in Code)

| PC | Mutation (method-scoped, restore after) | Expected red test |
|---|---|---|
| PC-1 | `AgentTaskPipelineStatusService.GetAsync`: set `Runners = []` regardless of the directory | V-1 (`runners` empty, desktop row missing) |
| PC-2 | `RunnerDefaultSettingsService.ReadAsync`: call `EnsureInitializedAsync` before projecting | V-2 (row count 1 after the uninitialised read) |
| PC-3 | `HostLimitSummaryDto.Remaining`: drop `Math.Max(0, …)` | V-3 (`remaining` −1 instead of 0 after the zero budget) |
| PC-4 | `server/Bundles/orchestrator.md`: reinsert `CARD-0881 will replace the three-route read.` | V-4 (negative pin), R-2 (byte pin) |

### Checkpoints

Closed list. Each row is one isolated build and one exact filter, run through the checkpoint tool (`dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-10-card-0881-effective-settings-pipeline-plan.md --rows CP-n`, then `wait` while the exit is 75) on server2/Linux with the task platform unpinned (Any). No row requires Windows. The declared red-first run of CP-1 (tests written, S1 production edit absent) is the same row and is reported with its red receipt; reruns are counted on the row. Delete every `bin-c881-*` directory before finishing.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c881-s1/` | effective-settings-wire | `/*/*/(EffectiveSettingsPipelineTests*)\|(DispatchConcurrencyPipelineTests*)\|(AgentTaskPipelineStatusTests*)\|(HostEndpointTests*)/*` | V-1, V-2, V-3, R-1 | all listed, 0 failed, 0 skipped: 3 new + 5 + 39 + 4 | 51 | 12 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c881-s2/` | policy-pins | `/*/*/(StandingPipelinePolicyDocumentationTests*)\|(OrchestratorInstructionsGuidanceTests*)\|(InstructionBundleTests*)\|(TaskPlatformGuidanceTests*)\|(RunnerDefaultGuidanceTests*)\|(CheckpointRepeatDocumentationTests*)/*` | V-4, V-5, R-2 | all listed, 0 failed, 0 skipped: 11 + 4 + 74 + 9 + 1 | 99 | 6 |

Cost: ordinary floor 18 minutes of checkpoint time plus one red-first CP-1 (12) and roughly 20 minutes of editing: 50 minutes, inside the 30-60 minute budget. Any other build or test command is unlisted and must be reported with a reason.

## Activation, rollback and follow-ups

Activation is the ordinary land plus AppHost restart; confirm with `GET /api/version` and then `GET /api/agent-tasks/pipeline?projectId=<Antiphon project>` showing `runners` and `runnerDefaults`. The bundle version changes on rebuild; orchestrators started after the restart receive the new text, and the generated instructions file is unaffected (its builder and renderer are untouched). Rollback is a revert of the two slices; the JSON additions are additive and no client reads them.

Follow-ups for the caller to file if wanted (not blockers): (1) "Dispatch-concurrency seed origin: distinguish code default from configuration leaf in `default` sources" (D-3); (2) "Collapse `AgentTaskPipelineStatusService` remote occupancy and `SessionRunnerCatalogue` occupancy into one helper" (the two compute the same sessions + pending + mirrors sum; kept duplicated here to leave both contracts byte-identical).

Collision note for the caller: S1 touches `AgentTaskPipelineDtos.cs`, `AgentTaskPipelineStatusService.cs` and `RunnerDefaultSettingsService.cs`; S2 touches the orchestrator bundle, AGENTS.md, the loop doc, the skill and both API docs. Defer this Code task while any in-flight Code task edits those paths.

## Results

Code task `7b32e72e` on `feat/card-task-7b32e72e` in `/work/worktrees/task-7b32e72e`. Base `b30a3d8e746d7b454d2a534e564520a83a298e98`. Bound (token present, not printed). Post-land Mutation was not run; PC-1..PC-4 stay pending. Restart: none. Landing owner is this task. Closed checkpoint table was not rewritten. No unlisted build or test. `builds: 2` on each report is the tool's own count, not an extra driver.

Bundle after S2, LF file `server/Bundles/orchestrator.md`: length 13379 (pin ceiling 14310; plan projection 13379). `InstructionBundles` version prefix `b202af70` (SHA-256 of the trimmed LF text; plan baseline `884bd8a4`). File SHA-256 `cc5dfdfc6fd462fd162d21d1de5a07f982e94c068d7db578856e30fa0c4bf057` (was `80b0c0be058db4f78ab068c05eeb47deac5966bf2d9aa97a7225a35de1e1f56a`). Argv headroom is the CP-2 `InstructionBundleTests` assertion `budget - 500` (29500); that class passed, and the success path does not print the composed length.

Deviations from the plan text, kept so the pins stay true:

- `docs/orchestration-loop.md` item 1 still contains `use the lower effective stage cap`. The plan's replacement omitted that phrase, and it occurs only in item 1 inside the standing-policy slice. The sentence is "Antiphon's enforced limits are the ceiling, so use the lower effective stage cap: the lower of four and the enforced stage limit applies."
- The stale-phrase loop is in both pin classes. The plan snippet named only `StandingPipelinePolicyDocumentationTests`; V-4 also names `OrchestratorInstructionsGuidanceTests`.
- The `docs/antiphon-api.md` sentence uses `` `/api/agent-tasks/pipeline?projectId=<guid>` `` so the file contains that contiguous string. The plan's append said `` `?projectId=<guid>` ``, which is not that string, and the row did not already contain it.

CP-1 roster is 78, not the plan's 51. `AgentTaskPipelineStatusTests` on this base reports 66 results, not 39. Min 51 holds. Green run: 78 passed, 0 failed, 0 skipped.

### CP-1 compile failure (missing usings; run 20261010-190256-9b99)

The executor granted build lease `09a133d7` and released it. The row line says `slot=skipped`.

```
CHECKPOINT CP-1 commit=7d3788440f77e76c4007639860bdc5faca2ffb46 build=failed filter=/*/*/(EffectiveSettingsPipelineTests*)|(DispatchConcurrencyPipelineTests*)|(AgentTaskPipelineStatusTests*)|(HostEndpointTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=7d3788440f77e76c4007639860bdc5faca2ffb46 sourceState=clean buildSource=unknown
```

### CP-1 red (run 20261010-190712-1585)

executed=78 passed=75 failed=3 skipped=0. The three failures are the missing JSON properties `runners`, `runnerDefaults`, and `remaining`.

```
CHECKPOINT CP-1 commit=1a562d8395e34a8992884a00686fffcaacbc09cc build=ok filter=/*/*/(EffectiveSettingsPipelineTests*)|(DispatchConcurrencyPipelineTests*)|(AgentTaskPipelineStatusTests*)|(HostEndpointTests*)/* executed=78 passed=75 failed=3 skipped=0 trx=/work/worktrees/task-7b32e72e/.antiphon/checkpoints/20261010-190712-1585/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=1a562d8395e34a8992884a00686fffcaacbc09cc sourceState=clean buildSource=verified
```

### CP-1 green (run 20261010-191127-c8d2)

executed=78 passed=78 failed=0 skipped=0.

```
CHECKPOINT CP-1 commit=3891db5b6054cc126cd33962fbfdf2a382ee7d59 build=ok filter=/*/*/(EffectiveSettingsPipelineTests*)|(DispatchConcurrencyPipelineTests*)|(AgentTaskPipelineStatusTests*)|(HostEndpointTests*)/* executed=78 passed=78 failed=0 skipped=0 trx=/work/worktrees/task-7b32e72e/.antiphon/checkpoints/20261010-191127-c8d2/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=3891db5b6054cc126cd33962fbfdf2a382ee7d59 sourceState=clean buildSource=verified
```

### CP-2 compile failure (Shouldly overload; run 20261010-192441-c87e)

`StandingPipelinePolicyDocumentationTests.cs` CS1503: `ShouldContain(string, string)` bound to the char predicate. The executor granted build lease `cbaed9b6-e2ce-41fa-b572-7e0ca6a8799d` and released it. The row line says `slot=skipped`. Fixed in `c18147511` by passing `Case.Sensitive`.

```
CHECKPOINT CP-2 commit=c998782c6ea19d571057ff181f466fc298dfa945 build=failed filter=/*/*/(StandingPipelinePolicyDocumentationTests*)|(OrchestratorInstructionsGuidanceTests*)|(InstructionBundleTests*)|(TaskPlatformGuidanceTests*)|(RunnerDefaultGuidanceTests*)|(CheckpointRepeatDocumentationTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=c998782c6ea19d571057ff181f466fc298dfa945 sourceState=clean buildSource=unknown
```

### CP-2 green (run 20261010-192656-a6da)

executed=99 passed=99 failed=0 skipped=0. Tested source `c18147511dd0aa5756b086f8adbb6594da49f909`.

```
CHECKPOINT CP-2 commit=c18147511dd0aa5756b086f8adbb6594da49f909 build=ok filter=/*/*/(StandingPipelinePolicyDocumentationTests*)|(OrchestratorInstructionsGuidanceTests*)|(InstructionBundleTests*)|(TaskPlatformGuidanceTests*)|(RunnerDefaultGuidanceTests*)|(CheckpointRepeatDocumentationTests*)/* executed=99 passed=99 failed=0 skipped=0 trx=/work/worktrees/task-7b32e72e/.antiphon/checkpoints/20261010-192656-a6da/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=c18147511dd0aa5756b086f8adbb6594da49f909 sourceState=clean buildSource=verified
```

