# CARD-0506 plan — concurrency settings UI (global and per-project)

Plan task `9761afeb`, branch `feat/card-task-9761afeb`, source verified at `00fa6b884a5f127982cff26a677aae74e6666650`.
This document is the only deliverable of the Plan stage: no production code, no builds, no test runs.
Backend state is CARD-0505 as landed (investigation `docs/investigations/2026-10-13-card-0505-configurable-dispatch-concurrency-parallel-queued-limits-per-stage-project.md`); every claim below was re-read from the files named.

The card asks for three things: a global place to view and edit per-role parallel and queued limits, a per-project override of those limits, and an explicit revert that restores inheritance rather than leaving a stale copy. All three are purely client work plus one E2E; the API already does the rest.

## 1. Current state

### 1.1 Backend contract (server, landed)

| Route | Handler | Body / result |
|---|---|---|
| `GET /api/dispatch-concurrency` | `server/Api/Endpoints/DispatchConcurrencyEndpoints.cs:16` | `DispatchConcurrencyGlobalDto` (`server/Application/Dtos/DispatchConcurrencyDtos.cs:57`): `seed`, `overrides` (raw canonical JSON), `effective`, `nullProject`, `supportedRoles` (14 ordinary roles), `ranges`, `revision`, `updatedAt`, `reason`, `provenance`, `callerTaskId`, `occupancy`. |
| `PUT /api/dispatch-concurrency` | `:18`, `:31` | `PutDispatchConcurrencyRequest` (`DispatchConcurrencyDtos.cs:99`): `expectedRevision` (long, required), `expectedGlobalRevision` (ignored on global), `overrides` (object, required), `reason` (string), `provenance` (`Human`/`Auto`). Returns the new global DTO. |
| `GET /api/projects/{projectId}/dispatch-concurrency` | `:24` | `DispatchConcurrencyProjectDto` (`DispatchConcurrencyDtos.cs:71`): `projectId`, `overrides`, `inherited` (global effective), `effective`, `supportedRoles`, `ranges`, `revision` (0 when the project has no row), `globalRevision`, nullable `updatedAt`/`reason`/`provenance`, `occupancy`. Unknown project is 404 (`DispatchConcurrencySettingsService.cs:214`). |
| `PUT /api/projects/{projectId}/dispatch-concurrency` | `:26`, `:42` | Same body; `expectedGlobalRevision` is mandatory (`DispatchConcurrencyEndpoints.cs:117`, service `:543`). |
| `GET …/revisions?limit=&beforeRevision=` | `:19`, `:27`, `:54` | `DispatchConcurrencyRevisionPageDto` (`DispatchConcurrencyDtos.cs:95`): rows `revision`, `previousRevision`, `snapshot` (envelope with `scopeKey`, `revision`, `provenance`, `reason`, service `:582`), `createdAt`, `reason`, `provenance`; `nextBeforeRevision`. limit 1–100, default 50. |

Request strictness: the PUT body is parsed by hand; a duplicate or unknown top-level field, a missing required field, or a non-object `overrides` is a 422 before the service runs (`DispatchConcurrencyEndpoints.cs:107-128`, `:136-140`). The client must send exactly the five fields and nothing else.

Effective DTO shape (`DispatchConcurrencyDtos.cs:17-34`): `mode`/`modeSource`, `maxParallel` (int, never null)/`maxParallelSource`, `maxQueued` (int|null)/`maxQueuedSource`, `roles[]` with `role`, `maxParallel` (int|null), `maxParallelSource`, `maxQueued` (int|null), `maxQueuedSource`, `combinedParallel` (min of role and overall, `DispatchConcurrencyPolicy.cs:701-707`), plus `globalRevision`/`projectRevision`. Sources are the strings `project`, `global`, `default` (`DispatchConcurrencyPolicy.cs:18-20`). Occupancy (`DispatchConcurrencyDtos.cs:46-55`): `open`, `parallel`, `queued`, nullable `parallelRemaining`/`queuedRemaining`, overages, `exceedsBounds`, per-role rows.

