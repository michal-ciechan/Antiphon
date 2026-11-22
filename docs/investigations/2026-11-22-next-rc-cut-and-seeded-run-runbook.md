# Next rc cut and seeded Windows Full run

The next available date after `rc/2026-11-30` was **`rc/2026-12-01`** at the 2026-11-22 remote read. The observed master tip was **`72a1da4428d8d0dc8b0ecd8ad533cbad75399288`**. The land of task `4714c580` finished with `reviewed_source_mismatch`, so that tip omits the lane-lock fix. Recheck the land and master before choosing the actual pin. The execution order is **cut the single rc ref → activate and prepare the exact pin in a quiet window → start the seeded Windows Full**.

## 1. Audit master and cut the rc ref

`rc/YYYY-MM-DD` names a long-lived release line. The parser validates the calendar date and uses it for the candidate ID and journal `cutUtc`. The journal separately records actual `createdAt`, and publication derives tag dates from the publisher's UTC clock. Neither owner prescribes today's date or requires a scheduled ship date. The previous November 20 cut used `rc/2026-11-30` at `493bee0ee3af2052575bc7a26e9a313669b2fc2d`. November 22 through November 30 were all occupied. December 1 is the proposed next unused date, subject to a fresh remote read. The orchestrator creates the single remote branch. The candidate journaler expects the branch to exist. Tags, publishing and promotion are subsequent human-only operations. [Release line](../release-gates.md#the-release-line) · [Cut and publish](../release-gates.md#cutting-testing-and-publishing-a-candidate) · [Date parser](../../scripts/lib/Antiphon.Nightly/ReleaseGateCredit.cs#L107) · [Journal clocks](../../scripts/release-candidate.ps1#L190) · [Previous cut receipt](2026-11-20-post-cut-activation-runbook.md) · [Human-only boundaries](../../.claude/skills/antiphon-rc-qualification/SKILL.md#L201)

The caller's direction for this cycle freezes rc pushes while qualification is healthy and uses a fresh cut from master after failure. The standing direct-to-rc rule covers bug/test fixes for code already on rc when an rc repair is authorized. Test-side Debug repairs can go direct under the skill, while product, dispatcher and watchdog changes still require Review. During the freeze, land eligible fixes on master for inclusion in the next cut and retain the failed run and its rc ref. [Standing rc fix rule](../release-gates.md#cutting-testing-and-publishing-a-candidate) · [Skill overrides](../../.claude/skills/antiphon-rc-qualification/SKILL.md#operator-direction-overrides-for-rc-qualification)

Review waiting and running lands, Merge workers and the orchestrator's intended lands before selecting the pin. At 05:51Z the Antiphon list returned 9,801 rows, zero outstanding request/start clocks and zero open Merge or explicitly Windows-required tasks in the queried statuses. Task `4714c580-cfae-476b-892f-cec540489e0e` had a completed request `dc33cdd4-117c-4776-b105-f5fcc77b90ee` at 05:40Z with `sourceRefusalReason=reviewed_source_mismatch`. It expected `8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d` and the source ref resolved to `66ff06eff83edf4542061f5c9f3f3b3c045ebaf1`. Neither SHA was in master. Resolve the reviewed source and land receipt if this repair is intended for the cut, then re-fetch and audit the resulting master. If the caller chooses the observed pin, record the omission. Queue-empty is an operational check, with an explicit requirement before moving the canonical checkout or restarting AppHost. [Scoped task read](../ops-http.md#L205) · [Land request fields](../ops-http.md#L713) · [Canonical checkout gates](../orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout)

Run in PowerShell 7 in a clean orchestrator-owned Windows audit checkout, with Antiphon as `origin`, a usable build-slot endpoint and desktop native work clear. Keep the variables for all three steps. Use a dedicated audit checkout distinct from the serving checkout and the qualification clone.

```powershell
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$projectId = 'd4ea7ae9-e769-474b-95b9-aa25fbc1303f'
if (-not $env:ANTIPHON_API) { throw 'Set ANTIPHON_API to the serving Antiphon API origin.' }
$api = $env:ANTIPHON_API.TrimEnd('/')
$tasks = Invoke-RestMethod "$api/api/agent-tasks?projectId=$projectId"
$tasks.items | Where-Object {
    $_.landRequestedAt -or $_.landStartedAt -or
    ($_.role -eq 'Merge' -and $_.status -in 'Queued','Dispatched','Working','Blocked')
} | Select-Object id,title,status,landRequestedAt,landStartedAt
$lane = Invoke-RestMethod "$api/api/agent-tasks/4714c580-cfae-476b-892f-cec540489e0e"
$lane.landRequest | Select-Object id,state,expectedSourceSha,remoteSourceSha,sourceRefusalReason

$rc = 'rc/2026-12-01'
$rcRef = "refs/heads/$rc"
$auditedMaster = '72a1da4428d8d0dc8b0ecd8ad533cbad75399288'
if (git status --porcelain) { throw 'Audit checkout is dirty.' }
git fetch --no-tags origin master
if ($LASTEXITCODE -ne 0) { throw 'Master fetch failed.' }
$sha = (git rev-parse --verify 'FETCH_HEAD^{commit}').Trim()
if ($LASTEXITCODE -ne 0 -or $sha -cne $auditedMaster) {
    throw 'Master moved. Review the new lands and deliberately replace the audited pin.'
}
git switch --detach $sha
if ($LASTEXITCODE -ne 0 -or (git status --porcelain)) { throw 'Audit checkout is not clean.' }
$auditRoot = (Get-Location).Path
git log -1 --format='%H %s'
git log --oneline "bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0..$sha"
git diff --name-only bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0 $sha -- Directory.Build.props Directory.Build.targets Antiphon.AppHost scripts
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-cut-master-graph -- scripts/audit-land-verification-graph.ps1 -Check
if ($LASTEXITCODE -ne 0) { throw 'Master graph audit did not pass. Record the diagnostic before deciding on the cut.' }
```

Review the log against intended lands' verified **target** SHAs, since land can rebase the source. Record the graph output and lease. The evaluator builds into its own isolated output if cold. Resolve graph refusals through the owning land path. The inherited-digest advisory for Merge/Review is distinct from a graph receipt for this cut. Master scheduled readiness is a separate receipt. The old rc-to-master diff includes `Directory.Build.props` and the desktop runner restart path. For startup-path picks, run the exact-candidate probe before the push and the restore rehearsal on the updated canonical checkout before restart. The function below runs each in a fresh native shell and removes the process-only opt-out before invoking the owner command. [Graph evaluator](../testing-and-build.md#L1402) · [Land-graph refresh](../orchestration-loop.md#land-graph-refresh) · [Startup probe and rehearsal](../release-gates.md#startup-path-rc-picks) · [Master readiness](../release-gates.md#two-lanes-two-kinds-of-credit)

```powershell
function Invoke-RcStartupProbe([string]$phase) {
    pwsh -NoProfile -Command {
        param($phase)
        $ErrorActionPreference = 'Stop'
        if (-not $IsWindows) { throw 'Native Windows required.' }
        foreach ($name in 'C589_SLOT_SHIM','C589_COMMAND_SHIM','ANTIPHON_APPHOST_TEST_SEAMS') {
            if ([Environment]::GetEnvironmentVariable($name, 'Process')) { throw "Test seam present $name" }
        }
        [Environment]::SetEnvironmentVariable('ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT', [NullString]::Value, 'Process')
        if ([Environment]::GetEnvironmentVariables('Process').Contains('ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT')) {
            throw 'Process opt-out is still present.'
        }
        $dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop
        if ($phase -eq 'rehearsal') {
            & ./scripts/build-slot.ps1 -Label rc-restart-rehearsal -- $dotnet.Source restore Antiphon.AppHost
        } else {
            & ./scripts/build-slot.ps1 -Label rc-native-probe -- $dotnet.Source --version
        }
        exit $LASTEXITCODE
    } -args $phase
    if ($LASTEXITCODE -ne 0) { throw "Startup check failed ($phase)." }
}
Invoke-RcStartupProbe probe
```

After reviewing the land state, source and probe receipt, serialize this cut with other rc writers. Re-read master and require exact absence of the intended rc ref (`ls-remote --exit-code` exit 2). The full-SHA refspec creates the branch remotely. Verify readback even if the push errors.

```powershell
$masterLine = git ls-remote --exit-code origin refs/heads/master
if ($LASTEXITCODE -ne 0 -or ($masterLine -split '\s+')[0] -cne $sha) {
    throw 'Remote master differs from the audited pin.'
}
git ls-remote --exit-code origin $rcRef
if ($LASTEXITCODE -ne 2) { throw 'RC ref is occupied or remote absence could not be verified.' }
git push --no-follow-tags origin "$($sha):$rcRef"
$pushExit = $LASTEXITCODE
$rcLine = git ls-remote --exit-code origin $rcRef
if ($LASTEXITCODE -ne 0 -or ($rcLine -split '\s+')[0] -cne $sha) {
    throw "RC publication unconfirmed (push exit $pushExit). Resolve the remote ref before proceeding."
}
$rcLine
```

## 2. Prepare for Full and finish planned activation before launch

### Desktop admission and disk measurement

Clear other Windows build/test work, including native Debug and scheduled nightly ownership. Keep new native work off the desktop during Full. Confirm actual host processes with their owners. The direct desktop catalogue showed zero occupied seats in this investigation, which covers only catalogue seats. Identify any existing lock by PID, process start, run and task identity. A stale shared lock requires the owner's evidence-preserving procedure. The native driver takes the shared verification lock and refuses a busy owner. [Native lane serialization](../release-gates.md#serialising-the-two-lanes) · [Inspecting qualification](../release-gates.md#inspecting-and-stopping-a-qualification)

The observed AppHost API reported `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` and `land-v2`. `server2` reported `493bee0ee3af2052575bc7a26e9a313669b2fc2d` and was accepting, with four sessions and four runner sessions. CARD-1744 was in Review and its owner does not record cutover. Recheck topology before activation. Observe the direct desktop runner at localhost `17204`. The phone-home status for `desktop` returned unavailable and `desktop-win` returned 404. [Direct runner ports](../apphost-runbook.md#L7) · [Runner status contracts](../ops-http.md#L169) · [Production topology](../docker-stack.md#production-server-on-the-server2-host-card-1744)

```powershell
if (-not $IsWindows) { throw 'Run desktop admission on native Windows PowerShell 7.' }
$releaseRoot = 'C:\Antiphon\releases'
$coordinationRoot = 'C:\Antiphon\verification'
$canonical = 'C:\src\Antiphon'
$oldLogDir = "$releaseRoot\candidates\rc-2026-11-30-bd34a7ff6da9\logs\2026-11-21-0735-3528e6b1"
$oldTimingRoot = "$releaseRoot\candidates\rc-2026-11-30-bd34a7ff6da9\logs\timing-3528e6b1"
docker info --format '{{.ServerVersion}}'
if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop is unavailable.' }
docker ps --format '{{.Names}} {{.Status}}'
docker exec antiphon-postgres pg_isready
if ($LASTEXITCODE -ne 0) { throw 'Check configured Postgres topology and readiness.' }
Get-PSDrive -Name C | Select-Object Name, @{n='FreeGiB';e={[math]::Round($_.Free / 1GB, 2)}}
foreach ($root in $oldLogDir,$oldTimingRoot) {
    $size = Get-ChildItem -LiteralPath $root -Recurse -Force -File -ErrorAction Stop | Measure-Object Length -Sum
    [pscustomobject]@{ Path = $root; Files = $size.Count; Bytes = $size.Sum; GiB = [math]::Round($size.Sum / 1GB, 2) }
}
Get-Item -LiteralPath "$coordinationRoot\native-run.lock",'C:\Antiphon\nightly\run.lock',"$releaseRoot\state-3528e6b1\run.lock" -ErrorAction SilentlyContinue |
    Select-Object FullName,LastWriteTimeUtc
Invoke-RestMethod 'http://localhost:17202/health'
Invoke-RestMethod 'http://localhost:17202/api/version'
(Invoke-RestMethod 'http://localhost:17204/capabilities').build.commitSha
Invoke-RestMethod 'http://localhost:17204/build-slots'
```

The Postgres command checks topology A's local container. If production Postgres is configured elsewhere, also verify the live database via the serving app's health path. Native tests use their own Docker/Testcontainers PostgreSQL. Allow free space beyond both measured evidence roots for the new clone, build outputs, test temp files and Docker data. The mirror cannot read these paths and the prior report records file counts without byte totals. Budget a 24-hour desktop window. The prior Full took **19 h 9 min 38 s**, including one hour-long chunk timeout. Use actual progress to forecast the new run. [Bootstrap](../bootstrap.md#machine-steps) · [Test Postgres](../testing-and-build.md#lazy-postgresql-and-restart-preflight-cache-card-0476) · [Prior Full receipt](2026-11-22-rc-run-3528e6b1-final-analysis.md#source-and-verdict)

### Activate the services in the current topology

Finish planned activation before Full, after native Windows work clears and canonical lands settle. A restart overlapped a C1514 host-load flake in run `06863906`. The seeded launcher uses its own clone, supervisor and isolated test children. It restarts no production service. Qualification can start while a planned activation is deferred, with restarts held until the run ends. [Host-load incident](../incident-runbook-server2-dispatch.md#cost-during-a-windows-qualification) · [Supervisor launch](../../.claude/skills/antiphon-rc-qualification/SKILL.md#supervisor-launch-set-card-1667) · [Activation check](../orchestration-loop.md#L406)

For topology A, record the old clean canonical HEAD and matching serving SHA, both AppHost locks, runner capabilities, Docker/Postgres health and CLI versions. Verify the fresh project land read and that no worktree is half-reset. Resolve a phone-home config or maintenance marker through its owner. Use the independent Windows shell from step 1, outside any agent Job Object. Advance the canonical checkout under the land exclusion, then run the exact-candidate probe and restore rehearsal. The prior activation rolled the server2 worker before restarting AppHost so the new ladder could route to a compatible CLI. Verify the candidate's CLI floor and both executable versions before activating. [Canonical restart and Job Object](../apphost-runbook.md#first-start-and-apphost-restart) · [Prior rollout order](2026-11-20-post-cut-activation-runbook.md#2-server2-runner-image-one-phase-at-a-time) · [CLI gate](../cli-upgrade.md)

```powershell
Set-Location $canonical
if (git status --porcelain) { throw 'Canonical checkout is dirty.' }
$landRows = (Invoke-RestMethod "$api/api/agent-tasks?projectId=$projectId").items
if ($landRows | Where-Object { $_.landRequestedAt -or $_.landStartedAt }) { throw 'A land is still outstanding.' }
$markers = Get-Item -LiteralPath 'logs\apphost.restart.lock','logs\apphost.launch.lock','logs\apphost.down-on-purpose','C:\Antiphon\runner\phone-home.json' -ErrorAction SilentlyContinue
if ($markers) { $markers | Select-Object FullName; throw 'Resolve the marker with its owner before activation.' }
$oldHead = (git rev-parse HEAD).Trim()
$oldVersion = (Invoke-RestMethod 'http://localhost:17202/api/version').version
if ($oldHead -cne $oldVersion) { throw 'Existing canonical and serving SHA disagree.' }
codex --version
git fetch --no-tags origin $rcRef
if ($LASTEXITCODE -ne 0 -or (git rev-parse FETCH_HEAD).Trim() -cne $sha) { throw 'RC pin moved.' }
git switch --detach $sha
if ($LASTEXITCODE -ne 0 -or (git rev-parse HEAD).Trim() -cne $sha -or (git status --porcelain)) {
    throw 'Canonical checkout does not match the clean rc pin.'
}
Invoke-RcStartupProbe probe
Invoke-RcStartupProbe rehearsal
```

Upgrade the standing server2 worker through the named phases below, **one command at a time** with the owner table's stop and smoke gates. Check jq and cache readiness first. Prove the standing container's ID and start stayed fixed during `deploy-temp`, smoke and canary temp before `drain-old`, require all three old counters at zero before `redeploy-old`, and finish the standing runner's exact-SHA readback, smoke, canary and `Both` before moving dispatch back. Maintain an accepting runner throughout. [Staged rollout commands and smoke gates](../docker-stack.md#staged-server2-rolling-rollout-card-0934) · [Continuous dispatch](../docker-stack.md#continuous-dispatch-during-the-rollout)

```powershell
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2-temp
Invoke-RestMethod "$api/api/runner-defaults"
bash scripts/c1893-runner-hold.sh $sha
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $sha -Phase check-census
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $sha -Phase check-host-jq
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha $sha
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Inventory -Sha $sha
ssh mc@server2 'docker inspect -f "{{.Id}} {{.State.StartedAt}}" antiphon-runner-session-runner-1'
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $sha -Phase deploy-temp
ssh mc@server2 'docker inspect -f "{{.Id}} {{.State.StartedAt}}" antiphon-runner-session-runner-1'
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2-temp
ssh mc@server2 'c=$(docker ps -q --filter label=com.docker.compose.project=antiphon-runner-temp --filter label=com.docker.compose.service=session-runner); test -n "$c" && docker exec -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex "$c" codex --version && docker exec "$c" pwsh --version && docker exec "$c" dotnet --version'
# Complete owner gate 2's pinned Plan canary before drain-old.
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $sha -Phase drain-old -WaitIdleMinutes 240
pwsh -NoProfile -File scripts/runner-slots.ps1 list -RunnerId server2
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2
# Require owner gate 5's known zero counts before redeploy-old.
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $sha -Phase redeploy-old
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2
ssh mc@server2 'c=$(docker ps -q --filter label=com.docker.compose.project=antiphon-runner --filter label=com.docker.compose.service=session-runner); test -n "$c" && docker exec -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex "$c" codex --version && docker exec "$c" pwsh --version && docker exec "$c" dotnet --version'
# Complete owner gate 7's pinned Plan canary before Both and drain-temp.
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both -Sha $sha
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $sha -Phase drain-temp -WaitIdleMinutes 240
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $sha -Phase retire-temp -DryRun
```

If the jq check returns `HostJqMissing`, use `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $sha -Phase provision-host-jq` from the clean canonical pin and repeat the check. For each smoke, use the owner's sanctioned Plan canary pinned to the runner just inspected. Check its completed report and effective runner before routing more work there. Use the owner cache recovery for an unready `Inventory`, and the gate 7 rollback if the standing smoke fails. [Server2 owner gates](../docker-stack.md#staged-server2-rolling-rollout-card-0934)

On a Ready preview, set `$operationId` from that preview and apply immediately. Missing trust registry requires the operator. Protected unpublished work or dirty worktrees are gate 9 stops. Normal rollout and retirement are orchestrator-owned. [Rollout autonomy](../orchestration-loop.md#orchestrator-operational-autonomy-restart-rollout) · [Retirement gate](../docker-stack.md#L130)

```powershell
$env:ANTIPHON_VOLUME_RETIREMENT_APPLY = "enabled-$operationId"
try {
    pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $sha -Phase retire-temp -RetirementProof $operationId
    if ($LASTEXITCODE -ne 0) { throw 'Retirement apply refused. Follow gate 9.' }
} finally { Remove-Item Env:ANTIPHON_VOLUME_RETIREMENT_APPLY }
pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Retired
```

From the independent owning Windows shell in `C:\src\Antiphon`, recheck both AppHost locks, exact HEAD and the absence of live native verification. For topology A require AppHost restart exit 0, health, exact SHA and `land-v2`. The observed pin contains deployed-only SHA stamping (`b2911c28665fd198f3fa67f74f03c4dfb33cdb5c`) and the runner hint (`906aaf0fcc2a9e756c0556ddb989c4b126d46ab2`). If this is the first desktop runner restart onto the change, use `-Hard` as a **separate planned activation** to replace the Scheduled Task supervisor. The qualification skill excludes `-Hard` inside its own workflow. Subsequent routine refreshes use the soft command in the same runbook. Use the operator's independent shell if the caller cannot establish Job Object independence or planned refresh authority. [AppHost restart](../apphost-runbook.md#first-start-and-apphost-restart) · [CARD-2031 runner procedure](../apphost-runbook.md#restart-only-the-runner) · [Qualification boundaries](../../.claude/skills/antiphon-rc-qualification/SKILL.md#L201)

```powershell
pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha $sha
if ($LASTEXITCODE -ne 0) { throw 'AppHost activation failed. Inspect the documented exit and locks.' }
Invoke-RestMethod 'http://localhost:17202/health'
$version = Invoke-RestMethod 'http://localhost:17202/api/version'
if ($version.version -cne $sha -or $version.capabilities -cnotcontains 'land-v2') {
    throw 'AppHost SHA/capability check failed.'
}
# First desktop runner refresh onto SHA stamping only.
pwsh -NoProfile -File scripts/restart-session-runner.ps1 -Hard
if ($LASTEXITCODE -ne 0) { throw 'Runner activation unconfirmed. Use the restart observation procedure.' }
pwsh -NoProfile -File scripts/check-daemon-build.ps1
if ($LASTEXITCODE -ne 0 -or (Invoke-RestMethod 'http://localhost:17204/capabilities').build.commitSha -cne $sha) {
    throw 'Desktop runner stamp does not equal the pin.'
}
```

After a recorded topology B cutover, keep desktop AppHost in maintenance. On the server2 host's clean pinned compose context, use `pwsh -NoProfile -File scripts/deploy-server2-server.ps1 -Execute -ExpectedServerSha $sha` for a new image, with `SOURCE_REVISION` in the existing managed env and context HEAD both equal to `$sha`. Use `pwsh -NoProfile -File scripts/restart-linux-server.ps1 -ExpectedServerSha $sha` to probe the configured production server and `pwsh -NoProfile -File scripts/restart-linux-server.ps1 -Execute -ExpectedServerSha $sha` for a separately authorized restart. Use the host's existing `ANTIPHON_LINUX_SERVER_CONFIG` or `-Config`. The gate refuses an image change, which needs the compose deployment above. Verify loopback `127.0.0.1:17400` and the serving API's exact SHA. The worker runner still has its own rolling phases. First cutover, DNS/Caddy, key custody and rollback belong to the operator window. [Linux restart gate](../docker-stack.md#linux-server-sha-restart-gate-card-1745) · [Production cutover](../docker-stack.md#production-server-on-the-server2-host-card-1744)

### Create and pin the private candidate

Run from the desktop audit checkout at the exact SHA. At observed master the candidate ID is `rc-2026-12-01-72a1da4428d8`. `release-candidate.ps1` creates the new clone, `.antiphon-release-owned` and `candidate.json` for the already-pushed long-lived ref. `-NoSync` also requires `.antiphon-nightly-owned`. Create it only in the fresh clone after checking release ownership, origin and detached HEAD. Git ignores both markers. [Candidate script](../../scripts/release-candidate.ps1#L100) · [Nightly ownership](../../scripts/lib/nightly-common.ps1#L207) · [Marker format](../../scripts/lib/nightly-run-impl.ps1#L482) · [Previous native receipt](https://github.com/michal-ciechan/Antiphon/blob/61f93c5d58b04df662310bd9a7b6fece15ef20e9/docs/investigations/2026-11-21-rc-2026-11-30-run-launch.md)

```powershell
Set-Location $auditRoot
if ((git rev-parse HEAD).Trim() -cne $sha -or (git status --porcelain)) {
    throw 'Run candidate preparation from a clean checkout of the audited SHA.'
}
$pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$repoUrl = 'https://github.com/michal-ciechan/Antiphon'
$runId = [guid]::NewGuid().ToString('N').Substring(0, 8)
$sha12 = $sha.Substring(0,12)
$candidateId = "rc-$($rc.Substring(3))-$sha12"
$checkout = Join-Path $releaseRoot "checkout-$runId"
$taskRoot = Join-Path $releaseRoot "task-$runId"
$stateRoot = Join-Path $releaseRoot "state-$runId"
$candidateRoot = Join-Path $releaseRoot "candidates\$candidateId"
$logRoot = Join-Path $candidateRoot 'logs'
$timingRoot = Join-Path $logRoot "timing-$runId"
if (Test-Path -LiteralPath $checkout,$taskRoot,$stateRoot,$candidateRoot | Where-Object { $_ }) {
    throw 'Run or candidate path exists. Inspect its recorded identity before proceeding.'
}
$remote = git ls-remote --exit-code origin $rcRef
if ($LASTEXITCODE -ne 0 -or ($remote -split '\s+')[0] -cne $sha) { throw 'Unexpected rc tip.' }
& $pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-candidate-journal -- $pwsh -NoProfile -File scripts/release-candidate.ps1 -Ref $rc -RemoteUrl $repoUrl -ReleaseRoot $releaseRoot -CheckoutRoot $checkout
if ($LASTEXITCODE -ne 0) { throw 'Candidate journaling failed.' }
$journal = Get-Content -LiteralPath "$candidateRoot\candidate.json" -Raw | ConvertFrom-Json
if ($journal.sha -cne $sha -or $journal.ref -cne $rc -or $journal.candidateId -cne $candidateId) {
    throw 'Candidate journal differs from the rc pin.'
}
if (-not (Test-Path -LiteralPath "$checkout\.antiphon-release-owned" -PathType Leaf) -or
    (git -C $checkout config --local --get remote.origin.url) -cne $repoUrl) {
    throw 'New clone ownership or origin check failed.'
}
git -C $checkout config --local core.longpaths true
if ($LASTEXITCODE -ne 0) { throw 'Could not enable long Git paths.' }
git -C $checkout fetch --no-tags origin $rcRef
if ($LASTEXITCODE -ne 0 -or (git -C $checkout rev-parse FETCH_HEAD).Trim() -cne $sha) {
    throw 'Candidate ref moved during preparation.'
}
git -C $checkout switch --detach $sha
if ($LASTEXITCODE -ne 0 -or (git -C $checkout rev-parse HEAD).Trim() -cne $sha -or
    (git -C $checkout status --porcelain)) { throw 'Candidate checkout pin or cleanliness check failed.' }
[ordered]@{ origin = $repoUrl; kind = 'nightly-clone' } | ConvertTo-Json -Compress |
    Set-Content -LiteralPath "$checkout\.antiphon-nightly-owned" -Encoding utf8NoBOM
```

## 3. Launch seeded Windows Full and arm monitors

On the native desktop recheck idle status, the shared lock and previous owners, Docker/Postgres, the direct runner build-slot endpoint, and the journal, checkout and remote pin. Cold-reset workspace slots for all runners before the Full. Leased slots reset on release. Keep the request IDs with the run. [Pre-run slot reset](../release-gates.md#workspace-slots-before-an-rc-run-card-0824-s1)

The seed maps 78 failed cases to 59 method identities from `3528e6b1`, with interrupted g47 also recorded. Both seed timestamps must be within 14 days, allowing up to five minutes future skew. Mapping uses the new Full roster. The seed prioritizes work and the new Full earns coverage through execution. Use one build-slot lease, the shared native-run lock and serial Windows chunks. Keep the default `-CoordinationRoot` and complete suite roster. `LogRoot` is an input, and `LogDir` is emitted when tests start. [Prior exact seed recipe](2026-11-22-rc-run-3528e6b1-final-analysis.md#failed-first-seed-and-next-launch) · [Seed intake](../../scripts/lib/Antiphon.Nightly/PriorFailureHistory.cs#L743) · [Ordering](../testing-and-build.md#nightly) · [Launch wrapper](../../scripts/qualification/launch-rc.ps1#L36)

```powershell
Set-Location $checkout
$seedPath = "$releaseRoot\state-3528e6b1\seed-3528e6b1-61cfe0c5.json"
$seedDigest = 'a46d1a1522ae84758368735a442d69ba7ef1a0ac2676b28f2393c453bc37a549'
$seedSource = 'rc-2026-11-30-bd34a7ff6da9'
if ((Get-FileHash -LiteralPath $seedPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $seedDigest) {
    throw 'Failed-first seed hash differs from the reviewed file.'
}
$seed = Get-Content -LiteralPath $seedPath -Raw | ConvertFrom-Json
if ($seed.sourceCandidateId -cne $seedSource -or $seed.sourceRunId -cne '3528e6b1') {
    throw 'Seed source identity mismatch.'
}
$seedPayload = $seed.payload | ConvertFrom-Json
$now = [datetimeoffset]::UtcNow
foreach ($stamp in $seed.createdAtUtc,$seedPayload.sourceObservedAtUtc) {
    if (-not $stamp) { throw 'Seed timestamp missing.' }
    $when = [datetimeoffset]$stamp
    if ($when -lt $now.AddDays(-14) -or $when -gt $now.AddMinutes(5)) { throw 'Seed timestamp out of window.' }
}
$journal = Get-Content -LiteralPath "$candidateRoot\candidate.json" -Raw | ConvertFrom-Json
$remote = git ls-remote --exit-code origin $rcRef
if ($LASTEXITCODE -ne 0 -or ($remote -split '\s+')[0] -cne $sha -or
    $journal.sha -cne $sha -or $journal.candidateId -cne $candidateId -or
    (git rev-parse HEAD).Trim() -cne $sha -or (git status --porcelain)) { throw 'Launch pin or clean checkout check failed.' }
if ((Test-Path -LiteralPath "$coordinationRoot\native-run.lock") -or
    (Test-Path -LiteralPath 'C:\Antiphon\nightly\run.lock') -or
    (Test-Path -LiteralPath "$releaseRoot\state-3528e6b1\run.lock") -or
    (Test-Path -LiteralPath "$stateRoot\run.lock")) { throw 'Inspect the existing lock and owner before launching.' }

& $pwsh -NoProfile -File scripts/workspace-slots.ps1 cold-reset -AllRunners -Reason "rc qualification $rc $sha12"
if ($LASTEXITCODE -ne 0) { throw 'Workspace cold-reset refused.' }

$null = New-Item -ItemType Directory -Path $taskRoot -ErrorAction Stop
Copy-Item -LiteralPath (Get-ChildItem -LiteralPath "$checkout\scripts\qualification" -Filter '*.ps1' -File).FullName -Destination $taskRoot
$record = [ordered]@{
    checkoutRoot = $checkout
    expectedSha = $sha
    buildSlotLabel = "rc-$sha12-windows-qualification"
    nightlyArguments = @(
        '-NoSync', '-Profile', 'rc', '-Trigger', 'rc', '-SelectionMode', 'Full',
        '-Ref', $rc, '-ExpectedSha', $sha, '-CandidateId', $candidateId,
        '-CheckoutRoot', $checkout, '-StateRoot', $stateRoot, '-LogRoot', $logRoot,
        '-ReleaseRoot', $releaseRoot, '-RunId', $runId,
        '-PriorFailuresPath', $seedPath, '-PriorFailuresDigest', $seedDigest,
        '-AcceptCrossCandidateSeed', '-SeedSourceCandidateId', $seedSource
    )
}
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$taskRoot\launch-record.json" -Encoding utf8NoBOM
& $pwsh -NoProfile -File "$taskRoot\preflight.ps1" -CheckoutRoot $checkout
if ($LASTEXITCODE -ne 0) { throw 'Qualification preflight refused the candidate.' }

$starter = ('"{0}" -NoProfile -File "{1}\start-supervisor.ps1" -CheckoutRoot "{2}" -TaskRoot "{1}" -LaunchScript "{1}\launch-rc.ps1" -LogRoot "{3}" -StateRoot "{4}" -RunId "{5}"' -f $pwsh, $taskRoot, $checkout, $logRoot, $stateRoot, $runId)
$start = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $starter }
if ($start.ReturnValue -ne 0) { throw "Win32_Process Create failed ($($start.ReturnValue))." }
[pscustomobject]@{ RunId = $runId; StarterPid = $start.ProcessId; CandidateId = $candidateId; Sha = $sha; TaskRoot = $taskRoot; StateRoot = $stateRoot; LogRoot = $logRoot; TimingRoot = $timingRoot }
```

The CIM return proves starter creation. Look for `start-supervisor.json`, `supervisor-identity.json` and `launch-child.json` under `$taskRoot`. Logs there include `supervisor.log` and `commission-console.log`. The heartbeat is `$taskRoot\supervisor-heartbeat.json`. The driver writes `$stateRoot\last-run.json` and the supervisor retains `$taskRoot\own-last-run.json`. Find the exact `logDir` in matching run state. Its pattern is `$logRoot\yyyy-MM-dd-HHmm-$runId` (UTC), and timing is under `$logRoot\timing-$runId`. CARD-2044 fixed explicit `-ReleaseRoot` refusal and CARD-2045 fixed library resolution from the copied launch set in `adce1900111e85c0e437e2caf7c2f98b8c9a203f`, present on master. [Launch set](../../.claude/skills/antiphon-rc-qualification/SKILL.md#supervisor-launch-set-card-1667) · [Run directory writer](../../scripts/lib/nightly-run-impl.ps1#L662) · [UTC format](../../scripts/lib/Antiphon.Nightly/NightlyRunParts.cs#L137) · [Timing root](../testing-and-build.md#L2059)

```powershell
& $pwsh -NoProfile -File "$taskRoot\observe.ps1" -CheckoutRoot $checkout -TaskRoot $taskRoot -StateRoot $stateRoot
& $pwsh -NoProfile -File "$checkout\scripts\rc-qualification-status.ps1" -CandidateId $candidateId -RunId $runId -StateRoot $stateRoot -CoordinationRoot $coordinationRoot -ReleaseRoot $releaseRoot

$run = Get-Content -LiteralPath "$stateRoot\last-run.json" -Raw | ConvertFrom-Json
if ($run.runId -cne $runId -or $run.sha -cne $sha -or -not $run.logDir) {
    throw 'Wait for matching run state with a recorded log directory.'
}
$logDir = $run.logDir
$heartbeat = Join-Path $taskRoot 'supervisor-heartbeat.json'
```

Use an orchestrator-owned scratch directory outside the run, such as the `$watchRoot` shown below. Pass literal paths and IDs for monitors in another shell. Start the three watcher commands in separate bounded Monitor tasks (1,800,000 ms, rearm about every 30 minutes). The TRX watcher runs on the desktop against the recorded `$logDir`. The task and master watchers can run from any suitable checkout with API or Git access. [Watch policy](../../.claude/skills/antiphon-rc-qualification/SKILL.md#L50)

```powershell
$watchRoot = Join-Path $env:LOCALAPPDATA "Antiphon\qualification-monitors\$runId"
$null = New-Item -ItemType Directory -Path $watchRoot -Force
& $pwsh -NoProfile -File "$checkout\scripts\orchestration\watch-qual.ps1" -LogDir $logDir -StateFile "$watchRoot\qual-$runId.seen.txt" -Heartbeat $heartbeat -TimingRoot $timingRoot
& $pwsh -NoProfile -File "$checkout\scripts\orchestration\watch-tasks.ps1" -ProjectId $projectId -StateFile "$watchRoot\tasks.json"
& $pwsh -NoProfile -File "$checkout\scripts\orchestration\watch-master.ps1" -From $sha -Repository $checkout
```

Read `dispatch-schedule.json` under `$logDir` once available. Require matching `runId` and `historyDigest`, `preparationComplete=true` and operator-seed ordering. Review the unresolved entries and the leading failed-method groups. A refused seed can fall back to ordinary ordering while tests continue. A stale heartbeat calls for observation. Live-run stop is human-only. A matching `completedAt` in current or retained state is the completion signal. [Observer outcomes](../../.claude/skills/antiphon-rc-qualification/SKILL.md#L283) · [Schedule fields](../../scripts/lib/Antiphon.Nightly/NightlyDispatch.cs#L58)

## Open risks at the observed master

| Risk | Evidence and next observation |
| --- | --- |
| `4714c580` post-land lane lock | The reviewed-source mismatch left the repair outside master. The branch investigation reproduced 8 failures in 10 Linux attempts and separately observed dual acquisition. The Full includes the test family. The Windows rate is unmeasured. Confirm the land receipt and target ancestry before cutting. [Lane-lock reproduction](https://github.com/michal-ciechan/Antiphon/blob/66ff06eff83edf4542061f5c9f3f3b3c045ebaf1/docs/investigations/2026-11-22-why-missed-post-land-lock-test-flake.md) |
| CARD-2053, g47 LandingGit pipe drain | The chunk timed out after 3,601.261 s with 37 planned cases missing. Card row `2ee29f5e-e90f-4e19-a72e-131c79401052` attributes it to redirected streams held by leftover Windows conhost after Git exit. Master contains test containment `50f6f5506584a20c29dfb6975c45763237c9e627` and witness `cbab2558abe1cccd228b8db00503ed99b7ff8781`. Product tip `8fa8148983c5d5513624081a17e227976d19891b` is outside master and blocked after round 3. Capture all three classes' TRX and any pipe/owner diagnostics. [g47 receipt](2026-11-22-rc-run-3528e6b1-final-analysis.md#L158) |
| CARD-2054 checkpoint cache | Card row `2aca3f30-ddc6-4977-aca5-e99596b2b12d` is parked. Its tip `edeb9dd4995a16d490f940a03aaf720fc93f3be4` is outside master. The row records seal-dispose and pre-slot invalidation blockers plus outstanding CP-7 through CP-9. This is an unlanded performance change. The previous Full has no failed case attributed to its absence. |
| Profile 404 CARD-0824 retirement | Historical `Double_run_is_idempotent(concurrent)` left a checkout present. The `e6f4c3dd` branch contains the blocked remedy lineage and evidence. The old TRX omits the retirement verdict. Capture both arms and the native retirement outcome if it repeats. [Original investigation](https://github.com/michal-ciechan/Antiphon/blob/1a48bee0aa136a051043dfad533603cd1d9a1e4f/docs/investigations/2026-11-21-why-missed-rc-run-3528e6b1-profile-404.md) |
| g94 C1960 | The fixture saw zero quarantine rows before calling the route. Six native passes on unchanged master and 14 green cases on the g92 repair build leave its original trigger unconfirmed. Capture pre-route release response and ledger on repeat. g92's same-handle exit repair is in master at `21115af40541210b0704a622b2315ecc3402c19b`. [Native slot report](2026-11-22-slot-pool-windows-g92-g94.md#evidence) · [g94 fixture](../../tests/Antiphon.Tests/Application/C1960QuarantineReleaseRouteTests.cs#L192) |
| g71 C475 | Three native passes reached the queue commit, injected forwarding exception and single prompt receipt. The historical failure masked the pre-write state. Capture `WriteReached`, PTY and queue attempt/verdict evidence on repeat. [Native g71 recheck](2026-11-22-windows-g71-g137-native-recheck.md) · [Fixture](../../tests/Antiphon.Tests/Application/SessionQueueReceiptPlumbingTests.cs#L32) |
| g137 `LaunchStampIsCheaper` | Historical full/stamp was 207.937/267.306 ms. Three native replays passed with stamps 14.142–21.850 ms. The strict timing assertion remains. Capture both costs and host-load phases on recurrence. [Native timing recheck](2026-11-22-windows-g71-g137-native-recheck.md) · [Stamp assertion](../../tests/Antiphon.Tests/Scripts/RcRunEvidenceTests.cs#L744) |
| Remaining replay debt | g61 passed three native repeats per case with the alias fix. g39 passed 3/3 after fixture changes now in master (`bc8dc6230`, `aadb6f77b`). Its original recovery scan remains unobserved. The three historical POSIX-only skips have Windows-roster updates in master (`2a02fc5a9`, `1a1ed969c`). The landed repairs still need the exact new-SHA Full receipt. [g39/g61 recheck](2026-11-22-windows-g39-g61-native-recheck.md) · [Prior coverage gaps and families](2026-11-22-rc-run-3528e6b1-final-analysis.md#failure-dispositions) |

## Not done, noted

This investigation used repository sources, remote refs, public card rows and read-only operational status. Graph execution, Windows disk measurement and launch checks belong to the execution window. This note is the sole deliverable.
