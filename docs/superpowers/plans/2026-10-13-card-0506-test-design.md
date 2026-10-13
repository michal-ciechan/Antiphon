# CARD-0506 verification design — concurrency settings UI

TestDesign task `fe5c4378`, based on `0b161391ff2567fe0043476db3b65ad4ce009337` (2026-10-13 source census).
This is an additive verification companion to [the fix plan](2026-10-13-card-0506-concurrency-settings-ui.md); the fix design is unchanged. O-1–O-5 are accepted: read-only mode, global Clear allowed, history read-only, desktop E2E, Concurrency after Routing.
Design only: no production/test changes, builds, test discovery execution, or test runs. Task binding is present (`ANTIPHON_TASK_TOKEN` checked for presence only). Browser fixtures send no task token.
New test names below are **binding required** until Code creates them and records their execution; this designation survives Code for any missing/unbound payload. Source census is not execution evidence. Post-land Mutation is paused: PC-1–PC-49 are specified and pending, none is claimed run.

Slice order is S1 API + pure model + invalidation (50 min) → S2 editor (60) → S3 tab/deep link/project link (40) → S4 docs (30, including route-contract review) → S5 E2E (45). Commit/push each slice before its checkpoint. Authoring estimate 225 minutes. S1 first; CP-1 after S1, CP-2 after S3, CP-3/4 after S5. These are the plan's file boundaries and four ordinary checkpoints.

## Verification design

### Inspection

Bodies read, not just declaration searches:

| Bodies read | Boundary → coverage or exclusion |
|---|---|
| `server/Application/Dtos/DispatchConcurrencyDtos.cs`; `server/Api/Endpoints/DispatchConcurrencyEndpoints.cs`; `server/Api/Middleware/ExceptionMiddleware.cs`, `ValidationException.cs` and Program JSON options | DTO nullability, strict PUT parsing, error extensions → V-5, V-12, V-13, fixtures F below |
| `DispatchConcurrencyPolicy.cs`: role enumeration, resolve, validate, integral parse, canonical writer; `DispatchConcurrencySettingsService.cs`: GET projection, PUT transaction/conflict/no-op, validation, history snapshot, occupancy, publish | Presence/null/inheritance, bounds, revision and post-commit notification → V-1–V-6, V-8–V-14, V-18; backend admission algorithm unchanged |
| `server/Domain/Enums/AgentTaskEnums.cs`; `server/Infrastructure/Realtime/EventBus.cs` | Actual ordinary roles and non-durable SignalR transport → V-7, V-8, V-18 |
| `client/src/api/client.ts`, `runnerDefaults.ts`, **`runnerDefaults.test.ts`** | ApiError and real hook/MSW recipe for new API file → V-5–V-6, V-12–V-14 |
| **`RunnerDefaultsSection.test.tsx`**; `SettingsPage.tsx`, **`SettingsPage.test.tsx`**; `ProjectConfig.tsx`, **`ProjectConfig.test.tsx`** | Nearest editor/tab fixtures and touched tests → V-8–V-17, R-1–R-3 |
| `client/src/hooks/useSignalRInvalidation.ts`, **`useSignalRInvalidation.test.ts`** | Callback registration, active-query refetch, unmount → V-7, V-12, R-4 |
| `client/src/test/utils.ts`, `setup.ts`, `mocks/server.ts`, relevant `mocks/handlers.ts` handlers; `vite.config.ts`; `lint.gate.test.ts`; `App.test.tsx`; `scripts/test-client.ps1` | Providers, retry disabled, MSW strictness, wrapper exit, bundle-conditional test → recipes, CP-1–3, R-5 |
| **`tests/Antiphon.E2E/ProjectDeleteE2ETests.cs`**, `PipelineGlanceE2ETests.cs` setup/attributes; `Fixtures/AntiphonAppFixture.cs` startup/bundle/runner/teardown methods, `PlaywrightFixture.cs`, `IsolatedSessionRunner.cs` start/stop, `ProcessSpawnLimit.cs` | Nearest browser fixture, owned server/Postgres/runner, receipt and diagnostics → V-18 |
| `docs/project-context.md`; testing/build owner checkpoint, census, slot, mutation, evidence sections; orchestration owner stage order; checkpoint importer/command runner | Four-row schema, stage separation and costs; no execution in TestDesign |

#### Bound fixtures and recipes

`M` = direct pure-module calls. `H` = real query hooks under `renderHookWithProviders`, real `apiGet/apiPut`, MSW handlers. `E` = real editor/hooks under `renderWithProviders`, Mantine test environment and `userEvent`; mock notifications only. `N` = real Settings/ProjectConfig/tab and BrowserRouter with the same providers. `B` = real built App, HTTP server, DB, SignalR and Chromium. Each V row below supplies recipe, variant/case, independent oracle, reachability and classification. Names are top-level test titles (no describe prefix), so the exact `-t` anchors below bind. Parameterize with literal, hand-written expected outcomes; do not derive expected values by calling the production resolver/serializer/diff.

F-1 contract vocabulary (camelCase on the wire; include nullable fields explicitly):

