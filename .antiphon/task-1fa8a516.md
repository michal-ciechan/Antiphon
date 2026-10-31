# CARD-1609 round 13 — apply gate and repaired rows

Outcome: the default-off apply gate is in code, and CP-8, CP-9, CP-16, and CP-17 are green at one SHA. Production volume deletes stay off unless the operator sets a token bound to that retirement's operation id. Verified source: `aa331d0135c4aeb0be4b03137c4d4c0d54daddff` on `feat/card-task-1fa8a516`.

## Checkpoint receipts

Both receipts validate. `dotnet exec tools/Antiphon.Checkpoints/bin-c1609-r13-tool/Antiphon.Checkpoints.dll validate` printed `CHECKPOINT SOURCE VALID` and exited 0. The tool directory was removed after that validate; the evidence below was kept.

CP-8, run `20261031-134413-9ec4`, `--rows CP-8 --row-timeout 27m --total-timeout 30m`, verdict GREEN, exit 0, wall 1632s, execution 807s, admission 825s, cleanedOutputs true, cleanupRefusals 0:

```
CHECKPOINT CP-8 commit=aa331d0135c4aeb0be4b03137c4d4c0d54daddff build=ok filter=/*/Antiphon.Tests.Scripts/RollingVolumeRecycleScriptTests/(C1008_Same_sha_and_partial_retries_are_safe*)|(C1008_Option_manifest_is_strict*)|(C1008_Wrapper_option_manifest_is_strict*)|(C1008_Present_or_unknown_temp_keeps_null_refusal*)|(C1008_Busy_routed_and_land_in_flight_refuse*)|(C1008_Preview_helper_removal_is_owned*)|(C1008_Wrapper_retired_absent_null_is_accepted*)|(C1008_Retired_absent_null_is_accepted*)|(C1008_Host_proof_admits_only_an_authenticated_private_origin*)|(C1008_Host_proof_file_omits_credentials*)|(C1008_Documentation_and_transport_pins_match*) executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-1fa8a516/.antiphon/checkpoints/20261031-134413-9ec4/rows/CP-8/run.trx slot=granted waited=420s dirty=0 source=aa331d0135c4aeb0be4b03137c4d4c0d54daddff sourceState=clean buildSource=verified
```

Validate: `--evidence .antiphon/checkpoints/20261031-134413-9ec4/report.json --expected-source-sha aa331d0135c4aeb0be4b03137c4d4c0d54daddff --rows CP-8` → `CHECKPOINT SOURCE VALID source=aa331d0135c4aeb0be4b03137c4d4c0d54daddff rows=1`.

CP-9, CP-16, CP-17, run `20261031-141231-82ff`, same SHA, `--rows CP-9,CP-16,CP-17 --row-timeout 27m --total-timeout 45m`, verdict GREEN, exit 0, wall 2857s, execution 1657s, admission 1200s, cleanedOutputs true, cleanupRefusals 0. `--parallel 1` was not passed. Width stayed at the default of 2. CP-16 started after CP-9 because the build slot admitted one row at a time.

```
CHECKPOINT CP-9 commit=aa331d0135c4aeb0be4b03137c4d4c0d54daddff build=ok filter=/*/Antiphon.Tests.Scripts/RetiredTempContainerHostTests/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-1fa8a516/.antiphon/checkpoints/20261031-141231-82ff/rows/CP-9/run.trx slot=granted waited=345s dirty=0 source=aa331d0135c4aeb0be4b03137c4d4c0d54daddff sourceState=clean buildSource=verified
```

```
CHECKPOINT CP-16 commit=aa331d0135c4aeb0be4b03137c4d4c0d54daddff build=reused filter=/*/Antiphon.Tests.Scripts/RemoteScriptContractTests/(C0995_Deploy_temp_checks_host_absence_before_checkout*)|(C849_Deploy_temp_names_saved_import_when_seed_is_missing*)|(C849_Deploy_prepares_and_verifies_before_acceptance*)|(C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo*)|(C849_Docker_volume_paths_have_no_bare_host_reads*)|(C849_Seed_publishes_complete_payloads_before_its_marker*)|(Temp_deploy_uses_its_own_project_and_the_inspected_grok_store*)|(Nested_lane_never_uses_sudo_or_python*)|(C1067_Nested_lane_sudo_guard_is_sensitive*)|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(C849_Saved_donor_archive_is_checked_and_imported_without_a_container*)|(C849_Deploy_ordering_contract_is_pinned*)|(C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads*)|(C912_Cold_exception_never_weakens_maintenance_or_donor_gates*)|(C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers*) executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-1fa8a516/.antiphon/checkpoints/20261031-141231-82ff/rows/CP-16/run.trx slot=granted waited=165s dirty=0 source=aa331d0135c4aeb0be4b03137c4d4c0d54daddff sourceState=clean buildSource=verified
```

