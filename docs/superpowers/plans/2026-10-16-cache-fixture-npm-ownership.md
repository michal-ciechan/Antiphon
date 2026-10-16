# Cache Fixture npm writer ownership

Code owner/landing task: `bad91a49-3daa-4109-90f6-edf967608264`.
Base: RC `b7265a05aa8773f08dbf457b93c7561e7874822d`; target `rc/2026-10-15`.
Diagnosis: `70bc61bdf36f543d30c690e8df62d1109f52a7c5`,
`docs/investigations/2026-10-16-server2-fixture-permission.md`.

The volume-backed c849_seed verifier creates npm logs as root and asks the
UID-1654 writer to remove them. Run npm through the existing writer, preserving
checked verification and cleanup and the existing isolated host-backend command.
No live rollout, drain, restart, container or server2 volume work is authorized.

S1 commits native ownership regression before the fix; CP-1 must be red on the
unmodified production seed. S2 changes only verifier dispatch, then reruns CP-1
and runs CP-2. S3 records receipts and evidence without changing executable code.

## Verification design

The task-specific narrow-only/CARD-1287 instruction overrides the generic Final
Unit-lane boilerplate. The full new affected integration class is selected;
RemoteScriptContractTests is a broad unrelated script collection, so only the
named adjacent methods below run. No full assembly or Unit lane is authorized.

- V-1: `CacheSeedNpmOwnershipTests.Volume_seed_verifies_as_writer_and_removes_npm_logs`:
  native Linux tmp storage, real npm, real UID 1654, actual production seed,
  helper acquire/exec, filesystem dispatch and failure cleanup. Root-created
  control log cannot be deleted by UID 1654. Fixed seed observes 1654-owned npm
  logs, removes npm-cache, accepts seed, and leaves no root-owned payload.
- V-2: `Volume_seed_failure_cleans_stage_and_never_accepts(verify)`: real npm
  runs and creates logs, then injected verifier exit 42 must refuse, remove
  stage as writer and leave no accepted marker or root leftovers.
- V-3: the same method `(cleanup)`: injected first npm-cache removal exit 43
  must refuse, remove the stage through failure cleanup, and never accept.
- R-1: existing `C1192_Real_cache_cases_keep_the_host_payload_backend` and
  `C1192_Fixture_seed_and_rerun_keep_payloads_in_volume` preserve backend routing.
- R-2: existing `C849_Seed_refuses_invalid_donors_and_partial_payloads` and
  `C1195_Seed_import_chmod_restores_volume_root` preserve rejection and root modes.
- R-3: manually inspect host verifier argv/checks unchanged; parse shell source;
  validate clean SHA-bound receipts and inspect each fresh TRX identity/count.

The native harness models Docker transport and unrelated donor/import/smoke
checks, but executes npm and ownership/deletion with kernel UID semantics.
Windows uses local WSL root; Linux requires root (non-root runs explicitly skip
and cannot qualify this checkpoint). Every write is in a unique
native /tmp directory removed by EXIT; no actual Docker or SSH command runs.
This is not full live Fixture qualification; release activation owns that later.
Mutation candidate: `docs/superpowers/mutations/2026-10-16-cache-fixture-npm-ownership.md`.
All PCs remain pending for SourceLanding Mutation.

Windows runner compatibility: import this table with the checkpoint tool into
`.antiphon/npm-ownership.yaml`, set only `build.properties.UseAppHost` to `false`,
and run that manifest with the committed `--expected-source-sha`. This RC's tool
implicitly disables apphosts during its confined build but otherwise launches
the Windows test executable; an explicit property aligns build and execution.
The imported rows, filters, counts and deadlines must remain unchanged. Retain
the failed no-TRX attempt as infrastructure evidence, not defect reproduction.

### Cost

Ordinary floor: 7 minutes plus authoring. One expected-red CP-1 run precedes
the fix; no repeated proof after green. Checkpoint tool bootstrap is an explicit
unlisted build through build-slot.ps1 because the tool must exist to run rows.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-npm-ownership/` | native-npm-ownership | `/*/*/CacheSeedNpmOwnershipTests/*` | V-1, V-2, V-3 | all 3 cases, 0 failed/skipped after S2; expected red at S1 | 3 | 5 |
| CP-2 | S2 | CP-1 | adjacent-cache-contracts | `/*/*/RemoteScriptContractTests/(C1192_Real_cache_cases_keep_the_host_payload_backend)\|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume)\|(C849_Seed_refuses_invalid_donors_and_partial_payloads)\|(C1195_Seed_import_chmod_restores_volume_root)` | R-1, R-2, R-3 | all 4 methods, 0 failed/skipped | 4 | 2 |