- Supported roles in current order: `Custom, Plan, Code, Review, Debug, Coverage, Docs, Commit, Test, Deploy, Merge, Investigate, TestDesign, Mutation`. No Check/Distill/Diagnose. A synthetic role advertised by an MSW DTO tests forward compatibility, not current backend acceptance.
- `ranges = {parallelMin:1, parallelMax:512, queuedMin:0, queuedMax:4096}`.
- Seed: `{origin:'startupConfiguration', importedAt:'2026-10-13T10:00:00Z', mode:'LegacyOpen', maxParallel:6, maxQueued:null, roles:[{role,maxParallel:2,maxQueued:null}, ...]}`. Include 14 role rows.
- Effective: `{mode,modeSource,maxParallel,maxParallelSource,maxQueued,maxQueuedSource,roles,globalRevision,projectRevision}`. Effective role: `{role,maxParallel,maxParallelSource,maxQueued,maxQueuedSource,combinedParallel}`. Overall parallel is non-null; role parallel/queued, overall queued and combinedParallel have nullable DTO types. Sources are `project`, `global`, `default`.
- Occupancy: `{open,parallel,queued,parallelRemaining,queuedRemaining,parallelOverage,queuedOverage,exceedsBounds,roles}`; role occupancy has the same numeric fields except `exceedsBounds`, plus `role`. Remaining values nullable, counts/overages non-null. Baseline counts and overages 0, exceedsBounds false; remaining equals the applicable limit, with null for unbounded.
- Global DTO: `{seed,overrides,effective,nullProject,supportedRoles,ranges,revision,updatedAt,reason,provenance,callerTaskId,occupancy}`. Global updatedAt/reason/provenance non-null; callerTaskId nullable. `nullProject` is an effective object, not a project DTO.
- Project DTO: `{projectId,overrides,inherited,effective,supportedRoles,ranges,revision,globalRevision,updatedAt,reason,provenance,callerTaskId,occupancy}`. Project updatedAt/reason/provenance/callerTaskId nullable. Even a project without a row has an object `overrides`, not null.
- History page: `{revisions:[{revision,previousRevision,snapshot,createdAt,reason,provenance,callerTaskId}],nextBeforeRevision}`; previousRevision/callerTaskId/nextBeforeRevision nullable. Snapshot is `{scopeKey,projectId,revision,provenance,reason,callerTaskId,overrides}` plus seed for global. Global scopeKey `global`; project scopeKey the GUID string. Snapshot projectId/callerTaskId can be null. Display snapshot as read-only, do not confuse it with overrides.

F-2 fixed states, constructed locally in each new test file (no new shared helper file needed):

- Project P = `05060000-0000-4000-8000-000000000001`; second project Q ends `0002`. Project-list records use `ProjectConfig.test.tsx`'s full ProjectDto shape with these IDs, `name:'P'/'Q'`, and distinct paths; readiness/boards/GitHub/API-key handlers follow that fixture when mounted.
- G7 = global revision 7, reason `operator`, provenance `Human`, callerTaskId null, fixed updatedAt. Overrides `{schemaVersion:1,maxParallel:4,maxQueued:12,roles:{Code:{maxParallel:3},Review:{maxQueued:5}}}`. Effective mode LegacyOpen/default, parallel 4/global, queued 12/global; Code parallel 3/global, queued null/default, combinedParallel 3; Review parallel 2/default, queued 5/global, combinedParallel 2; other roles 2/default and null/default, combinedParallel 2. Revisions in effective/nullProject are 7/0.
- P0 = revision 0/globalRevision 7, overrides `{schemaVersion:1}`, metadata null; inherited/effective equal G7 effective with revisions 7/0. Its subtitle states inheritance. Q0 has Q identity, separate cache and route.
- P4 = revision 4/globalRevision 7, overrides `{schemaVersion:1,roles:{Code:{maxParallel:1},Review:{maxQueued:null}}}`, Human metadata. Inherited is G7 effective with revisions 7/4. Effective Code parallel 1/project/combined 1; Review queued null/project; remaining fields inherited. Adjust occupancy consistently.
- P5/G8 conflict refresh: project revision 5/globalRevision 8; saved Code parallel 3, draft Code parallel 1. Update both inherited and effective revision fields and the top-level revisions; reason `another operator`. For inherited-change case keep project Code override 1 while inherited Code changes 3→2.
- F-empty = schema-only `{schemaVersion:1}`; F-mode = an otherwise empty document with `mode:'SeparateQueues'`; F-null has explicit nullable override(s). Schema-only is cleared; mode-only is not cleared. Absent, explicit null and numeric zero have separate expected values.
- History: `limit=10` required for global and project URLs, one fixed row r3 with previousRevision 2, reason `earlier`, Human, fixed createdAt and snapshot envelope above; empty-page variant uses `revisions:[],nextBeforeRevision:null`.

F-3 HTTP handlers: GET/PUT `/api/dispatch-concurrency`, GET `/api/dispatch-concurrency/revisions`; corresponding project routes `/api/projects/:projectId/dispatch-concurrency[/revisions]`. Match and assert P/Q path parameters; capture parsed request AND raw JSON when checking null/presence. PUT success swaps a server-side fixture variable, increments revision and returns the corresponding complete DTO; no-op returns the same Human DTO/revision. Active GET hooks must observe the updated object. Hold responses using a test-owned promise, then explicitly release it in finally; no sleep-based race.

- Planned PUT is exactly `{expectedRevision,expectedGlobalRevision,overrides,reason,provenance:'Human'}`. Global sends 7/7; project P0 sends 0/7, P4 sends 4/7. Global endpoint permits omission of expectedGlobalRevision, but a present null fails its integer reader; the planned five-field client sends a number. No top-level schemaVersion/origin/projectId. Nested schemaVersion 1 is server-accepted but intentionally omitted by this client serializer. Trim reason before sending.
- F409: status 409, content type `application/problem+json`, `{type:'https://tools.ietf.org/html/rfc9110#section-15.5.10',title:'Conflict',status:409,detail:'Dispatch concurrency revision does not match. Global revision is 8; project revision is 5.',traceId:'c506-conflict',code:'dispatch_concurrency_revision_conflict',globalRevision:8,projectRevision:5}`. Contains no live overrides: a separate GET is required. Global conflict uses projectRevision 0. Refresh-error arm returns 503 after this 409.
- F422: status 422, content type `application/problem+json`, `{type:'https://tools.ietf.org/html/rfc4918#section-11.2',title:'Unprocessable Entity',status:422,detail:'One or more validation errors occurred.',traceId:'c506-validation',code:'validation_failed',errors:{'roles.Code.maxParallel':['roles.Code.maxParallel must be an integer from 1 to 512.']}}`. Other real keys include `maxParallel`, `maxQueued`, `roles.Review.maxQueued`, `reason`, `overrides`. Unknown role returns `errors:{roles:["Unknown role 'FutureRole'."]}`, not `roles.FutureRole`. An MSW advertised FutureRole followed by this rejection models version skew; preserve draft and show error, no success.
- Offline PUT uses `HttpResponse.error()`; GET failure uses 503, missing project uses 404. Do not fabricate the Auto-only `dispatch_concurrency_human` error for a Human write.