```
CHECKPOINT CP-17 commit=aa331d0135c4aeb0be4b03137c4d4c0d54daddff build=reused filter=/*/Antiphon.Tests.Scripts/VolumeRetirementApplyGateTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-1fa8a516/.antiphon/checkpoints/20261031-141231-82ff/rows/CP-17/run.trx slot=granted waited=15s dirty=0 source=aa331d0135c4aeb0be4b03137c4d4c0d54daddff sourceState=clean buildSource=verified
```

Validate: `--evidence .antiphon/checkpoints/20261031-141231-82ff/report.json --expected-source-sha aa331d0135c4aeb0be4b03137c4d4c0d54daddff --rows CP-9,CP-16,CP-17` → `CHECKPOINT SOURCE VALID source=aa331d0135c4aeb0be4b03137c4d4c0d54daddff rows=3`.

Counts at that SHA: CP-8 11/11/0/0, CP-9 22/22/0/0, CP-16 23/23/0/0, CP-17 4/4/0/0. CP-8 tests wall 465.6s. CP-9 tests wall 550.9s. CP-16 tests wall 65.5s. CP-17 tests wall 497.5s. Shared build `bin-c1609-final` (CP-3). The green runs deleted that output.

The checkpoint tool takes one `--row-timeout` for every row. The requested 6m and 10m caps are below the derived floor `max(15, 3*EstimatedMinutes)`, so the run used 27m for every row. The first combined attempt at `509f208917288eb27e6fda779e3329645e0ac5cc` used `--total-timeout 30m` and exited 5 (`execution-budget-exceeded`) after CP-9 and CP-16 had passed and CP-8 had failed; CP-17 was not executed on that attempt. The validating runs are the two above. Greens recorded inside the earlier red runs at `509f20891` (CP-9, CP-16) and `ba2f264bd` (CP-17) do not validate: those runs kept `bin-c1609-final` and the receipt records `cleanup_refused`.

CP-13 was not executed. `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs` changed in `509f20891`. CP-17's Linux source test pins the same gate-9 sentences and passed. The closed roster CP-3, CP-4, CP-5, CP-6, CP-7, CP-10, and CP-15 was not re-run. `c590-remote.sh` changed, so the `cf3c0359` greens cannot be carried. Historical roster red at `cf3c0359`: CP-8 10 executed / 8 passed / 2 failed; CP-9 22/21/1; CP-10 3/2/1; CP-16 23/18/5. CP-8's floor is now 11.

## Apply gate

`c1609_apply_gate` in `scripts/c590-remote.sh` refuses unless `C1008_OPERATION` matches `^c1008[0-9a-f]{32}$` and `ANTIPHON_VOLUME_RETIREMENT_APPLY` equals exactly `enabled-${C1008_OPERATION}`. Unset, `1`, `true`, `enabled`, and a previous operation's token all refuse. A fresh `retire-temp` mints `'c1008' + [guid]` in `scripts/deploy-server2.ps1`, so a leftover export cannot authorize the next retirement. Resume and `-RetirementProof` reuse that id and need the same token.

Refusal is `write_result false 'apply-gate-off' 2`. That returns before `docker volume rm --` and does not call `c1008_refuse` or `c1609_phase_end`. On a fresh apply the call sits before `C1609_APPLY_ACTIVE=1` and before the delete loop. On resume, `C1609_APPLY_ACTIVE=1` is set earlier, after `c1609_prepare_resume` and before the dry-run return. The resume refusal is still `write_result`. CP-17 `Resume_refuses_until_the_operation_token_matches` showed exit 2, `DIAGNOSIS=apply-gate-off`, zero `docker volume rm`, and a following resume with the same token completed and removed the four temp volumes.

