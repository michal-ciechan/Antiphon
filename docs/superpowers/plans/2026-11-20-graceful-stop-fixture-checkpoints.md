# Graceful-stop fixture closure

Debug repair for rc run e7f92f66 scripts/all. Red witnesses are committed before the copier
is wired into `Install-C716Scripts`. Direct builds use the worktree's native output
(no `-o` / `OutputPath`). The driver project is built before `DerivedImpactIndexTests`
because `regenerate_script_reports_changed_then_unchanged` runs
`scripts/regenerate-derived-impact-index.ps1 -NoBuild`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-red | n/a | missing-lib-red | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T13` | T-13 | T-13 FAIL on lib/windows-phone-home-env.ps1, exit 1 | n/a | 15 |
| CP-2 | S1-red | n/a | synthetic-red | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 -Case T14` | T-14 | T-14 FAIL, leaf and nested absent, exit 1 | n/a | 2 |
| CP-3 | S2 | n/a | graceful-stop | `pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1` | T-1 through T-14 | 0 failed, exit 0 | n/a | 8 |
| CP-4 | S2 | `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -> bin/` | driver | `dotnet build scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj -p:UseAppHost=false` | driver for -NoBuild | build exit 0 | n/a | 8 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin/` | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | committed index | >= 27 executed, 0 failed | 27 | 15 |
