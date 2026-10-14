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
### Cost

Red floor 3 minutes, green floor 6 minutes, queue time separate. Checkpoint tool is
bootstrapped once through build-slot; client dependencies installed through build-slot
and npm's esbuild hard link broken before qualification. Manifest `rowMinutes: 40`,
`--total-timeout 150m`, committed `--expected-source-sha`, no keep-outputs on Linux.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1, S3 | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx src/features/settings/ComplexityChainCellEditor.test.tsx src/features/settings/RunnerDefaultsSection.test.tsx` | V-1, V-2, R-1 | 5 files, nonzero count; guarded new assertions red before fix | n/a | 3 | true |
| CP-2 | S2, S4 | n/a | client-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx src/features/settings/ComplexityChainCellEditor.test.tsx src/features/settings/RunnerDefaultsSection.test.tsx` | V-1, V-2, R-1 | 5 files passed, at least 223 tests, 0 failed/skipped | n/a | 3 | true |
| CP-3 | S2, S4 | n/a | client-build | `npm --prefix client run build` | R-2 | typecheck and production build exit 0 | n/a | 3 | true |