Overrides document semantics (the heart of the UI model):

- A field is an override only when its key is present. Absent means inherit. Present with `null` means "specified, unbounded" for the nullable fields (`DispatchConcurrencyPolicy.cs:281-290`, `:627-636`). `overrides: {}` clears everything (`DispatchConcurrencySettingsService.cs:303`, `:330-331`).
- Resolution per field is project-specified → global-specified → seed (`DispatchConcurrencyPolicy.cs:471-479`). Overall `maxParallel` is resolved with `allowNull: false` (`:71`), overall `maxQueued` and every role field with `allowNull: true` (`:72`, `:81-89`), so an explicit null on a role or queued field really does override a numeric global.
- Validation (`DispatchConcurrencyPolicy.cs:169-213`, `:497-570`): `maxParallel` integer 1–512 and never null; `maxQueued` integer 0–4096 or null; `roles.<Role>.maxParallel` 1–512 or null; `roles.<Role>.maxQueued` 0–4096 or null; `mode` `LegacyOpen`/`SeparateQueues`; `schemaVersion` only 1; unknown field, unknown role or specialist role (Check/Distill/Diagnose) is an error. The ranges are also in the DTO `ranges` (`DispatchConcurrencyRangeDto`, `:5`), so the client reads them rather than hard-coding.
- The server rewrites what it stores to canonical JSON: `schemaVersion`, then `mode`, `maxParallel`, `maxQueued` only when specified, then `roles` with only specified fields, roles ordered by name (`DispatchConcurrencyPolicy.cs:589-625`). The GET `overrides` is that canonical form.

Write semantics: `reason` trimmed 1–400 chars, `provenance` `Human`/`Auto` (`DispatchConcurrencySettingsService.cs:533-546`). `expectedRevision` must equal the scope row's revision (0 for a project without a row) and, for a project, `expectedGlobalRevision` must equal the global revision (`:284-293`). An identical Human-on-Human write rolls back and returns the unchanged DTO (`:305-311`); a Human write onto a Migration/Auto row always creates a revision. Commit publishes SignalR `DispatchConcurrencyChanged` `{scope, revision, globalRevision, projectRevision}` to all clients (`:705-720`).

Error shapes (`server/Api/Middleware/ExceptionMiddleware.cs:55-80`): `application/problem+json` with `type`, `title`, `status`, `detail`, `traceId`, `code` when set, extension members, and `errors` (field → string[]) for 422. 422 code `validation_failed` (`server/Application/Exceptions/ValidationException.cs:10-14`); the override error keys are the camelCase JSON paths (`maxParallel`, `roles.Code.maxQueued`, `reason`, `overrides`). 409 `dispatch_concurrency_revision_conflict` carries `globalRevision` and `projectRevision` (`DispatchConcurrencySettingsService.cs:24`, `:548-556`); 409 `dispatch_concurrency_human` only for `Auto` provenance (`:295-300`, `:558-561`), unreachable from a UI that always sends `Human`.

Auth: there is no authorization layer; every request runs as the hardcoded admin (`server/Api/Middleware/CurrentUserMiddleware.cs:19-21`, `:35-38`), and the task-token header is only used to attribute `callerTaskId` (`DispatchConcurrencyEndpoints.cs:147`). The browser sends no token, so writes record `callerTaskId: null`.

Pipeline read: `GET /api/agent-tasks/pipeline?projectId=<guid>` returns `concurrencyScopes[]` of `{projectId, nullBucket, effective, occupancy}` (`server/Application/Dtos/AgentTaskPipelineDtos.cs:31`, `:56-64`) built by the same `ToEffective`/`ToOccupancy` the scope GETs use (`server/Application/Services/AgentTaskPipelineStatusService.cs:419-437`); scope parsing at `:310-323`, endpoint `server/Api/Endpoints/AgentTaskEndpoints.cs:20-28`. Documented at `docs/antiphon-api.md:460`. The dispatch-concurrency routes themselves have no row in `docs/antiphon-api.md` (grep count 0) and one row in `docs/ops-http.md:177`.

