# Self-contained Docker stack (CARD-0590)

## Staged server2 rolling rollout (CARD-0934)

Run from the reviewed, landed **canonical desktop checkout**, with `<sha>` its full lowercase
`HEAD`. Use one `scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase <name>` call per gate;
never use the default `-Phase all`. The phase list in that script is the authority. Keep the
standing `server2` container accepting until `server2-temp` has passed its canary. Do not drain
both runners or drain the only accepting runner. A refusal means stop, record its exact code and
receipt under `.antiphon/rolling-server2/`, and report it; do not force a phase or improvise a
Docker replacement. The only timed stop exception is step 5 below.

Before starting, verify no land is pending, the checkout has no half-reset worktree, and the
running server's `/health` and `GET /api/version` SHA match the canonical source-root `HEAD`
(see [the autonomy policy](orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout)).
Before host qualification or gate 1, check project identity and both filtered task
closures without changing runner state:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-census
```

Expect `RECYCLE_PROJECT id=<guid> name=<name> resolvedBy=url|path|both|explicit` and
two `RECYCLE_CENSUS runner=<id> open=<n> boundOpen=<n> landPending=<n> scopes=<n> elapsedMs=<n>`
lines, for `server2` and `server2-temp`. This phase uses GET only, performs no SSH or
host jq check, and reports bound work and pending lands as counts. It authorizes no
recycling. Resolution, read, malformed-data or unstable-census failures still refuse;
retain the redacted `census-check-census-<runner>.json` receipts under the printed
`.antiphon/rolling-server2/<run-id>/` path, including partial receipts on refusal.

`RecycleProjectUnresolved cause=NoMatch` means neither canonical Git URL nor this
checkout's path matches the project row. Set either identity through project settings
(a full-row update), or pass `-ProjectId <lowercase-guid>` to this check and every later
phase and record it in the rollout receipt. Never use a partial project PUT. Multiple
matches (`cause=Ambiguous`), an archived row (`cause=Archived`), an absent explicit id
(`cause=NotFound`) or invalid id (`cause=InvalidId`) require correcting the identity.
`cause=Http status=400` can mean the server predates the `landPending` filter: land
the reviewed server change and restart AppHost, then verify `/api/version` against HEAD.

Before cache preparation or gate 1, qualify **outer-host jq** in the non-login SSH
deployment shell. The runner image's jq does not satisfy this prerequisite.
Use the reviewed, landed canonical checkout at `<sha>`:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-host-jq
```

