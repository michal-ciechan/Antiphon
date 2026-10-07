# CARD-1105: rolling redeploy-old must accept the previous mount generation

Plan with folded verification design, 2026-10-06. Source inspected at
`5b713f6855ee417737ff7d7e47cb340d66e3d9b8` (branch base, equal to `origin/master` at
dispatch). The card was read in full through `scripts/card.ps1 get CARD-1105 -Board Antiphon`;
the Debug report is `.antiphon/task-281b29ce.md` on `feat/card-task-281b29ce` (`c91cad85`,
not an ancestor of the base). Line numbers below are at the base SHA.

## Outcome and boundaries

`redeploy-old`, `drain-temp` and `retire-temp` at a SHA that adds or removes a Compose mount
succeed against a runner created by the previous generation, while every destructive gate keeps
its current strength: the containers that are stopped and removed must carry exactly the mount
set their own generation declared, the replacement container must carry exactly the new
generation's mount set (today: the eleventh `session-runner` mount, the read-only directory bind
`/run/antiphon/github-token`), and a mount that belongs to neither generation refuses before any
stop. The fix is in `scripts/c590-remote.sh` and its offline fixtures only; the desktop wrapper
`scripts/deploy-server2.ps1`, the Compose files, the images and the runner are unchanged.

Out of scope: provisioning the GitHub token (operator-only, `scripts/refresh-server2-github-token.ps1`),
starting a fresh `deploy-temp`, retiring temp before the fix lands, `-ResumeRecycle` of any kind
(no journal exists), CARD-1010's state/cache opt-ins, and a redeploy to a SHA older than this fix.

## Ground truth

