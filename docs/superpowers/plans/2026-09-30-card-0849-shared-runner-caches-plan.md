# CARD-0849: shared runner download caches

Date: 2026-09-30. Stage: Plan. Baseline: `71e5153c8e1eda4fa8992e95d8ec9d602fe8376e`.

## Outcome and scope

Persist the NuGet global packages directory and npm's package-content cache in explicitly named, external Docker volumes shared by `antiphon-runner` and `antiphon-runner-temp` on the server2 host. Give NuGet a third shared volume for its cross-process locks. Pre-seed from the existing temp container before recreating it. Gate admission after deployment on an actual net9 apphost build as uid 1654 and preserve the cache through temp retirement.

This is a deployment change, with no server API, database, session-runtime, provider-authentication, or project build-layout change. It does not fix CARD-0671's independent FakeClaude output-directory collision. Keep `UseAppHost=false` for ordinary Linux checkpoint builds; explicitly use `UseAppHost=true` in this card's isolated smoke fixture.

The plan is ready for TestDesign under the decisions below. Production activation remains a desktop-operated, drain-gated follow-up after Code and Review. This Plan session changed no containers, mounts, runner admission, cache contents, or credentials.

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

The runner's Docker CLI reaches its nested daemon, not the server2 host daemon. `ssh -o BatchMode=yes -o ConnectTimeout=8 mc@server2 ...` refused with `Host key verification failed`. Do not disable host-key checks or try to reach the host through a privileged nested container. A request for the trusted desktop SSH lane was sent; **temp's current detailed cache inventory and both host-level mount inspections are outstanding**. The decisions above use measured server2 data and the explicitly attributed temp card evidence. CP-3 repeats the allow-listed inventory through trusted SSH before changing either container. Do not infer absent temp caches from absent server2 caches.

Live API status at approximately 10:30Z:

| Runner | Build version | Sessions / runnerSessions / queuedTasks | Admission |
|---|---|---|---|
| server2 | `d7456a2352d15391f37eca8781c16db499a3759d` | 4 / 4 / 0 | accepting, not draining |
| server2-temp | `a8b4e9e5a0715a644a21c4270dac8b4d60810d4a` | 1 / 1 / 0 | draining, not accepting, not yet retired |

These are observations, not reusable deployment authorization. Re-read status immediately before every destructive operation.

The missing read-only inventory can be collected now from the trusted server2 host shell (no build slot needed). Keep this allow-list; do not replace it with a home-directory walk or an environment dump:

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
4. Populate new, not-yet-consumed package/npm volumes. Initialize scratch empty. A seed marker is written only after import, ownership checks, and the cache-only apphost probe pass. Record donor ID/image, UTC time, byte counts and package-payload hashes. Never merge by overwriting an already-active shared cache. An interrupted unmarked seed is retryable only after proving no consumers; otherwise refuse for operator recovery.
5. Keep the stopped donor until the seed probe passes and a bounded recovery copy of the allow-listed seed is saved. Compose may then replace the donor container; retain the recovery copy through the rollout/rollback window. If the donor disappears before seeding, stop activation; select and verify another cache donor explicitly or perform a connected warm-up as a changed operational procedure. Do not claim the requested first build avoided downloads.

Retain net9 host/reference packages from the donor; copying only the host executable is insufficient for a fresh restore. A donor seed does not guarantee every future dependency/version is cached.

### D-6: size and cleanup policy

Initial operating budgets are 10 GiB for NuGet packages, 2 GiB for npm content, and 256 MiB for scratch. Warn at 80%; require maintenance before another rollout when any budget is exceeded or backing filesystem free space is below 20 GiB. Record `du -s -B1` at every deploy, before retirement, and in the existing operator maintenance review at least weekly. These are operational budgets, **not** fictitious Docker local-volume hard quotas; a long interval with no maintenance can exceed them. Do not add a scheduler or imply automatic eviction.

