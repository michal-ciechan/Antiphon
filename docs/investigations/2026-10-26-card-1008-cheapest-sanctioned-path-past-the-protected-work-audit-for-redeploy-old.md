# Cheapest sanctioned path past the protected-work audit for redeploy-old

2026-10-26; Investigate task `6437634e-428e-4c38-ae15-1f2361d30c67`. Source inspected at `b6db2de3dd0c0bad2ba30823d01882ca6fc9388e`, branch `feat/card-task-6437634e`. This task has no card binding; CARD-1008 in the filename identifies the recycling policy being investigated.

**Confirmed from code and stored operational evidence: the refusal protects dirty repository contents before destructive volume recycling. It is not an intrinsic prerequisite of changing the runner image. There is no keep-volume flag in the rolling wrapper. A nonrecycling lower-level entry and a successful, explicitly reviewed preserve-volume recovery both exist, but neither is a general permission to bypass the staged rollout.** The records identify ten distinct dirty repository entries across the recoveries, eight already resolved and two source worktrees still dirty. They do not identify 936 dirty worktrees or establish a passing full audit.

This is an operational guard working as specified, rather than a demonstrated product defect. No worktree other than this report checkout, container, task, database row, or deployment was changed. No server2 audit, build or test was run.

## Evidence provenance and present state

The three original desktop artifacts were read in full through the documented agent file-content GET surface (`docs/antiphon-api.md:1186`; handler `server/Api/Endpoints/ReviewEndpoints.cs:55`). Each response had `truncated=false`, `missing=false`. Local copies under `.antiphon/investigation-6437634e/` are ignored generated evidence. Original line numbers below refer to the returned text, retaining its CRLF line endings.

| Label | Original artifact | Bytes / SHA-256 of returned UTF-8 text |
|---|---|---|
| R1 | `C:\src\Antiphon\.antiphon\rolling-server2\task-1d297147\recovery-report.md` | 4380 / `7a5b444ec1b7df778781092e3181c81e251f55fc028ccefde7c903bd1a1cc3e4` |
| R2 | `C:\src\Antiphon\.antiphon\rolling-server2\task-fb2b95ad\final-observation.json` | 6068 / `00d7b3fe77eec02c650066a211076a3682f8b6d41040a5e82e16337c6bc3fe1f` |
| R3 | `C:\src\Antiphon\.antiphon\rolling-server2\task-fd74c8ec\final-observation.json` | 5987 / `1581431454e1b3f7bbb8a9c1405f2a77ce9e20db206adf2e0bc773df534bf39d` |

The corresponding stored task rows were also read with `GET /api/agent-tasks/{id}`:

- `1d297147-5561-4f9c-8a4c-e8d435b9b8a2`, Blocked: seven seeds identified, no cleanup yet.
- `fb2b95ad-1d26-4920-8a54-b29f1b999b92`, Succeeded: seven explicitly authorized seeds removed; audit refused the next family.
- `fd74c8ec-7a4d-49e5-a190-532877da8978`, Succeeded: two evidence files preserved/removed; real source preserved; audit incomplete.

Normalized transcript corroboration: session `de9256c7-007f-485c-9c41-963b3fcbe226`, sequence 8, is the R1 final report. Session `7827407c-6cb1-465c-87fe-3e7e3f931e2f`, sequences 25 and 63, are the R2/R3 final reports; sequences 39, 59–62 record enumeration, source discovery, rescue publication and shutdown. These were retrieved through `GET /api/sessions/{id}/transcript?since=0`, not inferred from screen output.

Fresh read-only observations at **2026-10-26T21:58:12Z**:

| GET | Observed result |
|---|---|
| `/api/session-runners/server2/status` | Build `81cfb267a0af5c0ddb65e8b238af355067bee9cc`; available and dispatch-eligible; drained to `server2-temp`; accepting false; sessions / runnerSessions / queuedTasks = 0 / 0 / 0; store `f519bd08-e53a-47d1-adb1-2ab33475446f`; eventMode `legacy` |
| `/api/session-runners/server2-temp/status` | Build `bedea1c03cd788d83b2ff54c40bfb603927bd69c`; available, dispatch-eligible and accepting; sessions / runnerSessions / queuedTasks = 1 / 1 / 0; eventMode `window-v1` |
| `/api/version` | `bedea1c03cd788d83b2ff54c40bfb603927bd69c` |