### 1.2 Client surfaces and conventions

- `/settings` → `SettingsPage` (`client/src/App.tsx:210`). Tabs are a fixed list read once from `?tab=` (`client/src/features/settings/SettingsPage.tsx:11-24`) and rendered uncontrolled with `defaultValue` and `keepMounted={false}` (`:31`); nothing navigates between tabs today.
- Routing tab (`RoutingSettingsTab.tsx:68-84`) stacks four self-contained sections, each owning its queries, loading `Loader` and failure `Text c="dimmed"` states (`:86-110`). `RunnerDefaultsSection.tsx` is the closest precedent for a revisioned editor: form keyed on `revision:updatedAt` (`:56`), `expectedRevision` from the loaded DTO (`:149-155`), Save disabled without a reason (`:209`), a conflict `Alert` that keeps the draft (`:158-160`, `:178-182`), notification on success/failure via `getApiErrorMessage` (`:61-70`), inline history list (`:213-235`).
- Projects tab: `ProjectConfig.tsx` table with per-row `ActionIcon` edit/delete carrying `aria-label` (`:297-318`); Edit Project is a `Modal size="md"` (`:346-351`) with about ten fields bound to `useUpdateProject` (`:185-229`), whose error handling flattens `errors` into one Alert (`:213-228`). Nothing concurrency-related exists in the client: `grep -rn 'dispatch-concurrency|DispatchConcurrency|maxParallel|maxQueued' client/src` is 0 lines.
- Host budgets already have their own editor on the Orchestrator page (`client/src/features/orchestrator/HostsPanel.tsx:14-45`, `usePutHostBudget` in `client/src/api/hosts.ts:99-103`); that is the operator-only surface AGENTS.md names and it stays there.
- Data layer: `apiGet`/`apiPut` throw `ApiError {status, body}` (`client/src/api/client.ts:6-18`, `:130-151`); `getApiErrorMessage` picks `errors` → `detail` → `title` (`:20-50`); `getApiFieldErrors` flattens `errors` to field → first message (`:64-83`). Query-key objects and `useMutation` with `invalidateQueries` on success are the pattern (`client/src/api/runnerDefaults.ts:53-84`). Projects list via `useProjects` (`client/src/api/projects.ts:83-89`). `usePipeline` is fleet-only and its `AgentTaskPipelineDto` type has no `concurrencyScopes`/`hosts` fields (`client/src/api/agentTasks.ts:597-604`, `:805-815`). SignalR events map to query keys in `INVALIDATION_MAP` (`client/src/hooks/useSignalRInvalidation.ts:25-174`); `RunnerDefaultsChanged` is at `:161-164` and there is no `DispatchConcurrencyChanged` entry.
- Roles: the client's `AGENT_TASK_ROLES` is a hand-ordered picker list with labels (`agentTasks.ts:688-704`); the editor must iterate the DTO's `supportedRoles` instead, so a new server role appears without a client change.
- i18n: none (no i18next/react-intl in `client/src`); strings are inline English. Theming: Mantine `createTheme` with semantic palettes (`client/src/theme.ts:58-59`); secondary text is `c="dimmed"`, status is `Badge variant="light"`; dark mode is Mantine's, no custom CSS needed. Responsive props such as `w={{ base: '100%', sm: 300 }}` (`ProjectConfig.tsx:256`); no `Table.ScrollContainer` is used anywhere yet (grep 0).
- Tests: Vitest through `pwsh -NoProfile -File scripts/test-client.ps1 <filters…>` (filters pass through, `scripts/test-client.ps1:15-17`); msw `server.use(http.get/put…)` with `renderWithProviders`/`renderHookWithProviders` (`client/src/test/utils.ts:50`, `:67`; precedents `RunnerDefaultsSection.test.tsx:1-45`, `runnerDefaults.test.ts:19-39`). The full suite includes the ESLint gate (`client/src/lint.gate.test.ts:16-23`), so any lint warning is a red test. E2E: `tests/Antiphon.E2E` is TUnit + Playwright; `AntiphonAppFixture` boots its own `IsolatedSessionRunner` (`tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs:108`), `UsePrebuiltFrontend` serves `client/dist` and `EnsureClientBundleIsCurrent` hard-fails on a stale bundle (`:45`, `:326`, `:356`; `docs/testing-and-build.md:46`). Browser tests are `[NotInParallel] [Category("OptIn")]`, navigate to `{PlaywrightAddress}/settings` and click tabs by role (`tests/Antiphon.E2E/ProjectDeleteE2ETests.cs:25-26`, `:168-175`). The `e2e` suite is manual mode outside nightly (`tests/test-execution-policy.json:556-563`).

