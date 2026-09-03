# CARD-0333 — Settings: model availability, honest subscription-usage visibility, and the role × complexity routing editor

**Date:** 2026-09-03
**Card:** CARD-0333 “Settings: model availability + usage visibility, and role × complexity model picker UI (CARD-0332 dependent)”
**Depends on:** CARD-0332 being landed first. Its matrix schema and HTTP contract are the contract this plan consumes; this card must not substitute a second routing representation.

## Outcome and scope

Add a **Routing** tab at \`/settings?tab=routing\`. It becomes the fleet-wide operator surface for:

1. current model availability: available versus held, hold source/reason, and reset/“until cleared”;
2. carefully-qualified subscription-usage observations, when the optional monitor has actually produced a sample;
3. the live, sparse \`Any role | role\` × \`Hard | Medium | Easy\` complexity-chain matrix, including ordered candidate editing and the routing pins that can take precedence.

The settings are global database-backed routing state, not session or board preferences. CARD-0333 deliberately does **not** add a way to change the model of a currently running session, change \`RolePolicy\`, seed chains, or make model availability itself a routing choice.

## Decisions

### D1. Use CARD-0332’s matrix verbatim; do not call it Plan/Complex or Coding/Simple on the wire

The grid is the \`roles[]\` and \`complexities[]\` returned by \`GET /api/complexity-chains\`, with a first synthetic row labelled **Any role** for \`role: null\`. The current vocabulary stays **Hard**, **Medium**, and **Easy**. Plan and Code are ordinary returned role rows; the UI must not hard-code a two-role, six-cell matrix.

For each returned routable role, fetch \`GET /api/complexity-chains?role={role}\` in parallel. That endpoint deliberately returns the three **effective** cells and \`resolvedFrom: role | any | config | none\`; the full-list response alone cannot correctly render a missing role cell which inherits an any-role or config list. The Any-role row comes from the first three null-role entries in the full list.

Each cell shows its ordered aliases, live candidate availability, provenance, expiry, and an unambiguous source badge:

| \`resolvedFrom\` | UI text | Meaning |
|---|---|---|
| \`role\` | Own rule | This role-specific row supplies the complete list. |
| \`any\` | Inherits Any role | Clearing/setting Any role changes this fallback. |
| \`config\` | Configuration fallback | A restart-cadence default supplied it; it is not a live matrix row. |
| \`none\` | Unset — dispatch blocks | A \`-Complexity\` dispatch for this role/tier has no candidate until an operator sets a row. |

An existing role cell replaces an inherited list as a whole; it never appends to it. The cell details and confirmation language must make that clear.

### D2. The matrix editor writes only Human rows and preserves the chain service’s operations

Selecting **Configure** on a cell opens a settings-owned editor with an ordered list of one to eight \`(agentKind, modelLevel)\` candidates. It supports add, remove, and deterministic move-up/move-down ordering; optional reason; optional \`notAfter\`; and a destructive **Clear override** action. Save sends:

\`\`\`json
PUT /api/complexity-chains/{role}/{complexity}
{
  "candidates": [{ "agentKind": "Codex", "modelLevel": "Frontier" }],
  "provenance": "Human",
  "reason": "...",
  "notAfter": "..."
}
\`\`\`

The Any-role row writes \`/api/complexity-chains/any/{complexity}\`; clearing either form uses the matching three-segment \`DELETE\`. A client click is always \`Human\`, so it may replace an Auto row and never attempts an Auto-over-Human write. The editor surfaces the server’s candidate, date, role, and conflict problem details instead of reimplementing validation.

Use the existing \`AGENT_MODEL_LEVEL_OPTIONS\` / \`AgentModelLevel\` and the Mantine \`ModelLevelSelect\` presentation pattern. Define the three current delegatable chain kinds (\`ClaudeCode\`, \`Codex\`, \`Grok\`) in one picker-local option list. Do **not** derive write options from model availability’s flat aliases, \`tierAlias\`, or Agent TUI profile/exact-model APIs: those represent runtime availability, presentation aliases, and profile model IDs respectively, not the \`agentKind + modelLevel\` chain wire type. The server remains authoritative on a bad or newly unsupported pair; the returned chain alias is presentation only.

Before clearing, distinguish “fall back to Any role/config” from “becomes unset and blocks”; deleting the Any-role row gets the stronger warning because it may remove the fallback for many roles. Do not add a second chain table, configuration blob, auto-seed, or \`RolePolicy\` fallback.

### D3. Pins are visible at the point they alter expectations; they are not edited here

Add a typed \`client/src/api/routingPins.ts\` read hook for \`GET /api/routing-pins\`, polling with the same short cadence as the chain and availability views. Group active pins into:

- **stage-wide pins**, displayed as a badge and explanation on the matching role row; a Required full pair means the matrix cell is bypassed, while a Preferred pair is tried before the matrix candidates and then falls through to the cell;
- **card-specific pins**, shown in a compact, separately labelled list because a global Settings view cannot say that they override every task in that role.

The screen is read-only for pins. It must name the existing stage-wide Human Required Code → Grok pin when present, so the Code row never appears to be malfunctioning. Pin creation/clearing remains CARD-0305’s established route/script surface.

### D4. Model availability is the existing live hold state; use it rather than inventing another status store

Reuse \`useModelAvailability\` and its existing mutation hooks against \`GET /api/model-availability\`, \`PUT /api/model-availability/{kind}/{alias}\`, and \`DELETE\`. The Routing tab’s availability section renders:

- every alias in \`available[]\` as **Available**;
- each hold as **Held**, with \`disabledUntil\` formatted as its reset when present, otherwise **until cleared**;
- hold source, reason, and observed time where supplied.

Keep the existing hold/clear controls and their query invalidation semantics; they are already the supported operator correction for availability. The client must not claim that \`available[]\` is a typed model catalog or infer a kind from its alias. Empty and loading states must describe the actual availability snapshot rather than showing stale data as current.

The existing Orchestrator “Needs attention” model-availability and complexity-chain panels remain compact operational summaries. CARD-0090 S4 is already shipped, so no unfinished S4 work is redirected. Add a Manage routing settings link to the new tab; do not build a second editable chain panel in Orchestrator. CARD-0332’s small update to make that read-only panel distinguish \`Plan/Hard\` from \`Hard (any role)\` still belongs to CARD-0332.

### D5. “Usage remaining” is explicitly best-effort subscription telemetry, never a per-model promise

The only public routing/availability state today is a hold after a wall. It has no proactive remaining-quota number. There is, however, an internal \`SubscriptionUsageSample\` path:

- Codex \`/status\` is supported and can supply a subscription/profile-level remaining percentage and reset;
- Grok \`/usage\` is degraded and opt-in;
- Claude has no established proactive command;
- monitoring defaults off, and there is currently no HTTP or client surface.

Expose a **read-only** \`GET /api/subscription-usage\` that reads stored samples through \`SubscriptionUsageReader\`; it must never poll a provider, enable monitoring, or alter \`SubscriptionUsageMonitoringSettings.Enabled\`. Its DTO must contain only safe display data: provider, plan label when known, remaining percent when observed, reset time when observed, observed-at, and age. Do not expose raw command output, subscription/profile keys, paths, credentials, or session identifiers.

The Routing tab labels this section **Subscription usage observations (best effort)**, separately from model availability:

- no sample → **Unknown — usage monitoring is off or no provider sample is available**;
- a sample without a percent/reset → show only its observed state/time;
- a sample with a percent/reset → show the value and “observed at …”, not “live” and not attached to an individual model alias;
- old samples retain their timestamp/age and are never presented as current capacity.

There is no aggregate fleet percentage, no synthetic zero/100%, and no “usage remaining” field on an availability row. This fulfils visibility where the system has evidence while making the known coverage limit explicit. Broader provider polling, Claude telemetry, or a normalised model-level quota contract is a follow-up, not hidden scope in this UI card.

### D6. Define “affects every orchestrator and workflow” at the dispatch boundary

The help text and save confirmation state exactly what changes:

1. **New complexity-routed dispatches** read the saved global cell immediately. This already happens at create time; no fan-out or session restart is required.
2. **Queued complexity-chain tasks whose snapshot can no longer run, and Blocked-for-routing tasks** are re-walked against the current matrix on the dispatcher’s next tick. CARD-0090’s existing dispatcher/auto-resume path provides this; this card adds no bespoke requeue mechanism.
3. **Running sessions keep the model with which they started.** Saving a cell never interrupts, replaces, or migrates a mid-turn delegate.

Non-chain queued tasks and explicit \`-Kind\`/\`-Level\` dispatches retain their snapshots and are not retroactively routed by this matrix. This wording belongs near the editor and in the settings help, not only in documentation.

### D7. CARD-0097 remains a distinct follow-up, not a superseded card

\`DelegationSettings.RolePolicy\` still resolves non-\`-Complexity\` work and contains escalation, timeout, and WIP settings in addition to Kind/Level. It is startup configuration with no endpoint/client editor today. Therefore CARD-0333 must not mark CARD-0097 superseded or pretend the matrix governs every dispatch.

The Routing tab shows a concise “Applies to complexity-routed tasks” note with the D6 boundary. CARD-0097 can later add a read-only default/non-complexity section to this same tab, followed by any deliberately designed configuration-editing flow. It is not part of this card.

## Implementation slices

### S1 — Server read contract for honest subscription observations

1. Add a display-safe subscription-usage DTO beside the existing Application DTOs and an API endpoint mapping \`GET /api/subscription-usage\`.
2. Read the existing \`SubscriptionUsageReader\` snapshots only. Map the safe fields in D5, preserve nulls, and return an empty collection for no samples; do not add provider calls or settings mutation.
3. Add endpoint/serialization tests covering empty data, a full Codex observation, optional reset/percent, and proof that raw sample/profile/session details are absent.
4. Leave the existing parser, monitor, and quota-gate behavior unchanged. Their tests remain coverage for supported/degraded/no-sample semantics, not a reason to enable monitoring in this feature.

### S2 — API clients, query/mutation lifecycle, and settings tab shell

1. Extend \`client/src/api/complexityChains.ts\` to CARD-0332’s additive DTO: \`role\`, \`resolvedFrom\`, list \`roles\`/\`complexities\`, per-role effective query, and Human PUT/DELETE mutation hooks. Use distinct tuple query keys for list and per-role effective rows, then invalidate both after a successful write.
2. Add \`client/src/api/routingPins.ts\` for the global list DTO and short polling. Add \`client/src/api/subscriptionUsage.ts\` for the new read endpoint and a similarly bounded refresh interval.
3. Add \`routing\` to \`SettingsPage\`’s recognised query-selected tabs and mount a settings-owned \`RoutingSettingsTab\` composition component. Preserve \`keepMounted={false}\` so routing queries start only when the operator opens the tab.
4. Reuse \`useModelAvailability\`; do not duplicate its REST functions or mutation invalidation.

### S3 — Routing tab: availability, subscription observations, pins, and effective matrix

1. Build the three clearly headed sections described in D3–D5 with Mantine \`Paper\`, \`Table\`, \`Badge\`, \`Stack\`, loaders, and informative empty states consistent with the existing availability/chain panels.
2. Render the returned matrix axes, Any role first, then the server’s role order. Fetch role-effective rows in parallel and keep an individual row error from blanking the remaining grid.
3. Render candidate order and \`availableNow\`/\`unavailableReason\`; do not decide a fallback client-side. Apply pin banners from the global pins query, differentiating Required bypass from Preferred prepend and scoped-card pins.
4. Add D6’s propagation boundary and D7’s non-complexity boundary in ordinary operator language.
5. Add Manage routing settings links from the two existing Orchestrator summary panels. Keep them read-only; do not duplicate editor controls there.

### S4 — Matrix editor and mutation feedback

1. Add a focused cell-editor component using the existing model-level select styling, a local ordered candidate list, optional reason/expiry fields, validation feedback, and accessible add/remove/reorder controls.
2. Save the exact Human PUT wire payload and invalidate all affected effective/list queries. On Clear, give source-specific fallback/blocking confirmation and issue the matching DELETE.
3. Handle server 422/409 Problem Details in the form and return focus to the failed operation; show a success notification that repeats D6’s “new/queued, not running” effect boundary.
4. Do not invoke subscription monitoring, pin writes, availability mutation, or a dispatch as a side effect of a matrix save.

### S5 — Documentation and operating hand-off

1. Update \`docs/antiphon-api.md\` for the subscription-usage read contract, including its intentionally incomplete/provider-level semantics and no-secret guarantee.
2. Update the routing/settings documentation to point operators at the new tab, to explain pin precedence and the D6 propagation boundary, and to state that \`RolePolicy\` remains the non-complexity fallback.
3. Add the deployment note that a Human Required stage-wide Code pin bypasses Code cells; clearing or making it Preferred is an explicit operator decision, not an automatic migration.

## Verification

Add focused tests, with MSW handlers local to each test or in \`client/src/test/mocks/handlers.ts\` and \`renderWithProviders\`:

- \`RoutingSettingsTab.test.tsx\`: all three sections; Any/role effective cells; inheritance/config/unset text; unavailable candidate reason; Required versus Preferred pin wording; card-scoped pin scope; no-sample subscription usage; full observed sample; and D6/D7 boundaries.
- \`SettingsPage.test.tsx\`: \`?tab=routing\` selects the tab and lazy-mounts its queries without fetching it for other tabs.
- complexity-chain API/client tests: role list/effective query URLs, Human PUT body, matching DELETE path, and list/effective invalidation after save.
- \`ModelAvailabilityPanel.test.tsx\`: preserve available/held/reset/until-cleared and hold/clear behavior; add the settings integration case if a shared rendering seam is extracted.
- \`ComplexityChainPanel.test.tsx\` and \`OrchestratorPage.test.tsx\`: preserve the dashboard summaries and verify the Manage-routing link; update only the CARD-0332 label expectations required by its landed contract.
- New server \`SubscriptionUsageHttpTests\`: safe response projection, null/empty behavior, and no monitor/poll invocation. Keep \`SubscriptionUsageParserTests\`, \`SubscriptionUsageMonitorTests\`, and \`SubscriptionQuotaGateTests\` green as regression coverage.

Run the narrow server class for the new endpoint and the touched client tests first, then the client suite:

\`\`\`powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0333/ -- --treenode-filter "/*/*/SubscriptionUsageHttpTests/*"
pwsh -File scripts/test-client.ps1 RoutingSettingsTab.test
pwsh -File scripts/test-client.ps1 SettingsPage.test
pwsh -File scripts/test-client.ps1 ModelAvailabilityPanel.test
pwsh -File scripts/test-client.ps1 ComplexityChainPanel.test
pwsh -File scripts/test-client.ps1 OrchestratorPage.test
pwsh -File scripts/test-client.ps1
\`\`\`

CARD-0332’s own focused matrix contract tests must also pass after its dependency is landed; this UI card does not replace their server-side coverage. Delete \`bin-card0333*\` after the server run.

## Acceptance criteria

- The settings URL opens a global Routing tab that shows the current availability snapshot and a held alias’s reset or “until cleared.”
- It never displays invented model-level remaining quota. Subscription percentages/resets are visibly provider/profile-level observations with their timestamp, and absent monitoring says unknown.
- The matrix is driven by CARD-0332’s returned axes and effective-cell API, displays Any role fallback accurately, and saves/clears Human cells using its exact routes.
- A Required stage pin visibly says it bypasses the relevant role row; a Preferred pin visibly says it prepends rather than replaces; card pins are not misrepresented as global.
- The UI says precisely that new and eligible queued/blocked complexity tasks use the changed row while live sessions do not.
- Existing Orchestrator summaries remain useful read-only operational views with a path to the editor.
- CARD-0097 remains open as the separate non-complexity \`RolePolicy\` visibility/editing work.

## Risks and ordering

| Risk | Mitigation |
|---|---|
| CARD-0332 has not landed or its DTO differs | Do not start S2–S4 until its landed contract is present; re-read its actual additive DTO/routes after rebase. |
| A numerical usage label is read as live model capacity | Separate subscription observations from availability, include observed time/provider scope, and show unknown instead of manufacturing a value. |
| A static picker drifts from valid server pairs | Keep its three delegatable kinds in one list, use server validation/problem details as authority, and never use alias/profile catalogs as a writer source. |
| Operator thinks a Code cell is ignored | Render the live stage-wide Required Code pin beside the row and explain the exact Required/Preferred difference. |
| Clearing an inherited fallback blocks work | Source-specific clear confirmations and explicit \`Unset — dispatch blocks\` labels. |
| Settings change is expected to replace a running agent | D6 copy and save notification state the dispatch-boundary behavior; no session-control code is added. |

**Estimated implementation:** 2–2.5 days after CARD-0332 lands: S1 0.5 d, S2 0.5 d, S3–S4 1.0 d, S5/verification 0.5 d. The usage endpoint is deliberately read-only; broad proactive-quota coverage is not included in that estimate.