Only `HostJqMissing` permits the explicit provision phase:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase provision-host-jq
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-host-jq
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase provision-host-jq
```

Retain the independently checked and both provision receipts under the printed
`.antiphon/rolling-server2/<run-id>/host-jq-<phase>.json` paths. The second provision
must record `installed=false` and the unchanged resolved binary hash. For an
already qualified jq, retain check/no-op receipts without replacement. Provision
requires clean canonical `HEAD=<sha>` and verifies the jq 1.7.1 artifact SHA-256
`5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5` before publication.
Each receipt binds source SHA, run, selected/executing phase, observation time and
SSH exit to host path/version/digest/owner/mode and both semantic predicate exits.
Invalid jq, unapproved PATH shadowing, unknown transport/proof or failed receipt
persistence stops preparation; diagnose the refusal rather than installing by hand.
[CARD-1058 host qualification](superpowers/plans/2026-10-05-card-1058-host-jq-file-identity-plan.md)
rejects canonical-leaf symlinks, home PATH shadows and files with multiple hardlinks.
It hashes and executes jq through one retained descriptor, checks the installed
artifact's pin before execution, and buffers the proof until final leaf identity,
link count and executable-access checks pass. Publication removes only its owned
staging link before qualification. Functional existing jq keeps its actual version
and digest; it need not match the installation pin.
The receipt is a point-in-time observation. The canonical directory and ancestors,
inode contents and host toolchain remain trusted: an FD does not prevent in-place
writes, ABA swaps or replacement after proof emission before a later consumer.
The image probe still trusts directory integrity and has no descriptor custody
from this host helper. These phases do not deploy or restart anything.
Give the host receipt reference to the [CARD-1040 image qualification owner](superpowers/plans/2026-10-04-card-1040-jq-prerequisites-and-unit-timing-plan.md);
host qualification does not qualify the image or its activation.

Run `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha <sha>` and
`-Case Inventory -Sha <sha>` and retain their receipts. For empty cache volumes use
`pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed -Cold -Sha <sha>`;
otherwise use the [documented Seed source](#legacy-full-seed-and-maintenance). Stop if Seed or
the inventory refuses; `Reset` is not an automatic recovery.

For status checks below, run `pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId
server2` (or `server2-temp`). A healthy deployment needs `buildVersion=<sha>`,
`dispatchEligible=true`, and `acceptingNewWork=true`; a drained runner needs fresh non-null zero
`sessions`, `runnerSessions`, and `queuedTasks`. A 404, null counter, stale observation or wrong
SHA is a stop-and-report condition. Before `deploy-temp`, an absent temp container may have
`runnerSessions=null` only when the temp status is offline (`available=false`,
`dispatchEligible=false`, `acceptingNewWork=false`) with zero bound `sessions` and
`queuedTasks`. The phase checks this before changing a retired placeholder; a live or busy
runner still requires a known zero `runnerSessions`. Run `Invoke-RestMethod 'http://localhost:17202/api/runner-defaults'`
to record the configured global and per-kind preferences; these phases do not change that
setting or a routing pin. If the intended unpinned work does not currently prefer `server2`,
the drain redirect cannot promote temp for it: stop and report the missing routing prerequisite.

| Gate | Exact phase / verification command | Stop and rollback |
|---|---|---|
| 1. Deploy beside old | Before and after, `ssh mc@server2 'docker inspect -f "{{.Id}} {{.State.StartedAt}}" antiphon-runner-session-runner-1'`; the ID and start time must be identical. Run `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase deploy-temp`. Check both runner statuses. | If temp fails or old changes, stop; leave old accepting and temp for diagnosis. Never touch the standing container to fix temp. |
| 2. Smoke temp | Run the command block below for `server2-temp`, then dispatch a sanctioned `Plan` canary pinned with `-Runner server2-temp`; inspect its task until it starts, runs, and reports. | A version/tool/canary failure blocks handoff. Keep old accepting; do not run `drain-old`. |
| 3. Mark temp primary | There is **no independent promotion phase today**. Verify `server2-temp` is accepting and the canary succeeded; record the runner-defaults read. The next phase, `drain-old`, atomically writes `server2.draining=true` and `redirectTo=server2-temp`; `DefaultRunnerRoutingPolicy` then redirects automatic placement from a `server2` default to temp. It does not PUT `/api/runner-defaults`. | If the redirect cannot be made with a healthy accepting temp, stop with old accepting. CARD-0935 tracks a separate promotion gate. Explicit pins and already running sessions need separate inspection; do not assume they moved. |
| 4. Drain old | Run `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase drain-old -WaitIdleMinutes 240`; check `server2` status shows `draining=true`, `redirectTo=server2-temp`, and temp still accepts new work. A new unpinned canary should resolve to temp. | If the phase refuses before drain, keep old accepting. If it refuses after drain, stop and report the code. To roll back while old is healthy, `pwsh -NoProfile -File scripts/runner-drain.ps1 clear -RunnerId server2 -Reason 'rolling rollback'`, then verify it accepts; only then consider draining temp. |
| 5. Wait up to four hours | `drain-old -WaitIdleMinutes 240` performs this wait. Check `pwsh -NoProfile -File scripts/runner-slots.ps1 list -RunnerId server2` and old status at the deadline. Proceed to `redeploy-old` only when all three counters are zero. | `OldRunnerStillBusy` at 240 minutes is a reportable cap, not permission to redeploy a busy runner. The operator explicitly authorizes stopping the remaining **server2** sessions at this cap: prefer a resumable owner/session stop; for each exact remaining seat, use `pwsh -NoProfile -File scripts/runner-slots.ps1 release -RunnerId server2 -SessionId <guid> -Reason 'CARD-0934 four-hour drain cap'`. Record the IDs and effects, recheck all counters, and rerun `drain-old`; if anything remains or a stop refuses, stop and report. Do not kill sessions on other runners. |
| 6. Upgrade old | Run `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase redeploy-old` only after step 5's zero gate. The phase runs `deploy-parent`, verifies mounts/cache and `buildVersion`, then clears old's drain. It accepts the running container's own generation and requires the new one after recreate. | If it refuses, keep temp accepting and old drained; use the retained rollback image and the [rollback procedure](#shared-server2-runner-caches-card-0849) only through a reviewed recovery. Do not clear an unverified old runner. |
| 7. Smoke upgraded old | Run the command block below for `server2`, a sanctioned `Plan` canary pinned with `-Runner server2`, and `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both -Sha <sha>`. | `redeploy-old` already clears old's drain after its own host checks, before this separate canary. If this gate fails, immediately drain old toward accepting temp with `pwsh -NoProfile -File scripts/runner-drain.ps1 drain -RunnerId server2 -RedirectTo server2-temp -Reason 'post-upgrade smoke failed'`; stop and report. CARD-0935 tracks a separate canary-before-promotion gate. |
| 8. Return scheduling and drain temp | Once old passes step 7 and accepts work, run `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase drain-temp -WaitIdleMinutes 240`. This sets temp `redirectTo=server2` and `retireWhenIdle=true`; automatic placement uses old again. Verify old accepting and temp drained/retired/offline, then prove temp container absence. The phase observes exit and removes only proven owned exited runner/state-init IDs without volumes. Retirement and exit share one deadline from `WaitIdleMinutes`; `TempContainerExitTimeout` removes nothing live. | If the phase refuses, leave old accepting and temp in its observed state; report the code. If a rollback is needed before temp retires, clear temp's drain only after verifying old remains accepting; do not start two drains. |
| 9. Retire temp | Always run `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase retire-temp` immediately after successful gate 8, including already-absent cleanup; reclaim its volumes under [Volume recycling and disk reclaim](#volume-recycling-and-disk-reclaim-card-1008). Retain the rollout receipts. | The retired, offline, absent placeholder may have null live inventory only with the complete host proof. Other refusals stop the rollout. |

At gate 1, `deploy-temp` accepts either a retired temp placeholder or a cleared offline
one, whether its drain is set or clear. For a retired start it checks offline zero work,
then POSTs `/drain/clear` with reason `CARD-0948 retired temp placeholder reactivation`,
and POSTs `/drain` with `redirectTo=server2`, `retireWhenIdle=false`. A cleared start
establishes that same non-retiring drain if needed. Both paths hold new work away from
temp during cache seed, container start, and cache verification. A connected temp can be
`dispatchEligible=true` while draining; only after verification does the phase POST the
final `/drain/clear` and wait for `acceptingNewWork=true` at the requested SHA.
An offline slot can retain its prior `buildVersion=<sha>`; the phase still starts a new
container when it is not dispatch eligible.
The phase writes `temp-retirement-clear.json` under its printed
`.antiphon/rolling-server2/<run-id>/` receipt after a successful retirement clear; it
records the original `retiredAt` and clear reason. The server drain state records the
subsequent hold reason. Retain both with the host case receipts. Do not manually clear
the placeholder before this phase.

CARD-0953 permits same-AppHost reuse after a stamped retirement is explicitly cleared.
That clear authorizes one replacement registration, including while the verification
hold drain is set. A different runner store id is admitted only after the old connection
is detached, its registration lease expires, and a full lease window passes after its
last disconnect. The old store and boot binding remain until registration succeeds.
A successful same-store reconnect consumes the authorization too. An ordinary drain
clear or an offline standing runner does not authorize a foreign store; an attached
connection still refuses it even after heartbeat expiry. `StoreMismatch` remains a
gate 1 stop condition: inspect retirement, connection and lease evidence rather than
replacing the standing runner or bypassing identity admission.

For either smoke gate, run the following from the desktop, replacing `<runner>` with `server2-temp`
or `server2` and using the matching Compose project name (`antiphon-runner-temp` or
`antiphon-runner`). The status command proves the registered full SHA; the Docker commands prove
Codex, PowerShell, and .NET actually execute inside that runner:

```powershell
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId <runner>
ssh mc@server2 'c=$(docker ps -q --filter label=com.docker.compose.project=<project> --filter label=com.docker.compose.service=session-runner); test -n "$c" && docker exec "$c" codex --version && docker exec "$c" pwsh --version && docker exec "$c" dotnet --version'
```

Use `scripts/delegate.ps1 -Role Plan -Worktree -Runner <runner> -Card <sanctioned-canary-card>
-Goal <bounded-canary-goal>` for the canary, then `scripts/delegate.ps1 -Status <task-id>` to
confirm it started, ran, and produced a report on the intended runner. If a canary cannot be
sanctioned, stop before the next handoff. `runner-defaults` may still name `server2` throughout;
the drain redirect, rather than a settings write, is the current scheduling switch.

### Failed temp deployment and abandoning a rollout (CARD-0957/0958)

`deploy-temp` requires an absent temp Compose project before changing its drain or
retirement. Its read-only SSH census includes stopped containers and refuses
`TempContainersRemain` or `TempContainerCensusUnavailable` before clearing a retired
placeholder. It also requires old to remain dispatch eligible, accepting, and not
retired or draining (`OldRunnerRedirectNotEligible`). The host repeats the absence
check before deployment and on cold-marker reuse. An available temp already at the
requested SHA waits for dispatch eligibility and verifies caches without reseeding.
A retirement stamp surviving the hold refuses `TempRunnerRetiredDuringHold` before
Seed; record it and stop. A failed hold POST after retirement clear is safe only
because the earlier census established absence; preserve the failure receipt.

A full donor container belongs to ordinary Seed's maintenance flow. Deploy-temp
refuses an existing donor with `TempContainersRemain` and refuses `-SavedDonor` with
`TempSavedDonorRequiresMaintenance`; run the documented ordinary Seed first under
its drained-consumer gates. Do not drain the standing runner just to satisfy Seed
during a beside-old rolling rollout.

A failed deploy-temp can leave a non-retiring hold (`draining=true`,
`retireWhenIdle=false`, redirect to `server2`). Normal `drain-temp` and
`retire-temp` intentionally refuse this hold. To **abandon** this rollout, first
verify that old is healthy and accepting and temp has zero bound `sessions` and
`queuedTasks`; `runnerSessions` must be zero, or null only when temp is offline.
Run the following host census and require success with **empty output**:

```powershell
ssh mc@server2 'docker ps -aq --filter label=com.docker.compose.project=antiphon-runner-temp'
```

If any container remains, stop and report its ID and the deployment receipt for a
reviewed recovery; this procedure never removes or stops a leftover container.
With absence confirmed, convert the hold into retirement explicitly:

```powershell
pwsh -NoProfile -File scripts/runner-drain.ps1 drain -RunnerId server2-temp -RedirectTo server2 -RetireWhenIdle -Reason 'abandon failed rolling rollout after confirmed temp absence'
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2-temp
```

Wait until `retiredAt` is non-null, `draining=true`, `retireWhenIdle=true`, and
`redirectTo=server2`. Preserve the status and census with the failed phase receipt.
The absent offline placeholder may keep `runnerSessions=null`; `retire-temp` accepts
that shape only with its retirement, counter, routing, work and current host-absence
proof. Run `retire-temp` and retain its volume and disk receipt. Preserve
the retired absent row; the next `deploy-temp` supports it. Unknown live inventory
without confirmed absence still stops cleanup. Do not clear the hold to abandon a rollout.

### Volume recycling and disk reclaim (CARD-1008)

Every mutating rolling phase freshly checks outer-host jq and persists its
`host-jq-<phase>.json` receipt **before** status, POST, cache seed, deploy, stop or
removal. This includes same-SHA retries, `-DryRun`, `-ResumeRecycle` and each entry
of `all`; none installs jq implicitly. Follow the explicit check/provision sequence
above before starting, and stop on any prerequisite refusal. Runner-image jq is
separate. Direct host recycling retains its final `RecycleToolsMissing` refusal
if jq disappears after preflight or the wrapper is bypassed; a previous receipt
never authorizes a later phase.

The wrapper and host census each walk two server-filtered closures, following
`excluded.byProject` with the same filter and reconciling counts. Open work is
`Queued/Dispatched/Working/Blocked`; a parked task is still Blocked. Any such row
bound to the target refuses `RecycleBoundTasks <id> status=<status> runner=<runner>`.
The independent `landPending=true` closure refuses `RecycleLandInFlight <id>` even
for an old Succeeded task. Failed/Canceled/Succeeded rows without a pending land
are not bound work; their worktrees remain protected by the publication audit below.
No time window hides an older pending land. Both closures must agree across two passes.

Every read has a 60-second budget. `RecycleTaskCensusUnknown` keeps its stable first
token and appends `cause=Timeout|Http|Transport|Empty|Malformed|UnfilteredRow|Unstable|ScopeLimit`,
with safe `path=`, `status=`, `field=` or `scopes=` details where applicable. Detail/summary
disagreement or invalid land proof remains `RecycleLandUnknown`. Project resolution
uses `RecycleProjectUnresolved` and its cause. Each census retains approved read
timings/counts and reduced task/land facts; no task prose, credentials or response body
is retained. A refusal stops the phase before destructive work, including on resume.

Always retire `server2-temp` once scheduling has moved back to the verified, accepting
main runner. After drain and the checks below, temp is disposable: `compose_temp down -v`
removes all four private volumes (work, runner-tmp, dind-data, runner-state). This is
part of the rollout's disk reclaim, not a reason to retain temp for rollback.

Main volume recycling is **scripted only**: the rolling script implements recycling
`antiphon-runner_work`, `antiphon-runner_runner-tmp` and `antiphon-runner_dind-data`
inside the `redeploy-old` / `deploy-parent` flow, after its admission checks and
controlled stop. Do not stop or remove main's container or recycle its volumes by
hand. A manually stopped main without this operation's journal reports `runnerSessions=null` while
still advertising the old SHA: `Assert-ZeroCounters` makes `redeploy-old` refuse
with `RunnerCounterUnknown server2 runnerSessions` before `deploy-parent` runs.
A matching `-ResumeRecycle <operation-id>` delegates proof of this script's own
stop/removal to the saved host journal; a generic offline main remains refused.
Never use blanket `down -v` on main. Its only documented manual disk reclaim is
the in-container cleanup below, with the runner kept **running**.

| Volume | What it holds | Default on retire/replace | Consequence of recreating |
|---|---|---|---|
| `antiphon-runner-temp_work` | Temp task worktrees and mirrors | Remove on temp retirement | Empty workspace; recover unpublished work first |
| `antiphon-runner-temp_runner-tmp` | Temp `/tmp` | Remove on temp retirement | Image `/tmp`, including `/tmp/antiphon-pty-hosts`, copies in on first mount (CARD-0827) |
| `antiphon-runner-temp_dind-data` | Temp nested Docker data | Remove on temp retirement | Nested images and containers must be rebuilt |
| `antiphon-runner-temp_runner-state` | Temp identity/store and volume-backed provider state | Remove on temp retirement | New store; next deployment uses explicit retirement clear and lease admission (CARD-0953) |
| `antiphon-runner_work` | Main task worktrees and mirrors | Recycle inside scripted replacement | Empty workspace; recover unpublished work first |
| `antiphon-runner_runner-tmp` | Main `/tmp` | Recycle inside scripted replacement | Image `/tmp`, including `/tmp/antiphon-pty-hosts`, copies in on first mount; never substitute a name-pattern sweep |
| `antiphon-runner_dind-data` | Main nested Docker data | Recycle inside scripted replacement | Nested images and containers must be rebuilt |
| `antiphon-runner_runner-state` | Main runner identity/store and volume-backed provider state | Preserve; opt-in deferred to CARD-1010 | A different store hits `StoreMismatch`; retire, explicitly clear retirement, allow connection detachment and lease expiry, then re-register under CARD-0953 |
| `antiphon-runner-cache-nuget-packages` | Shared NuGet packages | Preserve; opt-in deferred to CARD-1010 | CARD-0912 cold Seed required; minutes to an hour, best effort |
| `antiphon-runner-cache-nuget-scratch` | Shared NuGet lock scratch | Preserve; opt-in deferred to CARD-1010 | Recreate through the cache maintenance/Seed procedure |
| `antiphon-runner-cache-npm-content` | Shared npm package content | Preserve; opt-in deferred to CARD-1010 | CARD-0912 cold Seed required; minutes to an hour, best effort |

State/cache opt-in flags are not shipped by CARD-1008; `-RecycleRunnerState` and
`-RecycleCaches` are rejected by parameter binding. CARD-1010 owns them. Default
replacement preserves main state, all three caches and the seed marker, and invokes
no cold seed.

The three shared cache volumes remain external to temp's `down -v`. Any cache
recreation must finish the documented Seed and `verify-runner-caches` validation;
empty caches are not a passing cache gate. State/cache opt-in does not authorize
prune, deletion of deployment markers or the donor tar, or bypass of admission guards.

Before **any** volume removal, require completed drain, zero sessions and queued
tasks, no routing or pins to that runner, no land in flight, a stopped container,
and no container reference to each volume (including exited state-init containers).
A fresh non-null zero `runnerSessions` is required while a runner container exists.
For an already-retired, offline, absent temp only, null is expected: prove absence
with the host census and require `sessions=0`, `queuedTasks=0`, `retiredAt` set,
`draining=true`, `retireWhenIdle=true`, `redirectTo=server2`, and nothing routed there.
Unknown counters or failed observations otherwise stop removal.

Before removing work, apply CARD-0831: enumerate every runner-side mirror and its
worktrees, run `git worktree list --porcelain` in each repository, and run
`git -C <worktree-or-mirror-path> rev-list --count HEAD --not --remotes` for every
listed worktree and mirror HEAD. Check all local branch tips for unpublished commits
as well; origin refs must be current enough to establish publication. Inspect
Blocked/Failed tasks bound to this runner, including their branches and workspaces.
A nonzero count or unique unpublished work stops removal: publish or recover it first
and record the task IDs, paths and results. Perform this inventory before stopping
and removing the container, while its workspace can still be read; never mount the
volume in a helper container during the final unreferenced-volume check.
Run Git as the runner user (UID 1654), not root; root encounters `dubious ownership`,
and an ignored Git failure can masquerade as a zero unpublished-commit count.
Require successful command exits and current origin refs proving every commit is
published, rather than trusting a zero against an unrelated remote.

A blobless promisor checkout, including the deploy seed `git clone --filter=blob:none --no-checkout`, is audited with `GIT_NO_LAZY_FETCH=1` and system/global Git configuration disabled for every Git invocation. The audit classifies every entry of the common Git directory and of every linked worktree's own Git directory (`worktrees/<id>`) against a closed list; an entry outside it refuses `RecycleGitAuditUnknown`. Loose and packed refs, every HEAD (also bare HEAD), both sides of every reflog entry in the common directory and in each linked worktree's own logs, and the ORIG_HEAD, FETCH_HEAD, REBASE_HEAD, BISECT_HEAD, AUTO_MERGE and MERGE_AUTOSTASH pseudorefs must prove publication against current origin heads; a pseudoref naming a missing object refuses unknown. In-progress merge, cherry-pick, revert, rebase, sequencer, bisect or notes-merge state, stale lock files, replace refs, reftable ref storage, submodule Git directories and an index in a bare directory refuse unknown. Objects that no location names are outside the audit, as they are for `git gc`. Reflog-only amended/rebased commits remain recoverable work: unknown abandonment refuses. A full commit/tree traversal, without publication exclusions, refuses missing required objects as `RecycleGitAuditUnknown`; absent blobs are permitted. Noncommit tips, unsupported index modes and grafts refuse unknown. Promisor configuration alone is permitted.

A no-index checkout is accepted only with no content outside its top-level .git, no stash, and status containing at most HEAD deletions. This matches the deploy seed; an emptied used checkout is equivalent because there are no worktree/index bytes left to lose, while all retained Git tips still require publication. Any file, including ignored content, refuses `RecycleWorktreeDirty`. Existing indexes are checked for hidden-entry flags and every file/symlink is hashed against its index object ID without trusting timestamps. Sparse/assume-unchanged/skip-worktree states refuse; raw-byte normalization differences also conservatively refuse. Unpublished recoverable commits refuse `RecycleUnpublishedWork`.

The receipt records `repositories` and `partial` on success. On refusal `auditFailure` names the check, exit status and redacted repository path relative to the work volume; a first refusal also initializes `audit`. A saved successful `audit` is never overwritten by failure diagnostics, so unchanged publication proof can resume after connectivity recovers. `GithubTokenAbsent` stays a warning: this audit does not read the token. The token is used only for secondary receive-pack probes.

Preview each destructive phase with `-DryRun`; `all` is not a preview phase. The
preview reads inventories and prints the exact targets, preserved names and
`auditPending=true` when an offline audit would need a helper. It grants no apply
authority. Keep the no-new-land interval throughout reclaim. Apply obtains fresh
proofs, stops/removes only inspected owned container IDs, and never prunes.

Every attempt retains a schema-1 host journal under the deployment evidence root
outside the recycled volumes and a copied receipt under
`.antiphon/rolling-server2/<run-id>/`. `C1008_RECYCLE` records removed/alreadyAbsent
counts, before/after available bytes and a signed delta on Docker's data-root
filesystem. Reclamation occurs before main's allocation budget gate; safe temp
retirement does not require a warm marker or 20 GiB free.

After any stop, removal, seed/up or receipt-copy failure, keep main drained and temp
accepting. Use the recorded operation ID with the same full source SHA and phase:

`pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase redeploy-old -ResumeRecycle <operation-id>`

Resume rechecks source, options, project, Compose, retirement/store, publication
audit and original/recreated generations. It never deletes a replacement
generation. Same-SHA retries refuse an unfinished journal until explicitly resumed.
Recovery never commits, publishes, resets or salvages suspicious work.

#### Manual main cleanup while running

Keep main up and retain its container and every volume. From the canonical desktop
checkout, record main's status, container ID/start time, disk free space and an exact
candidate list in the rollout receipt. Inspect the fleet pipeline for lands, routing
and live sessions, and the complete project/board-scoped task listings; follow every
page. Reclaim only terminal task worktrees whose commits and local branch tips are
all on current origin refs, stale build outputs, and runner `/tmp` entries with no
open handles. Apply CARD-0831's mirror/worktree inventory above to each candidate;
Blocked/Failed tasks need publication or recovery first.

Use `docker exec -u 1654:1654` for both Git checks and cleanup, with main's exact
census container ID. For each exact candidate worktree, inspect:

```powershell
ssh mc@server2 'docker exec -u 1654:1654 <main-container-id> git -C <worktree-path> status --porcelain --untracked-files=all'
ssh mc@server2 'docker exec -u 1654:1654 <main-container-id> git -C <worktree-path> rev-list --count HEAD --not --remotes=origin'
ssh mc@server2 'docker exec -u 1654:1654 <main-container-id> git -C <mirror-path> worktree list --porcelain'
```

Refuse unique unpushed commits, modified tracked files, unrecovered untracked work,
live process paths (including working directories and open handles), lock files with
a live owner PID, and credential-like names. An unavailable ownership or handle
check stops deletion. Preserve `/tmp/antiphon-pty-hosts`, provider state, credentials,
deployment markers and donor tars; never sweep by name pattern. Recheck ownership
immediately before each removal. Remove an approved terminal worktree through its
owning mirror without force, as the runner user:

```powershell
ssh mc@server2 'docker exec -u 1654:1654 <main-container-id> git -C <mirror-path> worktree remove <approved-exact-worktree-path>'
ssh mc@server2 'docker exec -u 1654:1654 <main-container-id> rm -r -- <approved-exact-stale-build-output-or-tmp-entry>'
```

Use only individually reviewed, nonempty absolute paths for the second command;
never substitute a volume root, glob or possibly-empty variable. Verify main's
container ID/start time is unchanged and it remains up, and record disk free space
before/after with `df -Pk` in the container. This procedure does not remove volumes
or stop main before `redeploy-old`.

#### Retired-temp recovery (CARD-0994)

For an exited retired temp left by an older rollout, run normal `retire-temp`
from the canonical desktop checkout. Its container-only `retire-temp-containers`
host case rechecks typed retirement, main admission, complete task/land census,
all-state Docker ownership and production mount topology under the rollout lock.
Only exited runner/state-init IDs are removed, using `docker rm -- <full-id>`
without force or a volume flag. A live container refuses `TempContainerStillRunning`;
wait for normal shutdown and rerun. This does not stop, kill or sweep containers.
A non-retiring failed-deploy hold uses the abandonment procedure above.

After retaining the matching cleanup receipt, the wrapper re-reads status and
requires fresh host absence before the independent C1008 volume gate. Explicit
null live inventory is accepted only for a retired, offline, absent temp with
zero bound/queued work. The pinned original image from the source/project/retirement
receipt supplies the offline UID-1654 publication audit; unavailable image or
unpublished/dirty work preserves the volumes. A separate gate-9 invocation uses
the matching receipt across run IDs. Container removal retains all four private
volumes, provider binds, caches and images until this audit passes.

`retire-temp -DryRun` previews exact container IDs and four volume targets.
With exited containers present, volume proof is pending confirmed absence and the
offline audit is pending; no audit helper, removal or routing change occurs.
An absent project uses the existing C1008 preview. `-ResumeRecycle` uses only its
original journal and strict absence proof; it never starts new container cleanup
or removes a replacement generation. A failed container stage has not started
recycling: retry normal `retire-temp`, preserving exact partial removal receipts.

After gate 9, prove all four temp private volumes and containers absent, main's
container ID/start time and retained main state/cache identities unchanged, main
accepting, and temp still retired/offline without bound work. Keep cleanup and
volume/disk receipts outside the removed volumes. The next `deploy-temp` retains
its independent absence-before-retirement-clear admission. Docker prune, other
volumes, markers and donor archives remain human-gated.

Evidence, 2026-10-02/03: server2 reached 98% disk with 17.6 GiB free, below the
20 GiB `CacheDiskLow` gate. Main work occupied 218 GB (116 stale terminal worktrees
accounted for 100.7 GiB), runner-tmp 38.6 GB and dind-data 17 GB; retired temp volumes
occupied another 76 GB + 7.3 GB. Cleanup and temp-volume reclaim increased free space
from 17.6 GiB to 272 GiB (60% used).

### Mount generations and the rolling proofs (CARD-1105)

`redeploy-old` proves the containers it stops against the generation those containers were created
from, then proves the replacement against the generation this checkout renders. `retire-temp`
proves any owned temp containers against that same previous generation and does not start a
replacement runner. The previous generation is `SOURCE_REVISION` in the stack env (`SERVER2_ENV` on
main, `SERVER2_TEMP_ENV` on temp). That value must be 40 lowercase hex and equal the runner status
`buildVersion`. When the project owns exactly one session-runner image, `docker image inspect` of
`antiphon-server2/session-testing:<sha12>` must equal that container's image id. A missing stack
file, a value that is not 40 lowercase hex, a status body that cannot be read, a git or render
failure, an unknown bind target, or a structurally invalid previous Compose file refuses
`RecycleGenerationUnknown`. A `buildVersion` that is not the stack SHA, or a session-runner image
id that is not the inspected tag, refuses `RecycleGenerationMismatch`. No owned session-runner or
state-init means the previous generation is not derived.

A state-init-only temp binds by SOURCE_REVISION alone. The stack SHA must still be 40 lowercase
hex and equal the temp registration `buildVersion`; the recorded image tag and image id are null
and no image is inspected. The image leg applies only while a session-runner remains. Main still
requires exactly one session-runner image and refuses `RecycleGenerationUnknown` when it has none.
`retire-temp-containers` and `retire-temp-runner` both reach this proof through
`c1008_previous_generation` (`retire-temp-runner` calls `c1008_recycle`), so neither keeps a
stricter image refusal for that state-init-only shape.

The target roster is the hard-coded contract in `c1008_compose_model`, including the read-only
directory bind `/run/antiphon/github-token`. `RecycleComposeMismatch` refuses a target render
failure or a declaration that is not that roster. Host Compose v2.18.1 `docker compose config --format json`
omits a default service-secret target and emits secret declarations as name, file, and external
false. A missing target defaults to the source name, and an explicit relative target is honoured
under `/run/secrets/`. That shape is pinned by `scripts/fixtures/c1105-compose-v218-config.json`. The same token refuses a byte mismatch between
the materialized Compose file and `SERVER2_COMPOSE`: that comparison runs before the recycle lock
and before any removal, and `deploy-parent` compares the same two files again before `compose up`.
The running proof prints
`C1008_GENERATION project=<p> previous=<sha|none> target=<sha> previousMounts=<n> targetMounts=<n>`.

Inside `c1008_recycle`, `RecycleGenerationUnknown` and `RecycleGenerationMismatch` happen before
the recycle lock and before any recycle journal. A foreign extra mount is
`RecycleContainerStateUnknown` after that lock, before the recycle journal is written, and before
any `docker stop`; the volumes stay. `retire-temp-containers` takes the rollout lock and writes
its pending container receipt first. A generation refusal then marks that receipt refused and does
not `docker rm`.

Adding a bind: add it to the target roster in c1008_compose_model and to the bind-kind table in
the same commit as the Compose change. The previous generation is rendered from Git. It needs no
separate roster, but the bind-kind table must already name every bind that file contains. A target
the table does not name refuses `RecycleGenerationUnknown` the next time a generation containing
it is recycled.

Removing a bind: take it out of the target roster and the Compose file, keep its bind-kind entry
and keep exporting its host-path variable for at least one generation, because the previous
Compose file still interpolates that path. Moving a host path is a two-generation change. A
single-step move renders the previous file with the new path, which does not match the container,
and refuses `RecycleContainerStateUnknown`.

A `redeploy-old` rollback to an older SHA works only when that SHA contains this fix. An older SHA
runs the pre-fix script, which requires the target mount count of the containers it stops, so a
container that lacks the new bind refuses on mount count. Accepting temp remains the live rollback.

## Shared server2 runner caches (CARD-0849)

The `session-testing` image now carries the .NET 9.0.20 `Microsoft.NETCore.App.Ref`,
`Microsoft.AspNetCore.App.Ref`, and `Microsoft.NETCore.App.Host.linux-x64` packs in
`/usr/share/dotnet/packs`. Its SDK remains 10.0.401, selected by `global.json`. The
packs come from Microsoft's SDK 9.0.318 Linux x64 archive, pinned to the SHA-512 in
the [official .NET 9 release record](https://builds.dotnet.microsoft.com/dotnet/release-metadata/9.0/releases.json)
and checked before extraction. Archive ownership is discarded and the native apphost
template is set to 0755 so uid 1654 can execute it. Only those three packs are copied into the final image;
the .NET 9 SDK is not installed alongside SDK 10. `scripts/verify-card0660-codex-image.ps1`
checks eight Codex rows, Grok, jq and, for `session-testing`, `net9-offline`:
**11 rows**. The native row runs as uid 1654 with `--network none`, fresh task-owned
package and scratch volumes mounted at the production cache destinations with
`volume-nocopy`, and a private empty home. It clears sources and fallback folders,
uses all three image packs (including a compiled ASP.NET type), and records actual
SDK/runtime, image ID, source SHA, restore/build/native exits and the exact success
token. The controller holds the build slot; the disconnected child acquires no lease.
Ordinary `PackageReference` packages still need online restore or the best-effort
shared cache. Qualify the image independently of volumes-only cold Seed, then deploy
to the temporary runner within the approved rolling window.

The Grok row requires `grok 1.0.41 (<12 lowercase hex>)`, optionally followed by
` [stable]`, with no other tokens, exit 0, and empty stderr. The image install
removes the installer's home, so its binary prints no channel tag. CARD-0986 Review
observed `grok 1.0.41 (4220f3b224a6)`; that hash records the observed build and is
not a pin. Qualification follows the 1.0.41 dashboard at 120x30.

`docker-compose.server2-runner.yml` declares three external local Docker volumes shared by
the `antiphon-runner` and `antiphon-runner-temp` Compose projects:

| Volume | Container path | Purpose |
|---|---|---|
| `antiphon-runner-cache-nuget-packages` | `/home/app/.nuget/packages` | NuGet global packages |
| `antiphon-runner-cache-nuget-scratch` | `/var/cache/antiphon/nuget-scratch` | NuGet cross-process filesystem locks |
| `antiphon-runner-cache-npm-content` | `/home/app/.npm/_cacache` | npm package content |

Each mount uses `volume.nocopy: true`. The runner sets `NUGET_PACKAGES`, `NUGET_SCRATCH`
and `NPM_CONFIG_CACHE` to those paths. The shared scratch path is part of the package-cache
contract: a process must use the shared package and scratch pair together, or a fully
private pair. Worktrees, `obj`, `bin`, `node_modules`, `/state`, `/work`, `/tmp`, provider
homes, configuration and credentials remain outside these caches. In particular, each
runner project's `runner-tmp` volume stays private and `/tmp` stays mode 1777.

The host helper accepts only these names, the local driver, empty driver options and
labels `io.antiphon.owner=server2-runner`, `io.antiphon.cache-schema=1` and the matching
cache role. It initializes roots as uid/gid 1654 at mode 0700 and probes create, rename
and delete as that uid. A foreign volume, unsafe root or unmarked content is a refusal.
The maintenance lock is `/home/mc/antiphon-server2/locks/cache-maintenance.lock`.
These external volumes survive `docker compose down -v`; that command still removes the
temp project's private work, state, dind and tmp volumes.
The image creates `/home/app/.nuget` and `/home/app/.npm` for uid 1654 before Docker
mounts their child cache volumes. This lets an offline restore create NuGet's home files.

### Volumes-only cold first seed

When the three external volumes have not been populated, the operator can seed them
while the standing `server2` runner remains accepting work:

```powershell
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed -Cold
```

Run this only from the trusted desktop checkout at the reviewed full
`C849_DEPLOY_SHA`. `-Cold` is valid only for Seed and cannot be combined with
`-SavedDonor`. The host maintenance lock protects this supported lane. Before
any volume is created, it checks the retired temp status, one identified main
container, every container's mounts including stopped containers and bind
ancestors, and all three volume roots. Existing roots must be correctly
labelled, owned by uid/gid 1654, mode 0700 and empty, including hidden files.
The host repeats these checks around initialization, each uid-1654 canary
create/rename/delete probe, and marker publication. A failed probe removes only
its owned helper and canary; unaccepted empty volumes may remain for diagnosis.
There is no package copy, restore, apphost build, smoke or build-slot lease.

Success writes a distinct `schema=2`, `kind=cold` marker and reports
`C849_SEED kind=cold ready=true writable=3`. It carries the reviewed source SHA,
inspected image identity and the three fixed volume names, with no payload hash
or recovery directory. Normal `deploy-temp` reuses this marker without a donor
and verifies shared/private mounts, `/tmp` mode and uid writability before temp
admission. `Both` and `Retired` accept cold receipts without apphost smoke or
package hash; a valid cold marker remains usable as ordinary builds populate
the caches. Their full-marker receipts retain the prior smoke/hash checks.
The cold marker's `image=` is seed-time provenance checked by SHA-256 format;
normal superseded-image cleanup may remove that image. Later cache probes use
the requested deployment image or fall back to an identified standing runner
image when the historical seed image is absent. Cold Retired verification records
the resolved, locally available helper image in its rollback receipt. Full-marker
Retired verification still requires the retained seed/recovery image.
Reset, Prune, and explicit saved/live donor maintenance require a full marker;
they refuse a cold marker with `CacheFullSeedRequired`. An absent marker never
starts cold creation implicitly. The standing runner's drain/zero gate for
redeploy-old remains unchanged. After temp verification, complete a canary Plan
task on temp before the explicit `drain-old` phase.

Package contents are best effort. Cache misses may make the first builds slower,
but missing package payload does not block admission; wrong mounts or unwritable
roots do. An optional pre-redeploy snapshot of old package/npm trees can be
saved after drain-old and before redeploy-old. It is not a Seed source or a
rollout gate. Keep the existing saved archive untouched.

### Legacy full seed and maintenance

For a full-marker seed, refresh both runner statuses and cache inventory. Wait
for the already drained temp runner to reach fresh non-null zero `sessions`,
`runnerSessions` and `queuedTasks`. Use the explicit Seed front door from the reviewed
desktop checkout with `C849_DEPLOY_SHA` set to its full landed SHA. The host helper stops
only that idle donor container, copies the NuGet packages and npm content from its
writable layer, checks complete ordinary package versions, and starts the same old
container again under its drain after the private-cache apphost probe and recovery copy.
Do not replace or retire that donor when Seed has refused or before its reconnect with
fresh zero counters is recorded. No `/tmp` contents or NuGet scratch locks are copied.
When the old temp container has already been retired, import the operator's saved cache
archive explicitly. From the reviewed, landed desktop checkout, set `C849_DEPLOY_SHA`
to that checkout's full SHA and run:

```powershell
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed -SavedDonor /home/mc/runner-cache-donor/temp-runner-cache.tar
```

`-SavedDonor` is an absolute path on server2, never a desktop file. A directory is also
accepted. Its cache roots may be `packages` and `npm`, `.nuget/packages` and
`.npm/_cacache`, or `home/app/.nuget/packages` and `home/app/.npm/_cacache`. The
archive is inspected before extraction; links, traversal, other entries and oversized
content refuse. Both runner statuses must be drained with zero counters (only the
retired temp's disconnected `runnerSessions` may be null), the temp project must have
no containers, all cache volumes must have no attached containers, and the build broker
must be idle. Seed checks volume identities, complete generic package versions, npm
integrity and a leased uid-1654 native apphost build using image packs and private
empty caches. At least one complete ordinary version is required; incomplete versions
are removed only from staging. The saved archive stays untouched.

New full markers use schema 3 and bind a deterministic NUL-delimited recovery manifest
with a SHA-256 digest. The manifest includes both package and npm trees, regular-file
bytes, sizes and executable bits, plus empty directories. Import comparison precedes
smoke, recovery retention, donor reconnect and atomic publication. Readiness validates
the retained recovery and image, while permitting changes to the live best-effort
cache. Legacy unversioned full markers retain their payload-hash compatibility rules;
schema-2 cold remains volumes-only. Mixed, partial and unknown formats refuse.
Seed/Both/Retired receipts carry schema, digest type/value and smoke status. Marker
reuse reports `smoke=not-run`. Both requires matching typed digests.

Schema-3 Prune validates retained recovery before deletion, then performs the existing
leased ordinary refill, budget check and image-based smoke. It does not refill named
framework packages merely to make native smoke work. Legacy Prune retains its bounded
framework recovery. Admission stays held on success and failure.

If the archive lives elsewhere on server2, replace the example `-SavedDonor` value
with its actual absolute, canonical host path; the host user must be able to read it.
Do not copy the archive into the checkout. A successful seed keeps a recovery tree sized to the accepted donor at the `recovery=` path in
`/home/mc/antiphon-server2/cache/seed-accepted`. Retain this tree while its full marker is accepted: removing it invalidates readiness.
Retire a recovery generation only through an explicit reviewed replacement/reset. A refused seed
normally removes its own `stage-<run-id>-<suffix>` directory; inspect the cache
directory for any leftover stage from an interrupted run and remove only that exact
stage path after confirming no Seed is running. Older root-owned stages may need a
scoped `sudo chown -R mc:mc -- <exact-stage-path>` first. Keep the saved archive until
the recovery copy is no longer needed.

If the import refuses after copying into an unmarked volume, leave both drains held and
use the Reset command below only after confirming all cache consumers are detached;
then correct the source and repeat Seed. To roll back a completed import, drain both
runners, retain the saved archive and recovery copy, and switch to the prior image only
after its cache mount and apphost smoke checks pass. An older image without baked packs
needs a separately qualified compatible rollback path and its own private cache pair;
otherwise keep the verified runner serving. When there is no saved donor, warm
a temporary runner in its private cache, drain it to zero, then use Seed without
`-SavedDonor` before deploying either runner against the shared volumes.
After temp retirement, an accepted full ready marker and its verified recovery allow the
next `deploy-temp` to reuse the caches when the status is retired or cleared offline,
its bound sessions and queue are zero, and the host confirms the temp project has no
containers. The absent live connection may make only `runnerSessions` null, regardless
of drain and `retireWhenIdle`. If a donor container exists, Seed still requires its drained
zero-counter status and reconnect receipt.

An interrupted first Seed may leave imported content without a ready marker. For that
specific state, drain both runners and wait for zero `sessions`, `runnerSessions` and
`queuedTasks`; keep the broker idle and detach every container, including stopped
containers, from all three cache volumes. From the trusted desktop checkout at the
reviewed SHA, run exactly:

```powershell
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Reset -Sha <landed-sha>
```

Reset holds the cache maintenance lock, validates all three volume names, labels,
drivers, root owners and modes, rechecks that no container uses them, and clears only
their contents. It refuses an accepted marker. Re-run `-Case Seed` after Reset. A
failed Seed removes its temporary staging directory; retained recovery generations
remain for operator inspection.

The rolling sequence remains explicit: CP-3 Fixture and inventory, Seed, `deploy-temp`,
`drain-old`, `redeploy-old`, CP-4 Both, `drain-temp`, `retire-temp`, then CP-5 Retired.
The deploy wrapper verifies real mounts before clearing either drain. Full markers also
require a uid-1654 image-pack native apphost smoke; cold markers retain their filesystem
writability checks without package smoke. A failed
verification leaves the affected runner held. CP-3/4/5 are trusted desktop and host
operations at those gates; repo tests alone do not establish live acceptance.

Run the following from the trusted desktop checkout after setting `C849_DEPLOY_SHA` to
the full reviewed, landed SHA. The front door requires checkout HEAD to equal it and
prints the local receipt directory under `.antiphon/c849-<run-id>/`. It does not start
a rolling phase or clear admission on its own.

| Gate | Command | Required receipt summary |
|---|---|---|
| Before seed/deploy, CP-3 | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture` | `C849_FIXTURE groups=9 controls=32 expectedRed=32 variants=47 expectedRedVariants=47 inventories=2 failures=0 productionMutations=0` |
| Drained temp donor, before recreation | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed` | `C849_SEED donor=server2-temp ready=true schema=3 digestType=manifest-sha256 smoke=passed recovery=retained` |
| Retired temp, saved donor available | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed -SavedDonor /home/mc/runner-cache-donor/temp-runner-cache.tar` | `C849_SEED donor=saved ready=true schema=3 digestType=manifest-sha256 smoke=passed recovery=retained` |
| After redeploy-old, before drain-temp, CP-4 | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both` | `C849_BOTH runners=2 smokes=2 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0` |
| After retire-temp, CP-5 | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Retired` | `C849_RETIRED externalVolumes=3 tempPrivateVolumes=0 mainTmpRetained=true smokes=1 rollback=retained failures=0` |