### 1.3 Ground truth: what the card and brief assume versus what the code does

| Assumption | Code | Consequence for the UI |
|---|---|---|
| Backend knobs exist and are live | Yes: routes, store, revisions, change event (§1.1) | No backend slice. |
| Revert restores inheritance | Omit the key; `{}` clears all. Explicit `null` is an override meaning unbounded, not a revert (`DispatchConcurrencyPolicy.cs:281-290`, `:471-479`) | The draft model distinguishes absent from null; Revert omits keys and never sends null. |
| "Non-negative integers" | Parallel is 1–512; queued is 0–4096; ranges come in the DTO | Use `ranges`; parallel min is 1. |
| "null = unbounded" | True for `maxQueued` (overall and role) and role `maxParallel` (then the overall cap applies via `combinedParallel`). Overall `maxParallel` cannot be null (`:71`, `:497-503`) | "No limit" is offered on three field kinds, not on overall max parallel. |
| Effective value and source come from the pipeline read | The scope GETs already return `effective` with per-field sources, `inherited` (project) and `occupancy`, computed by the same code the pipeline uses (§1.1); the client pipeline hook is fleet-only and untyped for scopes | D-5: read effective/source from the scope GET; do not add a pipeline fetch. |
| 409 shows a diff | The 409 body carries the live revisions, not the live document (`:548-556`) | D-7: on 409 refetch, then diff draft against the fresh `overrides`. |
| Per-project override lives on the project edit surface | Edit Project is a `size="md"` modal with its own PUT and error shape (`ProjectConfig.tsx:346-351`, `:185-229`) | D-2: separate editor, linked from the project row. |
| Permissions matter | None exist; single admin; `Human` provenance | No role checks; `dispatch_concurrency_human` never shown. |
| Mode is a knob | Accepted by PUT, changes the meaning of every number (`DispatchConcurrencyPolicy.cs:122-166`) | D-6: read-only in this card. |
| Host budgets belong here | Separate route and existing Orchestrator editor | Out of scope; link text only. |

Stage checklist (sessions waiting for input): not applicable; this plan adds no session, delegate or wait.

## 2. Decisions

