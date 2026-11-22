# CARD-2052 Review round 1 repair: CP-1 receipt run

CP-1 of `docs/superpowers/plans/2026-11-22-card-2052-r1-checkpoints.md` alone, so its receipt validates by
itself: run 20261122-225246-ac11 was green on CP-1 but its report is refused as a whole (`report_failed`) for
the environmental CP-3 red rerun in `2026-11-22-card-2052-r1-checkpoints-rerun.md`. The four OutputCleanupTests
Windows-only skips are rostered in `WindowsUnitSkipRoster.WindowsOnly`. Same filter and floor, its own output.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c2052-named/ | named-eight | `/*/*/(OutputCleanupTests*)\|(CheckpointOwnedOutputTests*)\|(HostCleanupCheckpointExtractionTests*)\|(CheckpointToolCopyCleanupTests*)\|(PreflightCommandLineBudgetTests*)\|(SafeDeleteProcessBoundaryTests*)\|(HostCleanupCheckpointCustodyTests*)\|(HostCleanupCheckpointCompatibilityTests*)/*` | V-1,V-2,V-3,V-4,R-1 | 154 pass; 4 rostered OutputCleanupTests Windows skips | 154 | 15 | true |