`-Case Inventory` refreshes two read-only status and mount receipts. The Fixture case
uses run-scoped Docker resources and records `PASS F-1` through `PASS F-9` and all
32 control families (`PC-01` through `PC-32`), with exactly 47 negative variants.
Both shell and front door check the full roster, including duplicates. Fixture generates
ordinary packages locally and reads production only for the two inventories. A complete
successful census and an explicitly retired temp status may record an absent temp;
lookup failures and contradictory live containers refuse. Empty-cache native execution
is positive; disposable children mask each image pack separately for the negative cases.
Its temporary containers, networks and volumes are removed by exact recorded names;
the production cache names are outside its namespace. A nonzero case or missing
receipt leaves the checkpoint pending. CP-3/4/5 are operational gates after Review,
not Code-stage repo tests.

Each front-door invocation copies filtered host evidence into its printed local
directory. The fixture summary contains source/image identity, group/control counts
and no package data. Both stores separate status, mount, seed-hash and apphost smoke
receipts for each runner. Keep those receipts with the rollout record. The full
`docker inspect` output, environment, cache archive, package metadata contents and
provider credentials do not belong in that record.

The initial size budgets are 10 GiB packages, 2 GiB npm content and 256 MiB scratch.
Review `du -s -B1` for each at every deployment, before retirement and at least weekly;
warn at 80%, and require maintenance above a budget or below 20 GiB backing-space
headroom. These are review limits, not Docker quotas. Cleanup starts with
`pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case PrunePreview` and only
then `-Case Prune -Preview <receipt-path>`. Both runners must remain drained with zero
counters, no cache consumers and no active host build leases. Preserve volume roots and
one bounded recovery generation. Refill required packages under a build lease and pass
the disconnected smoke before admission; never clear all NuGet locals, prune Docker
volumes, or remove a mount root. Preserve the recovery generation for seven days after
successful activation, then remove only its recorded path during maintenance.

