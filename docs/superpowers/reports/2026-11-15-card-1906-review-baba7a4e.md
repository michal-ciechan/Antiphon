# CARD-1906 Final Review round 1

Review task `baba7a4e`. Subject and landing owner remain Code task `28eecdf5-2364-4811-934c-f9fbdc10ef55`.
Reviewed tip `34d62eb94c0397075910c5c75b68cfb10c902bae` on `feat/card-task-28eecdf5`.
Range `d100f8fa2c679a5c74b6b235087308776a8f5d50..34d62eb94c0397075910c5c75b68cfb10c902bae`.
Product behavior was last changed at `e2d3d6643c52d9a1873b462fd4a058434656c945`. The two commits after that are plan notes only.
Composed against `origin/master` `360baf90bea12f3a1f802cdafff4361fb9f259be` (fetched 2026-11-15 during this review). Merge 242 is already on that master.

Verdict: no product blocker. Default stays OFF. Exact `1` is the only ON value. Invalid saved lines refuse before compose, checkout, recycle, and env rewrite. The key reaches a real PTY session and a `build-slot.ps1` child. Advisories below do not hold the land. Positive controls and native Windows placement stay pending.

## What this change does

`docker-compose.server2-runner.yml` sets `ANTIPHON_LINUX_COMPILER_POLICY` only on `session-runner`, with the named anchor `&RunnerLinuxCompilerPolicyDefault` and interpolation `${ANTIPHON_LINUX_COMPILER_POLICY:-0}`. `build-slots` and `state-init` do not receive the key. The temp overlay does not clear it.

`scripts/c590-remote.sh` `runner_linux_compiler_policy` accepts only a whole line `ANTIPHON_LINUX_COMPILER_POLICY=0` or `=1`. The named shell default is `RUNNER_LINUX_COMPILER_POLICY_DEFAULT=0`. A missing file or key prints `0`. Any other shape, including duplicates, exits 2 before the print. `write_result` then exits. The file is not sourced. `compose_host` reads `stack.env`. `compose_temp` reads `stack.temp.env` and passes `--env-file` for that file. `case_deploy_parent` and `case_deploy_temp_runner` read the value before checkout, recycle, image build, and the env rewrite, then write the same value back.

`LinuxSharedCompilerPolicy.Enabled` is true only when the process is not Windows and the setting is exactly `1`. Unset, null, and `0` are OFF. The PowerShell adapter returns immediately on Linux unless the key is present and exactly `1`. It does not remove the key. `scripts/measure-linux-shared-compiler.ps1` removes the key; that script is not the deploy or build-slot path.

`PtyHostLauncher` starts the pty host without touching `ProcessStartInfo.Environment`, so the host inherits the runner process environment. Porta merges an explicit session map and leaves parent variables in place when the map does not mention them.

## Review receipts

All rows are this review's runs at `34d62eb94c0397075910c5c75b68cfb10c902bae`, `sourceState=clean`, `dirty=0`. Author receipts were not reused.

| Row | Result | Receipt |
|---|---|---|
| CP-1 `RunnerLinuxCompilerPolicyTests` | 35 passed, 0 failed, 0 skipped, build verified | `.antiphon/checkpoints/CP-1-20261115-163813-101a` |
| CP-2 deploy contracts | 120 passed, 0 failed | `.antiphon/checkpoints/CP-2-20261115-165836-1860` |
| CP-3 guard union | 164 passed, 0 failed, 1 Windows skip, host wall 459.6s | `.antiphon/checkpoints/CP-3-20261115-164740-b380` |
| CP-4 remote ordering | 7 passed, 0 failed | `.antiphon/checkpoints/CP-4-20261115-165859-d8a2` |
| CP-5 `c346-workspace-slot-compose.mjs` | exit 0, build n/a, 9.0s | `.antiphon/checkpoints/20261115-170225-9f77` |
| CP-6 `ProcessSpawnLimitTests` | 57 passed, 0 failed | `.antiphon/checkpoints/CP-6-20261115-170014-9fef` |
| CP-7 census | 2 passed, 0 failed | `.antiphon/checkpoints/CP-7-20261115-165624-b5d2` |
| CP-8 SessionRunner limiter | 2 passed, 0 failed, build verified | `.antiphon/checkpoints/CP-8-20261115-170244-522d` |
| CP-9 Pty limiter | 2 passed, 0 failed, build verified | `.antiphon/checkpoints/CP-9-20261115-170438-1fb2` |
| CP-10 nightly admission | 5 passed, 0 failed | `.antiphon/checkpoints/CP-10-20261115-165727-4a3e` |

