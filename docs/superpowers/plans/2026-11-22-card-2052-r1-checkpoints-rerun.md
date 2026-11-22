# CARD-2052 Review round 1 repair: CP-3 rerun

Rerun of CP-3 from `docs/superpowers/plans/2026-11-22-card-2052-r1-checkpoints.md` (run 20261122-225246-ac11,
red only on `TempRootSweepAnchoredDeleteTests.fifo_and_socket_inside_the_root_are_removed`). That fixture binds a
Unix socket under `Path.GetTempPath()` and returns -1 without calling bind when the path reaches 108 bytes
(sun_path). The checkpoint tool gives each dotnet child a private per-lease TMPDIR (CARD-1981, ProcessDriver
OwnsLeaseTemp), which makes that path too long; an explicit row TMPDIR is left as set. Neither the test nor the
sweep is touched by CARD-2052. Same filter, same floor, its own build; reruns=1.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-3 | all | tests/Antiphon.Tests -> bin-c2052-cp3/ | impacted-cleanup | `/*/*/(AnchoredPathDeleteWitnessTests*)\|(C1307ToolRemovalTests*)\|(CheckpointBuildBindingPathTests*)\|(CheckpointExecutorLogTests*)\|(CheckpointLargeStampTests*)\|(CheckpointOrFilterTests*)\|(CheckpointProcessIdentityTests*)\|(CheckpointSelectedOutputTests*)\|(CheckpointStatePublicationTests*)\|(CheckpointTaskOwnershipTests*)\|(EvidenceFolderTests*)\|(HostCleanupCheckpointAuthorityTests*)\|(ReapedProcStatIdentityTests*)\|(TempRootSweepAnchoredDeleteTests*)\|(UnsafeDeleteRecoveryTests*)\|(CheckpointReceiptSafetyTests*)\|(CheckpointShadowCustodyTests*)/*` | R-3 | >= 60 executed, 0 failed | 60 | 20 | true | TMPDIR=/tmp |
