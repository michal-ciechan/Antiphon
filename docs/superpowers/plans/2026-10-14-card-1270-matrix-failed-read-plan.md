# CARD-1270 matrix failed-read display

Task/landing owner: `7b9b0246-8618-43db-9dc1-63b956a5ff72`.
Base: `9cbe00bd52dd77367a34244c9cb9ea7d24539add`.
Round: Final. Branch: `feat/card-task-7b9b0246`.

## Scope

Client only. `ComplexityChainService.GetEffectiveAsync` reads five failable parts for a
role cell and appends `<part> read failed (<type>)` to `effective.withheldReason`:
`stage pin`, `chain`, `chain metadata`, `standing agent`, `availability`. The tab also
reads `/api/routing-pins` and `/api/role-policy` separately. RolePolicy itself is a
configuration projection, not a read; its "cannot launch" diagnostic is a known verdict.

Displayed cell fields: effective label, effective candidates, alert, stored-origin label,
stored candidates, provenance, reason, expiry, per-candidate availability, and the row's
stage-pin banner.

## Implementation

S1 commits failing table tests. S2:

- One segment parser over `withheldReason` (split on `; `, prefix `<part> read failed (`)
  replaces the substring checks, so free text mentioning a marker is not a failure.
- A failed chain read makes the effective route unknown for `rolePolicy` and `pin+chain`
  (a Preferred pin's tail is the unread row; on the default tier the server appended
  RolePolicy). A Required pin and a standing-agent pin do not walk the row and keep their
  definite label. A stage-pin failure stays unknown for every source (CARD-1258).
- A failed chain read shows `Provenance, reason and expiry unknown: <segment>`; a failed
  chain metadata read shows `Reason and expiry unknown: <segment>`; stored reason/expiry
  are not rendered in either case.
- A failed availability read renders `availability unknown: <reason>` (dimmed), not as an
  unavailable verdict.
- A failed standing-agent read renders the agent as `<id> (name unknown)`, not
  `(unavailable)`.
- docs/orchestration-loop.md gains the unknown-display rows and the non-default
  Preferred-pin row, each citing tests.

## Verification design

- V-1: `routingSettingsModel.test.ts`, `C1270 V-1 effective label %s` (13 rows) and
  `C1270 V-1 availability %s` (5 rows): literal labels, with segment-boundary,
  Required, metadata, missing-agent and held controls.
- V-2: `RoutingSettingsTab.test.tsx`, `C1270 V-2 $read read failure with a $strength pin on $tier`:
  8 reads (none, stage pin, chain, chain metadata, availability, standing agent,
  routing pins, role policy) x Preferred/Required x Medium (default)/Easy = 32 cases.
  Each asserts the literal label, every expected field text, absent definite texts,
  exact alert (or none), banner (or the pins failure text) and enabled Configure.
- R-1: full model, component and `src/api/complexityChains.test.tsx` files.
- R-2: client typecheck and production build.
- No server source changes, so no .NET class is affected; the brief excludes the
  whole Unit lane (it would exercise no changed code).

PC-1..PC-5 are pending for SourceLanding Mutation; see
`docs/superpowers/mutations/2026-10-14-card-1270-candidate.md`.


## Repair round (Review ce90839f D1-D4)

Code task `35080534-0f87-4490-bef3-ad25549454e3`, branch `feat/card-task-35080534`, continuing
from `d0f9c154b`; landing owner stays `7b9b0246`. S3 commits the failing rows, S4 the fix.

Surface x read enumeration for the Settings Routing tab (failed or loading read):