Document `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case PrunePreview`, then `-Case Prune -Preview <receipt-path>` through the same host helper. The preview records exact volume identities, candidates, bytes and consumer status; apply revalidates them and refuses a stale receipt. Destructive cleanup requires both runners drained with all three counts zero, no cache consumers, and no active build-slot leases. Hold the same maintenance lock. Never run `docker system prune`, `docker volume prune`, a wildcard `/tmp` sweep, or `dotnet nuget locals all --clear`.

For deterministic bounding, rebuild an over-budget cache during this maintenance window: preserve a validated recovery copy; clear **only the selected volume's validated contents** as uid 1654; restore the current deployed source's required packages and net9 smoke into the package volume, and refill npm from the current lockfile if needed. Use explicit `dotnet nuget locals global-packages --clear` with the fixed package/scratch environment and `npm cache clean --force --cache /home/app/.npm` only after those gates; validate that the mounted `_cacache` root itself is preserved. Never use mtime of extracted DLLs as last-use evidence. For selective removal, remove `.nupkg.metadata` first and then the complete package-version directory while all consumers are stopped. Scratch contents may be cleared only in that same fully idle window; keep the mounted root and 0700 ownership.

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

The Plan commit adds only this file. The following is the bounded Code footprint; TestDesign may refine test cases without expanding runtime scope.

| Slice | Files | Change |
|---|---|---|
| S1 | `docker-compose.server2-runner.yml`; `docker-compose.server2-runner.temp.yml` | Three external cache definitions, narrow nocopy mounts, environment, and temp inheritance comment. |
| S1 | `scripts/c590-remote.sh` | Host-only creation/validation, first seed, cache inventory, smoke, isolated fixture, retirement verification and maintenance helpers; invoke prepare before every runner compose launch. Keep cache operations out of nested cases. |
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
| V-3 | Seed imports complete package versions from the stopped idle donor, preserves executable bits, and leaves scratch empty; repeat does not overwrite an active cache. | Donor busy/null count, absent host pack, incomplete metadata, traversal/symlink, interrupted seed and wrong donor identity all refuse before recreation. |
| V-4 | Two disposable projects simultaneously restore different fixture projects using one shared package/scratch pair; both complete and a third offline restore reads the result. | A lock-holder probe using the installed NuGet locking primitive blocks a second process until release; isolated different scratch paths must fail this coordination assertion. Stress success alone cannot prove locking. |
| V-5 | Fresh tiny net9 apphost restore/build and executable run succeed as uid 1654 on temp, recreated main and main after retirement. | Use a private empty NuGet folder and empty package source; restore must fail while the normal seeded case succeeds. Never rename/delete the live host package to make the control red. |
| V-6 | Second container consumes npm package data written by the first with `npm ci --offline --ignore-scripts --no-audit --no-fund` in a distinct throwaway project. | Empty private `_cacache` fails offline. Do not run verify/GC against the production cache while consumers are active. |
| V-7 | Rolling trace orders seed/held startup/verify/clear; each busy or unknown counter prevents replacement; same SHA still verifies; a smoke failure prevents main clear and prevents draining main in deploy-temp. | Force verification exit nonzero or one nonzero/null counter in each fixture; assert no forbidden host case or clear request. Preserve old runner sessions. |
| V-8 | Both actual runners have distinct `runner-tmp` volume sources at `/tmp`, mode 1777; cache names match each other. Temp `down -v` removes only its project volumes; all three external caches remain readable by main. | Disposable two-project `down -v` with one external declaration removed fails retention; changing both `/tmp` sources to one volume fails isolation. |
| V-9 | Size preview is read-only; pruning requires all consumers idle, bounds traversal, retains mount roots and checks refill plus smoke before admission. Rollback retains cache and recoverable image/config references. | Busy consumer, wrong volume label, empty path, budget below required set and missing recovery image all refuse without touching foreign sentinel/production resources. |

CP-1 adds **at least eight independently executed helper/contract tests** for V-1/V-2/V-3/V-7/V-9 to the existing two classes (baseline 110 + 28 `[Test]` methods, no argument rows). Use real outcomes for file/ownership/order behavior, not assertions comparing a fake result to itself. Process-spawning tests use the assembly-local process limiter. Tests that need a Linux shell run on the Linux repo lane; Windows requires the established WSL lane, not silent skips.

