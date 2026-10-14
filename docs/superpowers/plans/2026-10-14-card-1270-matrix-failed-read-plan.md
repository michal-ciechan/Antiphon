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

### Cost

Red floor 3 minutes, green floor 6 minutes, queue time separate. Checkpoint tool is
bootstrapped once through build-slot; client dependencies installed through build-slot
and npm's esbuild hard link broken before qualification. Manifest `rowMinutes: 40`,
`--total-timeout 150m`, committed `--expected-source-sha`, no keep-outputs on Linux.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx` | V-1, V-2, R-1 | 3 files, nonzero count; guarded new assertions red before fix | n/a | 3 | true |
| CP-2 | S2 | n/a | client-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx` | V-1, V-2, R-1 | 3 files, nonzero count, 0 failed/skipped | n/a | 3 | true |
| CP-3 | S2 | n/a | client-build | `npm --prefix client run build` | R-2 | typecheck and production build exit 0 | n/a | 3 | true |