- **D-1 Global editor location: a new Settings tab `concurrency` labelled "Concurrency"**, rendering `ConcurrencySettingsTab` → `DispatchConcurrencyEditor`. Reason: a 15-row × 4-column editor plus history would dominate the Routing tab, which is about model routing; a tab gives a deep link (`/settings?tab=concurrency`) and `keepMounted={false}` scoping for free. Rejected: a fifth section in Routing (crowding); the Status tab (read-only by convention).
- **D-2 Per-project override: the same tab with a scope selector** (`Select`: "Global defaults" plus every project from `useProjects`), deep-linkable as `?tab=concurrency&project=<id>`. The Projects table row gains a "Project concurrency" `ActionIcon` that navigates there. This needs `SettingsPage` Tabs to become controlled by the search param (`value={tab}` + `onChange` → `setSearchParams`). Rejected: embedding in Edit Project (two PUTs with different revision and conflict shapes under one Save; no room); a modal from the row (second container, no deep link, same cramped width).
- **D-3 Interaction: one table, one draft, one Save.** Rows: "Overall" then each `supportedRoles` entry. Columns: Role · Max parallel · Max queued · Effective. A limit cell shows the inherited value greyed (`c="dimmed"`) with the source badge, and an "Override" `Switch` (aria-label `"<Row> max parallel override"`); switched on, it shows a `NumberInput` with `min`/`max` from `ranges` and, for the three nullable field kinds, a "No limit" `Checkbox` that sends explicit `null`. Each row has "Revert to inherited" (both switches off). The toolbar has a `Reason` input (required, 1–400), **Save**, and **Clear all overrides**, which sends `overrides: {}` after a confirm `Modal` and is disabled when the loaded `overrides` has no key other than `schemaVersion`. Global scope uses the same component; "inherited" there is the seed, and Clear restores the seed (what `scripts/dispatch-concurrency.ps1 clear` already does). The draft is `{ maxParallel, maxQueued, roles: Record<role, {maxParallel, maxQueued}> }` of `LimitDraft = { specified: boolean; value: number | null }`; `mode` is carried through untouched. A pure module turns canonical `overrides` into a draft and a draft into the PUT body (omit unspecified fields, omit a role object with no specified field, no `schemaVersion`).
- **D-4 Validation mirrors the server, client-side first.** Integer only, within `ranges`; overall max parallel cannot be "No limit"; reason trimmed non-empty. Save is disabled while any field is invalid or the reason is empty. A 422 hangs `getApiFieldErrors` messages on the matching inputs by their camelCase path key and shows the first message in an `Alert`.
- **D-5 Effective value, source and occupancy come from the scope GET**, not the pipeline route: same revision as the form, same computation, no second fetch and no new client type. Source labels: `project` → "Project override", `global` → "Global override", `default` → "Built-in default". The Effective column shows `combinedParallel` for roles when it differs from the role value. One line under the table shows occupancy: open, parallel, queued, remaining (`∞` when `parallelRemaining` is null).
- **D-6 Mode is displayed, not edited**: a `Badge` with its source and a one-line description of what the mode counts. Rejected: a `Select`, because switching mode changes what every number in the table means and needs its own confirmation UX; the shell script remains the way (open decision O-1).
- **D-7 Revision conflict: never overwrite, never auto-retry.** The mutation sends `expectedRevision` = loaded `revision` and, for a project, `expectedGlobalRevision` = loaded `globalRevision`. On 409 `dispatch_concurrency_revision_conflict`: keep the draft, refetch the scope GET, show a red `Alert role="alert"` "Saved revision changed (now global r{g}, project r{p})" with a list of fields where the draft differs from the fresh `overrides` ("Code max parallel: your draft 1, now saved 3"), and re-enable Save against the fresh revisions. Unlike `RunnerDefaultsSection` (`:56`), the form is keyed on scope only, so a SignalR-driven refetch does not wipe a dirty draft: if fresh data arrives while dirty, show the same banner; if not dirty, adopt it silently.
- **D-8 Loading, empty, error.** Loading: `Loader` inside the `Paper`. GET failure: dimmed message with a Retry button; the rest of Settings is unaffected. Project with `revision: 0` or empty overrides: subtitle "Inheriting global defaults; no project overrides". Save success: green notification "Saved revision N" or "No change" when the returned revision equals the sent one.
- **D-9 Live updates.** Add `DispatchConcurrencyChanged` → `[['dispatchConcurrency'], ['agentTasks', 'pipeline']]` to `INVALIDATION_MAP`; all concurrency query keys share the `dispatchConcurrency` prefix.
- **D-10 Accessibility and width.** Table inside `Table.ScrollContainer minWidth={720}`; every control has an `aria-label` naming row and field; conflict and validation alerts are `role="alert"`; buttons are reachable by accessible name for Playwright; scope selector and toolbar wrap on narrow widths; `data-testid` on section, rows (`dispatch-concurrency-row-<Role>`), inputs, Save, Clear and alerts, following the `runner-defaults-*` style.
- **D-11 History is read-only**: last 10 revisions (`…/revisions?limit=10`) as `r{n} · {provenance} · {reason} · {createdAt}`. No "restore" button (open decision O-3).
- **D-12 Not in scope**: host budgets and runner capacity (Orchestrator page), `Auto` provenance, mode editing, per-board overrides (no backend), restore-from-history, the pipeline route's `hosts`/`runners` typing.