F-4 live events: registered callback payload `{scope:P,revision:5,globalRevision:8,projectRevision:5}` (global scope `global`, projectRevision 0). Hook-only tests use the existing fake HubConnection callback map; editor live cases mount a test wrapper that calls the real invalidation hook with that connection and swaps F-2 server responses. Spy-only invalidation is insufficient: await DOM/query data from the subsequent GET. Ignore event payload data as a settings snapshot. Use `performance.now()`/Stopwatch if measuring elapsed deadlines; fixed UTC fixture dates are metadata only.

### Delivery inventory

| Path / identity | Producer → destination | Persistence / recovery | Receipt and substitute limits |
|---|---|---|---|
| UI PUT; `(scope, expectedRevision, expectedGlobalRevision)` → committed `(scope, revision)` | editor → real endpoint → settings transaction → response/query invalidation → editor | Settings row and history commit together; conflict requires fresh GET; response loss leaves outcome unknown until GET/manual retry. No automatic write retry. | V-9–V-14 MSW proves client behavior only. V-18 browser operation followed by independent GET, reload and visible source/value proves persisted settings consumed by UI. Request capture or 200 alone is not receipt. |
| Existing `DispatchConcurrencyChanged`, newly subscribed client; `(scope,revision,globalRevision,projectRevision)` | committed settings service → EventBus → SignalR → invalidation hook → scope GET → clean/dirty editor | Persistence is the settings/history transaction BEFORE publish. Event is best-effort, no durable queue/outbox or replay promise; GET/remount is recovery when an event is lost. | V-7 callback and V-12 MSW are transport substitutes: neither proves server publication/socket receipt. V-18 observes real ready and busy browser recipients after a real API producer write, without reload/focus as an alternate trigger; then separately tests missed-event recovery by remount/GET. |

There is **no new/changed queued delivery or session-input path**. Real queue busy/already-eligible and enqueue/crash handoffs, and complete UserPrompt receipts, are excluded for that reason; a SignalR socket is not described as a durable queue. No delivery claim is made for a missing event. Existing transaction/publish crash atomicity is backend scope, unchanged here. For the changed consumer, V-18's offline recipient covers missing notification and recovery from committed state. Real recipient evidence cannot be replaced by a publish spy, queue insert, Sent flag, event observation or ack. G-21–24, G-31–39 map the client recovery assertions to distinct PCs.

### Proves it works now

These are frozen future tests, not a claim that the not-yet-written feature passes. File aliases: M=`client/src/features/settings/dispatchConcurrencyModel.test.ts`; H=`client/src/api/dispatchConcurrency.test.ts`; E=`client/src/features/settings/DispatchConcurrencyEditor.test.tsx`; N=`client/src/features/settings/ConcurrencySettingsTab.test.tsx`; I=`client/src/hooks/useSignalRInvalidation.test.ts`. Every variant below is a separate execution. New top-level title format is exactly `C506 Vnn <variant>`.