CP-3 skip is `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact` (Windows placement owed). The derived-impact driver was built first (`UseAppHost=false`, default output) so `regenerate_script_reports_changed_then_unchanged` could run with `-NoBuild`. `committed_index_matches_the_builder` passed on this branch.

CP-1 covers the refusal matrix (32 shapes, 4 boundaries each), the 16 phase matrices, missing-file default `0`, baseline equivalence after deleting the new key, and real PTY plus `build-slot.ps1` inheritance for saved `0` and `1`. The override map passed to the session does not contain the key. The child probe observed the parent value.

Load at the guard row was about 1–4 on 24 cores. CP-3 execution stayed under 10 minutes. No pure timeout occurred.

## Questions

1. Default OFF. Compose interpolation uses `:-0`. The reader default is `0`. `Enabled` is false for unset, null, and `0`. CP-1 rendered absent and empty as `0`. CP-5 asserts the rendered session-runner value is `0`, then deletes the key before the pre-change model compare. The key is not added to other compose services.

2. Passthrough. No launch allowlist drops `ANTIPHON_LINUX_COMPILER_POLICY`. CP-1's inheritance cases passed for both `0` and `1`. The runner-to-pty-host hop is inheritance: `PtyHostLauncher` does not replace the environment. That is code inspection plus the executed PTY merge, not a separate container deploy.

3. Strict reader. CP-1 refused empty, whitespace, CRLF, casing, `export`, quotes, inline comments, duplicates including indented duplicates, and values other than `0`/`1` (`true`, `2`, and the rest of the 32 shapes) with `RunnerLinuxCompilerPolicySettingInvalid` before the fixture's side-effect stubs. `drain-old` and `drain-temp` do not rewrite `stack.env`. `redeploy-old` reaches `case_deploy_parent`, which validates first. Temp and host wrappers each read their own file. CARD-1950 flips `&RunnerLinuxCompilerPolicyDefault` and `RUNNER_LINUX_COMPILER_POLICY_DEFAULT` only. Tests pin the current default `0`.

4. Unset behavior. CP-2 (120), CP-4 (7), and CP-5 stayed green. No secret file is sourced. Refusal stdout is the diagnosis name. This review did not print `stack.env`.

5. Rollback text in `docs/docker-stack.md` matches the code for a saved `0` plus the named recreate: future builds only, existing compilers are not killed, and editing the file does not change a running container. Not executed against a live host. This review did not activate the trial.

6. Pins and index. Branch pins recompute to the values stored on the branch: policyHash `16917233483bf9f13534d8d5f86a1bdbdfabe4a4981153127287537c25bb5179`, PinnedInputDigest `0a6eaab72768e0e3f65c1a0cf15ef47c4471f913397389ca6900b9538b44fe00`. Those are correct for this branch's tree, not for current master. Master at `360baf90` currently has policyHash `a9c07d3cc95a1a2ba29d53680a66a6fd6a420598851f5c9719306227ceabf1c5` and PinnedInputDigest `8399dbfc36a28effaf5ceabbb0b54513ca22617fc626b78fe3c68cd3fc76af7c`. Composed onto that master (master policy text plus the CARD-1906 exclusion row, hash rewritten, digest recomputed): policyHash `c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`, PinnedInputDigest `2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`. LegacyExclusionDigest stays `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a` on both sides. The new row's reason starts with `Linux-only by construction:`, so it stays outside the frozen legacy payload.

`git merge-tree --write-tree origin/master HEAD` exits 1. Tree `9f1f13069a08d067929ea2fcedc0460a4ea86603`. Conflicts: `scripts/test-live-lane-run.ps1`, `tests/test-execution-policy.json`. The index auto-merged and is semantically stale: driver-first regeneration on a scratch checkout of that tree changed it, and the second run was unchanged. Regenerated blob `925cf2e97aec18459a86005177c37f4f7e06db3f`. Branch index blob `1689262f65ac83dd311489db247a9aaf07db2ac3` matches the builder for the branch (CP-3). Do not hand-merge the index or copy the branch hashes onto the rebase. Recompute both pins and regenerate the index on the rebase. Scratch commit `aa812c1b746db2ffc5eafff39a544357bb3a6fe1` is only a detached worktree at `/tmp/c1906-compose-wt`.

