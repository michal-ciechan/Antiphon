# Self-contained Docker stack (CARD-0590)

## Staged server2 rolling rollout (CARD-0934)

The SHA deployed to AppHost and server2 is the current `rc/YYYY-MM-DD` tip, never master
([release gates](release-gates.md)). Use one `scripts/deploy-server2.ps1 -Rolling -Sha <sha>
-Phase <name>` call per gate, with `<sha>` that tip; never use the default `-Phase all`.
The phase list in that script is the authority. Keep the
standing `server2` container accepting until `server2-temp` has passed its canary. Do not drain
both runners or drain the only accepting runner. A refusal means stop, record its exact code and
receipt under `.antiphon/rolling-server2/`, and report it; do not force a phase or improvise a
Docker replacement. The only timed stop exception is step 5 below.

<a id="continuous-dispatch-during-rollout"></a>

### Continuous dispatch during the rollout (CARD-1311)

During a staged server2 rolling rollout, keep dispatching new work to `server2-temp` until the standing `server2` is fully ready (`redeploy-old` exit 0 and smoke/Both passed); the moment it is ready send new work to `server2` and start `drain-temp` (which waits for temp's running tasks, `-WaitIdleMinutes 240`); there must always be a server2 (standing or temp) accepting work, and the orchestrator never pauses dispatch or drops below its in-flight target (6) because of a rollout. Tasks on `server2-temp` do not block `drain-temp`, it waits for them to settle.

That paragraph is the rule. The orchestrator copy is [operational autonomy](orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout). `redeploy-old` clears the standing runner's drain before gate 7's separate smoke and Both. Readiness for moving new work is that exit 0 together with smoke/Both passed, so pin new work to `server2-temp` until both have passed. Start `drain-temp` at that moment; tasks already running on temp settle under `-WaitIdleMinutes 240`.

Before starting, verify the checkout has no half-reset worktree and the running server's
`/health` and `GET /api/version` SHA match that `rc/YYYY-MM-DD` tip (see
[the autonomy policy](orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout)).
Lands keep running throughout the rollout (CARD-1353): a land is desktop git work that never
runs on, binds to or reads a session runner, and the only land a phase waits for is one bound
to the runner being recycled.
Before host qualification or gate 1, check project identity and both filtered task
closures without changing runner state:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-census
```

Expect `RECYCLE_PROJECT id=<guid> name=<name> resolvedBy=url|path|both|explicit` and
two `RECYCLE_CENSUS runner=<id> open=<n> boundOpen=<n> landPending=<n> boundLandPending=<n> scopes=<n> elapsedMs=<n>`
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
that fix on the current `rc/YYYY-MM-DD` line and restart AppHost onto that rc tip, then
verify `/api/version` against the tip.

Before cache preparation or gate 1, qualify **outer-host jq** in the non-login SSH
deployment shell. The runner image's jq does not satisfy this prerequisite.
Use that same `<sha>`:

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

Fixture's volume-backed seed verifies npm through its existing UID/GID 1654 writer.
npm creates `_logs` beside `_cacache`, so verification and checked removal must use
the same uid. A root verifier leaves logs the writer cannot remove and fails the
Fixture gate. Keep the host-backed isolated verifier separate; do not escalate
Fixture cleanup or change live cache ownership to recover this failure.

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
Keep dispatching through every gate under [Continuous dispatch](#continuous-dispatch-during-rollout).

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
ssh mc@server2 'c=$(docker ps -q --filter label=com.docker.compose.project=<project> --filter label=com.docker.compose.service=session-runner); test -n "$c" && docker exec -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex "$c" codex --version && docker exec "$c" pwsh --version && docker exec "$c" dotnet --version'
```

Codex must never be exec'd as root in the server2 runner container: use uid 1654, because a root exec leaves root-owned names under `/state/codex/tmp` that the session user cannot remove (CARD-1174).

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
The independent `landPending=true` closure refuses `RecycleLandInFlight <id>` only for a row
bound to the runner being recycled (`runnerId` equal to the target, the same predicate as
`RecycleBoundTasks`), even for an old Succeeded task, and only after a bounded wait: the
wrapper re-reads the census every 30 s for `-LandWaitMinutes` (default 30; 0 refuses on the
first read) and the host for `C1008_LAND_WAIT_SECONDS` (default 1800) every
`C1008_LAND_POLL_SECONDS` (default 30), printing `RECYCLE_LAND_WAIT runner=<id> id=<task> polls=<n>`
and `C1008_LAND_WAIT runner=<id> reason=<refusal> polls=<n>` while it waits. An expired wait
refuses `RecycleLandInFlight <id> runner=<runner> waited=<n>polls`; an invalid host bound
refuses `RecycleContextInvalid` before any read; the census receipt records the polls
(`landWait`, `landWaitPolls`). A pending land for a task on the other runner or the desktop is
read, counted and recorded in the receipt but is not part of the compared snapshot: the land
closure snapshot holds bound rows only, so a land requested for an off-runner task during
the audits cannot end `cause=Unstable` (CARD-1353). Failed/Canceled/Succeeded rows without a
pending land are not bound work; their worktrees remain protected by the publication audit
below. No time window hides an older pending land. Both closures must agree across two passes.

Every physical read attempt has a 60-second budget. Task census GETs retry only
502/503/504 and recognized timeout/reset/send/receive failures, at most three attempts
with one second between attempts (at most 182 seconds of configured request/wait
budgets per logical GET, not a total census deadline; CARD-1374). Exhaustion refuses
with `RecycleTaskCensusUnknown cause=Http attempts=3`, the last `failure` kind and
the actual `status` when known. Receipts retain every physical attempt's number,
raw outcome and timing, including failed attempts before recovery. All 4xx, other
statuses, unknown transport/DNS/TLS failures, empty bodies and malformed data refuse
immediately. Project resolution and runner status/commands keep their existing
single-attempt policy; audit and removal gates are unchanged.
`RecycleTaskCensusUnknown` keeps its stable first
token and appends `cause=Timeout|Http|Transport|Empty|Malformed|UnfilteredRow|Unstable|ScopeLimit|Drift`,
with safe `path=`, `status=`, `field=` or `scopes=` details where applicable. Detail/summary
disagreement or invalid land proof remains `RecycleLandUnknown`, except a terminal detail
(`Succeeded`, `Failed` or `Canceled`) with `landRequest` null, which is proven no-land and may
be absent from the next pass's list without `Unstable`, and except a task that first appears
on that pass with `landRequest` null, outside the `landPending` closure, and with neither land
timestamp set. An open row on another runner whose `landRequest` is `Queued`, `Held`,
`Running` or `NeedsResolution` is recorded as `{state, terminalEventId}` and counted, not
refused. CARD-1361 also permits changes among `Queued`, `Dispatched`, `Working` and
`Blocked` between list/detail reads and between the two census passes. Both observations
must describe the same off-runner task, with unchanged runner, project, scope source and
land timestamps, and explicit null or terminal-proven land evidence. Terminal proof means
`Completed`, `Superseded` or `Canceled` with a valid terminal event ID; across passes that
proof must be identical. Only comparison copies are aligned: snapshots and receipts retain
the actual list status, and the returned snapshot is the second pass. Stable off-runner
pending lands still follow CARD-1353; pending evidence cannot qualify a status change.
Bound work, unknown or missing land proof, changed non-status facts and any residual
closure difference still refuse.

CARD-1371 distinguishes malformed counts within one response from count drift between
individually valid reads. A cross-read `excluded.byProject` disagreement restarts the
complete two-pass census, at most three attempts with 30 seconds between attempts.
Persistent disagreement refuses as `cause=Drift field=excluded.byProject.count
project=<guid> expected=<integer> observed=<integer>`, choosing the first mismatching
project in sorted order. An advertisement conflict reports the first advertised count
and the later count; final reconciliation reports advertised versus collected rows.
Malformed counts still refuse immediately as `Malformed`, and binding and land guards
take precedence. Drift never enters the separate bound-land wait or permits a partial closure.
That refusal stops removal from starting. Once the journal records `ownedRemoved` or a
phase of `containersRemoved`, `removing`, `volumesRemoved`, `recreating` or `verified`,
the same drift is still retried, and the final attempt keeps the walked rows. Binding
and land guards still refuse before that acceptance. A wrapper `-ResumeRecycle` does
not treat exhausted Drift as fatal; the host census applies the journal rule. Malformed
counts and every pre-removal Drift refusal stay immediate refusals.

CARD-1326 keeps two edges of that exception. A task that appears under a nested excluded
scope still ends `RecycleTaskCensusUnknown cause=Unstable`: forgiveness rewrites the open
task, its null land proof and the scope id list only, and leaves `excluded.byProject` counts
and any scope pass 0 did not contain untouched. Each pass already reconciles those counts
with the rows it walked, so a nested appearance is a different closure. `check-census` stays
the read-only count report, so a row with `landRequestedAt` or `landStartedAt` increments
`landPending`, and `boundLandPending` when that row is bound to the reported runner. When
that row is the pass-1 appearance, or a row bound to the recycled runner appears in the
`landPending` closure, the two-pass compare refuses `cause=Unstable`; an off-runner land
appearing there is outside the compared snapshot. Mutating phases and the host census
refuse `RecycleLandInFlight` on a row bound to the recycled runner, after the bounded wait
above. Project resolution
uses `RecycleProjectUnresolved` and its cause. Each census retains approved read
timings/counts and reduced task/land facts; no task prose, credentials or response body
is retained. A refusal stops the phase before destructive work, including on resume,
while the journal still shows the containers present. After the journal records
container removal, persistent count drift follows the retry-and-keep rule above.

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
| `antiphon-runner-temp_runner-tmp` | Temp `/tmp` | Remove on temp retirement | Image `/tmp` copies in on first mount (CARD-0827). At image 8892b7b75 that copy does not include `/tmp/antiphon-pty-hosts`; the runner creates the directory on first session launch |
| `antiphon-runner-temp_dind-data` | Temp nested Docker data | Remove on temp retirement | Nested images and containers must be rebuilt |
| `antiphon-runner-temp_runner-state` | Temp identity/store and volume-backed provider state | Remove on temp retirement | New store; next deployment uses explicit retirement clear and lease admission (CARD-0953) |
| `antiphon-runner_work` | Main task worktrees and mirrors | Recycle inside scripted replacement | Empty workspace; recover unpublished work first |
| `antiphon-runner_runner-tmp` | Main `/tmp` | Recycle inside scripted replacement | Image `/tmp` copies in on first mount; at image 8892b7b75 that copy does not include `/tmp/antiphon-pty-hosts` (the runner creates it on first session launch). Never substitute a name-pattern sweep |
| `antiphon-runner_dind-data` | Main nested Docker data | Recycle inside scripted replacement | Nested images and containers must be rebuilt |
| `antiphon-runner_runner-state` | Main runner identity/store and volume-backed provider state | Preserve; opt-in deferred to CARD-1010 | A different store hits `StoreMismatch`; retire, explicitly clear retirement, allow connection detachment and lease expiry, then re-register under CARD-0953 |
| `antiphon-runner-cache-nuget-packages` | Shared NuGet packages | Preserve; opt-in deferred to CARD-1010 | CARD-0912 cold Seed required; minutes to an hour, best effort |
| `antiphon-runner-cache-nuget-scratch` | Shared NuGet lock scratch | Preserve; opt-in deferred to CARD-1010 | Recreate through the cache maintenance/Seed procedure |
| `antiphon-runner-cache-npm-content` | Shared npm package content | Preserve; opt-in deferred to CARD-1010 | CARD-0912 cold Seed required; minutes to an hour, best effort |

State/cache opt-in flags are not shipped by CARD-1008; `-RecycleRunnerState` and
`-RecycleCaches` are rejected by parameter binding. CARD-1010 owns them. Default
replacement preserves main state, all three caches and the seed marker, and invokes
no cold seed.

`redeploy-old` runs while `server2-temp` is still accepting. Temp bind-mounts the preserved
`antiphon-runner_runner-state` grok directory (`RUNNER_GROK_STORE_DIR`, that volume's mountpoint
plus `/grok`) at `/state/grok` through `docker-compose.server2-runner.temp.yml`.
`drain-temp` is still later, so the rollout order is unchanged. `docker/stack/init-state.sh`
owns `/state`, `/work` and `/runner-state` with one `chown -h` per find batch, and falls back
to one path at a time only when that batch fails. A missing path is not a failure:
a live Grok process can unlink a file after the walk has listed it.
A path that is still present and cannot be owned still fails the init, and `compose up` still stops on that error.

The three shared cache volumes remain external to temp's `down -v`. Any cache
recreation must finish the documented Seed and `verify-runner-caches` validation;
empty caches are not a passing cache gate. State/cache opt-in does not authorize
prune, deletion of deployment markers or the donor tar, or bypass of admission guards.

Before **any** volume removal, require completed drain, zero sessions and queued
tasks, no routing or pins to that runner, no land in flight for a task bound to that runner, a stopped container,
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

A blobless promisor checkout, including the deploy seed `git clone --filter=blob:none --no-checkout`, is audited with `GIT_NO_LAZY_FETCH=1`, system/global Git configuration disabled and `core.commitGraph=false` for every Git invocation, so a commit-graph cannot stand in for a missing commit object. The audit classifies every entry of the common Git directory and of every linked worktree's own Git directory (`worktrees/<id>`) against a closed list; an entry outside it refuses `RecycleGitAuditUnknown`. Every `worktrees/<id>` directory in the common directory is audited whether or not its checkout still exists or `git worktree list` shows it. When the checkout directory its `gitdir` file records still exists, its files are inspected even if the checkout's `.git` pointer is missing or names another directory: modified tracked files, untracked files that are not ignored and staged entries are compared against that admin directory's own index and HEAD and refuse `RecycleWorktreeDirty`. Only an absent checkout (Git's prunable state) leaves the index alone, which must then equal its HEAD; staged bytes there refuse `RecycleWorktreeDirty`. A worktree the runner registered under its `/tmp` (CARD-1005/0983 verification checkouts) lives in the runner's `runner-tmp` volume, which the audit helper mounts read-only beside `/work` when that volume exists (the helper's own scratch is a private tmpfs); that checkout is inspected like any other, and an absent one keeps the index-equals-HEAD rule. The recorded `/tmp` path is translated to the tmp mount before anything resolves it (the helper's own `/tmp` is a different directory) and is resolved there as the runner would: a `..` in the recorded path refuses, and each symlink is followed one hop at a time, a relative target from its own directory, an absolute target only under the runner's `/tmp`, never above the mount. Link targets and recorded paths are read byte for byte; one holding a control character (a newline, CR or NUL included) cannot be resolved exactly and refuses unknown. Inside a registered tmp checkout every `.git` entry and every Git-shaped `HEAD` directory is classified as under `/work`, where each is an entry of its own: only a `.git` file that is the pointer of a registered tmp checkout at that very directory passes; a `.git` directory, a symlinked `.git`, any other `.git` file and a Git-shaped `HEAD` directory refuse unknown (`dot-git`, `git-dir-shape`), ignored or not. A worktree registered anywhere else outside the work volume, under `/tmp` when the `runner-tmp` volume does not exist, or whose recorded `/tmp` path cannot be proven to stay inside the tmp mount refuses unknown (`worktree-confine`); the rest of `runner-tmp` is not audited. A per-worktree `core.worktree` (`config.worktree`) and a bare common directory named `.git` refuse unknown too. Loose and packed refs, every HEAD (also bare HEAD), both sides of every reflog entry in the common directory and in each linked worktree's own logs, and the ORIG_HEAD, FETCH_HEAD, REBASE_HEAD, BISECT_HEAD and MERGE_AUTOSTASH pseudorefs must prove publication against origin's real heads: for each common directory the helper fetches origin's heads without blobs (`git fetch --filter=blob:none`) into a scratch bare repository in its private tmpfs that borrows the audited object store read-only through `objects/info/alternates`, so the audited volume is never written; the advertised heads present in the clone count as well. A tip that is not a commit (an annotated tag, a tag of a tag, a tree or a blob) is itself work and must be the very object origin advertises (`ls-remote` heads and tags); a tree also passes when an origin head reaches it, and an advertised tag's commit is still proven against the heads. An unpublished tag object, tree or blob refuses `RecycleUnpublishedWork`. If that fetch fails or runs out of its 300 s, or once all proof fetches of the audit together have used their 600 s allowance, only the advertised heads present in the clone are compared, which can only refuse more. A present head that is not a commit, or no comparison head at all, refuses unknown; a local tip that no origin head contains refuses `RecycleUnpublishedWork`, so a tracking ref kept for a branch deleted on origin still refuses (whether such refs are work is an open operator decision, CARD-1105 replay section 3b). That refusal's receipt names `proof=fetched` or `proof=present` and counts `commits`, `tips`, `gone-tracking` (unpublished `refs/remotes/origin/*` tips whose branch origin no longer advertises) and `unadvertised-local` (unpublished local branches whose name origin does not advertise). A pseudoref naming a missing object refuses unknown. AUTO_MERGE, the tree Git leaves after a conflicted merge or rebase, is ignored when no merge, cherry-pick, revert, rebase, sequencer or bisect is in progress in that Git directory, and refuses unknown while one is. In-progress merge, cherry-pick, revert, rebase, sequencer, bisect or notes-merge state, stale lock files, replace refs, reftable ref storage, submodule Git directories and an index in a bare directory refuse unknown. In the common directory only, `antiphon/` (the never-unlinked `landing.lock` lease file, reviews and verification evidence) is product state, not repository state; a non-empty `antiphon/children/` journal means a repository mutation may still be running and refuses unknown. `antiphon-evidence/` is allowed only in the common directory. It is agent output a delegate wrote beside the repository (build logs, TRX, HTML, timing JSONL), not repository state. `antiphon/` is safe because it is product state: the audit checks the pending `children/` journal and does not treat the other files there as repository objects. `review-evidence/` is agent evidence rather than repository state. It is walked for Git state with the same rules as `antiphon-evidence/`. Plain files and plain directories stay allowed. The audit never reads file content, so a patch or notes there are deleted with the volume. `antiphon-evidence/` and `review-evidence/` are walked so neither can hide a repository. Every entry under either must be a regular file or a directory. A symlink, any other file type, a regular file with a second hard link, or an entry the audit cannot read refuses `RecycleGitAuditUnknown` (`gitdir-entry`). So does a name that makes a directory openable by Git, at any depth: `.git` (any type), `HEAD`, `ORIG_HEAD`, `FETCH_HEAD`, `MERGE_HEAD`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`, `REBASE_HEAD`, `BISECT_HEAD`, `AUTO_MERGE`, `MERGE_AUTOSTASH`, `commondir`, `gitdir`, `objects`, `refs`, `packed-refs`, `reftable`, `shallow`, and any name containing a control character. Other names stay allowed there, including other dot-names, `config`, `index`, `hooks`, `info`, `description`, `modules`, `*.lock` and `*~`. A refusal from this walk appends `entry=` and the entry's path relative to that admin directory, with credential-shaped path components redacted. There is no depth, file-count or size cap, and an executable bit on a regular file is allowed. A directory named `logs` is allowed: Git does not read it as the parent reflog. The same `antiphon-evidence` or `review-evidence` name in a linked worktree admin refuses `gitdir-entry`. In a linked worktree admin (`worktrees/<id>`) only, a regular file named exactly `antiphon-mirror-owner.json` is allowed: the runner mirror-owner stamp, not a symlink, not a directory, one hard link, at most 4096 bytes, and owned by the uid running the audit. The same name in the common Git directory, any other shape of that name, and every other unknown name still refuse `RecycleGitAuditUnknown` (`gitdir-entry`). A `gitdir-entry` refusal names that admin directory, relative to the work volume, with credential-shaped path components redacted. An empty `common/` directory or empty `git-daemon-export-ok` marker passes; an editor swap or backup file refuses unknown until it is recovered or deleted. Objects that no location names are outside the audit, as they are for `git gc`. Reflog-only amended/rebased commits remain recoverable work: unknown abandonment refuses. A full commit/tree traversal, without publication exclusions, refuses missing required objects as `RecycleGitAuditUnknown`; absent blobs are permitted. Unsupported index modes and grafts refuse unknown. Promisor configuration alone is permitted. Each common directory is audited once for all of its worktrees, and each worktree's files once, several at a time; the whole audit has a 3-hour (10800 s) budget by default (`C1008_AUDIT_TIMEOUT_SECONDS`, a positive integer of at most 7 digits), and running out refuses `RecycleGitAuditUnknown`, never passes. A clean standing server2 volume of 800 repositories measured 5225 s. The read-only replay of the server2 OLD volume took 23.4 minutes (1404 s, about 20 minutes of it content checks) at load 17. The deploy wrapper, the ssh bridge and `docker start -a` add no shorter wall deadline: ConnectTimeout only bounds the handshake, ServerAlive disconnects only when the server stops answering, and `docker start -a` waits until the helper exits. Both audits of a recycle (before the stop and after the containers are removed) have that budget each.

A no-index checkout is accepted only with no content outside its top-level .git, no stash, and status containing at most HEAD deletions. This matches the deploy seed; an emptied used checkout is equivalent because there are no worktree/index bytes left to lose, while all retained Git tips still require publication. In that no-index shape any file, including ignored content, refuses `RecycleWorktreeDirty`. In a checkout with an index, ignored files are not protected work and are not inspected, so a finished task worktree full of build output (`bin-*`, `obj`), caches or local configuration passes. The ignore rules are the audited repository's own (`.gitignore`, `info/exclude` and a `core.excludesFile` in its own configuration; system and global configuration are disabled): a file hidden by an ignore rule is accepted by design. Tracked files, even under an ignore pattern, staged entries and untracked files that are not ignored stay protected. Symlinks are never followed: a tracked symlink compares by its link text, an untracked one that is not ignored refuses as untracked content and an ignored one is ignored, wherever it points or if it dangles. A symlink inside a Git directory or outside every checkout must still resolve inside the work volume. A file named `HEAD` counts as a repository only in a directory shaped like a Git directory, so ignored output can neither refuse nor change the receipt; a Git-shaped directory that Git does not open, and a `.git` directory that is not that checkout's Git directory (Git never shows its contents), refuse unknown. Existing indexes are checked for hidden-entry flags and every file/symlink is hashed against its index object ID without trusting timestamps. Sparse/assume-unchanged/skip-worktree states refuse; raw-byte normalization differences also conservatively refuse. Unpublished recoverable commits refuse `RecycleUnpublishedWork`.

The receipt records `repositories` and `partial` on success. On refusal `auditFailure` names the check, exit status and redacted repository path relative to the work volume; a first refusal also initializes `audit`. A saved successful `audit` is never overwritten by failure diagnostics, so unchanged publication proof can resume after connectivity recovers. The audit lines carry only local Git facts (one line per repository entry and per retained tip of each common directory), so ignored content and a moving origin leave the before/after comparison equal. An ordinary land never moves the task's source branch (schema 3 pushes the rebased target only), so a land finishing between the two audits leaves them equal as well; the one land shape that can refuse is an adoption land, which rewinds origin's task branch to the reviewed SHA so that commits past it on a runner mirror become unpublished work and the audit refuses `RecycleUnpublishedWork` (CARD-1353). `GithubTokenAbsent` stays a warning: this audit does not read the token. The token is used only for secondary receive-pack probes.

Preview each destructive phase with `-DryRun`; `all` is not a preview phase. A
retire-temp preview bound to a cleanup receipt runs the same publication audit as
apply, inside the same pinned image, so it uses that image's Git and the production
`/work` path. Docker mounts the work volume and, when present, `runner-tmp`
read-only (`--mount type=volume,source=<volume>,target=/work,readonly` and the
same for `runner-tmp`). The helper's scratch is a private tmpfs at `/tmp`. The
preview uses apply's network: there is no `--network` argument, which is Docker's
default bridge, the same mode as apply's `docker create`. The audit's
`git ls-remote origin` and origin-proof fetch need that network. The container is
`--rm`, `--cap-drop ALL`, `--security-opt no-new-privileges`, `--user 1654:1654`,
and `--init`. Apply's helper receives `C1008_AUDIT_TIMEOUT_SECONDS`,
`C1008_SCRATCH_QUOTA_BYTES` when that variable is set, and `C1008_TMP_MOUNT` when
`runner-tmp` is mounted. When no host proof is supplied, the preview passes that same environment and nothing further. No GitHub token, SSH agent, or git credential helper is passed. A private origin whose in-container `git ls-remote` fails only for authentication is proved by the desktop, which runs `git ls-remote` with ambient credentials and passes only ref names and object ids through a read-only bind. No credential enters the container. Any other ls-remote failure stays a refusal. The proof is bound to operation id, operation start, repository path, and origin URL hash; a missing, empty, unreadable, stale, or mismatched proof refuses `RecycleGitAuditUnknown`. Apply asks the desktop again rather than reusing the preview proof. State
and auth volumes are not mounted. The audit program sets `GIT_CONFIG_SYSTEM` and
`GIT_CONFIG_GLOBAL` to `/dev/null`. The helper name is
`c1008-preview-<operation id>-<16 hex token>` and its label is
`io.antiphon.preview-audit=<operation id>:<token>`. `--cidfile` records the id
this run created. If that name already exists, the preview refuses with
`RecycleGitAuditUnknown` and leaves the existing container in place. The shared
audit budget above (default 10800 s, from the 5225 s clean-volume measurement
and the 1404 s read-only OLD-volume replay) is enforced with `docker kill` of the
owned id, so a client timeout or a dropped session cannot leave the helper
running for the rest of that budget. On timeout, SIGHUP, SIGINT, SIGTERM, and
exit, the preview `docker kill`s that id while it is still running and
`docker rm -f`s it only after the label matches, then checks the id is gone.
That is the only container a preview may create: one read-only, capability-less,
auto-removed, pinned-image helper on apply's network. A container that can write
a volume, a rollout stop or rm, a volume rm, and a recycle journal are not part
of a preview. The preview prints `auditPending=false` and grants no apply
authority when it binds an image. That image comes from a cleanup receipt for
this project, project id, and the normalized retirement stamp. A receipt for the
current source SHA is preferred when it carries a `sha256` image and is not a
dry-run, unless the caller named that dry-run for a preview. Two current-SHA
images that differ refuse. Otherwise the lookup uses completed, non-dry-run
receipts from another source SHA, including across run IDs. Partial, preview,
refused, pending, and dry-run receipts from another SHA do not count. Those
other-SHA receipts must carry the same `sha256` image; if two of them differ,
the lookup refuses `RecycleGitAuditUnknown` instead of taking the newest. When
they agree, the newest file (mtime, then operation id) is the one recorded. A
receipt for a different project, project id, or retirement is ignored. It does
not refuse. A named cleanup operation whose own file is a different project or
retirement still refuses `RecycleGitAuditUnknown` and does not borrow a sibling.
The image must still be present on the host (`docker image inspect` by id). A
missing image refuses `RecycleGitAuditUnknown` and starts no helper. An empty
image on a matching receipt refuses only when no other receipt binds an image,
rather than printing `auditPending=true` and exiting 0. A pending receipt is
not an empty match. Container cleanup does not use this refusal. When no
receipt matches, the preview prints
`C994_IMAGE_UNPINNED reason=no-matching-cleanup-record`, prints
`auditPending=true`, does not audit, and starts no container. Apply prints that
same warning. It still audits with `antiphon-server2/session-testing:<sha12>`
when that image is present, and refuses `RecycleGitAuditUnknown` when it is not.
Apply keeps its own helper, asks the desktop again for fresh proofs,
stops and removes only inspected owned container IDs, and never prunes.

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
A completed receipt from an earlier retirement is ignored, and an absent temp's
completed receipt has an empty image. A later container cleanup still succeeds.
The empty-image refusal belongs to the volume-gate audit below, and only when
no other receipt binds an image. A non-retiring failed-deploy hold uses the
abandonment procedure above.

After retaining the matching cleanup receipt, the wrapper re-reads status and
requires fresh host absence before the independent C1008 volume gate. Explicit
null live inventory is accepted only for a retired, offline, absent temp with
zero bound/queued work. The pinned original image supplies the offline UID-1654 publication audit. A
receipt matches on project, project id, and the retirement stamp. The current
source SHA is preferred when that receipt carries a `sha256` image and is not a
dry-run, unless the caller named that dry-run for a preview. Two current-SHA
images that differ refuse. Otherwise completed, non-dry-run receipts from
another source SHA are used, including across run IDs. Partial, preview,
refused, pending, and dry-run receipts from another SHA do not count. If two of
those other-SHA receipts carry different images, the gate refuses
`RecycleGitAuditUnknown` instead of taking the newest. When they agree, the
newest file (mtime, then operation id) is recorded. A receipt for a different
project, project id, or retirement is ignored. It does not refuse. A named
cleanup operation whose own file is a different retirement still refuses. The
image must be present on the host or the gate refuses `RecycleGitAuditUnknown`
and starts no helper. An empty image on a matching receipt refuses only when no
other receipt binds an image. A pending receipt is not an empty match.
Unpublished or dirty work preserves the volumes. When no receipt matches, the gate warns
`C994_IMAGE_UNPINNED reason=no-matching-cleanup-record`. Apply then uses the
current SHA `antiphon-server2/session-testing:<sha12>` image when that image
exists, and refuses when it does not. Container removal retains all four private
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
the materialized Compose file and `SERVER2_COMPOSE`. For the host project in apply mode
(`C1008_PROJECT == HOST_PROJECT` and `C1008_DRY_RUN == 0`), that comparison runs before the recycle lock
and before any removal. `deploy-parent` compares the same two files again before `compose up`.
The real-Docker fixture copies `$SERVER2_COMPOSE` byte for byte, so that refusal cannot occur there;
`C1105_Compose_cmp_refuses_before_removal` covers it.
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

A `redeploy-old` rollback to an older SHA runs that SHA's script. An older SHA that lacks the
previous-generation proof runs the pre-fix script. That script compares the containers it will stop
with the target roster from `c1008_compose_model`, after the recycle lock and before the recycle
journal, `docker stop`, or removal. In `c1008_owned_mounts`, a non-tmpfs count that differs from
that roster, or a repeated destination, is `error("mount count")`. A same-count replacement with
unique destinations passes that count guard. A missing expected destination is `error("destination")`,
and a found destination whose type, source, or read-write flag does not match is
`error("mount identity")`. On the pre-removal call in `c1008_recycle`, those errors refuse
`RecycleContainerStateUnknown`. In that script
`c1008_compose_model` raises `RecycleComposeMismatch` on `error("mount set")` when a rendered
service volume roster is not an array, differs in length from the expected block, repeats a target,
or carries configs. A newer generation with the same Compose files and the same expected roster does
not take that refusal. Accepting temp remains the live rollback.

## Shared server2 runner caches (CARD-0849)

The `session-testing` image now carries the .NET 9.0.20 `Microsoft.NETCore.App.Ref`,
`Microsoft.AspNetCore.App.Ref`, and `Microsoft.NETCore.App.Host.linux-x64` packs in
`/usr/share/dotnet/packs`. The packs come from Microsoft's SDK 9.0.318 Linux x64
archive, pinned to the SHA-512 in
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

**The Fixture gate needs the 9.0.20 targeting-pack pin (2026-10-19).** The image builds from the
floating `sdk:10.0` tag and has moved past 10.0.401 to 10.0.402, whose bundled
defaults select the net9 reference packs at 9.0.21, while the image deliberately ships only the
9.0.20 packs; `RuntimeFrameworkVersion=9.0.20` pins the runtime, not the reference packs. The
rolling rollout's Fixture seed smoke therefore failed its offline restore (`NU1100` on both
`*.App.Ref` 9.0.21) and surfaced as `CacheSeedSmokeFailed` / `FixtureSeedFailed` at
`df6b422c71b0`, with the raw restore output discarded; CARD-1333, residue and disk were ruled out
([Fixture gate diagnosis](investigations/2026-10-19-rollout-fixture-gate.md) on
`feat/card-task-efe949b4`). The fix landed on the rc line (`81cfb267a`, merged to master): the
generated smoke project in `scripts/c590-remote.sh` and the image's offline probe carry
`<FrameworkReference Update="..." TargetingPackVersion="9.0.20" />` for both frameworks, and
`SeedSmokeTargetingPackContractTests` holds both to the Dockerfile's `NET9_PACK_VERSION` pin. A
Fixture retry of an unpinned SHA against the same image fails the same way; do not warm the
deliberately empty package cache or downgrade the SDK to get past it.