## 3. Slices

| Slice | Files (closed list) | Est. |
|---|---|---|
| **S1 API client and pure model** | new `client/src/api/dispatchConcurrency.ts` (types mirroring §1.1 DTOs; keys `dispatchConcurrencyKeys.scope(projectId|null)`, `.revisions(projectId|null)`; hooks `useDispatchConcurrency(projectId?)`, `useDispatchConcurrencyRevisions(projectId?)`, `usePutDispatchConcurrency(projectId?)` invalidating the prefix); new `client/src/features/settings/dispatchConcurrencyModel.ts` (`draftFromOverrides`, `overridesFromDraft`, `validateDraft(ranges)`, `diffDrafts`, `isCleared`, `sourceLabel`); `client/src/hooks/useSignalRInvalidation.ts` (D-9). Tests: new `client/src/api/dispatchConcurrency.test.ts`, new `client/src/features/settings/dispatchConcurrencyModel.test.ts`, one case in `client/src/hooks/useSignalRInvalidation.test.ts`. | 50 min |
| **S2 Editor component** | new `client/src/features/settings/DispatchConcurrencyEditor.tsx` (table, overrides, reason, Save, Clear-all confirm, conflict flow, occupancy line, mode badge, history list). Tests: new `DispatchConcurrencyEditor.test.tsx`. | 60 min |
| **S3 Tab, scope selector, deep link, project row link** | new `client/src/features/settings/ConcurrencySettingsTab.tsx`; `client/src/features/settings/SettingsPage.tsx` (tab entry, controlled Tabs); `client/src/features/settings/ProjectConfig.tsx` (row `ActionIcon` → `/settings?tab=concurrency&project=<id>`). Tests: new `ConcurrencySettingsTab.test.tsx`; one case each in `SettingsPage.test.tsx` and `ProjectConfig.test.tsx`. | 40 min |
| **S4 Docs** | `docs/antiphon-api.md` (four route rows beside `:538-539`), `docs/ops-http.md:177` (name the Settings tab), `docs/orchestration-loop.md` WIP-defaults paragraph (`:811-816`: add "or the Settings → Concurrency tab"). No tests. | 20 min |
| **S5 E2E** | new `tests/Antiphon.E2E/DispatchConcurrencySettingsE2ETests.cs` (`[NotInParallel] [Category("OptIn")]`, `UsePrebuiltFrontend = true`): create a project over HTTP, open `/settings?tab=concurrency&project=<id>`, override Code max parallel to 1 with a reason, Save, assert the project GET shows `roles.Code.maxParallel = 1` with source `project`; click Clear all overrides, confirm, assert `overrides` is `{schemaVersion: 1}` and the Code source is no longer `project`. Rebuild `client/dist` first (`npm run build` in `client/`). | 45 min |

Commit and push after each slice; S1–S3 form the closed test list for CP-1 and CP-2, all five for CP-3 and CP-4.

## 4. Verification design

Case → mutation that must turn it red → red symptom. Every new test asserts an outcome the production line decides.