With the gate off, preview and audit still run. Preview prints `C1008_PREVIEW remove=` for each target, then `would delete: <present names>`, then the preserve line, and removes nothing. With the matching token, apply deletes as before.

The bridge in `scripts/c590-real.ps1` forwards the variable only when it matches `^enabled-c1008[0-9a-f]{32}$`. The fixture turns the gate on for existing positive cases unless the test sets `C1609_FIXTURE_APPLY_GATE=off`. Production's default stays unset. The checkpoint runs unset the variable.

Second rollout, two PowerShell steps:

```
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase retire-temp -DryRun
$env:ANTIPHON_VOLUME_RETIREMENT_APPLY = 'enabled-<operation-id>'
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase retire-temp -RetirementProof <operation-id>
```

Gate 9 in `docs/docker-stack.md` says apply is off by default, names `apply-gate-off` and exit 2, and states that a preview which is not followed by the enabled apply leaves the temp volumes so the next `deploy-temp` refuses `TempVolumesRetained`.

### Entry points

Every caller of the retirement delete reaches `c1008_recycle`, and the apply path of that function calls `c1609_apply_gate` before `docker volume rm --`.

| Entry | How it reaches the gate |
|---|---|
| Scripted `retire-temp` apply | `case_retire_temp_runner` calls `c1008_recycle "$TEMP_PROJECT"`. |
| Resume | Same function with `C1008_RESUME=1`. |
| Manual apply | `scripts/retire-runner-volumes.ps1` `Invoke-C1609ManualEntry`, and `scripts/c1609-manual.ps1` Apply, both reach `-Case retire-temp-runner`. |
| Host `deploy-parent` | `case_deploy_parent` calls `c1008_recycle "$HOST_PROJECT"`. The same loop deletes host volumes. |
| Dry-run intercept | `deploy-parent` and `retire-temp-runner` with `C1008_DRY_RUN=1` call `c1008_recycle` and return at the preview `write_result` before the gate. |
| Wrapper | `scripts/deploy-server2.ps1` phase `retire-temp` runs Preview then Apply under one operation id unless `-DryRun`, `-RetirementProof`, or `-ResumeRecycle`. `-DryRun` returns after preview. |
| Bridge | `scripts/c590-real.ps1` exports the token only on an exact `enabled-c1008<32 hex>` match. |

`retire_c590_leftovers` still has `docker volume rm "$volume"` with no `--`, limited to `^c590[0-9a-f]{12}_` volumes. That statement is outside the gate. It is the only other `docker volume rm` in `scripts/`. No new deletion path was added.

CP-17 methods, all four passed: `Apply_gate_is_checked_before_every_volume_removal` (the gate text precedes the one `docker volume rm --`, and the gate body contains `write_result` and does not contain `c1008_refuse` or `docker volume rm`); `Scripted_apply_refuses_until_the_operation_token_matches` (host `deploy-parent` off, temp unset / `enabled` / `1` / `true` / wrong id, matching token removes four); `Resume_refuses_until_the_operation_token_matches`; `Manual_apply_refuses_until_the_operation_token_matches`. The structural test is the mutant witness: a delete placed before the gate check fails `Apply_gate_is_checked_before_every_volume_removal`.

## Repairs

No test was removed, skipped, or relaxed. A replaced assertion kept its protective intent.