These observations confirm runner state, not a fresh complete task/land census or host disk/image inventory. A separate `git ls-remote origin refs/heads/rescue/b3b8a088` returned `49fa458e66e529603b63fe314b2c979e847699b9`, matching R3's preserved tip.

## 1. What redeploy-old does and why the audit precedes it

The actual front door is `scripts/deploy-server2.ps1`, with host implementation in `scripts/c590-remote.sh`. No tracked `scripts/rolling-server2*.sh` or `scripts/rolling-server2*.ps1` exists at the inspected source SHA.

When the old build differs from the requested SHA, the wrapper requires zero counters, the exact drain/redirect state, accepting temp and a task census. It then supplies a typed recycle context to `deploy-parent` (`scripts/deploy-server2.ps1:1258`, `:1267`, `:1273`, `:1277`). `case_deploy_parent` invokes `c1008_recycle` before building/seeding/starting the replacement (`scripts/c590-remote.sh:8034`, `:8038`, `:8064`, `:8070`, `:8081`).

The main targets are exactly `antiphon-runner_work`, `antiphon-runner_runner-tmp` and `antiphon-runner_dind-data` (`scripts/c590-remote.sh:7838`; `docs/docker-stack.md:324`). Main runner-state, the three shared caches and seed marker are preserved (`docs/docker-stack.md:342`, `:345`, `:350`). Removing/recreating work loses its existing checkouts and ignored output; removing dind loses nested Docker images. This is actual recycling, not just reattaching the old workspace to a new image.

The destructive ordering is explicit:

1. First protected-work audit, save proof: `scripts/c590-remote.sh:7960`, `:7966`.
2. Stop only inspected owned containers, then remove them: `:7972`, `:7979`, `:7987`, `:7991`.
3. Record `containersRemoved`, repeat the audit and require identical proof; refresh status and references: `:7997`–`:8001`.
4. Record removal intent, check identities/references again, then `docker volume rm`: `:8003`, `:8007`–`:8015`.
5. Recreate/seed work and start Compose: `:8070`–`:8082`.

The first audit prevents loss of protected bytes before stopping main; the second detects changes before deleting its now-unreferenced volumes. `RecycleWorktreeDirty` is raised from nonempty porcelain status, staged entries, hidden index flags or raw content differences (`scripts/c590-remote.sh:6530`, `:6553`, `:6557`, `:6572`). Publication is a separate check of retained Git tips, including reflogs and linked worktree admins (`docs/docker-stack.md:393`). A terminal task status does not waive either check. Ignored files in an indexed checkout are deliberately excluded, but nested repositories and an indexless checkout with extra content can still refuse (`docs/docker-stack.md:395`).

The helper mounts work and registered tmp checkouts read-only and runs as UID/GID 1654 (`scripts/c590-remote.sh:7572`, `:7575`, `:7598`). A dirty result propagates to the operation refusal (`:7606`, `:7637`). It is not a failed Git command: `audit check=status status=0` can mean Git status succeeded and returned protected changes.

`-DryRun` exits before this audit with `auditPending=true` (`scripts/c590-remote.sh:7878`–`:7881`; `docs/docker-stack.md:399`). `-ResumeRecycle` is recovery of the same journaled recycle, not keep-volume: it rechecks source/project/Compose/generation/publication (`docs/docker-stack.md:412`–`:420`). Its post-recreation shortcut requires saved removed/alreadyAbsent outcomes and exact recreated identities (`scripts/c590-remote.sh:7893`–`:7906`). Pending original volumes remain deletion targets. The historical recovery explicitly warns that resuming its `containersRemoved` operation would delete all three pending volumes (`docs/investigations/2026-10-17-server2-recycle-recovery.md:118`–`:124`).

## 2. Nonrecycling code versus sanctioned rollout authority

