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
| 6. Upgrade old | Run `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase redeploy-old` only after step 5's zero gate. The phase runs `deploy-parent`, verifies mounts/cache and `buildVersion`, then clears old's drain. It accepts the running container's own generation and requires the new one after recreate. Its task census, and the host census of a recycle, compare only rows bound to `server2` across the two passes (CARD-1697): off-runner work moving between the passes does not refuse `cause=Unstable`, while a task or pending land bound to `server2` still refuses `RecycleBoundTasks` or `RecycleLandInFlight` before any comparison. Default redeploy-old keeps main `work`, `runner-tmp` and `dind-data`. It prints `C1008_RECYCLE skipped=redeploy-old reason=not-requested`, does not stop the runner for a recycle, and needs no token. `-RecycleVolumes` is the explicit volume recycle. A recycle without `enabled-<operation-id>` in `ANTIPHON_VOLUME_RETIREMENT_APPLY` refuses `apply-gate-off` and exit 2 before any stop, remove, journal write, or preview consume. `-DryRun` only previews. | If it refuses, keep temp accepting and old drained; use the retained rollback image and the [rollback procedure](#shared-server2-runner-caches-card-0849) only through a reviewed recovery. Do not clear an unverified old runner. |
| 7. Smoke upgraded old | Run the command block below for `server2`, a sanctioned `Plan` canary pinned with `-Runner server2`, and `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both -Sha <sha>`. | `redeploy-old` already clears old's drain after its own host checks, before this separate canary. If this gate fails, immediately drain old toward accepting temp with `pwsh -NoProfile -File scripts/runner-drain.ps1 drain -RunnerId server2 -RedirectTo server2-temp -Reason 'post-upgrade smoke failed'`; stop and report. CARD-0935 tracks a separate canary-before-promotion gate. |
| 8. Return scheduling and drain temp | Once old passes step 7 and accepts work, run `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase drain-temp -WaitIdleMinutes 240`. This sets temp `redirectTo=server2` and `retireWhenIdle=true`; automatic placement uses old again. Verify old accepting and temp drained/retired/offline, then prove temp container absence. The phase observes exit and removes only proven owned exited runner/state-init IDs without volumes. Retirement and exit share one deadline from `WaitIdleMinutes`; `TempContainerExitTimeout` removes nothing live. | If the phase refuses, leave old accepting and temp in its observed state; report the code. If a rollback is needed before temp retires, clear temp's drain only after verifying old remains accepting; do not start two drains. |
| 9. Retire temp | Apply is off by default in the script, and the orchestrator always runs gate 9 itself, automatically and without asking a human (operator direction, 2026-11-03): the preview, then at once the enabled apply with that preview's token. Prerequisite: the operator-written host trust registry `/home/mc/antiphon-server2/retirement-origins.json` ([Temp retirement trusted origins](#temp-retirement-trusted-origins-card-1689)); without it every preview of a non-empty temp work volume refuses `RecycleGitAuditUnknown reason=registry-missing`. First, preview only: `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase retire-temp -DryRun` immediately after successful gate 8, including already-absent cleanup. The preview reports `would delete:` and its operation id (`C1609_RETIREMENT state=Ready operation=<operation-id>`), and removes nothing. The Ready preview expires after 900 s, so the second rollout needs a fresh `-DryRun` and then an apply within 15 minutes. A following apply without the matching token refuses `apply-gate-off` and exit 2 before any stop, remove, journal write, or preview consume. Immediately after a Ready preview the orchestrator sets `$env:ANTIPHON_VOLUME_RETIREMENT_APPLY = 'enabled-<operation-id>'` and runs `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase retire-temp -RetirementProof <operation-id>`. Unset, `1`, `true`, `enabled`, or another operation's token does not enable deletion. Reclaim its volumes under [Volume recycling and disk reclaim](#volume-recycling-and-disk-reclaim-card-1008). Retain the rollout receipts. | The retired, offline, absent placeholder may have null live inventory only with the complete host proof. Stop and report only on `RecycleUnpublishedWork` or `RecycleWorktreeDirty` (protected work on the temp volume). A missing registry (`RecycleGitAuditUnknown reason=registry-missing`) is also stop-and-report: only the operator writes that file, so neither a retry nor a Debug task can fix it. Any other refusal (a census `RecycleTaskCensusUnknown`, any other `RecycleGitAuditUnknown`, an expired preview) gets one fresh preview and apply, then a Debug task; it is never a question for the operator. Other refusals stop the rollout until then. A preview that is not followed by the enabled apply leaves the temp volumes; the next `deploy-temp` refuses `TempVolumesRetained` while any of them exists. |

The apply shell and the proof directory are the same ssh account, `mc` (`ssh mc@server2 bash -s`). The coordinator creates `/home/mc/antiphon-c590/c1008-proof-<operation>-<started>-<nonce>` mode 755 as that account, copies the proof files with scp, and `chmod 644`. Custody accepts a proof file only when it is a regular file, has one link, is not group- or world-writable, and is owned by the applying uid. It accepts a directory, the proof directory and each ancestor, only when that directory is owned by the applying uid or by root. A group- or world-writable directory is refused, except a sticky root-owned ancestor strictly above `SERVER2_ROOT`. A symlink is refused. A uid that is neither the applying uid nor root is refused. A root-owned file is refused unless the applier itself is root, which production is not: production apply is `mc`, not root and not container uid 1654. An app-owned directory is accepted only when the applying uid is that same owner. Running the apply as root does not make an app-owned proof directory acceptable.

At gate 1, `deploy-temp` accepts either a retired temp placeholder or a cleared offline
one, whether its drain is set or clear. For a retired start it checks offline zero work
and container absence, then `Assert-TempRetirementClearable` reads the four temp-private
volumes and the retirement journal. That read is before `/drain/clear`, `/drain`,
`runner-cache-seed`, or any mutating host case. A present volume refuses
`TempVolumesRetained` and names it. An unresolved journal (Applying, Partial,
PartialRefused, or any record that still has effects or intents) also refuses while
the four volumes are absent. Recovery is Resume of that operation, or
`pwsh -NoProfile -File scripts/retire-runner-volumes.ps1 -Close -OperationId <id> -ExpectedJournalDigest <sha256> -ReasonFile <file>`.
Close is a journal transition, not a delete, and it does not clear `retiredAt`.
The current Close implementation takes the same rollout/cache locks, validates
the protected receipt and lifecycle records, and requires complete stable Docker
container/reference census plus typed absence of all four volumes. An inspect
error is unknown. Missing child-join evidence, a live owner, a replaced/present
volume, or a changed digest refuses. Its append-only closure distinguishes
`removed-observed` for a durable intent from `unattributed-absence`; the original
journal bytes remain intact. A failed closure flush leaves `close-pending.json`
blocking deployment. Repeating the same Close after repairing the failure
reobserves the complete set before finishing that appendix.

Close reads the existing operator credential through `Get-RunnerOperatorToken`.
A commissioned delegate also supplies `-AuthorityFile` with schema 1, actor,
authorityRef and the exact operationId; a task token alone is insufficient. The
credential stays on the control host. Local journal/census environment overrides
are refused. Public manual Preview/Apply currently refuse
`ManualPreviewApplyIncomplete`; their complete content/proof flow remains owed.
Production deletion stays disabled pending complete S5 acceptance and Review.
There is no `-Adopt` and no force-close. Only a clearable journal continues,
then POSTs `/drain/clear` with reason `CARD-0948 retired temp placeholder reactivation`,
and POSTs `/drain` with `redirectTo=server2`, `retireWhenIdle=false`. A cleared start
establishes that same non-retiring drain if needed. Both paths hold new work away from
temp during cache seed, container start, and cache verification. A connected temp can be
`dispatchEligible=true` while draining; only after verification does the phase POST the
final `/drain/clear` and wait for `acceptingNewWork=true` at the requested SHA.
That hold lift does not repeat the volume and journal census, because the container this run
just deployed owns the four temp volumes. Under the rollout lock it rereads temp status and
refuses `TempRunnerRetiredDuringHold` when temp is retired, or `TempHoldLiftBuildMismatch`
when temp is not at the requested SHA, before the POST.
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

CARD-0824 S2 (origin branch deletion after a confirmed land, off by default) is enabled per runner volume through `Landing:SourceBranchDeletionRunnerIds`, only after that volume's legacy mirrors are drained (the checklist in docs/orchestration-loop.md, "Origin branch deletion after a confirmed land"). Before origin is touched, the runner deletes its own `refs/heads/<b>` and `refs/remotes/origin/<b>` at or behind the leased SHA (`WorkspaceBranchRefsDelete`), so a later recycle audit does not count them as `gone-tracking` or `unadvertised-local`; a branch still checked out in a legacy mirror refuses there and keeps its origin branch.

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
the audits cannot end `cause=Unstable` (CARD-1353). The open closure snapshot is scoped the
same way for both runners, `server2-temp` (CARD-1693) and the standing `server2` in
`redeploy-old`, `-RecycleVolumes` and `check-census` (CARD-1697): it holds only rows bound to the
recycled runner and their land proofs, with no per-scope id lists, so off-runner work that starts,
finishes, changes runner, scope or land proof, or appears under a nested scope between the passes
cannot end `cause=Unstable`. Every row is still read, shape-checked, count-reconciled and
receipted, and the bound-task refusals run on every row before any comparison. Failed/Canceled/Succeeded rows without a
pending land are not bound work; their worktrees remain protected by the publication audit
below. No time window hides an older pending land. The bound rows of both closures must agree
across two passes.

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
`Blocked` between list/detail reads and between the two census passes (since CARD-1697 an
off-runner row is outside the compared pass snapshot, so the pass rule matters only to the
reconciliation code itself; the list/detail rule still applies to every row). Both observations
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

CARD-1326 kept two edges of that exception. Its nested-scope edge is retired by CARD-1697: the
compared open closure no longer carries scope id lists, so an off-runner task that appears under a
nested excluded scope passes (each pass still reconciles `excluded.byProject` counts with the
rows it walked), and a bound one refuses `RecycleBoundTasks`. `check-census` stays
the read-only count report, so a row with `landRequestedAt` or `landStartedAt` increments
`landPending`, and `boundLandPending` when that row is bound to the reported runner. When
that bound row is the pass-1 appearance, or a row bound to the recycled runner appears in the
`landPending` closure, the two-pass compare refuses `cause=Unstable`; an off-runner row or land
appearing there is outside the compared snapshot. Mutating phases and the host census
refuse `RecycleLandInFlight` on a row bound to the recycled runner, after the bounded wait
above. Project resolution
uses `RecycleProjectUnresolved` and its cause. Each census retains approved read
timings/counts and reduced task/land facts; no task prose, credentials or response body
is retained. A refusal stops the phase before destructive work, including on resume,
while the journal still shows the containers present. After the journal records
container removal, persistent count drift follows the retry-and-keep rule above.

Always retire `server2-temp` once scheduling has moved back to the verified, accepting
main runner. After drain and the checks below, exact `docker volume rm -- <name>`
removes the four bound private volumes in order: state, dind, tmp, then work. This is
part of the rollout's disk reclaim, not a reason to retain temp for rollback.
Temp volume deletion uses a v2 census: four attempts, and only Drift and Unstable
are retried, with backoffs of 2s, 5s and 10s inside one 180-second monotonic budget.
Every task HTTP read uses the remaining budget. Each exact delete rechecks live guards
and the full volume incarnation, then durably records its intent. A failed delete or
later guard marks PartialRefused and stops before the next target; work is last.
Preview and apply use the same read-only content scan. Changed local refs, reflogs,
indices or linked-admin metadata require a fresh preview, even when newly published.
Each preview nonce has an immutable host artifact; proof.md is a rendering.
Temp resume reopens that protected operation and immutable preview, checks the
complete remaining incarnation set, then freshly audits retained content. Only a
durable delete intent permits confirmed absence to become `removed-observed`;
that result does not attribute who removed it. Replacement or absence without an
intent refuses. A completed replay rechecks absence and records no duplicate
intent, effect or removal. File data and the renamed record's directory are flushed
before the coordinator acknowledges persistence.
Main keeps the v1 collector: three attempts, a 30s sleep between Drift retries, and
after the journal records container removal the third attempt may keep those rows.
A content or census refusal at gate 9 returns nonzero and leaves the rollout incomplete.
Do not run the C849 Retired check as if that gate passed. `-Case Retired` still
requires `tempPrivateVolumes=0` together with `externalVolumes=3` and
`mainTmpRetained=true`. Retained private volumes are not a successful retired state.

Main volume recycling is **scripted only**. Default `redeploy-old` runs `deploy-parent`
and keeps `antiphon-runner_work`, `antiphon-runner_runner-tmp` and `antiphon-runner_dind-data`.
It prints `C1008_RECYCLE skipped=redeploy-old reason=not-requested` and does not stop the
runner for a recycle. Deletion of those three volumes requires `-RecycleVolumes` plus
`ANTIPHON_VOLUME_RETIREMENT_APPLY=enabled-<operation-id>`. Without that token the host
refuses `apply-gate-off` and exit 2 before any stop, remove, journal write, or preview
consume. `-DryRun` only previews. Do not stop or remove main's container or recycle its
volumes by hand. A manually stopped main without this operation's journal reports `runnerSessions=null` while
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
| `antiphon-runner_work` | Main task worktrees and mirrors | Keep. Delete only with `-RecycleVolumes` and `enabled-<operation-id>` | Empty workspace; recover unpublished work first |
| `antiphon-runner_runner-tmp` | Main `/tmp` | Keep. Delete only with `-RecycleVolumes` and `enabled-<operation-id>` | Image `/tmp` copies in on first mount; at image 8892b7b75 that copy does not include `/tmp/antiphon-pty-hosts` (the runner creates it on first session launch). Never substitute a name-pattern sweep |
| `antiphon-runner_dind-data` | Main nested Docker data | Keep. Delete only with `-RecycleVolumes` and `enabled-<operation-id>` | Nested images and containers must be rebuilt |
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

Preview each destructive phase with `-DryRun`; `all` is not a preview phase.
Temp retirement uses the protected host `audit-current.json` descriptor (schema 1,
audit protocol 2, policy 1). Deployment publishes its installed image digest after
a successful image build. Retirement resolves that current reference once per phase
and passes its digest to the helper. Missing, linked, writable, duplicate-key,
unsupported or unreadable configuration, or an unavailable current image, refuses
`RecycleGitAuditUnknown`. Install reviewed current tools; recovering an old build
is not a recovery step. The creating `-Sha` is optional retirement provenance.
Other rolling phases still require their deployment SHA.

The bridge stages c590-remote, c1008-retirement, c1008-content-audit and
c1008-volume-metadata in a fresh owner-only operation directory, verifies every
SHA-256 against the local files and seals the bundle read-only before executing it.
Each bundle retains `tooling.sha256`. Concurrent retirement requests do not overwrite
each other's scripts. Historical cleanup images and receipts do not select the
audit runtime. A complete zero-finding preview reports `auditPending=false` and
writes a protected schema-2 Ready receipt. Apply consumes the exact receipt and
reruns content and publication checks before any volume effect.

Temp's helper mounts work at `/work` and present runner-tmp at `/tmp`, both read-only
with `volume-nocopy`. It uses `--network none`, a read-only root filesystem, UID 1654,
`--cap-drop ALL`, `--security-opt no-new-privileges`, `--init` and an owned name,
label and cidfile. Writable scratch is a separate `/audit-scratch` tmpfs with
`TMPDIR=/audit-scratch`. Main's v1 namespace remains unchanged.
No GitHub token, SSH agent, or git credential helper is passed.
No credential enters the container. Host publication transport passes only ref names and object ids through a read-only bind
plus the credential-free graph bundle. The proof is bound to operation id, operation start, repository path, and origin URL hash;
a missing, empty, unreadable, stale, or mismatched proof refuses `RecycleGitAuditUnknown`.
Apply asks the desktop again rather than reusing the preview proof. It asks the desktop again for fresh proofs.
A 404, a DNS failure, and a timeout are admitted when a matching v2 proof exists.
Any other ls-remote failure stays a refusal. Main's v1 scanner adapter remains separate:
When no host proof is supplied, the preview passes that same environment and nothing further.

Production deletes remain disabled pending the complete S5 acceptance. In particular,
N1 must prove the kernel read-only/offline/nocopy namespace and cancellation contract;
the complete Linux/native Windows roster and strict v2 publication consumption
still require qualification. Public manual entry now uses operator authentication,
an unsigned exact-fingerprint template and a protected host grant; its small owned
fixture accepts Preview, Apply and Resume. Long scans can outlive that short grant:
phase admission/renewal remains a refusal to resolve before production enablement.
The wrapper's full content Preview/Apply flow must be qualified;
a census-only DryRun is not a completed content preview. The first live action
remains a read-only preview after reviewed tooling activation, with findings and
unknown denominators retained. No apply follows an incomplete or refused preview.

The public manual interface is `scripts/retire-runner-volumes.ps1`. From a
PowerShell session, supply `-Manual -RetiredRunnerId server2-temp -ProjectId <id>
-OperationId <c1008-id> -VolumeName $names`, where `$names` contains exactly the
four temp-private names. `-Preview -Template` writes an unsigned
`attestation-template.json` and never creates Ready. After explicit human authority,
fill actor, authority reference, reason and UTC; delegated authority also needs
`-AuthorityFile` with the exact operation, actor and authority reference. The
existing operator credential is required for both operator and delegate calls.
`-Preview -AttestationFile <file>` (or `-DryRun`) runs the full read-only gate.
For `-Apply`, use an Apply attestation bound to the completed Preview digest and
pass `-ExpectedProofDigest <sha256>`. `-Apply -Resume` additionally pins the current
journal digest while its fresh attestation still binds the immutable Preview.
Results are copied to a distinct ignored
`.antiphon/rolling-server2/manual-retire-<UTC>-<nonce>/` directory. A failed copy is
incomplete evidence; the protected host journal remains authoritative. A template,
an operation id, task token or free-text actor cannot replace operator authority.
These are interface/recovery instructions, not production enablement.

Every attempt retains a schema-1 host journal under the deployment evidence root
outside the recycled volumes and a copied receipt under
`.antiphon/rolling-server2/<run-id>/`. `C1008_RECYCLE` records removed/alreadyAbsent
counts, before/after available bytes and a signed delta on Docker's data-root
filesystem. Reclamation occurs before main's allocation budget gate; safe temp
retirement does not require a warm marker or 20 GiB free.

After any stop, removal, seed/up or receipt-copy failure, keep main drained and temp
accepting. Use the recorded operation ID with the same full source SHA and phase:

`pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase redeploy-old -ResumeRecycle <operation-id>`

An in-flight `-ResumeRecycle` with the same `enabled-<operation-id>` token still deletes.
The token does not expire. Resume rechecks source, options, project, Compose, retirement/store, publication
audit and original/recreated generations. It never deletes a replacement
generation. Same-SHA retries refuse an unfinished journal until explicitly resumed.
Recovery never commits, publishes, resets or salvages suspicious work.

#### Temp retirement trusted origins (CARD-1689)

Gate 9 has a one-time host prerequisite that no script installs: the trust registry
`/home/mc/antiphon-server2/retirement-origins.json`. Every repository in the temp `work`
volume names its origin as a private origin, so every non-empty temp retirement reads the
registry before it asks the desktop for proof. Without it, every `retire-temp -DryRun` refuses
`RecycleGitAuditUnknown reason=registry-missing`. Whether an origin is trusted is the
operator's decision. No agent creates or edits this file.

`c1609_export_trusted_origins` in `scripts/c1008-retirement.sh` accepts only this shape:

- The file is a regular file owned by `mc`, with one hard link, no group or world write
  bit, not a symlink and at most 1 MiB, and no parent in its path is a symlink. Every parent
  directory is owned by `mc` or root and is not group- or world-writable. The one exception
  is a root-owned sticky directory above `/home/mc/antiphon-server2`.
- Its bytes are exactly what `jq -cS` prints. That means canonical key order, compact
  output and no duplicate keys. A trailing newline is allowed.
- The top-level keys are exactly `projects` and `schema`, and `schema` is `1`.
- `projects` is an object keyed by lowercase project id (36 characters of `0-9a-f-`).
- Each project value has exactly one key, `origins`. Its value is an array of unique,
  non-empty strings.
- `projects` must have an entry for the retirement's project id (`C1008_PROJECT_ID`).
- Every `C1008_PRIVATE_ORIGIN origin=` that the inventory prints must be in that list as
  the exact same string. Origins with and without the `.git` suffix are different strings.
  A temp volume holding fixture checkouts of `https://github.com/michal-ciechan/Antiphon`
  needs that form listed as well.

To find the project id, run the read-only census. Use the `RECYCLE_PROJECT id=<guid>` line
it prints. If you pass `-ProjectId` to the rollout, use that id instead.

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-census
```

Then, on server2 as `mc`, write the registry once. List each origin only after reviewing it:

```bash
umask 077
jq -ncS --arg p '<project-id>' '{schema:1,projects:{($p):{origins:["https://github.com/michal-ciechan/Antiphon.git"]}}}' > /home/mc/antiphon-server2/retirement-origins.json
ls -l /home/mc/antiphon-server2/retirement-origins.json
```

To add an origin later, rewrite the whole file with the same `jq -ncS` command and the
complete list. A hand edit that changes the canonical bytes refuses `registry-invalid`.
`>` keeps the mode of an existing file, so check `ls -l` for `-rw-------`.

A refusal from the inventory exchange names its cause and the host file that holds the
detail. The coordinator console shows only the `DIAGNOSIS=` line.

| Diagnosis | Cause and next step |
|---|---|
| `RecycleGitAuditUnknown audit check=<check> status=<n> repo=<repo> reason=inventory detail=<file>` | The inventory helper refused a repository on a structural check. Read `<file>` (`retirement/<operation-id>/inventory-<phase-nonce>.out`) for every refusal line. |
| `RecycleGitAuditUnknown reason=registry-missing detail=<registry>` | The registry is absent. Write it as above. |
| `RecycleGitAuditUnknown reason=registry-invalid detail=<registry>` | The registry failed custody, canonical bytes or shape. Check `ls -l` and rewrite it with `jq -ncS`. |
| `RecycleGitAuditUnknown reason=registry-project detail=<registry>` | The registry has no entry for this retirement's project id. Compare it with the `RECYCLE_PROJECT id=` line. |
| `RecycleGitAuditUnknown reason=unapproved-origin detail=<private-origins.txt>` | An inventory origin is not listed. `sed -n 's/^C1008_PRIVATE_ORIGIN .* origin=\([^ ]*\) .*/\1/p' <private-origins.txt> \| sort -u` lists the exact strings. Review each one before adding it. |
| `RecycleGitAuditUnknown reason=proof-exchange stage=<stage>` | The registry passed and the desktop proof exchange failed. `request` means the host's proof-set check failed before it sent the request. `response` means no reply came within 600 s, or the desktop coordinator stopped. `binding` means the reply did not match the request or the inventory digest. `freshness` means the proof failed custody or age checks. `digest` means the proof set digest differed. `record` means the host could not persist the exchange. On the desktop, read `remote-output.txt` (`transport-exit`, `failure-line`), `coordinator-exchanges.jsonl` and the `exchange-*` directories under `.antiphon/rolling-server2/<run-id>/`. |

`RecycleUnpublishedWork` and `RecycleWorktreeDirty` from the inventory keep their bare form.
A bare `RecycleGitAuditUnknown` from this step means the host could not create or record
the exchange files.

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
zero bound/queued work. Current `audit-current.json` supplies the audit runtime.
Old source SHAs, cleanup receipt images and missing historical images are provenance.
Unpublished or dirty content still refuses and retains every remaining volume.
Container cleanup never supplies a volume deletion capability. The current Ready
receipt, fresh content/publication proof and exact incarnation/live guards remain
mandatory before `docker volume rm -- <name>`.

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

`redeploy-old` proves the containers it stops against their creating generation,
and the replacement against this checkout's target generation. Main retains its
`SOURCE_REVISION` / runner-status `buildVersion` equality and installed image
equality, historical Compose reconstruction and resume guards. Missing or malformed
main source/Compose/image facts refuse `RecycleGenerationUnknown`; a source or
image mismatch refuses `RecycleGenerationMismatch`. Main still requires one
session-runner image. Its running proof emits `C1008_GENERATION`.

Temp retirement uses the shipped topology catalog in `c1609_temp_topology_model`.
Historical source and image are diagnostic. It admits both complete supported
runner rosters (with and without the GitHub-token directory bind) and the supported
state-init roster. Every actual mount type, exact name/source, destination, RW bit
and ephemeral mount remains checked. Unknown, partial or altered actual topology
refuses `RecycleContainerStateUnknown`; changing an old Compose file alone does
not change admission. With no temp containers, no old source, image or credential
bind metadata is needed. A state-init-only temp needs no creating SHA or image.
Volume retirement requires container absence and the current content proof;
it never reconstructs a deployment generation.

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

For main inside `c1008_recycle`, `RecycleGenerationUnknown` and `RecycleGenerationMismatch` happen before
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

### Runner workspace slots on the volume (CARD-0824 S1)

For the server2 runner, set the non-secret stack env key
`PhoneHome__WorkspaceSlots__Enabled=true` in `secrets/stack.env` under the server2
deployment root before `-Phase redeploy-old`. To canary independently, set it in
`secrets/stack.temp.env` before `-Phase deploy-temp`; create that file if absent.
The named phases preserve each file's own value when rewriting it. A missing file or key
means `false`. A saved assignment must be exactly `PhoneHome__WorkspaceSlots__Enabled=true`
or `PhoneHome__WorkspaceSlots__Enabled=false`, with LF line endings, at most once.
Empty assignments, whitespace around/in the key or value, CRLF, different casing,
export prefixes, quoted values, active inline comments and duplicates (including
indented duplicates) refuse with `RunnerWorkspaceSlotsSettingInvalid` (exit 2)
before any deployment side effect. Whole-line comments are ignored. Other saved keys
are not sourced by this reader. `docker/stack.env.example` lists the key.

The standing Compose environment interpolates this key with a `false` default.
Compose merges the temp overlay's environment mapping by key: its runner id
overrides the base, while the workspace-slot entry inherits from the base.
Each wrapper supplies its own stack file's flag, so temp can be `true` while standing
remains `false`. A runner container recreation through the named phase is required;
editing a stack file alone does not change the running process. No additional
runner-side slot keys are required. The separate server flag
`Delegation:WorkspaceSlotsEnabled` and role gate must also permit leases; runner-only
enablement keeps new work on legacy mirrors until that server gate is enabled.
For rollback with outstanding leases, set server roles to `none` first and keep
runner support enabled until release finishes, then set the saved flag to `false`
and recreate the runner.

With `PhoneHome:WorkspaceSlots:Enabled=true` (default false) a runner keeps reusable task directories
`/work/worktrees/ws-NN` beside the legacy `task-<8hex>` mirrors. A re-homed slot lives at
`ws-NN-dK`. The ledger is `/work/workspace-slots/ledger.json`; evidence retained before a wipe
(`.antiphon/**/*.md`, `.antiphon/checkpoints/*/report.json`, 256 MiB per task, 7 days) is under
`/work/retained/<task>/<generation>/`. A quarantined directory stays registered and
`git worktree lock`ed with the reason `antiphon quarantine <slot> <class> <reason>`; its exact path is in
the ledger's `quarantinedDirectories`. Never remove one by hand: the phone-home operation
`WorkspaceSlotQuarantineRelease` (`Pushed`, `AcceptLoss` with the diff retained under
`/work/retained/quarantine/<dirId>/`, or `Removed` after fixing a blocker) is the exit. A custody
quarantine with unpublished work still refuses a CARD-1008 recycle; a filesystem quarantine whose content
is published passes it. The slot ledger and quarantined directories must be read before a temp-runner
drain (CARD-0831).

The server chooses which roles lease with `Delegation:WorkspaceSlotRoles` (CARD-0824 S1c; a single string:
`*`, `none` or `Plan,Docs`; an array is refused at startup, and `none`, not `[]`, is the soft rollback).
The Docker server has no reloadable settings file and sets `Delegation__*` through `docker-compose.yml`,
which is not reloaded, so its soft rollback is a server restart with `Delegation__WorkspaceSlotRoles=none`.
A hot reload works only for the desktop AppHost's `appsettings.Development.json`, and only while no
`Delegation__WorkspaceSlotRoles` environment variable shadows it. Confirm a rollback from the next task's
`WorkspaceSlotDecision` event (`reason=roles_none`).

### Runner worktree retention (CARD-1677)

The server job `antiphon:runner-worktree-retention` retires the `/work/worktrees/task-<8hex>` mirrors of
FINISHED tasks. The server decides "finished" from the task table (Succeeded, Failed or Canceled for at
least `MinTerminalHours`, default 12; no land pending, no open sync debt, no open task on the path, no live
session, `Result` stored, not slot-bound, not SourceLanding/repair-source, not Mutation). The runner rechecks
every fact itself right before it acts: the path is a direct child `task-<8hex>` of its own worktree root
equal to the task id, not a slot, not locked, owned by a runner repository, on the task's branch, clean
(`--untracked-files=all`), nothing outside `refs/remotes/origin`, the branch present on LIVE origin
(`ls-remote` of the runner-owned clone source) and containing HEAD, and no live process with its cwd or
an fd inside (an unreadable `/proc` keeps the tree). Execute moves the tree to
`/work/worktrees-retiring/<leaf>.<request8>` under the runner's command lock and then runs
`git worktree remove` WITHOUT `--force` outside it. It never runs `rm`, `git worktree prune`, `git fetch`
or a branch deletion, and never touches Docker volumes, `/state`, `/work/repos`, `/work/cache` or slots.

It ships ON for server2 (operator decision 2026-11-03): `RunnerWorktreeRetention:Enabled=true`,
`Execute=true`, `Runners=["server2"]` (set in `server/appsettings.json` only; the C# default list stays empty
so an override can empty it), age `MinTerminalHours=12`, at most `MaxRemovalsPerTick=25` requests per tick,
`PauseBetweenSeconds=5` apart, every 30 minutes (UTC cron `*/30 * * * *`).

- **Kill switch:** `RunnerWorktreeRetention:Enabled=false` (for example the environment variable
  `RunnerWorktreeRetention__Enabled=false`). It is re-read before every request, so it also stops a tick that
  is already running; the recurring job is removed at the next server start.
- **Dry run:** `Execute=false` sends DryRun only (the runner reports `would_remove`, nothing moves); switching
  it off mid-tick also stops that tick. `POST /api/agent-tasks/worktree-residue/runner-retention/server2/preview`
  is always one DryRun tick whatever `Execute` says.
- **Census:** each tick (or preview) writes a residue run with `Scope=runner-retention:server2`; read it with
  `GET /api/agent-tasks/worktree-residue/runs/{runId}?page=n`, following every page. `RunnerMirror` rows
  carry the verdict (`would_remove`, `removed` or a kept reason); `RunnerInventory` rows report names that
  match no task (`identity_unknown` with an `owner_hint`, `identity_ambiguous`) and `retiring_residue`.
  Those names are never acted on. The tick log line and the preview reply give `examined` (the runner's whole
  mirror population), `eligible`, `backedOff` and `sent`.
- **Progress over kept trees:** a tree the runner keeps (dirty, unpushed, branch gone from origin, a live
  process, a refused remove) is not re-sent for `KeptRecheckMinutes` (60), doubling for each repeat of the same
  reason up to `KeptRecheckMaxHours` (24). A changed reason, or changed task facts (it ran and finished again),
  re-sends it at once. Never-sent trees go first, oldest first, so kept trees never starve removable ones and
  successive dry-run ticks cover the whole population.
- **Tick bound:** a tick starts no new request after `MaxTickMinutes` (20); the rest wait for the next tick.
  A tick stopped early (bound, kill switch or `Execute` off) writes a `queued_next_tick` row for every unsent
  in-bound task as well as its Deferred stop row (CARD-1796).
- **One tick at a time (CARD-1796):** the scheduled tick, the preview and the one-shot execute share one in-process
  turn. The scheduled tick waits for it; the preview and execute routes answer 409 `tick_in_progress` while
  another tick runs (read that tick's run, or retry once it has finished).
- **Gone mirrors (CARD-1794):** when the runner inventory is complete, an eligible task whose `task-<8hex>` is not
  in it is sent after every present tree, on its own bound `MaxAbsentPerTick` (200), unpaced, and never counts
  against `MaxRemovalsPerTick`; the runner still answers `already_absent` before the path is cleared. An
  incomplete inventory proves nothing, so then every task shares the one removal bound. Every eligible task past a
  bound is a `queued_next_tick` row, so the census names it. The tick log and preview reply add `absentSent`,
  `queued` and `minTerminalHours` (the floor this server applied).
- **Settling beyond 12 h (CARD-1794):** a `settling` row means `CompletedAt` is younger than the applied
  `MinTerminalHours`. The 2026-11-08 previews (runs ddb57b73, 8f1f4338) held every task up to 23.98 h old as
  settling while the youngest `sync_debt` row was 24.03 h old: the server applied 24, not the shipped 12, so an
  out-of-repo configuration override was in force. Read `minTerminalHours` in a preview reply before
  changing anything; remove the override to get the 12 h floor back.
- **Unbranched reports (CARD-1794):** the task record is the durable report (D-9). A finished mirror whose
  `.antiphon/task-<id8>.md` is not in the branch is retired when the task row holds the report (`Result`); while
  `Result` is empty and the report file lives in the mirror it is held `report_unpreserved`. Never retire such a
  mirror by hand; nothing commits the file for it.
- **One-shot reclaim (CARD-1794):** `POST /api/agent-tasks/worktree-residue/runner-retention/server2/execute` with
  the `X-Antiphon-Operator-Token` header (403 `operator_token_required` otherwise) runs one Execute tick now, even
  while `Execute=false`. `Enabled` and `Runners` still gate it (409 `retention_disabled` / `runner_not_listed`),
  the tick bound and every runner check still apply, and the temp sweep stays DryRun. It is the sanctioned
  replacement for hand deletion. CARD-1796: it answers 202 `{ runnerId, runId }` at once and runs the tick in the
  background (a client or proxy timeout no longer cancels it); follow `GET /api/agent-tasks/worktree-residue/runs/{runId}`
  until the run has `finishedAt`.
- **Disk floor (CARD-1794):** the `WorkspaceDiskLow` attention row (Warning at `WorkspaceDiskAlarm:UsedPercent`, 85)
  turns Error when the volume has less than `WorkspaceDiskAlarm:MinFreeGiB` free (50 in `server/appsettings.json`,
  10 in code, 0 off), even below the percentage.
- **Runner-side recent use:** the runner keeps a tree it mirrored, or launched a session in, within the last
  `PhoneHome:RetentionRecentUseMinutes` (10), and any tree one of its live sessions runs in
  (`recently_mirrored`, `workspace_in_use`). That covers a task re-dispatched between the server's reread
  and the move.

A tree left in the retiring root (`retiring_refused`) stays registered, is reported, and is retried by phase B
only on the next request for that task; it is never moved back.

#### Runner temp sweep (CARD-1677 S2)

The same tick can also sweep the runner's `/tmp`, under its own switch. It ships **OFF and dry-run**
(`RunnerWorktreeRetention:TempSweep:Enabled=false`, `Execute=false`; the operator decision of 2026-11-03 turned
on the worktree retention only). It runs only inside an enabled retention tick, so
`RunnerWorktreeRetention:Enabled=false` stops it too. The runner (`src/Antiphon.SessionRunner/RunnerTempSweep.cs`)
acts only on a **registered family**: a top-level `/tmp` name matching one of its exact, anchored regexes, each
pinned to the repository file that creates it (`C1677TempFamilyRegistryTests`): `c487-probe`, `c487`,
`pty-test-root` (the `TestSessionLogRoot.KnownPrefixes` roots), `c514-pty`, `c502-exit-cap`, `c1514`,
`land-verify`, `bridge-queue`, `rcspeed`, plus `claude-scratch` (`/tmp/claude-1654/-work-worktrees-task-<8hex>`,
the Claude CLI scratchpad; its own flag `TempSweep:Families:claude-scratch` stays false, and even when on it
needs `/work/worktrees/task-<id>` gone and no `task-<id>.*` in the retiring root). An entry is removed only
when it is not denied (`antiphon-pty-hosts`, `antiphon-pty-*`, `CoreFxPipe_*`, `dotnet-diagnostic-*`, `.X*`,
`systemd-*`, `node-compile-cache` and the CARD-0826 `IsDenied` names), not a symlink, owned with every
descendant by the runner uid, within 100,000 entries and 1 GiB, its newest regular-file write older than
`MinAgeHours` (48), and the fail-closed holder scan finds no process with a cwd or fd inside. At most
`MaxEntriesPerTick` (200) entries and `MaxBytesPerTick` (20 GiB) go per tick, oldest first, through the
no-follow delete (an inner symlink is unlinked, never followed; a socket or FIFO keeps the entry). Every
other name is reported as `unregistered` with its count and bytes per normalized prefix and never touched.

Rows: Lane `RunnerTemp` in the tick's residue run (`Scope=runner-retention:server2`): one per family entry
(`would_remove`, `removed` or a kept reason, `Branch=temp:<family>;bytes=<n>`) and one per unregistered prefix
(`Branch=temp:unregistered;count=<n>;bytes=<n>`, a trailing `+` when the byte count hit its bound).

Enabling (dry run first): set `TempSweep:Enabled=true`, read one tick's or the preview's `RunnerTemp` rows
against the plan's census (docs/superpowers/plans/2026-11-03-card-1677-runner-worktree-retention-plan.md §1),
then `TempSweep:Execute=true`. Turn `Families:claude-scratch` on only after its dry-run list has been read.
Rollback: `TempSweep:Enabled=false` (or `Execute=false` for a dry run).

<a id="linux-docker-production-contract"></a>

## Production deployment contract (CARD-1221)

Accepted 2026-11-07. CARD-1741 transcribes the decision
[2026-11-07 CARD-1221 Linux/Docker production contract](investigations/2026-11-07-decide-card-1221-linux-docker-production-contract.md)
(`86ae197cddfc4133834f38a9e26fd07e35509079`). This section copies those dispositions. It does not
change them, and it does not authorize a cutover. Source citations below are that decision's
citations at `5d5973be2cd867bf592cd0363240e728f64d3b28`. Live runner capabilities were not
re-probed. The 2026-10-13 readiness report is not in this checkout.

**Topology A is the supported production deployment. Topology B is the accepted target. Topology C
is unsupported.** Positive per-session memory caps are unsupported. The root `docker-compose.yml`
four-service stack is a supported Linux application core with an explicit exclusion list. It is
not production, and it does not prove the desktop control plane.

"Supported on Linux" means the current code can run that service on a Linux server without a
Windows API. It does not mean the base compose file turns the service on.

### Topologies

| Id | Shape | Contract |
|---|---|---|
| A | server2 runs the phone-home runner only. The desktop runs AppHost (server, built client, Windows session runner, fake gateway, storybook), Postgres, and Redpanda. The public origin `https://antiphon.desktop.codeperf.net` fronts that desktop server. | Supported production today. |
| A2 | Host-daemon Postgres on server2 (CARD-1406). The desktop server keeps running and changes its connection string. AppHost, autostart, and `deploy-server2.ps1` stay. | CARD-1401 / CARD-1406. Not a control-plane move. Not topology B. The decision treats it as not yet executed: it records CARD-1401 in Review and CARD-1406 in Backlog, and whether the operator has already run the CARD-1407 window is not visible from the checkout. Unverified. |
| B | server2 host daemon runs the server, Postgres, and the phone-home runner. The desktop remains a browser client and the Windows session runner (Herdr, Windows-platform tasks, rollback AppHost). The image's embedded client is `Dockerfile:67-70` (`wwwroot/index.html`). Aspire is not the production supervisor after cutover. | Accepted target. Not authorization to cut over. The ordered cards below are the only path. |
| C | Two live control planes, with failover. | Unsupported. No card. |

A2's host service, as CARD-1406 specifies it: Tailscale bind, not `0.0.0.0`, not port 5433, not
Alpine, not nested dockerd, `pg_stat_statements` preload, new password. Port 5433 is Windmill and
stays Windmill. The volume name is `antiphon-server2-postgres_pgdata`, not `antiphon_pgdata`, not
`dind-data`, not Windmill. A2 does not wait for B. B's cutover waits for A2.

### Stacks

| Stack | What it is | Production? |
|---|---|---|
| Desktop control plane | Aspire AppHost plus `docker-compose.dev.yml` Postgres (`antiphon-postgres`, port 17280, volume `antiphon_pgdata`) and Redpanda (`:19092`). `docs/bootstrap.md` sections 4 and 6. | Yes. This is production today. |
| Persistent server2 runner | `docker-compose.server2-runner.yml`, project `antiphon-runner`. Phone-home only. Compose comment at lines 1-3, and the persistent-runner paragraph below: no standing Antiphon server or Postgres. | Yes, as an execution runner. Not a control plane. |
| Self-contained application core | Root `docker-compose.yml`: `state-init`, `antiphon`, `session-runner`, `postgres`. Shipped under CARD-0590 (Done). | No. Test and nested throwaway stack. |

### Linux land credential and repository mirrors (CARD-1776)

CARD-1775 owns the production server compose. This card does not edit that file.
`docker-compose.server2-land-credential.yml` is the include that mounts
`${RUNNER_GITHUB_TOKEN_DIR}` read-only at `/run/antiphon/github-token` and
`${SERVER_PUSH_ALLOW_LIST}` read-only at `/run/antiphon/push-allow-list`. The
same fragment mounts `${RUNNER_GIT_IDENTITY_FILE}` read-only at
`/run/antiphon/gitconfig` and sets `GIT_CONFIG_GLOBAL` to that path. The server
image bakes the credential helper and `docker/server/gitconfig`. The token stays
in the mounted directory. The identity file stays on the host. Land checkouts
are `/work/repos/<name>` on the production work volume.
`scripts/server2-repository-mirrors.ps1` defaults to `-WhatIf`. Cutover rewrites
`Project.LocalRepositoryPath` from a recorded before column; rollback restores
that before column. A missing credential mount refuses land with
`push_credential_absent` before git starts. A configured but unusable identity
file refuses with `git_identity_absent` before git starts. Custody is in
[agent-credentials.md](agent-credentials.md).

### Standing agents and specialists on the sidecar (CARD-1778)

In topology B the server image does not contain the agent executables, so production sets `SessionRunner:VerifyExecutablesOnServer` false and a missing executable is the sidecar's spawn failure (an incident per attempt), not a server 409; the desktop leaves the setting unset and the default true still checks the local disk.

The sidecar sets `CLAUDE_CONFIG_DIR=/state/claude`, the directory `server-state-init` creates at `/runner-state/claude`. The runner seeds Claude trust for the launch working directory in that file just before it spawns. The operator logs Claude in once on the sidecar with `docker exec -it -u 1654:1654 -e HOME=/home/app -e CLAUDE_CONFIG_DIR=/state/claude <sidecar> claude auth login`. That command carries no token. Always-on agents run in their own clone `/work/agents/<name>`. In the window, `PATCH /api/agents/{id}` sets `workingDirectory` to that path, `sessionBackend` to `PtyHost`, and clears `herdrWorkspaceLabel` and `herdrTabLabel`. The server writes orchestrator instructions at `/work/.antiphon/orchestrator-instructions.md` on the shared work volume. The launch environment still sets `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS` to that path and `ANTIPHON_ORCHESTRATOR_INSTRUCTIONS_URL` to the instructions URL, which remains the fallback when the file cannot be read. CARD-1695 remains the phone-home always-on path; this compose does not change it. Operator steps are in the CARD-1778 plan, [re-home agents to the sidecar](superpowers/plans/2026-11-08-card-1778-rehome-agents-to-sidecar.md).

`deploy-server2.ps1:1` says "Run from the desktop after Review, never on server2." Its default API
is `http://localhost:17202` (`deploy-server2.ps1:44`). Rolling phases call that API and SSH to the
host. They assume the desktop process owns runner state.

### One database writer

One database writer. Hangfire:Storage defaults to InMemory, which has one worker and no leader election. When Hangfire:Storage is PostgreSql, the process holds a Postgres advisory lock and a second worker against that database is refused. Phone-home has one
`PhoneHome__ServerOrigin` (`docker-compose.server2-runner.yml:54`). Runner capacity, custody, and
the data-protection key ring are single-server state. Two servers on one database would
double-fire retention and legacy release. A second server on the production database is forbidden
at every step before CARD-1744, and forbidden after it too. A hot spare that is not opening the
database and not registering Hangfire is a future idea, not this contract.

The PostgreSql worker calls `pg_try_advisory_lock(1743, 1)` on a connection that does not use the pool. The lease is released on `ApplicationStopped`, after the Hangfire server has stopped. Every 15 seconds the process reads `pg_locks` for its own backend. The check stores nothing. That 15 seconds is the detection bound: how long this process can run without re-confirming the lock. A successor can overlap for those 15 seconds plus the Hangfire drain (`BackgroundJobServerOptions.ShutdownTimeout`, 15 seconds by default) plus host shutdown (`HostOptions.ShutdownTimeout`, 30 seconds by default). A missing session opens a new non-pooled connection and takes the lock again. A holder that is this process's own previous backend is not another worker. When another worker holds the lock, the host stops on that tick. A tick that cannot reach Postgres retries until 60 seconds after the last confirmed hold, then the host stops. The host does not stop on the first unreachable tick. Dispose bounds the unlock command timeout at 5 seconds. CommandTimeout=5 applies to every lease command. A crashed host leaves the lock until Postgres closes the dead session. The lease sets `tcp_keepalives_idle` to 30, `tcp_keepalives_interval` to 10, and `tcp_keepalives_count` to 3, so that lock ends within 60 seconds of silence. A paused process still answers TCP keepalives, so the integration witness reads those socket settings on the Linux Postgres image instead of waiting out a silent peer. Windows-native Postgres reports `tcp_keepalives_count` as 0. An operator clears a leftover holder with `SELECT pg_terminate_backend(pid) FROM pg_locks WHERE locktype = 'advisory' AND classid = 1743 AND objid = 1 AND granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database()) AND objsubid = 2`. `Hangfire.PostgreSql` 1.21.1 is LGPLv3. Its Dapper.AOT analyzers ship `Dapper.AOT.Analyzers.dll` and `Microsoft.SqlServer.TransactSql.ScriptDom.dll`. This repo resolves Npgsql 9.0.3. The schema install runs `CREATE SCHEMA`, which needs the database CREATE privilege. `AddOrUpdate` rewrites the type name stored on a recurring row. Enqueued or scheduled instances keep the invocation that was already queued.

Desktop legacy release is on and executing (`server/appsettings.json:230-232`,
`WorktreeLegacyReleaseSettings.cs:8-21`). Hangfire registers it whenever the worker is on and the
flag is true (`Program.cs:1196-1199`). A Linux server with the worker on must set
`WorktreeLegacyRelease__Enabled` false until cutover, or it becomes a second releaser. Runner
worktree retention is also on, for runner `server2` (`server/appsettings.json:243-247`). Same
single-worker rule.

Production Postgres or the production server created by the runner's nested dockerd is forbidden.
CARD-1406 and CARD-1742 are host-daemon projects. Do not promote the root `docker-compose.yml`
Postgres command into production: that test Postgres has no `pg_stat_statements` preload, and
CARD-1406's host service does.

### Services the base compose turns off

Root `docker-compose.yml:41-49` (and `:46`, which the 2026-10-13 list omitted). Every row keeps
the decision's disposition and owner card.

| Service | Base compose | Desktop today | Disposition | Blocker | Owner card |
|---|---|---|---|---|---|
| Check interpreter, diagnose, output distiller | `Delegation__CheckInterpreterEnabled`, `DiagnoseEnabled`, `OutputDistillerEnabled` false (`docker-compose.yml:41-43`) | On. Hosted services no-op unless the flag is true (`AgentTaskCheckHostedService.cs:84`, `DiagnoseHostedService.cs:36`, `OutputDistillationHostedService.cs:36`). Code defaults are true (`DelegationSettings.cs:813`, `:877`, `:959`). | Supported on Linux | Tracked working directories are `C:\logs\antiphon\check-interpreter` and `C:\logs\antiphon\diagnose` (`server/appsettings.json:362-363`). The Linux stack must override them, as the root compose does at `:39-40`. | CARD-1742 (staging compose). No port card. |
| Hangfire worker | `Hangfire__ServerEnabled` false (`:44`) | On (`server/appsettings.json:185-187`). Hangfire:Storage defaults to InMemory (`server/Program.cs:989-993`). Jobs register only when the worker is on (`:1177-1212`). | Supported on Linux as a process. PostgreSql storage is opt-in and keeps recurring jobs across a process restart. | Census must stay unregistered or the WMI job throws. The base compose does not set Hangfire__Storage and keeps the worker off. | CARD-1743. Do not enable the worker against the production database from a second server. |
| Zombie census | `ZombieCensus__Enabled` false (`:45`) | On (`server/appsettings.json:208-209`). `Program.cs:622` always registers `WindowsZombieProcessCensus`. `SnapshotAsync` throws off Windows (`WindowsZombieProcessCensus.cs:17-18`). The job is added only when both the worker and `census.Enabled` are true (`Program.cs:1177-1180`). When the worker is on and the census flag is false, the stored recurring row is removed. | Not supported | WMI snapshot. Enabling the flag on Linux throws. | CARD-1222 (Backlog). Stays off in every Linux compose until that card lands. |
| Worktree residue | `WorktreeResidue__Enabled` false (`:46`) | On (`server/appsettings.json:218-219`). Sweep path compare is platform-aware (`WorktreeResidueSweepService.cs:755-758`). No WMI call. | Supported on Linux | Needs the Hangfire worker, and must sweep only worktrees that server can see. | CARD-1742 for a scratch server. Not a second worker on the production database. |
| Schedules | `Schedules__Enabled` false (`:47`) | On (`server/appsettings.json:413-414`). `ScheduleSweepHostedService.cs:35-38` returns when disabled. This sweep is not a Hangfire job. | Supported on Linux | None in code. Off in the application core by configuration. | CARD-1742. |
| Channel bridge | `ChannelBridge__Enabled` false (`:48`) | Forced on by AppHost (`Antiphon.AppHost/Program.cs:73`). Tracked `server/appsettings.json:391-392` is false. Hosted service starts only when enabled (`Program.cs:878-880`). | Supported on Linux once a broker is reachable. Not supported inside the four-service stack. | No broker in root compose. Port 19092 was not measured on 2026-10-21. Desktop `:17202` from server2 timed out that day (`docs/investigations/2026-10-21-card-1401-postgres-server2-and-snapshots.md:64`). | CARD-1746. |
| Digest | `Digest__Enabled` false (`:49`) | Tracked config off (`server/appsettings.json:403-404`). `AwayDigestHostedService.cs:17` returns when disabled. | Supported on Linux as a flag. Not part of the required production set. | None. Matches the desktop tracked default. | None. Stays off. |
| Messaging broker | Not in the four-service file | `docker-compose.dev.yml:48-75`, Redpanda, host `19092`. AppHost forwards `AntiphonMessaging:BootstrapServers` only when that value is set (`Antiphon.AppHost/Program.cs:63-85`). | Supported on Linux as a container. Placement is undecided. | Reachability between server2 and the desktop gateway hosts is unmeasured for 19092. | CARD-1746. |
| Messaging gateway | Not in any compose file | Fake gateway is a Windows exe (`Antiphon.AppHost/Program.cs:45-52`), dev/test only. Real gateways are separate processes (`docs/bootstrap.md:288-291`). | Supported only on the desktop (their current hosts). Not part of the Linux server image. | A Linux control plane still needs those processes to reach the broker. | CARD-1746 places the broker. It does not move gateway processes. |

### Section 2 features

From the 2026-10-13 report, checked by the decision against `5d5973be`. Same rule: disposition and
owner card stay as named there.

| Feature | Disposition | Blocker | Owner card |
|---|---|---|---|
| Zombie / OS census | Not supported | Same WMI throw as the compose row. | CARD-1222 |
| Claude RC watch | Supported only on the desktop control plane | `Program.cs:655-656` registers `WindowsRcBridgeProbe`. `GetExtendedTcpTable` is the evidence source (`WindowsRcBridgeProbe.cs:57-62`). Any failure returns 0 (`:90-94`), so a Linux server reports no connections and does not prove the watch. A zero is not evidence. | CARD-1223 (Backlog) |
| Herdr disposal | Supported only on the desktop Windows runner | Locator calls `CreateFileW` (`HerdrDisposalLocatorStore.cs:72`). The inspector returns incomplete off Windows (`HerdrDisposalProcessInspector.cs:23-24`). Both Linux composes set `SessionRunner__Herdr__Enabled` false (`docker-compose.yml:89`, `docker-compose.server2-runner.yml:96`). Phone-home projection sets `Herdr = null` (`PhoneHomeLaunchPolicy.cs:271`). | CARD-0864 (disposal), CARD-0872 (peer uid). Herdr stays off on Linux runners. Topology B keeps the Windows runner for Herdr sessions. |
| Positive per-session memory caps | Not supported | `WindowsJobObject.AssignMemoryLimitedJob` throws off Windows (`WindowsJobObject.cs:34-35`). Phone-home projection sets `MemoryLimitMb = 0` (`PhoneHomeLaunchPolicy.cs:269`). The server2 runner comment says there is no cgroup memory cap (`docker-compose.server2-runner.yml:68`). Custody cgroups are descendant containment, not a memory limit (`LinuxCgroupCustodyProbe.cs:6-16`). The persistent runner paragraph cites host kernel 4.15, and the custody probe advertises only cgroup v1 (`LinuxCgroupCustodyProbe.cs:28-31`). | None. Parity is explicitly not required. CARD-0038 allowed this alternative. A positive cap on a phone-home launch is discarded; a direct Linux launch that asks for one throws. Do not configure a positive cap and expect containment. |
| SourceLanding custody | Not supported on the live contract | The Linux backend exists (`VerificationCustody.cs:29`, probe at `LinuxCgroupCustodyProbe.cs:24-38`) and stays unadvertised unless delegated root, cgroup v1, the custody root, `NoNewPrivs=0`, and both sudo helpers succeed. On 2026-10-13 the server2 runner reported `verificationCustodyBackend=null` (readiness report). That observation was not repeated. The decision records CARD-0604 in Review. CARD-0634 (Backlog) still records the native containment and egress gap. | CARD-0634, after CARD-0604. Ordinary sessions do not need it. |
| Orchestrator deny hook | Not supported | The hook command is `powershell` (`DelegationWorktreeService.cs:505-517`). The comment at `:502-503` says Windows PowerShell 5.1 on purpose. The Linux runner image installs `pwsh`, not `powershell`. | CARD-1225 (Backlog). An orchestrator session on Linux that arms this hook does not get the deny behavior. |
| Delivery ceilings | Supported on Linux at the inbox numbers | `CeilingsFor` maps anything other than `ModernConPty`, including `UnixPty`, to inbox ceilings (`DelegationSettings.cs:292-299`): brief 900 bytes (`:160`), reply 3,000 chars (`:116`), single chunk 1,024 bytes (`:202`). Runner-bound input forces that inbox profile (`AgentTaskReplyService.cs:1096-1098`). | CARD-1224 (Backlog) measures and may raise them. No new number is set here. |
| Windows-only deployment pieces | Supported only on the desktop control plane | AppHost exe paths (`Antiphon.AppHost/Program.cs:34` and `:47`). Scheduled Tasks via `scripts/install-autostart.ps1` (`docs/bootstrap.md:251-259`). `deploy-server2.ps1` default API and "run from the desktop" (`:1`, `:44`). | CARD-1744 retargets the API when the control plane moves. CARD-1745 replaces the AppHost SHA gate. The Windows runner and Scheduled Tasks stay for the desktop rollback path and for Windows-platform work. |

CARD-1226 is a Windows CI assertion fix. It is not a deployment feature.

### Migration path

Stay on A until a later card in this list lands. Keep dispatching to the server2 runner. The
accepted order is CARD-1406, then CARD-1742, then CARD-1743, then CARD-1745, then CARD-1746, then
CARD-1744. CARD-1407 is the existing database window inside the CARD-1406 step, not a new
topology. Do not file another generic port card.

| Order | Card | What it does | Gate |
|---|---|---|---|
| 1 | CARD-1406 (existing), then the CARD-1407 window | Host Postgres and scratch restore. Desktop server, new connection string, desktop volume kept. | Blocks the cutover. A2 does not move the control plane. |
| 2 | CARD-1742 | Staging server compose against a scratch database, or against CARD-1406's service only after that card is Done. Phone-home origin unchanged. AppHost still production. | No cutover. |
| 3 | CARD-1743 | Hangfire jobs survive a restart. Still one worker. | Blocks the cutover. |
| 4 | CARD-1745 | SHA restart gate against the staging container. | Blocked by CARD-1742. |
| 5 | CARD-1746 | Measure port 19092 before choosing a side, place the broker, then enable the bridge. | Blocked by CARD-1742. |
| 6 | CARD-1744 | Cutover. Stop the desktop server first. One writer. Move origin, vhost, deploy-script API, and the key ring. Leave AppHost installed. | Blocked by CARD-1742, CARD-1406, and CARD-1743. Retires AppHost from the production path without uninstalling it. |

Existing owners stay the owners: CARD-1401 parents the database move; CARD-1222 census; CARD-1223
RC watch; CARD-1224 ceilings; CARD-1225 deny hook; CARD-0864 and CARD-0872 Herdr, which remains
desktop-only; CARD-0604 and CARD-0634 SourceLanding, which stays outside the supported contract
until qualification; CARD-0829 and CARD-0668 recreate identity, which is not topology work.

### Rollback

| Step | Rollback |
|---|---|
| A2 | CARD-1407 points the desktop connection string back at `localhost:17280`. `antiphon_pgdata` stays. |
| Staging server | Remove that compose project. Desktop production is untouched. |
| Cutover | Point phone-home origin and the public vhost back at the desktop, start AppHost, stop the Linux server. Do not delete `antiphon_pgdata`. Do not run both servers. |

Runner image rollback stays the staged rollout above. A control-plane move does not replace those
phases. It changes which API they call (CARD-1744).

Under B, Aspire does not supervise the production server. The server container's restart policy
plus CARD-1745's SHA check replace `restart-apphost.ps1` for that process. AppHost remains
installed so rollback can start the desktop server, the Windows runner, and the fake gateway.
Scheduled Tasks (`docs/bootstrap.md:251-259`) stay the desktop autostart. They are not
reimplemented as Linux cron in this contract. The embedded `wwwroot` client is the B UI. The
desktop Vite/serve client remains the rollback UI.

### Linux server SHA restart gate (CARD-1745)

`scripts/restart-linux-server.ps1` is the host-side gate for one configured Linux server
container. Topology A leaves it off. With no `-Config` and no `ANTIPHON_LINUX_SERVER_CONFIG`,
the script exits 3, prints that the Linux server is not configured, and does not probe, lock,
or call docker. `scripts/restart-apphost.ps1` remains the desktop AppHost restart.

A config file supplies `baseUrl`, `container`, and `lockPath`. The default mode is WhatIf:
`GET /health` and `GET /api/version` against that base URL, with no docker call. Exit 0
requires HTTP 200 from `/health` and a `version` field equal to `-ExpectedServerSha` (40 or
64 hex characters). A mismatch, a malformed body, or a version that is not HTTP 200 is exit 5
and leaves the lock file. `-Execute` probes that same running server once before any docker
call. When the server is already healthy and reports the expected SHA, it prints
`already at expected SHA` and exits 0 without stopping anything. A different well-formed SHA
is compared with the container image: `docker inspect` reads `{{.Image}}` and
`{{.Config.Image}}`, then `docker image inspect` reads the current id of that tag. The same
id means a restart cannot carry a new SHA, so the gate exits 5 with no `docker stop`. A
different id means a new image was pulled for that tag. `docker start` would still run the
old image, and this gate does not own the compose spec, so it does not recreate the
container. That path exits 5 with reason `image-moved`, again with no `docker stop`. A
malformed body, a version that is not HTTP 200, or `/health` not 200 on `-Execute` exits 5
with no docker call. Every `-Execute` refusal leaves the serving container left running on
the image it already had. If that container is later found stopped, `docker start <container>`
resumes that previous image. Replacing the image belongs to the CARD-1742 compose deploy,
then a fresh probe. The script refuses desktop AppHost ports 17200-17209, a short SHA, and
container names for the runner, Postgres, or the gateway, before any docker call. It does not
deploy the runner and it does not install a Scheduled Task. The staging compose remains
CARD-1742; this gate does not create it and it is not the cutover. `docker-compose.server2-server.yml`
is the production Antiphon server for the server2 host daemon. The project is `antiphon-server2-server`.
Services are `server-state-init`, `server`, and `server-session-runner`. The check interpreter, diagnose, and output distiller working directories are `/work/specialists/check-interpreter`, `/work/specialists/diagnose`, and `/work/specialists/output-distiller` on the shared work volume, mounted at `/work` by both `server` and `server-session-runner`. `server-state-init` creates them mode 0755 and chowns them to uid 1654. The server publishes
`127.0.0.1:17400` only and joins the external network `antiphon-server2`, so the connection host is
`Host=postgres` on the CARD-1406 service. `scripts/deploy-server2-server.ps1` defaults to `-WhatIf`.
`-Execute` is host-only and then calls this gate verify-only, with no execute switch on the gate,
against `http://127.0.0.1:17400` and container `antiphon-server2-server-server-1`. Before up, `-Execute`
refuses `revision-mismatch` unless the env `SOURCE_REVISION` equals `-ExpectedServerSha`, and
`head-mismatch` or `head-unknown` unless `git rev-parse HEAD` of the compose build context does too
(CARD-1788). `server-state-init` chowns `/work` and its top directories one level and walks only
`/work/specialists`, so an up does not walk every repository and worktree. `Hangfire__Storage`
is an explicit setting whose default is `InMemory` because CARD-1768 is on this tip and CARD-1774
A-1/A-2 are not. Nothing in this card starts the project or touches the production database. This slice
adds no cache.

### What `deploy-server2.ps1` deploys

`scripts/deploy-server2.ps1` deploys the phone-home runner from a checkout that is not the host
being mutated. It does not deploy the server or Postgres. `ANTIPHON_API` is required. An unset or
whitespace value exits 2 and names this document before any operator-token read or docker call.
There is no `localhost:17202` default. The CARD-1221 transcription above records the previous
default at `deploy-server2.ps1:44`. The named phases and their stop gates stay as they are.
`-Phase all` is unchanged. Census, drain, and retire keep their stop gates. A control-plane
cutover is not a rolling runner phase. Do not drain the only accepting runner in order to move
the server.

After the control plane moves, the operator token is the 32-byte hex value the Linux server
creates at `Operator:TokenPath` on first start. The production path is `/state/operator-token`.
The operator owns the copy and places it, without display, in the desktop file
`%LOCALAPPDATA%\Antiphon\operator-token` or the path named by `ANTIPHON_OPERATOR_TOKEN_FILE`.
`deploy-server2.ps1` reads that file on every phase except `check-census` and never prints the
value.

Host jq, volume recycle, and temp retirement are runner-host operations. They are unchanged by
where Postgres sits.

### Secrets and custody

| Material | Where it lives today | Move rule |
|---|---|---|
| Postgres password | Desktop compose and appsettings for dev. Production override is a user secret. | New password in an untracked `0600` env file on the server2 host (CARD-1406). Never the image, never a log. |
| Phone-home secret, deploy key | File secrets interpolated by `docker-compose.server2-runner.yml:146-148`. Entrypoint stages the phone-home secret to `/run/antiphon/phone-home`. | Stay file mounts. The runner already has them. The server move does not copy them into the server container. |
| Claude OAuth token, Codex home, GitHub token dir, git identity | Host files, read-only mounts (`docker-compose.server2-runner.yml:133-145`). The persistent runner paragraph below, and `docs/agent-credentials.md`. | Unchanged by the control-plane move. They belong to the runner. |
| Agent TUI data-protection key ring | Empty `AgentTui:KeyRingPath` resolves to `%LOCALAPPDATA%\Antiphon\DataProtection-Keys` on Windows and to the Unix data root on Linux (`AgentTuiSettings.cs:40-54`). Root compose sets `/state/keyring` (`docker-compose.yml:38`). | CARD-1744 copies or re-protects the ring before the Linux server serves. A new empty ring cannot read existing protected secrets. |
| AppHost user-secrets (`aspire-antiphon-apphost`) | Desktop only. Connection string and optional broker override. | Not inside the Linux image. The staging and production server compose take env or mounted files. |
| Operator token | 32-byte hex owner-only token created by the Linux server at `Operator:TokenPath` (production `/state/operator-token`) on first start. The operator copies it, without display, to `%LOCALAPPDATA%\Antiphon\operator-token` or `ANTIPHON_OPERATOR_TOKEN_FILE`. | `deploy-server2.ps1` reads that desktop file for every phase except `check-census` and never prints the value. The operator owns the copy. |

The database size, snapshot script (CARD-1405, `scripts/postgres-snapshot.ps1`), cutover script
(CARD-1407, Done, `scripts/cutover-postgres-server2.ps1`), and monitor (CARD-1408, Done) stay
owned by CARD-1401. The decision did not rerun that move. Encrypt before anything syncs. The
password and the age key are not printed. The new cluster password is not the committed dev value
in `docker-compose.dev.yml:31`. On 2026-10-21, server2 host port 5432 was refused on Tailscale
`100.93.77.126` and LAN `192.168.4.34`. That observation was not repeated.

### Unverified

- No fresh image smoke and no live `/capabilities` read in the decision. SourceLanding's null advertisement is the 2026-10-13 observation, not repeated.
- Broker port 19092 was not measured in either direction. CARD-1746 measures it.
- Desktop reachability of a future server2 HTTPS origin was not measured. The 2026-10-21 reverse path (`:443` open, `:17202` timeout) is not a substitute.
- Whether the CARD-1407 window has already been run is not visible from the checkout. The decision treats the database as still on the desktop.
- Digest and the real gateway processes may have machine-local overrides this repo does not track. Tracked config is the contract used here.
- A cgroup v1 `memory.limit_in_bytes` cap on kernel 4.15 is not this contract. It is only a possible later implementation if a future decision reverses the unsupported memory-cap disposition.

### Risks the contract already refuses

- Enabling census on Linux. The job throws.
- A Linux Hangfire worker left with `WorktreeLegacyRelease:Enabled` true. It files releases.
- Phone-home silently dropping a positive memory cap. Unsupported means do not set one.
- RC watch returning 0 on Linux and being read as "no Claude connections."
- Deny hook naming `powershell` on a Linux orchestrator session.
- SourceLanding admitted when the runner advertises no backend. The probe is supposed to prevent that. CARD-0634 is still open, so SourceLanding on server2 is outside this contract.
- PID reuse after a container recreate (CARD-0829, CARD-0668). The decision records both as Backlog. A runner recreate during B is the same risk as today.
- Public vhost and DNS. CARD-1744 stops for the operator. It does not edit Cloudflare.

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
timeout (default 45 minutes), measured with `date +%s` so a slow reader cannot stretch the wait
past that deadline. A probe that exits 4, or a wait that reaches that deadline while a
slot is still ungranted, is `FixtureBuildSlotTimeout`. The offline p3 restore uses the same
gate: exit 4 is `FixtureBuildSlotTimeout`, and any other offline failure stays
`FixtureOfflineRestoreFailed`. Once both slots are held, both restores
must start within the existing 30-second window or the diagnosis stays `FixtureRestoreBarrierFailed`.
The first granted slot stays held until the second grant or that 45-minute deadline. The fixture
does not release that slot to retry; a retry would drop the grant and reopen the arrival window.
A refusal, a slot timeout, or INT/TERM waits up to 60 seconds for an in-flight probe to publish
its container id or for that probe's job to exit, then kills and removes only a 64-hex id this run
recorded after launch whose labels are `io.antiphon.owner=c849-fixture`, `io.antiphon.fixture-run`
for this run, and the probe role. An absent recorded container is already gone and is not foreign
residue. A same-named container the launcher refused is left untouched and recorded as residue.
TERM exits 143. A pid-only SIGINT can be dropped while the shell waits on a foreground child that does not die of it; the fixture repeats that signal until the trap runs, and the trap then exits 130. A process-group SIGINT is the operator Ctrl-C. The probes survive it, because their PID 1 has no SIGINT handler, and only a proven container id is killed. That path exits 130 when the trap finishes, or 2 when the barrier records FixtureRestoreBarrierFailed first. Neither outcome is fail-open. The release also DELETEs any granted build-slot lease and removes
the fixture volumes. The signal handler invokes the existing idempotent finalizer before exiting: a signal can interrupt an active EXIT finalizer, and EXIT cannot run again after that handler exits. Unknown ownership or cleanup outcomes still retain residue and refuse success. It does not wait on a shell pid that is still running. TERM and `kill -9` are
sent only while `jobs -pr` lists that pid. The driver stops waiting for `go` at the same 45-minute
slot deadline and exits 4, which releases the lease. The first granted slot still stays held until
the second grant or that deadline.

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

`docker-compose.server2-runner.yml` (project `antiphon-runner`) is the persistent phone-home runner on server2. The host Postgres service `docker-compose.server2-postgres.yml` (project `antiphon-server2-postgres`) exists beside the runner on the host daemon. It is not the nested dockerd inside the runner and it is not Windmill. The runner is the one privileged service in the repository, it owns the `dind-data` volume as the nested daemon's store, and it requires both file secrets (`ANTIPHON_DEPLOY_KEY_FILE`, `PHONE_HOME_SECRET_FILE`) by interpolation. It also requires `CLAUDE_OAUTH_TOKEN_FILE`, the Claude setup-token file on the server2 host (0600). A deploy leaves that file alone unless `-RefreshClaudeToken` is passed; the host lane creates it empty when it is missing. It is mounted read-only at `/run/antiphon/claude-oauth-token`; `dind-entrypoint.sh` exports `CLAUDE_CODE_OAUTH_TOKEN` from it into the runner alone. No compose file lists that variable under `environment:`, where `docker inspect` would print it (CARD-0628). The throwaway stack a session stands up is the **nested** child: the unmodified base `docker-compose.yml` on the runner's own daemon, in a run-scoped project, torn down with `down -v`.

The runner mounts the named `runner-tmp` volume at `/tmp` (CARD-0827). The persistent `antiphon-runner` and temporary `antiphon-runner-temp` Compose projects each get their own volume; the exact-name retirement coordinator in `scripts/c590-remote.sh` removes the temporary project's volume only after its current content proof and durable delete intent. An existing container's writable-layer `/tmp` is not migrated when this mount is introduced. Keep cleanup restricted to an explicit allow-list of disposable families: `/tmp/antiphon-pty-hosts` is runner state and must never be swept by an `antiphon-*` name pattern. Its `bin/<stamp>` shadow copies are reused when the published host's content hash matches and the host executable and deps.json are present. If either file is missing, `ShadowCopyStore.EnsureCurrent` stages a new generation and `PtyHostLauncher` refreshes its cached path before the next launch; the damaged generation remains until normal cleanup. The runner's startup adoption sweep checks persisted manifests against host liveness, records dead hosts as exited, and removes invalid manifests; `AuditCleanupService` prunes unreferenced shadow copies and old logs. These paths tolerate stale generations, logs, and manifests after a recreate. Protect `/tmp/antiphon-pty-hosts` from external cleanup even though the launch path can repair a missing host executable or deps.json. Under `SessionRunner__PtyHostSocketDirMode=Owned` (CARD-1653 S2, default `Legacy`) new session sockets live in `/tmp/antiphon-pty-hosts/sock` (runner-owned, 0700); it is runner state like the rest of the directory. Roll out with `Owned` plus `SessionRunner__PtyHostPeerCheck=Observe` first and grep the runner log for `pty_host_peer_mismatch (observe)`, `pty_host_socket_dir_unsafe` and `pty_host_socket_path_too_long` before `Enforce`; `/`, `/tmp` and `/tmp/antiphon-pty-hosts` must be root- or app-owned and not group- or other-writable unless sticky.

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

## Server2 host Postgres (CARD-1406)

`docker-compose.server2-postgres.yml` is the host-daemon Postgres service. It exists beside the runner. It is not the nested dockerd inside the runner and it is not Windmill. The project is `antiphon-server2-postgres`, the service is `postgres`, the network is `antiphon-server2`, and the volume key is `pgdata` (full name `antiphon-server2-postgres_pgdata`, mounted at `/var/lib/postgresql/data`). The image digest, the tailnet bind, and the cluster password come from an untracked mode-0600 env file outside the repo. The published port is `${ANTIPHON_POSTGRES_BIND_ADDRESS:?}:5432:5432`. An empty bind refuses to start. There is no `0.0.0.0` publish, no LAN publish, and no port 5433.

`scripts/deploy-server2-postgres.ps1` defaults to `-WhatIf` and does not call docker in that mode. `-Execute` runs only when this process is on the server2 host daemon. Neither mode prints the password. The new cluster password is not the committed dev password. Exit codes: 0 plan or ok, 2 refusal, 3 ambiguous-mode.

`-WhatIf` refuses a bind outside `100.64.0.0/10` (`bind-not-cgnat`), including a LAN or public address. `-Execute` still requires the bind to equal the single line of `tailscale ip -4` on that host. The image must be `postgres:16@sha256:` plus 64 lowercase hex digits, or the RepoDigest form `postgres@sha256:` plus 64 lowercase hex digits. A tag or any other reference is `image-floating`. `alpine` anywhere in the reference is `image-alpine`.

The production file runs only as project `antiphon-server2-postgres`. Its volume name is pinned, so `docker compose -p antiphon-server2-postgres-drill -f docker-compose.server2-postgres.yml` still mounts `antiphon-server2-postgres_pgdata`. The script refuses that pair with `production-compose-wrong-project`. Do not improvise it.

### Scratch restore drill

The drill is an operator rehearsal on the server2 host. It is not a CI test. `-RestoreDrill` prints the command sequence below and the readings that are still unread. It does not invent those readings, it does not call docker, and it does not start the scratch project.

The scratch file is `docker-compose.server2-postgres-drill.yml`. Its project is `antiphon-server2-postgres-drill`, its volume is pinned to `antiphon-server2-postgres-drill_pgdata`, its network is `antiphon-server2-postgres-drill`, and its only publish is `127.0.0.1:5434:5432`. Rendering that file with the production project name still mounts the scratch volume, because the volume name is pinned. The script refuses the drill file under the production project (`drill-compose-wrong-project`). The drill file has no production volume pin and no tailnet bind variable.

Read on the host, not from the desktop and not from inside the runner: `docker ps`, `docker volume ls`, and `df` of the Docker data-root. Free space must be many times the desktop database size measured 2026-10-21 (1,265,417,239 bytes). Re-list the parent of the live Windmill compose and record it. The 2026-09-17 note of `/home/mc/docker/windmill` is that note, not a measurement.

`-RestoreDrill` prints this sequence. On the desktop, take or locate the encrypted B1 snapshot (`antiphon-*.dump.age` from `scripts/postgres-snapshot.ps1 -Execute`). No step writes a plaintext dump on the desktop or leaves one on either host. Time the copy of that ciphertext to server2 (`mc@server2`). On the server2 host, decrypt with `age -d` into `/var/tmp/antiphon-server2-postgres-drill/antiphon-drill-plain.dump` (mode 0600), restore into the scratch volume only, run the acceptance queries, then shred the plaintext. The desktop ciphertext stays. The transferred ciphertext is not deleted. `$AGE_IDENTITY` and `$AGE_RECIPIENT` are paths, never a key. Run the snapshot, the locate, and the timed copy on the desktop. Run every other line on the server2 host. The teardown removes the scratch project and then the scratch volume only when that volume name ends with `-drill_pgdata`.

```text
pwsh -NoProfile -File scripts/postgres-snapshot.ps1 -Execute -Container antiphon-postgres -RecipientFile "$AGE_RECIPIENT"
SNAPSHOT=$(ls -1t "$SNAPSHOT_DIR"/antiphon-*.dump.age | head -n 1)
mkdir -p -m 0700 /var/tmp/antiphon-server2-postgres-drill
docker volume create antiphon-server2-postgres-drill_pgdata
docker compose --env-file <env-file> -f docker-compose.server2-postgres-drill.yml -p antiphon-server2-postgres-drill up -d
copy_start=$(date +%s); scp "$SNAPSHOT" mc@server2:/var/tmp/antiphon-server2-postgres-drill/antiphon-drill.dump.age; copy_end=$(date +%s); echo copy-elapsed-sec=$((copy_end - copy_start))
umask 077; age -d -i "$AGE_IDENTITY" -o /var/tmp/antiphon-server2-postgres-drill/antiphon-drill-plain.dump /var/tmp/antiphon-server2-postgres-drill/antiphon-drill.dump.age; chmod 0600 /var/tmp/antiphon-server2-postgres-drill/antiphon-drill-plain.dump
docker exec -i antiphon-server2-postgres-drill-postgres-1 pg_restore -U antiphon -d antiphon --no-owner --no-acl < /var/tmp/antiphon-server2-postgres-drill/antiphon-drill-plain.dump
docker exec antiphon-server2-postgres-drill-postgres-1 psql -U antiphon -d antiphon -At -c "select relname, n_live_tup from pg_stat_user_tables order by relname"
docker exec antiphon-server2-postgres-drill-postgres-1 psql -U antiphon -d antiphon -At -c "select \"MigrationId\" from \"__EFMigrationsHistory\" order by \"MigrationId\""
docker exec antiphon-server2-postgres-drill-postgres-1 psql -U antiphon -d antiphon -At -c "select extname, extversion from pg_extension order by extname"
shred -u /var/tmp/antiphon-server2-postgres-drill/antiphon-drill-plain.dump
docker compose --env-file <env-file> -f docker-compose.server2-postgres-drill.yml -p antiphon-server2-postgres-drill down
if case antiphon-server2-postgres-drill_pgdata in *-drill_pgdata) true ;; *) echo 'SERVER2-POSTGRES fail exit=2 reason=volume-forbidden'; exit 2 ;; esac; then docker volume rm antiphon-server2-postgres-drill_pgdata; fi
```

Compare phase 3 of the CARD-1401 plan as well: sequence name / `last_value` / `is_called`, and count, minimum primary key, and maximum primary key on TranscriptEntries, AgentTasks, SessionQueuedMessages, AgentTaskLandNotifications, and AgentTaskEvents. All five are over 32 MiB, so the check is not a row-body checksum. Do not print transcript text. Record dump elapsed time, restore elapsed time, copy elapsed time, and the dump file size. The script has no command that removes `antiphon_pgdata` or the production volume.

## Staging server on the server2 host (CARD-1742)

`docker-compose.server2-staging-server.yml` is a staging Antiphon server on the server2 host daemon. The project is `antiphon-server2-staging`. It is not the phone-home runner project, not the nested dockerd, not Windmill, and not the desktop production database. It does not attach to the CARD-1406 host Postgres service. That service's operator drill is still owed, so this stack brings its own scratch Postgres on the network `antiphon-server2-staging`.

Services are `staging-state-init`, `staging-server`, and `staging-postgres`, so a mistaken project name cannot replace the phone-home runner's `state-init`. The scratch cluster volume is `antiphon-server2-staging_pgdata`, mounted only inside this project. Its image digest and password come from an untracked mode-0600 env file outside the repo (`ANTIPHON_STAGING_POSTGRES_IMAGE`, `POSTGRES_PASSWORD`). The image reference must be `postgres:16@sha256:` or `postgres@sha256:` plus 64 lowercase hex digits. There is no published database port. The server listens on `127.0.0.1:17420` only. The scratch cluster is limited to 1g and 1 CPU.

`scripts/deploy-server2-staging-server.ps1` defaults to `-WhatIf` and does not call docker in that mode. `-Execute` runs only when this process is on the server2 host daemon. Neither mode prints the password. The new cluster password is not the committed dev password. Exit codes: 0 plan or ok, 2 refusal, 3 ambiguous-mode. Acceptance, when an operator does run it, is `http://127.0.0.1:17420/health` and `http://127.0.0.1:17420/api/version`.

Hangfire is on. Zombie census is off. Schedules, the check interpreter, diagnose, and the output distiller are on, with working directories `/state/check-interpreter`, `/state/diagnose`, and `/state/output-distiller` on this project's state volume `antiphon-server2-staging_server-state`. The agent TUI key ring is `/state/keyring` on that same volume. A fresh empty ring cannot read secrets protected by the desktop ring; CARD-1744 copies that ring. Agent executables here are Linux paths; with the session runner off, specialists do not launch until cutover. Worktree residue is on and `Execute` is false (report only, the desktop default); the only worktree root is `/work/worktrees` on this project's work volume `antiphon-server2-staging_work`. Legacy release is off. Runner worktree retention is off, so this worker does not send removals to the phone-home runner. Channel bridge and digest are off. This project has no session runner and does not set a phone-home origin. `deploy-server2.ps1` does not deploy it. AppHost and the public vhost stay on the desktop.

Nothing in this card starts the project. Rollback is to remove the compose project. Desktop production is untouched. This slice adds no cache.

## Native Windows phone-home runner

A second Windows host (a laptop) can run the same session runner natively and phone home to the
desktop server exactly as server2 does: it registers at `/api/session-runners/register` with the
shared secret, then holds a WebSocket at `/api/session-runners/<id>/connect` through
`https://antiphon.desktop.codeperf.net` (Caddy, then Vite's `/api` proxy). It opens no inbound
port. Its sessions are native Windows processes with the host's own `claude`, `codex` and `grok`,
so it adds Windows seats, not Linux ones.

Runner-side paths may be drive-rooted on both sides of the protocol: `PhoneHome:*` on the runner
and `PhoneHomeRunner:Runners:<id>:*` on the server accept a POSIX path or a drive-rooted Windows
path (`C:/...` or `C:\...`). UNC, device and drive-relative forms are refused. Use the forward-slash
spelling for `RunnerWorkspace` and `RunnerRepository`: it is the form the runner reports
worktree paths in. `PhoneHome:Executables:<claude|codex|grok>` names the executable a projected
bare agent name starts; admission and the sign-in probes still key on the bare name, and an
unconfigured name keeps the Linux behaviour (Codex pinned to `/usr/local/bin/codex`).

1. **Desktop (operator, main checkout).** Generate a secret, then add one map entry to the
   `antiphon-server` user-secrets beside `server2` (PowerShell, not Git Bash):
   `Enabled=true`, `DisplayName=Laptop`, `AllowDelegatedTasks=true`,
   `HostWorkspaceRoot=C:\src\Antiphon`, `RunnerWorkspace=C:/Antiphon/runner/work`,
   `RunnerRepository=C:/Antiphon/runner/work/repos/antiphon`, `MaxCapacity=2`, the laptop's
   `ChildClaudeHome`/`ChildCodexHome`/`ChildGrokHome` and `RawExeAllowList:0` (the start script
   prints all of them), `CallbackOrigin=https://antiphon.desktop.codeperf.net` and `SharedSecret`.
   Restart with `scripts/restart-apphost.ps1`; `GET /api/session-runners` then lists the id.
2. **Laptop.** Write the same secret to `C:\Antiphon\runner\secrets\phone-home` and run
   `pwsh -NoProfile -File scripts/start-windows-phone-home-runner.ps1 -RunnerId laptop` from a normal
   terminal (an agent shell's Job Object would kill the detached pty hosts). The runner clones its
   own checkout into `RunnerRepository` on the first workspace mirror. `GET
   /api/session-runners/laptop/status` reports `available` and `dispatchEligible` once connected.

Like the desktop runner, a Windows phone-home runner advertises the `windows-job-v1` custody
backend, and the server accepts its slash-normalized drive-rooted verification coordinates under
its own roots. A remote SourceLanding Mutation on a Windows runner has not been exercised end to
end yet; keep those on the desktop or server2 until it has.
