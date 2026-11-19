# CARD-1744 S4 checkpoints

Slice S4 of [2026-11-19-card-1744-s4-s5-plan.md](2026-11-19-card-1744-s4-s5-plan.md).
CP-10 is the red witness (helper absent, so every case prints FAIL and exits 1).
CP-11 through CP-13 are the green rows. One build and one filter per row.
CP-14 belongs to S5 and is not part of this slice.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-10 | S4-red | tests/Antiphon.Tests -> bin-c1744g3r/ | g3-red | `/*/*/C1744G3PhoneHomeSupervisionTests/*` | W-9..W-14 | red: 6 failed, 0 passed | 6 | 15 |
| CP-11 | S4 | tests/Antiphon.Tests -> bin-c1744g3/ | g3-green | `/*/*/(C1744G3PhoneHomeSupervisionTests*)\|(RestartApphostLockHintTests*)\|(LinuxServerRestartGateTests*)/*` | W-9..W-14, restart-apphost ASCII and lock hint, AppHost files never name the Linux gate | 0 failed | 25 | 12 |
| CP-12 | S4 | CP-11 | g3-index | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | regenerated index matches the builder | 0 failed | 1 | 4 |
| CP-13 | S4 | tests/Antiphon.SessionRunner.Tests -> bin-c1744g3sr/ | g3-sr | `/*/*/PtyHostDeploymentGuardTests/*` | autostart still launches the built exe with `-BuildProjectDir` (T-21) | 0 failed | 3 | 10 |
