# CARD-1274 Settings tabs: pending, paused and failed reads

Task/landing owner: `6889fec1`. Base: `78e4001788e99ebc04f0590a84db18442f6324c6`.
Round: Final. Branch: `feat/card-task-6889fec1`.

## Scope

Client change; the CARD-1270 follow-up. React Query 5 reports an offline read with no data as
pending and paused (`isLoading` false, no data, no error). Components that branched on
`isLoading` fell through to their success branch and rendered an empty state, a default or a
definite value. The fix reuses `pendingReadLabel` (`routingSettingsModel.ts`) and moves the
Routing tab's `PendingRead` into `PendingRead.tsx` so the other tabs share it.

## Surface x read x state

Before = rendered text with that read paused (offline, no data). After = text the C1274 rows assert.

| Surface | Read | Before (paused) | After: loading / paused | After: failed |
|---|---|---|---|---|
| ProjectDeleteDialog impact and Delete | `/api/projects/{id}/deletion-impact` | no impact text, Delete disabled | `Deletion impact loading.` / `Deletion impact unknown (read paused offline).`, Delete disabled | `Could not check what this project owns. Delete stays disabled until the check loads.`; a failed refetch hides the older impact and the acknowledgement and disables Delete (before: older impact listed, Delete enabled after the tick) |
| ApiKeysSection list and Save key | `/api/api-keys/global`, `/api/projects/{id}/api-keys` | `No global API keys configured.`, Save key enabled | `API keys loading.` / `API keys unknown (read paused offline).`; Save key disabled with `Save key is disabled until the API key list loads: a key with the same name would be replaced.` | error alert (unchanged); Save key disabled with the same note |
| TemplateManager page | `/api/settings/templates` | `No templates found.`, `No template groups.` | `Templates loading.` / `Templates unknown (read paused offline).` | `Error loading templates` (unchanged) |
| TemplateManager groups section | `/api/settings/template-groups` | `No template groups.` | `Template groups loading.` / `… unknown (read paused offline).` | `Could not load template groups.` (before: `No template groups.`) |
| Template model routing (per template) | `/api/settings/providers` | `Add a provider first to configure model routing.` | `Providers loading.` / `Providers unknown (read paused offline).` | `Could not load providers.` (before: the add-a-provider hint) |
| Template model routing (per template) | `/api/settings/templates/{id}/model-routing` | `No routing rules. …` | `Model routing loading.` / `… unknown (read paused offline).` | `Could not load model routing.` (before: `No routing rules. …`) |
| ProjectConfig page | `/api/projects` | `No projects yet. …` | `Projects loading.` / `Projects unknown (read paused offline).` | `Error loading projects` (unchanged) |
| ProjectConfig Boards cell | `/api/boards` | `--` | `Boards loading.` / `Boards unknown (read paused offline).` | `Could not load boards.` (before: `--`) |
| AgentTuiConfig page | `/api/agent-tui/profiles` | empty table, `New profile` | `AI Agent TUI profiles loading.` / `… unknown (read paused offline).` | `Error loading AI Agent TUI profiles` (unchanged) |
| StatusTab page | `/api/github/status` | `No`, `Not connected`, `0`, `15 minutes` | `GitHub status loading.` / `GitHub status unknown (read paused offline).` | `Error loading status` (unchanged) |
| ProviderConfig page | `/api/settings/providers` | `No providers configured. …` | `Providers loading.` / `Providers unknown (read paused offline).` | `Error loading provider configuration` (unchanged) |
| DispatchConcurrencyEditor history | `/api/dispatch-concurrency/revisions` | `No revisions yet.` | `History loading.` / `History unknown (read paused offline).` | `Could not load history.` (unchanged) |

Decisions:

- D-1 ApiKeysSection: the PUT is keyed by name (`usePutApiKey`, `writePath`), so Save key on an
  unread list can replace a stored key. Save key is disabled while the list is pending or failed.
  Replace and Delete act on listed rows and are unchanged.
- D-2 ProjectDeleteDialog: `force` follows `requiresConfirmation` from the impact read, so a failed
  refetch over an older answer is treated as unknown (`impact.isError` hides the data).
- D-3 Fetching with stale data and a successful refetch keep the last value (CARD-1270 rule).

Enumerated, not changed (disclosures, see the report): ProjectConfig readiness cell renders `--`
for a pending or failed read (not a definite value); ConcurrencySettingsTab scope select labels an
unread project by its raw id; DispatchConcurrencyEditor scope read shows a loader for paused;
ProjectSetupModal's existing-project hint and AgentTuiProfileModal's runner list read lists that
can be pending.

## Verification design

