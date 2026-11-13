# CARD-1862 selector replay cost

Keep all 27 DerivedImpactIndexTests cases, historical comparisons, replay ranges,
assertions, ProcessSpawnLimit and deadlines. First record the unchanged algorithm
with opt-in stderr timing; then move only the per-invocation census to C# through
Add-Type. No disk or cross-invocation cache, selected paths or classes survives.
Keep the same regexes, bin/obj exclusions, case handling and project assignment.

Fresh-input witnesses cover changed/new/removed declarations, malformed tables,
duplicate classes in different projects, and Windows/Linux index labels. A cost
witness must fail at the measured old census and pass on the new census. Mutation
checks are waived by the operator. No whole CARD-1860 rerun is authorized.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-0 | baseline | n/a | driver-prerequisite | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:1 --nologo` | real regeneration prerequisite | exit 0 | n/a | 6 |
| CP-1 | baseline | `tests/Antiphon.Tests -> bin-c1862-base/` | replay-baseline | `/*/*/DerivedImpactIndexTests/*` | identical full roster, measured baseline | 27 passed | 27 | 10 |
| CP-2 | red | n/a | census-red | `pwsh -NoProfile -File scripts/test-selector-census.ps1` | freshness and cost witnesses | expected cost assertion red | n/a | 1 |
| CP-3 | green | n/a | census-green | `pwsh -NoProfile -File scripts/test-selector-census.ps1` | freshness and cost witnesses | all assertion groups pass | n/a | 1 |
| CP-4 | green | `tests/Antiphon.Tests -> bin-c1862-after/` | replay-after | `/*/*/DerivedImpactIndexTests/*` | all original replay cases and assertions | 27 passed | 27 | 6 |

Run --after baseline, red or green with --serial and --expected-source-sha.
Only unlisted build: leased native-output checkpoint tool bootstrap in this fresh
worktree. CP-0 deliberately uses native output consumed by regeneration -NoBuild.
Baseline and green full class run once each; only failures justify a targeted
rerun. Add exact S1 guard union in a scratch checkout after measuring this repair;
record source composition, counts, admission and the unchanged S1 budget.

## Performance budget

Historical server2 Linux sample: 27 cases sum 517.720s; CP-4 wall 518.150s.
Target >=200s saved in the full identical class. Not a p95 or a guarantee.
One current baseline and one after sample, followed by ordinary Review and next
three naturally required runs, are the bounded collection plan. No timeout is
widened; importer defaults are legacy, not measured R5 budgets (CARD-1404).
Record host/load/SDK and source in checkpoint receipts; opt-in
ANTIPHON_SELECTOR_TIMING=1 records table, census, JSON input/output and git spans.
Outer process elapsed minus script time is startup/exit/transport remainder,
not a claim of pure startup. Table includes JSON input (nested); no DB/container,
web host, migrations or shared service setup. Native driver compilation is a
prerequisite, outside replay time. Full class fixture setup is preserved; each
selector must read fresh files. Raw per-test durations and process records remain
in TRX. Unmeasured IO/setup/teardown subdivisions stay unknown. No cache or
build-slot semantics change.