For rollback, keep the previous runner and state-init image IDs under explicit rollback
tags before the rollout. Drain before replacing either container and verify the image
SHA, mounts, private `/tmp` mode and apphost before clearing its drain. Keep shared
packages and shared scratch together when a prior image can use them. If the mount design
is suspect, drain every consumer and use a private package/scratch pair seeded from the
saved recovery copy. Retain external caches for diagnosis. The explicit four-hour cap in the
staged rollout above is the only authorization here to stop exact remaining server2 sessions.

**No application service ever mounts a Docker socket.** Superseded 2026-09-22 by CARD-0604: the sibling-socket lane is retired, and with it `docker-compose.session-testing.yml` and `DOCKER_SOCKET_GID`. The netns split it caused (a mapped port lands in the HOST's namespace while the test process reads its own `localhost`) is why Testcontainers never worked on that shape.

The root `Dockerfile` publishes the server for `linux-x64` with SDK 10 and the ASP.NET 9 runtime. `docker/session-runner-grok/Dockerfile` keeps the phone-home runtime as its default target. `receipt-probe` adds FakeGrok and no Docker tooling at all. `session-testing` is the **persistent server2 runner**: it carries the whole Docker engine from one pinned tarball (`dockerd`, `containerd`, `runc`, the shims, `docker-proxy`, `ctr`), `iptables` pinned to the legacy backend (server2 is kernel 4.15 with iptables 1.6.1), `iproute2`, `openssh-client`, SDK 10 with runtimes 9 and 10, Node 22 and PowerShell. `runtime-base` (and so every runner target) carries Grok 1.0.41, Claude Code 2.1.280, codex-cli 0.160.0, and jq 1.7.1 (static jq-linux-amd64, SHA-256 `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, verified before installing root-owned mode 755 at `/usr/local/bin/jq`). Claude Code is pinned by SHA-256 `1e08503dbdf3c2cb0d706d32f3408277388d1c76ef108673e8fe42c1b322925b` and verified with `sha256sum -c` before install (CARD-0628); Codex is pinned by npm's platform-package SHA-512 and verified before unpacking (CARD-0904). No credential for either is ever baked. Every runner target needs **minimum Git 2.43**: `LandingGit`, `GuardedWorktreeRemoval` and `GuardedVerificationRemoval` run `git show-ref --exists` (new in 2.43) to tell a missing ref from a lookup error, and landing inspection fails on anything older. Debian bookworm ships 2.39.5 and bookworm-backports carries no git, so the `git-build` stage compiles Git 2.47.3 from the kernel.org tarball pinned by SHA-256 `c073471530e92b716641ea2b381fcd0ece53eea9a76a9c5415f93f89e870dd5f` (verified with `sha256sum -c` before unpacking), with `sysconfdir=/etc` so the baked `/etc/gitconfig` stays in force; `runtime-base` copies it to `/usr/local`, installs no apt git, and fails the build when `git --version` is below `GIT_MINIMUM_VERSION` (CARD-0661). Raising the pin means a new version/SHA-256 pair, never dropping the check. It starts as root under tini so `docker/session-runner-grok/dind-entrypoint.sh` can run its own `dockerd`, then drops the runner to uid 1654 with the `docker-nested` group (gid 1656). A dead daemon takes the container down; `restart: unless-stopped` brings both back.

`docker-compose.server2-runner.yml` (project `antiphon-runner`) is the persistent deployment and the only standing Antiphon process on server2 — no server, no Postgres. It is the one privileged service in the repository, it owns the `dind-data` volume as the nested daemon's store, and it requires both file secrets (`ANTIPHON_DEPLOY_KEY_FILE`, `PHONE_HOME_SECRET_FILE`) by interpolation. It also requires `CLAUDE_OAUTH_TOKEN_FILE`, the Claude setup-token file on the server2 host (0600). A deploy leaves that file alone unless `-RefreshClaudeToken` is passed; the host lane creates it empty when it is missing. It is mounted read-only at `/run/antiphon/claude-oauth-token`; `dind-entrypoint.sh` exports `CLAUDE_CODE_OAUTH_TOKEN` from it into the runner alone. No compose file lists that variable under `environment:`, where `docker inspect` would print it (CARD-0628). The throwaway stack a session stands up is the **nested** child: the unmodified base `docker-compose.yml` on the runner's own daemon, in a run-scoped project, torn down with `down -v`.

The runner mounts the named `runner-tmp` volume at `/tmp` (CARD-0827). The persistent `antiphon-runner` and temporary `antiphon-runner-temp` Compose projects each get their own volume; `compose_temp down -v` in `scripts/c590-remote.sh` removes the temporary project's volume at retirement. An existing container's writable-layer `/tmp` is not migrated when this mount is introduced. Keep cleanup restricted to an explicit allow-list of disposable families: `/tmp/antiphon-pty-hosts` is runner state and must never be swept by an `antiphon-*` name pattern. Its `bin/<stamp>` shadow copies are reused when the published host's content hash matches and the host executable and deps.json are present. If either file is missing, `ShadowCopyStore.EnsureCurrent` stages a new generation and `PtyHostLauncher` refreshes its cached path before the next launch; the damaged generation remains until normal cleanup. The runner's startup adoption sweep checks persisted manifests against host liveness, records dead hosts as exited, and removes invalid manifests; `AuditCleanupService` prunes unreferenced shadow copies and old logs. These paths tolerate stale generations, logs, and manifests after a recreate. Protect `/tmp/antiphon-pty-hosts` from external cleanup even though the launch path can repair a missing host executable or deps.json.

`PhoneHome__Capacity` on that compose file is `"10"` (CARD-0653): ten concurrent sessions on server2 (24 cores, ~98 GB RAM). The server refuses a registration or capacity push above that runner’s `PhoneHomeRunner:Runners:<id>:MaxCapacity` (default 10); legacy single-runner configuration uses `PhoneHomeRunner:MaxCapacity`. The runner counts a seat only while the session is live or occupied; exited and vanished records do not fill it. A runner-bound task stays Queued, with one deduplicated Held trace, when those seats are full, and it is checked before remote prep. Those tasks do not consume `Delegation:MaxConcurrentTasks`. `PUT /api/session-runners/{runnerId}/capacity` changes a live runner's declared value with a reason and persists it at `PhoneHome:CapacityStatePath` (default `/state/runner-capacity`); that persisted value takes precedence over the compose default at restart. Heartbeats report the applied value so the server catches up if a push confirmation times out. `PUT /api/hosts/{hostId}/budget` separately sets a server-side admission limit; effective runner admission is the minimum of that budget and the runner declaration. Lowering either limit holds new work without killing running sessions. `GET /api/session-runners/{runnerId}/slots` lists the seats the runner remembers (state, age, whether the seat counts, custody). `POST /api/session-runners/{runnerId}/slots/{sessionId}/release` and `POST /api/session-runners/{runnerId}/slots/release-orphans` each require a reason and the operator token (the `X-Antiphon-Operator-Token` header; a loopback address is not a credential because the public vhost reaches Kestrel through Caddy and Vite), kill the process tree, and delete the durable manifest so a restart does not adopt the session again. `scripts/runner-slots.ps1` calls those routes.

The same compose file sets `SessionRunner__HostStats__Volumes__0: /work` and
`SessionRunner__HostStats__Volumes__1: /state` (CARD-0718). The runner samples those volumes
every five seconds and serves `/host-stats` locally and over phone-home. Its 30-minute series
is in memory and resets on runner restart; the server's `/api/hosts/stats` cache is likewise
not a durable history. On Linux these are the worktree and state volume views, even when they
share one underlying filesystem.

The same compose file sets the server2 **host build budget** (CARD-0589): `SessionRunner__BuildSlots__MaxConcurrent: "4"`, `SessionRunner__BuildSlots__MaxCpuCount: "6"`, `SessionRunner__BuildSlots__MinAvailableMemoryMb: "16384"`. Ten seats share four concurrent build/test driver leases at `-maxcpucount:6` each, and no lease is granted below 16 GB available (24 cores, no cgroup memory cap, other services on the host). Seats are counted first, then slots, then the memory floor: a free seat can still queue its builds, and a free slot never admits a session. To change the budget, edit those values and redeploy the runner; `SessionRunner__BuildSlots__Enabled: "false"` makes every acquire answer unlimited. Check it after a deploy with `docker exec antiphon-runner-session-runner-1 curl -fsS http://127.0.0.1:8080/build-slots` (200, `budget` equal to the configured value). Delegates in the container reach it at that address, the default of `scripts/lib/build-slot.ps1` on Linux (`ANTIPHON_BUILD_SLOTS_URL` overrides it). Contract, wait lines and exit codes: [testing-and-build.md](testing-and-build.md), Build slots (CARD-0589).

### server2 runner startup identity files (CARD-0631)

Everything the persistent runner boots with that is *who it is*, rather than *what it runs*, lives on server2 under `/home/mc/antiphon-server2/secrets/`, never in the image: files owned by `mc`, plus the Codex home and GitHub token directories owned by the runner uid 1654 (CARD-0660, CARD-0817). `deploy-parent` (`scripts/c590-remote.sh`, host lane) creates missing boot paths and records only presence or non-secret values as evidence; the GitHub token file is operator-provisioned. `docker/stack.env.example` lists the compose variables.

| File on server2 | Secret? | Created / delivered by | Compose variable → mount in the runner | How the runner uses it |
|---|---|---|---|---|
| `secrets/deploy_key` (+ `.pub`) | **Yes** (private key, 0600) | `deploy-parent`, `ssh-keygen` once; the public half is registered on GitHub by the operator | `ANTIPHON_DEPLOY_KEY_FILE` → compose secret `/run/secrets/antiphon-deploy-key` | `dind-entrypoint.sh` installs it at `/run/antiphon/deploy-key` (0400 uid 1654, tmpfs); `ssh_config` names it for pushes only |
| `secrets/phone-home` | **Yes** (0600) | `deploy-parent`, `openssl rand -hex 32` once | `PHONE_HOME_SECRET_FILE` → compose secret `/run/secrets/phone-home` | Staged at `/run/antiphon/phone-home` (0400 uid 1654); `PhoneHome__SecretPath` |
| `secrets/claude_oauth_token` | **Yes** (0600, may be empty) | `deploy-parent` leaves an existing file unchanged. `-RefreshClaudeToken` (or manifest `refreshClaudeToken`, or `ANTIPHON_REFRESH_CLAUDE_TOKEN=1`) streams vault item `antiphon/server2/claude-oauth-token` over SSH stdin. The host lane creates the file empty when it is missing. A deploy does not need `BW_SESSION` | `CLAUDE_OAUTH_TOKEN_FILE` → `/run/antiphon/claude-oauth-token:ro` | Exported as `CLAUDE_CODE_OAUTH_TOKEN` into the runner alone; empty means Claude reports signed out |
| `secrets/gitconfig` | No (0644) | `deploy-parent` creates it **only when missing**, from `RUNNER_GIT_USER_NAME`/`RUNNER_GIT_USER_EMAIL` in `stack.env` (defaults `antiphon-server2-runner` / `antiphon-server2-runner@users.noreply.github.com`); an existing file is never rewritten, so edit the file itself to change the identity | `RUNNER_GIT_IDENTITY_FILE` → `/run/antiphon/gitconfig:ro` | `GIT_CONFIG_GLOBAL` names it for uid 1654 (entrypoint export, and compose `environment:` so `docker exec` sees the same). A missing, empty or incomplete file is `GitIdentityMissing`/`GitIdentityUnusable` at boot |
| `secrets/github-token/` (a directory) | **Yes** (`token` is 0400; directory 0700; owner `1654:1654`) | `ensure_runner_github_token_dir` creates only the empty directory, refuses symlinks/non-directories, re-asserts directory owner/mode without traversal, and records `github-token-present.txt`. The operator streams the vault item through `scripts/refresh-server2-github-token.ps1` | Required `RUNNER_GITHUB_TOKEN_DIR` → `/run/antiphon/github-token:ro`, inherited by server2-temp; no state-init mount | The baked HTTPS credential helper reads the file live and enforces the runner's `/run/antiphon/push-allow-list`. Missing token warns `GithubTokenAbsent`; that warning is not a recycle-audit failure, and the audit does not read the token. Directory binding makes atomic rotation visible without recreating either runner; custody, expiry and rotation are in [agent-credentials.md](agent-credentials.md) |
| `secrets/codex/` (a directory) | **Yes** (holds Codex's ChatGPT sign-in; directory 0700, owner uid 1654) | `deploy-parent` (`ensure_runner_codex_home`) creates it **empty** when missing (`sudo -n install -d -o 1654 -g 1654 -m 0700`), re-asserts owner and mode on the directory alone, refuses a symlink or non-directory (`CodexHomePathIsSymlink`, `CodexHomePathIsNotDirectory`), never reads, lists or copies it, and records only `codex-home-present.txt` / `codex-auth-present.txt` (`WARN CodexAuthAbsent` when there is no sign-in). The sign-in itself is the operator's: `codex login --device-auth` in the runner, or the one-time migration below | `RUNNER_CODEX_HOME_DIR` → `/state/codex` read-write in `session-runner`, and `/codex-home` in `state-init` | `CODEX_HOME` (the CLI and transcript tailer) and `PhoneHome__CodexHome` (the auth probe). `state-init` seeds the non-secret `config.toml` only when absent and owns the directory, never its contents. Only the server2 `state-init` sets `CODEX_HOME_REQUIRED=1`, so a missing mount refuses `CodexHomeNotMounted` (exit 43) there; the base `docker-compose.yml` (and the nested child built from it) has no Codex home and its `state-init` skips it. A directory, not a single-file mount, because Codex rewrites its credential file by rename on every token refresh, which a file bind mount cannot survive |

Grok is **not** a file of this kind: its OAuth store is `GROK_HOME/auth.json` on the `runner-state` volume (`/state/grok`), made once by an interactive `grok login` inside the container (see `docs/agent-credentials.md`). The Claude interactive-login fallback store `/state/claude` is likewise volume state.

**Codex home: one-time migration of the pre-amendment sign-in (CARD-0660).** Before the host directory existed, the ChatGPT sign-in was made on the `runner-state` volume and now sits at `/state/codex/auth.json` there (the runner's view; `state-init` sees it as `/runner-state/codex/auth.json`). The new runner mounts the host directory *over* the volume's `/state/codex`, so that copy is no longer visible inside the runner; it is still visible in the exited `state-init` container, which sees the volume at `/runner-state` and the host directory at `/codex-home`. After a `deploy-parent` of this revision (which creates `secrets/codex/` and brings the new runner up), run on server2 as `mc`, from any directory. The file streams from one container to the other through a pipe: it lands nowhere on the host except the Codex home itself, never passes through a terminal, and nothing prints it. Only do this while `/state/codex/auth.json` is absent in the runner; a later `docker cp` would replace a newer sign-in.

```sh
i=antiphon-runner-state-init-1
c=antiphon-runner-session-runner-1
docker cp "$i:/runner-state/codex/auth.json" - | docker cp - "$c:/state/codex/"
docker exec -u 0:0 "$c" chown 1654:1654 /state/codex/auth.json
docker exec -u 0:0 "$c" chmod 0600 /state/codex/auth.json
docker exec -u 1654:1654 "$c" stat -c '%u:%g %a %F' /state/codex/auth.json
```

The last line must print `1654:1654 600 regular file` (metadata only). The second `docker cp` takes no `-a`: that resolves the archive's owner by name inside the runner, whose user is `0:0`, and fails (`getent unable to find entry "0:0"`); the `chown` and `chmod` that follow set owner and mode. The copy lands in `/home/mc/antiphon-server2/secrets/codex/auth.json` on the host. If the first `docker cp` reports no such file, stop: the sign-in is not where this migration expects it, and a fresh `codex login --device-auth` in the runner is the alternative. The old copy stays on the volume, invisible to the runner; removing it is a separate operator decision.

**Why `GIT_CONFIG_GLOBAL`, not an `[include]` from `/etc/gitconfig`.** The global level outranks the image's system `/etc/gitconfig` (which keeps `core.sshCommand`, the exact Antiphon `pushInsteadOf`, and the HTTPS credential helper configuration), and it *replaces* `~/.gitconfig` and the XDG file, so an identity left in a home directory or on a volume by an earlier stopgap cannot shadow the mount. Only a repository's own config outranks it; `deploy-parent` removes a `user.name`/`user.email` it finds in the runner checkout's local config and then proves `git config --show-origin --get user.email` resolves to `file:/run/antiphon/gitconfig`, both in `/` and inside the checkout (`GitIdentityNotEffective`, `GitIdentityOverrideNotRemoved`). The mount is read-only, so a session cannot rewrite the identity later commits use.

### Runner checkout: lazy clone and deploy verification (CARD-0631)

CARD-0812 mirrors each admitted task repository through its own blobless checkout beside the
primary, for example `/work/repos/markdown-package`, while task worktrees remain flat under
`/work/worktrees/`. The desktop worktree's `origin` supplies the repository identity. The runner
admits identities under `PhoneHome:AllowedCloneSources` (default
`https://github.com/michal-ciechan/`), verifies an existing checkout's origin, and probes
secondary push access with a receive-pack dry-run before making a mirror. Typed 409 refusals are
`phone_home_repository_not_admitted`, `phone_home_repository_mismatch`, and
`phone_home_repository_push_unauthorized`; the last includes a safe failure category and remedy. The
probe defaults on through `PhoneHome:ProbeSecondaryRepositoryPushAccess`. A legacy request with
no `repository` still uses the primary checkout. `deploy-parent` seeds and verifies only the
primary checkout; secondary checkouts are lazily cloned and never seeded or verified there.
CARD-0817 keeps non-Antiphon pushes on HTTPS through the allow-list-enforcing credential
helper and the one token shared by both runners. The exact Antiphon URL keeps its SSH deploy
key; the deploy-parent seeding, primary verification and push smoke are unchanged.

The runner's repository (`PhoneHome__RunnerRepository`, default `/work/repos/antiphon` on the `work` volume) is created lazily by the runner on the first workspace mirror: an anonymous `git clone --filter=blob:none --no-checkout https://github.com/michal-ciechan/Antiphon.git` as uid 1654. Fetches stay anonymous HTTPS; only pushes go over SSH with the deploy key.

`deploy-parent` **seeds a fresh volume, then verifies** that checkout. Before `compose up` starts the runner (so no lazy mirror can be in flight), it runs `state-init` once and then a one-off `session-runner` container as uid 1654 that clones anonymously with the same command into the configured repository, only when that destination is absent or empty (`runner-checkout-seed.txt` records `seeded`, `present` or `occupied`; failures refuse `StateInitFailed` / `RunnerCheckoutSeedFailed`). The lazy clone cannot bootstrap a first deploy on its own, because phone-home may still be disabled on the server at that gate. Verification follows the health and phone-home probes and precedes any image retirement or acceptance. Every probe is `docker exec -u 1654:1654` inside the runner, against the configured repository, never the host's identically named `/work/repos/antiphon` checkout and never a child volume. Named refusals: `RunnerCheckoutMissing` (no `.git`), `RunnerCheckoutInvalid` (not a worktree rooted at that path), `RunnerCheckoutOriginMismatch` (origin is not the anonymous HTTPS URL), `RunnerCheckoutFetchFailed` (`GIT_TERMINAL_PROMPT=0 timeout --kill-after=5s 120s git fetch --no-tags origin <branch>` failed or timed out). The receipt is `runner-checkout.txt`: path, origin, branch and `FETCH_HEAD` SHA.

**Fresh-volume bootstrap.** An empty `work` volume is seeded by the first `deploy-parent` itself, as above. The seed never replaces an existing or occupied destination, so `RunnerCheckoutMissing` still refuses a non-empty directory with no `.git`; the images are then left unretired. Never accept a Missing verdict as green.

**Identity file before a restart.** `persistent-restart` ensures `~/antiphon-server2/secrets/gitconfig` (created when missing, refused when a directory, a symlink or incomplete) *before* it stops the runner, so a runner deployed before the identity mount existed is never stopped into a start that cannot bind it. A symlink at that path refuses `GitIdentityPathIsSymlink` before anything touches it; only a file the deploy creates is set to 0644, and an operator's existing file keeps its mode.

Base `docker-compose.yml` publishes only the server, on `127.0.0.1:5000` unless `ANTIPHON_BIND_ADDRESS` is set. The runner and Postgres have no published ports. `docker-compose.test.yml` is nested-only: it is composed inside a session on the nested daemon, never on the server2 host daemon.

`scripts/test-docker.ps1 -Group small|backend|all` is the foreground test entry. Its preflight refuses a daemon that is not the runner's own: `docker info` `Name` must equal the process's `hostname`, or the run is `SiblingDaemonRefused`; an unreachable daemon is `NestedDaemonUnavailable`. `scripts/verify-docker-stack.ps1 -Case <literal> -Manifest <path>` runs one checkpoint. `scripts/verify-card0604-dind-runner.ps1` boots the same image on Docker Desktop and grades the nested daemon, egress, loopback, launch, key custody, restart and the three exit-3 refusals without server2 or GitHub.

SourceLanding Mutation runs on Windows (`windows-job-v1`) or on the persistent server2 runner (`linux-cgroup-v1`, CARD-0604 D-17). The Linux backend is a root-owned cgroup the tracked child is placed in before it exists, entered through `sudo -n /usr/local/bin/antiphon-custody-enter <execution-id> -- <exe> <args>` and terminated through `antiphon-custody-kill <execution-id>`; `sudoers.d/antiphon-custody` grants uid 1654 those two commands and nothing else, and the shim execs the child under `no_new_privs`, which makes sudo and every setuid binary inert for the whole tree. The runner advertises `linux-cgroup-v1` only when `LinuxCgroupCustodyProbe` passes at startup, and never advertises `windows-job-v1`. This mechanism is entirely container-internal: the session-runner container is already privileged (it runs its own dockerd), and this constrains the uid-1654 process inside it. It does not touch server2's host sudoers. An ordinary Linux image run is still not a custody receipt -- only a tracked execution in that cgroup produces one.

The class accounting checked into `tests/linux-test-roster.json` is the executable copy of the frozen plan roster. Do not drop a class because Linux is red.

Windows CP-1 through CP-5 passed on `3569ed7cd520e3a76bafc0643feb1da0b42c73ca`. Live cases run on server2 through `scripts/c590-remote.sh`, invoked by `scripts/verify-docker-stack.ps1` when `ANTIPHON_C590_STUB` is unset. Evidence for a run is `/work/test-evidence/<run-id>/<case>/`. Stub mode still answers `UnknownCase` for a case the local guards do not implement.
PowerShell 7's `ConvertFrom-Json` returns an ISO-8601-looking manifest `tempRetiredAt` as `[datetime]`, so `Invoke-C590LiveCase` reformats it with `ToUniversalTime().ToString('o')` before validating; a case must never cast such a field directly to `[string]` (CARD-0780).
