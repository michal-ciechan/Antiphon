# CARD-0366 — scope the CARD-0147 concurrency gate per project, not fleet-wide

**Plan pass, 2026-09-04 (task 11402298). Design only; no production code is changed by this plan.**

**Card:** CARD-0366 — Concurrency limit should be scoped per project, not fleet-wide.

**Extends:** CARD-0147 (S1 create-time gate `da5d55ee`, S2 `-IgnoreConcurrencyLimit` `9c815fbf`, S3 worktree-health sweep `5371de31`; plan `docs/superpowers/plans/2026-09-03-card-0147-concurrency-limit-and-stuck-worktree-health-plan.md`). This card changes the **key the gate counts by**. It does not replace the gate, move it off the create path, change the lock, change the override, or touch the sweep.

**Verified against:** worktree `feat/card-task-11402298` @ `0c17e0dc`. All file:line citations below were read at that commit.

---

## Verdict up front

**The scope key already exists on the row: `AgentTask.ProjectId`, the `Project` entity's id.** That is exactly the guid the `AgentTask.Created` event prints as `project scope: <guid>`. It is set once at create from caller provenance (parent task, else the calling session's card → board → project, else the session's standing agent → board → project) and is `null` only when no trustworthy project identity exists (CARD-0115 S1).

**Today the gate ignores it and counts fleet-wide.** `DelegationOpenGate.LoadSnapshotAsync` filters on non-specialist role and open status only — no `ProjectId` predicate anywhere in the class. The advisory lock key is a single global string. So Gym Stat's three open tasks refuse Antiphon's next dispatch and the 409 names Gym Stat's occupants to the Antiphon operator. The card's suspicion is correct.

