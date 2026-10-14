# CARD-1258 matrix unknown cells

Task/landing owner: `01f4a4f0-6ba6-4753-afc4-4f21063e74ec`.
Base: `5665ddb1a3ba730f3b858acc109e65b5d7304d6c`.
Round: Final. Branch: `feat/card-task-01f4a4f0`.

## Implementation

S1 commits failing tests before any fix. S2 makes one shared routing-unknown
predicate recognize a diagnostic segment beginning `stage pin read failed (`
for every effective source, before agent/pin/origin verdicts. Unknown cells keep
their alert and Configure action, withhold effective and stored candidate lists,
and withhold stored origin labels. Existing chain-read and availability semantics
remain independently covered. The Any-role configuration row has no effective
stage pin; `any` here means a named role inheriting that row.

S2 also changes `ComplexityEffective.Compose`: a missing chain is passed as null
to the shared composer only for the default tier. An explicit non-default
complexity walk passes the empty list, so a Preferred pin cannot append the
RolePolicy fallback. Fixing the projection preserves pin order and avoids client
guesswork about candidate origins. Dispatch composition itself is unchanged.

One existing model expectation deliberately changes from Own rule to unknown.
The old component role diagnostic row becomes a metadata-failure control; the
new source-by-tier table replaces its stage-pin case with stronger assertions.

## Verification design

- V-1: `routingSettingsModel.test.ts`, `C1258 V-1 stage-pin failure $source/$tier`:
  seven sources x Hard/Medium/Easy = 21 literal unknown labels.
- V-2: `RoutingSettingsTab.test.tsx`, `C1258 V-2 stage-pin failure suppresses route promises $source/$tier`:
  the same 21 variants assert unknown, exact alert, no origin/target/block verdict,
  neither stored nor effective candidate, and an enabled Configure button.
- V-3: `ComplexityEffectiveComposeTests.A_preferred_pin_on_an_empty_cell_appends_policy_only_for_the_default_tier`:
  three expanded results assert exact ordered candidates, with RolePolicy only on Medium.
- V-4: `ComplexityEffectiveComposeTests.A_non_default_preferred_pin_does_not_report_an_unused_policy_as_unlaunchable`:
  an unusable, unused policy must not create a diagnostic or candidate.
- V-5: `ComplexityEffectiveHttpTests.A_preferred_pin_on_empty_tiers_exposes_policy_only_on_the_default_tier`:
  a real persisted Preferred pin and GET role view assert all three tier lists,
  exact source/default flags, pin ID, empty stored rows and null diagnostics.
- R-1: full client model, component and complexity API files preserve successful
  reads, diagnostic segment boundaries, Required pins, metadata/availability
  failures, standing agents, editor behaviour and old-server handling.
- R-2: full affected `ComplexityEffectiveComposeTests`, `ComplexityEffectiveHttpTests`
  and adjacent `ComplexityRoutingComposeTests` preserve Required and multi-slot pins,
  policy withholding, deduplication and existing rows. HTTP tests own an isolated
  test database/factory with its established production-runner guard.
- R-3: full `Antiphon.Tests` Unit lane, required by the Code-stage Final contract.
  This supersedes the inbox's conflicting no-Unit sentence. It is a category
  selection, not a full integration assembly. No unbounded integration classes.
- R-4: client typecheck and production build.
- M-1: inspect final diff and confirm both candidate paths use the same predicate;
  inspect the shared composer null-versus-empty rule and the HTTP wire assertion.
  No live routing changes or real-browser acceptance is required for this repair.

PC-1 (seven source x three tier variants), PC-2 (effective/stored candidate paths),
PC-3 (default-only fallback) and PC-4 (unused invalid policy) remain pending for
SourceLanding Mutation. See the candidate document; Code does not run mutants.

### Cost

Ordinary green floor: 23 minutes; red-first floor: 11 minutes; authoring: 20 minutes.
Queue time is separate. Bootstrap the checkpoint tool once through build-slot
(necessary out-of-table driver), and install client dependencies through build-slot.
Break npm's esbuild hard link before source qualification. Import this table, set
manifest `rowMinutes: 40`, run serial with `--row-timeout 40m --total-timeout 150m`
and committed `--expected-source-sha`. No keep-outputs on Linux. Red and green use
the same exact selections in separate committed groups, without deliberate mutants.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx` | V-1, V-2, R-1 | 3 files, nonzero count; guarded new assertions red before fix | n/a | 3 | true |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1258-red/` | projection-red | `/*/*/(ComplexityEffectiveComposeTests)\|(ComplexityEffectiveHttpTests)\|(ComplexityRoutingComposeTests)/*` | V-3, V-4, V-5, R-2 | all three full classes, new guarded assertions red before fix | 17 | 8 | true |
| CP-3 | S2 | n/a | client-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/routingSettingsModel.test.ts src/features/settings/RoutingSettingsTab.test.tsx src/api/complexityChains.test.tsx` | V-1, V-2, R-1 | 3 files, nonzero count, 0 failed/skipped | n/a | 3 | true |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1258-green/` | projection-green | `/*/*/(ComplexityEffectiveComposeTests)\|(ComplexityEffectiveHttpTests)\|(ComplexityRoutingComposeTests)/*` | V-3, V-4, V-5, R-2 | all three full classes, 0 failed/skipped | 17 | 8 | true |
| CP-5 | S2 | CP-4 | unit | `/*/*/*/*[Category=Unit]` | R-3 | nonzero full Unit count, failures and platform skips reported separately | 1000 | 9 | true |
| CP-6 | S2 | n/a | client-build | `npm --prefix client run build` | R-4 | typecheck and production build exit 0 | n/a | 3 | true |