| Surface | Reads | Before | After S4 |
|---|---|---|---|
| Matrix cell fields | role-cell parts | unknown (CARD-1270 S2) | unchanged; stored-reason coexist rows added (D4) |
| Configure editor | chain, chain metadata (from the cell's `effective.withheldReason`) | `This cell is currently Unset`, default draft, Clear hidden; empty Reason/Expiry as if stored (D1) | see D1 below |
| Row stage-pin banners, card-pin count | `/api/routing-pins` | loading: absent; failed refetch: stale banners | row text `Stage and card pins loading.` / `Stage and card pins unknown (routing pins read failed: <reason>)`; stale pins not rendered |
| Card-specific pins list | `/api/routing-pins` | `No card-specific pins.` on loading and failure (D2) | `Card-specific pins loading.` / `Card-specific pins unknown (routing pins read failed: <reason>).` |
| Runner defaults history | `/api/runner-defaults/revisions` | `No revisions yet.` on loading and failure | `History loading.` / `History unknown (revisions read failed: <reason>).` |
| Runner defaults runner labels | `/api/session-runners` | stored runners labelled `missing from catalogue` on failure | `<id> · catalogue unknown` plus `Runner catalogue unknown (session runners read failed: <reason>).` (loading already shows the section loader) |
| Standing-agent label + failed chain read | chain | definite agent route by code, untested | V-1 and V-2 rows added |
| Availability, usage, role policy, matrix list, role-cell request | own reads | section loader or error text | unchanged (already unknown) |

D1 decision: the PUT is a whole-row replacement. `ComplexityChainService.UpsertAsync` stores a
null or blank reason as the default `<cell> chain` label and a null `notAfter` as no expiry, so
omitting them cannot preserve the stored values. The editor therefore requires an explicit
overwrite: on a failed chain read it shows `Current value unknown (<segment>)…`, starts with no
candidate (no default draft), shows Clear override, and keeps Save and Clear disabled until the
operator ticks `Overwrite the unread stored cell…`. On a failed chain metadata read it keeps the
read candidates, starts Reason and Expires empty with `Stored reason and expiry unknown
(<segment>)…`, and keeps Save disabled until `Overwrite the unread reason and expiry…` is ticked.
Disabling Save outright was rejected: a persistent read failure would leave no way to repair the cell.

Repair verification rows (all in the CP-1/CP-2 file set):

- V-1: `C1270 V-1 effective label required-standing-agent-with-chain-read-failure`,
  `…preferred-standing-agent-with-chain-read-failure` (coverage rows; the code was already definite).
- V-2 (`RoutingSettingsTab.test.tsx`): `C1270 V-2 $read read failure beside a stored reason on $tier`
  (chain, chain metadata x Medium, Easy); `C1270 V-2 standing agent pin with a chain read failure
  keeps the agent route with a $strength pin` (2); `C1270 V-2 editor after a $label read failure on
  $tier` (none, stage pin, availability, standing agent controls; chain and chain metadata, each also
  beside a stored reason; x Medium, Easy = 16); `C1270 V-2 routing pins %s` (failed, loading, stale
  after a failed refetch).
- V-2 (`RunnerDefaultsSection.test.tsx`): `C1270 V-2 runner defaults history read %s` (failed,
  loading, empty control); `C1270 V-2 runner defaults catalogue read %s` (failed, loaded control).
- R-1 adds `ComplexityChainCellEditor.test.tsx` and `RunnerDefaultsSection.test.tsx` to the file set.

## Repair round 3 (Review 036839b4 D-1..D-3)

Code task `dfc1166f-9815-466a-8264-30363b97e1f5`, branch `feat/card-task-dfc1166f`, continuing from
`18d3ae9a5`; landing owner stays `7b9b0246`. S5 commits the failing rows, S6 the fix.

Query states (React Query 5.90): `loading` = pending and fetching; `paused` = pending and not
fetching because the browser is offline (`isLoading` false, no data, no error); `failed` = error
without data; `refetch failed` = error with stale data; `fetching with stale data` = success and
fetching; `success empty`; `success`. Before S6 the tab keyed on `isLoading`, so `paused` fell
through to the success branch with no data.

| Surface | Read | loading | paused (before S6) | paused (after S6) | failed / refetch failed | fetching with stale data | success empty |
|---|---|---|---|---|---|---|---|
| Matrix section, Any role row | list | `Complexity chains loading.` | Unset Any row, Configure, default draft, PUT on reconnect | `Complexity chains unknown (read paused offline).`, no Any row, no Configure | `Could not load complexity chains.` | last list | Unset Any row |
| Role row cells | role effective | `<role> effective cells loading.` | Unset cells, Configure | `<role> effective cells unknown (read paused offline).`, no Configure | `Could not load <role> effective cells.` | last row | Unset cells |
| Configure editor (role cell) | role effective, after open | Save, Clear disabled with `Save and Clear are disabled until the cell's read loads: …` | enabled | disabled with that note | snapshot kept (R2 table) | snapshot kept | n/a |
| Configure editor (Any row) | list | editor not rendered (section shows the pending text) | opened on the synthesized Unset row | not rendered | not rendered | snapshot kept | Unset note (definite) |
| Card pins list, row banners and count | routing pins | `Card-specific pins loading.` | `No card-specific pins.` | `Card-specific pins unknown (routing pins read paused offline).` | `… unknown (routing pins read failed: …)` | last pins | `No card-specific pins.` |
| Availability section | availability | `Model availability loading.` | `no available aliases and no holds` | `Model availability unknown (read paused offline).` | `Could not load model availability.` | last snapshot | `no available aliases and no holds` |
| Usage section | usage | `Subscription usage loading.` | `Unknown — …` (USAGE_UNKNOWN) | `Subscription usage unknown (read paused offline).` | `Could not load subscription usage observations.` | last samples | USAGE_UNKNOWN |
| Role policy section | role policy | `Role policy loading.` | `Could not load role policy.` | `Role policy unknown (read paused offline).` | `Could not load role policy.` | last policy | empty table |
| Runner defaults form | runner defaults | `Runner defaults loading.` | `Could not load runner defaults.` | `Runner defaults unknown (read paused offline).` | `Could not load runner defaults. …` | last defaults | n/a (the read returns one object) |
| Runner defaults labels | session runners | `Runner catalogue loading.` | `<id> · missing from catalogue` | `Runner catalogue unknown (read paused offline).` | `<id> · catalogue unknown` + `Runner catalogue unknown (…)` | last catalogue | `<id> · missing from catalogue` |
| Runner defaults history, Restore | revisions | `History loading.` | `No revisions yet.` | `History unknown (read paused offline).` | `History unknown (revisions read failed: …).` | last history | `No revisions yet.` |

D-1 decision: one model helper `pendingReadLabel` keys on `isPending` (no data, no error) and names
the paused case; the sections of the tab and `routingPinsReadLabel` use it (V-3 table rows below). The matrix narrows on
`isPending`/`isError` before reading `list.data` and each row before reading its cells, so no
Any row is synthesized and no Configure is rendered without data. A role-cell editor observes its
row read and disables Save, Clear and Confirm clear while it is pending. Mutations keep React
Query's default network mode: an editor opened on read data and saved offline still queues
its PUT (ordinary stale edit, no concurrency token, as before).

D-2 decision: `clearOverrideCopy` takes the failed chain and chain metadata segments. A failed
chain read words the cell as unknown (own rule `if one exists`) and the fallback as conditional.
Reviewer disclosure 2 (Clear after a failed metadata read deletes the unread reason and expiry):
the confirmation states that they are unknown and deleted with the row. Requiring the overwrite
checkbox was rejected: it would change the round-2 assertion that Clear is enabled before the
checkbox, and Clear's effect does not depend on the unread values.

D-3 decision: the doc sentence is qualified; `Stored cell: unset` stays because the chain read
that established it succeeded.

Repair verification rows:

- V-3 (`RoutingSettingsTab.test.tsx`): `C1270 D-1 $read read $state` (9 reads x 7 states, less
  runner defaults x success empty = 62); `C1270 D-1 offline from the first render shows no value and
  sends no PUT after reconnect`; `C1270 D-1 editor whose cell read returns to pending blocks %s until
  the read loads` (Save, Clear override); `C1270 D-2 clear confirmation after a chain read failure on
  $tier` (2); `C1270 D-2 clear confirmation after a chain metadata read failure on %s names the unread
  reason and expiry` (2); `C1258 V-2 stage-pin failure on an Unset cell keeps Stored cell: unset %s` (3).
- V-3 (`routingSettingsModel.test.ts`): `C1270 D-1 pending read label $name` (5),
  `C1270 D-1 routing pins read label $name` (5), `C1270 D-2 clear copy …` (7).
- R-1 adds `RolePolicySection.test.tsx` and `SettingsPage.test.tsx` (both render changed sections).

### Cost

Red floor 3 minutes, green floor 6 minutes, queue time separate. Checkpoint tool is
bootstrapped once through build-slot; client dependencies installed through build-slot
and npm's esbuild hard link broken before qualification. Manifest `rowMinutes: 40`,
`--total-timeout 150m`, committed `--expected-source-sha`, no keep-outputs on Linux.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1, S3, S5 | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx src/features/settings/ComplexityChainCellEditor.test.tsx src/features/settings/RunnerDefaultsSection.test.tsx src/features/settings/RolePolicySection.test.tsx src/features/settings/SettingsPage.test.tsx` | V-1, V-2, V-3, R-1 | 7 files, nonzero count; guarded new assertions red before fix | n/a | 3 | true |
| CP-2 | S2, S4, S6 | n/a | client-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx src/features/settings/ComplexityChainCellEditor.test.tsx src/features/settings/RunnerDefaultsSection.test.tsx src/features/settings/RolePolicySection.test.tsx src/features/settings/SettingsPage.test.tsx` | V-1, V-2, V-3, R-1 | 7 files passed, at least 313 tests, 0 failed/skipped | n/a | 3 | true |
| CP-3 | S2, S4, S6 | n/a | client-build | `npm --prefix client run build` | R-2 | typecheck and production build exit 0 | n/a | 3 | true |