CP-2 retains the existing six named scenario groups and adds at least eight named scenarios: busy temp seed; held fresh temp; retired temp verify-before-clear; main same-SHA verify; main smoke red; missing counter; nonzero runnerSessions; nonzero queuedTasks. Assert command/HTTP order and forbidden actions, not just exit codes. TestDesign freezes their exact assertion roster before Code.

CP-3 creates uniquely named disposable volumes and two Compose projects on the Linux host, with no production secrets, no runner registration, and no production volume writes. Use an already-deployed runner image by recorded immutable image ID, overriding its entrypoint; this proves cache/filesystem behavior before building the replacement image. Its cold concurrent-restore fixture can use an in-memory/local package feed built from a fixed synthetic nupkg; the actual NuGet lock probe must use the SDK's NuGet assembly/primitive. The npm fixture uses one fixed package tarball and distinct workdirs. Include real `down -v` retention and permission controls. Do not use the runner's nested daemon as evidence for production external volume identity. Capture the actual temp inventory first; absent/unreachable evidence fails this row.

### Smoke recipe and receipts

For post-start verification, the host helper executes inside the target runner as `1654:1654`, `HOME=/home/app`, through `scripts/build-slot.ps1`; hold exactly one server2 host broker lease per build/test driver. A lease timeout is exit 4, never permission for an unleased retry. Wrap the small restore/build/run driver once and cap its MSBuild commands explicitly at `-maxcpucount:1` with `-nodeReuse:false` (the wrapper cannot inject a grant into a nested shell command). Use a uniquely owned scratch project under `/tmp/c849-<run-id>/<runner-id>`, outside repository ancestry, with private `obj`/`bin`.

The pre-seed probe and CP-3 cannot `docker exec` a stopped donor. Run those in disposable, unprivileged host-daemon containers of the recorded image, with entrypoint overridden, uid 1654, only the selected cache/fixture mounts, private scratch workdirs, and no provider credentials or Docker socket. Bind the reviewed `scripts/build-slot.ps1` and its `scripts/lib/build-slot.ps1` dependency read-only; attach to `antiphon-build-slots` and set `ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots` so their wrappers lease the same real host budget. Assert broker availability before starting. Network package sources remain disabled as below. Remove only these named helper containers after collecting evidence. This gives seed acceptance before the donor is replaced without starting an agent or modifying a live runner's network.

Write `Smoke.csproj` with `OutputType=Exe`, `TargetFramework=net9.0`, `UseAppHost=true`, `RuntimeIdentifier=linux-x64`, `RuntimeFrameworkVersion=9.0.20`, `TargetLatestRuntimePatch=false`, `SelfContained=false`, and `NuGetAudit=false`. Write a one-line program that prints `CARD0849_APPHOST_OK`. Set `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true`. Supply an explicit NuGet.Config with `<packageSources><clear/><add key="empty" value="<private-empty-directory>"/></packageSources>` and no inherited sources or credentials.

Under the one leased driver, run `dotnet restore Smoke.csproj --configfile NuGet.Config --no-http-cache -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1`, then `dotnet build Smoke.csproj --no-restore -p:UseAppHost=true -p:NuGetAudit=false -nodeReuse:false -maxcpucount:1`, then run the produced apphost. Assert actual UID, resolved package/scratch locations, 9.0.20 package metadata and apphost payload, executable output, expected stdout and exits. Capture package hash before/after. Do not count a DLL-only build as success.

This production smoke forbids network package sources and HTTP-cache fallback; it does **not** change the live runner's network namespace or firewall. Record that network transport remains available. The mandatory pack-presence assertion plus the fresh cache-only build exceeds the card's minimum pack-directory check without disrupting sessions. Where isolated fixtures can run with `--network none`, also do so. Empty private cache must fail under the same source configuration, and the fixture must assert that the image itself has no net9 host pack which would invalidate that control.