| ID | Recipe / exact title variants (count) | Case, hand-written oracle and reachability | Classification; compiling mutation → red |
|---|---|---|---|
| V-1 | M `C506 V01 <variant>`: `absent-overall`, `null-overall-queued`, `null-role-parallel`, `null-role-queued`, `zero-queued`, `numeric-role` (6) | Decode literal override documents; absent specified=false; present null specified=true/value=null; zero specified=true/value=0; Code parallel 3 stays 3. Enter through draftFromOverrides, not handcrafted output. | Pure semantics; null→unspecified → null-role-queued specified assertion false (PC-1). |
| V-2 | M `C506 V02 <variant>`: `omit-overall`, `omit-role`, `explicit-null`, `preserve-mode`, `omit-schema`, `empty-document` (6) | Encode draft with unchanged other field. Assert exact object/key membership: unspecified overall absent; empty Review role omitted; specified null emitted; mode carried untouched; no schemaVersion; cleared output `{}`. | Pure serialization; emit unspecified fields/null or remove mode → exact object mismatch (PC-2–5). |
| V-3 | M `C506 V03 <field> <value>` (40), `C506 V03 reason <variant>` (6), `C506 V03 <range-variant>` (4), `C506 V03 unspecified-invalid` (1): **51** | Fields `overall-parallel`,`overall-queued`,`role-parallel`,`role-queued`. Values named `null`,`below`,`min`,`max`,`above`,`fraction`,`string`,`blank`,`nan`,`infinity`. Parallel numeric set 0/1/512/513/1.5; queued −1/0/4096/4097/0.5. String is `'1'` or `'0'`, blank `''`. Only min/max and nullable null pass; overall parallel null fails. Typed illegal values enter validator via deliberate unknown cast, never serialized JSON. | Pure validation; invert one guard → its literal expected path errors differ (PC-6–15). Runtime `it.each` titles quote string substitutions (`C506 V03 'role-parallel' 'below'`, `C506 V03 reason '401'`, `C506 V03 'queued-8-reject'`); PC selectors bind those exact titles (Review ef9c0e6b disclosure). Reason variants `empty`,`whitespace`,`one`,`trimmed`,`400`,`401`: first two/last fail, rest pass. Range variants `parallel-1-reject`,`parallel-2-accept`,`queued-7-accept`,`queued-8-reject` use min 2/max 7 from DTO. Unspecified invalid value ignored, serializes absent. |
| V-4 | M `C506 V04 <variant>`: `presence-only`,`value-change`,`equal`,`hidden-unspecified` (4) | diffDrafts receives absent versus specified null → one correct field diff; 1→3 → that field only; identical or different hidden unspecified value → empty diff. | Pure conflict diff; compare values only → presence-only diff missing (PC-20). |
| V-5 | H `C506 V05 <variant>`: `global`,`project-zero`,`project-existing` (3) | Mount GET, use its revision to mutate, capture five-key F-3 body with Human, right route, explicit null retained, reason trimmed. Assert equality, no body spread from DTO. P0 expectedRevision exactly 0. | Hook/HTTP contract; omit globalRevision, use revision fallback 1, add seed, change provenance → body assertion fails (PC-16–19). |
| V-6 | H `C506 V06 <variant>`: `global`,`project` (2) | Mount scope AND history hooks. PUT advances fixture. After success both GET counts 1→2 and both hook data revisions advance; unrelated query sentinel stays unchanged. History query limit exactly 10. | Hook/cache integration; remove scope/history invalidation independently → corresponding hook stays old (PC-21/22). |
| V-7 | I `C506 V07 changed-event` (1 added) | Register real hook on callback-map connection. Seed scope/history/pipeline/unrelated cache entries, emit F-4, assert concurrency prefix (including both scopes/history) and pipeline invalidated, unrelated retained, off called on unmount. | Consumer unit substitute; delete each mapped key independently → corresponding isInvalidated false (PC-23/24). |
| V-8 | E `C506 V08 <variant>`: `rows-and-sources`,`dto-only-role`,`unknown-source` (3) | G7/P4 display 15 rows, numeric inherited values and correct source badges; mode read-only; effective combined cap visible (variant role maxParallel=null, combinedParallel=4); nullable remaining shows infinity. History row reason/date/provenance visible, no Restore control. Advertised FutureRole adds one row despite absent client picker enum. Unknown source string is shown neutrally, not labelled Built-in default. | Component projection; hard-code client roles, mislabel source or use raw role limit → decisive row/source/cap mismatch (PC-43–46). |
| V-9 | E `C506 V09 <variant>`: `sparse-project`,`sparse-global`,`trim-reason`,`invalid-disabled`,`double-submit`,`offline` (6) | P0/G-empty: enable Code parallel, enter 1, reason; exact overrides `{roles:{Code:{maxParallel:1}}}` only. UI typing an integer emits JSON number, float/blank with override active disables Save. Trim reason. Hold PUT, double click → one request until release. Offline failure keeps draft/reason, reports error and permits explicit retry, no success. | Component/HTTP; copy effective table, bypass invalid/pending gate, clear on network failure → body/count/draft assertion fails (PC-25/30/38/42). |
| V-10 | E `C506 V10 <variant>`: `overall-queued`,`review-parallel`,`review-queued` (3) | Enable nullable override and No limit: exact respective key exists with null; save. Then Revert row: both row fields/empty role omitted, unrelated Code override retained; inherited value/source visible. Overall parallel exposes no No limit. | Component semantics; No limit toggles specified=false or Revert sends null → body key/presence mismatch (PC-26/27). |
| V-11 | E `C506 V11 <variant>`: `global`,`project`,`schema-only-disabled`,`cancel` (4) | Non-empty overrides, reason supplied: opening confirmation sends zero PUT; confirm sends exact `overrides:{}` even if mode was specified. Schema-only disables Clear; mode-only permits it. Cancel sends zero PUT and keeps draft. Run global and P4 independently. | Clear safety; remove confirm or serialize existing overrides → premature write/nonempty body (PC-28/29). |
| V-12 | E `C506 V12 <variant>`: `conflict-project`,`conflict-global`,`refresh-fails`,`live-dirty`,`live-clean`,`inherited-change` (6) | F409 after edited reason/Code=1: one PUT, preserved draft/reason, fresh GET before alert diff `Code max parallel: your draft 1, now saved 3`, revisions 8/5 (8/0 global). Explicit second click uses fresh revisions. Refetch 503: keep draft, withhold Save until successful refresh. F-4 dirty refetch shows banner, clean refetch adopts; inherited change 3→2 leaves project override 1/effective project while inherited display changes. | Component recovery; reset draft, retry write, skip refresh, adopt fresh over dirty → exact draft/revision/request-count/banner assertion red (PC-31–37). |
| V-13 | E `C506 V13 <variant>`: `field-error`,`unknown-role` (2) | Send valid client draft; backend F422 can reject independently. CamelCase Code path attaches message to Code parallel input (`aria-invalid`/described error) and role=alert. Version skew FutureRole rejection at `roles` goes to alert; edited values retained and no success notification/retry. | Server refusal consumer; drop error handling or report success → error/draft/success-count red (PC-39). |
| V-14 | E `C506 V14 <variant>`: `loading-retry`,`no-change`,`saved`,`history-empty` (4) | Held initial GET shows loader/no active Save; release to 503 → Retry; click Retry serves DTO. Returned same revision → No change; incremented → Saved revision N. Empty history has no restore and usable editor. | UI status; always notify Saved → no-change text red (PC-49); remove Retry → loading-retry missing button; invent history row → history-empty red. |
| V-15 | N `C506 V15 <variant>`: `project-deeplink`,`scope-selector`,`missing-project` (3) | Initial `/settings?tab=concurrency&project=P` fetches P, selects P, not global. Change selector P→Q→global; routes/cache/draft isolated and URL follows. Unknown GUID GET404 (or malformed project parameter) holds error state, zero global PUT, Retry available; no silent scope fallback. | Navigation/scope safety; ignore project selector or fallback global → wrong route/scope/draft assertion (PC-40/41). |
| V-16 | `SettingsPage.test.tsx` N `C506 V16 controlled-tab` (1 added) | Mount routing; navigate to concurrency through real router after mount; aria-selected/panel follow, tab order Routing→Concurrency→Status; back/forward follow URL. Invalid tab retains existing templates fallback. No unrelated concurrency fetch when inactive. | Navigation regression; use defaultValue again → post-mount panel wrong (PC-47). |
| V-17 | `ProjectConfig.test.tsx` N `C506 V17 project-row-link` (1 added) | Render two project rows; click P's accessible Project concurrency button; URL pathname `/settings`, tab concurrency, project exactly P; Edit project remains independently usable. | Scope targeting; omit/wrong project param → URL mismatch (PC-48). |
| V-18 | B `DispatchConcurrencySettingsE2ETests.Project_override_clear_and_live_receipts_preserve_inheritance` (1) | Browser recipe below: UI override→GET/reload project source, Clear→GET/reload inheritance; real ready/busy notification consumption and offline remount recovery. | Integrated persistence/recipient; submit Code=2 despite input 1 → independent GET Code maxParallel=1 assertion red (PC-25 detector). |