- V-1 `settingsPendingReads.test.tsx`, `C1274 $read read $state`: each read in `c1274Reads` x loading, paused,
  failed, success. Each row asserts the query state, the literal label for its state, the absence
  of the other labels, of its success values and of its empty-state texts, and disabled write
  buttons (Delete, Save key with a typed name and value) outside success.
- V-2 same file: `C1274 project delete dialog offline sends no DELETE and shows no impact until the
  check loads`; `C1274 api keys offline keep Save key disabled with a typed key and send no PUT
  after reconnect`; `C1274 $name offline from the first render shows its read as unknown`
  (ProjectConfig, ProviderConfig, TemplateManager, AgentTuiConfig, StatusTab); `C1274 project
  delete dialog hides the stale impact and keeps Delete disabled after a failed refetch`.
- V-3 `DispatchConcurrencyEditor.test.tsx`, `C1274 history read %s shows no empty history`
  (loading, paused).
- R-1 the component files touched or rendering a changed component: `ProjectDeleteDialog.test.tsx`,
  `ApiKeysSection.test.tsx`, `ProjectConfig.test.tsx`, `DispatchConcurrencyEditor.test.tsx`,
  `RoutingSettingsTab.test.tsx`, `RunnerDefaultsSection.test.tsx`, `RolePolicySection.test.tsx`,
  `routingSettingsModel.test.ts`, `SettingsPage.test.tsx`, `ConcurrencySettingsTab.test.tsx`.
- R-2 client typecheck and production build.
- Amendment after CP-2 run `20261015-061840-d5fc`: `SettingsPage.test.tsx` moved from CP-2 to CP-4. Its two
  template-request-count tests fail at the base with the same assertion, so the row records them as
  inherited instead of reddening the C1274 rows' receipt.
- No server source changes, so no .NET class is affected; the brief excludes the whole Unit lane.

PC-1..PC-6 are pending for SourceLanding Mutation; see
`docs/superpowers/mutations/2026-10-15-card-1274-candidate.md`.

### Cost

