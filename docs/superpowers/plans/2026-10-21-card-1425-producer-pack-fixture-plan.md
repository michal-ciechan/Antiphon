# CARD-1425 Producer-pack fixture for the nine CARD-1281 correction tests

Nine CARD-1281 correction tests (11 cases) skip on every clone because `CorrectionWorld.CreateAsync`
reads the untracked CARD-1281 producer pack from `<git common dir>/antiphon/code-evidence/04228bff`
and pins it by SHA-256. CARD-1415 then excluded them from the rc profile on every platform. This
plan replaces the pack with a deterministic in-test fixture, pins the fixture's own digest, makes
the nine run unconditionally, and removes the two CARD-1415 all-platform exclusion rows.

Lane: default runner (server2, Linux; `GET /api/runner-defaults` `globalRunnerId=server2`). Omit
`-Runner`. The Windows acceptance row (CP-5) runs on a Windows-platform dispatch (`-Platform Windows`),
not in the Linux Code task.

## Ground truth (card assumption vs code)

| # | Card / brief assumes | Code actually does | Consequence |
|---|---|---|---|
| G-1 | Tests need "the pack" | `CorrectionWorld.CreateAsync` (`tests/Antiphon.Tests/Application/AdvisoryChangePolicyTests.Correction.cs:207-291`) copies every inventory file into `w.Root/input`, then the real `CheckpointReceiptValidator` (`server/Infrastructure/Verification/CheckpointReceiptValidator.cs`) parses it. Only these pack facts are consumed: inventory `task/sourceCommit/sourceWorktree/cleanCheckpointRun/files[]`; `report.json` (schema 2, commit, branch, worktree, rows CP-14..16, manifestPath/Hash, source, verdict, exitCode, cleanupRefusals); per-row TRX `UnitTestResult outcome="Passed"` count == executed; a `.../bin-c1281-foundation/checkpoint-build-source.json` stamp; `manifest.resolved.yaml`. | The 141-file pack is not needed; a ~8-file pack with exactly these facts satisfies the real validator. |
| G-2 | Only the pack is missing on a clone | The selection is read with `git ls-tree`/`git show <sourceCommit>:docs/superpowers/plans/2026-10-15-card-1281-cheap-slices-checkpoints.md` in `CheckpointProducer.SelectionRepositoryPath` (= `w.Repository` = the real repo). The pack's source commit `e39bccc6744f4a94724efd891d4acd360cee8941` is reachable only from `origin/feat/card-task-04228bff` / `origin/feat/card-task-7bf8c86b`, **not** from `origin/master`. | A tracked copy of the real pack would still fail on a master-only/nightly clone (`selection_not_committed`). The fixture must own its source commit. |
| G-3 | — | `scripts/validate-checkpoint-receipt.ps1` runs via `pwsh` on the retained `report.json` with `-ExpectedSourceSha` and `-Rows CP-14,CP-15,CP-16`; with `ExpectedRepeat=1` it never opens TRX paths; it checks row `line` tokens (`commit= source= sourceState=clean buildSource=verified dirty=0 executed= passed= failed= skipped=`), `source` v1 (`captureStatus known`, 64-hex equal start/end fingerprints, `state clean`, `buildSource verified`), `verdict GREEN`, `cleanupRefusals []`. | Generated `report.json` must carry these exactly; original paths may be synthetic strings. |
| G-4 | — | `sourceWorktree` / `RepositoryPath` / `originalPath` are compared only as strings (service lines 55,120,126,139; validator `OriginalAsync`, `report.worktree`). Land (`C1303_R12` via `AgentTaskLandService.RequestAsync`) never touches that path on disk (the live pack's `/work/worktrees/task-04228bff` did not exist where it ran). | A fixed synthetic POSIX path is safe on Windows and keeps the inventory digest platform-independent. |
| G-5 | — | `WiringFactory` (`PipelineSpeedIntegrationTests.Http.cs:300-330`) and `ConfigureCorrectionServices` pass `correction.Repository` both as validator-script root and as `SelectionRepositoryPath`. | Must split into `Repository` (real repo, validator script) and `SelectionRepository` (fixture git repo). |
| G-6 | — | `CorrectionWorld.DisposeAsync` uses `Directory.Delete(Root, true)`; a git repo under Root has read-only loose objects on Windows. | Use `GitFixtureCleanup.Delete(Root)`. |
| G-7 | Rows live in an "rc exclusion" list | They are `profiles.rc.exclusions` rows with `owner: CARD-1415`, no `platform` (= all platforms) at `tests/test-execution-policy.json` ~L570-590; two Windows-only CARD-1415 rows stay. `policyHash` is `Get-NightlyPolicyHash` (`scripts/lib/nightly-policy.ps1`) over the canonical payload minus `policyHash`; `Read-NightlyExecutionPolicy` throws `stale policy hash` on mismatch. | Remove two rows, recompute hash. |
| G-8 | Removing rows is the only policy-side change | `scripts/test-rc-platform.ps1` `Test-C1415_SkipExclusions` asserts exactly 4 CARD-1415 rows, the pack set excluded on both platforms, and `skipped -eq 11` in its control; `tests/Antiphon.Tests/Scripts/ReleaseGatePlatformTests.cs:25-30` expects 9 checks and names `c1415-four-method-scoped-rows`, `c1415-linux-excludes-exactly`. `docs/release-gates.md` (CARD-1415 paragraph) documents the rows. | All three must change in the same slice as the policy, or CP-4 goes red. |
| G-9 | — | Defect arms in `CreateAsync` edit `report.json` (`schemaVersion`, `commit`, CP-15 `state`), delete CP-15 `run.trx` or the `bin-c1281-foundation/checkpoint-build-source.json` entry, and `ReceiptVariantsAsync` tampers `manifest.resolved.yaml`. Expected outcomes: `missing-provenance` → all three rows missing (one shared build stamp); `failed-row`/`missing-trx` → `["CP-15"]`. | Fixture keeps one shared build stamp `bin-c1281-foundation`, row ids CP-14..16, CP-15 TRX path `.antiphon/checkpoints/<run>/rows/CP-15/run.trx`, and a `manifest.resolved.yaml` entry. |

## Decisions

- **D-1 Deterministic in-test generation, not tracked fixture files.** New helper
  `tests/Antiphon.Tests/TestHelpers/C1281ProducerPackFixture.cs` writes the pack and a one-commit
  selection git repo under the world's temp root. Rejected: (a) committing the real 141-file pack
  — its source commit is unreachable from master (G-2) and it bloats the tree; (b) tracked static
  fixture files — Windows `core.autocrlf` checkouts change bytes and break every SHA-256, and the
  report's commit SHA must match a commit the test owns anyway; (c) a tracked git bundle — opaque
  binary, still needs import plumbing.
- **D-2 The fixture owns its source commit.** `git init --object-format=sha1 --template=` in
  `<Root>/selection-repo`, write the selection plan with explicit `\n` and UTF-8 without BOM,
  commit with fixed `GIT_AUTHOR_NAME/EMAIL/DATE` and `GIT_COMMITTER_*`
  (`2026-10-15T11:52:46Z`), `-c core.autocrlf=false -c commit.gpgsign=false -c core.hooksPath=<empty dir>`
  and `GIT_CONFIG_GLOBAL`/`GIT_CONFIG_NOSYSTEM` neutralised, so the commit SHA is the same on every
  host. Use `ScratchGitRepo.GitInAsync(path, env, ...)`. The committed plan is a minimal copy of the
  real one: the exact section heading `## Shared bindings B-1/B-2/B-3 (TestDesign, 2026-10-15, task 598a38d5)`,
  a `### Checkpoints` header row and the three real CP-14/15/16 rows (11 cells, filters and Min
  3/2/2 copied verbatim).
- **D-3 Synthetic, fixed coordinates.** `sourceWorktree = "/fixture/c1425/producer-worktree"`,
  producer `task = "04228bff-e4a8-4ecd-bdbb-9315c2e41395"`, branch `feat/card-task-04228bff`, run
  `20261015-115246-09c4`, fingerprint `SHA-256("c1425-fixture-build")`. Keeping the original
  task/branch/run identities leaves every assertion string unchanged; the path is not under
  `/work` so it can never alias a real worktree (G-4).
- **D-4 Pin the fixture, not the live pack.** `C1281ProducerPackFixture.InventoryDigest` is a
  constant; `CreateAsync` asserts the generated inventory digest equals it
  (`"pinned-fixture-inventory"`, replacing `"authentic-producer-inventory"` and the
  `5afb8488...` literal). Because the inventory embeds `sourceCommit` and every file digest, the pin
  also covers git determinism. Code fills the constant from the first Linux green run and CP-5
  proves the same value on Windows. A deliberate fixture change updates the constant in the same
  commit.
- **D-5 Positive stays unedited; negatives edit only the copy.** Generation writes into
  `w.ProducerRoot`; the existing defect arms keep editing that copy, unchanged.
- **D-6 No skip path remains.** Remove `CommonDirectoryAsync`, the `04228bff` path and
  `Skip.Test`. Nothing in the nine methods may depend on untracked state.
- **D-7 Policy rows go in the same slice as the harness/doc updates** (G-8) and `policyHash` is
  recomputed with the shipped function, never hand-edited:
  `pwsh -NoProfile -Command ". ./scripts/lib/nightly-policy.ps1; Get-NightlyPolicyHash -Object (Get-Content -Raw tests/test-execution-policy.json | ConvertFrom-Json); exit $LASTEXITCODE"`
  (if `nightly-policy.ps1` needs `nightly-tests-impl.ps1` dot-sourced first, source that instead).
- **D-8 Windows proof is a separate Windows-platform dispatch** (CP-5), because the Code task runs
  on the Linux default lane. Review may run CP-5 on `-Platform Windows`; it must not be skipped.

## Slices

| Slice | Files | What |
|---|---|---|
| S1 red witness | `tests/Antiphon.Tests/Application/AdvisoryChangePolicyTests.Correction.cs` | Replace `Skip.Test(...)` with `File.Exists(producerIndex).ShouldBeTrue("CARD-1425: no producer pack and no fixture")`. Commit; run RW-1 on a pack-less clone (server2 has none: `/work/repos/antiphon/.git/antiphon/code-evidence` absent). Proves the nine now execute and fail for the missing pack, rather than skip. |
| S2 fixture | new `tests/Antiphon.Tests/TestHelpers/C1281ProducerPackFixture.cs`; `AdvisoryChangePolicyTests.Correction.cs`; `PipelineSpeedIntegrationTests.Http.cs` | `C1281ProducerPackFixture.WriteAsync(root) -> (ProducerRoot, SelectionRepository, InventoryJson)` per D-1..D-5: selection repo commit, `report.json`, `report.md`, `manifest.resolved.yaml`, three TRX files (3/2/2 `Passed` results), `tests/Antiphon.Tests/bin-c1281-foundation/checkpoint-build-source.json`, inventory with each file's synthetic `originalPath`, relative path, byte count, SHA-256. `CorrectionWorld`: add `SelectionRepository`, drop the git-common-dir read, assert D-4 pin, keep defect arms, use `SelectionRepository` for `git show` and as `SelectionRepositoryPath` in `ConfigureCorrectionServices` and `WiringFactory`; `DisposeAsync` uses `GitFixtureCleanup.Delete`. Remove the `CARD-1415` skip comments in `PipelineSpeedIntegrationTests.Http.cs` (keep `C1281_WiredRoundtripDelivery`). Add `C1425_ProducerPackFixtureIsDeterministic` (two roots → identical inventory bytes, digest == pin, and selection commit SHA identical). |
| S3 policy | `tests/test-execution-policy.json`; `scripts/test-rc-platform.ps1`; `tests/Antiphon.Tests/Scripts/ReleaseGatePlatformTests.cs`; `docs/release-gates.md` | Delete the two all-platform CARD-1415 rows (AdvisoryChangePolicyTests, PipelineSpeedIntegrationTests); recompute `policyHash` (D-7). `Test-C1415_SkipExclusions`: expect 2 CARD-1415 rows (`c1415-two-method-scoped-rows`), Linux excludes nothing, Windows excludes only the two Windows-only identities, control `skipped -eq 2`; add `c1425-pack-methods-required` asserting the 11 pack cases are in the launched set on both platforms. Update `ReleaseGatePlatformTests.C1415_SkipExclusions` expected labels/count accordingly. Rewrite the `docs/release-gates.md` CARD-1415 paragraph: two Windows-only rows remain; the nine pack methods are required everywhere since CARD-1425. |

Commit and push after each slice. S1 is committed before S2 (failing test before fix).

## Verification design

- **V-1** (S2) The nine methods, 11 cases, pass with no producer pack in the git common dir; the
  positive goes through the real `CheckpointReceiptValidator` and `validate-checkpoint-receipt.ps1`.
  Labels kept: `valid-correction-route`, `actual-landing-consumer`, `missing-execution-route-reached`,
  `restored-valid-control`, `actual-validator-source-argument`, `retained-content-digest`,
  `tampered-blob-refused`, `real-program-correction-route`.
- **V-2** (S2) `C1425_ProducerPackFixtureIsDeterministic`: two generations → byte-identical
  inventory, digest equals `C1281ProducerPackFixture.InventoryDigest`, identical selection commit
  SHA. Label `pinned-fixture-inventory`.
- **V-3** (S2) Negative arms still discriminate: `C1281_IncompleteNeverClean` defects
  (`failed-row`, `missing-trx`, `missing-provenance`, `wrong-receipt`, `bad-number`) and the three
  `C1303_R12` defects keep their exact codes/missing rows (G-9).
- **V-4** (S3) Shipped policy loads (hash valid), carries no all-platform CARD-1415 row, and the
  rc census launches the 11 pack cases on Linux and Windows.
- **R-1** Regression: the pack-free CARD-1281 siblings still pass (`C1281_WiredRoundtripDelivery`,
  `C1281_B1Schema`, the CARD-1415 CP-1 advisory pack-free set).

### Red witness (S1 commit, not a checkpoint row)

RW-1 at the S1 commit, through the build slot:
`pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1425-rw1 -- dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c1425/ -- --treenode-filter "/*/*/AdvisoryChangePolicyTests/(C1303_R12_LandRefusesUnavailableCorrectedEvidence*)|(C1281_ReceiptCorrection*)|(C1281_IncompleteNeverClean*)"`.
Expect executed=5 failed=5 skipped=0, each failure the `CARD-1425: no producer pack and no fixture`
Shouldly assertion (not a build or DB error). Zero executed or a non-assertion failure is not a red
witness: stop and report. Record the counts and commit SHA in the Code report. Delete `bin-c1425`
afterwards.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S3 | `tests/Antiphon.Tests -> bin-c1425/` | advisory-pack-cases | `/*/*/AdvisoryChangePolicyTests/(C1303_R12_LandRefusesUnavailableCorrectedEvidence*)\|(C1281_ReceiptCorrection*)\|(C1281_IncompleteNeverClean*)` | V-1, V-3 | 5 passed (3 R12 cases + 2), 0 failed/skipped | 5 | 6 | n/a |
| CP-2 | S3 | CP-1 | pipeline-pack-methods | `/*/*/PipelineSpeedIntegrationTests/(C1281_B1Authorization*)\|(C1281_B1ReceiptImport*)\|(C1281_B1CorrectionTransaction*)\|(C1281_WiredRoundtrip*)\|(C1281_B1Schema*)` | V-1, V-3, R-1 | 6 passed (4 pack methods + `C1281_WiredRoundtripDelivery` matched by the `C1281_WiredRoundtrip*` prefix + `C1281_B1Schema`), 0 failed/skipped | 6 | 12 | n/a |
| CP-3 | S3 | CP-1 | fixture-pin | `/*/*/AdvisoryChangePolicyTests/(C1425_ProducerPackFixtureIsDeterministic*)\|(C1281_AdvisoryDoesNotRerun*)` | V-2, R-1 | 2 passed, 0 failed/skipped | 2 | 2 | n/a |
| CP-4 | S3 | CP-1 | release-gate-platform | `/*/*/ReleaseGatePlatformTests/(C1415_SkipExclusions*)` | V-4 | 1 passed, 0 failed/skipped | 1 | 3 | n/a |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1425w/` | windows-pack-cases | `/*/*/(AdvisoryChangePolicyTests*)\|(PipelineSpeedIntegrationTests*)/(C1303_R12_LandRefusesUnavailableCorrectedEvidence*)\|(C1281_ReceiptCorrection*)\|(C1281_IncompleteNeverClean*)\|(C1281_B1Authorization*)\|(C1281_B1ReceiptImport*)\|(C1281_B1CorrectionTransaction*)\|(C1281_WiredRoundtrip*)\|(C1425_ProducerPackFixtureIsDeterministic*)` | V-1, V-2 | 11 passed (9 pack cases + `C1281_WiredRoundtripDelivery` + fixture pin; digest equals the Linux pin), 0 failed/skipped | 11 | 15 | n/a |

Lanes (moved out of the Environment column, which takes only `NAME=value` entries): CP-1..CP-4 run on
server2 (default lane); CP-5 runs on a Windows-platform dispatch (`-Platform Windows`; Review or a
dedicated check, not the Linux Code task). Code amendment (CARD-1425 Code task 12653d93): CP-5 Expect/Min
corrected from 13 to 11; the producer pack is nine cases (5 advisory + 4 pipeline methods), the earlier
"11" was the old two-platform skip-control count. The CP-5 filter is unchanged; on Linux it ran 11, all passed.
CP-1..CP-3 After moved from S2 to S3 so CP-4 can reuse the CP-1 build (the tool requires one After group per
reused build); they run at the S3 tip, and S3 does not touch the code they exercise.

CP-4 depends on `pwsh` and the C1323 harness; per memory `release-gate-harness-linux-pitfalls`,
`C1323_PlatformExecution` is an inherited Linux red and is deliberately not selected.

Before landing: `git fetch origin master` and `git merge-tree --write-tree origin/master HEAD`
with no conflicts; `scripts/check-evidence-diff.ps1` over base..HEAD.

## Risks

- **Git determinism across hosts** (D-2): a global config the env does not neutralise
  (e.g. `init.defaultObjectFormat=sha256`) would change the SHA; `--object-format=sha1` and
  `GIT_CONFIG_GLOBAL` to an empty file cover it. CP-5 is the proof; a Windows digest mismatch is a
  red row, not a reason to make the pin platform-specific.
- **`pwsh` availability**: the real validator spawns `pwsh`; both lanes have it today, and the
  nine tests already required it when the pack was present.
- **Policy hash drift**: if another card lands a policy change first, recompute after rebasing at
  land time; the land rebase owns that, not this branch.