The restore race does not count build-slot wait against its 30-second arrival window. Each
restore probe writes `slot-held` only after `scripts/build-slot.ps1` grants the slot, and the
barrier starts that window only once both probes hold slots. Slot acquisition uses the build-slot
timeout (default 45 minutes). A probe that exits 4, or a wait that reaches that deadline while a
slot is still ungranted, is `FixtureBuildSlotTimeout`. Once both slots are held, both restores
must start within the existing 30-second window or the diagnosis stays `FixtureRestoreBarrierFailed`.
A refusal, a slot timeout, or INT/TERM kills `c849-${RUN}-restore-p1` and `c849-${RUN}-restore-p2`
(and the other named restore and lock probes) by exact name, DELETEs any granted build-slot lease,
removes the fixture volumes, and does not wait on those probes.

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
Until CP-4 Both has passed, new work stays on `server2-temp`; when Both passes, send it to
`server2` and start `drain-temp`
([continuous dispatch](#continuous-dispatch-during-rollout)).
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
| Before seed/deploy, CP-3 | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture` | `C849_FIXTURE groups=9 controls=32 expectedRed=32 variants=60 expectedRedVariants=60 inventories=2 failures=0 productionMutations=0` |
| Drained temp donor, before recreation | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed` | `C849_SEED donor=server2-temp ready=true schema=3 digestType=manifest-sha256 smoke=passed recovery=retained` |
| Retired temp, saved donor available | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Seed -SavedDonor /home/mc/runner-cache-donor/temp-runner-cache.tar` | `C849_SEED donor=saved ready=true schema=3 digestType=manifest-sha256 smoke=passed recovery=retained` |
| After redeploy-old, before drain-temp, CP-4 | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both` | `C849_BOTH runners=2 smokes=2 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0` |
| After retire-temp, CP-5 | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Retired` | `C849_RETIRED externalVolumes=3 tempPrivateVolumes=0 mainTmpRetained=true smokes=1 rollback=retained failures=0` |

`-Case Inventory` refreshes two read-only status and mount receipts. The Fixture case
uses run-scoped Docker resources and records `PASS F-1` through `PASS F-9` and all
32 control families (`PC-01` through `PC-32`), with exactly 60 negative variants.
Both shell and front door check the full roster, including duplicates. Fixture generates
ordinary packages locally and reads production only for the two inventories. A complete
successful census and an explicitly retired temp status may record an absent temp;
the failed-deployment hold is also accepted when `retiredAt=null`, `retireWhenIdle=false`,
`draining=true`, and `redirectTo=server2`. Both absent shapes require offline status
(`available=false`, `dispatchEligible=false`, `acceptingNewWork=false`), zero `sessions`
and `queuedTasks`, and zero or null `runnerSessions`. Inventory additionally requires a
successful, empty whole-project `docker ps -aq` census (all services, including stopped
containers), retained in `server2-temp-container-census.txt`; the status receipt retains
the drain, redirect and retirement fields. PC-31 covers the hold with zero and null live
inventory and 13 refusal variants. Rerun its isolated local controls with
`bash scripts/fixtures/c1589-cache-inventory.sh "$PWD" controls`.
This admits the documented `deploy-temp` retry without manually changing the hold;
it grants no retirement or volume-removal authority. Lookup failures, unknown status,
busy or live placeholders and remaining temp-project containers refuse. Empty-cache native execution
is positive; disposable children mask each image pack separately for the negative cases.
CP-45..52 model post-Compose finalizer probes for registered consumers, helpers,
work/mask/cache volumes and the fixture network. CP-53 models work/mask creation
followed by unknown ownership, with persistent daemon loss and a one-probe outage:
registration and both cleanup records remain, and the wrapper refuses success.
C1192_Foreign_volume_is_refused_after_create (CP-44) checks confirmed-foreign work/mask refusal: registration drops, and those volumes are neither mounted nor deleted.
C1208_Foreign_cache_is_dropped_and_untouched checks foreign cache refusal, ledger removal and untouched volume; C1208_Legacy_container_replacement_is_untouched and C1208_Legacy_network_replacement_is_untouched check legacy replacement retention/refusal; C1208_Unacquired_roster_is_untouched checks unacquired consumer/helper retention; C1208_Acquired_objects_are_removed and C1208_Recorded_container_id_does_not_delete_replacement check acquired disposal and ID-only removal; C1208_Legacy_owned_markers_are_migrated checks verified legacy migration, and C1208_Unknown_identity_retains_residue checks unknown refusal/residue. CP-45..53 retain positive-absence checks. CARD-1207 records Compose project resources before teardown, probes each recorded name afterward, and refuses success unless absence is confirmed; live Docker W-1 is pending.
C1208_Wrong_cidfile_preserves_foreign_container and C1208_Recorded_id_with_changed_labels_is_untouched check full-ID owner/run/role validation. C1208_Recorded_creation_fingerprint_is_reverified checks recorded container name/image/Created fields. C1208_Wrong_helper_id_preserves_foreign_container and C1208_Wrong_network_id_is_untouched check foreign helper/network IDs. C1208_Recorded_container_inspect_failure_refuses_cleanup and C1208_Recorded_network_inspect_failure_refuses_cleanup check ownership-probe failure. C1208_Owned_packages_registration_survives_production_controls checks authoritative registration through production PC-09/10 and residue through PC-32. C1208_Dangling_network_record_retains_residue, C1208_Invalid_network_record_retains_text and C1208_Confirmed_absent_network_record_is_cleared check invalid-record refusal/text and positive-absence clearing/receipt. These are modeled contracts; native Docker W-1 remains pending.

The production cache names are
outside its namespace. A nonzero case or missing
receipt leaves the checkpoint pending. CP-3/4/5 are operational gates after Review,
not Code-stage repo tests.

CARD-1192 Fixture storage uses case-owned local Docker work/mask volumes. Generated
packages, feeds, programs, npm working files, recovery trees and mutation backups
stay in the work volume; the mask volume stays empty. Readers mount work readonly
with volume-nocopy. Only readonly checkout inputs use host binds. Small controller
documents and command logs live in a fresh mode-0700, host-owned
`CASE_DIR/fixture-controller` directory; the maintenance lock stays on the original
host root. Finalization attempts consumer release, exports the five safe race/lock logs,
releases helpers, removes label-checked work/mask volumes and registered cache
volumes (subject to CARD-1208 above), and removes the identity-checked
controller before publishing success (CP-34/35/38). The shared resource-removal
helper checks ownership on the inspected ID (or registered volume name), then probes that identity after removal; CP-45..51 distinguish recognized
absence from daemon/socket, timeout, empty and unrecognized probe results.
Unknown registered-finalizer outcomes name residue in `fixture-cleanup-residue.txt`
and `unknown-cleanup-outcome` in `fixture-cleanup-ledger.txt`. CP-53 preserves
acquisition uncertainty in those files even if a later cleanup probe succeeds.
CP-34 covers detected cleanup/export refusal and preservation of the primary failure.
No fixture payload cleanup depends on host sudo or mixed file ownership.

S1-S7 repo-contract evidence does not establish native Docker/UID/archive/NuGet-lock
acceptance. W-1 remains pending with the rollout owner after Review and land, using
`verify-card0849-caches.ps1 -Case Fixture -Sha <landed-sha>` and the unchanged
9-group/32-family/47-variant/two-inventory receipt above. SIGKILL, host loss or an
unavailable Docker daemon can leave attributable residue; this storage contract
does not authorize removing earlier failed runs.

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

The root `Dockerfile` publishes the server for `linux-x64` with SDK 10 and the ASP.NET 9 runtime. Its runtime carries the same pinned Git as the runner targets, from its own `git-build` stage with the same version/SHA-256 pair, installs no apt git, and runs the same floor guard (CARD-1260): the server's `LandingGit`, `LandOperationFactory` and guarded removals run inside it in the self-contained `docker-compose.yml` stack and in the nested child a server2 session stands up from that file, while the production server on the desktop uses the host's Git. Measured on a fresh root build of `797aac855be2` on server2 (2026-10-14, image `143cdb711f61`, revision label equal to the source SHA): the guard printed `Git 2.47.3 meets minimum 2.43`; in the image, as uid 1654, `git` is `/usr/local/bin/git` at 2.47.3 with no Debian `git` package, `git show-ref --exists` returns 0 for a present ref and 2 for a missing one in a fresh repository, and an HTTPS `ls-remote` succeeds. Before this change the same image shipped Debian's 2.39.5, where `show-ref --exists` exits 129. `docker/session-runner-grok/Dockerfile` keeps the phone-home runtime as its default target. `receipt-probe` adds FakeGrok and no Docker tooling at all. `session-testing` is the **persistent server2 runner**: it carries the whole Docker engine from one pinned tarball (`dockerd`, `containerd`, `runc`, the shims, `docker-proxy`, `ctr`), `iptables` pinned to the legacy backend (server2 is kernel 4.15 with iptables 1.6.1), `iproute2`, `openssh-client`, SDK 10 with runtimes 9 and 10, Node 22 and PowerShell. `runtime-base` (and so every runner target) carries Grok 1.0.41 (raw artifact `https://x.ai/cli/grok-1.0.41-linux-x86_64`, SHA-256 `9ce03ed23e16ea01072b4496263d6213a27899e1e3e107f008d36edf82e70407`, verified with `sha256sum --check --strict` before install; a pin that is not exactly 64 lowercase hex characters fails before the fetch; CARD-0989), Claude Code 2.1.280, codex-cli 0.160.0, and jq 1.7.1 (static jq-linux-amd64, SHA-256 `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`, verified before installing root-owned mode 755 at `/usr/local/bin/jq`). Claude Code is pinned by SHA-256 `1e08503dbdf3c2cb0d706d32f3408277388d1c76ef108673e8fe42c1b322925b` and verified with `sha256sum -c` before install (CARD-0628); Codex is pinned by npm's platform-package SHA-512 and verified before unpacking (CARD-0904). No credential for either is ever baked. Every runner target needs **minimum Git 2.43**: `LandingGit`, `GuardedWorktreeRemoval` and `GuardedVerificationRemoval` run `git show-ref --exists` (new in 2.43) to tell a missing ref from a lookup error, and landing inspection fails on anything older. Debian bookworm ships 2.39.5 and bookworm-backports carries no git, so the `git-build` stage compiles Git 2.47.3 from the kernel.org tarball pinned by SHA-256 `c073471530e92b716641ea2b381fcd0ece53eea9a76a9c5415f93f89e870dd5f` (verified with `sha256sum -c` before unpacking), with `sysconfdir=/etc` so the baked `/etc/gitconfig` stays in force; `runtime-base` copies it to `/usr/local`, installs no apt git, and runs `docker/git-minimum-version.sh`, which fails the build when the installed `git --version` is below the floor (CARD-0661). That script is the one declaration of the floor (`GIT_MINIMUM_VERSION=2.43`, CARD-1260); neither Dockerfile restates it, and raising it is a change there and here. Raising the pin means a new version/SHA-256 pair, never dropping the check. It starts as root under tini so `docker/session-runner-grok/dind-entrypoint.sh` can run its own `dockerd`, then drops the runner to uid 1654 with the `docker-nested` group (gid 1656). A dead daemon takes the container down; `restart: unless-stopped` brings both back.

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

### Chromium runtime witness (CARD-1323)

`session-testing` installs the Debian 12 x64 Chromium libraries and shared tools/fonts
listed by the **Microsoft.Playwright 1.63.0** driver pinned in
`tests/Antiphon.E2E/Antiphon.E2E.csproj`. The package provenance is that NuGet package's
`.playwright/package/lib/coreBundle.js`, bundled `nativeDeps` entries
`deps["debian12-x64"].chromium` and `.tools`. Its SHA-256 is
`549070af3acabb3efcc4f55bfe6210f9f7c2fcf633cf7eaa59bfe60719969171`.
When changing the driver pin or base distribution, derive the package set again from
that matching driver's manifest. The package install belongs only to `session-testing`.

The browser download is a separate matching-version prerequisite. Use the pinned
package's `.playwright/package/cli.js install chromium --only-shell`, with
`PLAYWRIGHT_BROWSERS_PATH` pointing at an owned directory readable by the test user.
For 1.63.0 this downloads Chromium headless shell revision **1243**
(153.0.8010.12) and FFmpeg revision 1011. Do not use an unpinned `npx latest` or
replace this prerequisite with a system Chromium. The image supplies OS packages;
it does not bake the downloaded browser or credentials.

The narrow acceptance witness is
`ChromiumRuntimeSmokeTests.Chromium_launches_and_renders_local_content`.
It launches headless Chromium with a unique temporary profile, renders local HTML,
asserts text written by JavaScript, closes the context within a bounded wait and
disposes Playwright before deleting the profile. It uses the E2E process-spawn limiter
and no application, server, Postgres, provider or broker fixture.

Use CP-7/CP-8 in the
[CARD-1323 plan](superpowers/plans/2026-10-20-card-1323-rc-lane-landdelivery-placement-plan.md#checkpoints),
through the checkpoint tool with `--row-timeout 40m` and the full committed
`--expected-source-sha`. Build the `session-testing` target through
`scripts/build-slot.ps1` under a task-owned image identity. Run the unchanged method
as uid 1654 in disposable baseline/candidate containers with copied clean source and
the matching browser, without live mounts or secrets. Preserve the image IDs,
source SHAs, unedited CHECKPOINT lines, fresh TRX and build provenance. If the
checkpoint tool runs inside the container, sharing the runner's PID/network namespaces
lets the host build-slot broker verify its lease holders; it needs no Docker socket
or fleet-state mount. Copy the checkout as a directory owned by uid 1654: Docker's
auto-created working directory can be root-owned and cause Git source admission to refuse.

CP-7 must execute one test and fail on a browser loader/dependency error with the
browser present. A missing browser, build failure or slot refusal is an incomplete
prerequisite. CP-8 must execute the identical method once with one pass and no skips.
This qualifies the dependency repair only; it confers no RC or lane credit and does
not prove fleet activation or the historical 35-case browser lane.

After ordinary source review and approved inclusion on the current RC line, the
operator owns a separate [staged rollout](#staged-server2-rolling-rollout-card-0934):
deploy beside the old runner, smoke/canary, drain old, upgrade it, smoke, return
scheduling, drain temporary and retire it. Retain the deployed image/source identities
and browser result with that rollout receipt. A master-only land leaves live activation
outstanding and does not authorize deploying master.

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
