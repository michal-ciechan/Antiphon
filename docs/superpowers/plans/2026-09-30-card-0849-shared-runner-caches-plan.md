# CARD-0849: shared runner download caches

Date: 2026-09-30. Stage: TestDesign finalized. Plan baseline: `71e5153c8e1eda4fa8992e95d8ec9d602fe8376e`. Static test census and design review: `9acb34f1b6cb1e5afc0e92106eb5804dcfebc674`.

## Outcome and scope

Persist the NuGet global packages directory and npm's package-content cache in explicitly named, external Docker volumes shared by `antiphon-runner` and `antiphon-runner-temp` on the server2 host. Give NuGet a third shared volume for its cross-process locks. Pre-seed from the existing temp container before recreating it. Gate admission after deployment on an actual net9 apphost build as uid 1654 and preserve the cache through temp retirement.

This is a deployment change, with no server API, database, session-runtime, provider-authentication, or project build-layout change. It does not fix CARD-0671's independent FakeClaude output-directory collision. Keep `UseAppHost=false` for ordinary Linux checkpoint builds; explicitly use `UseAppHost=true` in this card's isolated smoke fixture.

The test design is ready for Code under the frozen rosters below. Production activation remains a desktop-operated, drain-gated follow-up after Code and Review. TestDesign performed source inspection and a read-only card lookup only: no builds, tests, mutation runs, SSH/host operations, or live cache changes. Only this plan is changed.

## Evidence and limits

Read the full live cards with `pwsh -NoProfile -File scripts/card.ps1 get CARD-0849`, `get CARD-0827`, and `get CARD-0671`. CARD-0671 revision 6 distinguishes a cold apphost cache from the FakeClaude collision. CARD-0849 records the 08:10Z recreation, the NuGet decrease from about 2.6 GB to 1.4 GB, and the unchanged temp container's approximately 2.6 GB cache. The 9.0.20 Linux apphost package was downloaded again at 10:12Z; nuget.org was reachable. That is historical evidence from the cards, not a new measurement of temp.

Read-only measurements in the running **server2** container `652c9d50dda2`, uid/gid `1654:1654`, at approximately 10:26-10:30Z on 2026-09-30:

| Path | Allocated bytes (`du -s -B1`) | Decision |
|---|---:|---|
| `/home/app/.nuget/packages` | 1,441,288,192 | Share and persist; immediate correctness and download benefit. |
| `/home/app/.npm/_cacache` | 208,818,176 | Share and persist; about 199 MiB of reusable package data. Whole `.npm` was 209,297,408 bytes. |
| `/home/app/.local/share/NuGet/http-cache` | 577,568,768 | Leave local. Large but duplicates downloaded package data and short-lived feed responses; persistent global packages remove the principal repeat download. |
| `/home/app/.dotnet` | 647,168-667,648 | Leave local; changing telemetry/small runtime state, not a downloaded SDK installation. |
| `/home/app/.dotnet/tools` | absent | No shared tool installation. Repo-local tools restore packages into the selected NuGet volume. |
| `/home/app/.cache` | 114,688-163,840 | Leave local; observed immediate child was PowerShell cache. |
| `/home/app/.cache/ms-playwright`, `/ms-playwright`, `/opt/ms-playwright` | absent | No browser-download volume justified. |
| `/home/app/.local/share/NuGet/plugin-cache` | absent | Leave local; do not expand into plugin/authentication state. |
| `/home/app/.local/share/Microsoft/MSBuild`, `/home/app/.cache/msbuild`, `/tmp/MSBuildTempapp`, `/tmp/MSBuildTemproot` | absent | No MSBuild cache mount. Intermediate build state remains task-local. |
| `/tmp/NuGetScratchapp` | 118,784 | Replace its use by an explicit shared `NUGET_SCRATCH` mount for coordination, not download savings. |

`dotnet nuget locals all --list` resolved `http-cache` and `plugin-cache`, **not** the documentation's example spellings `v3-cache` and `plugins-cache`; those example paths were absent. `npm config get cache` returned `/home/app/.npm`. The host package metadata timestamp was `2026-09-30 10:12:44Z`. Packages were uid/gid 1654, mode 0755; `/tmp` was root-owned, mode 1777, on its own ext4 mount. `/home/app/.nuget/packages` remained on overlay. The backing filesystem had about 449.9 GB available at observation time.

**Inventory gap CLOSED.** The caller supplied a read-only collection made through trusted SSH on the server2 host at approximately **10:45Z on 2026-09-30**. These are measured values from that collection, not TestDesign host observations; the plan is self-contained and does not depend on the caller's `inv849.out` scratch file.

| Measured path/property | `antiphon-runner-session-runner-1` | `antiphon-runner-temp-session-runner-1` |
|---|---|---|
| NuGet packages, allocated B | 1,441,288,192 | **2,767,990,784**; donor contains `microsoft.netcore.app.host.linux-x64/9.0.20` |
| npm `_cacache`, allocated B | 208,818,176 | 208,826,368 |
| NuGet HTTP cache, allocated B | 577,568,768 | 655,466,496 |
| `.dotnet`, allocated B | 684,032 | 667,648 |
| `/tmp/NuGetScratchapp`, allocated B | 143,360 | 266,240 |
| Package owner / mode / backing | `1654:1654` / `0755` / overlay, no package volume | `1654:1654` / `0755` / overlay, no package volume |
| `/tmp` owner / mode / backing | `0:0` / `1777` / `antiphon-runner_runner-tmp` volume | `0:0` / `1777` / writable layer, **no `/tmp` volume** |

Both resolve global packages to `/home/app/.nuget/packages/`, HTTP cache to `/home/app/.local/share/NuGet/http-cache`, plugins cache to `/home/app/.local/share/NuGet/plugin-cache`, NuGet temp to `/tmp/NuGetScratchapp`, and npm cache to `/home/app/.npm`.

| Host-inspected container | Named volume mounts | Existing binds (identities only; no contents collected) |
|---|---|---|
| main | `antiphon-runner_dind-data` -> `/var/lib/docker`; `antiphon-runner_runner-tmp` -> `/tmp`; `antiphon-runner_runner-state` -> `/state`; `antiphon-runner_work` -> `/work` | gitconfig, claude-oauth-token, `/state/codex`, deploy key, phone-home secrets |
| temp | `antiphon-runner-temp_dind-data` -> `/var/lib/docker`; `antiphon-runner-temp_runner-state` -> `/state`; `antiphon-runner-temp_work` -> `/work` | `/state/codex`, `/state/grok`, and the same secret files and gitconfig as main |

The later measurement supersedes the earlier sizes for these paths. The earlier table's other presence/absence findings remain **main-only**. The donor is measured, but completeness, hashes and stable identity still require D-5 acceptance. CARD-0827 is active on main and still awaits temp recreation; pre-rollout inventory must accept and record temp's absent `/tmp` mount, while CP-4 must require two distinct `/tmp` volumes at 1777. No copy of the old `/tmp` is authorized.

The runner's Docker CLI reaches its nested daemon, not the host daemon. The Plan session's direct SSH attempt failed host-key verification; the trusted collection above resolves the evidence gap without changing that trust boundary. Do not disable host-key checks or use a privileged nested container to reach the host. CP-3 refreshes the allow-listed inventory through the trusted desktop lane before rollout; this is a freshness gate, not an outstanding investigation.

Live API status at approximately 10:30Z:

| Runner | Build version | Sessions / runnerSessions / queuedTasks | Admission |
|---|---|---|---|
| server2 | `d7456a2352d15391f37eca8781c16db499a3759d` | 4 / 4 / 0 | accepting, not draining |
| server2-temp | `a8b4e9e5a0715a644a21c4270dac8b4d60810d4a` | 1 / 1 / 0 | draining, not accepting, not yet retired |

These are observations, not reusable deployment authorization. Re-read status immediately before every destructive operation.

The following is the **future operator's** read-only inventory refresh from the trusted server2 host shell (no build slot needed; not run in TestDesign). Keep this allow-list; do not replace it with a home-directory walk or an environment dump:

