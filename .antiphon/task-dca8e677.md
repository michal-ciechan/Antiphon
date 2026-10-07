# CARD-1105 S4 Final Review (task dca8e677)

Outcome: no defect. S4 (docs, the real-Docker proof, the `SERVER2_COMPOSE` fixture copy) is fail-closed
on real containers and volumes, the docs sentences are true against the code at this SHA, nothing
touches production server2, and the 11-row rerun at the reviewed SHA is green with the counts below.
Disclosures, all non-defects, are listed under "Disclosures" and filed as CARD-1142. The staged
rollout is NO-GO until this slice is landed and the AppHost is restarted at the landed SHA; the
checklist is at the end.

subjectTaskId `3be5d1b3-9854-4858-9191-71b80cae9d17` (the Code owner; carried as the landing owner).
Reviewed ref `refs/heads/feat/card-task-3be5d1b3` = `143b74545a7143d91e2beb85a07b8fa1371e720d`
(equal to `refs/heads/feat/card-task-dca8e677` at dispatch). Base
`64469a828a1fa36bdfa8aacb82df56ef72ee9817`; commits `c4d2d1c6bc91dd90e5dcd55eba347b62e299b530` and
`143b74545a7143d91e2beb85a07b8fa1371e720d`; 5 files, 167 insertions, 14 deletions
(`docs/docker-stack.md`, the plan, `scripts/fixtures/c1008-recycle-real-cases.mjs`,
`DockerStackDocumentationTests.cs`, `RollingVolumeRecycleDockerTests.cs`). `scripts/c590-remote.sh`
is unchanged in the range (`git diff --stat base..HEAD`). Review worktree `/work/worktrees/task-dca8e677`
(server2-temp mirror), no source file changed by this review; this report is the only commit.

## Merge onto the moved origin/master

`origin/master` at fetch time is `cc7b7101a2034c40499e4e1e56206bdde78e7164` (two commits past the
`c0448cf8f8…` the brief named). `git merge-tree --write-tree origin/master 143b74545` exits 0 with
tree `2cf5236490c7108cd6d5a45f7e2dbb848cfdc857`: no conflicting hunk. The seven master commits since
the base touch only `tests/Antiphon.Tests/Application/*` (CARD-1082/1108/1124), disjoint from the
slice's five paths.

## Rerun at the reviewed SHA (run `20261007-064959-e94e`)