| Path | What the evidence establishes | Applicability here |
|---|---|---|
| Rolling `redeploy-old` with a differing build | Always passes the default typed recycle context; no `NoRecycle`/`KeepVolume` parameter. Parameter roster: `scripts/deploy-server2.ps1:4`–`:18`. | Normal sanctioned replacement recycles and audits. |
| Same-SHA `redeploy-old`, no resume/dry-run | Skips `deploy-parent` only after the runner is already on target, available/eligible and has no unfinished same-SHA journal; still verifies cache/store and clears drain (`scripts/deploy-server2.ps1:1254`–`:1258`, `:1280`–`:1291`; journal check `:317`–`:328`). | It cannot itself upgrade the current old SHA. |
| Legacy `deploy-parent` without `recycle` in the manifest | Real supported code remains nonrecycling: the bridge defaults context to empty (`scripts/c590-real.ps1:359`–`:371`); `c1008_recycle` is conditional on `default` (`scripts/c590-remote.sh:8038`). The comment explicitly preserves this behavior (`:5485`–`:5486`). An existing fixture asserts the direct entry remains available and removes/stops nothing through recycling (`tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:604`–`:612`; inspected, not rerun). | Technical capability exists. Omitting a manifest field is not a sanctioned rolling phase or automatic substitute for gate 6. This entry also lacks the wrapper's complete staged admission. |
| Reviewed preserve-volume recovery | The October 17 diagnosis specifies volume-preserving Compose up, real mount/store/SHA/health checks, a truthful journal seal with outcomes `kept`, then same-SHA wrapper re-entry (`docs/investigations/2026-10-17-server2-recycle-recovery.md:128`–`:177`, `:191`–`:239`). Execution and eventual exit 0 are recorded in `docs/investigations/2026-10-17-rc-rollout-finish.md:214`–`:243`, `:342`–`:358`. | Documented precedent exists, but that incident had already removed containers after a successful first audit. Today's running, preflight-refused old runner is a different state. It needs its own reviewed recovery, not reuse of historical operation IDs or journal edits. |

The governing runbook requires named phases, forbids improvising a Docker replacement on refusal, and directs a refused gate 6 through reviewed recovery (`docs/docker-stack.md:5`–`:12`, `:127`). Standing operational authority likewise says to restart main only through rolling phases (`docs/orchestration-loop.md:17`–`:23`). Thus “no keep-volume implementation exists” would be false; “the current wrapper exposes a sanctioned audit-bypass flag” would also be false. This read-only commission neither prepares nor executes a new recovery.

**Cost:** retaining work/tmp/dind forgoes their volume-level disk reclaim and carries their residue forward; it does not make the next recycling audit clean. It also retains nested images/build output and avoids rebuilding them. The current legacy deploy still runs cache/disk budget checks (`scripts/c590-remote.sh:8064`–`:8067`); the budget includes at least 20 GiB free on Docker's data-root filesystem (`:2720`–`:2722`). Current free space was not measured. The historical preserve diagnosis had about 191 GiB free and did not need deletion to start (`docs/investigations/2026-10-17-server2-recycle-recovery.md:181`–`:186`). Its state-init actually took approximately 3 h 26 min (`docs/investigations/2026-10-17-rc-rollout-finish.md:218`), so preservation is not proven faster in wall time.

Preservation does not waive source/target image and Compose identity, drain/zero-work/routing/land census, temp availability, host prerequisite checks, unchanged runner store, mounts/cache checks, tmp mode, UID-1654 secret readability, health/nested Docker, actual registered build and dispatch eligibility. The historical seals and observations are at `docs/investigations/2026-10-17-server2-recycle-recovery.md:149`–`:151`, `:193`–`:239`; current staged gates are at `docs/docker-stack.md:122`–`:130`, `:226`–`:245`. Gate 7 still needs version smoke, a sanctioned Plan canary and Both. Once main is ready, dispatch returns to main, then drain-temp and retire-temp remain mandatory. Temp's own disposable volumes still require the applicable protected-work and removal proofs (`docs/docker-stack.md:319`–`:322`, `:369`–`:387`, `:477`). No audit applies to deletion that is not being attempted; that does not remove other deployment gates.

## 3. What prior recoveries actually demonstrated

The clean-audit record is `docs/investigations/2026-10-16-server2-worktree-audit.md`, ending on October 17. Three real-source rescues were committed **in their checkouts**, pushed, left on the published rescue branches and verified porcelain-clean (`:31`–`:41`). Generated or uninspectable fixtures were separately bundled/tarred, copied outside the recycled volumes and hash-verified before exact-path removal (`:68`–`:78`). The full read-only body then passed, `repositories=800 partial=800`, in 5225 seconds (`:93`–`:97`). `partial` counted promisor checkouts, not incomplete coverage.