```sh
for c in antiphon-runner-session-runner-1 antiphon-runner-temp-session-runner-1; do
  printf 'container=%s\n' "$c"
  docker inspect --format '{{range .Mounts}}{{println .Type .Name .Destination .RW}}{{end}}' "$c"
  docker exec -u 1654:1654 -e HOME=/home/app "$c" sh -c '
    dotnet nuget locals all --list
    npm config get cache
    for p in /home/app/.nuget/packages /home/app/.dotnet /home/app/.dotnet/tools \
      /home/app/.npm/_cacache /home/app/.cache /home/app/.cache/ms-playwright \
      /home/app/.local/share/NuGet/http-cache /home/app/.local/share/NuGet/v3-cache \
      /home/app/.local/share/NuGet/plugin-cache /home/app/.local/share/NuGet/plugins-cache \
      /home/app/.local/share/Microsoft/MSBuild /home/app/.cache/msbuild \
      /tmp/NuGetScratchapp /tmp/MSBuildTempapp /tmp/MSBuildTemproot /ms-playwright /opt/ms-playwright; do
      if [ -d "$p" ]; then du -s -B1 "$p"; else printf "absent %s\n" "$p"; fi
    done
    stat -c "%u:%g %a %n" /tmp /home/app/.nuget/packages
  '
done
```

## Design decisions

### D-1: external volumes, with narrow mounts

Use three host-daemon volumes with these fixed production names:

| Compose key | External Docker name | Runner destination | Environment |
|---|---|---|---|
| `runner-nuget-packages` | `antiphon-runner-cache-nuget-packages` | `/home/app/.nuget/packages` | `NUGET_PACKAGES=/home/app/.nuget/packages` |
| `runner-nuget-scratch` | `antiphon-runner-cache-nuget-scratch` | `/var/cache/antiphon/nuget-scratch` | `NUGET_SCRATCH=/var/cache/antiphon/nuget-scratch` |
| `runner-npm-content` | `antiphon-runner-cache-npm-content` | `/home/app/.npm/_cacache` | `NPM_CONFIG_CACHE=/home/app/.npm` |

Declare `external: true` and `name:` for each under the base Compose `volumes:`. Use long mount syntax with `volume.nocopy: true`; deployment owns initialization/seeding, not Docker's image-directory copy-up. Both projects inherit the same destinations and external names. The temp overlay needs only a comment describing that deliberate inheritance. Do not add cache mounts to the `build-slots` service, generic/nested stacks, or `state-init`.