One checkpoint-tool run from a hand-made selection table (scratchpad, outside the tree), `--after S4
--serial --expected-source-sha 143b74545…`, one isolated build `tests/Antiphon.Tests -> bin-c1105rv/`
(184 s, slot granted, waited 0 s). Row ids CP-n are the plan's own rows with the plan's exact
filters; RV-n are the brief's extra guard classes. Every row `slot=granted waited=0s dirty=0
sourceState=clean buildSource=verified`, 0 failed, 0 skipped.

| Row | Filter (class) | Executed/passed | Wall |
|---|---|---|---|
| CP-8 (R-1) | `DockerStackDocumentationTests/*` | 14/14 | 4 s |
| CP-9 (V-7, R-7) | `RollingVolumeRecycleDockerTests/C1008_Real_docker_comparison*`, `C1008_REAL cases=34 base=5 changed=29 failures=0 cleanup=absent` | 1/1 | 11m49s |
| RV-1 | `TestClassificationGuardTests/*` | 1/1 | <1 min |
| RV-2 | `SlowTestTripwireTests/*` | 2/2 | <1 min |
| RV-3 | `RetiredTempContainerScriptTests/*` | 13/13 | 2m56s |
| CP-7 (R-6) | `RollingVolumeRecycleScriptTests/*` | 16/16 | 19m57s (slot held 07:08:12-07:28:10; longest method 13m35s) |
| CP-2 (V-5, R-3) | `RemoteScriptContractTests` 5 text pins | 5/5 | 4 s |
| CP-3 (V-2, V-3, V-4, R-4) | `RemoteScriptContractTests` 5 flow methods | 5/5 | 4m01s |
| CP-4 (R-4) | `RemoteScriptContractTests` 3 long census methods | 3/3 | 16m47s |
| CP-5 (R-4) | `RemoteScriptContractTests` 7 census methods | 7/7 | 8m00s |
| CP-10 (CARD-1116/1117) | `RemoteScriptContractTests` 4 resume/compose methods | 4/4 | 1m34s |

Run wall 68m33s (sequential-equivalent 65m28s); verdict GREEN exit=0, 11 green / 0 red / 0 skipped, `unlisted: none`, the green run deleted `bin-c1105rv/`. Report: `.antiphon/checkpoints/20261007-064959-e94e/report.md`
(gitignored, in this worktree). The remote C1008_/C1087_/C1105_ classes were run (CP-2, CP-3, CP-4,
CP-5, CP-10 = 24 methods including the CP-2 text pin); CP-7 was run. Not run, and not in the brief's
list: CP-1 (`RollingProductionMountTests`), CP-6 (`RetiredTempContainerHostTests`) and
`HostJqPrerequisiteScriptTests`; S4 does not touch their code paths and the Code report lists them
green (6, 21, 39).

Checkpoint lines, unedited:

```
CHECKPOINT CP-8 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=ok filter=/*/*/DockerStackDocumentationTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=reused filter=/*/*/RollingVolumeRecycleDockerTests/C1008_Real_docker_comparison* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
CHECKPOINT RV-1 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=reused filter=/*/*/TestClassificationGuardTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/RV-1/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
CHECKPOINT RV-2 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=reused filter=/*/*/SlowTestTripwireTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/RV-2/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
CHECKPOINT RV-3 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=reused filter=/*/*/RetiredTempContainerScriptTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/RV-3/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=reused filter=/*/*/RollingVolumeRecycleScriptTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)|(Deploy_parent_seeds_or_verifies_runner_checkout*)|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)|(C1105_Generation_identity_refuses_mismatch*)|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)|(C1008_Recycle_exact_default_volumes*)|(C1008_Recycle_dry_run_never_mutates*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_resume_requires_matching_receipt*)|(C1008_Recycle_receipt_records_disk_and_partial_failure*)|(C1008_Recycle_refuses_references_and_unknown_census*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=reused filter=/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)|(C1008_Recycle_refuses_unpublished_and_dirty_work*)|(C1008_Retire_temp_rechecks_absence_and_retirement*)|(C1087_Host_census_filters_and_names_cause*)|(C1008_Recycle_preserves_tmp_copyup*)|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)|(C1008_Recycle_audits_work_as_1654*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=143b74545a7143d91e2beb85a07b8fa1371e720d build=reused filter=/*/*/RemoteScriptContractTests/(C1105_Compose_cmp_refuses_before_removal*)|(C1105_Resume_identity_refusals*)|(C1105_Resume_state_init_image_follows_the_journal*)|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-dca8e677/.antiphon/checkpoints/20261007-064959-e94e/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=143b74545a7143d91e2beb85a07b8fa1371e720d sourceState=clean buildSource=verified
```

## Code report audit (CP-n against the plan's table)

The Code report's CP-8 (`executed=14 passed=14`) and CP-9 (`executed=1 passed=1`, census line
`cases=34 base=5 changed=29 failures=0 cleanup=absent`) lines carry the plan's exact filters and meet
the rows' Expect/Min (14, 1), `sourceState=clean buildSource=verified slot=granted`, at the reviewed
SHA. No row is missing for S4, no zero count. The named regression runs are listed with counts
(recycle-script 16, retired-script 13, host-jq 39, guard 1, tripwire 2, compose pins 5, remote 20,
retired-host 21, production mounts 6); the tool bootstrap is the plan's one explained unlisted build.
Repair round 1 is one line in the fixture's `docker()` shim (commit `143b74545`). The new test
(`Mount_generation_protocol_is_documented`) and the two new fixture cases go red: see probes.

`scripts/check-evidence-diff.ps1 -BaseRef 64469a828… -HeadRef 143b74545…`: `EVIDENCE result commits=2
entries=0 violations=0`, exit 0.

## Hard checks

(a) **Real Docker runs production script code.** `scripts/fixtures/c1008-recycle-real-cases.mjs`
executes the committed `scripts/c590-remote.sh` text (version C, read at start) with only project
names rewritten; the injected prelude replaces lane detection, checkout, boot files, `compose_host`/
`compose_temp`, `c1008_compose_source`, `sudo`/`df` (through the owned `nsenter` helper) and a
`docker()` shim (name firewall, `image inspect` tag answer, `ps` label filter, fault injection).
`c1008_recycle`, `c1008_bind_generation`, `c1008_previous_generation`, `c1008_previous_model`,
`c1008_compose_model`, `c1008_owned_mounts`, `c1008_container_census`, the stop/rm/volume-rm loop
and `case_deploy_parent` run unchanged against containers from `docker compose up -d` and real
named volumes on the runner's nested daemon. `C-previous-generation-main` (`fixture(true,true)`)
brings main up from the 10-mount previous model, `main.env SOURCE_REVISION=bbbb…`, status
`buildVersion=bbbb…`, and the compose stub serves the previous file for that SHA: production prints
`C1008_GENERATION … previousMounts=10 targetMounts=11`, removes three volumes, and the assertion
inspects the single replacement runner for a `bind`, `RW=false` mount at `/run/antiphon/github-token`.
`C-previous-generation-foreign-mount` replaces the runner with a 12-mount copy (all 11 real mounts
plus a read-only bind at `/srv/c1008-foreign-extra`). In `c1008_recycle` the order is: target model,
early `cmp`, status proof, `c1008_bind_generation` (generation proofs, before the lock), `c1008_lock`,
status proof, volume facts, ownership, then `c1008_owned_mounts "$owned" "$C1008_PREVIOUS_MODEL"` →
`error("mount count")` → `c1008_refuse RecycleContainerStateUnknown`. The first `c1008_save` (journal)
and the `docker stop --time 90` loop both come later, so the refusal is before any journal, stop, rm
or volume rm. The assertions check state after the refusal: `refused()` requires exit 2 and
`accepted=false`, then `payload()` runs a container that mounts each of the three recycle-target
volumes read-only and reads its `sentinel:<name>:old` file (a removed volume would be recreated
empty and fail the read); `record()` then runs `preservation()` (retained volumes' inspect JSON
unchanged, bind file hashes, marker, broker still running). The journal must be `null` and the
bash xtrace must contain no line with the container id and the word `stop` and no `volume rm` of the
three targets. Probe: with the mount-count clause in `c1008_owned_mounts` replaced by `if false or`
(scripts/c590-remote.sh:4519, method-scoped, reverted), the fixture run under the slot gate reached
the foreign case, production printed `C1008_GENERATION … previousMounts=11 targetMounts=11` and
`C1008_RECYCLE … removed=3 … outcome=reclaimed`, and the fixture failed `ExpectedRefusal:
C-previous-generation-foreign-mount` with `C1008_REAL cases=33 base=5 changed=28 failures=1
cleanup=absent` (exit 1, which fails `child.ExitCode.ShouldBe(0)` and the `failures=0` regex). All
33 earlier outcomes were unchanged. The script was restored (`git status` clean).

(b) **The `SERVER2_COMPOSE` copy is sandboxed.** The prelude is inserted before the script's EXIT
`trap` (line 5725), after the production defaults at lines 897-900, so it overrides
`SERVER2_COMPOSE='<root>/server2-compose.yml'`, `SERVER2_TEMP_COMPOSE`, `SERVER2_ENV='<root>/main.env'`,
`SERVER2_TEMP_ENV`, `SERVER2_ROOT='<root>/server'`, `EVIDENCE_ROOT` and every secret/bind path with
paths under the fixture root `/tmp/c1008-real-<rand>/<case>/` (mkdtemp). `cp -f -- <case>/main.json
"$SERVER2_COMPOSE"` writes inside that root; `c1008_compose_source` is replaced by a copy from the
fixture's own files; `compose_host`/`compose_temp` are replaced and honour `C1008_COMPOSE_DIR`;
`CHECKOUT` is not reached on the recycle path (`ensure_checkout`, `seed_runner_checkout` and the
real `build_server2_images` are stubbed, `write_result` ends the case after `compose_host up`).
HTTP goes to `C604_SERVER_ORIGIN=http://127.0.0.1:<random>` (the fixture's own `http.createServer`;
`c1008_http` and `c849_status_body` use that variable only). The `docker()` shim returns 97 for any
argument containing `antiphon-runner`, `guard()` refuses such names before any Docker call, and the
daemon must be the nested one (`/.dockerenv` and `docker info Name == hostname`, else
`SiblingDaemonRefused`). Nothing contacts production server2; the broker/wrapper deploy-server2.ps1
copies are fixture-transformed with `ANTIPHON_API=<local origin>`.