| ID | Case (file) | Mutation | Red |
|---|---|---|---|
| V-1 | `draftFromOverrides` keeps absent vs null distinct (`dispatchConcurrencyModel.test.ts`) | Treat `null` as unspecified | `roles.Code.maxQueued.specified` expected true, got false |
| V-2 | `overridesFromDraft` omits unspecified fields and empty role objects, emits explicit null | Emit `null` for unspecified | body has `maxQueued: null` where none expected |
| V-3 | `validateDraft` rejects overall parallel null, parallel 0 and 513, queued −1 and 4097 from `ranges` | Drop the `allowNull` branch for overall parallel | error expected, got none |
| V-4 | `diffDrafts` lists only fields whose specified/value differ | Compare values only | row with specified-only change missing |
| V-5 | `usePutDispatchConcurrency` sends exactly five fields with `expectedRevision`/`expectedGlobalRevision` from the loaded DTO and `provenance: 'Human'` (`dispatchConcurrency.test.ts`) | Omit `expectedGlobalRevision` | captured body lacks the key |
| V-6 | Put success invalidates the scope and revisions keys | Remove invalidation | GET called 1 time, expected 2 |
| V-7 | `DispatchConcurrencyChanged` invalidates `['dispatchConcurrency']` and the pipeline key (`useSignalRInvalidation.test.ts`) | Delete the map entry | invalidate spy not called |
| V-8 | Editor renders one row per `supportedRoles` with greyed inherited value and source label (`DispatchConcurrencyEditor.test.tsx`) | Iterate `AGENT_TASK_ROLES` | a role present only in the DTO is missing |
| V-9 | Switching Override on Code max parallel and saving with a reason PUTs `{roles:{Code:{maxParallel:1}}}` with the loaded revisions | Send the whole effective table | body contains `Plan` |
| V-10 | "No limit" on Review max queued sends `roles.Review.maxQueued: null`; Revert on that row omits `Review` | Treat No limit as unspecified | key missing / present |
| V-11 | Clear all overrides sends `overrides: {}` after confirm and is disabled when nothing is overridden | Send the current overrides | body not `{}` |
| V-12 | 409 keeps the draft, refetches, shows the alert with both revisions and the field diff, and the next Save uses the fresh revisions | Reset the draft on error | reason input empty / expectedRevision stale |
| V-13 | 422 hangs `roles.Code.maxParallel` message on that input and shows the alert | Ignore `errors` | input has no error text |
| V-14 | Loading, GET-failure with Retry, and "No change" when the returned revision equals the sent one | Always notify "Saved" | notification text mismatch |
| V-15 | Scope selector loads the project route and the deep link `?project=<id>` preselects it (`ConcurrencySettingsTab.test.tsx`) | Ignore the search param | global DTO rendered |
| V-16 | `SettingsPage` shows the Concurrency tab and follows `?tab=` changes after mount (`SettingsPage.test.tsx`) | Keep `defaultValue` | panel not switched |
| V-17 | Project row "Project concurrency" navigates to the deep link (`ProjectConfig.test.tsx`) | Drop the `project` param | URL lacks `project=` |
| V-18 | Browser journey (`DispatchConcurrencySettingsE2ETests`) override then clear, verified by GET | Any of V-9/V-11 regressions | GET source/overrides assertions fail |

Regressions kept green: R-1 `RunnerDefaultsSection.test.tsx`, R-2 `SettingsPage.test.tsx` existing cases, R-3 `ProjectConfig.test.tsx` existing cases, R-4 `useSignalRInvalidation.test.ts` existing cases, R-5 lint gate.

### Checkpoints