V-18 reachability: desktop Windows lane, `[NotInParallel] [ParallelLimiter<ProcessSpawnLimit>] [Category("OptIn")]`, `AntiphonAppFixture {UsePrebuiltFrontend=true}` and PlaywrightFixture. Create P via HTTP with unique name, no real repository required. Read current global/project DTOs to capture inherited values and revisions; do not assume live machine defaults. Use accessible controls from D-10, Chromium width 390 for wrapping/scrolling and reachable Save/confirm. Fixture owns Postgres, Kestrel, a random-port IsolatedSessionRunner and per-run roots; production runner 17204 is forbidden. Await/dispose browser contexts and fixture, retain failure diagnostics.

Within this single method: (a) deep link P, UI Code=1 Save with unique reason, independent GET confirms specified value and `project` source, reload confirms same DOM; (b) open a second clean page and a third with dirty Code=2/reason; establish real hub connection and arm scope-GET observation before the next change; (c) first page Clear confirms `{}` and independent GET is exactly `{schemaVersion:1}`, inherited source/value restored; without focus/reload, clean page adopts that revision, dirty page retains Code=2/reason and shows changed-revision banner; correlate observed GET revisions/DOM to the clear revision; (d) take the clean page's context offline and await socket-close/disconnected evidence, make a second real UI change from first page, verify persisted revision by independent GET, reconnect and reload the disconnected page, assert current value/source/revision. This is recovery by authoritative read, not queued event replay. Use response/DOM awaits with bounded Playwright deadlines, Stopwatch for any custom poll; no hard-coded sleep. Ready/busy refer to clean/editing browsers, not sessions. No session delivery claim or manufactured transcript.

### Guards the regression

R-1 uses E, R-2/R-3 use N, R-4 uses the inspected H/callback recipe; R-5 runs the existing suite. Preserve their current production entry points and literal assertions. These are regression classifications, not new substitute transport evidence.

| ID | Existing selection / decisive assertions |
|---|---|
| R-1 | `RunnerDefaultsSection.test.tsx`, 4 executions: cleared kind keeps current revision/draft on conflict; global runner sends current revision; history restore sends current revision; load failure stays in section. |
| R-2 | `SettingsPage.test.tsx`, existing 3 executions: readiness delayed until Projects; routing queries delayed until Routing; routing deep link avoids template fetch. |
| R-3 | `ProjectConfig.test.tsx`, existing 13 executions (10 declarations; each expansions 2 and 3): environment parse/body, visibility and commit-on-settle omission/selection, workspace inheritance/select/unrelated save, readiness column/empty/create-directory refresh. Preserve their literal payload and DOM assertions. |
| R-4 | `useSignalRInvalidation.test.ts`, existing 5 executions: runner-default keys, ID-only policy/note isolation, mounted note/policy refresh, pipeline invalidation and mounted pipeline refresh after CardChanged. |
| R-5 | Full client suite including `lint gate > eslint reports zero problems across the client`; exact zero lint errors/warnings. App's built-entry test must execute, not skip for missing dist. Count below, existing unrelated source unchanged. |

### Guard inventory

Configuration writes affect dispatch capacity. The conservative inventory includes client guards preventing unintended writes, stale overwrites, false success or misleading scope/source; backend locking/admission guards are unchanged and excluded. Independently bypassable conditions have distinct PCs. No listed guard is untested by design.

| Guard | Plan reference / invariant | PC |
|---|---|---|
| G-1 | D-3 decode presence independently of null value | PC-1 |
| G-2 | D-3 unspecified overall key omitted | PC-2 |
| G-3 | D-3 unspecified role fields/empty role omitted | PC-3 |
| G-4 | D-3 explicit null survives serialization | PC-4 |
| G-5 | D-6 specified mode carried unchanged on ordinary Save | PC-5 |
| G-6 | D-4 overall parallel null rejected | PC-6 |
| G-7 | D-4 specified values require integer numbers (includes finite/type checks) | PC-7 |
| G-8 | D-4 parallel lower bound | PC-8 |
| G-9 | D-4 parallel upper bound | PC-9 |
| G-10 | D-4 queued lower bound | PC-10 |
| G-11 | D-4 queued upper bound | PC-11 |
| G-12 | D-4 bounds come from supplied ranges | PC-12 |
| G-13 | D-4 trimmed reason nonempty | PC-13 |
| G-14 | D-4 trimmed reason at most 400 | PC-14 |
| G-15 | D-3/4 unspecified values do not become validation/write input | PC-15 |
| G-16 | D-7 loaded scope revision retained, including zero | PC-16 |
| G-17 | D-7 expected global revision included for project | PC-17 |
| G-18 | §1.1/D-3 five-field request whitelist | PC-18 |
| G-19 | D-12 browser write provenance Human | PC-19 |
| G-20 | D-7 diff detects presence-only changes | PC-20 |
| G-21 | D-8 success refreshes scope state | PC-21 |
| G-22 | D-11 success refreshes history | PC-22 |
| G-23 | D-9 event invalidates concurrency prefix | PC-23 |
| G-24 | D-9 event invalidates pipeline key | PC-24 |
| G-25 | D-3 editor submits chosen value to intended settings scope | PC-25 |
| G-26 | D-3 No limit activates specified null | PC-26 |
| G-27 | D-3 Revert removes row override, preserves unrelated rows | PC-27 |
| G-28 | D-3 Clear requires confirmation | PC-28 |
| G-29 | D-3 confirmed Clear sends exact empty overrides | PC-29 |
| G-30 | D-3 Save excludes a second in-flight submission | PC-30 |
| G-31 | D-7 409 retains draft and reason | PC-31 |
| G-32 | D-7 409 refresh supplies next explicit Save revisions | PC-32 |
| G-33 | D-7 conflict does not automatically retry PUT | PC-33 |
| G-34 | D-7 failed conflict refresh withholds Save | PC-34 |
| G-35 | D-7 live refresh retains dirty draft and warns | PC-35 |
| G-36 | D-7 live refresh adopts clean saved state | PC-36 |
| G-37 | D-3/5 inherited change preserves explicit project override | PC-37 |
| G-38 | D-8 network failure retains draft, cannot report saved | PC-38 |
| G-39 | D-4 backend rejection retains draft, cannot report saved | PC-39 |
| G-40 | D-2/8 unresolved project cannot fall back to global writes | PC-40 |
| G-41 | D-2 scope change cannot reuse another scope's draft/revisions | PC-41 |
| G-42 | D-4 invalid editor draft cannot submit | PC-42 |
| G-43 | D-6 mode cannot be edited on this surface | PC-43 |
| G-44 | D-5 source attribution not guessed from value/null | PC-44 |
| G-45 | D-5 effective role cap comes from combinedParallel | PC-45 |
| G-46 | D-3 supportedRoles governs editable role rows | PC-46 |
| G-47 | D-1/2 controlled tab follows URL after mount | PC-47 |
| G-48 | D-2 project row navigation preserves target identity | PC-48 |
| G-49 | D-8 no-op reply distinguished from new saved revision | PC-49 |

