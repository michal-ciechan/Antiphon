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
| CP-5 | final | n/a | census-final | `pwsh -NoProfile -File scripts/test-selector-census.ps1` | freshness, full-census differential, cost | 9 script groups pass | n/a | 1 |
| CP-6 | final | `tests/Antiphon.Tests -> bin-c1862-final/` | replay-final | `/*/*/DerivedImpactIndexTests/*` | all original cases after parsing repair | 27 passed | 27 | 6 |

Run --after baseline, red, green or final with --serial and --expected-source-sha.
The unlisted builds are the leased native-output checkpoint-tool bootstrap in
this fresh worktree and the clean S1-tip native regeneration prerequisite in
the explicitly requested scratch checkout. Neither changes build-slot semantics. CP-0 deliberately uses native output consumed by regeneration -NoBuild.
Baseline and green full class run once each; only failures justify a targeted
rerun. Add exact S1 guard union in a scratch checkout after measuring this repair;
record source composition, counts, admission and the unchanged S1 budget.

## Performance budget

Historical server2 Linux sample: 27 cases sum 517.720s; CP-4 wall 518.150s.
Target >=200s saved in the full identical class. Not a p95 or a guarantee.
One baseline, the retained preliminary repair, and one final after sample after
further executable optimization, followed by ordinary Review and next
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

## Measured first repair and final refinement

At 674bf6c76861676bae2eab08c285ea2b387c4a98, CP-3 and CP-4 pass
(8 script groups; all 27 original cases). Class time is 426.143s versus
519.334s baseline: only 93.191s saved, below the 200s target. Census spans
drop 247.790s to 67.480s, while non-census script costs grow under higher load.
Preserve that green behavior receipt and unmet performance result. The final
slice replaces repeated generic New-Object dispatch with identical direct
constructors, and applies the unchanged line-bound declaration regex only to
lines containing its required class literal. The script differentially checks
every current test source and an explicit CRLF/shape corpus against the original
full-text regex, including class ownership, with an extra blindness assertion.
CP-5/6 rerun only because executable parsing/scanning code changed; they retain
the original full class, deadlines, source proof and host gate. No full CARD-1860
selection is run. Nested file-read/declaration/derive and index-hydration spans
are recorded in the final process records.