- `af9b3400ddccd1224ce73e0ff20fc3b6d9fb7589` — CP-16 harnesses that call `c1609_load_retirement` now receive the real retirement script the way production copies it. Every negative case stayed.
- `c413a011aa98904be20cd67b460de389536b5d35` — CP-8 wrapper expects Preview (`dryRun=true`) then Apply (`dryRun=false`) under one operation id. The unselected sibling `C1008_Retired_absent_null_is_accepted` was added to the filter. Floor moved from 10 to 11. The plan `### Checkpoints` row was amended in the same commit.
- `45db224731218a4c73f06846f281e39a9047b457` — CP-9 requires both `readonly` and `volume-nocopy` on every volume mount.
- `c4e76c8d3e47a90136555fc390e78c01912d4d64` — `C1008_Host_proof_admits_only_an_authenticated_private_origin` writes a bound v2 proof. The private authenticated origin is still the only admission. The public mismatched proof still refuses. `C1008_Host_proof_file_omits_credentials` was left on the v1 writer (`Write-C1008HostOriginProof` without `ProofVersion=2`) and still covers `ghp_FAKE_TOKEN_SHOULD_NOT_LEAK`.
- `f272b2c3549c0222fbe6779c2b1a25977c792bea` — red CP-17 tests and the CP-17 plan row, before the implementation.
- `509f208917288eb27e6fda779e3329645e0ac5cc` — the gate, the bridge filter, the fixture default-on injection, the runbook gate-9 text, and the documentation pins.
- `b87dba8009d23a24fbecaec0f18d8643daa86ee6` — after the unpublished-child `git reset --hard`, the host-proof test drops `.git/logs` and `ORIG_HEAD` before the later proof-binding refusals. The unpublished-child refusal stayed. Cause of the red: `RecycleUnpublishedWork` with `commits=1 tips=1` from the abandoned child.
- `ba2f264bd920a60da1e922a1f22031973be1e9b6` — the host-proof inventory registers the per-run `ext::` helper origin beside `VolumeRetirementFixture.OriginUrl`. Inventory assertions stayed. The resume cut calls `c1609_load_retirement` before replacing `c1609_target_intent`, so the real writer does not overwrite the cut.
- `aa331d0135c4aeb0be4b03137c4d4c0d54daddff` — the host-proof test deletes the public stand-in proof files immediately before the final private `WriteV2BoundProof` that feeds inventory and Ready preview. The public-origin mismatch refusal runs before that deletion and stayed. Cause of the red: `c1609_publication_fresh` requires every file in the proof directory to match the current operation, and the stand-in is a second file with a different operation id. The dry-run path labels that failure `reason=proof-stale`.

## Land graph

No definition file changed between `6d9bdc9067eec016f07b9dbb0d90709386955f6e` and `aa331d0135c4aeb0be4b03137c4d4c0d54daddff`. The diff of `*.csproj`, `*.props`, `*.targets`, `*.sln`, `*.slnx`, `*.slnf`, `*.rsp`, `Directory.Build.*`, `global.json`, `nuget.config`, `packages.lock.json`, `packages.config`, and `directory.packages.props` is empty.

`scripts/audit-land-verification-graph.ps1 -Check` was run with no `-Update`. Exit 0. Output: `digest=20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6 count=66`, then `phase=pass2` and `approved`. The evaluator was built through `scripts/build-slot.ps1 -Label c1609-land-graph` (`-c Release`, `OutputPath=bin-land-audit/Release/net10.0/`, `UseAppHost=false`). Slot lease `2e0c23cc-06eb-440d-bc78-9aca84772aca`, waited 270s, build 3.3s, 0 warnings.

## Delete-enable ledger

This work is due before the second rollout. A preview-only gate 9 leaves the temp volumes. `c1609_temp_retirement_clearable` returns `TempVolumesRetained` when any of `antiphon-runner-temp_work`, `antiphon-runner-temp_runner-tmp`, `antiphon-runner-temp_dind-data`, or `antiphon-runner-temp_runner-state` exists, and `case_deploy_temp_runner` refuses with that reason before checkout. The next rollout's `deploy-temp` stays refused until apply is enabled for that operation or a manual retirement runs first. This round did not probe whether those volumes exist on server2.

Still open, and not done in this round:

- CP-10 N2 and N3. N3 was left deferred. Custody requires proof files owned by `$EUID`. N3 runs as root via nsenter while the proofs belong to uid 1654. `reason=proof-stale` is also the label for that custody failure. Widening the freshness window would not fix it.
- Extended J6–J8 and the lock matrices.
- H6 pipe-holder.
- Native Windows CP-11, CP-12, and CP-13.
- The still-v1 `C1008_Host_proof_file_omits_credentials` rewrite requested by plan lines 940–945. The method stays. It still covers the credential sentinel.
- Final Opus review of the apply gate and the repaired rows.

Credentials do not enter a container. Production deletes stay disabled by default.

## Next stage

final Opus review of the apply gate and the repaired rows.