## Other checks

`scripts/check-evidence-diff.ps1` over the range: `EVIDENCE result commits=10 entries=0 violations=0`, exit 0.

`scripts/impacted-classes.ps1 -MergeSubset` on the range: `migrations=False`. No migration files in the diff. Downgrade selector count 0.

`scripts/check-os-witnesses.ps1` exit 0. Inventory covers the ten non-document paths. The only OS-token lines are three pre-existing `OperatingSystem.IsWindows()` uses in `RemoteScriptContractTests.cs` (same lines at base and head), classified non-behavior. CARD-1906 adds no OS-selected production branch. Compose, the example env, and the two JSON files are unrecognized extensions and are still inventoried. Windows-native execution stays pending.

## Advisory

`scripts/deploy-server2.ps1` `deploy-temp`, before `Invoke-HostCase deploy-temp-runner`, can POST `/drain/clear`, POST `/drain`, and run `runner-cache-seed` when the temp runner needs a new container. An invalid `stack.temp.env` is refused only inside `case_deploy_temp_runner`, which is after those calls. The shell still refuses before checkout, compose, and the env rewrite, and `write_result` exits, so a bad value is not written into the container. The same order already exists for the workspace-slots reader this card was required to mirror. `redeploy-old` does not drain or seed before `deploy-parent`. This is not a fail-open of the policy. It is an advisory for a later card: validate the temp file in the PowerShell phase before drain and cache seed. It does not hold this land.

## Owed

Positive controls in `docs/superpowers/mutations/2026-11-15-card-1906-compiler-policy-plumbing.md` stay pending. Mutation was waived for this dispatch. Native Windows placement of the one CP-3 skip stays pending. Live activation of `ANTIPHON_LINUX_COMPILER_POLICY=1` is not this change.

## Sample rows

Eight heaviest rollout and retirement partitions from the commissioned Debug manifest. Each row is this review's own checkpoint receipt at `34d62eb94c0397075910c5c75b68cfb10c902bae`, `build=reused` from `tests/Antiphon.Tests/bin-c1906-d/`, `buildSource=verified`, `dirty=0`, `sourceState=clean`, slot wait 0. Totals: 32 passed, 0 failed, 0 skipped. None were red, so none were re-run at bare origin/master.

| Row | Result | Host wall | Receipt |
|---|---|---|---|
| S-D36P04 `VolumeRetirementReceiptTests/Journal_intents_and_partial_resume_are_exact` | 1 passed | 442.7s | `.antiphon/checkpoints/S-D36P04-20261115-171144-6980` |
| S-I34 `VolumeRetirementApplyGateTests/*` | 13 passed | 345.6s | `.antiphon/checkpoints/S-I34-20261115-171912-247f` |
| S-I37 `VolumeRetirementWrapperTests/*` | 6 passed | 335.4s | `.antiphon/checkpoints/S-I37-20261115-172505-6048` |
| S-D36P01 `VolumeRetirementReceiptTests/Manual_attestation_is_exact_and_cannot_waive_findings` | 1 passed | 333.8s | `.antiphon/checkpoints/S-D36P01-20261115-173047-d0dd` |
| S-D25P14 `RollingVolumeRecycleScriptTests` C1361/C1371 (3 filters) | 3 passed | 319.8s | `.antiphon/checkpoints/S-D25P14-20261115-173627-d494` |
| S-D21P01 `RetiredTempContainerHostTests` C1105/C1609 (3 filters) | 3 passed | 306.0s | `.antiphon/checkpoints/S-D21P01-20261115-174153-d296` |
| S-D35P04 `VolumeRetirementContentTests` (2 filters) | 2 passed | 298.6s | `.antiphon/checkpoints/S-D35P04-20261115-174705-b5b7` |
| S-D25P06 `RollingVolumeRecycleScriptTests` C1008 (3 filters) | 3 passed | 267.7s | `.antiphon/checkpoints/S-D25P06-20261115-175210-2d05` |