(c) **Docs are true against the code at this SHA** (`docs/docker-stack.md` 398-449 and gate 6).
Verified sentence by sentence against `c1008_previous_generation` (3888-3931: envfile per project,
`^[0-9a-f]{40}$`, status `buildVersion` equality → `RecycleGenerationMismatch`, `image_count==0 &&
TEMP_PROJECT` SHA-only branch with null tag/id and no inspect, main `image_count==1` else
`RecycleGenerationUnknown`, tag `antiphon-server2/session-testing:<sha12>` inspect equality →
`RecycleGenerationMismatch`), `c1008_compose_source` (git show from the checkout, one fetch retry),
`c1008_previous_model` (render via `C1008_COMPOSE_DIR`, bind-kind table `C1008_BIND_KINDS` with the
six targets, `error("unknown bind")`/structural errors → `RecycleGenerationUnknown`),
`c1008_compose_model` (hard-coded roster incl. `bind($token;"/run/antiphon/github-token";false;false)`,
`RecycleComposeMismatch`), `c1008_recycle` (early `cmp` of
`$CASE_DIR/compose/<sha12>/docker-compose.server2-runner.yml` with `$SERVER2_COMPOSE` before
`c1008_lock`; `c1008_bind_generation` before the lock and before any `c1008_save`; foreign mount →
`RecycleContainerStateUnknown` after the lock, before the first save and the stop loop),
`case_deploy_parent` (same `cmp` before `compose_host up -d`), `c1008_bind_generation` (prints
`C1008_GENERATION project= previous= target= previousMounts= targetMounts=`),
`case_retire_temp_containers` (`c1008_lock` = `c1008_rollout_lock` + `c849_lock`, pending receipt
`c994_save` with `C994_READY=1` before the census; a `c1008_refuse` inside `c1008_previous_generation`
routes through `c994_refuse`, which marks `.refusal`/`.outcome=refused` and saves before exit 2; the
`docker rm` loop is later), `case_retire_temp_runner` (→ `c1008_recycle "$TEMP_PROJECT"` → the same
`c1008_previous_generation`). Gate 6 ("accepts the running container's own generation and requires
the new one after recreate"): pre-removal proof uses `C1008_PREVIOUS_MODEL`, `c1008_record_recreated`
uses `c1008_compose_model`. Add protocol: a target absent from `C1008_BIND_KINDS` is
`error("unknown bind")` → `RecycleGenerationUnknown` in `c1008_previous_model`. Remove protocol:
`compose_host` exports fixed variable names, the previous file interpolates them; a moved host path
renders a source that fails `$m.Source==$e.source` → `error("mount identity")` →
`RecycleContainerStateUnknown`. State-init-only: the `image_count==0 && project==TEMP_PROJECT` branch.
Pin probes: `Mount_generation_protocol_is_documented` green at baseline (1/1); with gate 6 changed
to "ignores the new one after recreate" it failed naming that pin; with "the same commit" changed
to "a later commit" it failed naming the add-protocol pin. Both reverted (`git status` clean). The
docs test reads the file from the repo root at run time (`DockerStackDocuments.Read`), so the pins
guard the committed text. One wording inaccuracy (rollback sentence) is disclosure 4 below; it
does not change the operator conclusion.

(d) **Ryuk repair is fixture-only.** Commit `143b74545` adds one `elif` to the fixture's `docker()`
shim: `docker ps …` gains `--filter label=io.antiphon.c1008-real=<prefix>`. Production
`c1008_container_census` (`docker ps -aq --no-trunc` then `docker inspect` per id, any inspect
failure → return 2 → `RecycleVolumeCensusUnknown`) is unchanged; the failure it hid was a
Testcontainers Ryuk container from another agent exiting between `ps` and `inspect`. Every fixture
container (foreign neighbours, prefix neighbours, broker, helper) carries that label, so no case
loses coverage; the fixture's own census (`cleanupCase`, final residue check) already filtered by
the same label.

(e) **No assertion weakened or deleted; no timeout widened.** `RollingVolumeRecycleDockerTests`
changes 32→34 and 27→29 to follow the two added cases and adds two `ShouldContain` assertions
(main accepted; foreign `RecycleContainerStateUnknown` not accepted); `TimeSpan.FromMinutes(12)` is
unchanged from the base. The docs test adds pins only. CP-7's plan `--row-timeout 20m` is unchanged:
this run measured CP-7 at 19m57s (slot held 07:08:12-07:28:10; longest method 13m35s) host wall (S3 review 19m48s, S3 Code 19m32s). The 20-minute row budget is not safe: this run used the tool's derived 45-minute budget and the row still took 19m57s, three seconds short of what `--row-timeout 20m` would have allowed (S3: 19m48s, 19m32s). CARD-1139 item 1 stands; raise the S3 group row timeout to 30m or split the class, and change no test deadline.

## Disclosures (not defects; filed as CARD-1142)

1. **CP-9 runs within seconds of its own 12-minute child deadline.** `RollingVolumeRecycleDockerTests`
   gives the fixture `TimeSpan.FromMinutes(12)` and kills it on expiry. The fixture took 618 s in
   the Code run and 709 s (TRX duration 00:11:49) in this run; the mutated probe run (which
   reclaimed one extra case) took 827 s under the slot. S4 added two full-up cases (about 70 s)
   without changing the deadline. On a loaded host the row reds as a child kill plus
   `c1008-recycle-cleanup.mjs`, not as a test failure. Raise the deadline with this measured
   reason (or split the fixture), keep the plan's CP-9 estimate honest (14 → 15+).
2. **The real foreign-mount case uses identical previous and target rosters.** `fixture()` with
   `previousMain=false` makes `c1008_compose_source` serve the current 11-mount file for the
   previous SHA too, so production printed `previousMounts=11 targetMounts=11` for
   `C-previous-generation-foreign-mount`. The refusal before stop with volumes retained is proved,
   but not against a 10-mount previous roster; that combination is proved offline (V-1 `extra`
   under either model, V-3 foreign extra bind). A `fixture(true,true)` + `addForeignBind` variant
   would make the name true on real Docker.
3. **Post-refusal container liveness is inferred, not inspected.** The foreign case proves "before
   any stop" by a trace scan (no line with the id and `stop`) plus `journal === null` (production
   saves `stopIntents` before every `docker stop`), and proves "volumes stay" by state (payload
   reads). A `docker inspect` of the foreign container after the refusal (`State.Running` and
   `StartedAt` unchanged) would make the before-stop claim a state assertion too.