**The change is one predicate plus plumbing.** Resolve `projectId` (already computed before the gate runs), pass it into the gate, add `t.ProjectId == projectId` to the count, carry it on the snapshot and the 409 payload, and say which project the numbers describe. No migration, no new setting, no new lock key. `-IgnoreConcurrencyLimit` keeps its shape. `MaxConcurrentTasks` (the dispatcher's host-process ceiling) stays fleet-wide on purpose.

---

## Ground truth (checked, not guessed)

Line numbers at `0c17e0dc`.

### What `project scope: <guid>` is

- `AgentTask.ProjectId` — `server/Domain/Entities/AgentTask.cs:50-57`. Nullable `Guid`. Doc-comment: *"The project on whose behalf this task runs … Set once at creation from caller provenance (the parent task, or the calling session's card/board binding), never from a filesystem path … Null means no trustworthy project identity."* Configured nullable, no index: `server/Infrastructure/Data/AppDbContext.cs` AgentTask block (`entity.Property(t => t.ProjectId).IsRequired(false)`; the block's indexes are `Status`, `RootTaskId+CreatedAt`, `ParentTaskId`, `AgentSessionId`, `TokenHash`, `CardId`, `CardId+Role+CreatedAt`, `LandRequestedAt` — none on `ProjectId`).
- It is a `Project` row id — `server/Domain/Entities/Project.cs` (*"Projects are the top-level container for workflows"*; has `Boards`, `ApiKeys`, `LocalRepositoryPath`, `ArchivedAt`). `Board.ProjectId` is required (`server/Domain/Entities/Board.cs:8`; `AppDbContext.cs:569`), so every board belongs to exactly one project and a project can own several boards.
- **Where the value is produced at create** — `AgentTaskService.CreateAsync`, `server/Application/Services/AgentTaskService.cs:619-624`:
  ```csharp
  var projectId = parent is null
      ? await DeriveCallerProjectAsync(caller, ct)
      : parent.ProjectId;
  ```
  `parent` is `caller.Task` (`:294`). `DeriveCallerProjectAsync` (`:1031-1053`): returns `null` when the caller is a task or has no session id; else the session's `CardId → Card.BoardId → Board.ProjectId`; else the standing agent found by `Agent.PersistentSessionId == sessionId` → `Agent.BoardId → Board.ProjectId`.
- **Where it is stored** — `ProjectId = projectId` on the new row (`:669`). The merge-conflict child copies its parent's (`:1654`). Retry keeps the original row's value (`AgentTaskProjectScopeTests.retrying_a_row_keeps_its_original_project_scope`).
- **Where the event text comes from** — `ProjectScopeSuffix` (`:1055-1056`): `" — project scope: {id}"`, appended to the `Created` detail at `:761` and to the conflict child's at `:1683`.
- **When it is null** — a tokenless caller (UI `DelegateModal`, curl) is `Caller(null, null, "")` (`server/Api/Endpoints/AgentTaskEndpoints.cs:198-206`), so `DeriveCallerProjectAsync` returns null. A session whose card has no board binding and whose agent has no board also yields null. Every pre-CARD-0115 row is null.
- **Live evidence (2026-09-04, dev Postgres `antiphon-postgres`, read-only query)**: open rows group as Antiphon `d4ea7ae9-…` (1 Dispatched, 1 Blocked) and gym-stat `1f30295a-…` (1 Dispatched, 6 Blocked). **No open row has a null `ProjectId`.** Historically 1057 of 1395 rows are null (pre-CARD-0115 residue), all settled. The projects table holds Antiphon, gym-stat, az-care and ~20 archived test-fixture projects.

### What the gate does today

- `server/Application/Services/DelegationOpenGate.cs`
  - `:17` `AdvisoryLockKey = "antiphon.delegation.max-open-tasks"` — one global key; `TakeLockAsync` `:102-105` runs `pg_advisory_xact_lock(hashtext(key))`.
  - `:19-24` `OpenStatuses` = Queued, Dispatched, Working.
  - `:42-53` `Snapshot(Open, Role, AbsoluteLimit, RoleLimit)`; `AbsoluteCount` = `Open.Count`; `RoleCount` filters `Open` by role.
  - `:60-71` `EnsureCanCreateAsync(role, ignoreConcurrencyLimit, ct)` — lock, snapshot, throw unless ignore. **No project parameter.**
  - `:73-92` `ToProblem` — for the `role` axis filters `Open` to the role; for `absolute` lists all of `Open`. Occupant list capped at `OccupantListCap` (12).
  - `:107-128` `LoadSnapshotAsync` — **the fleet-wide count**: `.Where(AgentTaskRoles.NotSpecialist).Where(t => OpenStatuses.Contains(t.Status))`, no `ProjectId` predicate.
- Call site — `AgentTaskService.cs:726-744`: `gateCreate` when the gate is registered, the role is not a specialist and this is not a live follow-up; opens a transaction if none is current; calls `EnsureCanCreateAsync(request.Role, request.IgnoreConcurrencyLimit, ct)`. **`projectId` is already resolved 100 lines earlier (`:622`), so it is in scope at the call.** Override Warning at `:783-795` via `ConcurrencyLimitException.FormatOverrideWarning(count, limit, role, roleCount, roleLimit)`.
- Settings — `server/Application/Settings/DelegationSettings.cs:24-29` `MaxOpenTasks = 3` (doc-comment: *"absolute create-time cap on non-specialist tasks"*); `:303-317` every named RolePolicy entry ships `RecommendedInFlight = 1`; `:881-882` `RecommendedInFlightFor(role)`; validator `:992-1002` (positive-int for both). `MaxConcurrentTasks = 6` (`:22`) is the dispatcher's Dispatched+Working process ceiling.
- Exception — `server/Application/Exceptions/ConcurrencyLimitException.cs`: `FormatDetail` `:30-46` builds `"{n} [Role ]task(s) already in flight (limit {L}): occupants. Coda"`; `FormatOverrideWarning` `:55-69`; `ConcurrencyLimitProblemDto(Axis, Role, Count, Limit, Open, Override)` `:72-78`; `ConcurrencyLimitOccupantDto` `:81-87`. Nothing outside the gate constructs either DTO (grep, server + tests).
- Registration — `server/Program.cs:303` `AddScoped<DelegationOpenGate>()`; `AgentTaskService` takes it as an optional ctor arg (`AgentTaskService.cs:74`).
- Tests — `tests/Antiphon.Tests/Application/AgentTaskConcurrencyLimitTests.cs` (533 lines). Every test uses an isolated schema, seeds rows without a `ProjectId` and creates through `ManualCaller(null, null, dir)` — so **every existing test lives in the null-project bucket and stays valid unchanged** under this design. `AgentTaskProjectScopeTests.cs:151-260` already has `SeedProjectBoardAsync` / `SeedCardAsync` / `SeedSessionAsync` / `SeedStandingAgentAsync` helpers for project-bound callers.
- Docs that describe the cap as fleet-wide — `docs/antiphon-api.md:258` (*"omits the create-time fleet/role cap"*) and `:271-277` (*"would push the fleet past `Delegation:MaxOpenTasks`"*); `scripts/delegate.ps1:13-14` and `:125-128` (*"fleet/role in-flight cap"*). `GET /api/agent-tasks/pipeline` is documented as *"fleet-wide advisory"* (`docs/antiphon-api.md:250`; `AgentTaskPipelineStatusService.cs:12`, `:158`).
- **Incidental gap found:** CARD-0147 S2 planned a "sequential-by-default / 409 means wait or the flag" sentence for `docs/orchestration-loop.md` and `server/Bundles/orchestrator.md` (D11). Neither file mentions `concurrency` today (grep). The docs slice below lands the sentence in its now-project-scoped form.

---

## Decisions

### D1. Scope key = `AgentTask.ProjectId`, exactly the value in the Created event

Not the board (a project may own several boards; the API-key scope already chose project over board in CARD-0115, and a card can be on a different board from the agent running it). Not the working directory (CARD-0115 rejected path derivation permanently: sibling worktrees make prefix matching unsafe, `ApiKeyEnvResolver.cs:21-26`). Not a new "workspace" entity — the row already carries the right thing.

The gate consumes the same `projectId` local that the row is written with (`AgentTaskService.cs:622` / `:669`), so the count key and the stored key can never disagree.

### D2. Both axes count within the project

- Absolute: `open(project) >= MaxOpenTasks` → 409 `absolute`.
- Role: `open(project, role) >= RecommendedInFlightFor(role)` → 409 `role`.

The predicate is the one addition to `LoadSnapshotAsync`: `.Where(t => t.ProjectId == projectId)`. EF translates `== null` to `IS NULL`, so the null case needs no special branch (confirm in the test, D3). Everything downstream (`Snapshot`, `ToProblem`, occupant labels, `stuck:` join) already operates on `Open` and therefore becomes project-scoped for free. **Do not add a second project filter in `ToProblem`** — the snapshot is the filtered set.

### D3. Null `ProjectId` is its own bucket

A create with no project identity counts only occupants with no project identity, and is named only to them.

| Alternative | Why not |
|---|---|
| Count null-project tasks against **every** project | Re-creates exactly the cross-project throttle this card removes, for the callers least able to say which project they meant (UI, curl), and puts unrecognisable occupants back in the 409. |
| **Exempt** null-project creates from the gate | A tokenless UI storm would be ungated; the null bucket is the honest backstop. |
| Refuse null-project creates | Breaks the UI modal and every pre-binding session. Out of the question. |

Live evidence says this bucket is empty today, so the choice costs nothing operationally and keeps one invariant: *a count is always keyed by exactly one scope value, and a refusal never names another scope's tasks.*

### D4. `RecommendedInFlight` stays a global per-role default; the project is the count's dimension, not the policy's

The card's "probably the former" is right. `RolePolicy` is the role → tier ladder; a per-project override would need a new `Project.DelegationOverridesJson`-style field, a validator, an editor surface and a precedence rule, for a number nobody has asked to vary. If a project ever needs Code=2 while others stay at 1, that is a follow-up card, not this one. Same for `MaxOpenTasks`: one global default, applied independently within each project's count.

### D5. The 409 says which project it counted, and lists only that project's occupants

- `DelegationOpenGate.Snapshot` gains `Guid? ProjectId` (last positional member).
- `ConcurrencyLimitProblemDto` gains `Guid? ProjectId` (last positional member; camelCase `projectId` on the wire; null when the create was unscoped). Nothing else constructs the record, so appending is safe.
- `FormatDetail` sentence, scoped: `3 tasks already in flight in project d4ea7ae9 (limit 3): …` / `1 Debug task already in flight in project d4ea7ae9 (limit 1): …`. Unscoped: today's sentence, verbatim. Use `DelegationReportFormatter.Short(projectId)` for the 8-hex form — it is the same guid the Created event prints in full, so an operator can match them. The `Coda` is unchanged.
- `FormatOverrideWarning` gains `Guid? projectId` and the same `in project xxxxxxxx` clause after the counts, so the recorded Warning says which scope's numbers were overridden. Existing assertions (`3/3`, `limit 3`, `Plan`, `ignoreConcurrencyLimit`) keep passing.
- Occupant filtering to the same project is inherent (D2). A test pins it against seeded rows from another project (V2, V3).

### D6. Keep the single global advisory lock key

Two projects' creates serialise on one `pg_advisory_xact_lock` for the milliseconds of count+insert. That is not what the card means by "block": the refusal decision is what must be independent, and it is (D2). A per-project key (`pg_advisory_xact_lock(hashtext(key), hashtext(projectId::text))`) would buy parallel creates across projects at the cost of a second code path, a null-key special case and a changed `two_concurrent_creates_…` test, for throughput nobody is short of. Rejected for now; revisit only if create latency is ever measured as lock-bound.

### D7. What stays fleet-wide, deliberately

| Knob | Stays fleet-wide? | Why |
|---|---|---|
| `Delegation:MaxConcurrentTasks` (6), dispatcher tick | **Yes** | It bounds pty processes on this host. A machine resource is not a project resource. Unchanged since CARD-0147 D3. |
| `GET /api/agent-tasks/pipeline` `RecommendedInFlight` / `atOrAboveRecommendation` | **Yes** | Read model, advisory, documented fleet-wide (`antiphon-api.md:250`). A `?projectId=` filter is a small follow-up if an operator wants a per-project view; not needed for the gate to be correct. |
| Shared-writer lease (CARD-0063), per-root guards | Yes | Already keyed on repo / root, not on fleet. |

### D8. No schema change, no migration, no new setting

`IX_AgentTasks_Status` narrows to the handful of open rows before the project predicate runs; a `(ProjectId, Status)` index would index 1395 rows to save nothing. No new `DelegationSettings` member: the same two numbers apply per project.

### D9. Exemptions and override unchanged in shape

Specialists (`AgentTaskRoles.IsSpecialist`) and live follow-ups skip the gate exactly as before (`AgentTaskService.cs:728-730`). `ignoreConcurrencyLimit` / `-IgnoreConcurrencyLimit` remain a one-shot per-dispatch body flag; `scripts/delegate.ps1:447` and `DelegateScriptKindTests` are untouched. The merge-conflict child (the row built at `:1654`, event at `:1683`) is server-spawned and never went through the gate; still doesn't.

### D10. Signature

```csharp
public async Task<Snapshot> EnsureCanCreateAsync(
    Guid? projectId,            // new, first — it is the scope, everything else is within it
    AgentTaskRole role,
    bool ignoreConcurrencyLimit,
    CancellationToken ct)
```

`LoadSnapshotAsync(Guid? projectId, AgentTaskRole role, ct)` likewise. The only caller is `AgentTaskService.cs:743`.

---

## Worked examples (defaults: 3 absolute, 1 per named role)

| Antiphon open | Gym Stat open | New create (scope) | Result |
|---|---|---|---|
| Plan W, Code Q | Code W, Debug W, Custom Q | Code (Antiphon) | **409 role** Code 1/1 — names Antiphon's Code only |
| Plan W, Code Q | Code W, Debug W, Custom Q | Debug (Antiphon) | **200** — Antiphon 2/3, no Antiphon Debug; Gym Stat's three are invisible |
| Plan W, Code Q, Custom D | Code W, Debug W, Custom Q | Test (Antiphon) | **409 absolute** 3/3 — lists Antiphon's three, never Gym Stat's |
| Plan W, Code Q, Custom D | — | Custom (Gym Stat) | **200** — Gym Stat 0/3 |
| — | Code W, Debug W, Custom Q | Custom (no project, UI) | **200** — null bucket 0/3 |
| 3 × Custom (null project) | — | Custom (no project, UI) | **409 absolute** 3/3 — null bucket, as today |
| Plan W, Code Q, Custom D | anything | Code (Antiphon) + override | **200** + Warning `… 3/3 open (limit 3) in project d4ea7ae9 …` |
| 6 Dispatched across all projects | — | anything, any project, override | 200 **Queued**; dispatcher still skips at `MaxConcurrentTasks` (D7) |

(W = Working, Q = Queued, D = Dispatched.)

---

## Implementation slices

Both slices fit one Code dispatch, `-Worktree`, ~2–3 h. Land S1 before or with S2; there is no reason to split the dispatch.

### S1 — Project-scoped gate

**Files:** `server/Application/Services/DelegationOpenGate.cs`, `server/Application/Exceptions/ConcurrencyLimitException.cs`, `server/Application/Services/AgentTaskService.cs`, `tests/Antiphon.Tests/Application/AgentTaskConcurrencyLimitTests.cs`.

1. `DelegationOpenGate`: add `Guid? projectId` to `EnsureCanCreateAsync` / `LoadSnapshotAsync`; add `.Where(t => t.ProjectId == projectId)` to the count query; add `Guid? ProjectId` to `Snapshot`; pass it into `ToProblem` → DTO. Update the class doc-comment (*"count-and-insert for the create-time concurrency cap, keyed by the task's project scope (CARD-0366)"*).
2. `ConcurrencyLimitException`: `ConcurrencyLimitProblemDto` gets `Guid? ProjectId` last; `FormatDetail` and `FormatOverrideWarning` add the `in project xxxxxxxx` clause when non-null (D5). Class doc-comment: *"a create that would push this project — or this role within it — past the in-flight cap"*.
3. `AgentTaskService.CreateAsync:743`: pass `projectId`. `:789-795`: pass `projectId` to `FormatOverrideWarning`.
4. `DelegationSettings.MaxOpenTasks` doc-comment (`:24-28`): *"per project scope (`AgentTask.ProjectId`; tasks with no project scope form their own bucket)"*. `RolePolicyEntry.RecommendedInFlight` doc-comment (`:867`): same one-clause note.
5. Tests — see Verification design. Extend the existing file's `SeedTaskAsync` with an optional `Guid? projectId` and add a `SeedOrchestratorParentAsync(db, dir, projectId)` helper (Kind = `Orchestrator`, Role = `Custom`, Status = `Working`, `ProjectId` set) so a project-scoped caller is `new Caller(parent, null, dir)` — the child inherits `parent.ProjectId` (`:622-624`), no session/card/board seeding needed. Note the parent itself is one open Custom in its project; the expected counts below include it.

**Verify:** `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c0366/ -- --treenode-filter "/*/*/AgentTaskConcurrencyLimitTests/*"` and `"/*/*/AgentTaskProjectScopeTests/*"` (the latter pins the provenance chain this design leans on; it must stay green untouched). Delete the `bin-c0366` directories afterwards.

### S2 — Docs that currently say "fleet"

**Files:** `docs/antiphon-api.md`, `scripts/delegate.ps1` (comments only), `docs/orchestration-loop.md`, `server/Bundles/orchestrator.md`.

1. `docs/antiphon-api.md:258`: *"omits the create-time project/role cap"*. `:271-277`: *"would push **its project scope** past `Delegation:MaxOpenTasks` … or that role **within the project** past …; tasks with no project scope form their own bucket; the `concurrency` extension names the axis, `projectId`, the occupants (same project only), and `override`"*.
2. `scripts/delegate.ps1:13-14`, `:125-128`: "fleet/role" → "project/role"; one clause: *"counts and occupants are the calling session's project only"*. No switch, parameter-set or body change — `DelegateScriptKindTests` must not need editing.
3. `docs/orchestration-loop.md` §2 "Launching an agent" (`:179`): one paragraph — dispatch is sequential-by-default; a 409 `concurrency_limit` names **this project's** occupants and cap; wait, or re-send with `-IgnoreConcurrencyLimit` only when the user asked for parallel work this turn; other projects' work never counts against yours. (This also lands the CARD-0147 D11 sentence that never arrived.)
4. `server/Bundles/orchestrator.md`: one sentence after the "Child work goes through `delegate.ps1`" paragraph (`:48-54`), same rule, same project clause.

AGENTS.md is the index; nothing to add there.

---

## Verification design

All integration tests use `TestDbFixture.CreateIsolatedSchemaAsync()` like the rest of the file (own schema per test, so counts are exact — Gotcha #24 does not bite). Add to `AgentTaskConcurrencyLimitTests`. Names are the assertions.

**Positive controls (independence across projects)**

- **V1 `two_projects_each_near_their_own_cap_do_not_block_each_other`** — P: orchestrator parent (Working) + 1 Custom Queued = 2 open; Q: orchestrator parent (Working) + 1 Custom Queued = 2 open; fleet = 4 ≥ 3. Create Custom under P's parent → `Queued`; create Custom under Q's parent → `Queued`. Both rows exist afterwards with the right `ProjectId`. This is the fleet-count-would-have-refused case and the heart of the card.
- **V2 `a_role_slot_taken_in_another_project_is_free_in_this_one`** — P: parent only; Q: parent + Debug Working. Create Debug under P → `Queued`.
- **V3 `null_project_tasks_form_their_own_bucket`** — 3 open Custom with `ProjectId = null`; P: parent only. Create under P → `Queued`. Then a `ManualCaller` (no project) create → 409 absolute whose `Open` is exactly the three null rows and does **not** contain P's parent.

**Negative controls (same project still refuses, and names only itself)**

- **V4 `same_project_at_the_cap_is_refused_and_names_only_its_own_occupants`** — P: parent + 2 open = 3; Q: 3 open with distinctive titles. Create Test under P → 409 `absolute`, `Count == 3`, `Limit == 3`, `Concurrency.ProjectId == P`, `Open` ids == P's three ids, none of Q's; `Message` contains `in project {Short(P)}` and contains **no** Q short id or title; no row inserted.
- **V5 `same_project_role_axis_still_refuses_and_lists_only_this_projects_role_occupant`** — P: parent + Debug Working; Q: Debug Working. Create Debug under P → 409 `role`, `Role == "Debug"`, `Count == 1`, `Open` single == P's Debug.
- **V6 `override_at_the_project_cap_creates_and_the_warning_names_the_project`** — P: parent + 2 open; create Plan under P with `IgnoreConcurrencyLimit = true` → `Queued`; the Warning event contains `3/3`, `limit 3`, `Plan`, `ignoreConcurrencyLimit`, and `in project {Short(P)}`.
- **V7 `an_unscoped_refusal_keeps_the_unscoped_sentence`** — existing `fourth_create_at_three_open_returns_409_concurrency_limit` plus one assertion: `Concurrency.ProjectId` is null and `Message` does not contain `in project`. (Pins that the wire shape for today's callers is unchanged apart from the new nullable field.)
- **V8 `the_lock_still_serialises_creates_within_one_project`** — optional; the existing `two_concurrent_creates_when_max_open_is_one_…` already covers the lock in the null bucket. If added, `MaxOpenTasks = 2`, two concurrent creates under the same P parent → exactly one 200, one 409. Do not weaken the timeout.

**Regression guards**

- Every existing test in `AgentTaskConcurrencyLimitTests` passes without assertion edits (all null-bucket).
- `AgentTaskProjectScopeTests` unchanged and green (provenance chain).
- `DelegateScriptKindTests` unchanged and green (no script contract change).
- `AgentTaskPipelineStatusTests` unchanged and green (pipeline stays fleet-wide, D7).

**Live spot-check after deploy (optional, no writes needed)** — read the next real `AgentTask.Created` event from each of the Antiphon and Gym Stat boards; both still print `project scope:`. If a 409 ever appears in a Grok/Codex orchestrator's transcript, its detail must now read `… in project <8-hex>` and list only that board's tasks. Do not manufacture a 409 on the live stack; the integration tests are the proof.

---

## Out of scope

- Per-project overrides of `MaxOpenTasks` or `RecommendedInFlight` (D4).
- Per-project advisory lock keys (D6).
- Scoping `MaxConcurrentTasks` or the pipeline read model (D7); a `?projectId=` pipeline filter is a small follow-up card if wanted.
- Backfilling `ProjectId` on the 1057 historical null rows (all settled; the gate never sees them).
- Any change to the worktree-health sweep, `stuck:` labels, `-IgnoreConcurrencyLimit` semantics, or the UI modal.

---

## Files (expected)

| Slice | Touch |
|---|---|
| S1 | `DelegationOpenGate.cs`, `ConcurrencyLimitException.cs`, `AgentTaskService.cs` (2 call-site lines), `DelegationSettings.cs` (2 doc-comments), `AgentTaskConcurrencyLimitTests.cs` |
| S2 | `docs/antiphon-api.md`, `scripts/delegate.ps1` (comments), `docs/orchestration-loop.md`, `server/Bundles/orchestrator.md` |

---

## Acceptance

- With Gym Stat at 3 open non-specialist tasks, an Antiphon-scoped `delegate.ps1` create returns 200 and queues.
- With Antiphon at 3 open, a fourth Antiphon-scoped create returns 409 `concurrency_limit` whose sentence says `in project d4ea7ae9` and whose occupant list contains only Antiphon tasks.
- The same holds on the role axis (a Gym Stat Code task does not consume Antiphon's Code slot; an Antiphon Code task still does).
- A tokenless (UI) create counts only other tokenless open tasks.
- `-IgnoreConcurrencyLimit` still queues past the cap and its Warning names the project.
- `MaxConcurrentTasks = 6` dispatcher behaviour and the pipeline endpoint are unchanged.

[antiphon-plan: CARD-0366]