Every row was checked in source at the base SHA. The jq failure was reproduced locally: the
`d985af05` Compose file rendered with inert paths through `docker compose config --format json`
has 10 `session-runner` mounts and the base script's filter answers `jq: error (at <stdin>:259):
mount set`; the base Compose file renders 11 and the same filter exits 0.

| Card or Debug statement | What the code does (file:line at `5b713f68`) | Verdict |
|---|---|---|
| The pre-removal proof compares the uploaded script's roster (11 mounts) with the Compose file rendered on the host. | `c1008_compose_model` (`scripts/c590-remote.sh:3840`) renders `compose_host config --format json` (`:3842`), where `compose_host` (`:941`) passes `-f "$SERVER2_COMPOSE"` and `SERVER2_COMPOSE="$CHECKOUT/docker-compose.server2-runner.yml"` (`:897`). The expected roster is hard-coded in the filter (`:3855-3863`, token bind at `:3862`); a length mismatch is `error("mount set")` (`:3869`) and the caller refuses `RecycleComposeMismatch` (`:4538`). | Confirmed. |
| The checkout moves only after `c1008_recycle` returns. | `case_deploy_parent` (`:4711`) calls `c1008_recycle` at `:4714` and `ensure_checkout` at `:4717`. The generic pre-case `ensure_checkout` at `:5468` is skipped when `C1008_CONTEXT=default` (`:5466-5467`). The dry-run intercept (`:5440-5445`) runs `c1008_recycle` before any checkout as well. | Confirmed. |
| The empty token directory is created only after, by `ensure_runner_github_token_dir`. | `ensure_runner_github_token_dir` (`:1164-1197`) runs from `ensure_runner_boot_files` (`:1328`), called at `:4718`, after the recycle. The failing filter never stats the directory; `c1008_owned_mounts` (`:4331-4339`) does, and is reached only after the model passes. | Confirmed. Provisioning the directory cannot make the base pass. |
| Nothing changed on server2; no journal; `-ResumeRecycle` does not apply. | The refused `c1008_compose_model` call (`:4538`) precedes `c1008_lock` (`:4566`), the journal directory and the first `c1008_save` (`:4624-4625`), every `docker stop`/`rm` (`:4645-4664`) and every volume removal (`:4678-4691`). Live status at 2026-10-06T13:29Z: `server2` buildVersion `4358939ecd85d6e7ff0941f970879499cb930e3d`, draining, redirect `server2-temp`, counters 0; `server2-temp` `d985af05…` accepting with 1 session. | Confirmed. |
| The running container is the previous generation with 10 mounts. | `git diff 4358939e d985af05 -- docker-compose.server2-runner.yml` is empty, so the running `4358939e` container and the host's `d985af05` file declare the same 10 `session-runner` and 3 `state-init` mounts. `4358939e` has no `c1008_compose_model` at all (it predates CARD-1008), so no recycle journal of the running generation exists anywhere. | Confirmed, and it rules out "the previous recycle journal" as a roster source for this rollout. |
| The host's rendered Compose file is the previous deploy's. | The checkout is at whatever SHA last ran any case: `deploy-temp-runner` (`:4895-4900`) moved it to `d985af05`, not to main's `4358939e`. It coincides with the running generation's mounts today only because the two files are equal. | Card is incomplete: the working tree is not a record of the running generation (D-2). |
| Any mount add or remove deadlocks the rollout. | Adding fails as above. Removing a mount would pass `mount set` on the new file and then fail `error("mount count")` in `c1008_owned_mounts` (`:4343-4344`) as `RecycleContainerStateUnknown`, before any stop. | Confirmed in both directions. |
| Only `redeploy-old` is affected. | `case_retire_temp_containers` (`:5157`) and `case_retire_temp_runner` (`:5226`, through `c1008_recycle "$TEMP_PROJECT"`) call the same `c1008_compose_model` for temp. Temp runs `d985af05` (10 mounts plus the grok bind). `drain-temp` and `retire-temp` at the fixed SHA would refuse temp's exited containers the same way. | Card is incomplete: the temp lane needs the same fix (D-6). |
| The wrapper runs `deploy-parent` with the uploaded script. | `deploy-server2.ps1:1019-1046` runs `deploy-parent` when `buildVersion` differs from `-Sha`; `Assert-NoIncompleteRecycle` only on a same-SHA retry (`:1022`). `c590-real.ps1:472` uploads the desktop checkout's `scripts/c590-remote.sh`; `:497-502` export the `C1008_*` context. | Confirmed: the script that runs is the `-Sha` checkout's. |
| The post-recreate proof must stay strict. | `c1008_record_recreated` (`:4474-4494`) renders the model again (`:4486`) and runs `c1008_owned_mounts` on the recreated containers (`:4488`); it runs after `ensure_runner_boot_files`, so the token directory exists by then. | Confirmed; this proof keeps the new roster unchanged. |

Platform read at dispatch: `GET /api/runner-defaults` revision 2, global `server2`, no kind
defaults; `GET /api/session-runners`: `desktop` (windows, accepting), `server2` (linux, draining,
not accepting), `server2-temp` (linux, accepting, 1 occupied). No host is pinned by this plan.

## Decisions

### D-1: two generations, two proofs

The custody proof of the containers being stopped and removed uses the **previous generation's**
mount roster. The proof of the recreated containers (`c1008_record_recreated`) and the final
acceptance use the **target generation's** roster, which stays the hard-coded shipped contract in
`c1008_compose_model` including `bind($token;"/run/antiphon/github-token";false;false)`. Both
proofs keep the exact-length, unique-target, canonical-source, kind and read-only checks of
`c1008_owned_mounts`; nothing is relaxed in either direction.

Rejected: dropping or loosening the length check (a foreign mount would pass); accepting the
running container "as is" without a roster (same weakness); editing the host checkout or the
container by hand (the next statement, `c1008_owned_mounts`, would still refuse; and it is the
procedure the runbook forbids).

### D-2: the previous roster is the previous generation's Compose file from Git objects, keyed by the deployed SHA and cross-checked three ways

The previous generation is identified by `SOURCE_REVISION` in the deployed stack file
(`$SERVER2_ENV` for `antiphon-runner`, `$SERVER2_TEMP_ENV` for `antiphon-runner-temp`), which
only `case_deploy_parent` / `case_deploy_temp_runner` write, after their own `up` succeeded.
Before it is trusted it must agree with:

1. the live registration: `C1008_STATUS.buildVersion` (already fetched by `c1008_status_proof`)
   must equal it exactly (40 lowercase hex); and
2. the image the session-runner container actually runs:
   `docker image inspect -f '{{.Id}}' antiphon-server2/session-testing:<sha12>` must equal that
   container's `Image`. `retire_superseded_server2_images` (`:1044`) keeps the live main, temp
   and broker tags, so the running generation's tag is always present.

Its Compose files are materialised from Git objects, never from the working tree:
`git -C "$CHECKOUT" show <sha>:docker-compose.server2-runner.yml` (plus
`docker-compose.server2-runner.temp.yml` for temp) into `$CASE_DIR/compose/<sha12>/`, with one
`git fetch --filter=blob:none origin <sha>` retry when the object is absent. The target generation
is materialised the same way from `$SHA`, so the dry-run preview never depends on where the
checkout happens to be. `compose_host` and `compose_temp` honour `C1008_COMPOSE_DIR` (default
`$CHECKOUT`) for their `-f` paths and pass `--project-directory "$CHECKOUT"` so a render from the
evidence directory is identical to one from the checkout.

Any disagreement refuses before the rollout lock: `RecycleGenerationUnknown` when the previous
generation cannot be determined or rendered (stack file missing or not 40 hex, Git object
unavailable, Compose render failure, a bind target outside the kind table of D-3, a structural
defect in the render), `RecycleGenerationMismatch` when it is determined but the legs disagree
(status `buildVersion`, image identity). Both are new first tokens with the usual `key=value`
tail; neither writes a journal (they fire before `C1008_ACTIVE`).

When the project owns no `session-runner`/`state-init` container (a retired, absent temp; a host
with no runner), no custody proof is needed and the previous generation is not derived; the
journal records `previousSha: null`. That is the first-ever-deploy answer: plain `deploy-parent`
without `C1008_CONTEXT` never recycles, and a recycle with nothing to remove needs no previous
roster.

Rejected: a deploy-time roster receipt on the host (nothing exists for `4358939e`, so it cannot
bootstrap the live rollout, and it adds a stale-file failure mode with its own recovery); the
previous recycle journal (exists only when the running generation was itself created by a
scripted recycle, which `4358939e` was not); the working-tree Compose file (keyed to the last
case that ran, not to the running container; see ground truth). The journal already records
`composeDigest` of the target and gains `previousSha` and `previousComposeDigest`, so a later
operation can read what this one accepted.

### D-3: the previous roster is derived from its own render, with a script-carried bind-kind table

For the target generation the hard-coded roster proves the shipped contract against the host
file. For the previous generation the Git object *is* the contract, so the expected topology is
derived from the render itself: each `services[svc].volumes[]` entry becomes
`{kind:"volume", role, source: model.volumes[role].name, target, rw, nocopy}` or
`{kind:"bind", source, target, rw, file}`; each `secrets[]` entry becomes a read-only file bind
at `/run/secrets/<name>`; each `tmpfs` entry a tmpfs. The render must still satisfy: `volumes` is
an array with unique targets, no `configs`, every volume source declared, every bind source
absolute, the recycle targets (`work`, `runner-tmp`, `dind-data`, and `runner-state` for temp)
declared as `${project}_<role>` and the three caches declared external with their fixed names.

The bind kind (`file` versus directory, needed by the host `stat` leg of `c1008_owned_mounts`)
comes from a table in the script, `C1008_BIND_KINDS`, listing every bind target the script has
ever shipped: `/run/antiphon/claude-oauth-token` file, `/run/antiphon/gitconfig` file,
`/run/antiphon/github-token` directory, `/state/codex` directory, `/codex-home` directory,
`/state/grok` directory. A previous-generation bind whose target is not in the table refuses
`RecycleGenerationUnknown`. This is the generalisation rule for any future change:

- **Adding a mount**: add it to the target roster in `c1008_compose_model`, to `C1008_BIND_KINDS`
  (binds), to the `compose_host`/`compose_temp`/`stack.env` variable lists and to
  `ensure_runner_boot_files` (host path creation), in one commit with the Compose change. The
  previous generation is rendered from Git and needs nothing else.
- **Removing a mount**: remove it from the target roster and the Compose file, but keep its
  `C1008_BIND_KINDS` entry forever and keep exporting its variable (pointing at the old host path)
  for at least one generation, because the previous Compose file still interpolates it. The
  previous render then still succeeds and the removed mount is still proven on the old container.
- **Moving a host path** is a two-generation change (first add the new path, then remove the old);
  the previous render uses the current script's paths, so a single-step move would refuse
  `RecycleContainerStateUnknown` at the pre-removal proof. Documented, not solved here.

Rejected: running the previous generation's own `c590-remote.sh` to produce its roster (executes
old code on the host); a committed roster data file (bootstraps nothing for `4358939e`; the
Compose file is already that record).

### D-4: `ensure_checkout` and `ensure_runner_boot_files` run before the recycle in the apply path; the preview stays read-only

`case_deploy_parent` becomes: `ensure_checkout`, `ensure_runner_boot_files`, then
`c1008_recycle "$HOST_PROJECT"` and `c849_budget_gate`, then the rest unchanged. Both calls are
idempotent and host-lane only, and `case_deploy_temp_runner` already orders them before any
Docker action. Consequences: every bind source of the target generation exists before the first
proof that could stat one; the working tree is at `$SHA` before `compose_host up`; and the
`umask 077` that `ensure_runner_boot_files` sets now also covers the journal directory (the
journal file is already `chmod 0600`; the desktop reads receipts as the same user). Before
`compose_host up -d --no-build` the script compares the materialised target file with
`$SERVER2_COMPOSE` byte for byte (`cmp`) and refuses `RecycleComposeMismatch` if they differ, so
the file that was proven is the file that is deployed.

The dry-run intercept (`:5440-5445`) does **not** call `ensure_checkout`: the preview renders both
generations from Git objects, prints the generation line and the existing `C1008_PREVIEW` lines,
and still touches no checkout, secret, container or volume (the existing
`C1008_Recycle_dry_run_never_mutates` contract).

Rejected: rendering only the target from the working tree after `ensure_checkout` (leaves the
preview on a stale checkout, the bug in preview form); moving only `ensure_checkout` (satisfies
the proof but leaves a stat-before-create hazard for any future bind).

### D-5: journal fields and resume rules

The schema-1 journal gains `previousSha` (40 hex or null), `previousComposeDigest` (sha256 of the
previous model or null) and `generation` (`{statusBuildVersion, imageTag, imageId}`), written at
creation beside `composeDigest`. `c1008_recycle` prints one line in preview and apply:
`C1008_GENERATION project=<p> previous=<sha|none> target=<sha> previousMounts=<n> targetMounts=<n>`.

On `-ResumeRecycle`: a journal without `previousSha` refuses `RecycleResumeMismatch`. While owned
containers still exist (phase before `containersRemoved`) the freshly derived previous SHA must
equal the saved one. After `ownedRemoved=true` the stack file may already name the target (the
deploy rewrites it after the recycle), so `SOURCE_REVISION` must be either the saved
`previousSha` or `sourceSha`; anything else refuses `RecycleResumeMismatch`.
`c1008_reconcile_owned` receives the previous model, because the containers it reconciles are the
previous generation's. The recreating/verified resume branch (`:4571-4585`) is unchanged: it
compares the recorded replacement containers raw.

### D-6: the temp lane uses the same previous-generation proof

`case_retire_temp_containers` (CARD-0994) proves the exited temp containers against the previous
temp model: `SOURCE_REVISION` from `$SERVER2_TEMP_ENV`, cross-checked with the retired temp's
`buildVersion` and the exited session-runner's image tag, rendered with the temp overlay.
`c1008_recycle "$TEMP_PROJECT"` does the same when temp containers exist; for the retired, absent
temp it derives nothing (D-2). The receipt's `candidates[].Topology` therefore describes the
containers' own generation.

### D-7: refusal vocabulary and what does not weaken

New first tokens: `RecycleGenerationUnknown`, `RecycleGenerationMismatch`. Unchanged and still
reached in the same order: `RecycleComposeMismatch` (target render or declaration roster),
`RecycleContainerStateUnknown` (a mount outside the applicable roster, a non-canonical or
wrong-kind host source, a tmpfs defect), `RecycleVolumeInUse`, `RecycleVolumeIdentityMismatch`,
`RecycleResumeMismatch`, and every census, audit and routing refusal. A running container with a
foreign extra mount still refuses before `docker stop`; a replacement container without the
eleventh bind still refuses in `c1008_record_recreated` and the journal stays
`verificationPending`.

### D-8: rollback

A rollback through `redeploy-old` to an older SHA works when that SHA contains this fix: the
previous roster (rendered from the running SHA) has the mount, the target roster does not, the
target proof requires exactly the older set, and the unused host directory stays in place. A SHA
older than this fix runs the pre-fix script and deadlocks in the `mount count` direction, so it
is not a supported `redeploy-old` target; the accepting temp runner remains the rollback for the
live rollout, as the rolling runbook already states.

### D-9: lanes

All new and changed tests are host-lane Bash fixtures that need `bash`, `git`, `node`, `jq` and
`docker compose config` (no daemon), and `RollingVolumeRecycleDockerTests` needs the runner's
nested daemon. Omit `-Runner`; pass `-Platform Linux` for Code and Review dispatches of this card
and `-Platform Any` to clear it afterwards. The group names in the Checkpoints table name the lane.

## Slices

Each slice is one 30-60 minute Code task on this card's branch; commit and push after each.

### S1: previous-generation model and identity (45-60 minutes)

Files: `scripts/c590-remote.sh`, `scripts/fixtures/c1008-fake-docker.sh`,
`scripts/fixtures/c994-production-compose-model.mjs`,
`tests/Antiphon.Tests/Scripts/RollingProductionMountTests.cs`.

- `C1008_BIND_KINDS` table (D-3) and `c1008_bind_kind <target>` (prints `true`/`false`, exit 2
  when unknown).
- `c1008_compose_source <sha> <dir>`: materialise both Compose files from Git objects (D-2), one
  fetch retry, exit 2 on failure.
- `compose_host`/`compose_temp`: `-f "${C1008_COMPOSE_DIR:-$CHECKOUT}/docker-compose.server2-runner.yml"`
  (and the temp overlay), plus `--project-directory "$CHECKOUT"`.
- `c1008_compose_model [sha]` (default `$SHA`): materialise, render through
  `C1008_COMPOSE_DIR=<dir> compose_host config --format json`, then the existing hard-coded
  roster filter unchanged.
- `c1008_previous_generation <project> <owned-json>`: resolves `C1008_PREVIOUS_SHA` from the
  stack file, checks the status and image legs, prints the `generation` object; sets
  `C1008_PREVIOUS_SHA=''` when no owned session-runner/state-init exists.
- `c1008_previous_model <sha>`: materialise and render the previous generation, derive its
  `c994Topology` from the render (D-3), run the same declaration checks and emit the same shape
  `c1008_compose_model` emits so `c1008_owned_mounts` consumes either.
- Fixture: `c1008-fake-docker.sh` serves `state.models[project + '@' + basename(C1008_COMPOSE_DIR)]`
  when that key exists, else `state.models[project]`; `c994-production-compose-model.mjs` gains
  `materializePrevious(root, project, temp)` that renders the current file with the
  `/run/antiphon/github-token` bind removed (a synthetic previous generation rendered by real
  `docker compose config`).
- `RollingProductionMountTests`: extend the `Prove` harness with `generation` (`previous`) vectors
  that stub `c1008_previous_generation` and `c1008_compose_source`, and add
  `C1105_Previous_generation_roster_is_derived_from_its_compose` (V-1).

### S2: recycle wiring, ordering and the host fixture (45-60 minutes)

Files: `scripts/c590-remote.sh`, `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`
(`C1008HostFixture`), `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`.

- `c1008_recycle`: after `c1008_status_proof` and the owned census, call
  `c1008_previous_generation`, render `prev_model` when a previous SHA exists, write the D-5
  fields and print `C1008_GENERATION`; `c1008_owned_mounts "$owned" "$prev_model" …` at the
  pre-removal proof (`:4620`) and `c1008_reconcile_owned "$prev_model"` (`:4557`);
  `c1008_record_recreated` keeps the target model. Resume rules of D-5.
- `case_deploy_parent`: D-4 ordering and the `cmp` guard before `compose_host up`. The dry-run
  intercept is untouched apart from the shared code path rendering from Git objects.
- `C1008HostFixture`: `PreviousSha` (`'b'` x 40), `PreviousModel(project)` from
  `materializePrevious`, `Container(id, project, service, running, generation: "previous")`,
  `main.env`/`temp.env` with `SOURCE_REVISION`, statuses `buildVersion` set to the previous SHA
  (the host fixture overrides the shared `mainDrained`/`tempRetiredAbsent` vectors locally; the
  wrapper and real-Docker fixtures keep their own values), and a `c1008_compose_source` stub that
  copies nothing (the fake serves models by directory name).
- Tests: `C1105_Redeploy_accepts_previous_generation_and_requires_new_bind` (V-2),
  `C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout` (V-4),
  `C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle` (V-5, text pins).

### S3: refusal battery and the temp lane (45-60 minutes)

Files: `scripts/c590-remote.sh`, `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`,
`tests/Antiphon.Tests/Scripts/RetiredTempContainerHostTests.cs`.

- `case_retire_temp_containers` and the temp branch of `c1008_recycle` use the previous temp
  model (D-6).
- `C1105_Generation_identity_refuses_mismatch` (V-3): six refusals, each proving no stop, no
  `rm`, no volume removal and no journal. The edited-journal `previousSha` case is the
  CARD-1117 control that belongs here; the other S2 resume controls stay in their own methods.
- `C1105_Temp_cleanup_uses_previous_generation` (V-6).
- Folded from the S2 repair review: CP-10 runs the four CARD-1116/CARD-1117 methods; CP-7's
  count is 16; the vacuous `runners=0` image `jq` leg is deleted; `C1008HostFixture.Run` waits
  120s (measured deploy-parent Runs are 22-30s, the method passes at 47-53s, and 30s cancels
  under load 19-23).

### S4: documentation and the real-Docker comparison (30-45 minutes)

Files: `docs/docker-stack.md`, `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs`,
`scripts/fixtures/c1008-recycle-real-cases.mjs`, `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleDockerTests.cs`.

- `docs/docker-stack.md`: new `### Mount generations and the rolling proofs (CARD-1105)` under
  the CARD-1008 section with D-2, D-3's add/remove protocol, both refusal codes, the
  `C1008_GENERATION` line and the D-8 rollback statement; one sentence in gate 6 of the rolling
  table ("accepts the running container's own generation and requires the new one after
  recreate"). `Mount_generation_protocol_is_documented` (R-1) pins it.
- Real-Docker fixture: `fixture()` writes `main.env` with `SOURCE_REVISION` and sets
  `statuses.server2.buildVersion` to a previous SHA; the `compose` shim honours
  `C1008_COMPOSE_DIR`; `c1008_compose_source` is stubbed to copy the fixture's previous model;
  the `image inspect` shim keeps answering the fixture image id for `session-testing:*`. Two new
  changed-script cases: `C-previous-generation-main` (main created from the previous model,
  reclaimed, three removals, replacement carries `/run/antiphon/github-token` read-only) and
  `C-previous-generation-foreign-mount` (main with an extra bind, refused before stop, volumes
  retained). The count line becomes `cases=34 base=5 changed=29`; the test's regex and
  `rows.Count` assertions follow.

## Operator runbook for the live rollout after land

Preconditions: the fix is landed on `origin/master` at SHA `F`; the canonical desktop checkout is
at `F` (`git pull --rebase`, no half-reset worktree, no land pending); the production server's
`GET /api/version` SHA equals `F` (restart AppHost per the runbook if not); `server2` is still
drained toward `server2-temp` with zero counters and `server2-temp` is accepting `d985af05`.
Do not run `deploy-temp`, `retire-temp` before step 6, or any `-ResumeRecycle`.

1. `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha F -Phase check-census`.
   Expect `RECYCLE_PROJECT …` and two `RECYCLE_CENSUS` lines with `open=0 boundOpen=0 landPending=0`
   for `server2`; temp may show open work.
2. `… -Phase check-host-jq` (fresh receipt; a `HostJqMissing` is the only reason to provision).
3. `pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2` and `server2-temp`:
   old `draining=true redirectTo=server2-temp retireWhenIdle=false` with fresh zero counters;
   temp `acceptingNewWork=true buildVersion=d985af05…`.
4. Preview: `… -Phase redeploy-old -DryRun`. Expect
   `C1008_GENERATION project=antiphon-runner previous=4358939ecd85… target=F previousMounts=10 targetMounts=11`,
   three `C1008_PREVIEW remove=antiphon-runner_{work,runner-tmp,dind-data}` lines and the preserve
   line. Any `RecycleGeneration*` refusal is a stop: report the code and the receipt.
5. Apply: `… -Phase redeploy-old`. The host case moves the checkout to `F`, creates the empty
   `secrets/github-token` directory (`WARN GithubTokenAbsent` is expected and is not a stop),
   proves the running container against the `4358939e` roster, stops and removes it, recycles the
   three volumes, builds, seeds, starts, proves the replacement against the 11-mount roster, and
   prints `C1008_RECYCLE … outcome=reclaimed`. The wrapper then verifies caches, clears old's
   drain and waits for `acceptingNewWork=true` at `F`.
6. Gate 7 smoke on `server2`: the status/`docker exec` block from the runbook, a sanctioned Plan
   canary with `-Runner server2`, and
   `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both -Sha F`. On failure drain
   old toward temp (`runner-drain.ps1 drain -RunnerId server2 -RedirectTo server2-temp -Reason 'post-upgrade smoke failed'`)
   and report.
7. `… -Phase drain-temp -WaitIdleMinutes 240`: temp redirects to `server2` and retires when idle;
   its exited containers are removed by `retire-temp-containers`, which now proves them against
   the `d985af05` temp roster.
8. `… -Phase retire-temp`: recycles temp's four volumes (`C1008_RECYCLE … outcome=completed`).
9. Record both runner statuses, the `C1008_GENERATION` and `C1008_RECYCLE` lines and the receipts
   under `.antiphon/rolling-server2/<run-id>/`.

Human-only, never part of this rollout's autonomy: `scripts/refresh-server2-github-token.ps1`
(vault token, any time after step 5; the helper reads it live); releasing remaining `server2`
sessions at a four-hour drain cap (`runner-slots.ps1 release`); `-KillSessions`; Docker prune;
abandoning the rollout; project identity corrections (`-ProjectId`); any `-ResumeRecycle` after a
partial failure, which is an operator decision taken from the journal.

## Verification design

All rows run on native Linux (`C1008HostFixture.RequireNativeLinux`). CP-9 additionally needs the
runner's nested daemon (`/.dockerenv` and the daemon name equal to the hostname), which the
server2-class runners provide.

### Coverage

| ID | Class.Method | Behaviour proven |
|---|---|---|
| V-1 | `RollingProductionMountTests.C1105_Previous_generation_roster_is_derived_from_its_compose` | With a previous model lacking the token bind, a 10-mount container is accepted by the previous proof and refused by the target proof; a previous model with an unknown bind target, duplicate targets, a relative bind source, an undeclared volume source or a missing recycle-target volume refuses; a container carrying the eleventh mount against the previous model refuses (`mount count`); a container with a foreign extra bind refuses under either model. |
| V-2 | `RemoteScriptContractTests.C1105_Redeploy_accepts_previous_generation_and_requires_new_bind` | Full `deploy-parent` flow with previous-generation containers: exit 0, exactly three removals, `C1008_GENERATION … previousMounts=10 targetMounts=11`, journal `previousSha`/`previousComposeDigest` set and different from `composeDigest`, `recreated.owned[0].Topology.mounts` contains the read-only `/run/antiphon/github-token` bind. Negative half: the replacement built from the previous model refuses in `c1008_record_recreated` (exit 2, `RecycleContainerStateUnknown`), the journal stays short of `verified`, and a resume performs zero second removals. |
| V-3 | `RemoteScriptContractTests.C1105_Generation_identity_refuses_mismatch` | Six refusals, each with empty `Removed`, no `stop`/`rm`/`volume rm` in the trace and no journal file: stack `SOURCE_REVISION` differs from status `buildVersion` (`RecycleGenerationMismatch`); stack file missing or not 40 hex (`RecycleGenerationUnknown`); image tag id differs from the container image (`image-inspect-wrong` fault, `RecycleGenerationMismatch`); `c1008_compose_source` fails (`RecycleGenerationUnknown`); a running container with a foreign extra bind (`RecycleContainerStateUnknown`); a resumed journal whose `previousSha` was edited (`RecycleResumeMismatch`, no new deletion). |
| V-4 | `RemoteScriptContractTests.C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout` | Previous-generation fixture with `C1008_DRY_RUN=1`: exit 0, `C1008_GENERATION` and `C1008_PREVIEW` lines, no `MUTATION` marker, no mutating Docker call; the apply afterwards still rechecks changed facts. |
| V-5 | `RemoteScriptContractTests.C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle` | Text pins: `Order(case_deploy_parent, ensure_checkout, c1008_recycle)`, `Order(case_deploy_parent, ensure_runner_boot_files, c1008_recycle)`, the `cmp` guard precedes `compose_host up -d`, the dry-run intercept block contains no `ensure_checkout`, `C1008_BIND_KINDS` lists the six targets, `c1008_compose_model` still carries the token roster line, `compose_host`/`compose_temp` use `C1008_COMPOSE_DIR` and `--project-directory`. |
| V-6 | `RetiredTempContainerHostTests.C1105_Temp_cleanup_uses_previous_generation` | Exited temp containers built from the previous temp model (grok bind, no token bind): cleanup exit 0 and the receipt's session-runner `Topology.mounts` has 14 entries without `/run/antiphon/github-token`; with `temp.env` naming a different SHA the same containers refuse `RecycleGenerationMismatch` with no mutation. |
| V-7 | `RollingVolumeRecycleDockerTests.C1008_Real_docker_comparison` | Real Docker: `C-previous-generation-main` reclaims a main created from the previous model and the replacement mounts the token directory read-only; `C-previous-generation-foreign-mount` refuses before stop with volumes retained. Count line `cases=34 base=5 changed=29 failures=0`, owned residue absent. |
| R-1 | `DockerStackDocumentationTests.Mount_generation_protocol_is_documented` | docker-stack.md carries the CARD-1105 heading, both refusal codes, `SOURCE_REVISION`, `C1008_GENERATION`, the add/remove protocol sentences and the gate 6 sentence. |
| R-2 | `RollingProductionMountTests.C994_*` (5 methods) | The target proof is unchanged for valid and faulted topologies, including the existing `extra` foreign-bind refusal. |
| R-3 | `RemoteScriptContractTests.Deploy_parent_creates_the_github_token_directory_without_reading_it` and the other three deploy-parent text pins | Existing ordering pins (`ensure_runner_boot_files` before `seed_runner_checkout` and `compose_host up -d`) and the roster line survive the reordering. |
| R-4 | `RemoteScriptContractTests.C1008_*` (10) and `C1087_Host_census_filters_and_names_cause` | Every existing host-lane recycle contract passes on the extended fixture, including the dry-run, resume and partial-failure journeys. |
| R-5 | `RetiredTempContainerHostTests.C994_*` (20) | The temp cleanup contracts, including the 15-mount topology receipt for a same-generation temp, pass on the extended fixture. |
| R-6 | `RollingVolumeRecycleScriptTests.*` (16) | Wrapper vectors, transport pins, secret-leak checks and the fixture's own pins pass. |
| R-7 | `RollingVolumeRecycleDockerTests` existing 32 outcomes | Unchanged base and changed outcomes beside the two new cases. |

### Positive controls (SourceLanding Mutation, method-scoped, one per behaviour)

Run only the detecting filter with `--treenode-filter "/*/*/Class/Method"`; restore before the
next. Zero tests, a build error or a missing tool is not red.

| PC | Change to production behaviour | Detecting filter | Expected red |
|---|---|---|---|
| PC-1 | Pre-removal proof passes the target model instead of `prev_model` | `/*/*/RemoteScriptContractTests/C1105_Redeploy_accepts_previous_generation_and_requires_new_bind` | Exit 2 `RecycleContainerStateUnknown`, no removal. |
| PC-2 | `c1008_record_recreated` uses the previous model | same method | The replacement without the token bind is accepted; negative half fails. |
| PC-3 | Skip the status `buildVersion` leg | `/*/*/RemoteScriptContractTests/C1105_Generation_identity_refuses_mismatch` | The status-mismatch case reaches `docker stop`. |
| PC-4 | Skip the image identity leg | same method | The `image-inspect-wrong` case is accepted. |
| PC-5 | Unknown bind target defaults to directory | `/*/*/RollingProductionMountTests/C1105_Previous_generation_roster_is_derived_from_its_compose` | The unknown-target vector is accepted. |
| PC-6 | Previous model skips the unique-target check | same method | The duplicate-target vector is accepted. |
| PC-7 | Dry-run intercept calls `ensure_checkout` | `/*/*/RemoteScriptContractTests/C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout` | `MUTATION ensure_checkout` appears. |
| PC-8 | Move `ensure_checkout` back after `c1008_recycle` | `/*/*/RemoteScriptContractTests/C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle` | Order assertion fails. |
| PC-9 | `case_retire_temp_containers` renders the target model | `/*/*/RetiredTempContainerHostTests/C1105_Temp_cleanup_uses_previous_generation` | Exit 2 on the valid previous-generation containers. |
| PC-10 | Drop the `mount count` check in `c1008_owned_mounts` | `/*/*/RollingProductionMountTests/C994_Bind_sources_are_canonical_and_typed` | The `extra` variant is accepted. |
| PC-11 | Remove the CARD-1105 heading from docker-stack.md | `/*/*/DockerStackDocumentationTests/Mount_generation_protocol_is_documented` | Pin assertion fails. |
| PC-12 | Delete the early `cmp` block in `c1008_recycle` | `/*/*/RemoteScriptContractTests/C1105_Compose_cmp_refuses_before_removal` | Exit 0 or removal before the refusal. The order pin in `C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle` also fails. |
| PC-13 | In the `runners=0` resume branch, refuse unless every leftover image equals `generation.imageId` | `/*/*/RemoteScriptContractTests/C1105_Resume_state_init_image_follows_the_journal` | `RecycleGenerationMismatch` instead of `RecycleResumeMismatch`. `C1105_Resume_foreign_leftover_is_not_an_image_mismatch` likewise misses `RecycleContainerStateUnknown`. |
| PC-14 | Replace the session-runner image compare in `c1008_previous_generation` with `true` | `/*/*/RemoteScriptContractTests/C1105_Resume_identity_refusals` | The `runner` case no longer reports `RecycleGenerationMismatch`. |

### Cost

Checkpoint floor is the `EstimatedMinutes` sum, 94 minutes, plus authoring of roughly 165-225
minutes across S1-S4. One checkpoint run per committed slice group, each through the build-slot
gate with the exact committed SHA:

```powershell
$planPath = 'docs/superpowers/plans/2026-10-06-card-1105-compose-mount-generation-plan.md'
$sha = git rev-parse HEAD
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1105-cp1 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1105-tool/ -- run --plan $planPath --rows CP-1 --expected-source-sha $sha --row-timeout 15m --total-timeout 30m
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1105-cp2 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1105-tool/ -- run --plan $planPath --rows CP-2 --expected-source-sha $sha --row-timeout 15m --total-timeout 30m
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1105-cp3-7 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1105-tool/ -- run --plan $planPath --after S3 --expected-source-sha $sha --row-timeout 20m --total-timeout 120m
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1105-cp8-9 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1105-tool/ -- run --plan $planPath --rows CP-8,CP-9 --expected-source-sha $sha --row-timeout 20m --total-timeout 40m
```

If `run` exits 75, call `wait` until it does not; never edit source while a run is active; exit 4
from the gate is a slot timeout to report. The tool bootstrap build is the one explained unlisted
build. Delete every `bin-c1105-*` directory before finishing. No assertion or timeout is loosened
to pass; a red row is fixed and rerun as the same row. The Code brief points at this table as
`checkpoints: <this path>@<plan commit sha> section "### Checkpoints"`.

### Checkpoints

Closed Code list: one isolated build and one literal TUnit filter per row. Counts are from source
inspection at the base (`RollingProductionMountTests` 5, `RetiredTempContainerHostTests` 20,
`RollingVolumeRecycleScriptTests` 16, `DockerStackDocumentationTests` 13, `RemoteScriptContractTests`
C1008/C1087 12); recount at Code admission. Rows that reuse a build share its `After` group.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1105-a/` | generation-model-linux | `/*/*/RollingProductionMountTests/*` | V-1, R-2 | exact 6 methods (5 C994_* + C1105_Previous_generation_roster_is_derived_from_its_compose), 0 failed/skipped, native Linux | 6 | 6 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1105-b/` | script-text-pins-linux | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(Deploy_parent_creates_the_github_token_directory_without_reading_it*)\|(Deploy_parent_creates_the_codex_home_directory_without_reading_it*)\|(Deploy_parent_seeds_or_verifies_runner_checkout*)\|(Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner*)` | V-5, R-3 | exact 5 methods, 0 failed/skipped | 5 | 3 | true |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1105-c/` | generation-flow-linux | `/*/*/RemoteScriptContractTests/(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)\|(C1105_Generation_identity_refuses_mismatch*)\|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)\|(C1008_Recycle_exact_default_volumes*)\|(C1008_Recycle_dry_run_never_mutates*)` | V-2, V-3, V-4, R-4 | exact 5 methods, 0 failed/skipped, native Linux | 5 | 12 | true |
| CP-4 | S3 | CP-3 | host-census-long-linux | `/*/*/RemoteScriptContractTests/(C1008_Recycle_resume_requires_matching_receipt*)\|(C1008_Recycle_receipt_records_disk_and_partial_failure*)\|(C1008_Recycle_refuses_references_and_unknown_census*)` | R-4 | exact 3 methods, 0 failed/skipped, native Linux | 3 | 15 | true |
| CP-5 | S3 | CP-3 | host-census-rest-linux | `/*/*/RemoteScriptContractTests/(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)\|(C1008_Retire_temp_rechecks_absence_and_retirement*)\|(C1087_Host_census_filters_and_names_cause*)\|(C1008_Recycle_preserves_tmp_copyup*)\|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)\|(C1008_Recycle_audits_work_as_1654*)` | R-4 | exact 7 methods, 0 failed/skipped, native Linux | 7 | 9 | true |
| CP-6 | S3 | CP-3 | temp-cleanup-linux | `/*/*/RetiredTempContainerHostTests/*` | V-6, R-5 | exact 21 methods (20 C994_* + C1105_Temp_cleanup_uses_previous_generation), 0 failed/skipped, native Linux | 21 | 12 | true |
| CP-7 | S3 | CP-3 | wrapper-and-fixture-pins | `/*/*/RollingVolumeRecycleScriptTests/*` | R-6 | exact 16 methods, 0 failed/skipped | 16 | 14 | true |
| CP-10 | S3 | CP-3 | resume-and-compose-refusals-linux | `/*/*/RemoteScriptContractTests/(C1105_Compose_cmp_refuses_before_removal*)\|(C1105_Resume_identity_refusals*)\|(C1105_Resume_state_init_image_follows_the_journal*)\|(C1105_Resume_foreign_leftover_is_not_an_image_mismatch*)` | CARD-1116, CARD-1117 | exact 4 methods, 0 failed/skipped, native Linux | 4 | 8 | true |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c1105-d/` | docs-pins | `/*/*/DockerStackDocumentationTests/*` | R-1 | exact 14 methods (13 existing + Mount_generation_protocol_is_documented), 0 failed/skipped | 14 | 1 | true |
| CP-9 | S4 | CP-8 | real-docker-nested-linux | `/*/*/RollingVolumeRecycleDockerTests/C1008_Real_docker_comparison*` | V-7, R-7 | 1 result; `C1008_REAL cases=34 base=5 changed=29 failures=0`, owned residue absent, 0 failed/skipped; needs the runner's nested daemon | 1 | 14 | true |