Lanes: CP-1 to CP-3 run on the server2 (Linux) lane. CP-4 runs on the desktop (Windows) lane, where Playwright Chromium and the client build are present; a Linux run is acceptable only after `playwright.ps1 install chromium` there, and the report must name the lane used.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | n/a | client-model | `pwsh -NoProfile -File scripts/test-client.ps1 dispatchConcurrency.test dispatchConcurrencyModel.test useSignalRInvalidation.test` | V-1–V-7, R-4 | the three files run, every new case present, 0 failed/skipped (TestDesign freezes the count) | n/a | 4 | false |
| CP-2 | S1-S3 | n/a | client-editor | `pwsh -NoProfile -File scripts/test-client.ps1 DispatchConcurrencyEditor.test ConcurrencySettingsTab.test SettingsPage.test ProjectConfig.test` | V-8–V-17, R-2, R-3 | the four files run, 0 failed/skipped | n/a | 6 | false |
| CP-3 | all | n/a | client-full | `pwsh -NoProfile -File scripts/test-client.ps1` | R-1, R-5, whole client suite | `CLIENT TESTS EXIT CODE: 0`, lint gate green | n/a | 10 | true |
| CP-4 | all | `tests/Antiphon.E2E -> bin-c506-e2e/` | e2e-concurrency | `/*/*/DispatchConcurrencySettingsE2ETests/*` | V-18 | exact 1 result, 0 failed/skipped; `client/dist` rebuilt first | 1 | 15 | true |

### Cost

Ordinary Code floor 35 minutes (sum of EstimatedMinutes) plus authoring; `-ExpectAbout` for the Code dispatch about 4.5 hours including S1–S5 authoring.

### Positive controls (pending; Mutation is paused)

| PC | Mutation | Test |
|---|---|---|
| PC-1 | `draftFromOverrides`: map `null` to `{specified:false}` | `dispatchConcurrencyModel.test.ts` V-1 |
| PC-2 | `overridesFromDraft`: emit unspecified fields as `null` | V-2 |
| PC-3 | `validateDraft`: allow null overall parallel | V-3 |
| PC-4 | `usePutDispatchConcurrency`: drop `expectedGlobalRevision` | `dispatchConcurrency.test.ts` V-5 |
| PC-5 | `INVALIDATION_MAP`: remove the entry | `useSignalRInvalidation.test.ts` V-7 |
| PC-6 | Editor: send the full effective table | `DispatchConcurrencyEditor.test.tsx` V-9 |
| PC-7 | Editor: Clear sends current overrides | V-11 |
| PC-8 | Editor: reset draft on 409 | V-12 |
| PC-9 | `SettingsPage`: back to `defaultValue` | `SettingsPage.test.tsx` V-16 |

## 5. Risks and open operator decisions

Risks:

- **Draft versus live refetch.** SignalR invalidation refetches on every write from any client or script; D-7 keeps a dirty draft but the banner logic is the one piece of state machinery here. Keep it in the pure model (`diffDrafts`) so it is unit-tested, not screen-tested.
- **Mantine `NumberInput` yields `number | string`** (`HostsPanel.tsx:17`); the model must coerce and reject blanks and decimals before Save, or the server's 422 is the first signal.
- **Strict PUT body.** Any extra key, including a helpful `schemaVersion`, `origin` or `importedAt`, is a 422 at the endpoint; the API client builds the five-field body itself and tests assert the exact key set (V-5).
- **Project revision 0.** A project without a row has `revision: 0`; sending anything else is a 409 on first save (`DispatchConcurrencySettingsService.cs:288-293`).
- **Lint gate.** Unused imports or `any` in the new files fail CP-3, not only `npm run lint`.
- **E2E bundle staleness.** CP-4 hard-fails on a stale `client/dist`; the Code report must state the build it served.

Open decisions, with a recommendation each:

- **O-1 Mode editing in the UI.** Recommend no (D-6); file a follow-up card if an operator asks, with a confirm dialog that restates how each mode counts.
- **O-2 Global "Clear all overrides".** Recommend allow (restores the imported seed, matching the script's `clear`), behind the same confirm as the project clear.
- **O-3 Restore a historical revision.** Recommend defer; the snapshot envelope would need its own parser and tests.
- **O-4 E2E lane.** Recommend desktop (Windows); Linux only if Chromium is already installed on server2 and the report says so.
- **O-5 Tab name and position.** Recommend "Concurrency" after "Routing" and before "Status".
