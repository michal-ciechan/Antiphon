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
| CP-1 | S1 | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/settingsPendingReads.test.tsx src/features/settings/DispatchConcurrencyEditor.test.tsx` | V-1, V-2, V-3 | both files run, nonzero count; new C1274 rows red before the fix | n/a | 5 | true |
| CP-2 | S2 | n/a | client-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/settingsPendingReads.test.tsx src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ProjectDeleteDialog.test.tsx src/features/settings/ApiKeysSection.test.tsx src/features/settings/ProjectConfig.test.tsx src/features/settings/RoutingSettingsTab.test.tsx src/features/settings/RunnerDefaultsSection.test.tsx src/features/settings/RolePolicySection.test.tsx src/features/settings/routingSettingsModel.test.ts src/features/settings/ConcurrencySettingsTab.test.tsx` | V-1, V-2, V-3, R-1 | the listed files passed, nonzero count, 0 failed/skipped | n/a | 8 | true |
| CP-4 | S2 | n/a | client-settings-page | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/SettingsPage.test.tsx` | R-1 | inherited red: `does not fetch routing queries until the Routing tab is opened` and `?tab=routing selects the tab and mounts routing queries without fetching templates` fail with `expected 15 to be 1` at base 78e400178 (run in a detached base worktree, see the evidence doc); other tests pass | n/a | 2 | true |
| CP-3 | S2 | n/a | client-build | `npm --prefix client run build` | R-2 | typecheck and production build exit 0 | n/a | 3 | true |
