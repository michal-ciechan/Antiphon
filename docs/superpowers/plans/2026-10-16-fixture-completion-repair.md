# Fixture completion repair

Code and landing owner: `13dee490` on `feat/card-task-13dee490`.
Base: `6ca859d006b0a3cac9a88dc6bd551c37e98b2217`; target RC `rc/2026-10-15`.
Diagnosis: Debug `5676e3a4`, commit `43a3f8700ec14dc83a6d8aef2d948ef3e821376e`.
The caller skipped Plan; this records the executable verification selection.

S1 adds failing fixture-model regressions. S2 repairs payload preview, S3 repairs
donor ordering, S4 retains safe diagnostics, S5 checks every Inventory probe.
Commit and push each slice. Run CP-1 red at S1 and green after S2-S5. CP-2 is
the named adjacent smoke, including the existing native UID/npm regression.
All model resources are private temporary directories. No live rollout or
Docker daemon, drain, deploy, restart or server2 mutation is authorized.

## Verification design

Task-specific narrow filters/CARD-1287 override generic whole-Unit boilerplate.
The new full integration class runs; the broad RemoteScriptContractTests class
is limited to affected named methods. No full assembly is needed.

- V-1: `CacheFixtureCompletionTests.Preview_reads_recovery_in_its_payload_filesystem`
  host/volume: actual preview, actual du, missing/link/escaping/noncanonical paths,
  partial failed size read, untouched payload and no success receipt on failure.
- V-2: `CacheFixtureCompletionTests.Fixture_prune_consumes_real_preview_and_exercises_controls`
  actual F-8 creates recovery, previews, exercises PC-21..25 and applies prune.
- V-3: `CacheFixtureCompletionTests.Finalization_releases_donor_before_project_absence_audit`
  same-project/different-service donor survives Compose down; finalizer removes
  acquired donor before positive-absence audit. Foreign/replaced/unknown stay protected.
- V-4: `CacheFixtureCompletionTests.Fixture_prune_retains_inner_refusal`
  preview/apply inner diagnosis and exit survive outer failure in durable receipts.
- V-5: `CacheFixtureCompletionTests.Running_inventory_refuses_each_failed_probe`
  UID-1654 command body runs; dotnet/npm/four du/stat failures each refuse;
  absent optional cache directories remain represented as absent.
- R-1: existing native `CacheSeedNpmOwnershipTests` (3 cases), volume seed/rerun,
  recovery controls, donor admission and root-mode normalization.
- R-2: existing Compose positive-absence scenarios and legacy replacement/unknown
  identity checks retain fail-closed cleanup and exact deletion ownership.
- R-3: manual Bash/PowerShell parsing, static Seed-and-later inspection, fresh TRX
  and source/build receipts, full base..HEAD evidence policy check.

PCs are pending for SourceLanding Mutation; existing production Fixture controls
executed by V-2 are ordinary behavior checks, not deliberate mutation proof.
Candidate: `docs/superpowers/mutations/2026-10-16-fixture-completion-repair.md`.

Windows checkpoint import sets only build.properties.UseAppHost=false, as the
RC checkpoint tool otherwise builds DLL-only and attempts an executable launch.
Pass committed HEAD as --expected-source-sha. Bootstrap the tool through
build-slot.ps1 (explicit unlisted build); all row builds/hosts take broker slots.
Cost: two CP-1 runs (one expected red, one fixed), one CP-2; ~15 minutes plus
authoring and host slot wait. Target only newly exposed red rows after repairs.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S5 | `tests/Antiphon.Tests -> bin-fixture-completion/` | fixture-completion | `/*/*/CacheFixtureCompletionTests/*` | V-1, V-2, V-3, V-4, V-5 | 10 cases, no failed/skipped after repair; expected red at S1 | 10 | 5 |
| CP-2 | S5 | `tests/Antiphon.Tests -> bin-fixture-adjacent/` | fixture-adjacent | `/*/*/(CacheSeedNpmOwnershipTests)\|(RemoteScriptContractTests)/(Volume_seed*)\|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume*)\|(C1192_Recovery_controls_validate_volume_bytes_modes_and_manifest*)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads*)\|(C1195_Seed_import_chmod_restores_volume_root*)\|(C1207_Compose_teardown_requires_recorded_positive_absence*)\|(C1208_Legacy_container_replacement_is_untouched*)\|(C1208_Unknown_identity_retains_residue*)` | R-1, R-2 | 14 cases, no failed/skipped | 14 | 5 |