## Risks and notes for Code and Review

- The `jq -e` filter in `c1008_compose_model` must stay byte-identical where
  `Deploy_parent_creates_the_github_token_directory_without_reading_it` pins it (`--arg token` and
  the `bind($token;…)` line inside the `c1008_compose_model` block). Keep the target roster in that
  function; put the previous derivation in `c1008_previous_model`.
- `C1008_COMPOSE_DIR=<dir> compose_host config …` is a temporary assignment on a function call;
  Bash propagates it to the function and its children for that call only, which is the intended
  scope. Do not `export` it.
- `git show` on server2's blob-less partial clone fetches a missing blob lazily; the commit for the
  running generation is present because `ensure_checkout` checked it out once. The one explicit
  fetch retry is for a target SHA the host has never seen (a dry-run before any apply).
- The shared vectors in `scripts/fixtures/c1008-recycle-cases.json` keep `buildVersion: "old"` for
  the wrapper fixture (the wrapper decides to run `deploy-parent` when `buildVersion` differs from
  `-Sha`). Only the host and real-Docker fixtures substitute a 40-hex previous SHA.
- Review runs `scripts/check-evidence-diff.ps1` over the task range and confirms every row's
  `CHECKPOINT` line is at the committed slice SHA with `sourceState=clean`.
- After land the rollout is operator work under the runbook above; the card stays open until the
  `redeploy-old` and `retire-temp` receipts exist and `server2` accepts at the landed SHA.