### Positive controls

Pending, **no runs authorized in this TestDesign task**. Code runs ordinary V/R; Review judges design/diff/evidence before land; Mutation runs break → intended red → restore → fresh green after land when resumed. Every PC below is mandatory then, no sampling. Use a compiling production defect, not changed expectations, removed tests, fixture error or zero executions. Run exact methods/cases only, restore source and refresh output timestamps/builds, retain per-PC source SHA and red assertion. Vitest detector syntax is `pwsh -NoProfile -File scripts/test-client.ps1 <exact file from alias> -t '^<exact title>$'`; no broad class/file run for a PC. PC-25 uses `/*/*/DispatchConcurrencySettingsE2ETests/Project_override_clear_and_live_receipts_preserve_inheritance`. Separate variants in one V row do not authorize broad PC selection.

| PC | Break matching G by compiling defect | Exact detecting case / intended red assertion |
|---|---|---|
| PC-1 | Decode null with specified=false | M `C506 V01 null-role-queued`: specified expected true |
| PC-2 | Emit unspecified overall queued as null | M `C506 V02 omit-overall`: maxQueued key expected absent |
| PC-3 | Keep empty Review object | M `C506 V02 omit-role`: roles.Review expected absent |
| PC-4 | Drop specified null during serialization | M `C506 V02 explicit-null`: roles.Review.maxQueued expected own key null |
| PC-5 | Drop carried mode | M `C506 V02 preserve-mode`: mode expected SeparateQueues |
| PC-6 | Allow null for overall parallel | M `C506 V03 'overall-parallel' 'null'`: maxParallel error expected |
| PC-7 | Replace integer predicate with finite-number predicate | M `C506 V03 'role-parallel' 'fraction'`: roles.Code.maxParallel error expected |
| PC-8 | Remove parallel lower comparison | M `C506 V03 'role-parallel' 'below'`: roles.Code.maxParallel error expected |
| PC-9 | Remove parallel upper comparison | M `C506 V03 'overall-parallel' 'above'`: maxParallel error expected |
| PC-10 | Remove queued lower comparison | M `C506 V03 'role-queued' 'below'`: roles.Code.maxQueued error expected |
| PC-11 | Remove queued upper comparison | M `C506 V03 'overall-queued' 'above'`: maxQueued error expected |
| PC-12 | Replace supplied ranges with built-in constants | M `C506 V03 'queued-8-reject'`: maxQueued error expected |
| PC-13 | Allow trimmed empty reason | M `C506 V03 reason 'whitespace'`: reason error expected |
| PC-14 | Remove reason maximum comparison | M `C506 V03 reason '401'`: reason error expected |
| PC-15 | Validate a field even when unspecified | M `C506 V03 unspecified-invalid`: errors expected empty |
| PC-16 | Serialize scope revision as `revision \|\| 1` | H `C506 V05 project-zero`: expectedRevision expected 0 |
| PC-17 | Omit expectedGlobalRevision | H `C506 V05 project-existing`: body expectedGlobalRevision expected 7 |
| PC-18 | Add top-level schemaVersion 1 to request | H `C506 V05 global`: exact sorted key set has five members |
| PC-19 | Set request provenance Auto | H `C506 V05 global`: provenance expected Human |
| PC-20 | Compare diff values but ignore specified bit | M `C506 V04 presence-only`: one Code queued diff expected |
| PC-21 | Remove scope invalidation on success | H `C506 V06 project`: scope data revision expected next, not 4 |
| PC-22 | Narrow success invalidation to scope only | H `C506 V06 global`: history GET count expected 2/new revision |
| PC-23 | Remove concurrency prefix from event mapping | I `C506 V07 changed-event`: concurrency query isInvalidated expected true |
| PC-24 | Remove pipeline key from event mapping | I `C506 V07 changed-event`: pipeline isInvalidated expected true |
| PC-25 | Editor submits Code parallel 2 when selected value is 1 | B `Project_override_clear_and_live_receipts_preserve_inheritance`: independent GET Code maxParallel expected 1 |
| PC-26 | No limit sets specified=false | E `C506 V10 review-queued`: PUT roles.Review.maxQueued expected present null |
| PC-27 | Row Revert sets nullable null instead of removing override | E `C506 V10 review-queued`: second PUT roles.Review expected absent |
| PC-28 | Clear toolbar immediately calls mutation | E `C506 V11 cancel`: PUT count expected 0 |
| PC-29 | Clear submits existing overrides | E `C506 V11 project`: PUT overrides expected exactly `{}` |
| PC-30 | Remove pending gate from button/submit handler | E `C506 V09 double-submit`: held request count expected 1 |
| PC-31 | Reset draft/reason on 409 | E `C506 V12 conflict-project`: Code input expected 1/reason unchanged |
| PC-32 | Keep old revision after conflict GET | E `C506 V12 conflict-project`: explicit second PUT expected 5/8 |
| PC-33 | Automatically retry PUT after refreshed conflict | E `C506 V12 conflict-global`: PUT count expected 1 before second click |
| PC-34 | Permit Save after conflict GET fails | E `C506 V12 refresh-fails`: Save expected disabled; PUT count 1 |
| PC-35 | Adopt refreshed data even when dirty | E `C506 V12 live-dirty`: Code input expected 1 and reason retained |
| PC-36 | Ignore fresh data even when clean | E `C506 V12 live-clean`: input/revision expected fresh value |
| PC-37 | Copy inherited Code value into explicit project override on refresh | E `C506 V12 inherited-change`: Code override expected 1, inherited expected 2 |
| PC-38 | Offline catch emits successful saved notification | E `C506 V09 offline`: success notification count expected 0 |
| PC-39 | Ignore 422 and clear field errors | E `C506 V13 field-error`: Code input described error and alert expected |
| PC-40 | Resolve missing project to global editor | N `C506 V15 missing-project`: global Save/PUT expected unavailable/0 |
| PC-41 | Reuse P draft when selector changes to Q | N `C506 V15 scope-selector`: Q starts inherited with Q revisions, P draft absent |
| PC-42 | Bypass invalid-draft gate in editor | E `C506 V09 invalid-disabled`: invalid Save disabled and PUT count 0 |
| PC-43 | Replace read-only mode badge with enabled mode Select | E `C506 V08 rows-and-sources`: editable Mode control expected absent |
| PC-44 | Label global source as Built-in default | E `C506 V08 rows-and-sources`: Code inherited source expected Global override |
| PC-45 | Display role maxParallel instead of combinedParallel | E `C506 V08 rows-and-sources`: effective Code cap expected 4, not infinity |
| PC-46 | Iterate client AGENT_TASK_ROLES rather than supportedRoles | E `C506 V08 dto-only-role`: FutureRole row expected present |
| PC-47 | Restore Tabs defaultValue instead of controlled value | `SettingsPage.test.tsx` `C506 V16 controlled-tab`: Concurrency selected after navigation |
| PC-48 | Remove project query parameter from row link | `ProjectConfig.test.tsx` `C506 V17 project-row-link`: URL project expected P |
| PC-49 | Notify Saved for same returned revision | E `C506 V14 no-change`: notification expected No change |

