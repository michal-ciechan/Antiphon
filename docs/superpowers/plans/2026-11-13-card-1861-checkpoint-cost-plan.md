# CARD-1861 checkpoint-cost repair

S1 adds a failing graph-cost witness: the regeneration driver must not build the
Antiphon.Tests graph. S2 source-links the unchanged builder and shared taxonomy,
with their direct packages, into the driver. No cached result or index is trusted.
The same witness then runs the shipped regeneration script twice against a fresh
tracked fixture, checking reverse edges, both OS labels, and changed/unchanged bytes.

## Verification design

V-1: zero project references prevents the full transitive test/server/helper build.
V-2: the real native-default driver retains the same builder and taxonomy semantics.
The operator's minimal-testing scope is one red and one narrow green; no whole
DerivedImpactIndexTests replay or historical CARD-1860 selection is repeated.
Mutation checks are waived by the operator (2026-11-12).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | n/a | driver-cost-red | `pwsh -NoProfile -File scripts/test-derived-impact-driver.ps1 -EvidenceRoot .antiphon/c1861-red` | V-1 | expected red: driver graph assertion | n/a | 1 |
| CP-0 | S2 | n/a | driver-build | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false -maxcpucount:1 --nologo` | V-1,V-2 | exit 0, no Antiphon.Tests build | n/a | 1 |
| CP-2 | S2 | n/a | driver-cost-green | `pwsh -NoProfile -File scripts/test-derived-impact-driver.ps1 -EvidenceRoot .antiphon/c1861-green` | V-1,V-2 | DRIVER WITNESS passed=3 failed=0 | n/a | 1 |
| CP-3 | S3 | n/a | matched-cap-benchmark | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -t:Rebuild -p:UseAppHost=false -maxcpucount:1 --nologo` | V-1,V-2 | exit 0, same cap as historical driver, no Antiphon.Tests build | n/a | 1 |

Run with the checkpoint tool, --after S1 or S2, --serial and the exact committed
--expected-source-sha. CP-0 uses native output because the real script -NoBuild
consumes that output. Command rows are outside isolated-build confinement; the
driver graph assertion and real fixture witness are the explicit acceptance.
S3 corrects the command-row cap omitted in the original S2 table: command rows do
not inject maxcpucount. Preserve S2's leased but uncapped build as historical
diagnostic evidence. Run only CP-3 at S3: Rebuild forces compilation in the same
native output for a matched-cap timing comparison, without repeating CP-2's green
script proof. S3 changes only this plan; production and witness bytes stay at S2.
Only unlisted build is the leased native-default checkpoint-tool bootstrap needed
in this fresh worktree. No change to slot semantics, custody, deadlines or filters.

## Performance budget

Historical input: server2 Linux run 20261112-235906-610d, source
73292703cc2ccbcab9f11d0d1616ae2bef33bea5, 2202.354 seconds wall. CP-0 alone compiled
for 367.68 seconds. This is one comparable run, not a measured run p95.
Provisional new budget: driver build under 60 seconds, witness under 30 seconds,
admission separate; collect one red and one green only. Tool importer timeouts are
unchanged legacy defaults, not newly p95-derived budgets (CARD-1404). No fixture
database/container/web host; one tiny git fixture retained under ignored evidence.
Raw reports contain phase/admission/build time. Investigation records unavailable
discovery and child-IO phases as unknown. One later ordinary Review can supply a
second host-load sample; do not repeat a full selection for timing alone.