4. **Rollback sentence direction.** docker-stack.md says an older SHA's pre-fix script "requires the
   target mount count of the containers it stops, so a container that lacks the new bind refuses on
   mount count". In a rollback from this fix the running container carries the new bind and the
   older roster lacks it (D-8: "deadlocks in the `mount count` direction"); the pre-fix script also
   refuses earlier, at `c1008_compose_model` (`error("mount set")`, `RecycleComposeMismatch`),
   because the on-disk Compose file is still the new one. The conclusion (unsupported target;
   accepting temp is the live rollback) is correct, so this is wording, not a safety-relevant
   falsehood. Not covered by an R-1 pin.
5. **Scope of the early `cmp` sentence.** The compare "runs before the recycle lock and before any
   removal" only for the host project in apply mode (`C1008_PROJECT == HOST_PROJECT && C1008_DRY_RUN
   == 0`); true for `redeploy-old`, which is what the section describes. In the real fixture the
   stub always yields a byte-identical copy of `$SERVER2_COMPOSE`, so that refusal cannot occur there;
   it is covered offline by CP-10 `C1105_Compose_cmp_refuses_before_removal`.

CARD-1139 (S3 follow-ups: CP-7 row margin, unpinned main-lane `image_count=0` guard, D-6/S4 doc
wording) stands; S4 delivered item 3 (the doc and plan text now state the SHA-only state-init
binding and that `retire-temp-runner` uses that path). Item 2 (the `$project` guard mutation
undetected) remains open and is a Mutation/TestDesign follow-up, not an S4 defect.

## PCs, platform, custody

PC-1..PC-14 stay pending for method-scoped SourceLanding Mutation; none were run here (the three
review probes above are review controls, restored). `GET /api/runner-defaults`: revision 2, global
runner `server2`, no kind defaults. `GET /api/session-runners`: desktop (windows), server2 (linux,
draining), server2-temp (linux, accepting). Build-slot broker at start: budget 4, occupied 0, no
waiters; every lease waited 0 s. No `-Runner` pinned; `-Platform Linux` applies to this card's
rows (D-9). Mutation build outputs `bin-c1105rvmut` (27 dirs) were deleted with a root-confined
loop; the green run deleted `bin-c1105rv`; the tool driver `bin-c1105rvdrv` was deleted last.

## GO / NO-GO for the staged rollout (docs/docker-stack.md, plan runbook)

Current state read at 07:00 UTC: server `GET /api/version` = `5b713f68…` (the pre-fix plan base);
`server2` status `buildVersion=4358939ecd85…`, `draining=true redirectTo=server2-temp
retireWhenIdle=false`, counters `sessions=0 runnerSessions=0 queuedTasks=0`; `server2-temp`
`buildVersion=d985af05…`, `acceptingNewWork=true`, 3 sessions. Verdict today: **NO-GO** (nothing
landed yet). Order, with the gate that is still open:

1. Land this slice (`-ExpectedSourceSha 143b74545…` for owner `3be5d1b3…`), then
   SourceLanding Mutation PC-1..PC-14 (plan requirement before the rollout) — OPEN.
2. Canonical desktop checkout: wait for no land in progress, `git pull --rebase` to the landed
   SHA `F`; no half-reset worktree; never from a worktree, never `-AllowWorktree` — OPEN.
3. Restart AppHost from the canonical checkout (`scripts/restart-apphost.ps1`, `-ExpectedServerSha F`),
   after checking `logs/apphost.restart.lock` / `logs/apphost.launch.lock`; exit 3 is a refusal —
   OPEN.
4. `GET /api/version` SHA == `F` and `/health` OK (today 5b713f68 ≠ F) — OPEN.
5. `deploy-server2.ps1 -Rolling -Sha F -Phase check-census`: `RECYCLE_PROJECT …` and two
   `RECYCLE_CENSUS` lines with `open=0 boundOpen=0 landPending=0` for `server2`; keep the
   `census-check-census-<runner>.json` receipts.
6. `-Phase check-host-jq`: fresh receipt; only `HostJqMissing` permits `provision-host-jq`.
7. `runner-drain.ps1 status -RunnerId server2` and `server2-temp`: old `draining=true
   redirectTo=server2-temp retireWhenIdle=false` with zero counters (true at 07:00 UTC); temp
   `acceptingNewWork=true buildVersion=d985af05…` (true).
8. `-Phase redeploy-old -DryRun`: expect `C1008_GENERATION project=antiphon-runner
   previous=4358939ecd85… target=F previousMounts=10 targetMounts=11`, three `C1008_PREVIEW remove=`
   lines and the preserve line; any `RecycleGeneration*` refusal is a stop (record code + receipt).
9. `-Phase redeploy-old` (autonomous per the policy): `WARN GithubTokenAbsent` is expected, not a
   stop; expect `C1008_RECYCLE … removed=3 … outcome=reclaimed`, then the wrapper clears old's
   drain and waits for `acceptingNewWork=true` at `F`.
10. Gate 7 smoke on `server2`: status/`docker exec` block, a sanctioned `Plan` canary with `-Runner
    server2`, `verify-card0849-caches.ps1 -Case Both -Sha F`. On failure: `runner-drain.ps1 drain
    -RunnerId server2 -RedirectTo server2-temp -Reason 'post-upgrade smoke failed'` and stop.
11. `-Phase drain-temp -WaitIdleMinutes 240`, then `-Phase retire-temp` (expect `outcome=completed`
    for four temp volumes); record statuses, `C1008_GENERATION`/`C1008_RECYCLE` lines and receipts
    under `.antiphon/rolling-server2/<run-id>/`. CARD-1105 closes only when both receipts exist and
    `server2` accepts at `F`.

Human-only, never autonomous: `scripts/refresh-server2-github-token.ps1` (vault token, any time
after step 9); releasing remaining `server2` sessions at the four-hour drain cap
(`runner-slots.ps1 release`); `-KillSessions`; Docker prune; abandoning the rollout; project
identity corrections (`-ProjectId`); any `-ResumeRecycle` after a partial failure; `-DryRun` is a
preview and grants no apply authority. Do not run `deploy-temp` or `retire-temp` before step 11.

## Commands to rerun

Bootstrap the tool under the slot gate, then one run from the selection table (kept outside the tree):

```
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1105rv-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1105rvdrv/ -nodeReuse:false
dotnet tools/Antiphon.Checkpoints/bin-c1105rvdrv/Antiphon.Checkpoints.dll start --plan <selection.md> --after S4 --serial --expected-source-sha 143b74545a7143d91e2beb85a07b8fa1371e720d --total-timeout 200m
dotnet tools/Antiphon.Checkpoints/bin-c1105rvdrv/Antiphon.Checkpoints.dll wait --max-wait 470s   # repeat while exit 75
dotnet tools/Antiphon.Checkpoints/bin-c1105rvdrv/Antiphon.Checkpoints.dll validate --evidence .antiphon/checkpoints/<run>/report.json --expected-source-sha 143b74545a7143d91e2beb85a07b8fa1371e720d
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 64469a828a1fa36bdfa8aacb82df56ef72ee9817 -HeadRef 143b74545a7143d91e2beb85a07b8fa1371e720d
```

Row timeouts were the tool's derived `max(15, 3 x EstimatedMinutes)` (CP-9 42 m, CP-7 45 m, CP-4 45 m); no `--row-timeout` was passed and no test deadline was changed. Review probes: `dotnet tests/Antiphon.Tests/bin-c1105rvmut/Antiphon.Tests.dll --treenode-filter "/*/*/DockerStackDocumentationTests/Mount_generation_protocol_is_documented"` under `build-slot.ps1` (baseline green, two doc mutations red), and `build-slot.ps1 -- node scripts/fixtures/c1008-recycle-real-cases.mjs` with the mount-count clause at scripts/c590-remote.sh:4519 set to `if false or` (red at the foreign case, 827 s). All mutations reverted; mutation outputs deleted.

--- review evidence ---
subjectTaskId: 3be5d1b3-9854-4858-9191-71b80cae9d17
reviewedSourceSha: 143b74545a7143d91e2beb85a07b8fa1371e720d
reviewedSourceClean: true
ordinaryScopeCompleted: Full