A plain project-scoped named volume persists within one project but is neither shared across these two projects nor protected from its `down -v`. A host bind directory could meet both requirements, but adds host path/backup/permissions conventions without a measured need. External local volumes fit the existing Docker administration model. Docker documents that explicit `name` avoids project scoping, external volumes must already exist, and external volumes survive `compose down -v`. [Compose volumes](https://docs.docker.com/reference/compose-file/volumes/), [compose down](https://docs.docker.com/reference/cli/docker/compose/down/).

Scope sharing to this host, Linux architecture, and existing trusted runner account. Use the local driver on the host filesystem; no NFS, network cache, other host, or cross-user sharing. Validate volume driver, empty driver options, and ownership labels before adoption; a same-named foreign volume is a refusal, not permission to repurpose it.

### D-2: NuGet concurrent use requires shared scratch

Microsoft documents filesystem locking through the common NuGet scratch directory for parallel processes accessing global packages or the HTTP cache. Therefore sharing global packages is supported for this use **only with the shared scratch directory and identical absolute paths in all participants**. Separate project `obj` directories remain necessary because NuGet does not coordinate concurrent restores of the same project. The same document explains cache cleanup, completion metadata, and the short HTTP-cache lifetime. [NuGet cache management](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders).

Put the environment settings in Compose so the runner, inherited task processes, one-off Compose processes, and `docker exec` probes agree. Verify effective `dotnet nuget locals` output as uid 1654. Preserve `HOME=/home/app` explicitly in probes. Keep `TMPDIR=/tmp`; do not share `/tmp` to obtain NuGet locking. Never seed or clear scratch locks while any consumer is alive. Explicit task-level cache overrides must use a completely private package/scratch pair, or the shared pair; mixed pairs are unsupported.

This is a documented concurrency design, with real multi-container verification still required by V-4. Filesystem locking is not a guarantee that a package may safely be removed while a compiler is reading it.

### D-3: npm selection and excluded state

Share `_cacache` only. npm identifies it as its content-addressed package cache and verifies data on insertion/extraction; its cache library explicitly supports concurrent access and process races. [npm cache](https://docs.npmjs.com/cli/v11/commands/npm-cache/), [npm/cacache](https://github.com/npm/cacache). Keep `.npm/_logs`, `.npm/_npx`, update state, `.npmrc`, installed `node_modules`, and global CLI installations local. Never print cache keys, request metadata, registry credentials, or config files into evidence; evidence is sizes, paths, package versions, and pass/fail.

Do not mount all of `/home/app`, `.nuget`, `.dotnet`, `.cache`, or `.local/share`. In particular `.dotnet/corefx` is not treated as disposable download content. The SDK/runtimes under `/usr/share/dotnet` and pinned CLIs are already image layers; mounting user homes over them would not solve this incident.

Never add shared mounts or seed inputs for task `obj`/`bin`, `node_modules`, worktrees, `/work`, `/tmp`, `/state`, Codex/Claude/Grok homes, `.ssh`, NuGet/npm config, certificate/key stores, or credentials. Preserve each project's work/state/dind/tmp volumes. Existing credential mounts in the current deployment are outside this change; do not copy, extend, or reclassify them as caches. An auth-home isolation redesign is not part of CARD-0849.

### D-4: creation, ownership and readiness

Add host-lane helpers to `scripts/c590-remote.sh`, called by both `case_deploy_parent` and `case_deploy_temp_runner` before `seed_runner_checkout` or any runner `compose run/up`:

1. Acquire a host-local deployment/cache-maintenance `flock` under `/home/mc/antiphon-server2/locks/`; fail or wait with bounded, visible output on contention. Hold it across prepare/seed/compose verification; never unlink an active lock file.
2. Inspect/create only the three exact external names with labels `io.antiphon.owner=server2-runner`, `io.antiphon.cache-schema=1`, and the individual cache role. Production helpers reject arbitrary volume names or paths. Isolated fixtures use a separately validated `c849-<run-id>` namespace.
3. Initialize fresh roots using an already-built runner image with its entrypoint overridden, uid 0, no credentials, no provider state, no network, and only the cache volumes mounted. Set each root to `1654:1654`, mode `0700`. For existing valid volumes, assert owner/mode; do not recursively chown or chmod packages another runner may be using. Refuse foreign owners, symlinks, unexpected mounts, or malformed labels.
4. The first seed may normalize ownership and owner read/write/directory-search permissions in its **unpublished staging tree**. Preserve executable bits. Production data thereafter is written as uid 1654. Do not grant 0777 as a repair.
5. Verify write/create/rename/delete of a uniquely named probe as uid 1654 on each mount, removing only that probe. Record filtered mount destination/type/name/RW and root uid/gid/mode, never full `docker inspect` or environment dumps.

No Dockerfile or state-init change is required: host deployment prepares the external resources, and Compose consumes them. External resource validation runs even when the requested SHA is already deployed. Image build/reuse remains the existing deploy path.

### D-5: first seed before temp recreation

The current donor is `antiphon-runner-temp-session-runner-1`. Resolve its exact container ID from Compose labels, confirm runner identity/image, and hold off `retire-temp` and any temp recreation until seed acceptance is durable.

1. Wait for temp's existing drain to finish naturally. Require an HTTP 200 with non-null `sessions=0`, `runnerSessions=0`, `queuedTasks=0`, and its expected drain/retirement state. Missing/stale/unavailable status is not zero. Check for cache-using processes outside tracked sessions without dumping command lines. Stop only this now-idle temp container to stabilize its writable layer; do not remove it. Re-read status just before stopping. The busy main runner continues working with its old private cache.
2. Copy only `/home/app/.nuget/packages/.` and `/home/app/.npm/_cacache/.` from the stopped donor to a unique owner-only staging directory on the **same server2 host**. Never copy `/home/app`, provider homes, `.nuget/NuGet`, or scratch. Copying the stopped container is supported without re-launching its entrypoint. No archive traverses the desktop.
3. Validate entries before import: cache-relative paths only, no symlink escape, special files, or hard links escaping the tree. Admit only complete NuGet package/version directories with `.nupkg.metadata`; do not publish incomplete extractions. Require readable, nonempty `microsoft.netcore.app.host.linux-x64/9.0.20` host payload and metadata, and the net9 reference packages needed by the smoke. Refuse a missing donor/pack instead of silently starting cold. Run npm integrity verification only on the staged copy, with aggregate output.
4. Populate new, not-yet-consumed package/npm volumes. Initialize scratch empty, never import donor locks; the smoke may subsequently create its own lock files, so do not require an empty scratch after smoke. A seed marker is written only after import, ownership checks, and the cache-only apphost probe pass. Record donor ID/image, UTC time, byte counts and package-payload hashes. Never merge by overwriting an already-active shared cache. An interrupted unmarked seed is retryable only after proving no consumers; otherwise refuse for operator recovery.
5. Keep the stopped donor until the seed probe passes and a bounded recovery copy of the allow-listed seed is saved. Before the explicit Seed case returns, restart **that same container ID and old image**, without recreation, still under its unchanged durable drain; wait for HTTP 200, available/dispatch-eligible but not accepting, and fresh non-null zero counters. This closes the stopped-donor gap: `PhoneHomeRunnerDirectory.Status` derives `runnerSessions` from `live?.ListedNonExited`, so offline/absent status cannot substitute for zero. A restart/reconnect failure leaves activation pending and the drain intact. The later `deploy-temp` seed call recognizes the accepted marker and does not stop/copy again; it still validates identity and readiness. Compose may replace the donor only after another fresh idle check. Retain the recovery copy through the rollout/rollback window. If the donor disappears before seeding, stop activation; select and verify another cache donor explicitly or perform a connected warm-up as a changed operational procedure. Do not claim the requested first build avoided downloads.

Retain net9 host/reference packages from the donor; copying only the host executable is insufficient for a fresh restore. A donor seed does not guarantee every future dependency/version is cached.

### D-6: size and cleanup policy

Initial operating budgets are 10 GiB for NuGet packages, 2 GiB for npm content, and 256 MiB for scratch. Warn at 80%; require maintenance before another rollout when any budget is exceeded or backing filesystem free space is below 20 GiB. Record `du -s -B1` at every deploy, before retirement, and in the existing operator maintenance review at least weekly. These are operational budgets, **not** fictitious Docker local-volume hard quotas; a long interval with no maintenance can exceed them. Do not add a scheduler or imply automatic eviction.

Document `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case PrunePreview`, then `-Case Prune -Preview <receipt-path>` through the same host helper. The preview records exact volume identities, candidates, bytes and consumer status; apply revalidates them and refuses a stale receipt. Destructive cleanup requires both runners drained with all three counts zero, no cache consumers, and no active build-slot leases. Hold the same maintenance lock. Never run `docker system prune`, `docker volume prune`, a wildcard `/tmp` sweep, or `dotnet nuget locals all --clear`.

For deterministic bounding, rebuild an over-budget cache during this maintenance window: preserve a validated recovery copy; clear **only the selected volume's validated child entries** as uid 1654; restore the current deployed source's required packages and net9 smoke into the package volume, and refill npm from the current lockfile if needed. Keep all three mount roots in place; do not invoke a cache-clean command that tries to remove a mounted root. The helper must bound removal to the validated mount, reject unexpected nested mounts/escaping links and empty targets, and use the fixed package/scratch environment for refill. Never use mtime of extracted DLLs as last-use evidence. For selective removal, remove `.nupkg.metadata` first and then the complete package-version directory while all consumers are stopped. Scratch contents may be cleared only in that same fully idle window; keep the mounted root and 0700 ownership.

Connected refill is an intentional maintenance cost, uses the build-slot wrapper, and must finish with the disconnected-source smoke before admission resumes. If the freshly required working set exceeds the budget, leave the runners drained and request a measured budget increase; repeatedly deleting required packages is not cleanup. Retain at most one recovery generation for seven days after successful activation, then delete that exact recorded generation during maintenance. Include recovery bytes in disk headroom checks. Rollback/cache deletion is never delegated to generic image retirement.

## Existing deployment wiring and required changes

`scripts/deploy-server2.ps1 -Rolling` orchestrates `deploy-temp`, `drain-old`, `redeploy-old`, `drain-temp`, and `retire-temp` from the desktop. Its host cases go through `scripts/verify-docker-stack.ps1`, `scripts/c590-real.ps1` (trusted SSH), and `scripts/c590-remote.sh`.

`compose_host` uses `docker-compose.server2-runner.yml`, project `antiphon-runner`. `compose_temp` merges that file with `docker-compose.server2-runner.temp.yml`, uses project `antiphon-runner-temp`, and reads `stack-temp.env`. Both run on the host Docker daemon. `case_retire_temp_runner` currently executes `compose_temp down -v`; retain that behavior for temp work/state/dind/tmp and prove the three external volumes remain.

The current rolling wrapper can skip deployment by SHA alone and un-drains main after eligibility. Add an explicit cache verification host case before either runner is admitted, including the same-SHA path. Do not let a healthy/registered runner substitute for correct mounts or a working apphost. For a drained/retired temp, verify it first, then clear its drain to reactivate it; the existing wrapper currently has no such temp-clear step. If the wrapper itself starts with an existing accepting same-SHA runner, run the non-disruptive verification without recreation; a failed verification stops the phase and must not drain the main runner.

Fresh/recreated temp must remain held against dispatch until verification completes. Establish a durable temp drain before startup (with no conflicting redirect; preserve an existing valid drain), then wait for dispatch eligibility/version, verify, and clear. Main must remain drained throughout `redeploy-old`. Require all three non-null zero counters immediately before recreation, even for reruns. Refuse conflicting drain ownership/state rather than rewriting it. A failure leaves the affected runner held; it does not stop other sessions.

### Activation sequence (desktop operator, after Code and Review)

Use the canonical desktop checkout and trusted `mc@server2` SSH route. Pin one reviewed, landed full SHA for all phases. Do not run these from the task mirror or run `-Phase all` for the first migration: evidence is reviewed between phases. The cache helpers and new verification cases named here are implementation deliverables, not commands present at this Plan commit.

1. Run CP-3's inventory and isolated fixture proof. Refresh both statuses, cache sizes and free space. Preserve old runner/state-init image IDs using `antiphon-server2-rollback/...` tags and copy the non-secret Compose/configuration version references. The existing `retire_superseded_server2_images` removes superseded normal tags, so relying on the old normal tag is insufficient. Record the shared broker's pinned image and leave it unchanged.
2. Wait for temp's current one session to settle and all three counts to reach zero. Run `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed` to perform D-5 while main's live Code tasks continue. This explicit seed case rechecks drain/zero counters itself. Do not call `retire-temp` merely because temp became retired: its writable-layer cache is the donor. Hold temp admission before any startup.
3. `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <landed-sha> -Phase deploy-temp`. Updated host code initializes/seeds before `compose_temp up`, verifies actual mounts and apphost, then the wrapper clears temp drain. Require the exact build version, dispatch eligibility and accepting state. Record temp `/tmp` mount and 1777 mode here.
4. `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <landed-sha> -Phase drain-old`. New unlaunched work redirects to temp; running main tasks finish in place. Wait for main `sessions=0`, `runnerSessions=0`, `queuedTasks=0`. A timeout changes no running process and leaves the drain in place.
5. `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <landed-sha> -Phase redeploy-old`. Recheck zero counters and drain; recreate main with the same external caches. Verify uid 1654 write access, effective cache paths, package payload and smoke before clearing its drain. Main becoming healthy is not sufficient. Record main `/tmp` mount and mode.
6. Run CP-4's two-runner acceptance while temp still exists: identical three external volume identities, different `/tmp`/work/state/dind sources, both `/tmp` modes 1777, both smoke receipts, exact deployed SHA, and retained seed payload. Then `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <landed-sha> -Phase drain-temp`; wait for retirement and all three counters zero.
7. `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <landed-sha> -Phase retire-temp`. `compose_temp down -v` removes its private volumes including `antiphon-runner-temp_runner-tmp`. Run CP-5 to verify all external caches and the main `runner-tmp` remain, main still builds the smoke, and temp resources are gone. Keep recovery artifacts until the rollback window expires.

One rollout activates both CARD-0849 and CARD-0827 acceptance. Do not reimplement `/tmp` persistence or copy the old writable-layer `/tmp`. In particular, never sweep `/tmp/antiphon-pty-hosts` or infer permission to stop sessions from stale files.

### Rollback

Before main has moved, keep main serving on its old image/private cache; leave a failing temp held and restore its stopped donor/container or saved image/config only after zero counters. After main has moved, use the verified temp as the redirect target, drain main normally, and redeploy the retained previous image/config at zero. Keep the shared cache mounts **and** shared scratch environment together in a compatibility Compose override where possible; old .NET/npm clients can continue using the same package data. Re-run smoke before clearing drain.

If cache contents or the mount design itself are suspect, drain every consumer before detaching the shared mounts. Give each rolled-back container a private package/scratch pair pre-seeded from the recovery copy; do not keep shared packages with private scratch. Never delete the external volumes as rollback. Preserve them for diagnosis, restore the prior broker pin, and verify runner SHA, mounts, `/tmp` mode, and admission. No forced task stop or live cache replacement is a rollback shortcut.

## Exact implementation footprint and slices

Plan and TestDesign change only this file. The following is the bounded Code footprint; the frozen tests below do not expand runtime scope.

| Slice | Files | Change |
|---|---|---|
| S1 | `docker-compose.server2-runner.yml`; `docker-compose.server2-runner.temp.yml` | Three external cache definitions, narrow nocopy mounts, environment, and temp inheritance comment. |
| S1 | `scripts/c590-remote.sh` | Host-only creation/validation, first seed, cache inventory, smoke, isolated fixture, retirement verification and maintenance helpers; invoke prepare before every runner compose launch. Keep cache operations out of nested cases. Route the new cases through narrow evidence initialization: the current global `ensure_dirs` recursively changes ownership under `/work` and the server2 root and must not run over cache staging/recovery or for Inventory/Fixture/PrunePreview. |
| S1 | `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs`; `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` | Update the exact Compose-key contract; add cache definitions/mount isolation and executable helper behavior guards using existing fake-command seams. |
| S2 | `scripts/c590-real.ps1`; `scripts/verify-docker-stack.ps1` | Route new explicit `runner-cache-inventory`, `runner-cache-fixture`, `runner-cache-seed`, `verify-runner-caches`, `verify-runner-caches-retired`, `runner-cache-prune-preview`, and `runner-cache-prune` cases through the host lane; forward validated runner/case fields only; scrub aggregate evidence. |
| S2 | `scripts/deploy-server2.ps1` | Seed-before-recreate integration, held temp startup/reactivation, explicit verification before clear, same-SHA verification, non-null zero-counter checks. |
| S2 | `scripts/test-deploy-server2.ps1`; `scripts/fixtures/c727-fake-http.ps1`; `scripts/fixtures/c727-fake-verify.ps1` | Execute rolling gate/order/refusal scenarios; preserve existing T-1 through T-6 coverage. |
| S2 | `scripts/verify-card0849-caches.ps1` (new) | Small desktop front door: `-Case Inventory`, `Fixture`, `Seed`, `Both`, `Retired`, `PrunePreview`, `Prune`; validated `-Sha` or required `C849_DEPLOY_SHA`; creates manifests and calls the existing bridge. Only explicit Seed/Prune cases mutate production caches. It does not start a rollout implicitly. |
| S3 | `docs/docker-stack.md`; `docs/bootstrap.md` | Own the mount, lock, seed, smoke, prune, rollback and rolling runbook in docker-stack; bootstrap links it and warns that old containers lose cache on recreation. |

No changes to generated `docs/cards/`, provider homes, `Directory.Build.*`, production credentials, or `docker-compose.dev.yml`. Existing shell line-ending policy already covers `scripts/*.sh`. PowerShell/deploy changes remain ASCII. Commit and push each slice; run the combined repo checkpoints once after S1-S3, then publish their receipts. Host activation follows Review and requires its own operational evidence.

## Verification design

### Required outcomes and positive controls

| ID | Required observation | Positive control (isolated resources only) |
|---|---|---|
| V-1 | Render both actual Compose models with harmless fixture interpolation; same three external names, correct environment/nocopy/destinations; project-private tmp/work/state/dind. | Remove `external` or change the temp cache name in a fixture copy; identity/retention assertion fails. Remove scratch env; effective-path check fails. |
| V-2 | Init twice is idempotent; uid 1654 can create/rename/remove its probe; foreign owner, symlink, driver options, labels, or nonempty unmarked seed refuse before compose. | Wrong owner/mode and a symlink fixture fail without modifying a sibling sentinel. No real credential fixtures. |
| V-3 | Seed imports complete package versions from the stopped idle donor, preserves executable bits, and initializes scratch without donor locks; repeat does not overwrite an active cache. | Donor busy/null count, absent host pack, incomplete metadata, traversal/symlink, interrupted seed and wrong donor identity all refuse before recreation. |
| V-4 | Two disposable projects simultaneously restore different fixture projects using one shared package/scratch pair; both complete and a third offline restore reads the result. | A lock-holder probe using the installed NuGet locking primitive blocks a second process until release; isolated different scratch paths must fail this coordination assertion. Stress success alone cannot prove locking. |
| V-5 | Fresh tiny net9 apphost restore/build and executable run succeed as uid 1654 on temp, recreated main and main after retirement. | Use a private empty NuGet folder and empty package source; restore must fail while the normal seeded case succeeds. Never rename/delete the live host package to make the control red. |
| V-6 | Second container consumes npm package data written by the first with `npm ci --offline --ignore-scripts --no-audit --no-fund` in a distinct throwaway project. | Empty private `_cacache` fails offline. Do not run verify/GC against the production cache while consumers are active. |
| V-7 | Rolling trace orders seed/held startup/verify/clear; each busy or unknown counter prevents replacement; same SHA still verifies; a smoke failure prevents main clear and prevents draining main in deploy-temp. | Force verification exit nonzero or one nonzero/null counter in each fixture; assert no forbidden host case or clear request. Preserve old runner sessions. |
| V-8 | After rollout, both actual runners have distinct `runner-tmp` volume sources at `/tmp`, mode 1777; cache names match each other. Temp `down -v` removes only its project volumes; all three external caches remain readable by main. | A separate disposable negative fixture removes all consumers before proving that a non-external cache is lost on `down -v`; changing both `/tmp` sources to one volume fails isolation. |
| V-9 | Size preview is read-only; pruning requires all consumers idle, bounds traversal, retains mount roots and checks refill plus smoke before admission. Rollback retains cache and recoverable image/config references. | Busy consumer, wrong volume label, empty path, budget below required set and missing recovery image all refuse without touching foreign sentinel/production resources. |

### Static census and frozen repo selection

The census at `9acb34f1b6cb1e5afc0e92106eb5804dcfebc674` read both complete class files, counted `[Test]` methods including same-line declarations, checked data/repeat attributes and class-name collisions, and parsed the PowerShell harness AST without executing it. Neither class has argument/data-source/repeat expansion, inheritance, or a second class matching its prefix. These are **source counts**, not claimed test passes.

| Repo selection | Existing census | Frozen additions | Expected after Code |
|---|---:|---:|---:|
| `Antiphon.Tests.Infrastructure.DockerStackContractTests` in `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs` | 110 distinct `[Test]` methods | R-01, R-02: 2 methods | 112 TUnit executions |
| `Antiphon.Tests.Scripts.RemoteScriptContractTests` in `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` | 28 distinct `[Test]` methods | R-03 through R-12: 10 methods | 38 TUnit executions |
| CP-1 total | 138 | 12 | **150**, 0 failed/skipped |
| `scripts/test-deploy-server2.ps1` | 6 named groups T-1..T-6; 5 `Run-C727` call sites expand to 6 child invocations; 17 `Assert-C727` call sites expand to **19** assertions (T-2 loops twice); T-6 reuses T-1 | Extend T-1/T-2 for the gates; add T-7..T-16 below; post-retire T-9 adds one leftover-container refusal and T-12 adds live garbage counters | **16 groups, 46 child invocations, 149 labeled assertions**, 0 failures; **0 TUnit executions** |

CP-1's only filter is `/*/*/(DockerStackContractTests*)|(RemoteScriptContractTests*)/*`. Each OR operand has its own parentheses and suffix wildcard, as required by the pinned TUnit source-generated discovery (CARD-0403). The table escapes the pipe for Markdown only. Require a fresh TRX with exactly the two named classes and all 150 methods; an unrelated suffix match, zero execution, fixture error, or skip is a failure. Do not add a broad Unit run. Retain all 138 existing methods; update `Server2_file_defines_runner_state_init_and_profiled_broker` for the three new volume keys without adding another execution. If landed changes alter this census, update the plan and explain the new source counts before running; do not lower the floor to fit a result.

### CP-1: twelve added methods, with executable outcomes

Use the existing `RemoteScriptContractTests.LinuxShell` seam: extract and execute the **production** helper bodies, substitute only Docker/HTTP/sudo/ownership observation at the boundary, and give them real temporary trees and trace files. Tests must assert the resulting files, modes, sentinels, exit codes and command order. Do not duplicate the decision logic in a fake. Ownership checks that need another UID are boundary simulations here; real uid-1654 access is CP-3. Add the assembly-local `[ParallelLimiter<ProcessSpawnLimit>]` to each new method starting a process. Linux is the primary repo lane; Windows must supply WSL. A WSL absence/skip cannot satisfy CP-1.

Each row is one new unparameterized `[Test]` with the exact method name below; variants inside it remain one TUnit execution. The input perturbations are part of that method, not extra builds or a production source mutation pass.

| ID / class | Exact method | Fixed assertions and isolated red control |
|---|---|---|
| R-01 / DockerStackContractTests | `C849_Cache_volumes_are_external_narrow_and_nocopy` | Read the actual base Compose model: exactly the three D-1 keys/names, `external: true`, destinations, `nocopy: true`, package/scratch/npm environment and unchanged `TMPDIR`. Validate fixture copies with each external/name/env/nocopy value removed or changed; the same validator must reject the specific field. Actual Docker rendering is F-1, not a home-grown merge claimed as Docker evidence. |
| R-02 / DockerStackContractTests | `C849_Temp_inherits_caches_and_keeps_private_state` | Base + temp declarations preserve inherited caches and project-scoped tmp/work/state/dind. No cache additions in state-init, broker, base/nested/development stacks or excluded homes. Reject fixture overlay overrides of cache identity, broad home mounts, or a shared tmp name. Preserve the existing `/state/grok` bind as out-of-scope auth state. |
| R-03 / RemoteScriptContractTests | `C849_Cache_cases_use_only_the_validated_host_lane` | All seven new bridge cases route to host handlers; execute wrong-lane and invalid name/path/runner/SHA cases with fake commands and assert refusal before Docker, SSH or a directory write. Exercise the front door's case mapping and explicit `-Sha`/`C849_DEPLOY_SHA` precedence with fake transport; invalid input cannot fall back to a live bridge. Inventory/Fixture/PrunePreview must not call general `ensure_dirs` or production cache creation/seed/stop/prune (Fixture acts only on its validated namespace). |
| R-04 / RemoteScriptContractTests | `C849_Cache_prepare_is_idempotent_and_preserves_payloads` | Execute prepare twice over fresh labeled roots. First prepares roots; second validates without recursive chmod/chown, preserves payload bytes/executable bits and a sibling sentinel; probe creates, renames and deletes only its own file. Test all three roles. |
| R-05 / RemoteScriptContractTests | `C849_Cache_prepare_refuses_foreign_or_unsafe_roots` | Separately supply wrong owner, wrong mode, symlink, regular file root, driver != local, nonempty driver options, wrong/missing owner/schema/role labels and nonempty unmarked content. Each refuses before compose/repair and leaves target/sibling bytes and modes unchanged. |
| R-06 / RemoteScriptContractTests | `C849_Seed_publishes_complete_payloads_before_its_marker` | Real temp package trees include complete 9.0.20 host/ref packages and npm content. Assert stopped-donor copy allow-list, file hashes and executable mode, no imported scratch, smoke-success-before-marker and recovery copy before donor removal. After seed, restart only the same donor ID under its existing drain and wait for fresh zero counters; simulate offline nulls until reconnect and reject a reconnect timeout. Execute the wrapper with retired, unavailable, non-eligible status and null runnerSessions: donorless reuse succeeds, while a leftover temp container is refused by the host guard. A ready-marker rerun never overwrites active data or stops/copies again. Record an initially empty scratch, not empty-after-smoke. |
| R-07 / RemoteScriptContractTests | `C849_Seed_refuses_invalid_donors_and_partial_payloads` | Isolate each fault: wrong donor ID/Compose identity, each busy or null counter, missing host payload, empty metadata, incomplete required version, absolute/traversal path, escaping symlink/hardlink, special file, interrupted unmarked import with a consumer, failing staged npm integrity, failing smoke. Refuse before publication/recreation; no ready marker; stopped donor/recovery/sibling sentinel retained. An unrelated incomplete version may be excluded, but must never be published as complete. |
| R-08 / RemoteScriptContractTests | `C849_Deploy_prepares_and_verifies_before_acceptance` | Execute both real host deployment function bodies with a command trace: prepare/accepted seed before checkout-seeding or runner compose run/up; mount/smoke verification before acceptance/image retirement; same-SHA path still validates. Force prepare and smoke failures separately; no later forbidden operation. Execute a retirement body with absent/unknown/busy status and assert no `down -v`. |
| R-09 / RemoteScriptContractTests | `C849_Prune_preview_is_read_only_and_bounded` | With exact labeled fixture volume identities and known byte sizes, preview emits candidates/bytes/status without create, stop, chmod, clear, or admission writes. Check below/at/above 80% warning, budget, and 20-GiB free-space boundaries; include recovery bytes. A tree/path outside the selected mount and a sibling sentinel remain untouched. |
| R-10 / RemoteScriptContractTests | `C849_Prune_refuses_stale_or_busy_authority` | Each runner with any busy/missing/null counter, a live cache consumer, active build lease, stale preview, replaced volume, wrong label, symlink or empty/out-of-root path refuses before clear; no deleted sentinel or drain-clear request. Revalidate under the maintenance lock, not only while producing preview. |
| R-11 / RemoteScriptContractTests | `C849_Prune_and_rollback_retain_roots_and_recovery` | Execute idle fixture cleanup/refill with stubbed package tools and real trees. Preserve roots/0700 ownership and exactly one bounded recovery generation; verify refill/smoke before readiness. Required-set-over-budget, refill/smoke failure or missing recovery image leaves held state. Validate recovery references and the rollback package/scratch pair; the runbook rollback remains operator-driven, not a new automatic rollback service. Never delete external volumes or stop a busy session. |
| R-12 / RemoteScriptContractTests | `C849_Cache_receipts_exclude_credentials_and_payloads` | Execute new receipt generation/scrubbing with fake metadata containing token/config/content sentinels. Retain allow-listed aggregate identities/sizes/diagnoses/hashes; reject leakage in every produced receipt. Assert no full inspect/env output, cache archives, provider-home enumeration or auth/config copy enters the evidence directory. |

### CP-2: frozen rolling trace roster

Execute the real `deploy-server2.ps1` through the existing offline `C727_TEST_HTTP_STUB` and `C727_TEST_VERIFY_STUB` seams. Both stubs remain mandatory for the harness and must refuse unknown cases instead of falling through to real network/host work. Extend fake status with explicit null/omitted fields, configured-but-not-connected HTTP 200, error status, target runner, SHA and drain-clear transitions. Trace host calls with their validated manifest fields; successful fake cache checks prove **wrapper order only**, while R-08/F-3/F-5 prove helper/cache behavior. The sentinel token is synthetic.

The successful all-phase trace has host cases `runner-cache-seed(temp) -> deploy-temp-runner -> verify-runner-caches(temp) -> deploy-parent -> verify-runner-caches(main) -> retire-temp-runner`. The seed case is idempotent when the operator already seeded. HTTP writes are temp hold -> temp clear -> main drain/redirect-to-temp -> main clear -> temp drain/retire-and-redirect-to-main. The hold precedes seed/start; each clear follows the corresponding successful verification. T-8's fresh startup means the new replacement container after an existing donor's live HTTP-200 zero-count status and accepted seed. A never-connected configured runner normally has unknown counters and must refuse; do not teach the fixture that offline means zero. A 404 never authorizes creation.

Each semicolon-separated assertion below is one labeled `Assert-C727` check per variant. Count outcomes and forbidden actions even on failure paths. T-1/T-2 change their old pre-cache ordering expectations; their original eligibility, drain-body and refusal coverage is retained.

| Group | Fixed variants / child invocations | Assertions per variant | Required assertions |
|---|---:|---:|---|
| T-1 happy all phases | 1 | 8 | exit 0; exact six host-case order/targets/SHA; exactly five POSTs; temp hold body and before-seed/start order; temp clear after verify and accepting status; main drain body redirects temp without retirement; main clear after verify; temp final drain body redirects main with retirement. |
| T-2 missing/ineligible temp | 2 | 3 | exit 2 with missing-status or eligibility diagnosis; 404 performs no host call, post-start ineligible never verifies/clears; neither drains main (404 has no POST at all). |
| T-3 main sessions busy, drain-old | 1 | 3 | `OldRunnerStillBusy`, exit 2; no deploy-parent; exactly one drain POST remains held. |
| T-4 drain-temp rerun | 1 | 2 | exit 0; no duplicate POST. |
| T-5 missing operator token | 1 | 2 | `OperatorTokenMissing`, exit 2; no HTTP or host call. |
| T-6 output custody | 0, reuses T-1 | 2 total | synthetic token absent from captured stdout/stderr and receipts; all touched deploy/front-door/fixture PowerShell files ASCII. |
| T-7 busy temp seed | 3: one of sessions/runnerSessions/queuedTasks = 1 | 3 | busy diagnosis, exit 2; no seed/deploy/retire host case; no drain clear or main drain. |
| T-8 held fresh temp | 1 | 5 | exit 0 and final accepting/version; durable hold before seed/start; correct target/SHA verify before exactly one clear; observed held status until successful verify; no main POST. |
| T-9 retired temp reactivation | 2, old SHA and valid existing retire drain; null runnerSessions and unavailable/non-eligible status in both; no temp container for success, leftover container for refusal | 5 success, 3 refusal | Success exits 0 with existing hold preserved until verify, seed before replacement, verify before one clear, final accepting/new SHA and no main POST. Leftover container fails at Seed before replacement or clear. |
| T-10 same-SHA verification | 2: held idle main; already accepting temp | 4 | exit 0; no seed/redeploy case; exactly one verify with correct target/SHA; only held main clears, after verification (accepting temp has no POST). |
| T-11 smoke red | 2: redeploy-old; deploy-temp | 3 | verifier exit 1 becomes `HostCaseFailed verify-runner-caches`, outer exit 2; no affected clear and runner stays held; no next phase (especially no main drain on temp failure). |
| T-12 unknown counters | 19: 3 fields x omitted/null x deploy-temp/redeploy-old/retire-temp, plus live temp runnerSessions=garbage | 3 | unknown-counter diagnosis, exit 2; no seed/replacement/retirement host case; no clear, forced stop or main drain. |
| T-13 runnerSessions busy | 2: redeploy-old; retire-temp | 3 | busy diagnosis, exit 2; no replacement/retirement host case; no clear/stop. |
| T-14 queuedTasks busy | 2: redeploy-old; retire-temp | 3 | busy diagnosis, exit 2; no replacement/retirement host case; no clear/stop. |
| T-15 retirement proof | 3: sessions=1; absent retiredAt; retiredAt with draining=false | 3 | busy/not-retired/state diagnosis, exit 2; no retire host case; no clear/stop and original hold unchanged. |
| T-16 conflicting/unavailable status | 4: conflicting temp drain; conflicting main drain; HTTP 503 temp; HTTP 503 main | 3 | named conflict/API diagnosis, exit 2; no destructive host call; no drain rewrite/clear/stop. |
| **Totals** | **46** | **149 assertions** | **16 named groups, 0 failures; never counted as TUnit results.** |

Use otherwise valid idle/held fixtures for each counter fault so a different precondition cannot mask it. Only a retired, unavailable, non-eligible temp with a verified absent host container may have null `runnerSessions`; bound sessions and queued tasks remain known zero. Emit exactly one `PASS T-n ...` per group and final `C849_ROLLING groups=16 invocations=46 assertions=149 failures=0`; keep the existing `V-32 assertions=149 failures=0` trailer for compatibility. Any missing group or assertion is failure even if the outer process exits 0. These are frozen design counts amended after Final Review 2: T-9 and T-12 each gained one invocation and three assertions, with no new TUnit method.

### CP-3: Linux-host-only fixture and executable positive controls

This is an **operational checkpoint**, not a repo test and not runnable in TestDesign. The trusted desktop front door launches it on the server2 **host daemon**, using uniquely named `c849-<run-id>` volumes/projects and the recorded immutable ID of an already-deployed image with entrypoint overridden. No runner registration, privilege, provider credentials, host Docker socket mount or production cache write is allowed. Only fixture trees/volumes, scoped evidence and build-slot leases may change. Refresh both inventories first; the measured absence of temp `/tmp` is valid **before** rollout. An unreachable inventory is failure, not permission to infer values.

Implement the nine groups below inside the new Fixture case. A control's expected refusal makes its surrounding group pass only after the normal input passed and the intended named assertion failed on the perturbation. Build errors, zero tests, unrelated permission failures and timeouts are not successful controls. Emit `PASS F-1` through `PASS F-9` plus one `CONTROL <id> expected-red observed=<diagnosis>` for every control ID below; omit raw metadata/log contents. The 26 IDs count control families, not process invocations: a family with several listed faults passes only if each separate variant produces its expected refusal. Record those variant counts separately.

| Group / coverage | Executable normal recipe | Mandatory isolated controls and expected diagnosis |
|---|---|---|
| F-1 / V-1 | Run `docker compose --env-file <fixture.env> -p <fixture-main> -f docker-compose.server2-runner.yml config --format json` and again with `-p <fixture-temp> -f docker-compose.server2-runner.yml -f docker-compose.server2-runner.temp.yml`. Populate every required interpolation with harmless fixture paths/values. Parse actual models and require D-1 identities, mounts/env/nocopy and private state. Never `up` these production service definitions; disposable runtime probes use stripped fixture Compose services. | PC-01: remove external -> `ExternalRequired`; PC-02: change temp cache name -> `CacheIdentityMismatch`; PC-03: omit scratch env -> `ScratchPathMismatch`; PC-04: share tmp source -> `PrivateVolumeCollision`; PC-05: widen package mount to `.nuget` -> `CacheMountTooBroad`. Run the same model validator on each rendered fixture copy. |
| F-2 / V-2 | Prepare all three disposable roots twice; `stat` is `1654:1654 700`. As uid 1654 create/rename/remove a probe on each; unchanged root/payload/sibling hashes after the second call. | PC-06: wrong owner; PC-07: 0755 root; PC-08: escaping symlink; PC-09: foreign label; PC-10: unexpected driver/options. Real prepare refuses with the matching owner/mode/path/identity diagnosis, performs no repair, and keeps a sibling sentinel. Driver/option refusal may use a recorded inspect fixture so no foreign storage driver is installed. |
| F-3 / V-3 | Exercise real seed logic using an isolated stopped donor container containing fixed complete/incomplete synthetic package trees and npm data. Check exact allow-list, retained executable/hash, no imported locks, seed marker after verifier success, repeat/no-overwrite, and retained recovery reference. Separately copy only the donor's required net9 smoke package versions into a fixture volume read-only at source; validate completeness and matching before/after hashes, or refuse unstable source. This is not production seed acceptance. | PC-11: wrong donor identity; PC-12: missing host payload; PC-13: incomplete required metadata; PC-14: archive traversal; PC-15: escaping symlink/hardlink or special file; PC-16: interrupted unmarked seed plus live consumer. Each emits the corresponding donor/payload/path/in-use refusal before a marker or recreation. |
| F-4 / V-4 | Two disposable containers restore distinct fresh net10 projects concurrently from a local fixed `C849.Probe/1.0.0` nupkg (nuspec, `_._` and sentinel payload; build it as a ZIP, not from repo projects). Both mount the same initially cold package/scratch volumes at identical absolute paths. Release a barrier after both drivers have leases/readiness; require both restore exits 0, one complete package/hash, then a third fresh restore with empty sources and `--no-http-cache` succeeds. Also execute the NuGet lock probe specified below. | PC-17: fresh processes use separate scratch volumes at the same path; waiter enters while holder still owns its lock -> coordination assertion `NuGetLockNotShared`. A stress restore success by itself cannot satisfy this group. |
| F-5 / V-5 | Run the exact smoke below with the fixture copy of measured donor packages, uid 1654, fresh obj/bin and empty-only sources. Image must lack `Microsoft.NETCore.App.Host.linux-x64/9.0.20` under its SDK packs. Verify the package payload and built native apphost, not only DLL output. | PC-18: repeat with a private empty package **and scratch** pair. Require restore failure naming missing `Microsoft.NETCore.App.Host.linux-x64` 9.0.20 (`NU1101`/`NU1102`), `AppHostCacheMiss`, and no build/run. If image packs or inherited feeds make this green, fail `ControlNotSensitive`; do not delete image/live packs. |
| F-6 / V-6 | Build one fixed `c849-cache-probe@1.0.0` tarball with sentinel content; record its SHA-512 integrity. Container A uses `npm cache add <fixture-http-url>` to fill its isolated shared `_cacache`. Supply a version-3 lockfile with that exact URL/integrity to B's separate project. Stop the fixture HTTP server, remove any local tarball visible to B, then run `npm ci --offline --ignore-scripts --no-audit --no-fund`; require exit 0 and installed sentinel/hash. No registry or user npmrc. | PC-19: identical B project with empty private `_cacache` -> nonzero `ENOTCACHED`, `NpmOfflineCacheMiss`. A `file:` dependency or tarball fallback cannot count as a cache hit. |
| F-7 / V-8 | Two disposable Compose projects consume the same three external fixture volumes and distinct tmp/work/state/dind. Write sentinels, recreate one probe container, then `docker compose -p <fixture-temp> -f <fixture-compose> down -v`. Require all external identities/sentinels retained and readable by main; only temp's four project volumes gone; main tmp stays 1777. | PC-20: in a **separate negative fixture**, detach all consumers with `down` without `-v`, then make one cache a temp-owned non-external volume and bring up/down temp with `-v`. Its expected retained volume is absent -> `ExternalCacheLost`. Never rely on Docker deleting an in-use volume: a survivor attached to it would mask this control. PC-04 separately proves tmp isolation. |
| F-8 / V-9 | On disposable caches only, run real preview/apply under the lock with fixture-scoped consumer/status/lease observations; never ask production runners to drain for this fixture. Preserve mount roots and recovery, revalidate receipt/zero fixture consumers, refill from the local fixture feed, then smoke before fixture readiness. Real build work still takes the host broker lease. Check default budgets (10 GiB/2 GiB/256 MiB), 80% warnings and 20-GiB headroom with boundary inputs; do not fill host disk to these sizes. | PC-21: busy fixture consumer/lease -> `CacheConsumersBusy`; PC-22: stale receipt -> `CachePreviewStale`; PC-23: wrong identity/empty/escaping target -> `CacheTargetInvalid`; PC-24: required set exceeds fixture budget -> `CacheBudgetExceeded`; PC-25: missing recovery image -> `CacheRecoveryMissing`. No admission clear or sibling mutation on refusal. Production Prune still requires D-6's live all-consumer and real broker-idle gate. |
| F-9 / evidence | Verify current main/temp inventory receipts, source/image IDs, the first eight group receipts and their 25 control IDs, then this group's custody control. Enumerate only this run's resources for cleanup; retain external fixture sentinels until F-7 evidence is collected, then remove only the recorded fixture names. No production mutation trace. | PC-26: inject a fake credential/content sentinel into a fixture observation -> `EvidenceNotAllowListed`, absent from exported evidence. Require **26** control IDs overall and nine completed groups, no missing/duplicate receipts. |

For PC-17, compile a small net10 console probe against **the installed SDK's** `NuGet.Common.dll` via `$(MSBuildBinPath)` (no downloaded replacement). Record SDK and assembly identity/hash; require the `ConcurrencyUtilities.ExecuteWithFileLockedAsync<int>(string, Func<CancellationToken, Task<int>>, CancellationToken)` API or fail the fixture as unsupported. Both processes pass the same full nupkg key `/home/app/.nuget/packages/c849.probe/1.0.0/c849.probe.1.0.0.nupkg`. Holder emits `LOCK_HELD` from inside its callback and waits for an explicit release file; waiter emits `LOCK_ATTEMPT` before calling the primitive and `LOCK_ENTERED` only inside its callback. A 30-second cancellation/deadline bounds owned probes. With shared scratch, no entry during a two-second post-attempt observation, followed by entry after release and exit 0 from both, is required. Calibrate with separate scratch first: waiter must enter before release within the same bound, proving the waiter was runnable. Re-run with shared scratch and collect the ordering events. No handwritten `flock` substitute, changed lock key, in-process mutex, or timing-only stress verdict. The API/lock-key choice follows [NuGet's cache guidance](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders) and [NuGet's implementation](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Common/ConcurrencyUtilities.cs); acceptance uses the installed assembly, not an assumed match to the current source branch.

### Smoke recipe and receipts

For post-start verification, the host helper executes inside the target runner as `1654:1654`, `HOME=/home/app`, through `scripts/build-slot.ps1`; hold exactly one server2 host broker lease per build/test driver. A lease timeout is exit 4, never permission for an unleased retry. Record actual granted/released receipts; an unleased fallback is not operational acceptance. Wrap the small restore/build/run driver once and cap its MSBuild commands explicitly at `-maxcpucount:1` with `-nodeReuse:false` (the wrapper cannot inject a grant into a nested shell command). Use a uniquely owned scratch project under `/tmp/c849-<run-id>/<runner-id>`, outside repository ancestry, with private `obj`/`bin`. F-4's two concurrent restores each own one lease and join before the next fixture group; the host fixture orchestrator holds no additional server2 lease around them. A desktop checkpoint-tool lease for bridge orchestration does not count as either host lease.

The pre-seed probe and CP-3 cannot `docker exec` a stopped donor. Run those in disposable, unprivileged host-daemon containers of the recorded image, with entrypoint overridden, uid 1654, only the selected cache/fixture mounts, private scratch workdirs, and no provider credentials or Docker socket. Bind the reviewed `scripts/build-slot.ps1` and its `scripts/lib/build-slot.ps1` dependency read-only; attach to `antiphon-build-slots` and set `ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots` so their wrappers lease the same real host budget. Assert broker availability before starting. Network package sources remain disabled as below. Remove only these named helper containers after collecting evidence. This gives seed acceptance before the donor is replaced without starting an agent or modifying a live runner's network.

Write `Smoke.csproj` with `OutputType=Exe`, `TargetFramework=net9.0`, `UseAppHost=true`, `RuntimeIdentifier=linux-x64`, `RuntimeFrameworkVersion=9.0.20`, `TargetLatestRuntimePatch=false`, `SelfContained=false`, and `NuGetAudit=false`. Write a one-line program that prints `CARD0849_APPHOST_OK`. Set `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true`. Supply an explicit NuGet.Config with `<packageSources><clear/><add key="empty" value="<private-empty-directory>"/></packageSources>` and no inherited sources or credentials.

Under the one leased driver, use the following exact sequence from the fresh scratch project; stop on each nonzero exit. Expected stdout from the last command is exactly `CARD0849_APPHOST_OK` (newline allowed).

```sh
dotnet restore Smoke.csproj --configfile NuGet.Config --no-http-cache -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1
dotnet build Smoke.csproj --no-restore -p:UseAppHost=true -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1
./bin/Debug/net9.0/linux-x64/Smoke
```

Assert actual UID, effective `global-packages=/home/app/.nuget/packages/`, `temp=/var/cache/antiphon/nuget-scratch` and npm cache `/home/app/.npm`; the 9.0.20 `.nupkg.metadata` and `runtimes/linux-x64/native/apphost` are readable/nonempty. Capture apphost-package hash before/after, require executable output, and record restore/build/run exits `0/0/0`. Emit `C849_SMOKE runner=<id> uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK`. Do not count a DLL-only build as success. PC-18 uses its private package/scratch paths instead and must produce its named expected failure.

This production smoke forbids network package sources and HTTP-cache fallback; it does **not** change the live runner's network namespace or firewall. Record that network transport remains available. The mandatory pack-presence assertion plus the fresh cache-only build exceeds the card's minimum pack-directory check without disrupting sessions. Initialization-only helpers may use `--network none`; leased build drivers retain broker connectivity and rely on empty-only sources/`--no-http-cache` (or npm `--offline`) to exclude downloads. Empty private cache must fail under the same source configuration, and the fixture must assert that the image itself has no net9 host pack which would invalidate that control.

Store receipts under `/work/test-evidence/<run-id>/<case>/` through the existing bridge: source SHA, runner/container/image identity, timestamp, filtered mounts, byte counts, owner/mode, effective cache paths, smoke exits, aggregate pass/fail counts, and exact positive-control diagnoses. No provider logs, auth files, full environment/inspect output, package metadata contents, or cache archives in receipts. Operational checkpoints run only from the trusted desktop bridge after the required phase; this card's own active Code session must not try to drain/recreate its host.

### Operational entry points and exact expected summaries

The following front door is a **Code deliverable**, not an existing executable or a TestDesign result. Before the operator starts, set `C849_DEPLOY_SHA` to the full reviewed/landed 40-hex SHA and run from its trusted desktop checkout. The front door validates that source identity, generates a unique run ID and manifests, records the immutable image ID and checks host lane identity before invoking any case. No command below implicitly drains, rolls out or retires a runner.

| When / case | Exact future command | Required exit and summary (variable identities live in the receipt) |
|---|---|---|
| CP-3, before seed/deploy | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture` | exit 0; `C849_FIXTURE groups=9 controls=26 expectedRed=26 inventories=2 failures=0 productionMutations=0`, plus all F/PC receipts; pre-rollout temp tmp absence recorded, not rejected. |
| D-5, drained/idle temp, before recreation | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed` | exit 0; `C849_SEED donor=server2-temp ready=true smoke=passed recovery=retained`; durable marker, nonempty payload hashes and the same old donor reconnected with fresh zero counters under its drain. Busy, unknown counts or absent donor refuse; no replacement or admission follows a failure. This is operational seed work, not a sixth checkpoint. |
| CP-4, after redeploy-old, before drain-temp | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both` | exit 0; `C849_BOTH runners=2 smokes=2 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0`; two smoke receipts, exact deployed SHA, both accepting, retained seed hash and verification-before-clear trace. Each runner's actual Docker mount names match D-1; private work/state/dind names also differ. |
| CP-5, after retire-temp | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Retired` | exit 0; `C849_RETIRED externalVolumes=3 tempPrivateVolumes=0 mainTmpRetained=true smokes=1 rollback=retained failures=0`; temp containers and four project volumes absent, main tmp `antiphon-runner_runner-tmp` at 1777, main at reviewed SHA/accepting, and all three cache roots still writable by uid 1654. |

Fixture may read production package versions solely for its validated copy. Both/Retired's smoke naturally creates fresh project outputs and may touch cache metadata/locks; their authority does not include seeding, replacing or pruning live caches. Seed and Prune are the only front-door cases that import/clear production cache contents. Inventory/PrunePreview may create only scoped evidence, never repair roots.

All operational summaries are assertions over actual receipts, not echoed constants. A host check failure is nonzero and includes a bounded diagnosis; an expected-red control exits nonzero only in its isolated child and is graded by the Fixture parent. The front door refuses any missing prerequisite/receipt and the caller records that CP as pending/failed. Baseline measurements close the inventory question but do not pass CP-3/4/5 or authorize a current drain.

### Checkpoints

The table is the closed execution list. All rows are serial. `C849_DEPLOY_SHA` is the full reviewed deployment SHA, required and validated by the new front door; CP-3 uses that verifier source with a recorded existing image, CP-4/5 assert the newly deployed image SHA. Repo rows run once after committed S1-S3. **CP-3/4/5 are Linux-host-only operational checkpoints and cannot run in TestDesign.** They are mandatory acceptance at their stated rollout points; Code without trusted host access reports them pending with operator/phase prerequisites, never passed or silently skipped. This TestDesign stage executes none of the five rows.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c849-contract/` | repo-cache-contracts | `/*/*/(DockerStackContractTests*)\|(RemoteScriptContractTests*)/*` | Repo test: V-1, V-2, V-3, V-7, V-9 | exactly 154 executed: 112 DockerStackContractTests + 42 RemoteScriptContractTests, 0 failed/skipped; all R-01..R-12 and four saved-donor/retired-guard cases | 154 | 10 | true |
| CP-2 | S1-S3 | n/a | repo-rolling-gates | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | Repo test: V-7, V-9 | exactly 17 groups T-1..T-17, 48 child invocations, 154 assertions, 0 failures; 0 TUnit executions | n/a | 6 | true |
| CP-3 | S1-S3 | n/a | host-cache-fixture | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture` | Linux-host-only operational verification before seed/deploy: V-1 through V-6, V-8, V-9 | 9 groups F-1..F-9 green, 26 controls PC-01..PC-26 expected-red, 2 refreshed inventories, 0 failures/production mutations | n/a | 12 | true |
| CP-4 | S1-S3 | n/a | host-both-runners | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both` | Linux-host-only operational verification after redeploy-old and before drain-temp: V-3, V-5, V-7, V-8 | 2 uid-1654 apphost builds/runs; 3 matching external cache identities; 2 distinct tmp mounts at 1777; complete seed/admission-order receipts; 0 failures | n/a | 5 | true |
| CP-5 | S1-S3 | n/a | host-after-retirement | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Retired` | Linux-host-only operational verification after retire-temp: V-5, V-8, V-9 | 3 external volumes retained; temp project volumes absent; main tmp retained at 1777; 1 apphost build/run succeeds; rollback references retained | n/a | 3 | true |

## Execution and handoff

Code uses the checkpoint tool for one run of the committed repo slice group. The only additional build is the explicitly declared isolated **checkpoint-tool bootstrap**, if a current tool output is unavailable; it builds the launcher, not another test selection. It takes a build slot and releases it before the tool schedules rows. The following commands are future Code instructions, not commands executed by TestDesign:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c849-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c849-launcher/ --property:UseAppHost=false -nodeReuse:false --nologo
dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c849-launcher/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-09-30-card-0849-shared-runner-caches-plan.md --rows CP-1,CP-2 --max-wait 50s
```

Continue `dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c849-launcher/ --property:UseAppHost=false -- wait --run <printed-run-id> --max-wait 50s` while exit is 75; do not start another run. The checkpoint tool owns the CP-1/CP-2 row leases; do not wrap it or the script row in another same-host slot wrapper. CP-1 retains automatic Linux `UseAppHost=false`. CP-2 is a script-command row with `Min=n/a`, not a TUnit filter.

From the desktop, run the same `run` command with `--rows CP-3`, then later `--rows CP-4`, then later `--rows CP-5`, each only at its named operational gate with `C849_DEPLOY_SHA` set; never select all five together. The table's front-door commands are the exact command bodies the tool executes. Report CP-n counts, SHA, failures, reruns and receipt path; the operational rows report their explicit summary fields, not fictitious TRX counts. Record the launcher bootstrap separately with its stated reason. No deployment/host work is authorized merely by running repo checkpoints.

The existing image builds in `deploy-temp`/`redeploy-old`, their mandatory smoke checks, and a connected refill if cleanup is needed are explicitly listed operational work, outside the ordinary repo test build. They must use the host build budget where they run and be reported with that reason; do not rebuild images solely for duplicate testing. The new front door's Fixture case must acquire the server2 broker lease for build/test work, not a desktop lease pretending to bound server2. Keep one lease-owning layer and route actual dotnet smoke work inside the runner through the existing wrapper. Inventory, Compose rendering and status reads require no build.

### Cost

Ordinary Code repo checkpoint floor: 16 minutes, plus authoring and the launcher bootstrap if needed. Linux-host operational verification floor: 20 minutes. Total listed verification: 36 minutes, excluding existing image builds, archive copy duration, rollout/drain waits, and optional cleanup/refill. Drain waits are unbounded by task duration: a busy runner is deferred, never killed to meet an estimate.

Next stage: **Code**. Implement S1-S3 and the frozen 150-case/16-group repo roster, commit/push each slice, then execute CP-1/CP-2 once for the combined committed slice group. Shared NuGet scratch, three narrow external mounts, stopped-donor seeding, cleanup/rollback bounds and all drain gates are mandatory. The 10:45Z inventory closes the investigation gap; refresh it at activation, including CARD-0827's still-unmounted temp `/tmp`. Review assesses repo evidence; the desktop operator performs CP-3 and the phased rollout, attaching CP-4/CP-5 receipts before claiming live acceptance. TestDesign has run no builds, tests, mutation passes or host operations; the five checkpoint results remain unexecuted here.