Checkpoint tool bootstrapped through build-slot; client dependencies installed through
build-slot, npm's esbuild hard link broken before the client rows. `--total-timeout 150m`,
committed `--expected-source-sha`, no keep-outputs on Linux.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/settingsPendingReads.test.tsx src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ProjectSetupModal.test.tsx` | V-1, V-2, V-3, V-4, V-5, V-6 | three files run, 147 executed; repair rows red before the fix | n/a | 5 | true |
| CP-2 | S2 | n/a | client-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/settingsPendingReads.test.tsx src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ProjectDeleteDialog.test.tsx src/features/settings/ApiKeysSection.test.tsx src/features/settings/ProjectConfig.test.tsx src/features/settings/RoutingSettingsTab.test.tsx src/features/settings/RunnerDefaultsSection.test.tsx src/features/settings/RolePolicySection.test.tsx src/features/settings/routingSettingsModel.test.ts src/features/settings/ConcurrencySettingsTab.test.tsx src/features/settings/ProjectSetupModal.test.tsx` | V-1, V-2, V-3, V-4, V-5, V-6, R-1 | 11 files; >= 480 executed, 0 failed/skipped (inspect Vitest log; command-row Min must be n/a) | n/a | 8 | true |
| CP-4 | S2 | n/a | client-settings-page | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/SettingsPage.test.tsx` | R-1 | inherited red: `does not fetch routing queries until the Routing tab is opened` and `?tab=routing selects the tab and mounts routing queries without fetching templates` fail with `expected 15 to be 1` at base 78e400178 (run in a detached base worktree, see the evidence doc); other tests pass | n/a | 2 | true |
| CP-3 | S2 | n/a | client-build | `npm --prefix client run build` | R-2 | typecheck and production build exit 0 | n/a | 3 | true |

## Repair 9277c201: independent inventory before implementation

Start `97acbfa6e1008b850594672d2b6ebef9bc57c24a`; Review `010d7154`, F1-F4.
L = first loading; P = first paused offline; F = first failed; RF = failed refetch with
cached data; E = successful empty or partial response. This is a source inspection,
not execution evidence. Each row names its read boundary and the decision for this repair.
Static input defaults and values supplied by a loaded parent are distinguished from read defaults.

| Component / surfaces | Reads | L / P | F | RF | E / decision |
|---|---|---|---|---|---|
| SettingsPage tab labels and selected tab | URL; child reads below | static tabs | static tabs | static tabs | URL default is navigation, not a server value |
| TemplateManager list, names, group-name column, Add/Edit/Delete dialogs | templates | pending label | error | error | empty-list message; modal fields copy the loaded template; optional group-name dash comes from that DTO |
| TemplateManager Group select in Add/Edit | template-groups | ungrouped placeholder (F1) | ungrouped placeholder (F1) | cached options | fix with explicit read state, disabled selector, retained group id; a missing stored id after a successful list must remain visible |
| TemplateGroupsManager list, template counts, Add/Edit/Delete | template-groups | pending label | error | error | legitimate empty message; Add fields are local; Edit fields and Delete count come from a loaded row |
| TemplateModelRouting table, provider names, Add Rule | providers and template model-routing | pending label | error | error | empty provider hint / empty routing text; missing provider id says Unknown |
| TemplateModelRouting open edit/add Provider select | providers | blank if query reset while modal open | cached or blank options | cached options | disclose: opened draft can outlive a provider reset; stored provider id retained, selector has no explicit read label; separate from F1 |
| ProviderConfig / ProviderList table, Add/Edit/Delete, connectivity result | providers; connectivity mutation | pending label | error | error | legitimate empty list; type options static; API key mask is from row; create fields local |
| ProjectConfig / ProjectList counts, search, edit fields, API-key sub-editor, delete dialog | projects; nested reads below | pending label | error | error | empty projects and filtered count follow loaded list; absent optional commit-on-settle field displays Inherit (on), a DTO compatibility default, disclosed |
| ProjectBoardsCell badges and Delete Board | boards | pending label | error | error | dash for loaded empty; count and last-board warning follow loaded list |
| ProjectReadinessCell / readiness popover | readiness list per visible ids | spinner / dash | dash | dash | missing project row -> dash; retain as unknown marker, not Ready or a missing-count claim |
| ProjectReadinessPanel / status icons / fix controls | readiness prop, mutation results | parent gated | parent gated | parent gated | displays checks supplied by parent; no independent read |
| Project edit repository picker | github/repos | picker absent | picker absent | cached picker | hidden if empty; No repos found is a search over populated data; URL input stays editable; retain and disclose missing read feedback |
| ProjectDeleteDialog impact, acknowledgement, Delete | deletion-impact | label, disabled | error, disabled | older impact hidden, disabled | success uses canDelete and requiresConfirmation; existing acknowledgement persistence is unchanged |
| ApiKeysSection global and project lists, Save key, Replace/Delete | api-keys for scope | label, Save disabled | error, Save disabled | error, Save disabled | legitimate empty list; row actions require displayed row |
| ProjectSetupModal Directory: existing-project hint | projects | hint absent | hint absent | cached hint | absence is not a claim of uniqueness; retain, server checks conflict |
| ProjectSetupModal Directory: git / browse hints | filesystem/workspaces; DirectoryAutocomplete browse | git hint absent; browse suggestions empty | git hint absent; browse suggestions empty | cached hints | git-negative text needs returned gitInfo; retain; imported browse child is disclosed below |
| ProjectSetupModal project/board/default columns | local inputs | local choices | local choices | local choices | constants are new-project preferences, not stored values |
| ProjectSetupModal First agent: preset, bundles, name/prompt preview, model level, switches | setup-catalog | empty choices/default draft (F3) | same (F3) | cached draft | fix: withhold catalog-dependent form and disable Create until successful catalog; empty/missing selected preset blocks agent creation with explicit text |
| ProjectSetupModal First agent: profile, remote-control support | agent-tui/profiles, runner-types via support hook | blank profile / Not available (F3) | same (F3) | cached support | fix: profile read message, disabled RC and Create for agent while unknown; successful empty profile list is an actual empty list |
| ProjectSetupModal First agent: exact-model select | selected profile models | tier-only dropdown while unread | same | cached list | fix at wizard boundary with model read message and Create gate; keep profile selection usable after recovery |
| ProjectSetupModal Delegation: root boundary, concurrency, depth, tier | setup-catalog | negative root and zero counts (F2) | same (F2) | cached facts | fix with pending/failed label including failure detail; successful response supplies the values |
| ProjectSetupModal review/Create and completed readiness | setup-catalog; setup mutation | Create currently enabled (F3) | same | cached draft | fix Create guard; completed UI consumes mutation response; CardModal/DelegateModal mount after that response |
| AgentTuiConfig / ProfileRow table, validation, models/secret metadata, actions | agent-tui/profiles | pending label | error | error | loaded empty table; validation and model metadata come from row; validation/refresh are mutations |
| AgentTuiProfileModal Runner type and setup guidance | runner-types | blank dropdown | blank dropdown | cached guidance | fix explicit unknown label and disabled selector; retain kind value; loaded empty/missing type shown by kind id |
| AgentTuiProfileModal commands, models, secrets, Save | loaded profile prop; local create defaults | independent of runner catalogue | independent | profile snapshot | profile kind/commands retained; saving does not depend on catalogue lookup; new profile uses explicit OpenCode defaults |
| StatusTab connectivity, cache count, age, TTL, refresh | github/status | pending label | error | error | optional null timestamps use dash; malformed partial DTO could reach 0/15 defaults, disclosed as outside typed response contract |
| ConcurrencySettingsTab Scope options and label | projects | Global defaults plus raw selected id | same | cached names | retain raw id as identity; disclose incomplete options and missing read feedback; Unknown project is restricted to a returned list |
| DispatchConcurrencyEditor scope form, effective cells, numeric controls, clear confirmation | dispatch-concurrency scope | spinner | error + Retry | cached draft | retain spinner as pending, with no numeric controls; populated DTO governs limits; conflict/refresh rules separate |
| DispatchConcurrencyEditor history | revisions | pending label | error | error plus cached history | empty message restricted to successful empty list |
| RoutingSettingsTab AvailabilitySection / hold form | model-availability | pending label | error | error | actual empty snapshot message; imported hold form is a local mutation form |
| RoutingSettingsTab UsageSection / UsageRow | subscription-usage | pending label | error | error | no-sample unknown text; optional percentage/time fields show dash |
| RunnerDefaultsSection / form / history restore | runner-defaults, session-runners, revisions | labelled reads | defaults error; catalogue/history unknown | same error handling | stored missing runner kept with catalogue diagnosis; inherited choice is from loaded defaults; history empty message gated |
| RolePolicySection / StagePinCell | role-policy | labelled read | error | error | role DTO supplies policy and optional stage pin; empty pin candidate text describes returned pin |
| RolePolicyRow Worker/Orchestrator next-create cells | complexity-chains/effective per role | preview unknown if matrix needed | preview unknown | cached projection | retain cached data per D-3; disclose RF is not separately diagnosed here |
| MatrixSection / Any role cells / role rows | complexity-chains; role effective cells | labelled read, configure withheld | error | error | typed empty row fallback; partial failedReads DTO uses unknown labels |
| Routing stage pin banners / CardPinsList / counts | routing-pins | unknown text | unknown text | unknown text | counts and empty-pin state after successful response |
| ComplexityChainCellEditor candidates, metadata, overwrite, Save/Clear | parent loaded snapshot; role readPending | open role editor gated on readPending | parent snapshot retained | parent snapshot retained | partial failedReads needs overwrite acknowledgement; open Any-role editor retains snapshot if list resets (disclosed, CARD-1275 area) |
| PendingRead | label prop | supplied label | supplied label | supplied label | no read |

DirectoryAutocomplete shows empty suggestions without a pending/error diagnosis and reports pathMissing=false before a response. It does not show an exists assertion; this UX remains disclosed. Imported DirectoryAutocomplete and AgentTuiSelection are outside this directory. The wizard repair
covers AgentTuiSelection's profile/model read boundary; DirectoryAutocomplete's browse UX and the
post-create CardModal/DelegateModal are unchanged. No claim of exhaustive runtime coverage is made
for malformed DTOs, optional compatibility defaults, or these imported dialogs.

### Repair decisions and verification

- F1: unknown Group select is disabled but saving another template field keeps its stored group id.
- F2: show the setup-catalog read state in Delegation, including the HTTP failure detail.
- F3: choose the Create-project gate (including skipped-agent submissions while catalog is unknown).
  Agent creation additionally waits for profile/model reads. Catalog presets initialize explicit
  fields before submission. Source contract: ProjectSetupService forwards fields to AgentService;
  AgentPresets.Apply uses `requestValue ?? presetValue`, so false and empty bundles override presets.
- F4: replace the helper's universal comment with a description of the fixtures it actually serves.
- V-4: C1274 repair Group rows: Add/Edit x loading/paused/failed/stale failure; retained id and recovery;
  successful empty/partial lists keep the edit's stored group visible.
- V-5: C1274 repair wizard rows: catalog and profiles x loading/paused/failed/stale failure;
  delegation literals, Create gate and recovery payload; selected-model read boundary; empty presets.
- V-6: C1274 repair Runner type rows: loading/paused/failed/stale failure, retained kind and recovery.
- R-1 adds the full ProjectSetupModal test file to CP-2; earlier assertions stay in their files.
- R-2 remains typecheck plus production build. Unit is excluded by this client repair brief.
- Manual acceptance: source-level server contract trace and this inventory; browser not commissioned.
- New PCs are pending for Mutation; this stage exercises ordinary red tests before the repair.

Repair red-fixture adjustment: Mantine Switch includes its description in the accessible name.
The four profile rows use a prefix locator followed by a literal full-name expectation. Their
first run stopped at the locator before reaching the defect; CP-1 is repeated before production edits.