Inventory audit: **guards=49, mapped=49, missing=0, duplicate PC maps=0**. All 49 PCs must run after resumption; current run count 0, pending 49. G-30/42 may have both button and handler gates in Code: if independently bypassable, split them and add a distinct PC before claiming a complete Code inventory. The same rule applies to separately implemented per-field validators; do not count a shared helper test as proof of bypassable callers. New/unbound cases stay binding required, not waived by this audit.

### Out of scope

- Mode editing, Auto provenance, restore history, per-board scope, host budgets/runner capacity, pipeline DTO expansion: D-6/D-12 and accepted operator decisions. Mode preservation on Save and removal on explicit Clear are in scope.
- Backend admission, settings transaction/locking, durable session queue delivery and transcript receipt: no implementation change. Client tests cannot prove these. Existing SignalR publish failure/restart replay is not asserted; authoritative GET recovery is tested.
- Crash recovery of an unsaved browser draft is not promised. Invalid project/query state withholds writes; it does not choose a scope for the operator.
- No standalone full Antiphon.Tests, whole-Unit or broad E2E runs. The full client run remains because App routing, shared query invalidation and lint are in the accepted plan.
- S2 editor, not covered / known limitations (Review 24d4e81c diagnostics, same result on base 29e70fefa): `R3 restore during PUT lower read remains current after late reply`, `R3 restore during PUT reused read remains current after late reply`, `R3 same store time equal read after failed save refresh is current`; a Save continuation after unmount still invalidates the shared query cache and shows the global notification.
- JSDOM does not prove layout or wire transport. V-18 covers narrow viewport reachability and actual browser/server consumption; it is not a screenshot baseline or an accessibility certification.

### Checkpoints

Fresh census at the base SHA, **read from source, no tests executed**: 114 client files, 1,013 `it/test` declarations; `.each` expansion adds 48 and the two-context `viteProxyConfig` loop adds 3 = **1,064** results when dist exists. Expansion inputs: App 13; taskVisuals 3; TaskDrawer 3; AttentionPanel 2; attentionVisuals 6; TaskDetailBody 2; AgentsPage 2; ProjectConfig 2+3; ProjectSetupModal 2; StandingSessionRecovery 3; AgentReplyStyle 2+2+2; TaskCard 9; CardHistory 3; CardFilePrivacy 2+3+2+2. No .skip/.todo declarations; App has one `skipIf(!existsSync(dist/index.html))`, removed as an execution condition by the fresh bundle prerequisite, not by editing that test.
Source listing recipe: `rg --files client` narrowed to `\.(test|spec)\.[cm]?[jt]sx?$`; declaration census `^\s*(it|test)(\.(each|for|skipIf|skip|todo|only|concurrent|fails))?(<[^\n]+>)?\s*\(`, followed by the literal-array and enclosing-loop inspection above. TUnit source search finds no current DispatchConcurrencySettingsE2ETests; nearest ProjectDeleteE2ETests has 8 `[Test]` methods, with browser/fixture bodies read. Planned E2E filter is deliberately method-exact, target count 1 once created. A zero-result Code run is a wrong/missing binding and fails the checkpoint.

