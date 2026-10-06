# CARD-1105 S2 Code report (task 1075abf0)

Outcome: S2 recycle wiring and ordering is implemented. `redeploy-old` resolves the running generation after the status proof, journals it, checks the checkout out and creates boot files before removal, and compares the materialized target compose with `$SERVER2_COMPOSE` before `compose up`. The pre-removal proof uses the previous roster. The post-recreate proof keeps the hard-coded 11-mount roster. CP-2 is green on the tested source. restart: none (script and tests only; no server or runner restart).

Tested source `fa3e06f028731da82f4ae7711157ee94b15d0c3e`. Branch `feat/card-task-1075abf0`. Worktree `/work/worktrees/task-1075abf0`. Landing owner / original Code task `1075abf0`. Card `5f07b7ce-0d6e-48e5-a7b8-3d77cfd62cf2`. Base `e839feda98ce3d97b6329bcd4a1030f6299460cd` (plan commit; merge-base with `origin/master`). Plan `docs/superpowers/plans/2026-10-06-card-1105-compose-mount-generation-plan.md`. Built on S1 tip `f2f1b68417dfb6975f7224541e382e7f18575178`.

This report commit is documentation only. CP-2 verified `fa3e06f028731da82f4ae7711157ee94b15d0c3e`.

## Commits

- `93f37391845e9e596505037765a8b40769144ae2` feat(CARD-1105): prove the running generation before recycle removal
- `0ec8434e740d3afae9f59c9c80cee7ffafbffcda` test(CARD-1105): reconcile resume against the running roster
- `fa3e06f028731da82f4ae7711157ee94b15d0c3e` fix(CARD-1105): resume generation after the session-runner is already gone

## What S2 does

`c1008_recycle` calls `c1008_bind_generation` after `c1008_status_proof` and the temp-image lookup, and before the dry-run return. That helper prints `C1008_GENERATION project=<p> previous=<sha|none> target=<sha> previousMounts=<n> targetMounts=<n>` and stores `previousSha`, `previousComposeDigest`, and `generation` on a fresh journal. Mount counts are session-runner volume and bind rows. A previous SHA renders `c1008_previous_model`; an empty previous SHA uses the target model and `previousMounts=0`. `c1008_capture_json` keeps refusals in the current shell and leaves `DIAGNOSIS` on stdout. Pre-removal `c1008_owned_mounts` uses `C1008_PREVIOUS_MODEL`. `c1008_record_recreated` still uses `c1008_compose_model` (the 11-mount contract, including read-only `/run/antiphon/github-token`).

Resume refuses `RecycleResumeMismatch` when `previousSha`, `previousComposeDigest`, or `generation` is missing or the wrong shape. While a session-runner still exists and `ownedRemoved` is false, the freshly derived previous SHA must equal the saved one. After that session-runner is already gone, resume keeps the journaled SHA, requires `SOURCE_REVISION` to equal it, and requires every leftover container image to equal `generation.imageId`. After `ownedRemoved=true`, `SOURCE_REVISION` must be the saved `previousSha` or `$SHA`. The recreating/verified branch still compares recorded replacements raw. `c1008_reconcile_owned` receives the previous model.

`case_deploy_parent` now runs `ensure_checkout`, then `ensure_runner_boot_files`, then `c1008_recycle` and `c849_budget_gate`. Immediately before `compose_host up -d --no-build`, and only when `C1008_ACTIVE=1`, `cmp -s` refuses `RecycleComposeMismatch` if the materialized target compose differs from `$SERVER2_COMPOSE`. The dry-run intercept does not call `ensure_checkout`. `RecycleGenerationUnknown` and `RecycleGenerationMismatch` still refuse before the journal, `docker stop`, `rm`, and `volume rm`.

The host fixture's main containers and `SOURCE_REVISION` are the previous generation (`b`*40). The previous compose model is stored only at `project@bbbbbbbbbbbb`, so the target proof still falls back to `models[project]`. `C994_Host_task_and_land_census_is_complete` follows CARD-1087: Queued, Dispatched, Working, Blocked, and a Succeeded row with a pending land still refuse; Failed and Canceled do not.

## Checkpoint (run 20261006-192218-2312)

Tool command, not wrapped in a second build slot: `dotnet exec tools/Antiphon.Checkpoints/bin-c1105-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-06-card-1105-compose-mount-generation-plan.md --after S2 --expected-source-sha fa3e06f028731da82f4ae7711157ee94b15d0c3e --row-timeout 15m --total-timeout 30m`.

`--after S2` selects CP-2 only. Host Debian GNU/Linux 12. Tool line: `unlisted: none (the tool ran no other build or test command)`. Verdict GREEN exit=0. Wall 2m15s. Outputs deleted: `bin-c1105-a/`, `bin-c1105-b/`, `bin-c1105-c/`, `bin-c1105-d/`. The tool directory `.antiphon/checkpoints/20261006-192218-2312/tool` was removed by the tool (`identity-dead`).

```
CHECKPOINT CP-2 commit=fa3e06f028731da82f4ae7711157ee94b15d0c3e build=ok filter=/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-1075abf0/.antiphon/checkpoints/20261006-192218-2312/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=fa3e06f028731da82f4ae7711157ee94b15d0c3e sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=129.7844345s startup=4.037092s testsWall=0.1170947s teardown=0.4494268s hostWall=4.6036279s
```

