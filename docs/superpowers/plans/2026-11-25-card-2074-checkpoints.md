# CARD-2074 checkpoints: completion-notes follow-ups

Slices: S1 commits the red witnesses (items 1 to 3) with the old behaviour still in place. S2 is the
fix: the native caller strips every inherited `CLAUDE*`/`ANTHROPIC*` variable itself (item 1), the
migration chain pins derive the tip (item 2), the per-agent incident cap never deletes a pending
runner slot release intent (item 3), and the one-note-per-settlement contract text (item 4).

Coverage:

- V-1 (item 1): `CallerWakeTests.C2074_Native_caller_strips_every_inherited_provider_variable`, and the
  explicit native caller `CallerWakeTests.Adopted_native_recipient_reads_its_report` (two cases) run
  under this host's own inherited Claude session variables.
- V-2 (item 2): `C1514HostLoadMigrationTests` and `C0824RefOnlySnapshotMigrationTests`,
  `C2074_Chain_pin_accepts_a_newer_migration` plus each class's existing chain test.
- V-3 (item 3): `C2074IncidentCapIntentTests.Null_agent_cap_never_deletes_a_pending_release_intent`,
  plus the existing prune contract `StandingBootAttentionTests.C1156_Prune_preserves_active_dedup_and_releases_resolved_history` (four cases).
- G-1: the six-class Antiphon.Tests guard subset (docs/testing-and-build.md, 53 results).

CP-1 is the red witness: four tests, each expected to fail at S1. CP-4 needs the installed Claude CLI.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c2074-red/ | red | `/*/*/(CallerWakeTests*)\|(C1514HostLoadMigrationTests*)\|(C0824RefOnlySnapshotMigrationTests*)\|(C2074IncidentCapIntentTests*)/(C2074_*)\|(Null_agent_cap*)` | V-1, V-2, V-3 red | 4 executed, 4 failed (red witness) | 4 | 20 | false | n/a |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c2074/ | green | `/*/*/(CallerWakeTests*)\|(C1514HostLoadMigrationTests*)\|(C0824RefOnlySnapshotMigrationTests*)\|(C2074IncidentCapIntentTests*)\|(StandingBootAttentionTests*)/(C2074_*)\|(Null_agent_cap*)\|(ChainIsLinear*)\|(Migration_chain_is_linear*)\|(C1156_Prune_preserves*)` | V-1, V-2, V-3 | 10 executed, 0 failed or skipped | 10 | 20 | false | n/a |
| CP-3 | S2 | CP-2 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestLaneCategoryGuardTests*)/*` | G-1 | at least 53 executed, 0 failed | 53 | 5 | false | n/a |
| CP-4 | S2 | CP-2 | native | `/*/*/CallerWakeTests/Adopted_native_recipient_reads_its_report` | V-1 native | 2 executed, 0 failed or skipped | 2 | 8 | true | ANTIPHON_REAL_CLI_STUB_TESTS=1 |