The October 17 rollout-finish report does not establish that every later attempt passed. It records tmp-checkout and stale-admin recovery, including archive/hash proof and narrowly scoped Git worktree pruning (`docs/investigations/2026-10-17-rc-rollout-finish.md:80`–`:94`, `:117`–`:135`). Its eventual upgrade used the reviewed volume-preserving recovery. After that, same-SHA `redeploy-old` skipped `deploy-parent` and exited 0; smoke/canary/Both followed (`:348`, `:360`–`:380`). That is not evidence that an originally dirty volume passed a fresh destructive audit.

The requested `docs/investigations/2026-10-23-card-1572-outage-recovery.md:3`–`:23` is an attributed fixture/test recovery, explicitly not a clean lane or rc qualification. Its scratch checkout was restored to clean and removed (`:709`–`:715`, `:723`–`:735`). It provides no production `redeploy-old` protected-work-audit success and no general snapshot-then-clean terminal-worktree script.

No general sanctioned snapshot-then-clean helper was identified in the examined script inventory. R1:10–17 explicitly documents why the available worktree-residue and build-junk helpers did not remove these fixture seeds. R2:59 records the subsequent **explicit exact-fixture deletion authority**, not a newly discovered generic cleaner. R3's rescue is snapshot-only: `originalOwnerBranchIndexAndFilesUnchanged=true` (R3:38–49). Neither a published rescue tip elsewhere nor an off-volume bundle changes dirty status in the original checkout.

The distinction is decisive for `task-b3b8a088`: its rescue publication is verified, but `consider_dirty` still examines that original checkout's index/HEAD/files, which R3 says were unchanged. The prior successful rescue pattern satisfied **both** publication and checkout cleanliness; this rescue currently satisfies preservation only. No reset, branch switch or source disposition is authorized by an audit refusal itself (`docs/docker-stack.md:420`, `:442`–`:448`).

## 4. Count by family and the existing route to a passing audit

Counts below were reconstructed directly from R2's seven-element `deletedSeedInventory` and R3's cleaned evidence repository and two source entries, corroborated by the stored task results/transcripts. Counts are repository entries, not files or unique task owners.

| Family / owner | Distinct dirty entries observed | Recorded disposition |
|---|---:|---|
| C1313/CARD-1545 fixture seeds under `task-2dbadf60` | 1 | Deleted under explicit authority; R2:8–14 |
| Same fixture seeds under `task-87928ecc` | 6 | Deleted; baseline master/rc, fixed assigned/master/rc, master-base; R2:15–56 |
| CARD-1540 `task-85215635/.antiphon/evidence/c1540/path-v2` | 1 | Two files, 115 + 67 bytes, hash-verified off-volume then removed; repository clean; R3:5–23 |
| CARD-1464 `task-b3b8a088` | 1 | 15 changed source/plan paths; rescue `49fa458e66e529603b63fe314b2c979e847699b9` published, original still dirty; R3:38–49 |
| Detached `/work/worktrees/87928ecc-master` | 1 | Three modified scripts, not rescued, untouched; R3:51–64 |
| `d3bad549` ordinary-status error | 0 proven dirty | Classified bare; status was inapplicable, not evidence of dirty files; R3:33–36 |
| **Known distinct dirty entries across these recoveries** | **10** | **8 resolved; at least 2 remain** |

The main `task-87928ecc` checkout being clean at `31d6db92ed14fc298e775963e6a5eed9fbba114f` does not clean its separate detached `87928ecc-master` checkout at `7635f8de4d76434fb6ae1e8a758188d3b4da80d9` (R1:5; R3:52). The latter modifies `scripts/test-change-aware-release.ps1`, `scripts/test-rc-platform.ps1` and `scripts/test-test-selection.ps1` (R3:55–58).

The synthetic seed mechanism also reconstructs exactly: the fixture creates `seed` with identity `C1313 fixture` (`scripts/test-change-aware-release.ps1:32`–`:39`), then stages a baseline client blob at `Directory.Build.props` with `update-index --cacheinfo` without creating that file (`:167`–`:170`). Its finalizer only clears read-only attributes (`:373`–`:378`). R1:8 and R2's seven `AD Directory.Build.props` statuses agree with that mechanism. These generated changes and the real source changes are different families.