Earlier CP-2 runs at `93f37391845e9e596505037765a8b40769144ae2` (`20261006-174815-a3fe`, 5/5, waited=15s) and `0ec8434e740d3afae9f59c9c80cee7ffafbffcda` (`20261006-184330-b6de`, 5/5, waited=0s) are superseded by the line above. The tool bootstrap build was `scripts/build-slot.ps1 -Label c1105-tool` (lease `dab800c1-de2a-4aa1-969e-0dd08c6d5f63`, waited=0s, held=5s) before the first of those runs. The later two runs reused that DLL.

## Ordinary V/R for S2

| ID | Outcome |
|---|---|
| V-2 previous roster accepted, recreated container must have the token bind | Passed. `C1105_Redeploy_accepts_previous_generation_and_requires_new_bind` in the remote selection below. Official row is CP-3 After S3. |
| V-4 dry-run prints the generation line and does not move the checkout | Passed. `C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout` in that same selection. Official row is CP-3 After S3. |
| V-5 checkout and boot files before recycle; `cmp` before `up`; dry-run has no `ensure_checkout` | Passed inside CP-2 |
| R-3 github-token, codex-home, checkout seed, and identity-file pins | Passed inside CP-2 (5/5 with V-5) |
| V-3, V-6, V-7, R-1, R-4, R-5, R-6, R-7 | Not this slice. S3 and S4. |

Red first, shell unmodified: the three new tests failed on the guarded defects (order still had `ensure_checkout` after `c1008_recycle`; dry-run had no `C1008_GENERATION`; the host proof refused `RecycleContainerStateUnknown` / `mount count` on the 11-vs-10 roster). Lease `00810ee1-fe01-4153-b7a4-0100d4c8c66e` (3 failed) and `cd389e0b-5187-41c3-8f20-11e4304709f3` (V-2 alone). They passed 3/3 on the behavior source before `93f373918` (lease `fb6e02b1-1c18-4839-b1e9-fc07d0f9b6fa`, waited=0s, held=58s).

## Legacy classes the diff touches

Serial `TUNIT_MAX_PARALLEL_TESTS=1`, `--no-build`, `OutputPath=bin-c1105-legacy/`, `UseAppHost=false`, through `scripts/build-slot.ps1`. Not a checkpoint row. Reason: the brief requires these classes once, in addition to CP-2.

Class filter at `93f37391845e9e596505037765a8b40769144ae2`, lease `a991b0c7-ec61-4646-a955-db8d25974889`, waited=0s, held=2437s: executed 58, passed 57, failed 1, skipped 0.

| Class | Result in that run |
|---|---|
| RollingVolumeRecycleScriptTests | 16/16. Plan CP-7 says 18; the source has 16. CP-7 is S3. |
| RetiredTempContainerScriptTests | 12/13. `C994_Resume_never_runs_container_cleanup` compared previous-generation containers with `c1008_compose_model`. |
| RetiredTempContainerHostTests | 20/20, including `C994_Host_task_and_land_census_is_complete`. |
| RollingProductionMountTests | 6/6 |
| SlowTestTripwireTests | 2/2 |
| TestClassificationGuardTests | 1/1 |

The resume proof was pointed at `c1008_previous_model` of the stack `SOURCE_REVISION` and passed 1/1 at `0ec8434e7` (lease `c6facbe5-4da7-484e-8f19-f631e64eb9a9`, waited=0s, held=13s). Rebuild lease `631a425b-8224-4323-a07e-75356e895f13`, waited=0s, held=128s.

Remote filter `/*/*/RemoteScriptContractTests/(C1008_*)|(C1087_*)|(C1105_*)` at that same test assembly, lease `3100b1a0-7c04-4a1c-90d3-333e67321200`, waited=0s, held=1312s: executed 15, passed 14, failed 1, skipped 0. The 11 `C1008_*` methods (plan R-4 says 10), `C1087_Host_census_filters_and_names_cause`, and the three `C1105_*` methods. `C1008_Recycle_receipt_records_disk_and_partial_failure` recovered the remove boundary with `RecycleGenerationUnknown`: the session-runner was already deleted, `ownedRemoved` was still false, and re-derivation had no image to read.

`fa3e06f02` keeps the journaled identity on that path. Rerun of that method and `C1008_Recycle_resume_requires_matching_receipt`: 2/2, duration 10m 14s (lease `cc783207-c974-4308-9457-aebcddab06b3`, waited=0s, held=621s). The other 13 remote methods and the 57 class methods do not take the new branch (session-runner already gone, `ownedRemoved` false). They were not rerun after this shell change. CP-2 was rerun on `fa3e06f02` and is the line quoted above.

## Evidence

`scripts/check-evidence-diff.ps1 -BaseRef e839feda98ce3d97b6329bcd4a1030f6299460cd -HeadRef fa3e06f028731da82f4ae7711157ee94b15d0c3e`: commits=5, entries=1, violations=0, exit 0. `git diff --check` on that SHA exited 0. The report-inclusive rerun is in the completion message.

## Pending mutation

PC-1 through PC-11 stay pending for post-land SourceLanding Mutation. Not run. PC-3, PC-4, PC-9, and PC-11 name tests S3/S4 have not added.

## Deferred

Whole Unit lane was not run. The brief limits this Final round to the S2 checkpoint row plus the named classes. S3 refusal battery, temp-lane previous proof, S4 docs, and real Docker are later tasks. No `-ResumeRecycle` of the live rollout, no retire-temp, no deploy-temp.
