# CARD-0604 local harness (evidence tooling, not product). Docker Desktop only.
# Boots the privileged session-testing image with its nested dockerd and grades D-1, D-3, D-4,
# D-5 and D-8's plumbing without server2, GitHub or the production server. Steps 1-11 are Cut A;
# -Containment adds Cut B's steps 12-15. Never binds 17202-17205, never touches a server2
# resource, and names only its own container and volume.
#
# Exit codes: 0 every graded observation is ok, 1 one or more are not, 2 setup failed (refused
# port, no Docker, failed build, unhealthy container).
param(
    [Parameter(Mandatory = $true)][string] $Image,
    [switch] $Build,
    [switch] $Containment,
    [int] $Port = 18298,
    [string] $EvidenceRoot = '.antiphon/c604-harness',
    [string] $Checkout = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$forbiddenPorts = 17202, 17203, 17204, 17205
$repoRoot = Split-Path -Parent $PSScriptRoot
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$containerName = "c604-$stamp"
$volumeName = "c604-$stamp-dind"

# Graded observations. 'unknown' survives a partial run so the result line stays legible.
$nested = 'unknown'
$egress = 'unknown'
$loopback = 'unknown'
$launchMs = -1
$keyState = 'unknown'
$restartState = 'unknown'
$refusalState = 'unknown'
$claudeVersion = 'unknown'
$claudeAuth = 'unknown'
$containmentState = if ($Containment) { 'unknown' } else { 'skipped' }

function Write-Evidence([string] $name, [string] $text) {
    Set-Content -LiteralPath (Join-Path $script:evidenceDir $name) -Value $text -Encoding ascii
}

function Invoke-Docker([string[]] $dockerArgs, [string] $evidenceName) {
    $output = & docker @dockerArgs 2>&1 | Out-String
    $code = $LASTEXITCODE
    if ($evidenceName) {
        Write-Evidence $evidenceName ("docker " + ($dockerArgs -join ' ') + "`n--- exit $code ---`n" + $output)
    }
    return [pscustomobject]@{ ExitCode = $code; Output = $output }
}

function Exit-Harness([int] $code, [string] $reason) {
    if ($reason) { Write-Host "C604 HARNESS NOTE: $reason" }
    Write-Host ("C604 HARNESS: image={0} nested={1} egress={2} loopback={3} launchMs={4} key={5} restart={6} refusal={7} containment={8} claude={9} claudeAuth={10}" -f `
            $Image, $nested, $egress, $loopback, $launchMs, $keyState, $restartState, $refusalState, $containmentState, $claudeVersion, $claudeAuth)
    Write-Host "C604 HARNESS EXIT CODE: $code"
    exit $code
}

if ($Port -in $forbiddenPorts) {
    Write-Host "C604 HARNESS NOTE: refusing production port $Port (17202-17205 are the local stack)."
    Write-Host "C604 HARNESS EXIT CODE: 2"
    exit 2
}

if (-not $EvidenceRoot) { $EvidenceRoot = '.antiphon/c604-harness' }
$evidenceRootPath = if ([System.IO.Path]::IsPathRooted($EvidenceRoot)) { $EvidenceRoot } else { Join-Path $repoRoot $EvidenceRoot }
$script:evidenceDir = Join-Path $evidenceRootPath $stamp
New-Item -ItemType Directory -Force -Path $script:evidenceDir | Out-Null

$dockerVersion = Invoke-Docker @('version', '--format', '{{.Server.Version}}') 'docker-version.txt'
if ($dockerVersion.ExitCode -ne 0) {
    Exit-Harness 2 'Docker daemon is not reachable. Start Docker Desktop and retry.'
}

# --- step 1: optional build -----------------------------------------------------------------
if ($Build) {
    Push-Location $repoRoot
    try { $headSha = (& git rev-parse HEAD).Trim() } finally { Pop-Location }
    Write-Host "C604 HARNESS: building $Image (session-testing) from $headSha"
    Push-Location $repoRoot
    try {
        $buildResult = Invoke-Docker @(
            'build',
            '-f', 'docker/session-runner-grok/Dockerfile',
            '--target', 'session-testing',
            '--build-arg', "SOURCE_REVISION=$headSha",
            '-t', $Image,
            $repoRoot) 'docker-build.txt'
    }
    finally { Pop-Location }
    if ($buildResult.ExitCode -ne 0) {
        Exit-Harness 2 "docker build failed; see $script:evidenceDir/docker-build.txt"
    }
}

# --- step 2: throwaway credentials, registered nowhere ---------------------------------------
$keyPath = Join-Path $script:evidenceDir 'throwaway_key'
$secretPath = Join-Path $script:evidenceDir 'throwaway_secret'
& ssh-keygen -t ed25519 -N '""' -C 'c604-harness-throwaway' -f $keyPath 2>&1 | Out-String | Out-Null
if (-not (Test-Path -LiteralPath $keyPath)) {
    Exit-Harness 2 'ssh-keygen did not produce a throwaway key.'
}
$randomSecret = -join ((1..32) | ForEach-Object { '{0:x2}' -f (Get-Random -Minimum 0 -Maximum 256) })
Set-Content -LiteralPath $secretPath -Value $randomSecret -Encoding ascii -NoNewline
$volume = Invoke-Docker @('volume', 'create', $volumeName) 'volume-create.txt'
if ($volume.ExitCode -ne 0) {
    Exit-Harness 2 'docker volume create failed.'
}

$started = $false
try {
    # --- step 3: the privileged container ----------------------------------------------------
    $runArgs = @(
        'run', '-d',
        '--privileged', '--init',
        '--restart', 'unless-stopped',
        '--name', $containerName,
        '-p', "${Port}:8080",
        '-v', "${volumeName}:/var/lib/docker",
        '--tmpfs', '/run/antiphon',
        '-v', "${keyPath}:/run/secrets/antiphon-deploy-key:ro",
        '-v', "${secretPath}:/run/secrets/phone-home:ro",
        '-e', 'ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key',
        '-e', 'PhoneHome__SecretPath=/run/secrets/phone-home',
        '-e', 'ANTIPHON_DOCKERD_LOG_DIR=/tmp/state/logs',
        '-e', 'SessionRunner__SessionLogPath=/tmp/state/session-runner',
        '-e', 'SessionRunner__PtyHostDir=/tmp/antiphon-pty-hosts',
        '-e', 'Serilog__LogPath=/tmp/state/runner-logs',
        '-e', 'CLAUDE_CONFIG_DIR=/tmp/state/claude',
        '-e', 'PhoneHome__ClaudeHome=/tmp/state/claude',
        '-e', 'PhoneHome__Enabled=false',
        '-e', 'SessionRunner__Herdr__Enabled=false',
        '-e', 'TMPDIR=/tmp',
        $Image)
    $run = Invoke-Docker $runArgs 'docker-run.txt'
    if ($run.ExitCode -ne 0) {
        Exit-Harness 2 "docker run failed; see $script:evidenceDir/docker-run.txt"
    }
    $started = $true

    $healthDeadline = (Get-Date).AddSeconds(90)
    $healthy = $false
    while ((Get-Date) -lt $healthDeadline) {
        try {
            $health = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 5 -SkipHttpErrorCheck
            if ([int]$health.StatusCode -ge 200 -and [int]$health.StatusCode -lt 300) { $healthy = $true; break }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $healthy) {
        Invoke-Docker @('logs', $containerName) 'container-logs-unhealthy.txt' | Out-Null
        Exit-Harness 2 "container never became healthy within 90s; see $script:evidenceDir/container-logs-unhealthy.txt"
    }

    # --- step 4: the daemon is this container's own, not a sibling ---------------------------
    $info = Invoke-Docker @('exec', $containerName, 'docker', 'info', '--format', '{{.Name}} {{.Driver}} {{.CgroupVersion}}') 'nested-info.txt'
    $hostname = Invoke-Docker @('exec', $containerName, 'hostname') 'nested-hostname.txt'
    $daemonName = ($info.Output -split '\s+')[0]
    $ownHostname = $hostname.Output.Trim()
    $nested = if ($info.ExitCode -eq 0 -and $daemonName -and $daemonName -eq $ownHostname) { 'yes' } else { 'no' }

    # --- step 5: nested pull, NAT and TLS egress ---------------------------------------------
    $zen = Invoke-Docker @('exec', $containerName, 'docker', 'run', '--rm', 'alpine:3.20', 'wget', '-qO-', 'https://api.github.com/zen') 'nested-egress.txt'
    $egress = if ($zen.ExitCode -eq 0 -and $zen.Output.Trim()) { 'yes' } else { 'no' }

    # --- step 6: a nested mapped port answers on the runner's own loopback, as the app uid ----
    # `docker exec -u 1654:1654` grants NO supplementary groups, so it cannot reach the nested
    # socket (group docker-nested, 1656) the way a real session can - the entrypoint gives the
    # runner that group with `setpriv --groups=1656` and every session inherits it. Running the
    # probe as 1654:1656 is the closest credentials `docker exec` can express.
    $loopScript = 'set -e; ' +
    'id=$(docker run -d -p 127.0.0.1::80 nginx:alpine); ' +
    'p=$(docker port $id 80 | head -n1 | sed "s/.*://"); ' +
    'sleep 2; ' +
    'curl -fsS -o /dev/null -w %{http_code} http://127.0.0.1:$p/; ' +
    'echo; docker rm -f "$id" >/dev/null'
    $loop = Invoke-Docker @('exec', '-u', '1654:1656', $containerName, 'sh', '-c', $loopScript) 'nested-loopback.txt'
    $loopMatch = [regex]::Match($loop.Output, '(?m)^(\d{3})\s*$')
    $loopback = if ($loopMatch.Success) { $loopMatch.Groups[1].Value } else { 'no' }

    # --- step 7: a real session launch through the runner's own POST /sessions ---------------
    $body = @{
        sessionId = ([Guid]::NewGuid()).ToString()
        exe       = '/bin/bash'
        args      = @()
        env       = @{}
        cwd       = '/tmp'
        cols      = 120
        rows      = 30
    } | ConvertTo-Json -Compress
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/sessions" -Method Post -Body $body `
            -ContentType 'application/json' -TimeoutSec 180 -SkipHttpErrorCheck
        $sw.Stop()
        Write-Evidence 'post-sessions.txt' ("status=$([int]$response.StatusCode) elapsedMs=$($sw.ElapsedMilliseconds)`n" + $response.Content)
    }
    catch {
        $sw.Stop()
        Write-Evidence 'post-sessions.txt' ("elapsedMs=$($sw.ElapsedMilliseconds)`n" + $_.Exception.Message)
    }
    $launchMs = [int]$sw.ElapsedMilliseconds

    # --- step 7b: CARD-0628 - the pinned claude runs as the app uid against a fresh store -------
    # No credential of any kind is passed: the harness proves the binary and the signed-out
    # verdict only. `docker exec` does not see the entrypoint's exports, so HOME and the store are
    # named explicitly, exactly as the operator's provisioning commands do.
    Invoke-Docker @('exec', $containerName, 'sh', '-c', 'mkdir -p /tmp/state/claude && chown 1654:1654 /tmp/state/claude') 'claude-store-create.txt' | Out-Null
    $claudeEnv = @('-u', '1654:1654', '-e', 'HOME=/home/app', '-e', 'CLAUDE_CONFIG_DIR=/tmp/state/claude', '-e', 'DISABLE_AUTOUPDATER=1')
    $claudeVer = Invoke-Docker (@('exec') + $claudeEnv + @($containerName, 'claude', '--version')) 'claude-version.txt'
    $claudeVerMatch = [regex]::Match($claudeVer.Output, '(\d+\.\d+\.\d+)')
    $claudeVersion = if ($claudeVer.ExitCode -eq 0 -and $claudeVerMatch.Success) { $claudeVerMatch.Groups[1].Value } else { 'no' }
    $claudeStatus = Invoke-Docker (@('exec') + $claudeEnv + @($containerName, 'claude', 'auth', 'status', '--json')) 'claude-auth-status.txt'
    $claudeAuth = if ($claudeStatus.ExitCode -eq 1 -and $claudeStatus.Output -match '"loggedIn"\s*:\s*false') { 'logged-out' } else { 'unexpected' }

    # --- step 8: the deploy key is materialised for the app uid, and ssh resolves the config --
    $stat = Invoke-Docker @('exec', '-u', '1654:1654', $containerName, 'stat', '-c', '%u %a', '/run/antiphon/deploy-key') 'deploy-key-stat.txt'
    $sshG = Invoke-Docker @('exec', '-u', '1654:1654', $containerName, 'ssh', '-F', '/etc/antiphon/ssh_config', '-G', 'github.com') 'ssh-config-resolved.txt'
    $keyOk = $stat.ExitCode -eq 0 -and $stat.Output.Trim() -eq '1654 400'
    $sshOk = $sshG.ExitCode -eq 0 `
        -and $sshG.Output -match '(?m)^hostname ssh\.github\.com' `
        -and $sshG.Output -match '(?m)^port 443' `
        -and $sshG.Output -match '(?m)^identitiesonly yes' `
        -and $sshG.Output -match '(?im)^identityfile /run/antiphon/deploy-key'
    $keyState = if ($keyOk -and $sshOk) { 'ok' } else { 'no' }

    # --- step 9: a dead daemon takes the container down; the policy brings it back ------------
    # The nested store is a named volume, so the images pulled above must still be there
    # afterwards; that is what makes the restart cheap instead of a full re-pull every time.
    $imagesBefore = Invoke-Docker @('exec', $containerName, 'docker', 'images', '-q') 'nested-images-before-restart.txt'
    $imagesBeforeSet = ($imagesBefore.Output -split '\r?\n' | Where-Object { $_.Trim() } | Sort-Object) -join ','
    $before = Invoke-Docker @('inspect', '-f', '{{.RestartCount}}', $containerName) 'restart-count-before.txt'
    $beforeCount = 0
    [int]::TryParse($before.Output.Trim(), [ref] $beforeCount) | Out-Null
    Invoke-Docker @('exec', $containerName, 'sh', '-c', 'kill -TERM $(pidof dockerd)') 'kill-dockerd.txt' | Out-Null
    $restartDeadline = (Get-Date).AddSeconds(90)
    $restarted = $false
    while ((Get-Date) -lt $restartDeadline) {
        $now = Invoke-Docker @('inspect', '-f', '{{.RestartCount}}', $containerName) ''
        $nowCount = 0
        [int]::TryParse($now.Output.Trim(), [ref] $nowCount) | Out-Null
        if ($nowCount -gt $beforeCount) { $restarted = $true; break }
        Start-Sleep -Seconds 2
    }
    $backHealthy = $false
    if ($restarted) {
        $backDeadline = (Get-Date).AddSeconds(90)
        while ((Get-Date) -lt $backDeadline) {
            try {
                $health = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 5 -SkipHttpErrorCheck
                if ([int]$health.StatusCode -ge 200 -and [int]$health.StatusCode -lt 300) { $backHealthy = $true; break }
            }
            catch { }
            Start-Sleep -Seconds 2
        }
    }
    $retained = Invoke-Docker @('exec', $containerName, 'docker', 'images', '-q') 'nested-images-after-restart.txt'
    $retainedSet = ($retained.Output -split '\r?\n' | Where-Object { $_.Trim() } | Sort-Object) -join ','
    $imagesRetained = $imagesBeforeSet.Length -gt 0 -and $retainedSet -eq $imagesBeforeSet
    $restartState = if ($restarted -and $backHealthy -and $imagesRetained) { 'ok' } else { 'no' }
    Write-Evidence 'restart-summary.txt' ("restarted=$restarted backHealthy=$backHealthy imagesRetained=$imagesRetained`n" +
        "before=$imagesBeforeSet`nafter=$retainedSet")

    # --- steps 12-15: Cut B containment (only with -Containment) -----------------------------
    if ($Containment) {
        $containmentState = 'unsupported'
        Write-Evidence 'containment.txt' 'Cut B (D-17) is not implemented in this cut; -Containment is a no-op here.'
    }
}
finally {
    # --- step 10: the three refusals, each a fresh throwaway container -----------------------
    $noKey = Invoke-Docker @('run', '--rm', '--privileged', '--init',
        '-e', 'ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key',
        '-e', 'PhoneHome__SecretPath=/run/secrets/phone-home',
        $Image) 'refusal-no-key.txt'
    $noSecret = Invoke-Docker @('run', '--rm', '--privileged', '--init',
        '-v', "${keyPath}:/run/secrets/antiphon-deploy-key:ro",
        '-e', 'ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key',
        '-e', 'PhoneHome__SecretPath=/run/secrets/phone-home',
        $Image) 'refusal-no-secret.txt'
    $notPrivileged = Invoke-Docker @('run', '--rm', '--init',
        '-v', "${keyPath}:/run/secrets/antiphon-deploy-key:ro",
        '-v', "${secretPath}:/run/secrets/phone-home:ro",
        '-e', 'ANTIPHON_DEPLOY_KEY_SOURCE=/run/secrets/antiphon-deploy-key',
        '-e', 'PhoneHome__SecretPath=/run/secrets/phone-home',
        $Image) 'refusal-not-privileged.txt'
    $refusalOk = $noKey.ExitCode -eq 3 -and $noKey.Output -match 'DeployKeyMissing' `
        -and $noSecret.ExitCode -eq 3 -and $noSecret.Output -match 'PhoneHomeSecretMissing' `
        -and $notPrivileged.ExitCode -eq 3 -and $notPrivileged.Output -match 'NotPrivileged'
    $refusalState = if ($refusalOk) { 'ok' } else { 'no' }

    # --- step 11: this run's own container and volume, and nothing else ----------------------
    if ($started) {
        Invoke-Docker @('logs', '--tail', '400', $containerName) 'container-logs.txt' | Out-Null
        Invoke-Docker @('rm', '-f', $containerName) 'docker-rm.txt' | Out-Null
    }
    Invoke-Docker @('volume', 'rm', '-f', $volumeName) 'volume-rm.txt' | Out-Null
    foreach ($throwaway in @($keyPath, ($keyPath + '.pub'), $secretPath)) {
        if (Test-Path -LiteralPath $throwaway) { Remove-Item -LiteralPath $throwaway -Force }
    }
}

$graded = @{
    nested   = $nested -eq 'yes'
    egress   = $egress -eq 'yes'
    loopback = $loopback -eq '200'
    launch   = $launchMs -ge 0 -and $launchMs -lt 5000
    key      = $keyState -eq 'ok'
    restart  = $restartState -eq 'ok'
    refusal  = $refusalState -eq 'ok'
    claude   = $claudeVersion -eq '2.1.280'
    claudeAuth = $claudeAuth -eq 'logged-out'
}
if ($Containment) { $graded['containment'] = $containmentState -eq 'ok' }
Write-Evidence 'graded.txt' (($graded.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join "`n")
$failed = @($graded.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object { $_.Name })
if ($failed.Count -gt 0) {
    Exit-Harness 1 ('observations not ok: ' + ($failed -join ','))
}
Exit-Harness 0 ''