R3:24–31 records `exit=143`, `emittedEntryCount=355`, `enumeratedEntries=936`, `complete=false`. **581 entries had not emitted audit lines.** Eight resolved entries plus two known dirty source checkouts are a lower-bound inventory, not an estimate that the other entries are clean. Source path count is 18 across two worktrees; it is not 18 dirty worktrees. A bare status failure adds no dirty entry. No statistically defensible total dirty count or cleanup duration can be extrapolated from this stopped scan.

The least additional work supported by the **existing ordinary recovery procedure** is the following dependency chain; it is not a newly designed cleaner or a guarantee of success:

1. Keep main running/drained and temp accepting. Revalidate exact ownership, stopped sessions, publication and retained artifacts for the two known source checkouts; do not repeat the seven-seed cleanup or discard the unfinished source checkouts. The manual procedure requires recovery before deletion and refuses dirty/unrecovered work (`docs/docker-stack.md:424`–`:448`). The prior published-and-clean rescue example is the relevant precedent, not snapshot-only publication.
2. Resolve those two original checkout states through the caller's commissioned source recovery/disposition. The unresolved work is currently 15 preserved paths plus three unrescued scripts. The stored reports do not authorize an automatic blanket reset or worktree retirement. Individual recovery can leave published clean checkouts in place; deleting all 936 entries is not a prerequisite.
3. After that recovery, a complete protected-work audit must establish success. Ordinary status alone, an interrupted scan or dry-run cannot do so. A fresh census and preview precede a fresh named gate-6 Deploy; the production recycle independently repeats its own pre-stop/post-removal audit and removal proofs. Matching journal/resume rules still apply.
4. After gate 6, complete smoke/canary/Both, move new dispatch to main and run drain-temp/retire-temp (`docs/docker-stack.md:127`–`:130`).

For the different objective **upgrade without main-volume disk reclaim**, the evidence supports commissioning a state-specific reviewed preserve-volume recovery using the precedent above. That avoids main-volume deletion, not the dirty-work recovery obligation or temp retirement. It does not produce a passing audit. There is no current sanctioned one-flag sequence that simultaneously preserves dirty original workspaces and reports a passing recycle audit.

Repeated full scans have material cost: the documented clean 800-entry scan took 5225 s (87 min); a separate OLD-volume replay took 1404 s (23.4 min), with a current default budget of 10800 s for **each** audit (`scripts/c590-remote.sh:6370`–`:6377`; `docs/docker-stack.md:393`). Those are historical measurements, not a promise for today's 936-entry volume. They support addressing already-known blockers before another full scan; they do not support a precise cleanup-time estimate or relaxing the proof.

## Remaining uncertainties and validation limits

- No full audit after the latest cleanup exists in these records. Additional dirty/unpublished/unknown Git state may remain beyond the two known source checkouts. Completing the scan after source recovery resolves this uncertainty.
- Current Docker disk free space, target image presence/identity, Compose compatibility and exact journal inventory were not read from the host. A reviewed preserve recovery must establish them afresh; the October 17 IDs, image tags and journal seal are not transferable facts.
- R2's stored task result says the audit exited 1, while its raw `final-observation.json` has `protectedAudit.exit=2` (R2:67). Both agree on the same dirty refusal and incomplete result. This investigation does not choose an undocumented reconciliation between body/wrapper exits.
- The exact source disposition for the two worktrees remains caller-owned. Publication of the rescue does not prove every later working byte is represented without a fresh comparison.
- The legacy entry's lack of recycling is source-confirmed and has an existing fixture assertion. It was not executed here and is not represented as qualified for this rollout.

## Not done, noted

A general volume-preserving rolling mode would require separately commissioned design; none was designed or implemented here.

--- next stage ---
next: none
handoff: Confirmed intended protected-work refusal, not a demonstrated bug. Caller-owned operational recovery must resolve at least two dirty source checkouts before full audit, or commission a separately reviewed preserve-volume recovery; no deploy ran.
artifact: docs/investigations/2026-10-26-card-1008-cheapest-sanctioned-path-past-the-protected-work-audit-for-redeploy-old.md