Planned new Vitest counts: M 67, H 5, I +1, E 28, N 3, SettingsPage +1, ProjectConfig +1 = **106**. Four new files → **118** client files/**1,170** results. CP-1 existing 5 + new 73 = 78, plus the Review ef9c0e6b F1 repair cases (Code task 48cc1d59): M +39 (`C506 F1 decode-document-role`/`encode-role`/`roundtrip-role`/`diff-saved-only`/`diff-draft-only` × 5 prototype names, `C506 F1 decode-supported-role` × 5, `C506 F1 source-label` × 9) and H +3 (`C506 F1 keys-project-named-global`, `project-scope-own-property`, `project-id-path-encoded`) = **120** (M 106, H 8, I 6); full-client totals in CP-3 rise by the same 42 (**1,212**); CP-2 existing Settings 3 + Project 13 + new 33 = 49, plus the S2 Review e9c9efa5 F1-F3 repair cases in the editor file: E +12 (`C506 S2F1 live|conflict mode-changed|mode-added|mode-removed` 6, `C506 S2F2 refresh-fails`/`refresh-pending`/`newer-after-save` 3, `C506 S2F3 pending-save`/`pending-clear`/`pending-error` 3) = **61** (E 40); full-client CP-3 rises by the same 12 (**1,224**); plus the S2 Review b6249616 F1 repair cases in the editor file (Code task 87938db0): E +12 (`C506 S2R3 rollback-global-clean`, `rollback-project-project-clean`/`rollback-project-global-clean`, `rollback-dirty-global`/`rollback-dirty-project`, `reused-revision`, `same-revision-new-content`, `after-save-lower`, `after-failed-refresh-equal-data`, and the Review safety controls `late-put-newer-get`, `conflict-reload-controls`, `unmount-pending`) = **73** (E 52); full-client CP-3 rises by the same 12 (**1,236**). Until S3 adds ConcurrencySettingsTab.test.tsx (3) and the SettingsPage/ProjectConfig cases (+1 each), CP-2 runs 3 files with exactly **68** results (E 52 + SettingsPage 3 + ProjectConfig 13). Code must confirm these source bindings and report runtime counts, without treating today's absent new files as already verified. If unrelated source additions land, record a fresh delta census before running; do not silently lower floors or drop names.

Each row has one test selection and an isolated worktree/runtime transform (Vitest) or one isolated .NET build (CP-4); `Build=n/a` is the owner's command-row schema, not permission for hidden .NET builds. CP-1/2 run on server2; CP-3 may run on server2, but run CP-3 and CP-4 sequentially on the desktop lane to reuse the fresh bundle at the identical committed SHA and avoid a second frontend build. This is lane placement, not an OS dependency of the client tests. The desktop checkout must fetch the pushed S5 SHA before building. Rebuild `client/dist` there before CP-3/4; no source changes between them.

CP-3 includes one explicit frontend build in its command, `npm --prefix client run build`, followed by its sole full-client test selection; the checkpoint owns the build slot for both. This builds once in the assigned worktree before tests so App's bundle case executes. If CP-3 is instead on server2, a second slot-gated build on the desktop before CP-4 is a declared 3-minute lane-transfer cost and must be reported. CP-4's .NET build stays `bin-c506-e2e/`; verify Chromium/Docker availability before starting; no production runner fallback. No tests/builds are launched by this design task.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---|
| CP-1 | S1 | n/a | client-model | `pwsh -NoProfile -File scripts/test-client.ps1 src/api/dispatchConcurrency.test.ts src/features/settings/dispatchConcurrencyModel.test.ts src/hooks/useSignalRInvalidation.test.ts` | V-1–V-7, R-4, F1 | 3 files, exactly 120 results, 0 failed/skipped | n/a | 4 | false |
| CP-2 | S1-S3 | n/a | client-editor | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ConcurrencySettingsTab.test.tsx src/features/settings/SettingsPage.test.tsx src/features/settings/ProjectConfig.test.tsx` | V-8–V-17, R-2, R-3 | 4 files, exactly 73 results, 0 failed/skipped (before S3: 3 files, exactly 68) | n/a | 6 | false |
| CP-3 | all | n/a | client-full | `npm --prefix client run build && pwsh -NoProfile -File scripts/test-client.ps1` | V-1–V-17, R-1–R-5 | 118 files, exactly 1236 results, 0 failed/skipped; lint zero; CLIENT TESTS EXIT CODE: 0 | n/a | 10 | true |
| CP-4 | all | `tests/Antiphon.E2E -> bin-c506-e2e/` | e2e-concurrency | `/*/*/DispatchConcurrencySettingsE2ETests/Project_override_clear_and_live_receipts_preserve_inheritance` | V-18 | exact named method, 1 executed, 0 failed/skipped; current dist and owned runner | 1 | 15 | true |

Run via `dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this-path> --rows <CP-list> --expected-source-sha <committed-slice-sha>` through the host build-slot gate. Select `--rows CP-1` after S1, `--rows CP-2` after S3, and `--rows CP-3,CP-4` after S5 on desktop; wait to terminal completion, exit 75 is still running. Bootstrap or direct build/test drivers take `scripts/build-slot.ps1`; checkpoint runner owns row slots. Slot exit 4 means not run, not unleased retry. Preserve unedited CHECKPOINT lines, Vitest per-file result counts and clean source/build provenance. Command rows' success exit alone does not establish their specified census. Keep JSON/TRX/logs ignored and record output ownership; remove owned bin-c506-e2e directories before settlement. Code/Review run `scripts/check-evidence-diff.ps1` over the full task range. Review checks desktop evidence against the same S5 commit, not against an earlier runner receipt.

### Cost

All times estimated, not measured here. Ordinary V/R floor (Code) = CP-1 4 + CP-2 6 + CP-3 10 + CP-4 15 = **35 minutes**. This includes the CP-3 frontend build (3) and CP-4 isolated .NET build (4); test/transform portion is 28. Dependency/tool/Chromium setup allowance **8**, expected slot wait **0** on an idle host (report actual added wait). Ordinary setup/build/V/R total **43**; authoring 225 + 43 = **268 minutes** Code budget. Alternate two-host CP-3/4 adds 3 = 271.

PC floor (Mutation) is separate: 48 exact Vitest cases at **3 minutes each** (0.5 defect/setup + 1 red transform/test + 0.5 restore + 1 fresh green transform/test) = **144**; PC-25 exact E2E method **34** (2 defect/setup + 15 red build/test + 2 restore + 15 green rebuild/test) = **178 minutes**. Mutation setup/discovery/reporting allowance **12**; total **190**. PC-25 rebuilds client/dist on both arms; fixture/build failure is not red. Filter names are the 49 exact detectors above, not whole files/suites. Ordinary+PC floors **213**; combined setup/build/V/R/PC work **233** (=43+190), excluding 225 authoring and unpredictable slot waits.

Savings relative to running the accepted 10-minute full-client row after each of S1, S2, S3 and S5: 40 minutes versus CP-1+CP-2+CP-3 =20, **20 minutes saved**. One shared desktop frontend build for CP-3/4 saves **3** more versus rebuilding on both hosts. Do not skip CP-3/4 or any pending PC to realize a larger saving.