Store receipts under `/work/test-evidence/<run-id>/<case>/` through the existing bridge: source SHA, runner/container/image identity, timestamp, filtered mounts, byte counts, owner/mode, effective cache paths, smoke exits, aggregate pass/fail counts, and exact positive-control diagnoses. No provider logs, auth files, full environment/inspect output, package metadata contents, or cache archives in receipts. Operational checkpoints run only from the trusted desktop bridge after the required phase; this card's own active Code session must not try to drain/recreate its host.

### Checkpoints

The table is the closed execution list. All rows are serial. `C849_DEPLOY_SHA` is the full reviewed deployment SHA, required and validated by the new front door; CP-3 uses that verifier source with a recorded existing image, CP-4/5 assert the newly deployed image SHA. Repo rows run once after committed S1-S3. Operational rows are mandatory acceptance at their stated rollout points; a Code report without trusted host access must mark them pending, never passed or silently skipped. This Plan stage runs no builds/tests.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c849-contract/` | repo-cache-contracts | `/*/*/(DockerStackContractTests\|RemoteScriptContractTests)/*` | Repo test: V-1, V-2, V-3, V-7, V-9 | >=146 executed, 0 failed/skipped; includes >=8 added behavior/contract tests | 146 | 10 | true |
| CP-2 | S1-S3 | n/a | repo-rolling-gates | `pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | Repo test: V-7, V-9 | >=14 named scenario groups, 0 failures; report assertions separately from TUnit counts | n/a | 2 | true |
| CP-3 | S1-S3 | n/a | host-cache-fixture | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture` | Linux-host-only operational verification: V-1 through V-6, V-8, V-9; includes both-runner inventory | 9 named invariant groups green, all applicable positive controls red as designed, both inventories present, zero production mutations | n/a | 12 | true |
| CP-4 | S1-S3 | n/a | host-both-runners | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both` | Linux-host-only operational verification after redeploy-old and before drain-temp: V-3, V-5, V-7, V-8 | 2 uid-1654 apphost builds/runs; 3 matching external cache identities; 2 distinct tmp mounts at 1777; complete seed/admission-order receipts; 0 failures | n/a | 5 | true |
| CP-5 | S1-S3 | n/a | host-after-retirement | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Retired` | Linux-host-only operational verification after retire-temp: V-5, V-8, V-9 | 3 external volumes retained; temp project volumes absent; main tmp retained at 1777; 1 apphost build/run succeeds; rollback references retained | n/a | 3 | true |

## Execution and handoff

Use `dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-09-30-card-0849-shared-runner-caches-plan.md --rows CP-1,CP-2` for the committed repo slice group, through the prescribed launcher/build-slot procedure in `docs/testing-and-build.md`. Continue `wait` while the checkpoint tool returns 75; use foreground waits of at most 60 seconds. Run CP-3, CP-4 and CP-5 separately with `--rows` at their respective operational gates from the desktop. Record CP-n counts, SHA, failures, and reruns. Do not execute all five rows blindly: the host rows require phase-specific state.

The existing image builds in `deploy-temp`/`redeploy-old`, their mandatory smoke checks, and a connected refill if cleanup is needed are explicitly listed operational work, outside the ordinary repo test build. They must use the host build budget where they run and be reported with that reason; do not rebuild images solely for duplicate testing. The new front door's Fixture case must acquire the server2 broker lease for build/test work, not a desktop lease pretending to bound server2. Keep one lease-owning layer and route actual dotnet smoke work inside the runner through the existing wrapper. Inventory, Compose rendering and status reads require no build.

### Cost

Ordinary Code repo checkpoint floor: 12 minutes, plus authoring. Linux-host operational verification floor: 20 minutes. Total listed verification: 32 minutes, excluding existing image builds, archive copy duration, rollout/drain waits, and optional cleanup/refill. Drain waits are unbounded by task duration: a busy runner is deferred, never killed to meet an estimate.

Next stage: TestDesign. Freeze executable positive controls and the helper/rolling scenario roster, retain the narrow cache selection and shared NuGet scratch requirement, and keep unavailable temp inventory and live activation explicitly gated. Then Code implements S1-S3; Review assesses repo evidence; the desktop operator performs the phased rollout and attaches CP-3/4/5 receipts before live acceptance is called complete.
